using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>Application service behind the REST API: calendar read model, transactional submissions,
/// the admin review queue, blackouts, group permissions and policies.</summary>
public class ReservationService
{
    private readonly InMemoryDataStore _store;
    private readonly ReservationRulesEngine _rules;
    private readonly FacilityClock _clock;
    private readonly NotificationService _notifications;
    private const int BlackoutIdBase = 900_000;

    public ReservationService(InMemoryDataStore store, ReservationRulesEngine rules, FacilityClock clock, NotificationService notifications)
    {
        _store = store;
        _rules = rules;
        _clock = clock;
        _notifications = notifications;
    }

    public CorporateGroup? GroupOf(AppUser user) => _store.FindGroup(user.GroupId);

    // =================================================================================================
    // Calendar read model
    // =================================================================================================
    /// <param name="actor">null = anonymous public calendar.</param>
    public List<ScheduleEventDto> GetEvents(AppUser? actor)
    {
        bool isAdmin = actor?.Role == Roles.Admin;
        var result = new List<ScheduleEventDto>();
        lock (_store.Sync)
        {
            foreach (var r in _store.Reservations.Where(r => r.IsActive))
            {
                bool own = actor is not null && r.GroupId is not null && r.GroupId == actor.GroupId;
                if (isAdmin || own)
                    result.Add(ToDto(r, own));
                else
                    result.Add(new ScheduleEventDto
                    {
                        // Other organizations' bookings: the slot is shown as taken, nothing else is disclosed.
                        Id = r.Id, Subject = "Reserved", StartTime = r.StartTime, EndTime = r.EndTime, IsAllDay = r.IsAllDay,
                        RecurrenceRule = r.RecurrenceRule, RecurrenceID = r.RecurrenceID, RecurrenceException = r.RecurrenceException,
                        FacilityId = r.FacilityId, RangeId = r.RangeId, Kind = "masked", IsReadonly = true
                    });
            }

            foreach (var b in _store.Blackouts)
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
                        Description = isAdmin ? b.Reason : null
                    });
                }
            }
        }
        return result;
    }

    private static ScheduleEventDto ToDto(Reservation r, bool own) => new()
    {
        Id = r.Id, Subject = r.Subject, StartTime = r.StartTime, EndTime = r.EndTime, IsAllDay = r.IsAllDay,
        RecurrenceRule = r.RecurrenceRule, RecurrenceID = r.RecurrenceID, RecurrenceException = r.RecurrenceException,
        FacilityId = r.FacilityId, RangeId = r.RangeId, Kind = "reservation", IsOwn = own,
        Status = r.Status.ToString(), RequestId = r.RequestId, GroupId = r.GroupId, GroupName = r.GroupName,
        Tier = r.Tier is { } t ? TierNames.Display(t) : null, SubmittedByName = r.SubmittedByName,
        Headcount = r.Headcount, FirearmTypes = r.FirearmTypes, TargetType = r.TargetType,
        TargetDistanceYards = r.TargetDistanceYards, TargetCount = r.TargetCount, RsoRequested = r.RsoRequested,
        Description = r.Description, AdminOverride = r.AdminOverride
    };

    // =================================================================================================
    // UC-06 / UC-07 / UC-08 / UC-09 / UC-10 - submissions (one call = one all-or-nothing transaction)
    // =================================================================================================
    public BatchResult ApplyBatch(AppUser actor, ReservationBatchRequest req)
    {
        var result = new BatchResult { DryRun = req.DryRun };
        bool isAdmin = actor.Role == Roles.Admin;
        bool tierOverride = isAdmin && req.AdminOverride;
        if (req.AdminOverride && !isAdmin) result.Errors.Add("Only administrators can override booking rules.");
        var notifyApproved = new List<Reservation>();

        lock (_store.Sync)
        {
            var now = _clock.Now;
            var working = _store.Reservations.Select(x => x.Clone()).ToList();
            var toValidate = new List<(Reservation Item, bool IsNew)>();

            // ---------- Cancellations ----------
            foreach (var d in req.Deleted)
            {
                var existing = working.FirstOrDefault(x => x.Id == d.Id);
                if (existing is null || !existing.IsActive) { result.Warnings.Add($"Reservation {d.Id} was already cancelled."); continue; }
                if (!CanModify(actor, existing, result)) continue;
                if (!isAdmin)
                {
                    var next = RecurrenceExpander.Expand(existing.StartTime, existing.EndTime, existing.EffectiveRule,
                        existing.EffectiveExceptions, now, now.AddYears(3), 1).FirstOrDefault();
                    CheckCutoff(existing, next == default ? existing.StartTime : next.Start, now, result);
                }
                foreach (var x in working.Where(x => x.Id == existing.Id || x.RecurrenceID == existing.Id))
                    x.Status = ReservationStatus.Cancelled;
            }

            // ---------- Changes (editor, drag & drop, occurrence exceptions) ----------
            foreach (var c in req.Changed)
            {
                var existing = working.FirstOrDefault(x => x.Id == c.Id);
                if (existing is null || !existing.IsActive) { result.Errors.Add($"Reservation {c.Id} no longer exists - please refresh."); continue; }
                if (!CanModify(actor, existing, result)) continue;

                bool schedulingChanged = existing.StartTime != c.StartTime || existing.EndTime != c.EndTime
                    || existing.RangeId != c.RangeId || existing.IsAllDay != c.IsAllDay
                    || !string.Equals(Norm(existing.RecurrenceRule), Norm(c.RecurrenceRule), StringComparison.OrdinalIgnoreCase)
                    || existing.Headcount != (c.Headcount ?? existing.Headcount)
                    || !existing.FirearmTypes.OrderBy(x => x).SequenceEqual((c.FirearmTypes ?? existing.FirearmTypes).OrderBy(x => x))
                    || !string.Equals(existing.TargetType, c.TargetType, StringComparison.OrdinalIgnoreCase);

                if (!isAdmin && existing.Status == ReservationStatus.Approved)
                {
                    // Cancelling single occurrences of an approved series honours the cutoff too.
                    var newExceptions = RecurrenceExpander.ParseExceptions(c.RecurrenceException)
                        .Except(RecurrenceExpander.ParseExceptions(existing.RecurrenceException));
                    foreach (var ex in newExceptions) CheckCutoff(existing, ex, now, result);
                    if (schedulingChanged) CheckCutoff(existing, existing.StartTime, now, result);
                }

                Apply(existing, c);
                if (schedulingChanged)
                {
                    if (!isAdmin && existing.Status == ReservationStatus.Approved)
                    {
                        existing.Status = ReservationStatus.Pending;   // changes by the group need re-approval
                        existing.ReviewedBy = null; existing.ReviewedAt = null;
                        result.Warnings.Add("The change was sent back to the admin review queue for re-approval.");
                    }
                    if (isAdmin) existing.AdminOverride = tierOverride;
                    toValidate.Add((existing, false));
                }
            }

            // ---------- New reservations ----------
            var created = new List<Reservation>();
            string requestId = _store.NextRequestId();
            foreach (var a in req.Added)
            {
                Reservation r;
                if (a.RecurrenceID is { } parentId)
                {
                    // Edited single occurrence of a series -> exception record that belongs to the series' group & request.
                    var parent = working.FirstOrDefault(x => x.Id == parentId);
                    if (parent is null || !parent.IsActive) { result.Errors.Add("The recurring series for this occurrence no longer exists - please refresh."); continue; }
                    if (!CanModify(actor, parent, result)) continue;
                    r = parent.Clone();
                    r.Id = _store.NextReservationId();
                    r.RecurrenceID = parentId;
                    r.Status = isAdmin ? parent.Status : ReservationStatus.Pending;
                    if (r.Status == ReservationStatus.Pending && !isAdmin)
                        result.Warnings.Add("The changed occurrence was sent to the admin review queue.");
                }
                else
                {
                    var group = isAdmin ? _store.FindGroup(a.GroupId) : GroupOf(actor);
                    if (!isAdmin && group is null) { result.Errors.Add("Your account is not linked to a corporate group."); continue; }
                    r = new Reservation
                    {
                        Id = _store.NextReservationId(), GroupId = group?.Id, GroupName = group?.Name ?? "Facility hold (AZGFD)",
                        Tier = group?.Tier, SubmittedById = actor.Id, SubmittedByName = actor.Name, CreatedAt = now,
                        RequestId = requestId, AdminOverride = tierOverride,
                        // Corporate submissions wait for review; admin-entered bookings are approved immediately.
                        Status = isAdmin ? ReservationStatus.Approved : ReservationStatus.Pending,
                        ReviewedBy = isAdmin ? actor.Name : null, ReviewedAt = isAdmin ? now : null
                    };
                }
                Apply(r, a);
                working.Add(r);
                created.Add(r);
                toValidate.Add((r, true));
            }

            // ---------- UC-07 / UC-08: multi-date cart rules ----------
            var newBookings = created.Where(x => x.RecurrenceID is null).ToList();
            if (newBookings.Select(x => x.RangeId).Distinct().Count() > 1)
                result.Errors.Add(ReservationRulesEngine.SingleRangeMessage);
            if (!tierOverride && newBookings.Count > 1 && newBookings[0].Tier is { } t && newBookings.Count > _store.Policy(t).MaxDatesPerTransaction)
                result.Errors.Add($"{TierNames.Display(t)} groups can request up to {_store.Policy(t).MaxDatesPerTransaction} dates per booking (requested {newBookings.Count}).");

            // ---------- Rule validation ----------
            int occurrenceCount = 0;
            foreach (var (item, isNew) in toValidate)
            {
                var violations = _rules.Validate(item, _store.FindGroup(item.GroupId), working, now, tierOverride, isNew, result.Warnings, ref occurrenceCount);
                result.Errors.AddRange(violations.Select(x => x.Message));
            }
            result.OccurrencesValidated = occurrenceCount;
            result.Errors = result.Errors.Distinct().ToList();
            result.Warnings = result.Warnings.Distinct().ToList();
            if (newBookings.Count > 0) { result.RequestId = requestId; result.Status = newBookings[0].Status.ToString(); }
            if (!result.Success || req.DryRun) return result;

            // ---------- Commit ----------
            _store.Reservations.Clear();
            _store.Reservations.AddRange(working);
            result.SavedIds = created.Select(x => x.Id).Concat(req.Changed.Select(x => x.Id ?? 0)).ToList();
            if (isAdmin && newBookings.Count > 0 && newBookings[0].GroupId is not null) notifyApproved = newBookings;
        }

        if (notifyApproved.Count > 0) _notifications.SendDecision(notifyApproved, approved: true);
        return result;
    }

    private static string Norm(string? rule) => (rule ?? "").Trim().TrimEnd(';');

    private static bool CanModify(AppUser actor, Reservation r, BatchResult result)
    {
        if (actor.Role == Roles.Admin || (r.GroupId is not null && r.GroupId == actor.GroupId)) return true;
        result.Errors.Add("You can only change reservations that belong to your group.");
        return false;
    }

    private void CheckCutoff(Reservation r, DateTime occurrenceStart, DateTime now, BatchResult result)
    {
        if (r.Tier is not { } tier || r.Status != ReservationStatus.Approved) return;
        var cutoff = _store.Policy(tier).CancellationCutoffHours;
        var label = ReservationRulesEngine.Label(_store.FindRange(r.RangeId), occurrenceStart);
        if (occurrenceStart < now) result.Errors.Add($"{label}: past reservations cannot be changed.");
        else if (occurrenceStart < now.AddHours(cutoff))
            result.Errors.Add($"{label}: approved reservations can be changed or cancelled up to {cutoff} hour(s) before start - please contact the range office.");
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
        target.Headcount = src.Headcount ?? 1;
        target.FirearmTypes = (src.FirearmTypes ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct().ToList();
        target.TargetType = src.TargetType;
        target.TargetDistanceYards = src.TargetDistanceYards;
        target.TargetCount = src.TargetCount;
        target.RsoRequested = src.RsoRequested;
        target.Description = src.Description;
    }

    // =================================================================================================
    // UC-12 - review queue and dashboard
    // =================================================================================================
    public List<RequestSummary> GetRequests(AppUser actor, ReservationStatus? status = null)
    {
        lock (_store.Sync)
        {
            var now = _clock.Now;
            var query = _store.Reservations.Where(r => r.RecurrenceID is null);
            if (actor.Role != Roles.Admin) query = query.Where(r => r.GroupId == actor.GroupId);
            if (status is not null) query = query.Where(r => r.Status == status);

            return query.GroupBy(r => (r.RequestId, r.Status))
                .Select(g => Summarize(g.OrderBy(x => x.StartTime).ToList(), actor, now))
                .OrderBy(s => s.Status == nameof(ReservationStatus.Pending) ? 0 : 1)
                .ThenByDescending(s => s.CreatedAt)
                .ToList();
        }
    }

    private RequestSummary Summarize(List<Reservation> items, AppUser actor, DateTime now)
    {
        var first = items[0];
        var range = _store.FindRange(first.RangeId);
        var s = new RequestSummary
        {
            RequestId = first.RequestId, Status = first.Status.ToString(), GroupId = first.GroupId, GroupName = first.GroupName,
            Tier = first.Tier is { } t ? TierNames.Display(t) : "-", SubmittedByName = first.SubmittedByName, CreatedAt = first.CreatedAt,
            RangeName = range?.Name ?? "?", FacilityName = range is null ? "" : _store.FindFacility(range.FacilityId)?.Name ?? "",
            Subject = first.Subject, Headcount = first.Headcount, FirearmTypes = first.FirearmTypes,
            Target = $"{first.TargetType}{(first.TargetDistanceYards is { } d ? $" @ {d} yd" : "")}{(first.TargetCount is { } c ? $" × {c}" : "")}",
            RsoRequested = first.RsoRequested, RecurrenceRule = first.RecurrenceRule, DenialReason = first.DenialReason,
            ReviewedBy = first.ReviewedBy, ReviewedAt = first.ReviewedAt,
            CanCancel = first.IsActive && first.StartTime > now
        };

        if (!string.IsNullOrEmpty(first.RecurrenceRule))
        {
            var occ = RecurrenceExpander.Expand(first.StartTime, first.EndTime, first.RecurrenceRule, first.RecurrenceException,
                DateTime.MinValue, ReservationRulesEngine.RecurrenceCap(now).AddYears(1), 500).ToList();
            s.Kind = $"Recurring series ({occ.Count} dates)";
            s.Dates = occ.Take(6).Select(o => $"{o.Start:ddd MMM d, yyyy h:mm tt} - {o.End:h:mm tt}").ToList();
            if (occ.Count > 6) s.Dates.Add($"… through {occ[^1].Start:MMM d, yyyy}");
        }
        else
        {
            s.Kind = items.Count > 1 ? $"Multi-date ({items.Count} dates)" : "Single slot";
            s.Dates = items.Select(o => $"{o.StartTime:ddd MMM d, yyyy h:mm tt} - {o.EndTime:h:mm tt}").ToList();
        }

        // Policy compliance for the admin (pending requests are re-validated against the live calendar).
        if (first.Status == ReservationStatus.Pending)
        {
            var group = _store.FindGroup(first.GroupId);
            var violations = new List<RuleViolation>();
            int n = 0;
            foreach (var r in items)
                violations.AddRange(_rules.Validate(r.Clone(), group, _store.Reservations, now, r.AdminOverride, false, null, ref n));
            s.Checks = ReservationRulesEngine.Checks.All
                .Select(c => new ComplianceCheck(c, violations.All(v => v.Check != c), violations.Where(v => v.Check == c).Select(v => v.Message).Distinct().ToList()))
                .ToList();
        }
        return s;
    }

    public (bool Ok, List<string> Errors) Approve(string requestId, AppUser admin)
    {
        List<Reservation> items;
        lock (_store.Sync)
        {
            items = _store.Reservations.Where(r => r.RequestId == requestId && r.Status == ReservationStatus.Pending).ToList();
            if (items.Count == 0) return (false, new() { "Nothing pending for this request." });

            // Re-check the hard rules against the live calendar - something may have changed since submission.
            var now = _clock.Now;
            var group = _store.FindGroup(items[0].GroupId);
            var blocking = new List<string>();
            int n = 0;
            foreach (var r in items)
                blocking.AddRange(_rules.Validate(r.Clone(), group, _store.Reservations, now, true, false, null, ref n)
                    .Where(v => ReservationRulesEngine.Checks.Blocking.Contains(v.Check)).Select(v => v.Message));
            if (blocking.Count > 0) return (false, blocking.Distinct().ToList());

            foreach (var r in items) { r.Status = ReservationStatus.Approved; r.ReviewedBy = admin.Name; r.ReviewedAt = now; r.DenialReason = null; }
        }
        _notifications.SendDecision(items, approved: true);
        return (true, new());
    }

    public (bool Ok, List<string> Errors) Deny(string requestId, string reason, AppUser admin)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return (false, new() { "A reason for denial is required (at least 5 characters)." });
        List<Reservation> items;
        lock (_store.Sync)
        {
            items = _store.Reservations.Where(r => r.RequestId == requestId && r.Status == ReservationStatus.Pending).ToList();
            if (items.Count == 0) return (false, new() { "Nothing pending for this request." });
            var now = _clock.Now;
            foreach (var r in items) { r.Status = ReservationStatus.Denied; r.DenialReason = reason.Trim(); r.ReviewedBy = admin.Name; r.ReviewedAt = now; }
        }
        _notifications.SendDecision(items, approved: false);
        return (true, new());
    }

    public (bool Ok, List<string> Errors) CancelRequest(string requestId, AppUser actor)
    {
        lock (_store.Sync)
        {
            var items = _store.Reservations.Where(r => r.RequestId == requestId && r.IsActive).ToList();
            if (items.Count == 0) return (false, new() { "Nothing to cancel." });
            var probe = new BatchResult();
            if (!CanModify(actor, items[0], probe)) return (false, probe.Errors);
            if (actor.Role != Roles.Admin)
                foreach (var r in items) CheckCutoff(r, r.StartTime, _clock.Now, probe);
            if (probe.Errors.Count > 0) return (false, probe.Errors.Distinct().ToList());
            foreach (var r in items) r.Status = ReservationStatus.Cancelled;
            return (true, new());
        }
    }

    public List<NotificationRecord> GetNotifications(AppUser actor)
    {
        lock (_store.Sync)
            return _store.Notifications.Where(n => actor.Role == Roles.Admin || n.GroupId == actor.GroupId).Take(50).ToList();
    }

    // =================================================================================================
    // UC-13 - blackouts
    // =================================================================================================
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

            // Existing reservations that now fall inside the blackout, so the admin can follow up.
            var now = _clock.Now;
            var impacted = new List<string>();
            foreach (var res in _store.Reservations.Where(x => x.IsActive))
            {
                var range = _store.FindRange(res.RangeId);
                if (range is null || !ReservationRulesEngine.AppliesTo(b, range)) continue;
                var occ = RecurrenceExpander.Expand(res.StartTime, res.EndTime, res.EffectiveRule, res.EffectiveExceptions, now, now.AddDays(800)).ToList();
                if (occ.Count == 0) continue;
                var bo = RecurrenceExpander.Expand(b.StartTime, b.EndTime, b.RecurrenceRule, null, occ[0].Start, occ[^1].End).ToList();
                var hit = occ.FirstOrDefault(o => bo.Any(x => o.Start < x.End && o.End > x.Start));
                if (hit != default) impacted.Add($"{ReservationRulesEngine.Label(range, hit.Start)} - {res.GroupName} ({res.RequestId}, {res.Status})");
            }
            return (b, errors, impacted);
        }
    }

    public bool DeleteBlackout(int id) { lock (_store.Sync) return _store.Blackouts.RemoveAll(b => b.Id == id) > 0; }

    // =================================================================================================
    // UC-14 - group range permissions, policies & facility settings
    // =================================================================================================
    public (bool Ok, List<string> Errors) UpdateGroup(int groupId, GroupUpdateInput input, AppUser admin)
    {
        lock (_store.Sync)
        {
            var g = _store.FindGroup(groupId);
            if (g is null) return (false, new() { "Unknown group." });
            g.Tier = input.Tier;
            foreach (var p in input.RangePermissions)
            {
                var range = _store.FindRange(p.RangeId);
                if (range is null || !range.IsSpecialized) continue;
                var existing = g.RangePermissions.FirstOrDefault(x => x.RangeId == p.RangeId);
                if (p.Granted && existing is null)
                {
                    if (string.IsNullOrWhiteSpace(p.VerificationNote))
                        return (false, new() { $"{range.Name}: record how the orientation / qualification was verified before granting access." });
                    g.RangePermissions.Add(new RangePermission { RangeId = p.RangeId, GrantedOn = _clock.Now, GrantedBy = admin.Name, VerificationNote = p.VerificationNote.Trim() });
                }
                else if (p.Granted && existing is not null && !string.IsNullOrWhiteSpace(p.VerificationNote))
                    existing.VerificationNote = p.VerificationNote.Trim();
                else if (!p.Granted && existing is not null)
                    g.RangePermissions.Remove(existing);
            }
            // Keep denormalized tier on the group's reservations in sync.
            foreach (var r in _store.Reservations.Where(r => r.GroupId == g.Id)) r.Tier = g.Tier;
            return (true, new());
        }
    }

    public List<string> UpdateSettings(SettingsInput input)
    {
        var errors = new List<string>();
        lock (_store.Sync)
        {
            foreach (var p in input.Policies)
            {
                var existing = _store.Policies.FirstOrDefault(x => x.Tier == p.Tier);
                if (existing is null) { errors.Add($"Unknown tier {p.Tier}."); continue; }
                if (p.AdvanceBookingDays < 0 || p.MaxHoursPerBooking < 1 || p.MaxDatesPerTransaction < 1)
                { errors.Add($"{existing.DisplayName}: values must be positive."); continue; }
                existing.AdvanceBookingDays = p.AdvanceBookingDays;
                existing.MinLeadHours = p.MinLeadHours;
                existing.MaxHoursPerBooking = p.MaxHoursPerBooking;
                existing.MaxDatesPerTransaction = p.MaxDatesPerTransaction;
                existing.AllowRecurring = p.AllowRecurring;
                existing.CancellationCutoffHours = p.CancellationCutoffHours;
            }
            _store.Settings.MondayExclusiveForTier4 = input.Settings.MondayExclusiveForTier4;
            _store.Settings.AllowSameGroupOverlap = input.Settings.AllowSameGroupOverlap;
        }
        return errors;
    }
}
