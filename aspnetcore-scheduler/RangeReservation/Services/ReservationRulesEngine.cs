using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>
/// Authoritative, server-side booking rules. The Scheduler UI mirrors some of these for a better UX
/// (greyed-out cells, hidden recurrence options) but every request is re-validated here.
///
/// Rule groups:
///   1. Hard blocks (always enforced, even for admins): conflicts, blackouts, operating hours, past dates.
///   2. Range safety / compliance (always enforced): firearm type, participant capacity, qualification.
///   3. Tier policy (enforced for customers; admins may override): range eligibility, advance window,
///      lead time, duration, recurrence caps, per-day / active / monthly caps, transaction size.
/// </summary>
public class ReservationRulesEngine
{
    private readonly InMemoryDataStore _store;
    private readonly IQualificationRegistry _qualifications;
    public static readonly TimeSpan ValidationHorizon = TimeSpan.FromDays(731);

    public ReservationRulesEngine(InMemoryDataStore store, IQualificationRegistry qualifications)
    {
        _store = store;
        _qualifications = qualifications;
    }

    public static string Label(ShootingRange? range, DateTime start) =>
        $"{range?.Name ?? "Unknown range"} · {start.ToString(start.Year == DateTime.Today.Year ? "ddd MMM d, h:mm tt" : "ddd MMM d yyyy, h:mm tt")}";

    /// <summary>Validates a single reservation (all of its future occurrences) against the proposed state.</summary>
    public List<string> Validate(Reservation r, AppUser owner, List<Reservation> proposed, DateTime now,
        bool tierOverride, bool isNew, ref int occurrenceCounter)
    {
        var errors = new List<string>();
        var range = _store.FindRange(r.RangeId);
        string label = Label(range, r.StartTime);

        if (range is null) { errors.Add($"{label}: please select a valid range."); return errors; }
        r.FacilityId = range.FacilityId;

        if (r.EndTime <= r.StartTime) { errors.Add($"{label}: end time must be after start time."); return errors; }

        // ---- Recurrence shape -------------------------------------------------------------------------
        int? totalOccurrences = null;
        bool isRecurring = !string.IsNullOrWhiteSpace(r.EffectiveRule);
        if (isRecurring)
        {
            try { totalOccurrences = RecurrenceExpander.CountOccurrences(r.StartTime, r.EndTime, r.EffectiveRule, r.EffectiveExceptions); }
            catch { errors.Add($"{label}: the repeat rule could not be understood."); return errors; }
            if (totalOccurrences is null && !tierOverride)
                errors.Add($"{label}: recurring reservations must have an end (a number of occurrences or an end date).");
        }

        var occurrences = RecurrenceExpander.Expand(r.StartTime, r.EndTime, r.EffectiveRule, r.EffectiveExceptions,
            isNew ? DateTime.MinValue : now, now + ValidationHorizon, 2000).ToList();
        occurrenceCounter += occurrences.Count;

        if (occurrences.Count == 0) { errors.Add($"{label}: there are no upcoming occurrences to book."); return errors; }

        // ---- 1. Hard blocks ---------------------------------------------------------------------------
        if (occurrences[0].Start < now)
            errors.Add($"{Label(range, occurrences[0].Start)}: reservations cannot start in the past.");

        if (r.IsAllDay && owner.Role != Roles.Admin)
            errors.Add($"{label}: all-day reservations are reserved for facility administrators.");

        if (!r.IsAllDay)
        {
            foreach (var o in occurrences)
            {
                var open = o.Start.Date + range.OpenTime;
                var close = o.Start.Date + range.CloseTime;
                if (o.Start < open || o.End > close)
                {
                    errors.Add($"{Label(range, o.Start)}: outside operating hours ({Fmt(range.OpenTime)} - {Fmt(range.CloseTime)}).");
                    break;
                }
            }
        }

        errors.AddRange(CheckConflicts(r, range, occurrences, proposed));
        errors.AddRange(CheckBlackouts(range, occurrences));

        // ---- 2. Range safety / compliance -------------------------------------------------------------
        if (r.Participants < 1 || r.Participants > range.MaxParticipants)
            errors.Add($"{label}: {range.Name} allows 1-{range.MaxParticipants} participants (requested {r.Participants}).");

        if (string.IsNullOrWhiteSpace(r.FirearmType))
            errors.Add($"{label}: firearm type is required.");
        else if (!range.AllowedFirearms.Contains(r.FirearmType, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{label}: {r.FirearmType} is not permitted on {range.Name} (allowed: {string.Join(", ", range.AllowedFirearms)}).");

        if (!string.IsNullOrEmpty(range.RequiredQualification))
        {
            var lastOccurrence = occurrences[^1].Start;
            if (string.IsNullOrWhiteSpace(r.QualificationId))
                errors.Add($"{label}: {range.Name} requires a valid '{range.RequiredQualification}' qualification ID.");
            else
            {
                var q = _qualifications.Lookup(r.QualificationId);
                if (q is null)
                    errors.Add($"{label}: qualification '{r.QualificationId}' was not found in the qualification registry.");
                else if (!string.Equals(q.QualificationType, range.RequiredQualification, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{label}: qualification '{q.QualificationId}' is '{q.QualificationType}', but {range.Name} requires '{range.RequiredQualification}'.");
                else if (q.ExpiresOn.Date < lastOccurrence.Date)
                    errors.Add($"{label}: qualification '{q.QualificationId}' expires {q.ExpiresOn:MMM d, yyyy}, before the reservation date {lastOccurrence:MMM d, yyyy}.");
                else if (owner.Role == Roles.Customer && !string.Equals(q.HolderName, owner.Name, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{label}: qualification '{q.QualificationId}' belongs to a different person.");
            }
        }

        // ---- 3. Tier policy ---------------------------------------------------------------------------
        if (owner.Tier is { } tier && !tierOverride)
        {
            var p = _store.Policy(tier);
            var duration = (r.EndTime - r.StartTime).TotalHours;

            if (range.MinimumTier > tier)
                errors.Add($"{label}: {range.Name} is available to {range.MinimumTier} tier and above (your tier: {tier}).");

            if (duration > p.MaxHoursPerBooking)
                errors.Add($"{label}: {tier} tier bookings are limited to {p.MaxHoursPerBooking} hour(s) (requested {duration:0.#}).");
            if (duration > range.MaxHoursPerBooking)
                errors.Add($"{label}: {range.Name} bookings are limited to {range.MaxHoursPerBooking} hour(s).");

            if (occurrences[0].Start < now.AddHours(p.MinLeadHours))
                errors.Add($"{label}: {tier} tier must book at least {p.MinLeadHours} hour(s) ahead.");

            var lastAllowed = now.Date.AddDays(p.AdvanceBookingDays + 1);
            var tooFar = occurrences.FirstOrDefault(o => o.Start >= lastAllowed);
            if (tooFar != default)
                errors.Add($"{Label(range, tooFar.Start)}: {tier} tier can book up to {p.AdvanceBookingDays} days in advance (until {lastAllowed.AddDays(-1):MMM d, yyyy}).");

            if (isRecurring)
            {
                if (!p.AllowRecurring)
                    errors.Add($"{label}: recurring reservations are not available for {tier} tier.");
                else if (totalOccurrences > p.MaxRecurringOccurrences)
                    errors.Add($"{label}: {tier} tier recurring bookings are capped at {p.MaxRecurringOccurrences} occurrences (requested {totalOccurrences}).");
            }
        }

        return errors;
    }

    /// <summary>Per-customer caps evaluated over the whole proposed state (per day, active future, monthly hours).</summary>
    public List<string> ValidateAggregates(AppUser owner, List<Reservation> proposed, DateTime now)
    {
        var errors = new List<string>();
        if (owner.Tier is not { } tier) return errors;
        var p = _store.Policy(tier);

        var occ = proposed.Where(x => x.CustomerId == owner.Id)
            .SelectMany(x => RecurrenceExpander.Expand(x.StartTime, x.EndTime, x.EffectiveRule, x.EffectiveExceptions,
                now, now + ValidationHorizon, 2000))
            .ToList();

        if (occ.Count > p.MaxActiveFutureBookings)
            errors.Add($"{tier} tier may hold at most {p.MaxActiveFutureBookings} upcoming bookings (this request would make {occ.Count}).");

        var busyDay = occ.GroupBy(o => o.Start.Date).FirstOrDefault(g => g.Count() > p.MaxBookingsPerDay);
        if (busyDay is not null)
            errors.Add($"{tier} tier may book {p.MaxBookingsPerDay} session(s) per day ({busyDay.Key:ddd MMM d} would have {busyDay.Count()}).");

        var busyMonth = occ.GroupBy(o => new { o.Start.Year, o.Start.Month })
            .Select(g => new { g.Key, Hours = g.Sum(o => (o.End - o.Start).TotalHours) })
            .FirstOrDefault(g => g.Hours > p.MaxHoursPerMonth);
        if (busyMonth is not null)
            errors.Add($"{tier} tier is capped at {p.MaxHoursPerMonth} range hours per month ({new DateTime(busyMonth.Key.Year, busyMonth.Key.Month, 1):MMMM yyyy} would have {busyMonth.Hours:0.#}).");

        return errors;
    }

    private IEnumerable<string> CheckConflicts(Reservation r, ShootingRange range, List<Occurrence> occurrences, List<Reservation> proposed)
    {
        var from = occurrences[0].Start;
        var to = occurrences[^1].End;
        int reported = 0;
        foreach (var other in proposed.Where(o => o.RangeId == r.RangeId && o.Id != r.Id))
        {
            foreach (var oo in RecurrenceExpander.Expand(other.StartTime, other.EndTime, other.EffectiveRule, other.EffectiveExceptions, from, to))
            {
                var hit = occurrences.FirstOrDefault(o => o.Start < oo.End && o.End > oo.Start);
                if (hit != default)
                {
                    yield return $"{Label(range, hit.Start)}: conflicts with an existing reservation ({oo.Start:h:mm tt} - {oo.End:h:mm tt}).";
                    if (++reported >= 3) yield break;
                    break;
                }
            }
        }
    }

    private IEnumerable<string> CheckBlackouts(ShootingRange range, List<Occurrence> occurrences)
    {
        var from = occurrences[0].Start;
        var to = occurrences[^1].End;
        int reported = 0;
        foreach (var b in _store.Blackouts.Where(b => AppliesTo(b, range)))
        {
            foreach (var bo in RecurrenceExpander.Expand(b.StartTime, b.EndTime, b.RecurrenceRule, b.RecurrenceException, from, to))
            {
                var hit = occurrences.FirstOrDefault(o => o.Start < bo.End && o.End > bo.Start);
                if (hit != default)
                {
                    yield return $"{Label(range, hit.Start)}: blocked by blackout '{b.Title}'.";
                    if (++reported >= 3) yield break;
                    break;
                }
            }
        }
    }

    public static bool AppliesTo(Blackout b, ShootingRange range) =>
        b.RangeId == range.Id || (b.RangeId is null && (b.FacilityId is null || b.FacilityId == range.FacilityId));

    private static string Fmt(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");
}
