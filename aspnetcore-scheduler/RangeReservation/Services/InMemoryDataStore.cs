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
/// Thread-safe in-memory store seeded with demo data. Replace with EF Core (SQL Server on-prem) or the customer's
/// reservation services - controllers and the rules engine only depend on this class's API.
/// </summary>
public class InMemoryDataStore
{
    public object Sync { get; } = new();
    public List<AppUser> Users { get; }
    public List<CorporateGroup> Groups { get; }
    public List<Facility> Facilities { get; }
    public List<ShootingRange> Ranges { get; }
    public List<TierPolicy> Policies { get; }
    public FacilitySettings Settings { get; } = new();
    public List<Reservation> Reservations { get; } = new();
    public List<Blackout> Blackouts { get; } = new();
    public List<NotificationRecord> Notifications { get; } = new();
    public List<OnboardingApplication> OnboardingApplications { get; } = new();
    public List<ComplianceDocument> ComplianceDocuments { get; } = new();
    public List<ComplianceExpirationEvent> ExpirationEvents { get; } = new();
    private int _reservationSeq = 1000;
    private int _blackoutSeq = 0;
    private int _requestSeq = 10230;
    private int _notificationSeq = 0;
    private int _applicationSeq = 1000;
    private int _docSeq = 1000;
    private int _eventSeq = 1000;

    // ==================== Onboarding & Compliance CRUD Methods ====================

    public void AddApplication(OnboardingApplication app)
    {
        lock (Sync)
        {
            app.Id = ++_applicationSeq;
            OnboardingApplications.Add(app);
        }
    }

    public OnboardingApplication? GetApplication(int id)
    {
        lock (Sync)
            return OnboardingApplications.FirstOrDefault(a => a.Id == id);
    }

    public void UpdateApplication(OnboardingApplication app)
    {
        lock (Sync)
        {
            var existing = OnboardingApplications.FirstOrDefault(a => a.Id == app.Id);
            if (existing != null)
            {
                OnboardingApplications.Remove(existing);
                OnboardingApplications.Add(app);
            }
        }
    }

    public List<OnboardingApplication> GetAllApplications()
    {
        lock (Sync)
            return new List<OnboardingApplication>(OnboardingApplications);
    }

    public void AddComplianceDocument(ComplianceDocument doc)
    {
        lock (Sync)
        {
            doc.Id = ++_docSeq;
            ComplianceDocuments.Add(doc);
        }
    }

    public ComplianceDocument? GetComplianceDocument(int id)
    {
        lock (Sync)
            return ComplianceDocuments.FirstOrDefault(d => d.Id == id);
    }

    public void UpdateComplianceDocument(ComplianceDocument doc)
    {
        lock (Sync)
        {
            var existing = ComplianceDocuments.FirstOrDefault(d => d.Id == doc.Id);
            if (existing != null)
            {
                ComplianceDocuments.Remove(existing);
                ComplianceDocuments.Add(doc);
            }
        }
    }

    public List<ComplianceDocument> GetComplianceDocuments(int groupId)
    {
        lock (Sync)
            return new List<ComplianceDocument>(ComplianceDocuments.Where(d => d.GroupId == groupId));
    }

    public List<ComplianceDocument> GetAllComplianceDocuments()
    {
        lock (Sync)
            return new List<ComplianceDocument>(ComplianceDocuments);
    }

    public void AddComplianceExpirationEvent(ComplianceExpirationEvent evt)
    {
        lock (Sync)
        {
            evt.Id = ++_eventSeq;
            ExpirationEvents.Add(evt);
        }
    }

    public ComplianceExpirationEvent? GetComplianceExpirationEvent(int groupId, int documentId, string eventType)
    {
        lock (Sync)
            return ExpirationEvents.FirstOrDefault(e => 
                e.GroupId == groupId && e.DocumentId == documentId && e.EventType == eventType);
    }

    public List<ComplianceExpirationEvent> GetAllExpirationEvents()
    {
        lock (Sync)
            return new List<ComplianceExpirationEvent>(ExpirationEvents);
    }

    public InMemoryDataStore(FacilityClock clock)
    {
        Facilities = new()
        {
            new() { Id = 1, Name = "Desert Ridge Range Complex", City = "Phoenix, AZ", Color = "#b45309" },
            new() { Id = 2, Name = "Saguaro Valley Range", City = "Tucson, AZ", Color = "#0f766e" },
        };

        string[] pistol = { "Handgun", "Rimfire" };
        Ranges = new()
        {
            new() { Id = 1, FacilityId = 1, Name = "Pistol Range 1", RangeType = "Pistol", Color = "#d97706", OpenTime = new(7, 0, 0), CloseTime = new(20, 0, 0),
                    MaxHeadcount = 12, MaxHoursPerBooking = 4, AllowedFirearms = pistol.ToList(), AllowedTargetTypes = { "Paper", "Steel" }, MaxTargetDistanceYards = 25 },
            new() { Id = 2, FacilityId = 1, Name = "Pistol Range 2", RangeType = "Pistol", Color = "#ea580c", OpenTime = new(7, 0, 0), CloseTime = new(20, 0, 0),
                    MaxHeadcount = 12, MaxHoursPerBooking = 4, AllowedFirearms = pistol.ToList(), AllowedTargetTypes = { "Paper", "Steel" }, MaxTargetDistanceYards = 25 },
            new() { Id = 3, FacilityId = 1, Name = "Pistol Range 3", RangeType = "Pistol", Color = "#c2410c", OpenTime = new(7, 0, 0), CloseTime = new(20, 0, 0),
                    MaxHeadcount = 12, MaxHoursPerBooking = 4, AllowedFirearms = pistol.ToList(), AllowedTargetTypes = { "Paper", "Steel" }, MaxTargetDistanceYards = 25 },
            new() { Id = 4, FacilityId = 1, Name = "Rifle Range (100 yd)", RangeType = "Rifle", Color = "#65a30d", OpenTime = new(7, 0, 0), CloseTime = new(19, 0, 0),
                    MaxHeadcount = 16, MaxHoursPerBooking = 4, AllowedFirearms = { "Rifle", "Rimfire", "Muzzleloader" }, AllowedTargetTypes = { "Paper", "Steel" }, MaxTargetDistanceYards = 100 },
            new() { Id = 5, FacilityId = 1, Name = "High Power Range", RangeType = "High Power", Color = "#15803d", OpenTime = new(7, 0, 0), CloseTime = new(18, 0, 0),
                    MaxHeadcount = 10, MaxHoursPerBooking = 6, IsSpecialized = true, SpecializedRequirement = "High Power secondary orientation",
                    AllowedFirearms = { "Rifle" }, AllowedTargetTypes = { "Paper", "Steel" }, MaxTargetDistanceYards = 600 },
            new() { Id = 6, FacilityId = 2, Name = "Trap & Skeet Field", RangeType = "Shotgun", Color = "#0e7490", OpenTime = new(8, 0, 0), CloseTime = new(18, 0, 0),
                    MaxHeadcount = 10, MaxHoursPerBooking = 3, AllowedFirearms = { "Shotgun" }, AllowedTargetTypes = { "Clay" }, MaxTargetDistanceYards = 50 },
            new() { Id = 7, FacilityId = 2, Name = "Frankoma Range", RangeType = "Multi-purpose", Color = "#0369a1", OpenTime = new(7, 0, 0), CloseTime = new(19, 0, 0),
                    MaxHeadcount = 20, MaxHoursPerBooking = 8, IsSpecialized = true, SpecializedRequirement = "Frankoma range qualification",
                    AllowedFirearms = { "Handgun", "Rifle", "Shotgun" }, AllowedTargetTypes = { "Paper", "Steel", "Reactive" }, MaxTargetDistanceYards = 50 },
            new() { Id = 8, FacilityId = 2, Name = "Tactical Training Bay", RangeType = "Tactical", Color = "#7c3aed", OpenTime = new(6, 0, 0), CloseTime = new(21, 0, 0),
                    MaxHeadcount = 24, MaxHoursPerBooking = 8, MinimumTier = CustomerTier.Tier4,
                    AllowedFirearms = { "Handgun", "Rifle", "Shotgun" }, AllowedTargetTypes = { "Paper", "Steel", "Reactive", "Silhouette" }, MaxTargetDistanceYards = 50 },
        };

        var today = clock.Today;
        Groups = new()
        {
            new() { Id = 1, Name = "Desert Valley Police Department", Tier = CustomerTier.Tier4, ContactEmail = "training@dvpd.example",
                    RangePermissions = { new() { RangeId = 5, GrantedOn = today.AddDays(-120), GrantedBy = "Alex Morgan", VerificationNote = "High Power orientation completed - roster on file" },
                                         new() { RangeId = 7, GrantedOn = today.AddDays(-60), GrantedBy = "Alex Morgan", VerificationNote = "Frankoma qualification verified" } } },
            new() { Id = 2, Name = "Sonoran Security Services LLC", Tier = CustomerTier.Tier2, ContactEmail = "ops@sonoransecurity.example" },
            new() { Id = 3, Name = "Copper State Rifle Club", Tier = CustomerTier.Tier3, ContactEmail = "board@copperstaterc.example",
                    RangePermissions = { new() { RangeId = 5, GrantedOn = today.AddDays(-200), GrantedBy = "Alex Morgan", VerificationNote = "Club RSOs completed High Power orientation" } } },
            new() { Id = 4, Name = "Mesa Youth Shooting Sports", Tier = CustomerTier.Tier1, ContactEmail = "coach@mesayouth.example" },
        };

        Users = new()
        {
            new() { Id = "admin", Name = "Alex Morgan", Email = "alex.morgan@azgfd.example", Role = Roles.Admin, Description = "AZGFD Administrator - review queue, blackouts, permissions" },
            new() { Id = "dvpd.primary", Name = "Lt. Maria Ramirez", Email = "m.ramirez@dvpd.example", GroupId = 1, GroupRole = GroupRole.Primary, Description = "Primary user" },
            new() { Id = "dvpd.delegate", Name = "Sgt. Kevin Cho", Email = "k.cho@dvpd.example", GroupId = 1, GroupRole = GroupRole.Delegate, Description = "Delegate" },
            new() { Id = "sss.primary", Name = "Dana Whitfield", Email = "dana@sonoransecurity.example", GroupId = 2, GroupRole = GroupRole.Primary, Description = "Primary user" },
            new() { Id = "sss.delegate", Name = "Eli Navarro", Email = "eli@sonoransecurity.example", GroupId = 2, GroupRole = GroupRole.Delegate, Description = "Delegate" },
            new() { Id = "csrc.primary", Name = "Morgan Hale", Email = "morgan@copperstaterc.example", GroupId = 3, GroupRole = GroupRole.Primary, Description = "Primary user" },
            new() { Id = "mysa.primary", Name = "Riley Chen", Email = "riley@mesayouth.example", GroupId = 4, GroupRole = GroupRole.Primary, Description = "Primary user" },
        };

        Policies = new()
        {
            new() { Tier = CustomerTier.Tier1, DisplayName = "Tier 1", Audience = "Community / youth / non-profit",
                    AdvanceBookingDays = 30, MinLeadHours = 24, MaxHoursPerBooking = 3, MaxDatesPerTransaction = 4, AllowRecurring = false, CancellationCutoffHours = 48 },
            new() { Tier = CustomerTier.Tier2, DisplayName = "Tier 2", Audience = "Commercial / corporate",
                    AdvanceBookingDays = 60, MinLeadHours = 24, MaxHoursPerBooking = 4, MaxDatesPerTransaction = 8, AllowRecurring = true, CancellationCutoffHours = 48 },
            new() { Tier = CustomerTier.Tier3, DisplayName = "Tier 3", Audience = "Clubs & certified instructors",
                    AdvanceBookingDays = 90, MinLeadHours = 12, MaxHoursPerBooking = 6, MaxDatesPerTransaction = 12, AllowRecurring = true, CancellationCutoffHours = 24 },
            new() { Tier = CustomerTier.Tier4, DisplayName = "Tier 4", Audience = "Law Enforcement / Military",
                    AdvanceBookingDays = 365, MinLeadHours = 2, MaxHoursPerBooking = 8, MaxDatesPerTransaction = 26, AllowRecurring = true, CancellationCutoffHours = 4 },
        };

        Seed(today);
    }

    public int NextReservationId() => Interlocked.Increment(ref _reservationSeq);
    public int NextBlackoutId() => Interlocked.Increment(ref _blackoutSeq);
    public string NextRequestId() => $"REQ-{Interlocked.Increment(ref _requestSeq)}";
    public int NextNotificationId() => Interlocked.Increment(ref _notificationSeq);

    public AppUser? FindUser(string? id) => Users.FirstOrDefault(u => u.Id == id);
    public CorporateGroup? FindGroup(int? id) => id is null ? null : Groups.FirstOrDefault(g => g.Id == id);
    public ShootingRange? FindRange(int id) => Ranges.FirstOrDefault(r => r.Id == id);
    public Facility? FindFacility(int id) => Facilities.FirstOrDefault(f => f.Id == id);
    public TierPolicy Policy(CustomerTier tier) => Policies.First(p => p.Tier == tier);

    /// <summary>Ranges a group may see / select: standard ranges plus specialized ranges it has been granted.</summary>
    public bool IsRangeAuthorized(CorporateGroup? group, ShootingRange range) =>
        group is null || !range.IsSpecialized || group.HasPermission(range.Id);

    private void Seed(DateTime today)
    {
        DateTime NotMonday(int offset)
        {
            var d = today.AddDays(offset);
            return d.DayOfWeek == DayOfWeek.Monday ? d.AddDays(1) : d;
        }
        var nextMonday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7);
        if (nextMonday <= today) nextMonday = nextMonday.AddDays(7);

        string R(string subject, int groupId, string userId, int rangeId, DateTime start, int hours, int headcount, string[] firearms,
                 string target, int distance, ReservationStatus status, string? rule = null, string? requestId = null, string? denial = null, bool rso = false)
        {
            var g = FindGroup(groupId)!; var u = FindUser(userId)!; var range = FindRange(rangeId)!;
            requestId ??= NextRequestId();
            Reservations.Add(new Reservation
            {
                Id = NextReservationId(), Subject = subject, StartTime = start, EndTime = start.AddHours(hours), RecurrenceRule = rule,
                RangeId = rangeId, FacilityId = range.FacilityId, GroupId = g.Id, GroupName = g.Name, Tier = g.Tier,
                SubmittedById = u.Id, SubmittedByName = u.Name, Headcount = headcount, FirearmTypes = firearms.ToList(),
                TargetType = target, TargetDistanceYards = distance, TargetCount = headcount, RsoRequested = rso,
                Status = status, RequestId = requestId, DenialReason = denial, CreatedAt = today.AddDays(-2).AddHours(9),
                ReviewedBy = status is ReservationStatus.Approved or ReservationStatus.Denied ? "Alex Morgan" : null,
                ReviewedAt = status is ReservationStatus.Approved or ReservationStatus.Denied ? today.AddDays(-1).AddHours(10) : null
            });
            return requestId;
        }

        R("Armed guard requalification", 2, "sss.primary", 3, NotMonday(3).AddHours(9), 2, 8, new[] { "Handgun" }, "Paper", 15, ReservationStatus.Approved);
        R("Patrol rifle in-service", 1, "dvpd.primary", 8, nextMonday.AddHours(7), 4, 18, new[] { "Handgun", "Rifle" }, "Steel", 25,
          ReservationStatus.Approved, rule: "FREQ=WEEKLY;BYDAY=MO;INTERVAL=1;COUNT=8;", rso: true);
        R("Monday handgun qualification", 1, "dvpd.delegate", 2, nextMonday.AddHours(13), 2, 10, new[] { "Handgun" }, "Paper", 25, ReservationStatus.Approved);
        R("Club high power match practice", 3, "csrc.primary", 5, NotMonday(4).AddHours(8), 4, 8, new[] { "Rifle" }, "Paper", 600, ReservationStatus.Pending);
        var youth = R("Youth rimfire clinic", 4, "mysa.primary", 1, NotMonday(5).AddHours(10), 2, 10, new[] { "Rimfire" }, "Paper", 10, ReservationStatus.Pending);
        R("Youth rimfire clinic", 4, "mysa.primary", 1, NotMonday(12).AddHours(10), 2, 10, new[] { "Rimfire" }, "Paper", 10, ReservationStatus.Pending, requestId: youth);
        R("Carbine familiarization", 2, "sss.delegate", 4, NotMonday(2).AddHours(13), 2, 6, new[] { "Rifle" }, "Steel", 100, ReservationStatus.Denied,
          denial: "Requested time overlaps a scheduled range officer training. Please choose another afternoon.");

        // Blackouts: recurring maintenance, daily cleaning, a one-time event closure and annual holiday closures.
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Weekly berm maintenance", RangeId = 5, FacilityId = 1,
            StartTime = weekStart.AddDays(3).AddHours(7), EndTime = weekStart.AddDays(3).AddHours(10),
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=WE;INTERVAL=1;", Reason = "Target frame & berm repair" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Daily lead-abatement cleaning", RangeId = 8, FacilityId = 2,
            StartTime = today.AddDays(-7).AddHours(12), EndTime = today.AddDays(-7).AddHours(13),
            RecurrenceRule = "FREQ=DAILY;INTERVAL=1;", Reason = "HVAC & floor cleaning (indoor bay)" });
        var eventDay = NotMonday(6);
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "State match - facility closed", FacilityId = 2,
            StartTime = eventDay.AddHours(7), EndTime = eventDay.AddHours(16), Reason = "Major event - Saguaro Valley closed to reservations" });
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Independence Day - closed", IsAllDay = true,
            StartTime = new DateTime(today.Year, 7, 4), EndTime = new DateTime(today.Year, 7, 5),
            RecurrenceRule = "FREQ=YEARLY;BYMONTHDAY=4;BYMONTH=7;INTERVAL=1;", Reason = "Annual holiday closure (all facilities)" });
        var thanksgiving = Enumerable.Range(1, 30).Select(d => new DateTime(today.Year, 11, d)).Where(d => d.DayOfWeek == DayOfWeek.Thursday).ElementAt(3);
        Blackouts.Add(new Blackout { Id = NextBlackoutId(), Title = "Thanksgiving - closed", IsAllDay = true,
            StartTime = thanksgiving, EndTime = thanksgiving.AddDays(1),
            RecurrenceRule = "FREQ=YEARLY;BYDAY=TH;BYSETPOS=4;BYMONTH=11;INTERVAL=1;", Reason = "Annual holiday closure (all facilities)" });
    }
}
