using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>
/// Authoritative, server-side booking rules. The Scheduler UI mirrors several of them for a better experience
/// (greyed-out Mondays, hidden ranges, recurrence options, cart restrictions) but every request is re-validated here,
/// and again when an administrator approves it.
///
/// Rule groups:
///   1. Hard rules (always enforced, admins included): availability across organizations (UC-10), blackouts (UC-13),
///      Tier 4 Monday rule (UC-11), recurring end-date cap (UC-09), operating hours, past dates.
///   2. Range rules (always enforced): specialized range permission (UC-14), firearm / target restrictions, headcount.
///   3. Tier policy (enforced for corporate users; an admin may override): minimum tier, advance window, lead time,
///      duration, recurring allowed.
/// </summary>
public class ReservationRulesEngine
{
    public static class Checks
    {
        public const string Schedule = "Schedule & operating hours";
        public const string Authorization = "Range authorization";
        public const string Firearms = "Firearm restrictions";
        public const string Headcount = "Headcount";
        public const string Targets = "Target attributes";
        public const string Availability = "Availability (no conflicts)";
        public const string Blackouts = "Blackouts";
        public const string Monday = "Tier 4 Monday rule";
        public const string Recurrence = "Recurrence cap";
        public const string TierPolicy = "Tier policy";

        public static readonly string[] All = { Schedule, Authorization, Firearms, Headcount, Targets, Availability, Blackouts, Monday, Recurrence, TierPolicy };
        /// <summary>Violations that block approval even for an administrator.</summary>
        public static readonly HashSet<string> Blocking = new() { Schedule, Authorization, Firearms, Headcount, Targets, Availability, Blackouts, Monday, Recurrence };
    }

    public const string SingleRangeMessage = "You can only select multiple dates for one range per booking.";

    private readonly InMemoryDataStore _store;
    public ReservationRulesEngine(InMemoryDataStore store) => _store = store;

    /// <summary>UC-09: recurring series may run through the end of the following calendar year.</summary>
    public static DateTime RecurrenceCap(DateTime now) => new DateTime(now.Year + 1, 12, 31, 23, 59, 59);

    public static string Label(ShootingRange? range, DateTime start) =>
        $"{range?.Name ?? "Unknown range"} · {start.ToString(start.Year == DateTime.Today.Year ? "ddd MMM d, h:mm tt" : "ddd MMM d yyyy, h:mm tt")}";

    /// <summary>Validates one reservation (all of its occurrences) against the proposed state of the calendar.</summary>
    public List<RuleViolation> Validate(Reservation r, CorporateGroup? group, List<Reservation> proposed, DateTime now,
        bool tierOverride, bool checkPast, List<string>? warnings, ref int occurrenceCounter)
    {
        var v = new List<RuleViolation>();
        var range = _store.FindRange(r.RangeId);
        string label = Label(range, r.StartTime);
        void Add(string check, string msg) => v.Add(new RuleViolation(check, $"{label}: {msg}"));

        if (range is null) { Add(Checks.Schedule, "please select a valid range."); return v; }
        r.FacilityId = range.FacilityId;
        if (r.EndTime <= r.StartTime) { Add(Checks.Schedule, "end time must be after start time."); return v; }

        // ---- Recurrence (UC-09) -----------------------------------------------------------------------
        var cap = RecurrenceCap(now);
        bool isRecurring = !string.IsNullOrWhiteSpace(r.EffectiveRule);
        int? totalOccurrences = 1;
        if (isRecurring)
        {
            RecurrenceExpander.RRule rule;
            try { rule = RecurrenceExpander.Parse(r.EffectiveRule!); }
            catch { Add(Checks.Recurrence, "the repeat rule could not be understood."); return v; }
            if (rule.Freq is not ("WEEKLY" or "MONTHLY"))
                Add(Checks.Recurrence, "recurring reservations can repeat weekly or monthly only.");
            if (rule.IsInfinite)
                Add(Checks.Recurrence, $"recurring reservations must have an end date (no later than {cap:MMM d, yyyy}).");
            else
                totalOccurrences = RecurrenceExpander.CountOccurrences(r.StartTime, r.EndTime, r.EffectiveRule, r.EffectiveExceptions);
        }

        var occurrences = RecurrenceExpander.Expand(r.StartTime, r.EndTime, r.EffectiveRule, r.EffectiveExceptions,
            checkPast ? DateTime.MinValue : now, isRecurring ? cap.AddYears(1) : DateTime.MaxValue, 2000).ToList();
        occurrenceCounter += occurrences.Count;
        if (occurrences.Count == 0) { Add(Checks.Schedule, "there are no upcoming dates to reserve."); return v; }

        if (isRecurring)
        {
            var beyond = occurrences.FirstOrDefault(o => o.Start > cap);
            if (beyond != default)
                Add(Checks.Recurrence, $"the series runs past {cap:MMM d, yyyy}. Recurring reservations are capped at the end of the following calendar year (last requested date {occurrences[^1].Start:MMM d, yyyy}).");
            occurrences = occurrences.Where(o => o.Start <= cap).ToList();
            if (occurrences.Count == 0) return v;
        }

        // ---- 1. Schedule ------------------------------------------------------------------------------
        if (occurrences[0].Start < now)
            v.Add(new(Checks.Schedule, $"{Label(range, occurrences[0].Start)}: reservations cannot start in the past."));
        if (!r.IsAllDay)
        {
            var outside = occurrences.FirstOrDefault(o => o.Start < o.Start.Date + range.OpenTime || o.End > o.Start.Date + range.CloseTime);
            if (outside != default)
                v.Add(new(Checks.Schedule, $"{Label(range, outside.Start)}: outside operating hours ({Fmt(range.OpenTime)} - {Fmt(range.CloseTime)})."));
        }

        // ---- UC-11 Monday rule ------------------------------------------------------------------------
        if (_store.Settings.MondayExclusiveForTier4 && group is not null && group.Tier != CustomerTier.Tier4)
        {
            var monday = occurrences.FirstOrDefault(o => OverlapsMonday(o));
            if (monday != default)
                v.Add(new(Checks.Monday, $"{Label(range, monday.Start)}: Mondays are reserved exclusively for Tier 4 (Law Enforcement / Military) groups."));
        }

        // ---- UC-10 availability / UC-13 blackouts -----------------------------------------------------
        v.AddRange(CheckConflicts(r, range, occurrences, proposed, warnings));
        v.AddRange(CheckBlackouts(range, occurrences));

        // ---- 2. Range rules ---------------------------------------------------------------------------
        if (range.IsSpecialized && group is not null && !group.HasPermission(range.Id))
            Add(Checks.Authorization, $"{group.Name} is not authorized for {range.Name} ({range.SpecializedRequirement} required). Contact the range office.");

        if (r.Headcount < 1 || r.Headcount > range.MaxHeadcount)
            Add(Checks.Headcount, $"{range.Name} allows a headcount of 1-{range.MaxHeadcount} (requested {r.Headcount}).");

        if (r.FirearmTypes.Count == 0)
            Add(Checks.Firearms, "select at least one firearm type.");
        var notAllowed = r.FirearmTypes.Where(f => !range.AllowedFirearms.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        if (notAllowed.Count > 0)
            Add(Checks.Firearms, $"{string.Join(", ", notAllowed)} not permitted on {range.Name} (allowed: {string.Join(", ", range.AllowedFirearms)}).");

        if (string.IsNullOrWhiteSpace(r.TargetType))
            Add(Checks.Targets, "target type is required.");
        else if (!range.AllowedTargetTypes.Contains(r.TargetType, StringComparer.OrdinalIgnoreCase))
            Add(Checks.Targets, $"{r.TargetType} targets are not permitted on {range.Name} (allowed: {string.Join(", ", range.AllowedTargetTypes)}).");
        if (r.TargetDistanceYards is { } dist && (dist < 1 || dist > range.MaxTargetDistanceYards))
            Add(Checks.Targets, $"target distance must be 1-{range.MaxTargetDistanceYards} yd on {range.Name}.");

        // ---- 3. Tier policy ---------------------------------------------------------------------------
        if (group is not null && !tierOverride)
        {
            var p = _store.Policy(group.Tier);
            var tier = TierNames.Display(group.Tier);
            var duration = (r.EndTime - r.StartTime).TotalHours;

            if (range.MinimumTier > group.Tier)
                Add(Checks.Authorization, $"{range.Name} is restricted to {TierNames.Display(range.MinimumTier)} groups.");
            if (duration > Math.Min(p.MaxHoursPerBooking, range.MaxHoursPerBooking))
                Add(Checks.TierPolicy, $"bookings are limited to {Math.Min(p.MaxHoursPerBooking, range.MaxHoursPerBooking)} hour(s) ({tier} / {range.Name}).");
            if (checkPast && occurrences[0].Start < now.AddHours(p.MinLeadHours))
                Add(Checks.TierPolicy, $"{tier} groups must book at least {p.MinLeadHours} hour(s) in advance.");
            if (isRecurring && !p.AllowRecurring)
                Add(Checks.TierPolicy, $"recurring reservations are not available for {tier} groups.");
            if (!isRecurring)
            {
                var lastAllowed = now.Date.AddDays(p.AdvanceBookingDays + 1);
                if (occurrences[0].Start >= lastAllowed)
                    Add(Checks.TierPolicy, $"{tier} groups can book up to {p.AdvanceBookingDays} days ahead (until {lastAllowed.AddDays(-1):MMM d, yyyy}).");
            }
        }

        return v;
    }

    private static bool OverlapsMonday(Occurrence o)
    {
        for (var d = o.Start.Date; d < o.End; d = d.AddDays(1))
            if (d.DayOfWeek == DayOfWeek.Monday && o.End > d && o.Start < d.AddDays(1)) return true;
        return false;
    }

    private IEnumerable<RuleViolation> CheckConflicts(Reservation r, ShootingRange range, List<Occurrence> occurrences,
        List<Reservation> proposed, List<string>? warnings)
    {
        var from = occurrences[0].Start;
        var to = occurrences[^1].End;
        int reported = 0;
        foreach (var other in proposed.Where(o => o.IsActive && o.RangeId == r.RangeId && o.Id != r.Id))
        {
            bool sameGroup = r.GroupId is not null && other.GroupId == r.GroupId;
            foreach (var oo in RecurrenceExpander.Expand(other.StartTime, other.EndTime, other.EffectiveRule, other.EffectiveExceptions, from, to))
            {
                var hit = occurrences.FirstOrDefault(o => o.Start < oo.End && o.End > oo.Start);
                if (hit == default) continue;
                if (sameGroup && _store.Settings.AllowSameGroupOverlap)
                {
                    warnings?.Add($"{Label(range, hit.Start)}: overlaps another reservation of your own group (allowed).");
                    break;
                }
                yield return new RuleViolation(Checks.Availability,
                    $"{Label(range, hit.Start)}: this slot is already reserved by another organization ({oo.Start:h:mm tt} - {oo.End:h:mm tt}).");
                if (++reported >= 3) yield break;
                break;
            }
        }
    }

    private IEnumerable<RuleViolation> CheckBlackouts(ShootingRange range, List<Occurrence> occurrences)
    {
        var from = occurrences[0].Start;
        var to = occurrences[^1].End;
        int reported = 0;
        foreach (var b in _store.Blackouts.Where(b => AppliesTo(b, range)))
        {
            foreach (var bo in RecurrenceExpander.Expand(b.StartTime, b.EndTime, b.RecurrenceRule, b.RecurrenceException, from, to))
            {
                var hit = occurrences.FirstOrDefault(o => o.Start < bo.End && o.End > bo.Start);
                if (hit == default) continue;
                yield return new RuleViolation(Checks.Blackouts, $"{Label(range, hit.Start)}: blocked by blackout '{b.Title}'.");
                if (++reported >= 3) yield break;
                break;
            }
        }
    }

    public static bool AppliesTo(Blackout b, ShootingRange range) =>
        b.RangeId == range.Id || (b.RangeId is null && (b.FacilityId is null || b.FacilityId == range.FacilityId));

    private static string Fmt(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt");
}
