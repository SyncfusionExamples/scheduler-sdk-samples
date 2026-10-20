using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>Facility "now" in the facility time zone (Arizona does not observe DST).</summary>
public class FacilityClock
{
    private readonly TimeZoneInfo _tz;
    public FacilityClock(IConfiguration config)
    {
        var id = config["Facility:TimeZone"] ?? "America/Phoenix";
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { _tz = TimeZoneInfo.CreateCustomTimeZone("AZ", TimeSpan.FromHours(-7), "Arizona", "Arizona"); }
    }
    public string TimeZoneId => _tz.Id;
    public DateTime Now => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz), DateTimeKind.Unspecified);
    public DateTime Today => Now.Date;
}

/// <summary>
/// Thread-safe in-memory store seeded with demo data. In production, replace with EF Core (SQL Server on-prem)
/// or calls to the customer's existing reservation data services - the controllers only depend on this class's API.
/// </summary>
public class InMemoryDataStore
{
    public object Sync { get; } = new();
    public List<AppUser> Users { get; }
    public List<Facility> Facilities { get; }
    public List<ShootingRange> Ranges { get; }
    public List<TierPolicy> Policies { get; }
    public List<Reservation> Reservations { get; } = new();
    public List<Blackout> Blackouts { get; } = new();
    private int _reservationSeq = 1000;
    private int _blackoutSeq = 1;

    public InMemoryDataStore(FacilityClock clock)
    {
        Users = new()
        {
            new() { Id = "admin", Name = "Alex Morgan", Email = "alex.morgan@range.example", Role = Roles.Admin, Description = "Range Administrator - full calendar control" },
            new() { Id = "bronze", Name = "Bailey Brooks", Email = "bailey@example.com", Tier = CustomerTier.Bronze, Description = "Public day-pass customer" },
            new() { Id = "silver", Name = "Sam Rivera", Email = "sam@example.com", Tier = CustomerTier.Silver, Description = "Standard member" },
            new() { Id = "gold", Name = "Jordan Lee", Email = "jordan@example.com", Tier = CustomerTier.Gold, Description = "Premium member (Long Range qualified)" },
            new() { Id = "platinum", Name = "Casey Nguyen", Email = "casey@agency.example", Tier = CustomerTier.Platinum, Description = "Law enforcement / certified instructor" },
        };

        Facilities = new()
        {
            new() { Id = 1, Name = "Desert Ridge Range Complex", City = "Phoenix, AZ", Color = "#b45309" },
            new() { Id = 2, Name = "Saguaro Valley Range", City = "Tucson, AZ", Color = "#0f766e" },
        };

        Ranges = new()
        {
            new() { Id = 1, FacilityId = 1, Name = "Pistol Bay A", RangeType = "Pistol", Color = "#d97706", OpenTime = new(7, 0, 0), CloseTime = new(20, 0, 0),
                    MaxParticipants = 4, MaxHoursPerBooking = 2, MaxSlotsPerTransaction = 6, MinimumTier = CustomerTier.Bronze, AllowedFirearms = { "Handgun", "Rimfire" } },
            new() { Id = 2, FacilityId = 1, Name = "Pistol Bay B", RangeType = "Pistol", Color = "#ea580c", OpenTime = new(7, 0, 0), CloseTime = new(20, 0, 0),
                    MaxParticipants = 4, MaxHoursPerBooking = 2, MaxSlotsPerTransaction = 6, MinimumTier = CustomerTier.Bronze, AllowedFirearms = { "Handgun", "Rimfire" } },
            new() { Id = 3, FacilityId = 1, Name = "Rifle 100 yd", RangeType = "Rifle", Color = "#65a30d", OpenTime = new(7, 0, 0), CloseTime = new(19, 0, 0),
                    MaxParticipants = 6, MaxHoursPerBooking = 3, MaxSlotsPerTransaction = 6, MinimumTier = CustomerTier.Silver, AllowedFirearms = { "Rifle", "Rimfire" } },
            new() { Id = 4, FacilityId = 1, Name = "Rifle 300 yd (Long Range)", RangeType = "Long Range", Color = "#15803d", OpenTime = new(7, 0, 0), CloseTime = new(18, 0, 0),
                    MaxParticipants = 4, MaxHoursPerBooking = 4, MaxSlotsPerTransaction = 4, MinimumTier = CustomerTier.Gold, RequiredQualification = "Long Range", AllowedFirearms = { "Rifle" } },
            new() { Id = 5, FacilityId = 2, Name = "Trap & Skeet Field", RangeType = "Shotgun", Color = "#0e7490", OpenTime = new(8, 0, 0), CloseTime = new(18, 0, 0),
                    MaxParticipants = 5, MaxHoursPerBooking = 3, MaxSlotsPerTransaction = 6, MinimumTier = CustomerTier.Silver, AllowedFirearms = { "Shotgun" } },
            new() { Id = 6, FacilityId = 2, Name = "Tactical Training Bay", RangeType = "Tactical", Color = "#7c3aed", OpenTime = new(6, 0, 0), CloseTime = new(21, 0, 0),
                    MaxParticipants = 12, MaxHoursPerBooking = 8, MaxSlotsPerTransaction = 20, MinimumTier = CustomerTier.Platinum, RequiredQualification = "Tactical", AllowedFirearms = { "Handgun", "Rifle", "Shotgun" } },
        };

        Policies = new()
        {
            new() { Tier = CustomerTier.Bronze, DisplayName = "Bronze", Audience = "Public day pass", Color = "#a16207",
                    AdvanceBookingDays = 7, MinLeadHours = 2, MaxHoursPerBooking = 2, MaxBookingsPerDay = 1, MaxActiveFutureBookings = 3,
                    MaxHoursPerMonth = 8, AllowRecurring = false, MaxRecurringOccurrences = 0, MaxSlotsPerTransaction = 2, CancellationCutoffHours = 24 },
            new() { Tier = CustomerTier.Silver, DisplayName = "Silver", Audience = "Standard member", Color = "#64748b",
                    AdvanceBookingDays = 14, MinLeadHours = 2, MaxHoursPerBooking = 3, MaxBookingsPerDay = 2, MaxActiveFutureBookings = 8,
                    MaxHoursPerMonth = 20, AllowRecurring = true, MaxRecurringOccurrences = 4, MaxSlotsPerTransaction = 4, CancellationCutoffHours = 24 },
            new() { Tier = CustomerTier.Gold, DisplayName = "Gold", Audience = "Premium member", Color = "#ca8a04",
                    AdvanceBookingDays = 60, MinLeadHours = 1, MaxHoursPerBooking = 4, MaxBookingsPerDay = 3, MaxActiveFutureBookings = 20,
                    MaxHoursPerMonth = 40, AllowRecurring = true, MaxRecurringOccurrences = 12, MaxSlotsPerTransaction = 8, CancellationCutoffHours = 12 },
            new() { Tier = CustomerTier.Platinum, DisplayName = "Platinum", Audience = "Law enforcement / instructors / clubs", Color = "#4f46e5",
                    AdvanceBookingDays = 366, MinLeadHours = 0, MaxHoursPerBooking = 8, MaxBookingsPerDay = 6, MaxActiveFutureBookings = 150,
                    MaxHoursPerMonth = 200, AllowRecurring = true, MaxRecurringOccurrences = 52, MaxSlotsPerTransaction = 20, CancellationCutoffHours = 2 },
        };

        Seed(clock.Today);
    }

    public int NextReservationId() => Interlocked.Increment(ref _reservationSeq);
    public int NextBlackoutId() => Interlocked.Increment(ref _blackoutSeq) - 1;

    public AppUser? FindUser(string? id) => Users.FirstOrDefault(u => u.Id == id);
    public ShootingRange? FindRange(int id) => Ranges.FirstOrDefault(r => r.Id == id);
    public TierPolicy Policy(CustomerTier tier) => Policies.First(p => p.Tier == tier);

    private void Seed(DateTime today)
    {
        DateTime D(int dayOffset, int hour, int minute = 0) => today.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
        var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7 + 0);

        void R(string subject, int rangeId, DateTime start, int hours, string customerId, string firearm, int participants,
               string? rule = null, string? caliber = null, string? qual = null, bool rso = false)
        {
            var range = FindRange(rangeId)!;
            var user = FindUser(customerId)!;
            Reservations.Add(new Reservation
            {
                Id = NextReservationId(), Subject = subject, StartTime = start, EndTime = start.AddHours(hours),
                RangeId = rangeId, FacilityId = range.FacilityId, CustomerId = user.Id, CustomerName = user.Name, Tier = user.Tier,
                FirearmType = firearm, Caliber = caliber, Participants = participants, RecurrenceRule = rule,
                QualificationId = qual, RsoRequested = rso, CreatedAt = today.AddDays(-3), CreatedBy = user.Id
            });
        }

        R("Pistol practice", 1, D(1, 9), 1, "bronze", "Handgun", 1, caliber: "9mm");
        R("Rimfire session", 2, D(1, 14), 2, "silver", "Rimfire", 2, caliber: ".22 LR");
        R("Zeroing new optic", 3, D(2, 8), 2, "silver", "Rifle", 1, caliber: "5.56 NATO");
        R("Long range practice", 4, D(2, 10), 3, "gold", "Rifle", 2, caliber: "6.5 Creedmoor", qual: "LRQ-1001");
        R("Trap league", 5, D(3, 16), 2, "gold", "Shotgun", 5, caliber: "12 ga");
        R("Agency quarterly qualification", 6, nextMonday.AddDays(nextMonday == today ? 7 : 0).AddHours(7), 4, "platinum", "Handgun", 10,
          rule: "FREQ=WEEKLY;BYDAY=MO;INTERVAL=1;COUNT=8;", caliber: "9mm", qual: "TAC-2001", rso: true);
        R("Instructor-led basics class", 1, D(4, 17), 2, "platinum", "Handgun", 4, caliber: "9mm", rso: true);

        // Blackouts: recurring maintenance, a one-time event closure and an annual holiday closure.
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Weekly berm maintenance", RangeId = 4, FacilityId = 1,
            StartTime = today.AddDays(-(int)today.DayOfWeek).AddDays(3).AddHours(7), // Wednesday 07:00-10:00
            EndTime = today.AddDays(-(int)today.DayOfWeek).AddDays(3).AddHours(10),
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=WE;INTERVAL=1;", Reason = "Target frame & berm repair" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Daily lead-abatement cleaning", RangeId = 6, FacilityId = 2,
            StartTime = today.AddDays(-7).AddHours(12), EndTime = today.AddDays(-7).AddHours(13),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1;", Reason = "HVAC & floor cleaning (indoor bay)" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Private competition - facility closed", FacilityId = 2,
            StartTime = D(5, 8), EndTime = D(5, 15), Reason = "State match - Saguaro Valley closed to public" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Independence Day - closed", IsAllDay = true,
            StartTime = new DateTime(today.Year, 7, 4), EndTime = new DateTime(today.Year, 7, 5),
            RecurrenceRule = "FREQ=YEARLY;BYMONTHDAY=4;BYMONTH=7;INTERVAL=1;", Reason = "Annual holiday closure (all facilities)" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Thanksgiving - closed", IsAllDay = true,
            StartTime = ThanksgivingOf(today.Year), EndTime = ThanksgivingOf(today.Year).AddDays(1),
            RecurrenceRule = "FREQ=YEARLY;BYDAY=TH;BYSETPOS=4;BYMONTH=11;INTERVAL=1;", Reason = "Annual holiday closure (all facilities)" });
    }

    private static DateTime ThanksgivingOf(int year)
    {
        var d = new DateTime(year, 11, 1);
        while (d.DayOfWeek != DayOfWeek.Thursday) d = d.AddDays(1);
        return d.AddDays(21);
    }
}

public interface IQualificationRegistry
{
    QualificationRecord? Lookup(string qualificationId);
}

/// <summary>Stands in for an on-prem qualification / certification API (e.g. agency or state records system).</summary>
public class MockQualificationRegistry : IQualificationRegistry
{
    private static readonly Dictionary<string, QualificationRecord> Records = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LRQ-1001"] = new("LRQ-1001", "Jordan Lee", "Long Range", new DateTime(2027, 6, 30)),
        ["LRQ-1002"] = new("LRQ-1002", "Sam Rivera", "Long Range", new DateTime(2026, 1, 31)), // expired
        ["TAC-2001"] = new("TAC-2001", "Casey Nguyen", "Tactical", new DateTime(2027, 12, 31)),
        ["TAC-2002"] = new("TAC-2002", "Casey Nguyen", "Tactical", new DateTime(2026, 12, 15)), // expires soon
    };

    public QualificationRecord? Lookup(string qualificationId) =>
        Records.TryGetValue(qualificationId.Trim(), out var r) ? r : null;
}
