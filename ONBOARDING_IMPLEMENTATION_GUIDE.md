# Corporate Onboarding & Compliance Management Implementation Guide

## Overview

This document describes how the Range Reservation POC implements **UC-01 through UC-05** (Onboarding & Compliance Management) using **Syncfusion Scheduler** as the foundation and ASP.NET Core services for business logic.

The POC demonstrates:
- **UC-01**: Corporate account application submission
- **UC-02**: Admin review, tier assignment, and approval/denial workflow
- **UC-03**: Document upload (COI, user agreements)
- **UC-04**: Admin verification with expiration date tracking
- **UC-05**: Automated compliance monitoring, expiration reminders, and booking blocks

---

## Architecture Overview

### Domain Models

Three new domain entities manage the onboarding lifecycle:

#### 1. **OnboardingApplication** (`Models/DomainModels.cs`)

```csharp
public class OnboardingApplication
{
    public int Id { get; set; }
    public string CompanyName { get; set; }          // UC-01: Company name
    public string CompanyType { get; set; }          // UC-01: Type (LE, Commercial, Competitor, etc.)
    public string RegistrationNumber { get; set; }   // UC-01: Tax ID, business registration
    public string PrimaryContactName { get; set; }   // UC-01: Primary user assignment
    public string PrimaryContactEmail { get; set; }  // UC-01: Contact info
    public string PrimaryContactPhone { get; set; }  // UC-01: Contact info
    public string Address { get; set; }              // UC-01: Company address
    
    // UC-02: Admin review
    public CustomerTier? AssignedTier { get; set; }
    public string? AdminNotes { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? DenialReason { get; set; }
    
    // Lifecycle state
    public AccountStatus Status { get; set; }        // Pending → PreApproved → Active / Denied
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public enum AccountStatus
{
    Pending,       // Awaiting admin review
    PreApproved,   // Approved, awaiting compliance docs
    Active,        // All compliance verified
    Suspended,     // Compliance expired
    Denied,        // Rejected
    Archived       // Deactivated
}
```

**Flow:**
```
Prospective user submits application (UC-01)
         ↓
Admin reviews & assigns tier (UC-02)
         ↓
If approved: Status = PreApproved → Corporate user uploads docs (UC-03)
If denied: Status = Denied → Notification sent
```

#### 2. **ComplianceDocument** (`Models/DomainModels.cs`)

```csharp
public class ComplianceDocument
{
    public int Id { get; set; }
    public int GroupId { get; set; }                 // Link to CorporateGroup
    public ComplianceDocumentType DocumentType { get; set; }  // COI, Agreement, Other
    public string FileName { get; set; }             // UC-03: File name
    public string? DocumentPath { get; set; }        // UC-03: Storage location
    
    // UC-04: COI metadata
    public string? PolicyNumber { get; set; }
    public string? PolicyType { get; set; }          // "Occurrence" (required), not "Claims-made"
    public string? CoverageLimits { get; set; }      // Validated: 1M/2M minimum
    public string? CertificateHolder { get; set; }
    
    // UC-04: Verification
    public DateTime? ExpirationDate { get; set; }    // Admin sets; typically 1 year out
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? ReviewNotes { get; set; }
    
    // Lifecycle state
    public DocumentStatus Status { get; set; }       // PendingReview → Verified / Rejected / Expired
    public DateTime UploadedAt { get; set; }
}

public enum ComplianceDocumentType
{
    CertificateOfInsurance,    // COI - required
    UserGroupAgreement,         // Signed agreement/waiver - required
    Other                       // Optional supplementary
}

public enum DocumentStatus
{
    PendingUpload,     // Awaiting upload
    PendingReview,     // Uploaded, admin review in progress
    Verified,          // Approved, active through expiration
    Expired,           // Past expiration date
    Rejected           // Admin rejected, needs resubmission
}
```

**Flow:**
```
Corporate user uploads COI + Agreement (UC-03)
         ↓
Documents enter: Status = PendingReview
         ↓
Admin verifies (UC-04):
- Checks occurrence policy type
- Validates 1M/2M coverage minimums
- Confirms certificate holder
- Sets expiration date (typically +1 year)
         ↓
If verified: Status = Verified → Account activated (if all docs verified)
If rejected: Status = Rejected → User re-uploads
```

#### 3. **ComplianceExpirationEvent** (`Models/DomainModels.cs`)

```csharp
public class ComplianceExpirationEvent
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int DocumentId { get; set; }
    public string EventType { get; set; }            // "Reminder-60", "Reminder-30", "Reminder-7", "Expired"
    public DateTime DocumentExpirationDate { get; set; }
    public DateTime EventTriggeredAt { get; set; }
    public bool NotificationSent { get; set; }       // Track to avoid duplicates
}
```

**Used by UC-05:** Background job creates an event record for each reminder/expiration, linked to the document and group. Prevents duplicate notifications.

#### Updated: **CorporateGroup** (extended with compliance fields)

```csharp
public class CorporateGroup
{
    // Existing fields...
    
    // UC-01, UC-02: Link to application for audit trail
    public int? OnboardingApplicationId { get; set; }
    
    // UC-01, UC-02, UC-05: Current account status
    public AccountStatus Status { get; set; }        // Active, Suspended, etc.
    
    // UC-05: Track when compliance was last verified
    public DateTime? LastComplianceVerifiedAt { get; set; }
    
    // UC-05: Helper method to check compliance currency
    public bool IsComplianceCurrent(DateTime now, List<ComplianceDocument> docs)
    {
        if (Status != AccountStatus.Active) return false;
        return docs.Where(d => d.GroupId == Id && d.Status == DocumentStatus.Verified)
            .Any(d => d.DocumentType == ComplianceDocumentType.CertificateOfInsurance && d.ExpirationDate > now)
            && docs.Where(d => d.GroupId == Id && d.Status == DocumentStatus.Verified)
            .Any(d => d.DocumentType == ComplianceDocumentType.UserGroupAgreement && d.ExpirationDate > now);
    }
}
```

---

## API Endpoints

### UC-01: Apply for Corporate Account

**POST** `/api/onboarding/apply` *(Public - no auth required)*

```csharp
// Request
{
  "companyName": "Desert Valley Police Department",
  "companyType": "Law Enforcement",
  "registrationNumber": "LE-0001234",
  "primaryContactName": "Lt. Maria Ramirez",
  "primaryContactEmail": "m.ramirez@dvpd.example",
  "primaryContactPhone": "(602) 555-0101",
  "address": "123 Main St, Phoenix, AZ 85001"
}

// Response (201 Created)
{
  "id": 1001,
  "companyName": "Desert Valley Police Department",
  "companyType": "Law Enforcement",
  "primaryContactName": "Lt. Maria Ramirez",
  "primaryContactEmail": "m.ramirez@dvpd.example",
  "status": "Pending",
  "tier": null,
  "createdAt": "2025-06-15T10:30:00Z"
}
```

**Implementation** — `Services/OnboardingService.cs : SubmitApplication()`

```csharp
public (bool Ok, OnboardingApplication? Application, List<string> Errors) SubmitApplication(
    OnboardingApplicationInput input)
{
    var errors = new List<string>();
    
    // Validate all required fields
    if (string.IsNullOrWhiteSpace(input.CompanyName))
        errors.Add("Company name is required.");
    // ... more validations ...
    
    if (errors.Count > 0)
        return (false, null, errors);
    
    var app = new OnboardingApplication
    {
        CompanyName = input.CompanyName.Trim(),
        CompanyType = input.CompanyType.Trim(),
        PrimaryContactName = input.PrimaryContactName.Trim(),
        PrimaryContactEmail = input.PrimaryContactEmail.Trim(),
        // ... copy other fields ...
        Status = AccountStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
    
    _store.AddApplication(app);
    
    // Send confirmation email to prospective user
    _notifications.SendNotification(
        toEmail: app.PrimaryContactEmail,
        subject: $"Application Received: {app.CompanyName}",
        body: $"Your application ID is {app.Id}. Awaiting review..."
    );
    
    return (true, app, new());
}
```

**API Location** — `Controllers/Api/OnboardingApiController.cs : Apply()`

```csharp
[HttpPost("onboarding/apply")]
[AllowAnonymous]
public ActionResult<OnboardingApplicationDto> Apply([FromBody] OnboardingApplicationInput input)
{
    var (ok, app, errors) = _service.SubmitApplication(input);
    if (!ok)
        return BadRequest(new { Errors = errors });
    return Ok(MapToDto(app!));
}
```

---

### UC-02: Review & Approve Corporate Onboarding Application (Admin)

**GET** `/api/admin/onboarding/applications` *(Admin only)*

Lists all applications by status (Pending, PreApproved, Active, Denied, etc.).

**POST** `/api/admin/onboarding/applications/{applicationId}/review` *(Admin only)*

```csharp
// Request
{
  "approve": true,
  "tierId": 4,  // CustomerTier.Tier4 (Law Enforcement)
  "denialReason": null,
  "adminNotes": "Verified with LEO directory, good standing"
}

// Response (204 No Content on success)
// On error (422 Unprocessable Entity):
{
  "errors": [
    "Tier assignment is required for approval."
  ]
}
```

**Implementation** — `Services/OnboardingService.cs : ReviewApplication()`

```csharp
public (bool Ok, List<string> Errors) ReviewApplication(
    OnboardingReviewInput input, 
    AppUser admin)
{
    var errors = new List<string>();
    
    var app = _store.GetApplication(input.ApplicationId);
    if (app is null)
    {
        errors.Add("Application not found.");
        return (false, errors);
    }
    
    if (app.Status != AccountStatus.Pending)
    {
        errors.Add($"Application is already {app.Status}; cannot review.");
        return (false, errors);
    }
    
    app.ReviewedBy = admin.Name;
    app.ReviewedAt = DateTime.UtcNow;
    app.AdminNotes = input.AdminNotes;
    
    if (input.Approve)
    {
        if (!input.TierId.HasValue || !Enum.IsDefined(typeof(CustomerTier), input.TierId.Value))
        {
            errors.Add("Tier assignment is required for approval.");
            return (false, errors);
        }
        
        var tier = (CustomerTier)input.TierId.Value;
        app.AssignedTier = tier;
        app.Status = AccountStatus.PreApproved;
        
        // Create the CorporateGroup record
        var group = new CorporateGroup
        {
            Name = app.CompanyName,
            Tier = tier,
            ContactEmail = app.PrimaryContactEmail,
            OnboardingApplicationId = app.Id,
            Status = AccountStatus.PreApproved  // Booking locked until compliance verified
        };
        
        _store.AddGroup(group);
        
        // Send approval notification with next steps
        _notifications.SendNotification(
            toEmail: app.PrimaryContactEmail,
            subject: $"Application Approved: {app.CompanyName}",
            body: $"""
                Congratulations! Your application is approved as {TierNames.Display(tier)}.
                
                Next Steps:
                1. Log in to the portal
                2. Upload Certificate of Insurance (Occurrence, 1M/2M minimum)
                3. Upload signed User Group Agreement
                
                Once verified, your account will be activated for booking.
                """
        );
    }
    else
    {
        app.Status = AccountStatus.Denied;
        app.DenialReason = input.DenialReason ?? "Application denied.";
        
        // Send denial notification
        _notifications.SendNotification(
            toEmail: app.PrimaryContactEmail,
            subject: $"Application Denied: {app.CompanyName}",
            body: $"Reason: {app.DenialReason}"
        );
    }
    
    _store.UpdateApplication(app);
    return (true, new());
}
```

**API Location** — `Controllers/Api/OnboardingApiController.cs : ReviewApplication()`

```csharp
[HttpPost("admin/onboarding/applications/{applicationId:int}/review")]
[Authorize(Roles = Roles.Admin)]
public IActionResult ReviewApplication(int applicationId, [FromBody] OnboardingReviewInput input)
{
    if (Me is not { } me) return Unauthorized();
    
    input.ApplicationId = applicationId;
    var (ok, errors) = _service.ReviewApplication(input, me);
    
    return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

---

### UC-03: Upload Compliance Documentation

**POST** `/api/compliance/documents/upload` *(Corporate user only)*

```csharp
// Request
{
  "groupId": 1,
  "documentType": 0,  // ComplianceDocumentType.CertificateOfInsurance
  "fileName": "COI_DVPD_2025.pdf",
  "base64Content": "JVBERi0xLjQKJeLjz9M...truncated..."
}

// Response (201 Created)
{
  "id": 2001,
  "groupId": 1,
  "documentType": "CertificateOfInsurance",
  "fileName": "COI_DVPD_2025.pdf",
  "status": "PendingReview",
  "expirationDate": null,
  "uploadedAt": "2025-06-15T10:45:00Z",
  "verifiedBy": null,
  "complianceStatus": "Pending Review"
}
```

**Implementation** — `Services/OnboardingService.cs : UploadComplianceDocument()`

```csharp
public (bool Ok, ComplianceDocument? Document, List<string> Errors) UploadComplianceDocument(
    ComplianceDocumentUploadInput input,
    AppUser uploader)
{
    var errors = new List<string>();
    
    var group = _store.GetGroup(input.GroupId);
    if (group is null)
    {
        errors.Add("Corporate group not found.");
        return (false, null, errors);
    }
    
    // Only allow uploads if PreApproved or Active
    if (group.Status != AccountStatus.PreApproved && group.Status != AccountStatus.Active)
    {
        errors.Add($"Compliance documents cannot be uploaded when status is {group.Status}.");
        return (false, null, errors);
    }
    
    // Validate document type & content
    if (!Enum.IsDefined(typeof(ComplianceDocumentType), input.DocumentType))
    {
        errors.Add("Invalid document type.");
        return (false, null, errors);
    }
    
    if (string.IsNullOrWhiteSpace(input.FileName) || string.IsNullOrWhiteSpace(input.Base64Content))
    {
        errors.Add("File name and content are required.");
        return (false, null, errors);
    }
    
    var doc = new ComplianceDocument
    {
        GroupId = input.GroupId,
        DocumentType = (ComplianceDocumentType)input.DocumentType,
        FileName = input.FileName.Trim(),
        DocumentPath = $"uploads/compliance/{input.GroupId}/{Guid.NewGuid()}.pdf",
        Status = DocumentStatus.PendingReview,
        UploadedAt = DateTime.UtcNow
        // Save base64Content to file system or storage service
    };
    
    _store.AddComplianceDocument(doc);
    
    // Notify admin
    _notifications.SendNotification(
        toEmail: "admin@azgfd.example.com",
        subject: $"New Compliance Document: {group.Name}",
        body: $"{group.Name} uploaded {doc.DocumentType} for review."
    );
    
    return (true, doc, new());
}
```

**API Location** — `Controllers/Api/OnboardingApiController.cs : UploadDocument()`

```csharp
[HttpPost("compliance/documents/upload")]
[Authorize(Roles = Roles.Corporate)]
public ActionResult<ComplianceDocumentDto> UploadDocument([FromBody] ComplianceDocumentUploadInput input)
{
    if (Me is not { } me || me.GroupId != input.GroupId)
        return Forbid("You can only upload for your own group.");
    
    var (ok, doc, errors) = _service.UploadComplianceDocument(input, me);
    
    if (!ok)
        return BadRequest(new { Errors = errors });
    
    return Ok(MapToDto(doc!));
}
```

**UC-03: Corporate user views their compliance dashboard**

**GET** `/api/compliance/documents` *(Corporate user only)*

```csharp
// Response
{
  "id": 1,
  "name": "Desert Valley Police Department",
  "tier": "Tier 4",
  "accountStatus": "PreApproved",
  "complianceSummary": null,  // Not yet verified
  "documents": [
    {
      "id": 2001,
      "groupId": 1,
      "documentType": "CertificateOfInsurance",
      "fileName": "COI_DVPD_2025.pdf",
      "status": "PendingReview",
      "expirationDate": null,
      "uploadedAt": "2025-06-15T10:45:00Z",
      "daysUntilExpiration": null,
      "complianceStatus": "Pending Review"
    },
    {
      "id": 2002,
      "documentType": "UserGroupAgreement",
      "fileName": "Agreement_signed_2025.pdf",
      "status": "PendingReview",
      "complianceStatus": "Pending Review"
    }
  ],
  "bookingEnabled": false,
  "bookingBlockedReason": "Compliance documents required for activation"
}
```

---

### UC-04: Verify Insurance & Compliance Documents (Admin)

**GET** `/api/admin/compliance/documents` *(Admin only)*

Lists all documents, optionally filtered by status (PendingReview, Verified, Rejected, Expired).

**GET** `/api/admin/compliance/documents/{documentId}` *(Admin only)*

Returns detailed view with extracted metadata.

**POST** `/api/admin/compliance/documents/{documentId}/verify` *(Admin only)*

```csharp
// Request - Approval
{
  "verified": true,
  "expirationDate": "2026-06-15T00:00:00Z",
  "policyNumber": "AIG-2025-001234",
  "policyType": "Occurrence",
  "coverageLimits": "1M/2M",
  "certificateHolder": "Desert Valley Police Department - Training Division",
  "reviewNotes": "Verified occurrence policy meets 1M/2M minimums. Certificate holder matches."
}

// Response (204 No Content on success)
// On error (422 Unprocessable Entity):
{
  "errors": [
    "Policy type must be 'Occurrence'; Claims-made policies are not accepted.",
    "Coverage must meet minimum 1M/2M requirements."
  ]
}
```

**Implementation** — `Services/OnboardingService.cs : VerifyComplianceDocument()`

```csharp
public (bool Ok, List<string> Errors) VerifyComplianceDocument(
    ComplianceDocumentVerifyInput input,
    AppUser admin)
{
    var errors = new List<string>();
    
    var doc = _store.GetComplianceDocument(input.DocumentId);
    if (doc is null)
    {
        errors.Add("Document not found.");
        return (false, errors);
    }
    
    if (doc.Status != DocumentStatus.PendingReview)
    {
        errors.Add($"Only PendingReview documents can be verified. Current: {doc.Status}");
        return (false, errors);
    }
    
    var group = _store.GetGroup(doc.GroupId);
    if (group is null)
    {
        errors.Add("Associated group not found.");
        return (false, errors);
    }
    
    if (input.Verified)
    {
        // Validate COI specifics
        if (doc.DocumentType == ComplianceDocumentType.CertificateOfInsurance)
        {
            if (string.IsNullOrWhiteSpace(input.PolicyNumber))
                errors.Add("Policy number required.");
            
            if (input.PolicyType != "Occurrence")
                errors.Add("Policy must be Occurrence type (not Claims-made).");
            
            // Validate 1M/2M minimum
            if (!ValidateCoverageLimits(input.CoverageLimits))
                errors.Add("Coverage must meet 1M/2M minimum.");
        }
        
        if (!input.ExpirationDate.HasValue || input.ExpirationDate.Value <= DateTime.UtcNow)
            errors.Add("Expiration date must be set to a future date.");
        
        if (errors.Count > 0)
            return (false, errors);
        
        // Mark as verified
        doc.Status = DocumentStatus.Verified;
        doc.VerifiedBy = admin.Name;
        doc.VerifiedAt = DateTime.UtcNow;
        doc.ExpirationDate = input.ExpirationDate.Value;
        doc.PolicyNumber = input.PolicyNumber;
        doc.PolicyType = input.PolicyType;
        doc.CoverageLimits = input.CoverageLimits;
        doc.CertificateHolder = input.CertificateHolder;
        doc.ReviewNotes = input.ReviewNotes;
        
        _store.UpdateComplianceDocument(doc);
        
        // Check if all required docs now verified
        var allDocs = _store.GetComplianceDocuments(doc.GroupId);
        var requiredTypes = new[]
        {
            ComplianceDocumentType.CertificateOfInsurance,
            ComplianceDocumentType.UserGroupAgreement
        };
        
        var allVerified = requiredTypes.All(type =>
            allDocs.Any(d => d.DocumentType == type 
                && d.Status == DocumentStatus.Verified 
                && d.ExpirationDate > DateTime.UtcNow)
        );
        
        // If all verified and account PreApproved → Activate
        if (allVerified && group.Status == AccountStatus.PreApproved)
        {
            group.Status = AccountStatus.Active;
            group.LastComplianceVerifiedAt = DateTime.UtcNow;
            _store.UpdateGroup(group);
            
            _notifications.SendNotification(
                toEmail: group.ContactEmail,
                subject: $"Account Activated: {group.Name}",
                body: $"""
                    Your corporate account is now fully activated!
                    Booking is enabled through {doc.ExpirationDate:yyyy-MM-dd}.
                    
                    Log in to your portal to start booking range slots.
                    """
            );
        }
    }
    else
    {
        // Rejection: document must be re-uploaded
        doc.Status = DocumentStatus.Rejected;
        doc.VerifiedBy = admin.Name;
        doc.VerifiedAt = DateTime.UtcNow;
        doc.ReviewNotes = input.ReviewNotes;
        
        _store.UpdateComplianceDocument(doc);
        
        _notifications.SendNotification(
            toEmail: group.ContactEmail,
            subject: $"Compliance Document Rejected: {group.Name}",
            body: $"""
                Document: {doc.FileName}
                Reason: {input.ReviewNotes}
                
                Please upload a corrected version.
                """
        );
    }
    
    return (true, new());
}
```

**API Location** — `Controllers/Api/OnboardingApiController.cs : VerifyDocument()`

```csharp
[HttpPost("admin/compliance/documents/{documentId:int}/verify")]
[Authorize(Roles = Roles.Admin)]
public IActionResult VerifyDocument(int documentId, [FromBody] ComplianceDocumentVerifyInput input)
{
    if (Me is not { } me) return Unauthorized();
    
    input.DocumentId = documentId;
    var (ok, errors) = _service.VerifyComplianceDocument(input, me);
    
    return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

---

### UC-05: Automated Compliance Blocking & Expiration Warnings

**POST** `/api/admin/compliance/check-expirations` *(Admin only - trigger via scheduled job)*

```csharp
// Response
{
  "remindersScheduled": 3,
  "blocksApplied": 1,
  "message": "Checked compliance expirations: 3 reminders scheduled, 1 accounts blocked."
}
```

**Implementation** — `Services/OnboardingService.cs : CheckAndHandleExpirations()`

```csharp
public (int RemindersCount, int ExpirationBlocksCount) CheckAndHandleExpirations()
{
    int remindersCount = 0;
    int blocksCount = 0;
    var now = DateTime.UtcNow;
    
    var allGroups = _store.GetAllGroups();
    var allDocs = _store.GetAllComplianceDocuments();
    
    foreach (var group in allGroups)
    {
        if (group.Status != AccountStatus.Active)
            continue;
        
        var groupDocs = allDocs
            .Where(d => d.GroupId == group.Id && d.Status == DocumentStatus.Verified)
            .ToList();
        
        foreach (var doc in groupDocs)
        {
            if (!doc.ExpirationDate.HasValue)
                continue;
            
            var daysUntilExpiration = (doc.ExpirationDate.Value - now).TotalDays;
            
            // Check if expiration event already exists (to prevent duplicates)
            var expiredEvent = _store.GetComplianceExpirationEvent(group.Id, doc.Id, "Expired");
            
            if (daysUntilExpiration < 0 && !expiredEvent)
            {
                // BLOCK BOOKING: Expiration reached
                group.Status = AccountStatus.Suspended;
                _store.UpdateGroup(group);
                
                _store.AddComplianceExpirationEvent(new ComplianceExpirationEvent
                {
                    GroupId = group.Id,
                    DocumentId = doc.Id,
                    EventType = "Expired",
                    DocumentExpirationDate = doc.ExpirationDate.Value,
                    EventTriggeredAt = now,
                    NotificationSent = false
                });
                
                _notifications.SendNotification(
                    toEmail: group.ContactEmail,
                    subject: $"⚠️ URGENT: Booking Blocked - Compliance Expired: {group.Name}",
                    body: $"""
                        YOUR BOOKING ACCESS HAS BEEN BLOCKED
                        
                        Reason: {doc.DocumentType} expired on {doc.ExpirationDate:yyyy-MM-dd}
                        
                        IMMEDIATE ACTION REQUIRED:
                        Upload a renewed {doc.DocumentType} document to restore access.
                        
                        Status: Booking Disabled
                        """
                );
                
                blocksCount++;
            }
            else if (daysUntilExpiration <= 60 && daysUntilExpiration > 0)
            {
                // Send reminders at T-60, T-30, T-7 days
                var thresholds = new[] { 60, 30, 7 };
                
                foreach (var threshold in thresholds)
                {
                    if (Math.Abs(daysUntilExpiration - threshold) < 1.0)
                    {
                        var eventKey = $"Reminder-{threshold}";
                        var reminderEvent = _store.GetComplianceExpirationEvent(group.Id, doc.Id, eventKey);
                        
                        if (reminderEvent == null)
                        {
                            _store.AddComplianceExpirationEvent(new ComplianceExpirationEvent
                            {
                                GroupId = group.Id,
                                DocumentId = doc.Id,
                                EventType = eventKey,
                                DocumentExpirationDate = doc.ExpirationDate.Value,
                                EventTriggeredAt = now,
                                NotificationSent = false
                            });
                            
                            _notifications.SendNotification(
                                toEmail: group.ContactEmail,
                                subject: $"⏰ Compliance Reminder ({threshold} days): {group.Name}",
                                body: $"""
                                    Your {doc.DocumentType} will expire in {threshold} days.
                                    
                                    Expiration Date: {doc.ExpirationDate:yyyy-MM-dd}
                                    
                                    To avoid booking interruption, please prepare and upload a renewed document.
                                    """
                            );
                            
                            remindersCount++;
                        }
                    }
                }
            }
        }
    }
    
    return (remindersCount, blocksCount);
}
```

**Background Job Scheduling (Production):**

In production, use **Hangfire**, **Azure Functions**, or similar to trigger `/api/admin/compliance/check-expirations` daily:

```csharp
// Program.cs - Hangfire setup example
RecurringJob.AddOrUpdate(
    "compliance-expiration-check",
    () => _complianceService.CheckAndHandleExpirations(),
    "0 3 * * *"  // Daily at 3 AM
);
```

---

## Integration with Reservation System

The **ReservationRulesEngine** checks compliance status when validating booking requests:

```csharp
// In ReservationRulesEngine.ApplyRules()
public List<RuleViolation> ApplyRules(
    List<Reservation> bookingsToCreate,
    CorporateGroup? group,
    ShootingRange range,
    List<Reservation> allReservations,
    List<Blackout> allBlackouts,
    AppUser user)
{
    var violations = new List<RuleViolation>();
    
    // ... existing checks ...
    
    // UC-05: Check if group's compliance is current
    if (group is not null)
    {
        var compliantDocs = _store.GetComplianceDocuments(group.Id);
        if (!group.IsComplianceCurrent(_clock.Now, compliantDocs))
        {
            violations.Add(new RuleViolation(
                "Compliance",
                $"Booking blocked: Account compliance expired. " +
                $"Please upload renewed documentation."
            ));
        }
    }
    
    return violations;
}
```

**Booking Block UI Message (UC-05):**

When a user attempts to book but compliance is expired:

```html
<!-- Views/Home/Index.cshtml -->
@if (!isAdmin && !group.IsComplianceCurrent(now, docs))
{
    <div class="alert alert-danger">
        <strong>⛔ Booking Blocked: Insurance Expired on 
            @doc.ExpirationDate?.ToString("MMM dd, yyyy")
        </strong>
        <p>Upload renewed Certificate of Insurance and User Agreement to restore booking access.</p>
        <button onclick="goToCompliance()" class="e-btn e-small">Go to Compliance</button>
    </div>
}
```

---

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ UC-01: Prospective Corporate User                               │
│ POST /api/onboarding/apply                                      │
├─────────────────────────────────────────────────────────────────┤
│ · Submits company details, contact info                         │
│ · OnboardingApplication created: Status = Pending               │
│ · Confirmation email sent to applicant                          │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│ UC-02: AZGFD Administrator                                      │
│ POST /api/admin/onboarding/applications/{id}/review             │
├─────────────────────────────────────────────────────────────────┤
│ · Admin reviews & verifies entity standing                      │
│ · Assigns tier (Tier 1-4)                                       │
│ · If approved: Status = PreApproved, CorporateGroup created     │
│ · If denied: Status = Denied, denial email sent                 │
└──────────────────────┬──────────────────────────────────────────┘
                       │ (approved)
┌──────────────────────▼──────────────────────────────────────────┐
│ UC-03: Corporate Primary User (PreApproved)                     │
│ POST /api/compliance/documents/upload                           │
├─────────────────────────────────────────────────────────────────┤
│ · Uploads COI (occurrence, 1M/2M min) + User Agreement          │
│ · ComplianceDocument created: Status = PendingReview            │
│ · Admin notified of pending review                              │
│ · User sees dashboard: "Pending Review"                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────────┐
│ UC-04: AZGFD Administrator                                      │
│ POST /api/admin/compliance/documents/{id}/verify                │
├─────────────────────────────────────────────────────────────────┤
│ · Inspects COI: policy type, coverage, certificate holder       │
│ · Sets expiration date (+1 year typical)                        │
│ · If all required docs verified:                                │
│     - CorporateGroup Status = Active                            │
│     - Booking unlocked through expiration date                  │
│ · Approval email sent to corporate user                         │
└──────────────────────┬──────────────────────────────────────────┘
                       │ (Active)
┌──────────────────────▼──────────────────────────────────────────┐
│ UC-05: System Background Worker (Daily)                         │
│ POST /api/admin/compliance/check-expirations                    │
├─────────────────────────────────────────────────────────────────┤
│ · Monitors all verified documents' expiration dates             │
│ · At T-60, T-30, T-7 days:                                      │
│     - ComplianceExpirationEvent created (Reminder-N)            │
│     - Email reminder sent to corporate user                     │
│ · On expiration date:                                           │
│     - ComplianceExpirationEvent created (Expired)               │
│     - CorporateGroup Status = Suspended                         │
│     - Booking buttons disabled on portal                        │
│     - Urgent email sent: "Booking Blocked"                      │
│ · Events prevent duplicate notifications                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## Production Checklist

### Database & Persistence

- [ ] Replace `InMemoryDataStore` with EF Core DbContext
- [ ] Add migrations for `OnboardingApplication`, `ComplianceDocument`, `ComplianceExpirationEvent`
- [ ] Add indexes on `GroupId`, `Status`, `ExpirationDate` for query performance
- [ ] Implement audit logging: who reviewed, when, what decision

### File Storage

- [ ] Implement file upload to Azure Blob Storage or on-prem file share
- [ ] Add encryption for uploaded documents (at-rest and in-transit)
- [ ] Implement document lifecycle: 7-year retention, then purge

### Email Notifications

- [ ] Configure SMTP relay (or SendGrid, Azure Communication Services)
- [ ] Template library: application received, approved, denied, reminder, blocked, etc.
- [ ] Implement retry logic for failed sends
- [ ] Track delivery status (sent, bounced, complained)

### Background Jobs

- [ ] Set up Hangfire or Azure Functions to run expiration check daily
- [ ] Implement idempotency (avoid duplicate reminders if job runs twice)
- [ ] Log execution: how many reminders, how many blocks

### Security

- [ ] Validate file uploads: check MIME type, scan for malware
- [ ] Implement role-based access: only corporate primary can upload, only admin can verify
- [ ] Add audit trail: query who accessed what documents when
- [ ] Encrypt sensitive fields (policy number, coverage limits) in database

### Reporting

- [ ] Dashboard: applications submitted, approved, pending compliance, active, suspended
- [ ] Compliance expiration report: generate monthly
- [ ] Booking audit: tie all reservations to verified compliance status at time of approval

### Integration Points

- [ ] Hook account activation to Syncfusion Scheduler: enable booking buttons
- [ ] Hook compliance expiration to disable booking (hard block on server)
- [ ] Generate invoices (UC-15) only for active accounts
- [ ] Deactivate delegates if primary account is suspended

---

## Syncfusion Scheduler Integration

The **Scheduler** component is used to display:

1. **Booking Calendar** — Shows available slots (filtered by compliance status)
2. **Booking Blocks** — When compliance expires, booking UI is disabled

```javascript
// scheduler.js
function onScheduleCreated(args) {
    // Fetch compliance status
    appApi.get(`/api/compliance/documents`)
        .then(response => {
            const status = response.data;
            
            // If booking is disabled, hide booking controls
            if (!status.bookingEnabled) {
                document.getElementById('cartBook').disabled = true;
                document.getElementById('policyCard').innerHTML = 
                    `<p class="error">${status.bookingBlockedReason}</p>`;
            }
        });
}
```

---

## API Response Examples

### Successful Onboarding Flow

**1. Apply:**
```
POST /api/onboarding/apply
→ 201 { id: 1001, status: "Pending" }
```

**2. Admin Approves:**
```
POST /api/admin/onboarding/applications/1001/review
  { approve: true, tierId: 4, adminNotes: "LE verified" }
→ 204 No Content
```

**3. User Uploads COI:**
```
POST /api/compliance/documents/upload
  { groupId: 1, documentType: 0, fileName: "COI.pdf", base64Content: "..." }
→ 201 { id: 2001, status: "PendingReview" }
```

**4. User Uploads Agreement:**
```
POST /api/compliance/documents/upload
  { groupId: 1, documentType: 1, fileName: "Agreement.pdf", base64Content: "..." }
→ 201 { id: 2002, status: "PendingReview" }
```

**5. Admin Verifies COI:**
```
POST /api/admin/compliance/documents/2001/verify
  { verified: true, expirationDate: "2026-06-15", policyType: "Occurrence", 
    coverageLimits: "1M/2M", ... }
→ 204 No Content
```

**6. Admin Verifies Agreement:**
```
POST /api/admin/compliance/documents/2002/verify
  { verified: true, expirationDate: "2026-06-15", ... }
→ 204 No Content
(Account automatically activated: CorporateGroup.Status = Active)
```

**7. User Can Now Book:**
```
GET /api/compliance/documents
→ 200 { id: 1, status: "Active", bookingEnabled: true, 
        documents: [...], complianceSummary: "Verified through 2026-06-15" }
```

---

## Summary

The POC demonstrates a complete onboarding & compliance lifecycle:

| UC | What | Service | API |
|----|------|---------|-----|
| UC-01 | Apply | `OnboardingService.SubmitApplication()` | `POST /api/onboarding/apply` |
| UC-02 | Approve/Deny | `OnboardingService.ReviewApplication()` | `POST /api/admin/onboarding/applications/{id}/review` |
| UC-03 | Upload Docs | `OnboardingService.UploadComplianceDocument()` | `POST /api/compliance/documents/upload` |
| UC-04 | Verify | `OnboardingService.VerifyComplianceDocument()` | `POST /api/admin/compliance/documents/{id}/verify` |
| UC-05 | Expiration | `OnboardingService.CheckAndHandleExpirations()` | `POST /api/admin/compliance/check-expirations` |

All business logic is **service-layer agnostic** — easily swappable with customer's existing APIs. The API contract is stable; database, auth, email can be replaced without touching controllers or services.

---

## Links to Syncfusion Scheduler SDK

- [Getting Started with Syncfusion Scheduler](https://www.syncfusion.com/asp-net-core-scheduler/getting-started)
- [Scheduler Views](https://www.syncfusion.com/documentation/essential-js-2/schedule/views/)
- [Data Binding](https://www.syncfusion.com/documentation/essential-js-2/schedule/data-binding/)
- [Appointments (CRUD)](https://www.syncfusion.com/documentation/essential-js-2/schedule/appointments/)
- [Quick Popup](https://www.syncfusion.com/documentation/essential-js-2/schedule/quick-popup/)
- [Cell Rendering](https://www.syncfusion.com/documentation/essential-js-2/schedule/cell-rendering/)
- [Drag and Drop](https://www.syncfusion.com/documentation/essential-js-2/schedule/drag-and-drop/)
