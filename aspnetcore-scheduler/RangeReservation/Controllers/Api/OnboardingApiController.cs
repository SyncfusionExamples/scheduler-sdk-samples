using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RangeReservationPoc.Models;
using RangeReservationPoc.Services;

namespace RangeReservationPoc.Controllers.Api;

/// <summary>
/// UC-01: Apply for Corporate Account (Prospective Corporate Primary User)
/// UC-02: Review & Approve Corporate Onboarding Application (Admin)
/// UC-03: Upload Compliance Documentation (Corporate Primary User)
/// UC-04: Verify Insurance & Compliance Documents (Admin)
/// UC-05: Automated Compliance Blocking & Expiration Warnings (System)
/// </summary>
[ApiController]
[Route("api")]
public class OnboardingApiController : ControllerBase
{
    private readonly InMemoryDataStore _store;
    private readonly OnboardingService _service;

    public OnboardingApiController(InMemoryDataStore store, OnboardingService service)
    {
        _store = store;
        _service = service;
    }

    private AppUser? Me => User.CurrentUser(_store);

    // ==================== UC-01: Apply for Corporate Account ====================

    /// <summary>
    /// UC-01: Prospective corporate user submits an initial application.
    /// No authentication required for initial submission.
    /// </summary>
    [HttpPost("onboarding/apply")]
    [AllowAnonymous]
    public ActionResult<OnboardingApplicationDto> Apply([FromBody] OnboardingApplicationInput input)
    {
        var (ok, app, errors) = _service.SubmitApplication(input);

        if (!ok)
            return BadRequest(new { Errors = errors });

        return Ok(new OnboardingApplicationDto
        {
            Id = app!.Id,
            CompanyName = app.CompanyName,
            CompanyType = app.CompanyType,
            PrimaryContactName = app.PrimaryContactName,
            PrimaryContactEmail = app.PrimaryContactEmail,
            Status = app.Status.ToString(),
            CreatedAt = app.CreatedAt
        });
    }

    // ==================== UC-02: Review & Approve Corporate Onboarding Application (Admin) ====================

    /// <summary>UC-02: Admin views all pending onboarding applications.</summary>
    [HttpGet("admin/onboarding/applications")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<List<OnboardingApplicationDto>> ListApplications([FromQuery] string? status)
    {
        if (Me is not { } me) return Unauthorized();

        var apps = _store.GetAllApplications();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AccountStatus>(status, out var statusEnum))
            apps = apps.Where(a => a.Status == statusEnum).ToList();

        var dtos = apps.Select(a => new OnboardingApplicationDto
        {
            Id = a.Id,
            CompanyName = a.CompanyName,
            CompanyType = a.CompanyType,
            PrimaryContactName = a.PrimaryContactName,
            PrimaryContactEmail = a.PrimaryContactEmail,
            Status = a.Status.ToString(),
            Tier = a.AssignedTier.HasValue ? TierNames.Display(a.AssignedTier.Value) : null,
            ReviewedBy = a.ReviewedBy,
            ReviewedAt = a.ReviewedAt,
            DenialReason = a.DenialReason,
            AdminNotes = a.AdminNotes,
            CreatedAt = a.CreatedAt
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>UC-02: Admin gets detail of a specific onboarding application.</summary>
    [HttpGet("admin/onboarding/applications/{applicationId:int}")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<OnboardingApplicationDto> GetApplicationDetail(int applicationId)
    {
        if (Me is not { } me) return Unauthorized();

        var app = _store.GetApplication(applicationId);
        if (app is null)
            return NotFound();

        return Ok(new OnboardingApplicationDto
        {
            Id = app.Id,
            CompanyName = app.CompanyName,
            CompanyType = app.CompanyType,
            PrimaryContactName = app.PrimaryContactName,
            PrimaryContactEmail = app.PrimaryContactEmail,
            Status = app.Status.ToString(),
            Tier = app.AssignedTier.HasValue ? TierNames.Display(app.AssignedTier.Value) : null,
            ReviewedBy = app.ReviewedBy,
            ReviewedAt = app.ReviewedAt,
            DenialReason = app.DenialReason,
            AdminNotes = app.AdminNotes,
            CreatedAt = app.CreatedAt
        });
    }

    /// <summary>UC-02: Admin reviews and approves or denies the onboarding application.</summary>
    [HttpPost("admin/onboarding/applications/{applicationId:int}/review")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult ReviewApplication(int applicationId, [FromBody] OnboardingReviewInput input)
    {
        if (Me is not { } me) return Unauthorized();

        input.ApplicationId = applicationId;
        var (ok, errors) = _service.ReviewApplication(input, me);

        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    // ==================== UC-03: Upload Compliance Documentation ====================

    /// <summary>UC-03: Corporate user uploads compliance document (COI, agreement, etc.).</summary>
    [HttpPost("compliance/documents/upload")]
    [Authorize(Roles = Roles.Corporate)]
    public ActionResult<ComplianceDocumentDto> UploadDocument([FromBody] ComplianceDocumentUploadInput input)
    {
        if (Me is not { } me) return Unauthorized();

        // Validate that the user can upload for this group
        if (me.GroupId != input.GroupId)
            return Forbid("You can only upload documents for your own corporate group.");

        var (ok, doc, errors) = _service.UploadComplianceDocument(input, me);

        if (!ok)
            return BadRequest(new { Errors = errors });

        var now = DateTime.UtcNow;
        return Ok(new ComplianceDocumentDto
        {
            Id = doc!.Id,
            GroupId = doc.GroupId,
            DocumentType = doc.DocumentType.ToString(),
            FileName = doc.FileName,
            Status = doc.Status.ToString(),
            ExpirationDate = doc.ExpirationDate,
            UploadedAt = doc.UploadedAt,
            VerifiedBy = doc.VerifiedBy,
            VerifiedAt = doc.VerifiedAt,
            ReviewNotes = doc.ReviewNotes,
            DaysUntilExpiration = doc.ExpirationDate.HasValue ? (int)(doc.ExpirationDate.Value - now).TotalDays : null,
            ComplianceStatus = "Pending Review"
        });
    }

    /// <summary>UC-03, UC-05: Corporate user views their compliance documents and status.</summary>
    [HttpGet("compliance/documents")]
    [Authorize(Roles = Roles.Corporate)]
    public ActionResult<CorporateGroupOnboardingDto> GetComplianceStatus()
    {
        if (Me is not { } me || !me.GroupId.HasValue) return Unauthorized();

        var status = _service.GetGroupOnboardingStatus(me.GroupId.Value);
        return Ok(status);
    }

    // ==================== UC-04: Verify Insurance & Compliance Documents (Admin) ====================

    /// <summary>UC-04: Admin views all pending compliance documents requiring verification.</summary>
    [HttpGet("admin/compliance/documents")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<List<ComplianceDocumentDto>> ListPendingDocuments([FromQuery] string? status)
    {
        if (Me is not { } me) return Unauthorized();

        var docs = _store.GetAllComplianceDocuments();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<DocumentStatus>(status, out var statusEnum))
            docs = docs.Where(d => d.Status == statusEnum).ToList();

        var now = DateTime.UtcNow;
        var dtos = docs.Select(d => new ComplianceDocumentDto
        {
            Id = d.Id,
            GroupId = d.GroupId,
            DocumentType = d.DocumentType.ToString(),
            FileName = d.FileName,
            Status = d.Status.ToString(),
            ExpirationDate = d.ExpirationDate,
            UploadedAt = d.UploadedAt,
            VerifiedBy = d.VerifiedBy,
            VerifiedAt = d.VerifiedAt,
            ReviewNotes = d.ReviewNotes,
            DaysUntilExpiration = d.ExpirationDate.HasValue ? (int)(d.ExpirationDate.Value - now).TotalDays : null,
            ComplianceStatus = GetComplianceStatus(d, now)
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>UC-04: Admin gets detail of a specific compliance document.</summary>
    [HttpGet("admin/compliance/documents/{documentId:int}")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<ComplianceDocumentDto> GetDocumentDetail(int documentId)
    {
        if (Me is not { } me) return Unauthorized();

        var doc = _store.GetComplianceDocument(documentId);
        if (doc is null)
            return NotFound();

        var now = DateTime.UtcNow;
        return Ok(new ComplianceDocumentDto
        {
            Id = doc.Id,
            GroupId = doc.GroupId,
            DocumentType = doc.DocumentType.ToString(),
            FileName = doc.FileName,
            Status = doc.Status.ToString(),
            ExpirationDate = doc.ExpirationDate,
            UploadedAt = doc.UploadedAt,
            VerifiedBy = doc.VerifiedBy,
            VerifiedAt = doc.VerifiedAt,
            ReviewNotes = doc.ReviewNotes,
            DaysUntilExpiration = doc.ExpirationDate.HasValue ? (int)(doc.ExpirationDate.Value - now).TotalDays : null,
            ComplianceStatus = GetComplianceStatus(doc, now)
        });
    }

    /// <summary>UC-04: Admin verifies uploaded compliance document, setting expiration date and coverage details.</summary>
    [HttpPost("admin/compliance/documents/{documentId:int}/verify")]
    [Authorize(Roles = Roles.Admin)]
    public IActionResult VerifyDocument(int documentId, [FromBody] ComplianceDocumentVerifyInput input)
    {
        if (Me is not { } me) return Unauthorized();

        input.DocumentId = documentId;
        var (ok, errors) = _service.VerifyComplianceDocument(input, me);

        return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
    }

    // ==================== UC-05: Automated Compliance Blocking & Expiration Warnings ====================

    /// <summary>
    /// UC-05: Background job endpoint - called periodically to check expiration dates and send reminders.
    /// In production, trigger via Hangfire, Azure Functions, or similar scheduler.
    /// </summary>
    [HttpPost("admin/compliance/check-expirations")]
    [Authorize(Roles = Roles.Admin)]
    public ActionResult<object> CheckExpirations()
    {
        if (Me is not { } me) return Unauthorized();

        var (remindersCount, blocksCount) = _service.CheckAndHandleExpirations();

        return Ok(new
        {
            RemindersScheduled = remindersCount,
            BlocksApplied = blocksCount,
            Message = $"Checked compliance expirations: {remindersCount} reminders scheduled, {blocksCount} accounts blocked."
        });
    }

    // ==================== Helpers ====================

    private string GetComplianceStatus(ComplianceDocument doc, DateTime now)
    {
        return doc.Status switch
        {
            DocumentStatus.Verified when doc.ExpirationDate.HasValue && doc.ExpirationDate.Value <= now => "Expired",
            DocumentStatus.Verified when doc.ExpirationDate.HasValue && (doc.ExpirationDate.Value - now).TotalDays <= 7 => "Expiring Soon",
            DocumentStatus.Verified => "Verified",
            DocumentStatus.PendingReview => "Pending Review",
            DocumentStatus.Rejected => "Rejected",
            _ => doc.Status.ToString()
        };
    }
}
