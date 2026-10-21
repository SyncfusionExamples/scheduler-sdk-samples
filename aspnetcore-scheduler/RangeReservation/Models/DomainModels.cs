namespace RangeReservationPoc.Models;

/// <summary>Corporate group tiers. Tier 4 = Law Enforcement / Military (exclusive Monday access).</summary>
public enum CustomerTier
{
    Tier1 = 1,   // Community / youth / non-profit groups
    Tier2 = 2,   // Commercial / corporate (security firms, training companies)
    Tier3 = 3,   // Clubs & certified instructors
    Tier4 = 4    // Law Enforcement / Military
}

/// <summary>UC-01, UC-02: Corporate account onboarding states.</summary>
public enum AccountStatus
{
    Pending,           // Initial application submitted, awaiting admin review
    PreApproved,       // Admin approved, waiting for compliance documentation
    Active,            // All compliance verified, booking enabled
    Suspended,         // Compliance expired or other compliance issue
    Denied,            // Admin denied the application
    Archived           // No longer active
}

/// <summary>UC-03, UC-04, UC-05: Compliance document types.</summary>
public enum ComplianceDocumentType
{
    CertificateOfInsurance,    // COI - required, occurrence policy 1M/2M minimum
    UserGroupAgreement,         // Annual signed agreement/waiver
    Other                       // Additional supplementary documents
}

/// <summary>UC-03, UC-04, UC-05: Document verification states.</summary>
public enum DocumentStatus
{
    PendingUpload,     // Awaiting user upload
    PendingReview,     // Uploaded, awaiting admin verification
    Verified,          // Admin verified, active through expiration date
    Expired,           // Expiration date passed
    Rejected           // Admin rejected, user must re-upload
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

/// <summary>UC-01, UC-02: Corporate account onboarding application.</summary>
public class OnboardingApplication
{
    public int Id { get; set; }
    /// <summary>UC-01: Company name, legal entity.</summary>
    public string CompanyName { get; set; } = "";
    /// <summary>UC-01: Company type (e.g., "Law Enforcement", "Commercial Security", "Competition Organizer", "Non-Profit").</summary>
    public string CompanyType { get; set; } = "";
    /// <summary>UC-01: Registration number, tax ID or similar.</summary>
    public string RegistrationNumber { get; set; } = "";
    /// <summary>UC-01: Primary contact full name.</summary>
    public string PrimaryContactName { get; set; } = "";
    /// <summary>UC-01: Primary contact email.</summary>
    public string PrimaryContactEmail { get; set; } = "";
    /// <summary>UC-01: Primary contact phone.</summary>
    public string PrimaryContactPhone { get; set; } = "";
    /// <summary>UC-01: Company address.</summary>
    public string Address { get; set; } = "";
    
    /// <summary>UC-02: Admin tier assignment.</summary>
    public CustomerTier? AssignedTier { get; set; }
    /// <summary>UC-02: Admin notes (e.g., verification details, decision notes).</summary>
    public string? AdminNotes { get; set; }
    /// <summary>UC-02: Admin who reviewed the application.</summary>
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    /// <summary>UC-02: Denial reason if denied.</summary>
    public string? DenialReason { get; set; }
    
    /// <summary>UC-01, UC-02: Application lifecycle state.</summary>
    public AccountStatus Status { get; set; } = AccountStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>UC-03, UC-04, UC-05: Compliance documentation.</summary>
public class ComplianceDocument
{
    public int Id { get; set; }
    /// <summary>Reference to the CorporateGroup or OnboardingApplication.</summary>
    public int GroupId { get; set; }
    public ComplianceDocumentType DocumentType { get; set; }
    /// <summary>UC-03: File name / display name.</summary>
    public string FileName { get; set; } = "";
    /// <summary>UC-03: Base64 or file path to the stored document.</summary>
    public string? DocumentPath { get; set; }
    /// <summary>UC-03: Metadata extracted from COI (e.g., policy number, carrier name).</summary>
    public string? PolicyNumber { get; set; }
    /// <summary>UC-04: "Occurrence" or "Claims-made".</summary>
    public string? PolicyType { get; set; }
    /// <summary>UC-04: e.g., "1M/2M" or "$1,000,000 per occurrence".</summary>
    public string? CoverageLimits { get; set; }
    /// <summary>UC-04: Verify that the certificate holder matches expectations.</summary>
    public string? CertificateHolder { get; set; }
    
    /// <summary>UC-04: Admin-set expiration date (typically 1 year from verification).</summary>
    public DateTime? ExpirationDate { get; set; }
    /// <summary>UC-04: Admin who verified the document.</summary>
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    /// <summary>UC-04: Admin notes or rejection reason.</summary>
    public string? ReviewNotes { get; set; }
    
    /// <summary>UC-03, UC-04, UC-05: Document lifecycle state.</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.PendingReview;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Corporate customer group (organization). Reservations belong to the group, not the individual user.</summary>
public class CorporateGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public CustomerTier Tier { get; set; }
    public string ContactEmail { get; set; } = "";
    /// <summary>UC-01, UC-02: Link to onboarding application for audit trail.</summary>
    public int? OnboardingApplicationId { get; set; }
    /// <summary>UC-01, UC-02, UC-05: Current account status (Active, Suspended, etc.).</summary>
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    /// <summary>UC-05: When booking was last unlocked (after compliance verification).</summary>
    public DateTime? LastComplianceVerifiedAt { get; set; }
    /// <summary>UC-14: specialized ranges this group is authorized for (granted manually by an admin).</summary>
    public List<RangePermission> RangePermissions { get; set; } = new();
    
    public bool HasPermission(int rangeId) => RangePermissions.Any(p => p.RangeId == rangeId);
    
    /// <summary>UC-05: Check if group's compliance is current and booking is enabled.</summary>
    public bool IsComplianceCurrent(DateTime now, List<ComplianceDocument> docs)
    {
        if (Status != AccountStatus.Active) return false;
        var requiredDocs = docs.Where(d => d.GroupId == Id && d.Status == DocumentStatus.Verified).ToList();
        // All required types must be verified and not expired
        return requiredDocs.Any(d => d.DocumentType == ComplianceDocumentType.CertificateOfInsurance && d.ExpirationDate > now)
            && requiredDocs.Any(d => d.DocumentType == ComplianceDocumentType.UserGroupAgreement && d.ExpirationDate > now);
    }
}

/// <summary>UC-05: Automated compliance monitoring event (reminders and expiration).</summary>
public class ComplianceExpirationEvent
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int DocumentId { get; set; }
    /// <summary>"Reminder-60", "Reminder-30", "Reminder-7", "Expired".</summary>
    public string EventType { get; set; } = "";
    public DateTime DocumentExpirationDate { get; set; }
    public DateTime EventTriggeredAt { get; set; }
    /// <summary>Track if notification was sent.</summary>
    public bool NotificationSent { get; set; }
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
