using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>Application service behind the REST API: read model for the Scheduler + transactional writes.</summary>
public class ReservationService
{
    private readonly InMemoryDataStore _store;
    private readonly ReservationRulesEngine _rules;
    private readonly FacilityClock _clock;
    private const int BlackoutIdBase = 900_000;

    public ReservationService(InMemoryDataStore store, ReservationRulesEngine rules, FacilityClock clock)
    {
        _store = store;
        _rules = rules;
        _clock = clock;
    }

    // -------------------------------------------------------------------------------------------------
    // Read model
    // -------------------------------------------------------------------------------------------------
    public List<ScheduleEventDto> GetEvents(AppUser actor, DateTime? from, DateTime? to)
    {
        bool isAdmin = actor.Role == Roles.Admin;
        var result = new List<ScheduleEventDto>();

        lock (_store.Sync)
        {
            foreach (var r in _store.Reservations.Where(r => InWindow(r.StartTime, r.EndTime, r.EffectiveRule, r.EffectiveExceptions, from, to)))
            {
                bool own = r.CustomerId == actor.Id;
                if (isAdmin || own)
                {
                    result.Add(new ScheduleEventDto
                    {
                        Id = r.Id, Subject = r.Subject, StartTime = r.StartTime, EndTime = r.EndTime, IsAllDay = r.IsAllDay,
                        RecurrenceRule = r.RecurrenceRule, RecurrenceID = r.RecurrenceID, RecurrenceException = r.RecurrenceException,
                        FacilityId = r.FacilityId, RangeId = r.RangeId, Kind = "reservation", IsOwn = own,
                        CustomerId = r.CustomerId, CustomerName = r.CustomerName, Tier = r.Tier,
                        FirearmType = r.FirearmType, Caliber = r.Caliber, Participants = r.Participants,
                        QualificationId = r.QualificationId, RsoRequested = r.RsoRequested, Description = r.Description,
                        BookingGroupId = r.BookingGroupId, AdminOverride = r.AdminOverride
                    });
                }
                else
                {
                    // Privacy: public users only see that the slot is taken.
                    result.Add(new ScheduleEventDto
                    {
                        Id = r.Id, Subject = "Reserved", StartTime = r.StartTime, EndTime = r.EndTime, IsAllDay = r.IsAllDay,
                        RecurrenceRule = r.RecurrenceRule, RecurrenceID = r.RecurrenceID, RecurrenceException = r.RecurrenceException,
                        FacilityId = r.FacilityId, RangeId = r.RangeId, Kind = "masked", IsReadonly = true
                    });
                }
            }

            foreach (var b in _store.Blackouts.Where(b => InWindow(b.StartTime, b.EndTime, b.RecurrenceRule, b.RecurrenceException, from, to)))
            {
                // A Syncfusion block event belongs to one resource, so facility-wide / global blackouts fan out per range.
                foreach (var (range, index) in _store.Ranges.Select((x, i) => (x, i)).Where(t => ReservationRulesEngine.AppliesTo(b, t.x)))
                {
                    result.Add(new ScheduleEventDto
                    {
                        Id = BlackoutIdBase + b.Id * 100 + index, Subject = $"Blackout: {b.Title}",
                        StartTime = b.StartTime, EndTime = b.EndTime, IsAllDay = b.IsAllDay,
                        RecurrenceRule = b.RecurrenceRule, RecurrenceException = b.RecurrenceException,
                        FacilityId = range.FacilityId, RangeId = range.Id, IsBlock = true, Kind = "blackout",
                        Description = b.Reason
                    });
                }
            }
        }
        return result;
    }

    private static bool InWindow(DateTime start, DateTime end, string? rule, string? ex, DateTime? from, DateTime? to)
    {
        if (from is null || to is null) return true;
        if (string.IsNullOrWhiteSpace(rule)) return start < to && end > from;
        if (start >= to) return false;
        return RecurrenceExpander.Expand(start, end, rule, ex, from.Value, to.Value, 1).Any();
    }

    // -------------------------------------------------------------------------------------------------
    // Transactional write: one call = one all-or-nothing transaction (multi-slot / multi-date bookings,
    // recurring series, drag & drop, occurrence edits, cancellations).
    // -------------------------------------------------------------------------------------------------
    public BatchResult ApplyBatch(AppUser actor, ReservationBatchRequest req)
    {
        var result = new BatchResult { DryRun = req.DryRun };
        bool isAdmin = actor.Role == Roles.Admin;
        bool tierOverride = isAdmin && req.AdminOverride;
        if (req.AdminOverride && !isAdmin) result.Errors.Add("Only administrators can override booking rules.");

        lock (_store.Sync)
        {
            var now = _clock.Now;
            var working = _store.Reservations.Select(Clone).ToList();
            var toValidate = new List<(Reservation Item, AppUser Owner, bool IsNew)>();

            // ---------- Deletions ----------
            foreach (var d in req.Deleted)
            {
                var existing = working.FirstOrDefault(x => x.Id == d.Id);
                if (existing is null) { result.Warnings.Add($"Reservation {d.Id} was already removed."); continue; }
                if (!CanModify(actor, existing, result)) continue;

                if (!isAdmin)
                {
                    var next = RecurrenceExpander.Expand(existing.StartTime, existing.EndTime, existing.EffectiveRule,
                        existing.EffectiveExceptions, now, now + ReservationRulesEngine.ValidationHorizon, 1).FirstOrDefault();
                    CheckCutoff(actor, existing, next == default ? existing.StartTime : next.Start, now, result);
                }
                working.RemoveAll(x => x.Id == existing.Id || x.RecurrenceID == existing.Id);
            }

            // ---------- Changes ----------
            foreach (var c in req.Changed)
            {
                var existing = working.FirstOrDefault(x => x.Id == c.Id);
                if (existing is null) { result.Errors.Add($"Reservation {c.Id} no longer exists - please refresh."); continue; }
                if (!CanModify(actor, existing, result)) continue;

                bool schedulingChanged = existing.StartTime != c.StartTime || existing.EndTime != c.EndTime
                    || existing.RangeId != c.RangeId || existing.IsAllDay != c.IsAllDay
                    || !string.Equals(Norm(existing.RecurrenceRule), Norm(c.RecurrenceRule), StringComparison.OrdinalIgnoreCase)
                    || existing.Participants != (c.Participants ?? existing.Participants)
                    || !string.Equals(existing.FirearmType, c.FirearmType, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.QualificationId ?? "", c.QualificationId ?? "", StringComparison.OrdinalIgnoreCase);

                if (!isAdmin)
                {
                    // Cancelling individual occurrences of a series (new exception dates) honours the cutoff too.
                    var newExceptions = RecurrenceExpander.ParseExceptions(c.RecurrenceException)
                        .Except(RecurrenceExpander.ParseExceptions(existing.RecurrenceException));
                    foreach (var ex in newExceptions) CheckCutoff(actor, existing, ex, now, result);
                    if (schedulingChanged && existing.StartTime > now) CheckCutoff(actor, existing, existing.StartTime, now, result);
                }

                Apply(existing, c);
                if (isAdmin && !string.IsNullOrEmpty(c.CustomerId) && c.CustomerId != existing.CustomerId)
                {
                    var newOwner = _store.FindUser(c.CustomerId);
                    if (newOwner is null) result.Errors.Add($"Unknown customer '{c.CustomerId}'.");
                    else { existing.CustomerId = newOwner.Id; existing.CustomerName = newOwner.Name; existing.Tier = newOwner.Tier; schedulingChanged = true; }
                }
                if (isAdmin) existing.AdminOverride = tierOverride;

                if (schedulingChanged)
                    toValidate.Add((existing, _store.FindUser(existing.CustomerId) ?? actor, false));
            }

            // ---------- Additions ----------
            var created = new List<Reservation>();
            foreach (var a in req.Added)
            {
                AppUser owner = actor;
                if (a.RecurrenceID is { } parentId)
                {
                    // Edited single occurrence of a series -> exception record owned by the series owner.
                    var parent = working.FirstOrDefault(x => x.Id == parentId);
                    if (parent is null) { result.Errors.Add("The recurring series for this occurrence no longer exists - please refresh."); continue; }
                    if (!CanModify(actor, parent, result)) continue;
                    owner = _store.FindUser(parent.CustomerId) ?? actor;
                }
                else if (isAdmin && !string.IsNullOrEmpty(a.CustomerId))
                {
                    owner = _store.FindUser(a.CustomerId) ?? actor;
                }

                var r = new Reservation
                {
                    Id = _store.NextReservationId(), CustomerId = owner.Id, CustomerName = owner.Name, Tier = owner.Tier,
                    CreatedAt = now, CreatedBy = actor.Id, AdminOverride = tierOverride, RecurrenceID = a.RecurrenceID
                };
                Apply(r, a);
                working.Add(r);
                created.Add(r);
                toValidate.Add((r, owner, true));
            }

            // ---------- Transaction size limits (multi-slot / multi-date) ----------
            var newBookings = created.Where(x => x.RecurrenceID is null).ToList();
            if (!tierOverride && newBookings.Count > 1)
            {
                foreach (var g in newBookings.GroupBy(x => x.CustomerId))
                {
                    var owner = _store.FindUser(g.Key);
                    if (owner?.Tier is { } tier && g.Count() > _store.Policy(tier).MaxSlotsPerTransaction)
                        result.Errors.Add($"{tier} tier can reserve up to {_store.Policy(tier).MaxSlotsPerTransaction} slots per transaction (requested {g.Count()}).");
                }
                foreach (var g in newBookings.GroupBy(x => x.RangeId))
                {
                    var range = _store.FindRange(g.Key);
                    if (range is not null && g.Count() > range.MaxSlotsPerTransaction)
                        result.Errors.Add($"{range.Name} allows up to {range.MaxSlotsPerTransaction} slots per transaction (requested {g.Count()}).");
                }
            }

            // ---------- Per-reservation validation ----------
            int occurrenceCount = 0;
            foreach (var (item, owner, isNew) in toValidate)
                result.Errors.AddRange(_rules.Validate(item, owner, working, now, tierOverride, isNew, ref occurrenceCount));
            result.OccurrencesValidated = occurrenceCount;

            // ---------- Per-customer aggregate caps ----------
            if (!tierOverride)
            {
                foreach (var owner in toValidate.Select(t => t.Owner).DistinctBy(o => o.Id))
                    result.Errors.AddRange(_rules.ValidateAggregates(owner, working, now));
            }

            result.Errors = result.Errors.Distinct().ToList();
            if (!result.Success || req.DryRun) return result;

            // ---------- Commit (all-or-nothing) ----------
            if (newBookings.Count > 1)
            {
                var groupId = $"TX-{now:yyyyMMddHHmmss}-{newBookings[0].Id}";
                newBookings.ForEach(x => x.BookingGroupId = groupId);
                result.BookingGroupId = groupId;
            }
            _store.Reservations.Clear();
            _store.Reservations.AddRange(working);
            result.SavedIds = created.Select(x => x.Id).Concat(req.Changed.Select(x => x.Id ?? 0)).ToList();
            return result;
        }
    }

    private static string Norm(string? rule) => (rule ?? "").Trim().TrimEnd(';');

    private bool CanModify(AppUser actor, Reservation r, BatchResult result)
    {
        if (actor.Role == Roles.Admin || r.CustomerId == actor.Id) return true;
        result.Errors.Add("You can only change your own reservations.");
        return false;
    }

    private void CheckCutoff(AppUser actor, Reservation r, DateTime occurrenceStart, DateTime now, BatchResult result)
    {
        if (actor.Tier is not { } tier) return;
        var cutoff = _store.Policy(tier).CancellationCutoffHours;
        if (occurrenceStart < now) result.Errors.Add($"{ReservationRulesEngine.Label(_store.FindRange(r.RangeId), occurrenceStart)}: past reservations cannot be changed.");
        else if (occurrenceStart < now.AddHours(cutoff))
            result.Errors.Add($"{ReservationRulesEngine.Label(_store.FindRange(r.RangeId), occurrenceStart)}: {tier} tier changes/cancellations must be made at least {cutoff} hour(s) before start - please contact the range office.");
    }

    private static void Apply(Reservation target, ReservationInput src)
    {
        target.Subject = string.IsNullOrWhiteSpace(src.Subject) ? "Range reservation" : src.Subject.Trim();
        target.StartTime = src.StartTime;
        target.EndTime = src.EndTime;
        target.IsAllDay = src.IsAllDay;
        target.RecurrenceRule = string.IsNullOrWhiteSpace(src.RecurrenceRule) ? null : src.RecurrenceRule;
        target.RecurrenceException = string.IsNullOrWhiteSpace(src.RecurrenceException) ? null : src.RecurrenceException;
        target.RangeId = src.RangeId;
        target.FirearmType = src.FirearmType;
        target.Caliber = src.Caliber;
        target.Participants = src.Participants ?? 1;
        target.QualificationId = string.IsNullOrWhiteSpace(src.QualificationId) ? null : src.QualificationId.Trim();
        target.RsoRequested = src.RsoRequested;
        target.Description = src.Description;
    }

    private static Reservation Clone(Reservation r) => r.Clone();

    // -------------------------------------------------------------------------------------------------
    // Blackouts & policies (admin)
    // -------------------------------------------------------------------------------------------------
    public List<Blackout> GetBlackouts() { lock (_store.Sync) return _store.Blackouts.ToList(); }

    public (Blackout? Saved, List<string> Errors, List<string> Impacted) SaveBlackout(BlackoutInput input)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(input.Title)) errors.Add("Title is required.");
        if (input.EndTime <= input.StartTime) errors.Add("End must be after start.");
        if (input.RangeId is { } rid && _store.FindRange(rid) is null) errors.Add("Unknown range.");
        if (!string.IsNullOrWhiteSpace(input.RecurrenceRule))
        {
            try { RecurrenceExpander.Parse(input.RecurrenceRule); } catch { errors.Add("Invalid repeat rule."); }
        }
        if (errors.Count > 0) return (null, errors, new());

        lock (_store.Sync)
        {
            var b = input.Id is { } id ? _store.Blackouts.FirstOrDefault(x => x.Id == id) : null;
            if (b is null) { b = new Blackout { Id = _store.NextBlackoutId() }; _store.Blackouts.Add(b); }
            b.Title = input.Title.Trim();
            b.StartTime = input.StartTime;
            b.EndTime = input.EndTime;
            b.IsAllDay = input.IsAllDay;
            b.RecurrenceRule = string.IsNullOrWhiteSpace(input.RecurrenceRule) ? null : input.RecurrenceRule;
            b.RangeId = input.RangeId;
            b.FacilityId = input.RangeId is { } r ? _store.FindRange(r)!.FacilityId : input.FacilityId;
            b.Reason = input.Reason;

            // Report existing reservations that now fall inside the blackout so the admin can follow up.
            var now = _clock.Now;
            var impacted = new List<string>();
            foreach (var res in _store.Reservations)
            {
                var range = _store.FindRange(res.RangeId);
                if (range is null || !ReservationRulesEngine.AppliesTo(b, range)) continue;
                var occ = RecurrenceExpander.Expand(res.StartTime, res.EndTime, res.EffectiveRule, res.EffectiveExceptions, now, now.AddDays(400)).ToList();
                if (occ.Count == 0) continue;
                var bo = RecurrenceExpander.Expand(b.StartTime, b.EndTime, b.RecurrenceRule, null, occ[0].Start, occ[^1].End).ToList();
                var hit = occ.FirstOrDefault(o => bo.Any(x => o.Start < x.End && o.End > x.Start));
                if (hit != default) impacted.Add($"{ReservationRulesEngine.Label(range, hit.Start)} - {res.CustomerName} ({res.Subject})");
            }
            return (b, errors, impacted);
        }
    }

    public bool DeleteBlackout(int id) { lock (_store.Sync) return _store.Blackouts.RemoveAll(b => b.Id == id) > 0; }

    public List<TierPolicy> GetPolicies() { lock (_store.Sync) return _store.Policies.ToList(); }

    public List<string> UpdatePolicies(List<TierPolicy> policies)
    {
        var errors = new List<string>();
        lock (_store.Sync)
        {
            foreach (var p in policies)
            {
                var existing = _store.Policies.FirstOrDefault(x => x.Tier == p.Tier);
                if (existing is null) { errors.Add($"Unknown tier {p.Tier}."); continue; }
                if (p.AdvanceBookingDays < 0 || p.MaxHoursPerBooking < 1 || p.MaxBookingsPerDay < 1 || p.MaxSlotsPerTransaction < 1)
                { errors.Add($"{p.Tier}: values must be positive."); continue; }
                existing.AdvanceBookingDays = p.AdvanceBookingDays;
                existing.MinLeadHours = p.MinLeadHours;
                existing.MaxHoursPerBooking = p.MaxHoursPerBooking;
                existing.MaxBookingsPerDay = p.MaxBookingsPerDay;
                existing.MaxActiveFutureBookings = p.MaxActiveFutureBookings;
                existing.MaxHoursPerMonth = p.MaxHoursPerMonth;
                existing.AllowRecurring = p.AllowRecurring;
                existing.MaxRecurringOccurrences = p.AllowRecurring ? p.MaxRecurringOccurrences : 0;
                existing.MaxSlotsPerTransaction = p.MaxSlotsPerTransaction;
                existing.CancellationCutoffHours = p.CancellationCutoffHours;
            }
        }
        return errors;
    }
}
