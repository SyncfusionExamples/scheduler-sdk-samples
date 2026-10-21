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
    private readonly NotificationService _notifications;

    public OnboardingService(InMemoryDataStore store, NotificationService notifications)
    {
        _store = store;
        _notifications = notifications;
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
        _notifications.SendNotification(
            toEmail: app.PrimaryContactEmail,
            toName: app.PrimaryContactName,
            subject: $"Application Received: {app.CompanyName}",
            body: $"""
                Dear {app.PrimaryContactName},
                
                Thank you for submitting your corporate account application for {app.CompanyName}.
                
                Application ID: {app.Id}
                Status: Pending Review
                
                An AZGFD Administrator will review your application and contact you within 3-5 business days.
                
                Best regards,
                Arizona Game & Fish Department
                """
        );

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

            _store.AddGroup(group);
            app.UpdatedAt = DateTime.UtcNow;

            // Send approval notification
            _notifications.SendNotification(
                toEmail: app.PrimaryContactEmail,
                toName: app.PrimaryContactName,
                subject: $"Application Approved: {app.CompanyName}",
                body: $"""
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
                    """
            );
        }
        else
        {
            app.Status = AccountStatus.Denied;
            app.DenialReason = input.DenialReason ?? "Application denied by administrator.";
            app.UpdatedAt = DateTime.UtcNow;

            // Send denial notification
            _notifications.SendNotification(
                toEmail: app.PrimaryContactEmail,
                toName: app.PrimaryContactName,
                subject: $"Application Denied: {app.CompanyName}",
                body: $"""
                    Dear {app.PrimaryContactName},
                    
                    We regret to inform you that your corporate account application has been denied.
                    
                    Reason: {app.DenialReason}
                    
                    If you have questions, please contact the Arizona Game & Fish Department.
                    
                    Best regards,
                    Arizona Game & Fish Department
                    """
            );
        }

        _store.UpdateApplication(app);
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

        var group = _store.GetGroup(input.GroupId);
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

        _store.AddComplianceDocument(doc);

        // Notify admin that a document is waiting for review
        _notifications.SendNotification(
            toEmail: "admin@azgfd.example.com",  // Would be configured admin email
            toName: "AZGFD Administrator",
            subject: $"New Compliance Document for Review: {group.Name}",
            body: $"""
                A new compliance document has been uploaded and is awaiting verification.
                
                Company: {group.Name}
                Document Type: {doc.DocumentType}
                File Name: {doc.FileName}
                Uploaded By: {uploader.Name}
                Uploaded At: {doc.UploadedAt:F}
                
                Please review and verify the document in the admin dashboard.
                """
        );

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

        var doc = _store.GetComplianceDocument(input.DocumentId);
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

        var group = _store.GetGroup(doc.GroupId);
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

            _store.UpdateComplianceDocument(doc);

            // Check if all required compliance docs are now verified
            var allDocs = _store.GetComplianceDocuments(doc.GroupId);
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
                _store.UpdateGroup(group);

                // Notify the corporate user that their account is now active
                _notifications.SendNotification(
                    toEmail: group.ContactEmail,
                    toName: "Corporate Account",
                    subject: $"Account Activated: {group.Name}",
                    body: $"""
                        Your corporate account has been fully activated!
                        
                        Company: {group.Name}
                        Status: Active
                        Booking Enabled: Yes
                        
                        You can now start booking range slots through the portal.
                        Your compliance documents are valid through {doc.ExpirationDate:yyyy-MM-dd}.
                        
                        Best regards,
                        Arizona Game & Fish Department
                        """
                );
            }
        }
        else
        {
            // Rejection: document needs to be re-uploaded
            doc.Status = DocumentStatus.Rejected;
            doc.VerifiedBy = admin.Name;
            doc.VerifiedAt = DateTime.UtcNow;
            doc.ReviewNotes = input.ReviewNotes;
            _store.UpdateComplianceDocument(doc);

            // Notify the corporate user to re-upload
            _notifications.SendNotification(
                toEmail: group.ContactEmail,
                toName: "Corporate Account",
                subject: $"Compliance Document Rejected: {group.Name}",
                body: $"""
                    Your submitted compliance document has been rejected and requires resubmission.
                    
                    Document: {doc.FileName}
                    Reason: {input.ReviewNotes}
                    
                    Please upload a corrected version through your portal.
                    
                    Best regards,
                    Arizona Game & Fish Department
                    """
            );
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

        var allGroups = _store.GetAllGroups();
        var allDocs = _store.GetAllComplianceDocuments();

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
                var eventExists = _store.GetComplianceExpirationEvent(group.Id, doc.Id, "Expired");

                if (daysUntilExpiration < 0 && !eventExists)
                {
                    // EXPIRED: Block booking access
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
                        toName: "Corporate Account",
                        subject: $"URGENT: Booking Blocked - Compliance Expired: {group.Name}",
                        body: $"""
                            YOUR BOOKING ACCESS HAS BEEN BLOCKED
                            
                            Company: {group.Name}
                            Reason: {doc.DocumentType} expired on {doc.ExpirationDate:yyyy-MM-dd}
                            
                            IMMEDIATE ACTION REQUIRED:
                            To restore booking privileges, please upload a renewed {doc.DocumentType} document.
                            
                            Status: Booking Disabled
                            
                            Best regards,
                            Arizona Game & Fish Department
                            """
                    );

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
                            var eventExists2 = _store.GetComplianceExpirationEvent(group.Id, doc.Id, eventKey);

                            if (!eventExists2)
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
                                    toName: "Corporate Account",
                                    subject: $"Compliance Reminder ({threshold} days): {group.Name}",
                                    body: $"""
                                        Your {doc.DocumentType} will expire in {threshold} days.
                                        
                                        Expiration Date: {doc.ExpirationDate:yyyy-MM-dd}
                                        Current Date: {now:yyyy-MM-dd}
                                        
                                        To avoid booking interruption, please prepare and upload a renewed document.
                                        
                                        Best regards,
                                        Arizona Game & Fish Department
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

    // ==================== Helpers ====================

    /// <summary>Validate that coverage meets 1M/2M minimum.</summary>
    private bool ValidateCoverageLimits(string coverage)
    {
        // Simplified: check if coverage string contains "1M" or "1,000,000" or similar patterns
        // In production, parse the COI document metadata more rigorously
        return coverage.Contains("1M", StringComparison.OrdinalIgnoreCase)
               || coverage.Contains("1,000,000", StringComparison.OrdinalIgnoreCase)
               || coverage.Contains("$1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Get onboarding status for a corporate group's dashboard.</summary>
    public CorporateGroupOnboardingDto GetGroupOnboardingStatus(int groupId)
    {
        var group = _store.GetGroup(groupId);
        if (group is null)
            return new();

        var docs = _store.GetComplianceDocuments(groupId);
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
            DocumentStatus.Verified when doc.ExpirationDate <= now => "Expired",
            DocumentStatus.Verified when (doc.ExpirationDate - now).TotalDays <= 7 => "Expiring Soon",
            DocumentStatus.Verified => "Verified",
            DocumentStatus.PendingReview => "Pending Review",
            DocumentStatus.Rejected => "Rejected",
            _ => doc.Status.ToString()
        };
    }
}
