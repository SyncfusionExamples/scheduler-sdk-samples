using System.Text;
using RangeReservationPoc.Models;

namespace RangeReservationPoc.Services;

/// <summary>
/// UC-01: Apply for Corporate Account
/// UC-02: Review & Approve Corporate Onboarding Application (Admin)
/// UC-03: Upload Compliance Documentation (Corporate Primary User)
/// UC-04: Verify Insurance & Compliance Documents (Admin)
/// UC-05: Automated Compliance Blocking & Expiration Warnings (System)
/// 
/// Handles corporate account onboarding lifecycle: application submission, admin review/approval,
/// compliance document upload/verification, and automated expiration monitoring.
/// </summary>
public class OnboardingService
{
    private readonly InMemoryDataStore _store;
    private readonly ILogger<OnboardingService> _log;

    public OnboardingService(InMemoryDataStore store, ILogger<OnboardingService> log)
    {
        _store = store;
        _log = log;
    }
    
    private void LogNotification(string to, string subject, string body)
    {
        lock (_store.Sync)
        {
            _store.Notifications.Insert(0, new NotificationRecord
            {
                Id = _store.NextNotificationId(),
                SentAt = DateTime.Now,
                To = to,
                Subject = subject,
                Body = body,
                DeliveredVia = "in-memory log (onboarding)"
            });
        }
    }

    // ==================== UC-01: Apply for Corporate Account ====================

    /// <summary>
    /// UC-01: Prospective corporate user submits an initial application.
    /// Creates the application in Pending state; account is not yet functional for booking.
    /// Returns the application ID for tracking.
    /// </summary>
    public (bool Ok, OnboardingApplication? Application, List<string> Errors) SubmitApplication(
        OnboardingApplicationInput input)
    {
        var errors = new List<string>();

        // Validation
        if (string.IsNullOrWhiteSpace(input.CompanyName))
            errors.Add("Company name is required.");
        if (string.IsNullOrWhiteSpace(input.CompanyType))
            errors.Add("Company type is required.");
        if (string.IsNullOrWhiteSpace(input.PrimaryContactEmail) || !input.PrimaryContactEmail.Contains("@"))
            errors.Add("Valid primary contact email is required.");
        if (string.IsNullOrWhiteSpace(input.PrimaryContactName))
            errors.Add("Primary contact name is required.");
        if (string.IsNullOrWhiteSpace(input.PrimaryContactPhone))
            errors.Add("Primary contact phone is required.");

        if (errors.Count > 0)
            return (false, null, errors);

        var app = new OnboardingApplication
        {
            CompanyName = input.CompanyName.Trim(),
            CompanyType = input.CompanyType.Trim(),
            RegistrationNumber = input.RegistrationNumber?.Trim() ?? "",
            PrimaryContactName = input.PrimaryContactName.Trim(),
            PrimaryContactEmail = input.PrimaryContactEmail.Trim(),
            PrimaryContactPhone = input.PrimaryContactPhone.Trim(),
            Address = input.Address?.Trim() ?? "",
            Status = AccountStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _store.AddApplication(app);

        // Send confirmation email to prospective user
        var confirmBody = $"""
            Dear {app.PrimaryContactName},
            
            Thank you for submitting your corporate account application for {app.CompanyName}.
            
            Application ID: {app.Id}
            Status: Pending Review
            
            An AZGFD Administrator will review your application and contact you within 3-5 business days.
            
            Best regards,
            Arizona Game & Fish Department
            """;
        
        LogNotification(app.PrimaryContactEmail, $"Application Received: {app.CompanyName}", confirmBody);

        return (true, app, new());
    }

    // ==================== UC-02: Review & Approve Corporate Onboarding Application (Admin) ====================

    /// <summary>
    /// UC-02: Admin reviews submitted application and approves or denies it.
    /// If approved: creates a CorporateGroup record in PreApproved state (awaiting compliance docs).
    /// If denied: sends denial notification with reason.
    /// </summary>
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
            // Validate tier assignment
            if (!input.TierId.HasValue || !Enum.IsDefined(typeof(CustomerTier), input.TierId.Value))
            {
                errors.Add("Tier assignment is required for approval.");
                return (false, errors);
            }

            var tier = (CustomerTier)input.TierId.Value;
            app.AssignedTier = tier;
            app.Status = AccountStatus.PreApproved;

            // Create the CorporateGroup record in PreApproved state
            var group = new CorporateGroup
            {
                Name = app.CompanyName,
                Tier = tier,
                ContactEmail = app.PrimaryContactEmail,
                OnboardingApplicationId = app.Id,
                Status = AccountStatus.PreApproved
            };

            lock (_store.Sync)
            {
                group.Id = _store.Groups.Count > 0 ? _store.Groups.Max(g => g.Id) + 1 : 1;
                _store.Groups.Add(group);
            }
            app.UpdatedAt = DateTime.UtcNow;

            // Send approval notification
            var approvalBody = $"""
                Dear {app.PrimaryContactName},
                
                Congratulations! Your corporate account application has been approved by AZGFD.
                
                Company: {app.CompanyName}
                Tier: {TierNames.Display(tier)}
                Status: Pre-Approved - Awaiting Compliance Documentation
                
                Next Steps:
                You must now upload the following compliance documents to complete your onboarding:
                1. Certificate of Insurance (COI) - Occurrence policy, minimum 1M/2M coverage
                2. Signed User Group Agreement/Waiver
                
                Once verified by AZGFD, your account will be activated for range bookings.
                
                Please log in to your portal and upload these documents.
                
                Best regards,
                Arizona Game & Fish Department
                """;
            
            LogNotification(app.PrimaryContactEmail, $"Application Approved: {app.CompanyName}", approvalBody);
        }
        else
        {
            app.Status = AccountStatus.Denied;
            app.DenialReason = input.DenialReason ?? "Application denied by administrator.";
            app.UpdatedAt = DateTime.UtcNow;

            // Send denial notification
            var denialBody = $"""
                Dear {app.PrimaryContactName},
                
                We regret to inform you that your corporate account application has been denied.
                
                Reason: {app.DenialReason}
                
                If you have questions, please contact the Arizona Game & Fish Department.
                
                Best regards,
                Arizona Game & Fish Department
                """;
            
            LogNotification(app.PrimaryContactEmail, $"Application Denied: {app.CompanyName}", denialBody);
        }

        lock (_store.Sync)
        {
            var existing = _store.OnboardingApplications.FirstOrDefault(a => a.Id == app.Id);
            if (existing != null)
            {
                _store.OnboardingApplications.Remove(existing);
                _store.OnboardingApplications.Add(app);
            }
        }
        return (true, new());
    }

    // ==================== UC-03: Upload Compliance Documentation ====================

    /// <summary>
    /// UC-03: Corporate Primary User uploads required compliance documents (COI, agreements, etc.).
    /// Documents enter PendingReview state and are visible on the dashboard.
    /// </summary>
    public (bool Ok, ComplianceDocument? Document, List<string> Errors) UploadComplianceDocument(
        ComplianceDocumentUploadInput input,
        AppUser uploader)
    {
        var errors = new List<string>();

        var group = _store.Groups.FirstOrDefault(g => g.Id == input.GroupId);
        if (group is null)
        {
            errors.Add("Corporate group not found.");
            return (false, null, errors);
        }

        // Only allow uploads if in PreApproved or Active state
        if (group.Status != AccountStatus.PreApproved && group.Status != AccountStatus.Active)
        {
            errors.Add($"Compliance documents cannot be uploaded when account status is {group.Status}.");
            return (false, null, errors);
        }

        // Validate document type
        if (!Enum.IsDefined(typeof(ComplianceDocumentType), input.DocumentType))
        {
            errors.Add("Invalid document type.");
            return (false, null, errors);
        }

        if (string.IsNullOrWhiteSpace(input.FileName))
            errors.Add("File name is required.");
        if (string.IsNullOrWhiteSpace(input.Base64Content))
            errors.Add("Document content is required.");

        if (errors.Count > 0)
            return (false, null, errors);

        var docType = (ComplianceDocumentType)input.DocumentType;
        var doc = new ComplianceDocument
        {
            GroupId = input.GroupId,
            DocumentType = docType,
            FileName = input.FileName.Trim(),
            DocumentPath = $"uploads/compliance/{input.GroupId}/{Guid.NewGuid()}.pdf",
            Status = DocumentStatus.PendingReview,
            UploadedAt = DateTime.UtcNow
        };

        lock (_store.Sync)
        {
            doc.Id = (_store.ComplianceDocuments.Count > 0 ? _store.ComplianceDocuments.Max(d => d.Id) : 0) + 1;
            _store.ComplianceDocuments.Add(doc);
        }

        // Notify admin that a document is waiting for review
        var adminBody = $"""
            A new compliance document has been uploaded and is awaiting verification.
            
            Company: {group.Name}
            Document Type: {doc.DocumentType}
            File Name: {doc.FileName}
            Uploaded By: {uploader.Name}
            Uploaded At: {doc.UploadedAt:F}
            
            Please review and verify the document in the admin dashboard.
            """;
        
        LogNotification("admin@azgfd.example.com", $"New Compliance Document for Review: {group.Name}", adminBody);

        return (true, doc, new());
    }

    // ==================== UC-04: Verify Insurance & Compliance Documents (Admin) ====================

    /// <summary>
    /// UC-04: Admin inspects uploaded COI, verifies occurrence policy type, coverage minimums,
    /// certificate holder wording, and sets expiration date. Upon verification, the system
    /// unlocks booking privileges through the document expiration date.
    /// </summary>
    public (bool Ok, List<string> Errors) VerifyComplianceDocument(
        ComplianceDocumentVerifyInput input,
        AppUser admin)
    {
        var errors = new List<string>();

        var doc = _store.ComplianceDocuments.FirstOrDefault(d => d.Id == input.DocumentId);
        if (doc is null)
        {
            errors.Add("Document not found.");
            return (false, errors);
        }

        if (doc.Status != DocumentStatus.PendingReview)
        {
            errors.Add($"Only documents in PendingReview status can be verified. Current status: {doc.Status}");
            return (false, errors);
        }

        var group = _store.Groups.FirstOrDefault(g => g.Id == doc.GroupId);
        if (group is null)
        {
            errors.Add("Associated group not found.");
            return (false, errors);
        }

        if (input.Verified)
        {
            // Validation for COI documents
            if (doc.DocumentType == ComplianceDocumentType.CertificateOfInsurance)
            {
                if (string.IsNullOrWhiteSpace(input.PolicyNumber))
                    errors.Add("Policy number is required for COI verification.");
                if (string.IsNullOrWhiteSpace(input.PolicyType))
                    errors.Add("Policy type (Occurrence/Claims-made) is required for COI verification.");
                if (input.PolicyType != "Occurrence")
                    errors.Add("Policy type must be 'Occurrence'; Claims-made policies are not accepted.");
                if (string.IsNullOrWhiteSpace(input.CoverageLimits))
                    errors.Add("Coverage limits are required for COI verification.");
                // Verify 1M/2M minimum
                if (!ValidateCoverageLimits(input.CoverageLimits))
                    errors.Add("Coverage must meet minimum 1M/2M requirements.");
                if (string.IsNullOrWhiteSpace(input.CertificateHolder))
                    errors.Add("Certificate holder must be verified.");
            }

            if (!input.ExpirationDate.HasValue || input.ExpirationDate.Value <= DateTime.UtcNow)
            {
                errors.Add("Expiration date must be set to a future date.");
                return (false, errors);
            }

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

            lock (_store.Sync)
            {
                var existing = _store.ComplianceDocuments.FirstOrDefault(d => d.Id == doc.Id);
                if (existing != null)
                {
                    _store.ComplianceDocuments.Remove(existing);
                    _store.ComplianceDocuments.Add(doc);
                }
            }

            // Check if all required compliance docs are now verified
            var allDocs = _store.ComplianceDocuments.Where(d => d.GroupId == doc.GroupId).ToList();
            var requiredDocs = new[]
            {
                ComplianceDocumentType.CertificateOfInsurance,
                ComplianceDocumentType.UserGroupAgreement
            };

            var allVerified = requiredDocs.All(type =>
                allDocs.Any(d => d.DocumentType == type && d.Status == DocumentStatus.Verified && d.ExpirationDate > DateTime.UtcNow)
            );

            if (allVerified && group.Status == AccountStatus.PreApproved)
            {
                // Activate the account
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

                // Notify the corporate user that their account is now active
                var activationBody = $"""
                    Your corporate account has been fully activated!
                    
                    Company: {group.Name}
                    Status: Active
                    Booking Enabled: Yes
                    
                    You can now start booking range slots through the portal.
                    Your compliance documents are valid through {doc.ExpirationDate:yyyy-MM-dd}.
                    
                    Best regards,
                    Arizona Game & Fish Department
                    """;
                
                LogNotification(group.ContactEmail, $"Account Activated: {group.Name}", activationBody);
            }
        }
        else
        {
            // Rejection: document needs to be re-uploaded
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

            // Notify the corporate user to re-upload
            var rejectionBody = $"""
                Your submitted compliance document has been rejected and requires resubmission.
                
                Document: {doc.FileName}
                Reason: {input.ReviewNotes}
                
                Please upload a corrected version through your portal.
                
                Best regards,
                Arizona Game & Fish Department
                """;
            
            LogNotification(group.ContactEmail, $"Compliance Document Rejected: {group.Name}", rejectionBody);
        }

        return (true, new());
    }

    // ==================== UC-05: Automated Compliance Blocking & Expiration Warnings ====================

    /// <summary>
    /// UC-05: Background worker monitors document expiration dates.
    /// Sends reminders at T-60, T-30, T-7 days.
    /// On expiration date without renewal: locks booking access and suspends the account.
    /// </summary>
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
            if (group.Status != AccountStatus.Active)
                continue;

            var groupDocs = allDocs.Where(d => d.GroupId == group.Id && d.Status == DocumentStatus.Verified).ToList();

            foreach (var doc in groupDocs)
            {
                if (!doc.ExpirationDate.HasValue)
                    continue;

                var daysUntilExpiration = (doc.ExpirationDate.Value - now).TotalDays;

                // Check if expiration event already handled (to avoid duplicate notifications)
                var eventExists = _store.ExpirationEvents.FirstOrDefault(e =>
                    e.GroupId == group.Id && e.DocumentId == doc.Id && e.EventType == "Expired");

                if (daysUntilExpiration < 0 && eventExists == null)
                {
                    // EXPIRED: Block booking access
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

                    lock (_store.Sync)
                    {
                        var evt = new ComplianceExpirationEvent
                        {
                            Id = (_store.ExpirationEvents.Count > 0 ? _store.ExpirationEvents.Max(e => e.Id) : 0) + 1,
                            GroupId = group.Id,
                            DocumentId = doc.Id,
                            EventType = "Expired",
                            DocumentExpirationDate = doc.ExpirationDate.Value,
                            EventTriggeredAt = now,
                            NotificationSent = false
                        };
                        _store.ExpirationEvents.Add(evt);
                    }

                    var expiredBody = $"""
                        YOUR BOOKING ACCESS HAS BEEN BLOCKED
                        
                        Company: {group.Name}
                        Reason: {doc.DocumentType} expired on {doc.ExpirationDate:yyyy-MM-dd}
                        
                        IMMEDIATE ACTION REQUIRED:
                        To restore booking privileges, please upload a renewed {doc.DocumentType} document.
                        
                        Status: Booking Disabled
                        
                        Best regards,
                        Arizona Game & Fish Department
                        """;
                    
                    LogNotification(group.ContactEmail, $"URGENT: Booking Blocked - Compliance Expired: {group.Name}", expiredBody);

                    blocksCount++;
                }
                else if (daysUntilExpiration <= 60 && daysUntilExpiration > 0)
                {
                    // Send reminders at T-60, T-30, T-7
                    var reminderThresholds = new[] { 60, 30, 7 };

                    foreach (var threshold in reminderThresholds)
                    {
                        if (Math.Abs(daysUntilExpiration - threshold) < 1.0)  // Within 1 day of threshold
                        {
                            var eventKey = $"Reminder-{threshold}";
                            var reminderEvent = _store.ExpirationEvents.FirstOrDefault(e =>
                                e.GroupId == group.Id && e.DocumentId == doc.Id && e.EventType == eventKey);

                            if (reminderEvent == null)
                            {
                                lock (_store.Sync)
                                {
                                    var evt = new ComplianceExpirationEvent
                                    {
                                        Id = (_store.ExpirationEvents.Count > 0 ? _store.ExpirationEvents.Max(e => e.Id) : 0) + 1,
                                        GroupId = group.Id,
                                        DocumentId = doc.Id,
                                        EventType = eventKey,
                                        DocumentExpirationDate = doc.ExpirationDate.Value,
                                        EventTriggeredAt = now,
                                        NotificationSent = false
                                    };
                                    _store.ExpirationEvents.Add(evt);
                                }

                                var reminderBody = $"""
                                    Your {doc.DocumentType} will expire in {threshold} days.
                                    
                                    Expiration Date: {doc.ExpirationDate:yyyy-MM-dd}
                                    Current Date: {now:yyyy-MM-dd}
                                    
                                    To avoid booking interruption, please prepare and upload a renewed document.
                                    
                                    Best regards,
                                    Arizona Game & Fish Department
                                    """;
                                
                                LogNotification(group.ContactEmail, $"Compliance Reminder ({threshold} days): {group.Name}", reminderBody);

                                remindersCount++;
                            }
                        }
                    }
                }
            }
        }

        return (remindersCount, blocksCount);
    }

    // ==================== Helpers ====================

    /// <summary>Validate that coverage meets 1M/2M minimum.</summary>
    private bool ValidateCoverageLimits(string? coverage)
    {
        if (string.IsNullOrWhiteSpace(coverage))
            return false;
        
        // Simplified: check if coverage string contains "1M" or "1,000,000" or similar patterns
        // In production, parse the COI document metadata more rigorously
        return coverage.Contains("1M", StringComparison.OrdinalIgnoreCase)
               || coverage.Contains("1,000,000", StringComparison.OrdinalIgnoreCase)
               || coverage.Contains("$1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Get onboarding status for a corporate group's dashboard.</summary>
    public CorporateGroupOnboardingDto GetGroupOnboardingStatus(int groupId)
    {
        var group = _store.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group is null)
            return new();

        var docs = _store.ComplianceDocuments.Where(d => d.GroupId == groupId).ToList();
        var now = DateTime.UtcNow;

        var complianceSummary = "";
        var bookingBlockedReason = "";

        if (group.Status == AccountStatus.Active)
        {
            var expiredDocs = docs.Where(d => d.Status == DocumentStatus.Verified && d.ExpirationDate <= now).ToList();
            if (expiredDocs.Count > 0)
            {
                bookingBlockedReason = $"Compliance expired on {expiredDocs.First().ExpirationDate:yyyy-MM-dd}";
            }
            else
            {
                var nextExpiration = docs.Where(d => d.Status == DocumentStatus.Verified && d.ExpirationDate.HasValue)
                    .Min(d => d.ExpirationDate);
                if (nextExpiration.HasValue)
                    complianceSummary = $"Verified through {nextExpiration:yyyy-MM-dd}";
            }
        }
        else if (group.Status == AccountStatus.Suspended)
        {
            bookingBlockedReason = "Account suspended due to expired compliance documentation";
        }
        else if (group.Status == AccountStatus.PreApproved)
        {
            bookingBlockedReason = "Compliance documents required for activation";
        }

        var docDtos = docs.Select(d => new ComplianceDocumentDto
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

        return new CorporateGroupOnboardingDto
        {
            Id = group.Id,
            Name = group.Name,
            Tier = TierNames.Display(group.Tier),
            AccountStatus = group.Status.ToString(),
            ComplianceSummary = complianceSummary,
            Documents = docDtos,
            BookingEnabled = group.Status == AccountStatus.Active && string.IsNullOrEmpty(bookingBlockedReason),
            BookingBlockedReason = bookingBlockedReason
        };
    }

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
