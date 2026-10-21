# Comprehensive Solution: Arizona Range Reservations with Syncfusion Scheduler

## Overview

This document demonstrates how the **Syncfusion ASP.NET Core Scheduler** combined with server-side business logic implements all 16 use cases for the Arizona Game & Fish Department's Range Reservation system. The POC runs **fully on-premises** with no cloud dependencies.

---

## SECTION 1: Onboarding & Compliance Management (UC-01 through UC-05)

### UC-01: Apply for Corporate Account (Prospective Corporate Primary User)

**Requirement:** A commercial entity, law enforcement agency, or competition organizer submits an initial application with company details, contact information, and primary contact assignment. Account is created in a Pending / Pre-Approved state with direct range booking functionality locked.

**Implementation:**

The `OnboardingService` handles application submission:

```csharp
// OnboardingService.cs
public (bool Ok, OnboardingApplication Application, List<string> Errors) SubmitApplication(
    OnboardingApplicationInput input)
{
    var errors = new List<string>();

    // Validate input
    if (string.IsNullOrWhiteSpace(input.CompanyName)) errors.Add("Company name is required.");
    if (string.IsNullOrWhiteSpace(input.CompanyType)) errors.Add("Company type is required.");
    if (string.IsNullOrWhiteSpace(input.PrimaryContactName)) errors.Add("Primary contact name is required.");
    if (string.IsNullOrWhiteSpace(input.PrimaryContactEmail)) errors.Add("Primary contact email is required.");

    if (errors.Count > 0) return (false, null, errors);

    // Create application in Pending state
    var app = new OnboardingApplication
    {
        CompanyName = input.CompanyName,
        CompanyType = input.CompanyType,
        PrimaryContactName = input.PrimaryContactName,
        PrimaryContactEmail = input.PrimaryContactEmail,
        Status = AccountStatus.Pending,
        AppliedAt = DateTime.UtcNow
    };

    _store.AddApplication(app);

    // Send confirmation email
    LogNotification(app.PrimaryContactEmail, 
        $"Application Received: {app.CompanyName}", 
        $"Your application is pending admin review...");

    return (true, app, new());
}
```

**Domain Models** (`DomainModels.cs`):

```csharp
public enum AccountStatus
{
    Pending,           // Initial application submitted
    PreApproved,       // Admin approved, waiting for compliance docs
    Active,            // All compliance verified, booking enabled
    Suspended,         // Compliance expired
    Denied,            // Admin denied
    Archived
}

public class OnboardingApplication
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string CompanyType { get; set; } = "";  // "Commercial", "LE", "Competition"
    public string PrimaryContactName { get; set; } = "";
    public string PrimaryContactEmail { get; set; } = "";
    public DateTime AppliedAt { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Pending;
    public CustomerTier? AssignedTier { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? DenialReason { get; set; }
}
```

**API Endpoint** (`OnboardingApiController.cs`):

```csharp
[HttpPost("onboarding/apply")]
[AllowAnonymous]
public ActionResult<OnboardingApplication> ApplyForAccount([FromBody] OnboardingApplicationInput input)
{
    var (ok, app, errors) = _service.SubmitApplication(input);
    if (!ok) return UnprocessableEntity(new { Errors = errors });
    return Ok(app);
}
```

**Syncfusion SDK References:**
- [ASP.NET Core Scheduler Getting Started](https://www.syncfusion.com/products/aspnetcore/scheduler/getting-started)
- [Data Binding](https://www.syncfusion.com/help/aspnetcore-scheduler/data-binding)

---

### UC-02: Review & Approve Corporate Onboarding Application (Admin)

**Requirement:** Admin reviews the submitted application, verifies corporate entity standing, assigns appropriate tier, and approves or denies the application. If approved, account status transitions and user is prompted to complete compliance documentation; if denied, an automated email notification with the denial reason is sent.

**Implementation:**

```csharp
// OnboardingService.cs
public (bool Ok, List<string> Errors) ApproveApplication(
    int applicationId, 
    CustomerTier tier,
    AppUser admin)
{
    var errors = new List<string>();
    var app = _store.OnboardingApplications.FirstOrDefault(a => a.Id == applicationId);
    
    if (app is null) { errors.Add("Application not found."); return (false, errors); }
    if (app.Status != AccountStatus.Pending) { errors.Add("Only Pending applications can be approved."); return (false, errors); }

    // Transition to PreApproved
    app.Status = AccountStatus.PreApproved;
    app.AssignedTier = tier;
    app.ApprovedAt = DateTime.UtcNow;

    // Create a corporate group for this company
    var group = new CorporateGroup
    {
        Name = app.CompanyName,
        Tier = tier,
        ContactEmail = app.PrimaryContactEmail,
        Status = AccountStatus.PreApproved
    };

    lock (_store.Sync)
    {
        group.Id = (_store.Groups.Count > 0 ? _store.Groups.Max(g => g.Id) : 0) + 1;
        _store.Groups.Add(group);
        app.GroupId = group.Id;
    }

    // Notify corporate user to upload compliance documents
    LogNotification(app.PrimaryContactEmail,
        $"Application Approved: {app.CompanyName}",
        $"""
        Your application for {app.CompanyName} has been APPROVED.
        
        Tier Assigned: {TierNames.Display(tier)}
        Next Step: Upload required compliance documentation (COI + signed agreement).
        
        Access the portal to complete onboarding.
        """);

    return (true, new());
}

public (bool Ok, List<string> Errors) DenyApplication(
    int applicationId,
    string denialReason,
    AppUser admin)
{
    var errors = new List<string>();
    var app = _store.OnboardingApplications.FirstOrDefault(a => a.Id == applicationId);
    
    if (app is null) { errors.Add("Application not found."); return (false, errors); }

    app.Status = AccountStatus.Denied;
    app.DenialReason = denialReason;

    LogNotification(app.PrimaryContactEmail,
        $"Application Denied: {app.CompanyName}",
        $"""
        Your application for {app.CompanyName} has been DENIED.
        
        Reason: {denialReason}
        
        If you believe this decision was made in error, please contact the AZGFD.
        """);

    return (true, new());
}
```

**Admin Queue View** (Razor):

```html
<!-- Admin/Applications.cshtml -->
<div class="admin-queue">
    <table id="applicationsGrid">
        <thead>
            <tr>
                <th>Company</th>
                <th>Contact</th>
                <th>Type</th>
                <th>Status</th>
                <th>Applied</th>
                <th>Action</th>
            </tr>
        </thead>
    </table>
</div>

<script>
document.getElementById('approveBtn').onclick = () => {
    const tier = document.getElementById('tierSelect').value;
    fetch(`/api/onboarding/applications/${appId}/approve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tier })
    }).then(response => response.ok ? alert('Approved!') : alert('Error'));
};
</script>
```

**API Endpoints:**

```csharp
[HttpPost("onboarding/applications/{id}/approve")]
[Authorize(Roles = Roles.Admin)]
public IActionResult ApproveApplication(int id, [FromBody] ApproveApplicationInput input)
{
    var (ok, errors) = _service.ApproveApplication(id, input.Tier, Me!);
    return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}

[HttpPost("onboarding/applications/{id}/deny")]
[Authorize(Roles = Roles.Admin)]
public IActionResult DenyApplication(int id, [FromBody] DenyApplicationInput input)
{
    var (ok, errors) = _service.DenyApplication(id, input.Reason, Me!);
    return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

---

### UC-03: Upload Compliance Documentation (Corporate Primary User)

**Requirement:** User uploads required Certificate of Insurance (COI) under an occurrence policy meeting minimums (1M/2M) and signed annual user group agreements/waivers. Documents enter a Pending Admin Review state, visible on the user's dashboard.

**Implementation:**

```csharp
// OnboardingService.cs
public (bool Ok, ComplianceDocument? Document, List<string> Errors) UploadComplianceDocument(
    ComplianceDocumentUploadInput input,
    AppUser uploader)
{
    var errors = new List<string>();

    var group = _store.Groups.FirstOrDefault(g => g.Id == input.GroupId);
    if (group is null) { errors.Add("Corporate group not found."); return (false, null, errors); }

    if (input.DocumentType == ComplianceDocumentType.CertificateOfInsurance)
    {
        if (string.IsNullOrWhiteSpace(input.FileName)) 
            errors.Add("COI file name is required.");
        if (!input.FileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ?? false)
            errors.Add("COI must be a PDF file.");
    }

    if (errors.Count > 0) return (false, null, errors);

    // In production: file would be stored in blob storage or file system
    // For POC: just track metadata
    var doc = new ComplianceDocument
    {
        GroupId = input.GroupId,
        DocumentType = input.DocumentType,
        FileName = input.FileName ?? "",
        UploadedBy = uploader.Id,
        UploadedAt = DateTime.UtcNow,
        Status = DocumentStatus.PendingReview
    };

    lock (_store.Sync)
    {
        doc.Id = (_store.ComplianceDocuments.Count > 0 ? _store.ComplianceDocuments.Max(d => d.Id) : 0) + 1;
        _store.ComplianceDocuments.Add(doc);
    }

    // Notify admin
    LogNotification("admin@azgfd.example.com",
        $"New Compliance Document for Review: {group.Name}",
        $"Document Type: {doc.DocumentType}\nUploaded: {doc.UploadedAt:F}");

    return (true, doc, new());
}
```

**Domain Models:**

```csharp
public enum ComplianceDocumentType
{
    CertificateOfInsurance,
    UserGroupAgreement,
    Other
}

public enum DocumentStatus
{
    PendingUpload,
    PendingReview,
    Verified,
    Expired,
    Rejected
}

public class ComplianceDocument
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ComplianceDocumentType DocumentType { get; set; }
    public string FileName { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.PendingReview;
    public DateTime? ExpirationDate { get; set; }
    public string? PolicyNumber { get; set; }
    public string? PolicyType { get; set; }  // "Occurrence" or "Claims-made"
    public string? CoverageLimits { get; set; }
    public string? CertificateHolder { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? ReviewNotes { get; set; }
}
```

**Upload Form** (Razor):

```html
<!-- Home/Onboarding.cshtml -->
<div class="compliance-upload">
    <h2>Upload Compliance Documents</h2>
    <form id="uploadForm" enctype="multipart/form-data">
        <div class="form-group">
            <label>Certificate of Insurance (COI)</label>
            <input type="file" id="coiFile" name="file" accept=".pdf" required />
            <small>Occurrence policy, 1M/2M minimum coverage</small>
        </div>
        
        <div class="form-group">
            <label>User Group Agreement</label>
            <input type="file" id="agreementFile" name="file" accept=".pdf" required />
            <small>Signed annual waiver/agreement</small>
        </div>

        <button type="submit" class="btn btn-primary">Upload Documents</button>
    </form>
</div>

<script>
document.getElementById('uploadForm').onsubmit = async (e) => {
    e.preventDefault();
    const formData = new FormData();
    const coiFile = document.getElementById('coiFile').files[0];
    formData.append('documentType', 'CertificateOfInsurance');
    formData.append('file', coiFile);

    const resp = await fetch(`/api/onboarding/documents/upload`, {
        method: 'POST',
        body: formData
    });
    
    if (resp.ok) alert('Document uploaded for review');
    else alert('Upload failed: ' + await resp.text());
};
</script>
```

**API Endpoint:**

```csharp
[HttpPost("onboarding/documents/upload")]
[Authorize(Roles = Roles.Corporate)]
public ActionResult<ComplianceDocument> UploadDocument([FromForm] ComplianceDocumentUploadInput input)
{
    if (Me is not { GroupId: not null } me) return Unauthorized();
    input.GroupId = me.GroupId.Value;
    
    var (ok, doc, errors) = _service.UploadComplianceDocument(input, me);
    if (!ok) return UnprocessableEntity(new { Errors = errors });
    return Ok(doc);
}
```

---

### UC-04: Verify Insurance & Compliance Documents (Admin)

**Requirement:** Admin inspects the uploaded COI (checking occurrence policy type, coverage minimums, and Certificate Holder wording) and user group agreements, enters the document expiration dates, and marks them as verified. System unlocks full booking privileges through the document expiration date.

**Implementation:**

```csharp
// OnboardingService.cs
public (bool Ok, List<string> Errors) VerifyComplianceDocument(
    ComplianceDocumentVerifyInput input,
    AppUser admin)
{
    var errors = new List<string>();

    var doc = _store.ComplianceDocuments.FirstOrDefault(d => d.Id == input.DocumentId);
    if (doc is null) { errors.Add("Document not found."); return (false, errors); }

    if (doc.Status != DocumentStatus.PendingReview)
    {
        errors.Add($"Only PendingReview documents can be verified. Current: {doc.Status}");
        return (false, errors);
    }

    var group = _store.Groups.FirstOrDefault(g => g.Id == doc.GroupId);
    if (group is null) { errors.Add("Associated group not found."); return (false, errors); }

    if (input.Verified)
    {
        // Validate COI
        if (doc.DocumentType == ComplianceDocumentType.CertificateOfInsurance)
        {
            if (string.IsNullOrWhiteSpace(input.PolicyNumber))
                errors.Add("Policy number required.");
            if (input.PolicyType != "Occurrence")
                errors.Add("Policy must be Occurrence type; Claims-made not accepted.");
            if (!ValidateCoverageLimits(input.CoverageLimits))
                errors.Add("Coverage must meet 1M/2M minimum.");
        }

        if (!input.ExpirationDate.HasValue || input.ExpirationDate.Value <= DateTime.UtcNow)
            errors.Add("Expiration date must be in the future.");

        if (errors.Count > 0) return (false, errors);

        // Mark verified
        doc.Status = DocumentStatus.Verified;
        doc.VerifiedBy = admin.Name;
        doc.VerifiedAt = DateTime.UtcNow;
        doc.ExpirationDate = input.ExpirationDate.Value;
        doc.PolicyNumber = input.PolicyNumber;
        doc.PolicyType = input.PolicyType;
        doc.CoverageLimits = input.CoverageLimits;
        doc.CertificateHolder = input.CertificateHolder;
        doc.ReviewNotes = input.ReviewNotes;

        lock (_store.Sync)
        {
            var existing = _store.ComplianceDocuments.FirstOrDefault(d => d.Id == doc.Id);
            if (existing != null)
            {
                _store.ComplianceDocuments.Remove(existing);
                _store.ComplianceDocuments.Add(doc);
            }
        }

        // Check if all required docs are verified
        var allDocs = _store.ComplianceDocuments.Where(d => d.GroupId == doc.GroupId).ToList();
        var requiredTypes = new[] 
        { 
            ComplianceDocumentType.CertificateOfInsurance,
            ComplianceDocumentType.UserGroupAgreement 
        };

        var allVerified = requiredTypes.All(type =>
            allDocs.Any(d => d.DocumentType == type && 
                           d.Status == DocumentStatus.Verified && 
                           d.ExpirationDate > DateTime.UtcNow)
        );

        if (allVerified && group.Status == AccountStatus.PreApproved)
        {
            // Activate account - booking now enabled!
            group.Status = AccountStatus.Active;
            group.LastComplianceVerifiedAt = DateTime.UtcNow;
            
            lock (_store.Sync)
            {
                var existingGroup = _store.Groups.FirstOrDefault(g => g.Id == group.Id);
                if (existingGroup != null)
                {
                    _store.Groups.Remove(existingGroup);
                    _store.Groups.Add(group);
                }
            }

            LogNotification(group.ContactEmail,
                $"Account Activated: {group.Name}",
                $"Your account is now ACTIVE. Booking enabled through {doc.ExpirationDate:yyyy-MM-dd}");
        }
    }
    else
    {
        // Rejection
        doc.Status = DocumentStatus.Rejected;
        doc.VerifiedBy = admin.Name;
        doc.VerifiedAt = DateTime.UtcNow;
        doc.ReviewNotes = input.ReviewNotes;

        lock (_store.Sync)
        {
            var existing = _store.ComplianceDocuments.FirstOrDefault(d => d.Id == doc.Id);
            if (existing != null)
            {
                _store.ComplianceDocuments.Remove(existing);
                _store.ComplianceDocuments.Add(doc);
            }
        }

        LogNotification(group.ContactEmail,
            $"Compliance Document Rejected: {group.Name}",
            $"Document rejected. Reason: {input.ReviewNotes}\nPlease re-upload corrected version.");
    }

    return (true, new());
}
```

**Admin Compliance Review** (Razor):

```html
<!-- Admin/ComplianceQueue.cshtml -->
<div class="compliance-review">
    <table id="complianceGrid">
        <tr>
            <td>Company</td>
            <td>Document Type</td>
            <td>Uploaded</td>
            <td>Status</td>
            <td>Action</td>
        </tr>
    </table>
</div>

<div id="verifyDialog" class="dialog">
    <h3>Verify Insurance Document</h3>
    <form id="verifyForm">
        <input id="policyType" placeholder="Occurrence / Claims-made" />
        <input id="policyNumber" placeholder="Policy #" />
        <input id="coverage" placeholder="Coverage Limits (e.g., 1M/2M)" />
        <input id="certHolder" placeholder="Certificate Holder Name" />
        <input type="date" id="expiration" />
        <textarea id="reviewNotes" placeholder="Review notes..."></textarea>
        
        <button type="button" id="verifyBtn" class="btn btn-success">Verify</button>
        <button type="button" id="rejectBtn" class="btn btn-danger">Reject</button>
    </form>
</div>

<script>
document.getElementById('verifyBtn').onclick = async () => {
    const data = {
        documentId: docId,
        verified: true,
        policyType: document.getElementById('policyType').value,
        policyNumber: document.getElementById('policyNumber').value,
        coverage Limits: document.getElementById('coverage').value,
        certificateHolder: document.getElementById('certHolder').value,
        expirationDate: document.getElementById('expiration').value,
        reviewNotes: document.getElementById('reviewNotes').value
    };
    
    const resp = await fetch(`/api/onboarding/documents/${docId}/verify`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    
    if (resp.ok) alert('Document verified - account now active!');
};
</script>
```

**API Endpoint:**

```csharp
[HttpPost("onboarding/documents/{id}/verify")]
[Authorize(Roles = Roles.Admin)]
public IActionResult VerifyDocument(int id, [FromBody] ComplianceDocumentVerifyInput input)
{
    var (ok, errors) = _service.VerifyComplianceDocument(input, Me!);
    return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

---

### UC-05: Automated Compliance Blocking & Expiration Warnings (System)

**Requirement:** System monitors document expiration dates and sends automated reminder alerts at T-60, T-30, and T-7 days. Upon expiration without renewal, booking access is automatically locked.

**Implementation:**

```csharp
// OnboardingService.cs - Background check
public (int RemindersCount, int ExpirationBlocksCount) CheckAndHandleExpirations()
{
    int remindersCount = 0;
    int blocksCount = 0;
    var now = DateTime.UtcNow;

    List<CorporateGroup> allGroups;
    List<ComplianceDocument> allDocs;
    lock (_store.Sync)
    {
        allGroups = new List<CorporateGroup>(_store.Groups);
        allDocs = new List<ComplianceDocument>(_store.ComplianceDocuments);
    }

    foreach (var group in allGroups)
    {
        if (group.Status != AccountStatus.Active) continue;

        var groupDocs = allDocs.Where(d => d.GroupId == group.Id && d.Status == DocumentStatus.Verified).ToList();

        foreach (var doc in groupDocs)
        {
            if (!doc.ExpirationDate.HasValue) continue;

            var daysUntilExpiration = (doc.ExpirationDate.Value - now).TotalDays;

            // EXPIRED: Block booking
            if (daysUntilExpiration < 0)
            {
                var eventExists = _store.ExpirationEvents.Any(e =>
                    e.GroupId == group.Id && e.DocumentId == doc.Id && e.EventType == "Expired");

                if (!eventExists)
                {
                    group.Status = AccountStatus.Suspended;
                    lock (_store.Sync)
                    {
                        var existingGroup = _store.Groups.FirstOrDefault(g => g.Id == group.Id);
                        if (existingGroup != null)
                        {
                            _store.Groups.Remove(existingGroup);
                            _store.Groups.Add(group);
                        }
                    }

                    LogNotification(group.ContactEmail,
                        $"URGENT: Booking Blocked - Compliance Expired: {group.Name}",
                        $"""
                        YOUR BOOKING ACCESS HAS BEEN BLOCKED
                        
                        Reason: {doc.DocumentType} expired on {doc.ExpirationDate:yyyy-MM-dd}
                        
                        IMMEDIATE ACTION: Upload a renewed document to restore booking privileges.
                        """);

                    blocksCount++;
                }
            }
            // REMINDER: T-60, T-30, T-7
            else if (daysUntilExpiration <= 60 && daysUntilExpiration > 0)
            {
                foreach (var threshold in new[] { 60, 30, 7 })
                {
                    if (Math.Abs(daysUntilExpiration - threshold) < 1.0)
                    {
                        var eventKey = $"Reminder-{threshold}";
                        var reminderExists = _store.ExpirationEvents.Any(e =>
                            e.GroupId == group.Id && e.DocumentId == doc.Id && e.EventType == eventKey);

                        if (!reminderExists)
                        {
                            LogNotification(group.ContactEmail,
                                $"⏰ Compliance Reminder ({threshold} days): {group.Name}",
                                $"""
                                Your {doc.DocumentType} expires in {threshold} days ({doc.ExpirationDate:yyyy-MM-dd}).
                                
                                Please upload a renewed document to avoid booking interruption.
                                """);

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

**Background Service** (Hosted Service in `Program.cs`):

```csharp
// In Program.cs
builder.Services.AddHostedService<ComplianceExpirationBackgroundService>();

// ComplianceExpirationBackgroundService.cs
public class ComplianceExpirationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComplianceExpirationBackgroundService> _log;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public ComplianceExpirationBackgroundService(IServiceProvider serviceProvider, 
        ILogger<ComplianceExpirationBackgroundService> log)
    {
        _serviceProvider = serviceProvider;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var onboardingService = scope.ServiceProvider.GetRequiredService<OnboardingService>();
                
                var (reminders, blocks) = onboardingService.CheckAndHandleExpirations();
                _log.LogInformation($"Compliance check: {reminders} reminders, {blocks} blocks");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Compliance check failed");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }
}
```

**Dashboard UI** (Corporate User Sees Booking Status):

```html
<!-- Home/Dashboard.cshtml -->
<div class="booking-status">
    @if (group.Status == AccountStatus.Active)
    {
        <div class="alert alert-success">
            ✅ Booking Enabled
            <small>Valid through @nextExpiration.ToString("yyyy-MM-dd")</small>
        </div>
        <button class="btn btn-primary">Book a Range</button>
    }
    else if (group.Status == AccountStatus.Suspended)
    {
        <div class="alert alert-danger">
            🔒 Booking Blocked: Insurance expired on @expiredDate.ToString("MM/DD/YYYY")
        </div>
        <button class="btn btn-warning">Upload Renewed Insurance</button>
    }
    else if (group.Status == AccountStatus.PreApproved)
    {
        <div class="alert alert-info">
            📋 Complete Compliance Documents to Enable Booking
        </div>
        <button class="btn btn-primary">Upload Documents</button>
    }
</div>
```

**Syncfusion SDK References:**
- [Background Services](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.hosting.backgroundservice)
- [Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)

---

## SECTION 2: Reservation & Scheduling Workflows (UC-06 through UC-11)

### UC-06: Reserve Single Shooting Range Slot (Corporate User / Delegate)

**Implementation via Syncfusion Scheduler Editor:**

The full Scheduler editor collects:
- Range selection
- Date/time slots
- Headcount
- Firearm types (multi-select)
- Target attributes

See [CUSTOMER_RESPONSE.md](./CUSTOMER_RESPONSE.md) for complete UC-06 through UC-11 implementation with code examples and SDK links.

---

## SECTION 3: Admin Review & Facility Operations (UC-12, UC-13, UC-14)

See [CUSTOMER_RESPONSE.md](./CUSTOMER_RESPONSE.md) for complete implementation.

---

## SECTION 4: Financials & Account Administration (UC-15, UC-16)

See [CUSTOMER_RESPONSE.md](./CUSTOMER_RESPONSE.md) for complete implementation.

---

## Integration Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  ASP.NET Core MVC 10 Frontend                               │
│  - Syncfusion Scheduler (Day/Week/Month/Timeline views)     │
│  - Onboarding Form                                           │
│  - Admin Queue Dashboard                                    │
└─────────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────────┐
│  REST API Layer                                             │
│  - ReservationsApiController (UC-06 to UC-14)              │
│  - OnboardingApiController (UC-01 to UC-05)                │
│  - PublicController (iCalendar export)                      │
└─────────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────────┐
│  Business Logic Services                                    │
│  - ReservationService (booking validation + CRUD)          │
│  - OnboardingService (compliance lifecycle)                │
│  - ReservationRulesEngine (policy enforcement)             │
│  - RecurrenceExpander (RRULE processing)                   │
│  - NotificationService (email/pickup folder)               │
│  - ComplianceExpirationBackgroundService (UC-05)           │
└─────────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────────┐
│  In-Memory Data Store (POC) / EF Core (Production)          │
│  - CorporateGroup  (with Status: Active/Suspended/etc)    │
│  - Reservation     (with RequestId for batch requests)    │
│  - OnboardingApplication (lifecycle tracking)             │
│  - ComplianceDocument (expiration monitoring)             │
│  - Blackout        (facility maintenance)                 │
│  - NotificationRecord (audit trail)                       │
│  - ComplianceExpirationEvent (background job log)         │
└─────────────────────────────────────────────────────────────┘
```

---

## Key Features Implemented

✅ **UC-01:** Application submission with Pending state  
✅ **UC-02:** Admin approve/deny with tier assignment  
✅ **UC-03:** Document upload with PendingReview state  
✅ **UC-04:** Verification with COI validation (occurrence policy, 1M/2M check)  
✅ **UC-05:** Automated expiration monitoring (T-60, T-30, T-7, then block)  
✅ **UC-06:** Single slot booking via Scheduler  
✅ **UC-07:** Multi-date booking (single range, one cart)  
✅ **UC-08:** Single-range restriction with error message  
✅ **UC-09:** Recurring series with end-date cap (Dec 31, following year)  
✅ **UC-10:** Overlap prevention + same-group exceptions  
✅ **UC-11:** Tier 4 exclusive Monday access  
✅ **UC-12:** Admin review queue with compliance checklist  
✅ **UC-13:** Facility blackouts with iCalendar export  
✅ **UC-14:** Group permissions + specialized range toggles  
✅ **UC-15:** Tier-based invoicing (stub)  
✅ **UC-16:** Delegate roles with email invitations  

---

## Production Deployment Checklist

- [ ] Replace `InMemoryDataStore` with **EF Core + SQL Server**
- [ ] Configure **SMTP relay** or use **SendGrid** for notifications (see `Email:SmtpHost` in `appsettings.json`)
- [ ] Implement **document storage** (Azure Blob Storage or on-prem file share) for uploaded COIs
- [ ] Deploy **background service** (`ComplianceExpirationBackgroundService`) on a dedicated host
- [ ] Add **audit logging** for all compliance state transitions
- [ ] Integrate with **Active Directory / ADFS** for corporate user authentication
- [ ] Configure **SSL/TLS** with valid certificates
- [ ] Set up **monitoring & alerting** for compliance expiration failures
- [ ] Test **iCalendar export** with Outlook, Google Calendar, Apple Calendar

---

## Support & Documentation

- **Syncfusion Scheduler:** https://www.syncfusion.com/products/aspnetcore/scheduler
- **Getting Started:** https://www.syncfusion.com/help/aspnetcore-scheduler/getting-started
- **Data Binding:** https://www.syncfusion.com/help/aspnetcore-scheduler/data-binding
- **Recurrence Rules:** https://www.syncfusion.com/help/aspnetcore-scheduler/recurrence-rule
- **iCalendar Export:** https://www.syncfusion.com/help/aspnetcore-scheduler/export-import
- **Appointments (CRUD):** https://www.syncfusion.com/help/aspnetcore-scheduler/appointments

---

**Document Version:** 1.0  
**Last Updated:** [Current Date]  
**POC Repository:** `scheduler-sdk-samples/aspnetcore-scheduler/RangeReservation`
