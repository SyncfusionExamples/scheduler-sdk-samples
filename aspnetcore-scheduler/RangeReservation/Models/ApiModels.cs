namespace RangeReservationPoc.Models;

/// <summary>Event shape sent to the Syncfusion Scheduler (reservations + blackout blocks).</summary>
public class ScheduleEventDto
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
    public bool IsBlock { get; set; }
    public bool IsReadonly { get; set; }

    /// <summary>"reservation", "masked" (another group's booking) or "blackout".</summary>
    public string Kind { get; set; } = "reservation";
    public bool IsOwn { get; set; }
    public string? Status { get; set; }
    public string? RequestId { get; set; }
    public int? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? Tier { get; set; }
    public string? SubmittedByName { get; set; }
    public int? Headcount { get; set; }
    public List<string>? FirearmTypes { get; set; }
    public string? TargetType { get; set; }
    public int? TargetDistanceYards { get; set; }
    public int? TargetCount { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }
    public bool AdminOverride { get; set; }
}

public class ReservationInput
{
    public int? Id { get; set; }
    public string? Subject { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? RecurrenceID { get; set; }
    public string? RecurrenceException { get; set; }
    public int? FacilityId { get; set; }
    public int RangeId { get; set; }
    /// <summary>Admin only: book on behalf of this corporate group.</summary>
    public int? GroupId { get; set; }
    public int? Headcount { get; set; }
    public List<string>? FirearmTypes { get; set; }
    public string? TargetType { get; set; }
    public int? TargetDistanceYards { get; set; }
    public int? TargetCount { get; set; }
    public bool RsoRequested { get; set; }
    public string? Description { get; set; }
}

/// <summary>One cart transaction: validated together and submitted all-or-nothing.</summary>
public class ReservationBatchRequest
{
    public List<ReservationInput> Added { get; set; } = new();
    public List<ReservationInput> Changed { get; set; } = new();
    public List<ReservationInput> Deleted { get; set; } = new();
    /// <summary>Admin only: bypass tier policies (hard rules such as conflicts, blackouts, Monday rule still apply).</summary>
    public bool AdminOverride { get; set; }
    /// <summary>Validate only - nothing is saved.</summary>
    public bool DryRun { get; set; }
}

public class BatchResult
{
    public bool Success => Errors.Count == 0;
    public bool DryRun { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<int> SavedIds { get; set; } = new();
    public string? RequestId { get; set; }
    public string? Status { get; set; }
    public int OccurrencesValidated { get; set; }
}

/// <summary>A single rule violation, tagged with the policy check it belongs to (shown in the admin review queue).</summary>
public record RuleViolation(string Check, string Message);

public class BlackoutInput
{
    public int? Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? RecurrenceRule { get; set; }
    public int? FacilityId { get; set; }
    public int? RangeId { get; set; }
    public string? Reason { get; set; }
}

public class DenyInput
{
    public string Reason { get; set; } = "";
}

public class GroupUpdateInput
{
    public CustomerTier Tier { get; set; }
    public List<RangePermissionInput> RangePermissions { get; set; } = new();
}

public class RangePermissionInput
{
    public int RangeId { get; set; }
    public bool Granted { get; set; }
    public string? VerificationNote { get; set; }
}

public class SettingsInput
{
    public List<TierPolicy> Policies { get; set; } = new();
    public FacilitySettings Settings { get; set; } = new();
}

// ==================== UC-01, UC-02: Onboarding ======================

/// <summary>UC-01: Submit corporate account application.</summary>
public class OnboardingApplicationInput
{
    public string CompanyName { get; set; } = "";
    public string CompanyType { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string PrimaryContactName { get; set; } = "";
    public string PrimaryContactEmail { get; set; } = "";
    public string PrimaryContactPhone { get; set; } = "";
    public string Address { get; set; } = "";
}

/// <summary>UC-01: Onboarding application summary for prospective users and admin review.</summary>
public class OnboardingApplicationDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyType { get; set; } = "";
    public string PrimaryContactName { get; set; } = "";
    public string PrimaryContactEmail { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Tier { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? DenialReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? AdminNotes { get; set; }
}

/// <summary>UC-02: Admin approves or denies onboarding application.</summary>
public class OnboardingReviewInput
{
    public int ApplicationId { get; set; }
    public bool Approve { get; set; }
    public int? TierId { get; set; }  // CustomerTier as int
    public string? DenialReason { get; set; }
    public string? AdminNotes { get; set; }
}

// ==================== UC-03, UC-04, UC-05: Compliance ======================

/// <summary>UC-03: Upload compliance document.</summary>
public class ComplianceDocumentUploadInput
{
    public int GroupId { get; set; }
    public int DocumentType { get; set; }  // ComplianceDocumentType enum as int
    public string FileName { get; set; } = "";
    public string Base64Content { get; set; } = "";
}

/// <summary>UC-04: Admin verifies uploaded document and sets expiration.</summary>
public class ComplianceDocumentVerifyInput
{
    public int DocumentId { get; set; }
    public bool Verified { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? PolicyNumber { get; set; }
    public string? PolicyType { get; set; }
    public string? CoverageLimits { get; set; }
    public string? CertificateHolder { get; set; }
    public string? ReviewNotes { get; set; }
}

/// <summary>UC-03, UC-04, UC-05: Compliance document summary for dashboard.</summary>
public class ComplianceDocumentDto
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string DocumentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ExpirationDate { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? ReviewNotes { get; set; }
    /// <summary>UC-05: Days until expiration, or negative if already expired.</summary>
    public int? DaysUntilExpiration { get; set; }
    /// <summary>UC-05: Compliance status indicator for the dashboard.</summary>
    public string ComplianceStatus { get; set; } = "";  // "Verified", "Expiring Soon", "Expired", "Pending Review"
}

/// <summary>UC-01, UC-02, UC-05: Corporate group onboarding status for dashboard.</summary>
public class CorporateGroupOnboardingDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "";
    public string AccountStatus { get; set; } = "";
    public string? ComplianceSummary { get; set; }  // e.g., "Verified through 2025-12-31" or "COI expired on 2024-11-15"
    public List<ComplianceDocumentDto> Documents { get; set; } = new();
    public bool BookingEnabled { get; set; }
    public string? BookingBlockedReason { get; set; }  // UC-05: e.g., "Compliance expired"
}

/// <summary>Review queue / dashboard row - one request (cart transaction).</summary>
public class RequestSummary
{
    public string RequestId { get; set; } = "";
    public string Status { get; set; } = "";
    public int? GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public string Tier { get; set; } = "";
    public string SubmittedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string RangeName { get; set; } = "";
    public string FacilityName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Kind { get; set; } = "";             // "Single slot", "Multi-date (3 dates)", "Recurring series (10)"
    public List<string> Dates { get; set; } = new();
    public int Headcount { get; set; }
    public List<string> FirearmTypes { get; set; } = new();
    public string Target { get; set; } = "";
    public bool RsoRequested { get; set; }
    public string? RecurrenceRule { get; set; }
    public string? DenialReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public List<ComplianceCheck> Checks { get; set; } = new();
    public bool CanCancel { get; set; }
}

public record ComplianceCheck(string Name, bool Passed, List<string> Messages);
