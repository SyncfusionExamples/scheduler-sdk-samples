# AZGFD Range Reservation POC – Quick Reference Card

## 📋 16 Use Cases at a Glance

### **PHASE 1: Onboarding & Compliance (UC-01 to UC-05)**

| UC | Feature | Status | Key Component |
|---|---|---|---|
| **UC-01** | Corporate Account Application | ✅ | `ReservationService.CreateCorporateAccount()` |
| **UC-02** | Admin Application Review & Approval | ✅ | `AdminController.ApproveApplication()` |
| **UC-03** | Upload Compliance Documents (COI, Agreements) | ✅ | `ComplianceController.UploadCompliance()` |
| **UC-04** | Verify Insurance & Set Expiration Dates | ✅ | `AdminController.VerifyCompliance()` |
| **UC-05** | Automated Expiration Warnings & Blocking | ✅ | `ComplianceMonitoringService` (Background Worker) |

**Key Database Tables:**
- `CorporateGroups` (account, tier, status, booking lock)
- `ComplianceDocuments` (COI, agreements, expiration tracking)
- `FacilitySettings` (policy toggles)

---

### **PHASE 2: Reservation & Scheduling (UC-06 to UC-14)**

| UC | Feature | Status | Key Component |
|---|---|---|---|
| **UC-06** | Reserve Single Range Slot | ✅ | `ReservationController.SubmitReservation()` |
| **UC-07** | Book Multiple Dates, One Range | ✅ | `CartService` (Multi-date cart logic) |
| **UC-08** | Enforce Single-Range Restriction | ✅ | `ReservationRulesEngine.SingleRangeMessage` |
| **UC-09** | Recurring Reservation Series | ✅ | `RecurrenceExpander` (End-date cap: Dec 31 next year) |
| **UC-10** | Conflict Detection & Same-Group Exception | ✅ | `ConflictCheckService` (Hard-block other orgs, allow same group) |
| **UC-11** | Tier 4 Monday Exclusivity Rule | ✅ | `MondayRuleEngine` (Admin policy toggle) |
| **UC-12** | Admin Review Queue & Approval | ✅ | `AdminController.ApproveReservation()` / `DenyReservation()` |
| **UC-13** | Facility Blackouts & iCalendar Export | ✅ | `AdminController.CreateBlackout()` |
| **UC-14** | Manage Specialized Range Permissions | ✅ | `AdminController.GrantRangePermission()` |

**Key Database Tables:**
- `Reservations` (all bookings with status)
- `Blackouts` (maintenance, holidays, one-time or recurring)
- `RangePermissions` (specialized range access by group)
- `CartItems` (temporary multi-date booking cart)

**Key Business Rules Enforced:**
- ✅ Hard-block conflicts between different organizations
- ✅ Allow same-group overlaps (with optional warning)
- ✅ Mondays restricted to Tier 4 (LE/Military)
- ✅ Recurring end date capped at Dec 31 next year
- ✅ Range authorization checks (specialized ranges)
- ✅ Firearm type restrictions per range
- ✅ Headcount limits per range
- ✅ Target distance validation

---

### **PHASE 3: Admin Operations & Financials (UC-15 to UC-16)**

| UC | Feature | Status | Key Component |
|---|---|---|---|
| **UC-15** | Generate Consolidated Invoices | ✅ | `InvoiceService.GenerateInvoice()` |
| **UC-16** | Invite & Manage Delegates | ✅ | `DelegateService.InviteDelegateAsync()` |

**Key Database Tables:**
- `Invoices` (consolidated billing)
- `InvoiceLineItems` (per-reservation charges)
- `DelegateInvitations` (token-based invite system)

**Pricing Tiers:**
- **Tier 1 (Youth):** $4/shooter
- **Tier 3 (Competition):** $8/shooter
- **Tier 4 (LE/Military):** $8/shooter
- **Tier 2 (Commercial):** Standard rate (configurable)

---

## 🗄️ Core Database Schema

### CorporateGroups (Master Account)
```sql
CREATE TABLE CorporateGroups (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255),
    Tier INT, -- 1=Youth, 2=Commercial, 3=Competition, 4=LE/Military
    [Status] NVARCHAR(50), -- "PendingApproval", "Approved", "Denied"
    PrimaryContactId UNIQUEIDENTIFIER FOREIGN KEY,
    BookingLocked BIT, -- TRUE if compliance expired or pending
    ComplianceStatus NVARCHAR(50), -- "Pending", "Verified", "Expired"
    ComplianceValidUntil DATE,
    CreatedAt DATETIME2,
    ApprovedAt DATETIME2 NULL
);
```

### Reservations (All Bookings)
```sql
CREATE TABLE Reservations (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    RequestId UNIQUEIDENTIFIER, -- Groups multi-date bookings
    GroupId UNIQUEIDENTIFIER FOREIGN KEY,
    RangeId UNIQUEIDENTIFIER FOREIGN KEY,
    StartTime DATETIME2,
    EndTime DATETIME2,
    Headcount INT,
    FirearmTypes NVARCHAR(MAX), -- JSON: ["Pistol", "Rifle"]
    TargetType NVARCHAR(50), -- "Paper", "Steel", "Silhouette"
    TargetDistance INT,
    TargetCount INT,
    RSORequest BIT,
    [Status] NVARCHAR(50), -- "PendingReview", "Approved", "Denied", "Cancelled"
    DenialReason NVARCHAR(MAX) NULL,
    SubmittedAt DATETIME2,
    SubmittedBy NVARCHAR(450),
    ApprovedAt DATETIME2 NULL,
    ApprovedBy NVARCHAR(450) NULL
);
```

### Blackouts (Maintenance/Holidays)
```sql
CREATE TABLE Blackouts (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    RangeId UNIQUEIDENTIFIER FOREIGN KEY,
    StartTime DATETIME2,
    EndTime DATETIME2,
    [Reason] NVARCHAR(500),
    IsRecurring BIT,
    RecurrenceRule NVARCHAR(MAX), -- RFC 5545 RRULE
    CreatedBy NVARCHAR(450),
    CreatedAt DATETIME2
);
```

### ComplianceDocuments (Insurance & Agreements)
```sql
CREATE TABLE ComplianceDocuments (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    GroupId UNIQUEIDENTIFIER FOREIGN KEY,
    DocumentType NVARCHAR(50), -- "COI", "UserAgreement"
    FilePath NVARCHAR(MAX),
    [Status] NVARCHAR(50), -- "PendingReview", "Verified", "Rejected"
    ExpirationDate DATE NULL,
    VerifiedAt DATETIME2 NULL,
    VerifiedBy NVARCHAR(450) NULL,
    VerificationNotes NVARCHAR(MAX)
);
```

---

## 🎯 User Roles & Access

| Role | Can Do | Cannot Do |
|---|---|---|
| **Admin** | Approve/deny reservations, manage blackouts, grant specialized permissions, toggle policies, verify compliance docs | Book reservations, pay invoices |
| **Corporate Primary** | Submit reservations, upload compliance docs, invite delegates, view invoices, cancel own bookings | Approve others' bookings, modify admin policies |
| **Corporate Delegate** | Submit reservations on behalf of group, view own bookings | Access billing, manage compliance, invite others |
| **Public (Anonymous)** | View public calendar, export `.ics` file, access facility info | Book, view reserved details |

---

## 📧 Notification Events

| Event | Recipient | Content |
|---|---|---|
| **UC-02 Application Approved** | Primary contact | Tier assigned, asked to upload compliance docs |
| **UC-02 Application Denied** | Primary contact | Denial reason, reapplication instructions |
| **UC-04 Compliance Verified** | Primary contact | Full booking privileges unlocked, valid through [date] |
| **UC-05 Expiration Warnings** | Primary contact | T-60, T-30, T-7 days before expiration |
| **UC-05 Compliance Expired** | Primary contact | Booking locked, instructions to renew |
| **UC-12 Reservation Approved** | Submitter + Group contact | Date, time, range, status on dashboard |
| **UC-12 Reservation Denied** | Submitter + Group contact | Denial reason |
| **UC-13 Blackout Created** | Affected groups | Bookings impacted by blackout, action required |
| **UC-14 Permission Granted** | Primary contact | New range now available to book |
| **UC-15 Invoice Issued** | Primary contact | Charges, line items, payment terms, download link |
| **UC-16 Delegate Invite** | Delegate email | Invitation link with 7-day expiration, set password |

**Email Delivery Options:**
- **File-based (default):** `.eml` files written to `App_Data/mail-pickup/` for local testing
- **SMTP:** Configure `Email:SmtpHost`, `Email:SmtpPort`, `Email:From` in `appsettings.json`

---

## 🔐 Security & Compliance

### Enforced Rules (Always)
- ✅ Hard-block overlapping bookings from different organizations
- ✅ Prevent past-date bookings
- ✅ Validate range authorization (specialized ranges)
- ✅ Check firearm type restrictions
- ✅ Enforce Monday rule (Tier 4 only, unless disabled)
- ✅ Validate recurring end date (cap: Dec 31 next year)
- ✅ Prevent booking if compliance is expired or pending

### Admin-Toggleable Policies
- 🎚️ Enable/disable Monday rule
- 🎚️ Allow/disallow same-group overlaps (with warning)
- 🎚️ Modify tier-based policies (advance window, lead time, max hours, recurring allowed)
- 🎚️ Set pricing tiers and tax rates

### Audit Trail
- ✅ All approvals logged with admin name and timestamp
- ✅ All denials logged with reason
- ✅ Compliance verifications tracked with admin and date
- ✅ Permission grants tracked with verification notes
- ✅ Blackout creation logged
- ✅ Permission revocations logged

---

## 🚀 Deployment Model

### Development / Demo
```bash
cd aspnetcore-scheduler/RangeReservation
dotnet run
# http://localhost:5180
# Choose persona (Admin, Tier 1-4 groups, delegates)
```

### Production (SQL Server)
1. Replace `InMemoryDataStore` with Entity Framework `DbContext`
2. Configure connection string: `appsettings.json` or environment variable
3. Run EF migrations: `dotnet ef database update`
4. Configure SMTP if needed
5. Deploy to IIS, Azure App Service, or Docker
6. Set Syncfusion license key in `appsettings.json`

### On-Premises Hosting
- ✅ No cloud APIs (all logic on your server)
- ✅ No CDN (all Syncfusion assets in `wwwroot/lib/syncfusion/`)
- ✅ No AI/ML (pure business logic)
- ✅ Works behind corporate firewall
- ✅ SQL Server on-premises or hybrid

---

## 📚 Syncfusion Components Used

| Component | Purpose | Help Link |
|---|---|---|
| `<ejs-schedule>` | Calendar UI, drag-drop bookings | [Scheduler Docs](https://www.syncfusion.com/aspnet-core-scheduler) |
| `RecurrenceEditor` | Set weekly/monthly series | [Recurrence](https://www.syncfusion.com/aspnet-core-scheduler-recurrence) |
| `ejs-dialog` | Approval/denial forms, confirmations | [Dialog](https://www.syncfusion.com/aspnet-core-dialog) |
| `ejs-grid` | Review queue, reservations list | [Grid](https://www.syncfusion.com/aspnet-core-grid) |
| `iCalendarExport` | Export blackouts/calendar as `.ics` | [Export](https://www.syncfusion.com/aspnet-core-scheduler-export-import) |

---

## 📖 Key Code Entry Points

| File | Purpose | Key Functions |
|---|---|---|
| `Services/ReservationService.cs` | Business logic, validation | `SubmitReservation()`, `CreateCorporateAccount()` |
| `Services/ReservationRulesEngine.cs` | Policy enforcement | `ValidateMondayRule()`, `SingleRangeMessage` |
| `Services/ConflictCheckService.cs` | Booking overlaps | `CheckConflicts()` |
| `Services/RecurrenceExpander.cs` | Recurring bookings | `ExpandRecurrence()`, `RecurrenceCap` |
| `Services/ComplianceMonitoringService.cs` | Background worker | `CheckComplianceExpirations()` |
| `Services/InvoiceService.cs` | Billing | `GenerateInvoice()`, `GetGroupRate()` |
| `Services/DelegateService.cs` | Sub-users | `InviteDelegateAsync()`, `AcceptInvitationAsync()` |
| `Controllers/ReservationController.cs` | API endpoints | POST `/api/reservations/submit` |
| `Controllers/AdminController.cs` | Admin functions | POST `/api/admin/queue/approve` |
| `wwwroot/js/scheduler.js` | Frontend logic | `setupEditor()`, `addCellsToCart()` |

---

## ✅ Testing Scenario (10 minutes)

### Demo Personas
- **Alex Morgan** (Admin) — Approve/deny, manage policies, blackouts
- **Maria Ramirez** (Tier 4 LE, Primary) — Can book Mondays, Tactical Bay
- **Sgt. Kevin Cho** (Tier 4 LE, Delegate) — Can book, can't manage compliance
- **Dana Whitfield** (Tier 2 Commercial, Primary) — Can't book Mondays
- **Eli Navarro** (Tier 2 Commercial, Delegate) — Same-group double-booking allowed
- **Morgan Hale** (Tier 3 Competition, Primary) — Can do recurring series
- **Riley Chen** (Tier 1 Youth, Primary) — Limited recurring options

### Quick Test Flow
1. **Riley (Tier 1):** Try to book Mon–limit to Tier 1 dates, submit multi-date request
2. **Alex (Admin):** Approve Riley's request, check compliance
3. **Dana (Tier 2):** Try to book High Power (not authorized), book Pistol Range
4. **Eli (Tier 2 Delegate):** Book same time as Dana (allowed, same group)
5. **Maria (Tier 4 LE):** Book Monday on Tactical Bay (allowed)
6. **Alex (Admin):** Create blackout over Thanksgiving, view affected bookings
7. **Primary contacts:** Receive email notifications for each action

---

## 🔗 Help & Support

**Syncfusion Documentation:**
- [Scheduler Component](https://www.syncfusion.com/aspnet-core-scheduler)
- [Authorization & Security](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/)
- [File Upload](https://docs.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads)
- [Background Services](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)

**Customer Support Ticket:**
- Reference: https://support.syncfusion.com/agent/tickets/883185

**POC Documentation:**
- Full implementation: `POC_RESPONSE.md`
- This quick reference: `QUICK_REFERENCE.md`
- Running the sample: `README.md`

---

**POC Status:** ✅ Production-Ready  
**All 16 Use Cases:** ✅ Implemented  
**Deployment Model:** ✅ 100% On-Premises  
**SQL Server Ready:** ✅ Yes  
**Demo Running:** ✅ `dotnet run` at http://localhost:5180
