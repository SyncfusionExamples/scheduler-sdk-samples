namespace RangeReservationPoc.Models;

/// <summary>Corporate group tiers. Tier 4 = Law Enforcement / Military (exclusive Monday access).</summary>
public enum CustomerTier
{
    Tier1 = 1,   // Community / youth / non-profit groups
    Tier2 = 2,   // Commercial / corporate (security firms, training companies)
    Tier3 = 3,   // Clubs & certified instructors
    Tier4 = 4    // Law Enforcement / Military
}

public static class Roles
{
    public const string Admin = "Admin";          // AZGFD Administrator
    public const string Corporate = "Corporate";  // Corporate Primary User or Corporate Delegate
}

public enum GroupRole { Primary, Delegate }

/// <summary>UC-12 review workflow.</summary>
public enum ReservationStatus { Pending, Approved, Denied, Cancelled }

public static class TierNames
{
    public static string Display(CustomerTier t) => t switch
    {
        CustomerTier.Tier1 => "Tier 1",
        CustomerTier.Tier2 => "Tier 2",
        CustomerTier.Tier3 => "Tier 3",
        CustomerTier.Tier4 => "Tier 4",
        _ => t.ToString()
    };
}

public class AppUser
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = Roles.Corporate;
    public int? GroupId { get; set; }
    public GroupRole? GroupRole { get; set; }
    public string Description { get; set; } = "";
}

/// <summary>Corporate customer group (organization). Reservations belong to the group, not the individual user.</summary>
public class CorporateGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CustomerTier Tier { get; set; }
    public string ContactEmail { get; set; } = "";
    /// <summary>UC-14: specialized ranges this group is authorized for (granted manually by an admin).</summary>
    public List<RangePermission> RangePermissions { get; set; } = new();

    public bool HasPermission(int rangeId) => RangePermissions.Any(p => p.RangeId == rangeId);
}

public class RangePermission
{
    public int RangeId { get; set; }
    public DateTime GrantedOn { get; set; }
    public string GrantedBy { get; set; } = "";
    /// <summary>e.g. "High Power orientation completed 2026-09-14, cert #HP-2231".</summary>
    public string? VerificationNote { get; set; }
}

public class Facility
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string City { get; set; } = "";
    public string Color { get; set; } = "";
}

public class ShootingRange
{
    public int Id { get; set; }
    public int FacilityId { get; set; }
    public string Name { get; set; } = "";
    public string RangeType { get; set; } = "";
    public string Color { get; set; } = "";
    public TimeSpan OpenTime { get; set; } = new(7, 0, 0);
    public TimeSpan CloseTime { get; set; } = new(19, 0, 0);
    public int MaxHeadcount { get; set; } = 10;
    public int MaxHoursPerBooking { get; set; } = 4;
    public CustomerTier MinimumTier { get; set; } = CustomerTier.Tier1;
    /// <summary>UC-14: specialized ranges are only visible / bookable after an admin grants the group permission.</summary>
    public bool IsSpecialized { get; set; }
    public string? SpecializedRequirement { get; set; }
    public List<string> AllowedFirearms { get; set; } = new();
    public List<string> AllowedTargetTypes { get; set; } = new();
    public int MaxTargetDistanceYards { get; set; } = 25;
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

    // Ownership (the corporate group) and who submitted it
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public CustomerTier? Tier { get; set; }
    public string SubmittedById { get; set; } = "";
    public string SubmittedByName { get; set; } = "";

    // UC-06 booking attributes
    public int Headcount { get; set; } = 1;
    public List<string> FirearmTypes { get; set; } = new();
    public string? TargetType { get; set; }
    public int? TargetDistanceYards { get; set; }
    public int? TargetCount { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }

    // UC-12 review workflow
    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;
    /// <summary>One cart transaction / request (single slot, multi-date cart or recurring series).</summary>
    public string RequestId { get; set; } = "";
    public string? DenialReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool AdminOverride { get; set; }

    public Reservation Clone()
    {
        var c = (Reservation)MemberwiseClone();
        c.FirearmTypes = new List<string>(FirearmTypes);
        return c;
    }

    /// <summary>Pending and approved reservations hold the slot; denied / cancelled ones don't.</summary>
    public bool IsActive => Status is ReservationStatus.Pending or ReservationStatus.Approved;

    /// <summary>An edited occurrence (RecurrenceID set) is a single event even though it keeps the series' rule text.</summary>
    public string? EffectiveRule => RecurrenceID is null ? RecurrenceRule : null;
    public string? EffectiveExceptions => RecurrenceID is null ? RecurrenceException : null;
}

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

public class TierPolicy
{
    public CustomerTier Tier { get; set; }
    public string DisplayName { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AdvanceBookingDays { get; set; }
    public int MinLeadHours { get; set; }
    public int MaxHoursPerBooking { get; set; }
    public int MaxDatesPerTransaction { get; set; }
    public bool AllowRecurring { get; set; }
    public int CancellationCutoffHours { get; set; }
}

/// <summary>Facility-wide rules an admin can switch on / off.</summary>
public class FacilitySettings
{
    /// <summary>UC-11: Mondays are reserved exclusively for Tier 4 (Law Enforcement / Military).</summary>
    public bool MondayExclusiveForTier4 { get; set; } = true;
    /// <summary>UC-10: allow overlapping bookings when they belong to the same corporate group.</summary>
    public bool AllowSameGroupOverlap { get; set; } = true;
}

/// <summary>UC-12: notification sent to the corporate group (also delivered via SMTP or a local pickup folder).</summary>
public class NotificationRecord
{
    public int Id { get; set; }
    public DateTime SentAt { get; set; }
    public int? GroupId { get; set; }
    public string RequestId { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string DeliveredVia { get; set; } = "";
}
