namespace RangeReservationPoc.Models;

/// <summary>The four customer tiers discussed with the customer. Higher value = more privileges.</summary>
public enum CustomerTier
{
    Bronze = 1,   // Public day-pass customer
    Silver = 2,   // Standard member
    Gold = 3,     // Premium member
    Platinum = 4  // Law enforcement / certified instructor / club account
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}

public class AppUser
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = Roles.Customer;
    public CustomerTier? Tier { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>Top-level resource (facility / complex).</summary>
public class Facility
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Color { get; set; } = "";
}

/// <summary>Second-level resource: a bookable shooting range / bay inside a facility.</summary>
public class ShootingRange
{
    public int Id { get; set; }
    public int FacilityId { get; set; }
    public string Name { get; set; } = "";
    public string RangeType { get; set; } = "";
    public string Color { get; set; } = "";
    public TimeSpan OpenTime { get; set; } = new(7, 0, 0);
    public TimeSpan CloseTime { get; set; } = new(19, 0, 0);
    public int MaxParticipants { get; set; } = 4;
    public int MaxHoursPerBooking { get; set; } = 4;
    public int MaxSlotsPerTransaction { get; set; } = 10;
    public CustomerTier MinimumTier { get; set; } = CustomerTier.Bronze;
    /// <summary>Qualification type required to book this range (validated against the qualification registry).</summary>
    public string? RequiredQualification { get; set; }
    public List<string> AllowedFirearms { get; set; } = new();
}

/// <summary>A reservation. Field names match the Syncfusion Scheduler defaults so no field mapping is required.</summary>
public class Reservation
{
    public int Id { get; set; }
    public string Subject { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? RecurrenceID { get; set; }
    public string? RecurrenceException { get; set; }
    public int FacilityId { get; set; }
    public int RangeId { get; set; }

    // Ownership / tier
    public string CustomerId { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public CustomerTier? Tier { get; set; }

    // Custom reservation attributes (firearm qualification / validation details)
    public string? FirearmType { get; set; }
    public string? Caliber { get; set; }
    public int Participants { get; set; } = 1;
    public string? QualificationId { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }

    // Audit / transaction
    public string? BookingGroupId { get; set; }
    public bool AdminOverride { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";

    public Reservation Clone() => (Reservation)MemberwiseClone();

    /// <summary>An edited occurrence (RecurrenceID set) is a single event even though it keeps the series' rule text.</summary>
    public string? EffectiveRule => RecurrenceID is null ? RecurrenceRule : null;
    public string? EffectiveExceptions => RecurrenceID is null ? RecurrenceException : null;
}

/// <summary>Admin-controlled blackout window. RangeId = null means all ranges (facility-wide when FacilityId set).</summary>
public class Blackout
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? RecurrenceException { get; set; }
    public int? FacilityId { get; set; }
    public int? RangeId { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Per-tier booking policy. Editable by admins at runtime.</summary>
public class TierPolicy
{
    public CustomerTier Tier { get; set; }
    public string DisplayName { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Color { get; set; } = "";
    public int AdvanceBookingDays { get; set; }
    public int MinLeadHours { get; set; }
    public int MaxHoursPerBooking { get; set; }
    public int MaxBookingsPerDay { get; set; }
    public int MaxActiveFutureBookings { get; set; }
    public int MaxHoursPerMonth { get; set; }
    public bool AllowRecurring { get; set; }
    public int MaxRecurringOccurrences { get; set; }
    public int MaxSlotsPerTransaction { get; set; }
    public int CancellationCutoffHours { get; set; }
}

/// <summary>Record returned by the (mock) qualification registry - stands in for a state / internal API.</summary>
public record QualificationRecord(string QualificationId, string HolderName, string QualificationType, DateTime ExpiresOn);
