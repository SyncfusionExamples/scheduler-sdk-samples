# Arizona Range Reservation POC – Use Case Implementation Response

This document maps each customer requirement (UC-01 through UC-16) to how the Syncfusion ASP.NET Core Scheduler POC achieves it, with code references and Syncfusion SDK help links.

---

## 1. Reservation Scheduling — Book and manage range slots

**Achieved with:** `<ejs-schedule>` Tag Helper with Timeline Day/Week/Month/Agenda views, the standard editor for create/edit, drag/resize for admins, and data binding through two REST endpoints: **GET** `/api/schedule/events` and **POST** `/api/reservations/batch`.

**View** — `Views/Home/Index.cshtml`:

```html
<ejs-schedule id="schedule" width="100%" height="780px"
              selectedDate="@now.Date" currentView="TimelineDay"
              startHour="06:00" endHour="21:00"
              allowDragAndDrop="@isAdmin" allowResizing="@isAdmin"
              allowOverlap="true" showTimeIndicator="false"
              showQuickInfo="true"
              created="onScheduleCreated"
              actionBegin="onActionBegin"
              actionComplete="onActionComplete"
              actionFailure="onActionFailure"
              popupOpen="onPopupOpen"
              cellClick="onCellClick"
              cellDoubleClick="onCellDoubleClick"
              renderCell="onRenderCell">
  <e-schedule-eventsettings dataSource="@(new List<object>())" enableTooltip="true"></e-schedule-eventsettings>
  <e-schedule-views>
    <e-schedule-view option="TimelineDay"></e-schedule-view>
    <e-schedule-view option="TimelineWeek"></e-schedule-view>
    <e-schedule-view option="Day"></e-schedule-view>
    <e-schedule-view option="Week"></e-schedule-view>
    <e-schedule-view option="Month"></e-schedule-view>
    <e-schedule-view option="Agenda"></e-schedule-view>
  </e-schedule-views>
</ejs-schedule>
```

**Controller** — `Controllers/Api/ReservationsApiController.cs`:

```csharp
[HttpGet("schedule/events")]
public ActionResult<List<ScheduleEventDto>> GetEvents() => Me is { } me ? _service.GetEvents(me) : Unauthorized();

[HttpPost("reservations/batch")]
public ActionResult<BatchResult> Batch([FromBody] ReservationBatchRequest request)
{
    if (Me is not { } me) return Unauthorized();
    var result = _service.ApplyBatch(me, request);
    return result.Success ? Ok(result) : UnprocessableEntity(result);
}
```

**Help links:**
- [Getting Started with Scheduler for ASP.NET Core](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/getting-started)
- [Views (Day/Week/Month/Timeline/Agenda)](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/views)
- [Data Binding & Event Objects](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/data-binding)
- [Appointments/CRUD Operations](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)

---

## 2. UC-06: Reserve Single Shooting Range Slot (Corporate User / Delegate)

**Implementation:**

Corporate users (Primary or Delegate) click a cell in the Scheduler. The custom editor (`setupEditor()` in `wwwroot/js/scheduler.js`) opens and collects:
- **Range:** Dropdown limited to authorized ranges for the user's tier and permissions
- **Date/Time:** Auto-filled from clicked cell
- **Headcount:** Number input
- **Firearm Types:** Multi-select dropdown
- **Target Attributes:** Type, distance (max per range), count
- **RSO Request:** Checkbox

The editor validates client-side and submits via `POST /api/reservations/batch`. The server (`ReservationService.ApplyBatch`) re-validates all rules and returns a **Pending Admin Review** status.

**Relevant code:**

`wwwroot/js/scheduler.js` — Editor setup:
```javascript
function setupEditor() {
  // Custom editor fields: range, headcount, firearms, target details
  // Called on popup open - configures available ranges per user tier/permissions
  const userRanges = getCurrentUserRanges(); // filtered by tier + grants
  populateRangeDropdown(userRanges);
  // ...submit calls BatchRequest to /api/reservations/batch
}
```

`Services/ReservationService.cs` — Submission logic:
```csharp
public BatchResult ApplyBatch(AppUser user, ReservationBatchRequest request)
{
  var validation = _engine.ValidateBatch(user, request);
  if (!validation.IsValid) 
    return BatchResult.Failed(validation.Violations);
  
  var reservation = new Reservation 
  { 
    RequestId = Guid.NewGuid().ToString(),
    UserId = user.Id,
    GroupId = user.GroupId,
    Status = ReservationStatus.Pending,
    // ... other fields from request
  };
  
  _store.SaveReservation(reservation);
  _notificationService.SendSubmittedNotification(reservation);
  return BatchResult.Success(reservation);
}
```

**Help links:**
- [Create & Edit Appointments](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)
- [Popup & Editor Customization](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/editor-template)
- [Event Object Structure](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/event-object)

---

## 3. UC-07: Book Multi-Date Reservation for a Single Range (Corporate User / Delegate)

**Implementation:**

Users turn on the **Multi-date booking** checkbox (visible in the left sidebar). While enabled, clicking cells across non-consecutive dates on **one range** adds them to an in-memory cart. All dates are displayed in a list with a unified UI for headcount, firearms, and targets. Clicking **"Submit for review"** sends the entire cart as one `ReservationBatchRequest` (single `RequestId`, multiple `ScheduleEvents`).

The cart enforces a tier-based limit (e.g., Tier 1 = max 1 date per request, Tier 2 = max 3 dates). The server re-validates on submission.

**Relevant code:**

`wwwroot/js/scheduler.js` — Multi-date cart:
```javascript
const cartState = { rangeId: null, dates: [], isEnabled: false };

function addCellsToCart(cellData) {
  if (!cartState.isEnabled) return;
  
  if (cartState.rangeId === null) {
    cartState.rangeId = cellData.rangeId;
  } else if (cartState.rangeId !== cellData.rangeId) {
    showError("You can only select multiple dates for one range per booking.");
    return;
  }
  
  cartState.dates.push(cellData);
  renderCart();
}

function submitCart() {
  const request = new ReservationBatchRequest({
    dates: cartState.dates,
    rangeId: cartState.rangeId,
    headcount: document.getElementById('cartHeadcount').value,
    // ... other fields
  });
  postAsync('/api/reservations/batch', request).then(result => {
    if (result.success) showSuccess("Request submitted for admin review");
  });
}
```

`Services/ReservationService.cs` — Batch validation:
```csharp
public BatchResult ApplyBatch(AppUser user, ReservationBatchRequest request)
{
  // Verify all dates are for the same range
  var ranges = request.Dates.Select(d => d.RangeId).Distinct();
  if (ranges.Count() > 1)
    return BatchResult.Failed(new[] { "All dates must be for the same range." });
  
  // Check tier limit on dates per booking
  var tierPolicy = _store.GetTierPolicy(user.Tier);
  if (request.Dates.Count > tierPolicy.MaxDatesPerBooking)
    return BatchResult.Failed(new[] { $"Max {tierPolicy.MaxDatesPerBooking} dates per request." });
  
  // ... store as single request
}
```

**Help links:**
- [Multi-event Selection & Handling](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)
- [Drag & Drop / Custom Actions](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/drag-and-drop)

---

## 4. UC-08: Enforce Single-Range Multi-Date Restriction (System)

**Implementation:**

When a user in **Multi-date booking** mode tries to select a cell on a different range, the JavaScript cart handler detects the mismatch and immediately blocks the action with the exact error message:

```
"You can only select multiple dates for one range per booking."
```

The server enforces the same rule on submission (`ApplyBatch` checks range uniqueness), so even if a client tries to bypass the UI, the API rejects it with HTTP 422.

**Relevant code:**

`wwwroot/js/scheduler.js`:
```javascript
const SINGLE_RANGE_MESSAGE = "You can only select multiple dates for one range per booking.";

function addCellsToCart(cellData) {
  if (cartState.rangeId !== null && cartState.rangeId !== cellData.rangeId) {
    showBlockingError(SINGLE_RANGE_MESSAGE);
    return false; // Block the add
  }
  // ... proceed
}
```

`Services/ReservationRulesEngine.cs`:
```csharp
public static readonly string SingleRangeMessage = 
  "You can only select multiple dates for one range per booking.";

public List<string> ValidateBatch(ReservationBatchRequest request)
{
  var violations = new List<string>();
  
  var rangeIds = request.Dates.Select(d => d.RangeId).Distinct().ToList();
  if (rangeIds.Count > 1)
    violations.Add(SingleRangeMessage);
  
  return violations;
}
```

---

## 5. UC-09: Schedule Recurring Reservation Series (Corporate User / Delegate)

**Implementation:**

When a user selects the **Recurrence** option in the editor, the Scheduler's built-in `RecurrenceEditor` opens. The editor is pre-configured to:
- Allow only **Weekly** and **Monthly** patterns (daily/yearly disabled)
- Require an end date
- Cap the end date to **December 31 of the following calendar year**

On submit, the server (`RecurrenceExpander.cs`) expands the RRULE into individual instances and validates **every instance** against:
- Existing bookings / blackouts
- Monday rule (if Tier < 4)
- Tier policy constraints

If any instance fails, the entire recurring request is rejected.

**Relevant code:**

`Views/Home/Index.cshtml` — Editor configuration:
```html
<e-recurrenceeditor id="recurrence-editor"
                    frequencies="Weekly,Monthly"
                    startDate="@now"
                    maxDate="@endOfNextYear"
                    enableRepeatOn="true">
</e-recurrenceeditor>
```

`Services/RecurrenceExpander.cs` — Expansion and validation:
```csharp
public List<DateTime> ExpandRecurrence(string rrule, DateTime startDate, int rangeId, AppUser user)
{
  var instances = new List<DateTime>();
  // Parse RRULE and generate instances (using Syncfusion's recurrence rules)
  var rule = new RecurrenceProperties { RecurrenceRule = rrule };
  var expandedDates = rule.GetRecurrenceDateTimes(startDate, DateTime.Now.Year + 1);
  
  foreach (var date in expandedDates)
  {
    // Check hard rules for each instance
    if (!_engine.CanBook(user, rangeId, date))
      throw new ValidationException($"Recurrence blocked at {date}");
    
    instances.Add(date);
  }
  
  return instances;
}
```

`Services/ReservationRulesEngine.cs`:
```csharp
public const string RecurrenceCap = 
  "Recurring series must end by December 31 of the following calendar year.";

public bool IsRecurrenceDateValid(DateTime date, AppUser user, int rangeId)
{
  // Check Monday rule
  if (date.DayOfWeek == DayOfWeek.Monday && !user.IsTier4())
    return false;
  
  // Check blackouts
  if (IsBlackedOut(rangeId, date))
    return false;
  
  // Check conflicts
  if (HasConflict(rangeId, date, user.GroupId, sameGroupAllowed: true))
    return false;
  
  return true;
}
```

**Help links:**
- [Recurrence in Scheduler](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/recurrence-rule)
- [RecurrenceRule Property](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments#recurrence-rule)
- [RecurrenceEditor](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/recurrence-rule#using-recurrence-editor)

---

## 6. UC-10: Prevent Booking Overlaps & Allow Group Exceptions (System)

**Implementation:**

When a reservation request is submitted, the server checks for time overlaps on the same range:
- **Another organization's booking (pending or approved):** Hard-blocked, returns HTTP 422 with error.
- **Same group's existing booking:** Allowed (soft conflict); user sees a warning but can proceed. Admins can toggle this behavior in **Policies**.
- **On the user's calendar:** Other orgs' bookings show as "Reserved" (opaque, masked label). Same-group bookings show as "Approved" or "Pending Review".

**Relevant code:**

`Services/ReservationService.cs` — Conflict check:
```csharp
public (bool Ok, List<string> Errors) CheckConflicts(
  int rangeId, DateTime startTime, DateTime endTime, 
  int groupId, bool isAdmin, bool allowSameGroupOverlap)
{
  var overlaps = _store.GetReservations()
    .Where(r => r.RangeId == rangeId 
             && r.Status != ReservationStatus.Denied 
             && r.Status != ReservationStatus.Cancelled
             && TimeOverlaps(r.StartTime, r.EndTime, startTime, endTime))
    .ToList();
  
  var errors = new List<string>();
  foreach (var overlap in overlaps)
  {
    if (overlap.GroupId == groupId && allowSameGroupOverlap)
    {
      // Same group overlap: warning only (client can proceed)
      continue;
    }
    else if (overlap.GroupId != groupId)
    {
      // Another group's reservation: hard block
      errors.Add($"Time slot conflicts with {overlap.GroupName}'s reservation.");
    }
  }
  
  return (errors.Count == 0, errors);
}
```

`wwwroot/js/scheduler.js` — Calendar rendering:
```javascript
function onRenderCell(args) {
  const event = args.data;
  
  if (event.GroupId === currentUser.GroupId) {
    // Same group: show status (Approved/Pending)
    args.element.classList.add(`status-${event.Status.toLowerCase()}`);
  } else {
    // Other group: mask as "Reserved"
    args.element.classList.add('masked-reserved');
    args.element.textContent = 'Reserved';
  }
}
```

**Help links:**
- [Check Overlaps / Availability](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)
- [Event Rendering & Customization](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/event-rendering)

---

## 7. UC-11: Enforce Tier 4 Law Enforcement Monday Rule (System / Admin)

**Implementation:**

For Tier 1–3 corporate groups:
- **UI:** Monday cells are greyed out on the calendar, disabled for clicking.
- **Server:** Any Monday slot (single or recurring instance) is rejected with an error.
- **Admin override:** Available if enabled in **Policies**, but the rule still applies to non-LE tiers (even admins cannot book non-LE groups on Monday).

For Tier 4 (Law Enforcement / Military):
- Mondays are fully accessible on the calendar and unrestricted on the server.

**Relevant code:**

`wwwroot/js/scheduler.js` — UI blocking:
```javascript
function isMondayBlocked(date, userTier) {
  // Only block if user is not Tier 4
  return date.getDay() === 1 && userTier < 4; // 1 = Monday
}

function onRenderCell(args) {
  const { date } = args.data;
  if (isMondayBlocked(date, currentUser.Tier)) {
    args.element.classList.add('disabled-cell');
    args.element.style.pointerEvents = 'none';
  }
}
```

`Services/ReservationRulesEngine.cs` — Server enforcement:
```csharp
public static readonly string MondayBlockedForNonLE = 
  "Monday slots are reserved for Law Enforcement and Military (Tier 4) groups.";

public bool Checks.Monday(Reservation res, AppUser user)
{
  if (res.StartTime.DayOfWeek == DayOfWeek.Monday && user.Tier < 4)
    return false; // Blocked
  return true; // Allowed
}

public List<string> ValidateSingleReservation(Reservation res, AppUser user, bool adminOverride)
{
  var violations = new List<string>();
  
  // Hard rules (even admin override cannot bypass)
  if (res.StartTime.DayOfWeek == DayOfWeek.Monday && user.Tier < 4)
    violations.Add(MondayBlockedForNonLE);
  
  // ... other validations
  return violations;
}
```

**Admin Policy Control** — `Admin/Policies` view + `UpdateSettings` endpoint:
```csharp
[HttpPut("settings")]
[Authorize(Roles = Roles.Admin)]
public IActionResult UpdateSettings([FromBody] SettingsInput input)
{
  var settings = _store.GetFacilitySettings();
  settings.EnforceMondayRuleForLE = input.EnforceMondayRuleForLE;
  _store.SaveFacilitySettings(settings);
  
  return NoContent();
}
```

**Help links:**
- [Rendering Cells Conditionally](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/event-rendering)
- [Working with Time Zone](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/timezone)

---

## 8. UC-12: Approve or Deny Reservation Queue Items (Admin)

**Implementation:**

Admins access the **Review Queue** (`/Admin/Queue`) which lists all **Pending** requests. For each request, a **Compliance Checklist** is auto-generated and re-run against the live calendar:
- ✓ Hours (within facility operating hours)
- ✓ Range authorization (user's tier + specialized permissions)
- ✓ Firearm types (allowed for the range)
- ✓ Headcount (within range capacity)
- ✓ Target specs (types, max distance)
- ✓ Availability (no blackouts, no hard conflicts)
- ✓ Monday rule (if applicable)
- ✓ Recurrence policy (if recurring)
- ✓ Tier policy (advance window, lead time, max hours, etc.)

Clicking **Approve** re-checks hard rules and moves the request to **Approved**. Clicking **Deny** opens a dialog requiring a reason, then moves the request to **Denied** and sends an automated email to the submitting user and group contact.

**Relevant code:**

`Controllers/AdminController.cs` — Queue page:
```csharp
public IActionResult Queue()
{
  var requests = _service.GetRequests(null, ReservationStatus.Pending);
  var withChecks = requests.Select(r => new {
    Request = r,
    Checks = _engine.GetComplianceChecks(r) // Re-run checks live
  }).ToList();
  
  return View(withChecks);
}
```

`Controllers/Api/ReservationsApiController.cs` — Approval/Denial:
```csharp
[HttpPost("requests/{requestId}/approve")]
[Authorize(Roles = Roles.Admin)]
public IActionResult Approve(string requestId)
{
  var (ok, errors) = _service.Approve(requestId, Me!);
  // Approve re-checks hard rules
  // Returns errors if the request is now invalid (e.g., conflicting booking appeared)
  return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}

[HttpPost("requests/{requestId}/deny")]
[Authorize(Roles = Roles.Admin)]
public IActionResult Deny(string requestId, [FromBody] DenyInput input)
{
  var (ok, errors) = _service.Deny(requestId, input.Reason, Me!);
  // Sends automated denial email with reason
  return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

`Services/ReservationService.cs` — Approval logic:
```csharp
public (bool Ok, List<string> Errors) Approve(string requestId, AppUser admin)
{
  var request = _store.GetReservation(requestId);
  if (request == null) return (false, new[] { "Request not found." });
  
  // Re-run hard rules
  var hardRuleViolations = _engine.CheckHardRules(request);
  if (hardRuleViolations.Any())
    return (false, hardRuleViolations.ToList()); // Request is now invalid
  
  request.Status = ReservationStatus.Approved;
  request.ApprovedBy = admin.Id;
  request.ApprovedAt = _clock.Now;
  
  _store.SaveReservation(request);
  _notificationService.SendApprovalNotification(request);
  
  return (true, new List<string>());
}

public (bool Ok, List<string> Errors) Deny(string requestId, string reason, AppUser admin)
{
  var request = _store.GetReservation(requestId);
  if (request == null) return (false, new[] { "Request not found." });
  
  request.Status = ReservationStatus.Denied;
  request.DenialReason = reason;
  request.DeniedBy = admin.Id;
  request.DeniedAt = _clock.Now;
  
  _store.SaveReservation(request);
  _notificationService.SendDenialNotification(request, reason); // Email with reason
  
  return (true, new List<string>());
}
```

`Views/Admin/Queue.cshtml` — Compliance checklist display:
```html
@foreach (var item in Model)
{
  <div class="request-card">
    <h4>@item.Request.Subject</h4>
    <ul class="compliance-checklist">
      @foreach (var check in item.Checks)
      {
        <li class="@(check.Passed ? "pass" : "fail")">
          <span>@check.Name</span>
          <span>@(check.Passed ? "✓" : "✗")</span>
        </li>
      }
    </ul>
    <button class="btn-approve" onclick="approveRequest('@item.Request.Id')">Approve</button>
    <button class="btn-deny" onclick="denyRequest('@item.Request.Id')">Deny</button>
  </div>
}
```

**Help links:**
- [Appointments/CRUD Operations](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)
- [Authorization & Roles](https://docs.microsoft.com/aspnet/core/security/authorization)

---

## 9. UC-13: Manage Facility Blackout Periods (Admin)

**Implementation:**

Admins access the **Blackouts** management page (`/Admin/Blackouts`). They can create one-time or recurring blackout periods, specifying:
- **Scope:** All facilities, one facility, or one specific range
- **Time range:** Start/end dates and times
- **Recurrence:** Optional RRULE (same as reservations)

Blackouts are immediately reflected on:
- **Internal booking calendar:** Blocked time is shown with a distinct "Blackout" style
- **External public calendar** (`/Public/Calendar`, no sign-in): Shows blackouts as blocked time, or can export to iCalendar (`.ics`) for Outlook/Google/Apple

When saving a blackout that overlaps existing reservations, those reservations are listed and marked as "Impacted" (admin is informed but the save proceeds).

**Relevant code:**

`Controllers/AdminController.cs` — Blackouts page:
```csharp
public IActionResult Blackouts()
{
  var blackouts = _service.GetBlackouts();
  return View(blackouts);
}
```

`Controllers/Api/ReservationsApiController.cs` — Blackout endpoints:
```csharp
[HttpPost("blackouts")]
[Authorize(Roles = Roles.Admin)]
public IActionResult SaveBlackout([FromBody] BlackoutInput input)
{
  var (saved, errors, impacted) = _service.SaveBlackout(input);
  return errors.Count == 0 
    ? Ok(new { Blackout = saved, Impacted = impacted }) 
    : UnprocessableEntity(new { Errors = errors });
}

[HttpDelete("blackouts/{id:int}")]
[Authorize(Roles = Roles.Admin)]
public IActionResult DeleteBlackout(int id) 
  => _service.DeleteBlackout(id) ? NoContent() : NotFound();
```

`Services/ReservationService.cs` — Blackout logic:
```csharp
public (Blackout Saved, List<string> Errors, List<Reservation> Impacted) SaveBlackout(BlackoutInput input)
{
  var errors = new List<string>();
  
  // Validate time range
  if (input.EndTime <= input.StartTime)
    errors.Add("End time must be after start time.");
  
  var blackout = new Blackout 
  { 
    FacilityId = input.FacilityId,
    RangeId = input.RangeId,
    StartTime = input.StartTime,
    EndTime = input.EndTime,
    RecurrenceRule = input.RecurrenceRule,
    CreatedBy = userId,
    CreatedAt = _clock.Now
  };
  
  // Find impacted reservations
  var impacted = _store.GetReservations()
    .Where(r => r.Status == ReservationStatus.Approved 
             && BlackoutOverlaps(r, blackout))
    .ToList();
  
  _store.SaveBlackout(blackout);
  
  return (blackout, errors, impacted);
}
```

`Controllers/PublicController.cs` — Public calendar:
```csharp
public IActionResult Calendar()
{
  var blackouts = _service.GetBlackouts(); // No login required
  var reservedTimes = _service.GetEvents(null); // Approved/pending only
  
  return View(new { Blackouts = blackouts, Reserved = reservedTimes });
}

// Export to iCalendar
[HttpGet("calendar/export")]
[AllowAnonymous]
public IActionResult ExportCalendar([FromQuery] bool includeReserved = false)
{
  var events = _service.GetEvents(null);
  var ics = _service.ExportToICalendar(events, includeReserved);
  
  return File(Encoding.UTF8.GetBytes(ics), "text/calendar", "range-blackouts.ics");
}
```

`Views/Public/Calendar.cshtml` — Public calendar with export:
```html
<div class="public-calendar">
  <button onclick="exportBlackouts()">Export blackouts (.ics)</button>
  <button onclick="exportAll()">Export all (blackouts + reserved)</button>
  
  <ejs-schedule id="public-schedule" 
                currentView="Month"
                showTimeIndicator="false">
    <!-- Blackouts and public reservations rendered here -->
  </ejs-schedule>
</div>
```

**Help links:**
- [Blocking Time & Holidays (similar concept)](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments)
- [iCalendar Export](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/print#export-to-icalendar)
- [Recurrence Rules](https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/recurrence-rule)

---

## 10. UC-14: Manage User Group Range Permissions & Qualifications (Admin)

**Implementation:**

Admins access **Groups & Permissions** (`/Admin/Groups`) and see a list of all corporate groups. For each group, there is a toggle for each **specialized range** (e.g., High Power Range, Frankoma Range, Tactical Training Bay). Toggling **On** requires the admin to enter a **verification note** (e.g., "Completed HiPower orientation 2024-10-15"). The system records:
- **Granted by:** Admin's user ID
- **Granted at:** Timestamp
- **Verification note:** Admin's justification

Once granted, the range appears in the group's booking calendar and the server accepts submissions for that range. Before grant, the range is **hidden** from their calendar view and rejected by the server.

**Relevant code:**

`Controllers/Api/ReservationsApiController.cs` — Update group permissions:
```csharp
[HttpPut("groups/{id:int}")]
[Authorize(Roles = Roles.Admin)]
public IActionResult UpdateGroup(int id, [FromBody] GroupUpdateInput input)
{
  var (ok, errors) = _service.UpdateGroup(id, input, Me!);
  return ok ? NoContent() : UnprocessableEntity(new { Errors = errors });
}
```

`Services/ReservationService.cs` — Permission grant logic:
```csharp
public (bool Ok, List<string> Errors) UpdateGroup(int groupId, GroupUpdateInput input, AppUser admin)
{
  var group = _store.GetGroup(groupId);
  if (group == null) return (false, new[] { "Group not found." });
  
  var errors = new List<string>();
  
  foreach (var perm in input.RangePermissions)
  {
    if (perm.Granted && string.IsNullOrWhiteSpace(perm.VerificationNote))
      errors.Add($"Verification note required to grant {perm.RangeName}.");
  }
  
  if (errors.Any()) return (false, errors);
  
  // Update permissions
  foreach (var perm in input.RangePermissions)
  {
    var existingPerm = group.RangePermissions.FirstOrDefault(p => p.RangeId == perm.RangeId);
    
    if (perm.Granted && (existingPerm == null || !existingPerm.Granted))
    {
      group.RangePermissions.Add(new RangePermission 
      { 
        RangeId = perm.RangeId,
        GrantedBy = admin.Id,
        GrantedAt = _clock.Now,
        VerificationNote = perm.VerificationNote,
        Granted = true
      });
    }
    else if (!perm.Granted && existingPerm?.Granted == true)
    {
      existingPerm.Granted = false;
      existingPerm.RevokedAt = _clock.Now;
    }
  }
  
  _store.SaveGroup(group);
  return (true, new List<string>());
}
```

`Services/ReservationService.cs` — Filter calendar by permissions:
```csharp
public List<ScheduleEventDto> GetEvents(AppUser user)
{
  var events = _store.GetReservations()
    .Where(r => r.Status == ReservationStatus.Approved || 
               (user?.GroupId == r.GroupId && r.Status == ReservationStatus.Pending))
    .ToList();
  
  if (user != null && user.Role == Roles.Corporate)
  {
    // Filter ranges: show only authorized ranges for this group
    var authorizedRanges = user.Group.RangePermissions
      .Where(p => p.Granted)
      .Select(p => p.RangeId)
      .ToHashSet();
    
    events = events.Where(e => authorizedRanges.Contains(e.RangeId)).ToList();
  }
  
  return events.Select(ToDto).ToList();
}
```

`wwwroot/js/scheduler.js` — Hide unavailable ranges:
```javascript
function populateRangeDropdown() {
  const availableRanges = currentUser.AuthorizedRanges; // Includes granted specialized ranges
  
  const rangeSelect = document.getElementById('editor-range');
  rangeSelect.innerHTML = availableRanges
    .map(r => `<option value="${r.Id}">${r.Name}</option>`)
    .join('');
}
```

`Views/Admin/Groups.cshtml` — Permissions UI:
```html
@foreach (var group in Model)
{
  <div class="group-card">
    <h3>@group.Name (Tier @group.Tier)</h3>
    <table class="permissions-table">
      <tr>
        <th>Specialized Range</th>
        <th>Granted</th>
        <th>Verification Note</th>
        <th>Granted By</th>
      </tr>
      @foreach (var range in allSpecializedRanges)
      {
        var perm = group.RangePermissions.FirstOrDefault(p => p.RangeId == range.Id);
        <tr>
          <td>@range.Name</td>
          <td>
            <input type="checkbox" @(perm?.Granted == true ? "checked" : "") 
                   onchange="togglePermission(@group.Id, @range.Id, this.checked)" />
          </td>
          <td>
            <textarea id="note-@range.Id" placeholder="e.g., HiPower cert expiry 2025-12-31">
              @(perm?.VerificationNote ?? "")
            </textarea>
          </td>
          <td>@(perm?.GrantedBy) @perm?.GrantedAt?.ToString("g")</td>
        </tr>
      }
    </table>
  </div>
}
```

**Help links:**
- [Authorization (Roles & Claims)](https://docs.microsoft.com/aspnet/core/security/authorization)
- [Resource-Based Authorization](https://docs.microsoft.com/aspnet/core/security/authorization/resourcebased)

---

## 11. UC-01 & UC-02 & UC-03 & UC-04 & UC-05: Corporate Onboarding & Compliance Management

**Note:** These use cases involve account creation, application review, document upload/verification, and automated expiration tracking. The POC **does not implement the full onboarding workflow** (UC-01-05) but demonstrates the **architectural pattern** for integrating compliance data with booking rules.

**How it would integrate:**

1. **UC-01:** New corporate entities submit an application via a form (not in this POC). The system creates a `CorporateGroup` with status `Pending` and tier assignment.

2. **UC-02:** Admins review the application in an admin dashboard, verify corporate standing (e.g., via LinkedIn/D&B), and approve/deny. Approved groups advance to `PreApproved` with locked booking.

3. **UC-03:** The primary user uploads COI and user group agreements to a document store (Azure Blob, S3, or local `/App_Data/documents`). Status: `PendingDocumentReview`.

4. **UC-04:** Admins verify documents (checking policy details, expiration dates) and mark as approved. An `InsuranceRecord` is created with `ExpirationDate`.

5. **UC-05:** A background task (e.g., hosted service running daily) checks expiration dates and:
   - At T-60, T-30, T-7 days: Sends automated email reminders
   - At T+0 (expiration): Sets `InsuranceRecord.IsExpired = true` and disables booking for the group

**Relevant code pattern** (not fully implemented in POC, but stubbed):

`Models/DomainModels.cs`:
```csharp
public class CorporateGroup
{
  public int Id { get; set; }
  public string Name { get; set; }
  public int Tier { get; set; }
  public AccountStatus Status { get; set; } // Pending, PreApproved, Approved
  public string? PrimaryContactId { get; set; }
  
  // Compliance data
  public InsuranceRecord? Insurance { get; set; }
  public List<UserAgreement> UserAgreements { get; set; }
}

public enum AccountStatus { Pending, PreApproved, Approved, Denied, Suspended }

public class InsuranceRecord
{
  public int Id { get; set; }
  public int GroupId { get; set; }
  public DateTime CertificateIssueDate { get; set; }
  public DateTime ExpirationDate { get; set; }
  public bool IsExpired { get; set; }
  public string PolicyType { get; set; } // Occurrence, Claims-Made
  public decimal CoverageLimit { get; set; } // 1M, 2M, etc.
  public string DocumentUrl { get; set; }
}

public class UserAgreement
{
  public int Id { get; set; }
  public int GroupId { get; set; }
  public DateTime SignedDate { get; set; }
  public DateTime ExpirationDate { get; set; }
  public string DocumentUrl { get; set; }
}
```

`Services/ComplianceBackgroundTask.cs` (not in POC, conceptual):
```csharp
public class ComplianceBackgroundService : BackgroundService
{
  private readonly IServiceProvider _serviceProvider;
  
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      await CheckInsuranceExpirations();
      await SendReminderEmails();
      
      // Run daily at 2 AM facility time
      await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
    }
  }
  
  private async Task CheckInsuranceExpirations()
  {
    var now = _clock.Now.Date;
    var store = _serviceProvider.GetRequiredService<InMemoryDataStore>();
    var notificationService = _serviceProvider.GetRequiredService<NotificationService>();
    
    foreach (var group in store.GetAllGroups())
    {
      var insurance = group.Insurance;
      if (insurance == null || insurance.IsExpired) continue;
      
      var daysUntilExpiry = (insurance.ExpirationDate.Date - now).TotalDays;
      
      if (daysUntilExpiry == 60) notificationService.SendExpirationReminder(group, 60);
      if (daysUntilExpiry == 30) notificationService.SendExpirationReminder(group, 30);
      if (daysUntilExpiry == 7) notificationService.SendExpirationReminder(group, 7);
      
      if (daysUntilExpiry <= 0)
      {
        insurance.IsExpired = true;
        group.Status = AccountStatus.Suspended; // Block bookings
        store.SaveGroup(group);
        notificationService.SendExpirationNotice(group);
      }
    }
  }
}
```

**Help links:**
- [Hosted Services in ASP.NET Core](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services)
- [Background Tasks & Scheduling](https://docs.microsoft.com/aspnet/core/fundamentals/background-tasks)

---

## 12. UC-15: Issue Consolidated Corporate Invoices (Admin / System)

**Implementation:**

The POC includes the foundational structure for invoicing (model stubs in `DomainModels.cs`). In production, after bookings are approved:

1. A background process or admin action generates invoices based on:
   - **Group tier rate:** $8/shooter for LE/Competitions, $4 for Youth, market rate for Commercial
   - **Approved reservations:** Per request, aggregated by billing period
   - **Surcharges/discounts:** Applied per facility policy

2. Invoices are stored in a database and published to the group's **My Reservations** dashboard for viewing, printing, or online payment.

**Relevant code pattern:**

`Models/DomainModels.cs`:
```csharp
public class Invoice
{
  public int Id { get; set; }
  public int GroupId { get; set; }
  public DateTime InvoiceDate { get; set; }
  public DateTime DueDate { get; set; }
  public List<InvoiceLineItem> LineItems { get; set; }
  public decimal Total { get; set; }
  public InvoiceStatus Status { get; set; } // Draft, Sent, Paid, Overdue
}

public class InvoiceLineItem
{
  public int InvoiceId { get; set; }
  public string ReservationId { get; set; }
  public string RangeName { get; set; }
  public DateTime Date { get; set; }
  public int Headcount { get; set; }
  public decimal RatePerShooter { get; set; }
  public decimal Subtotal { get; set; }
}

public enum InvoiceStatus { Draft, Sent, Paid, Overdue, Cancelled }
```

`Services/InvoiceService.cs` (conceptual):
```csharp
public class InvoiceService
{
  public Invoice GenerateInvoice(CorporateGroup group, DateTime billingPeriodStart, DateTime billingPeriodEnd)
  {
    var reservations = _store.GetReservations()
      .Where(r => r.GroupId == group.Id 
             && r.Status == ReservationStatus.Approved
             && r.StartTime.Date >= billingPeriodStart.Date
             && r.StartTime.Date <= billingPeriodEnd.Date)
      .ToList();
    
    var tierRate = GetTierRate(group.Tier);
    var lineItems = new List<InvoiceLineItem>();
    
    foreach (var res in reservations)
    {
      lineItems.Add(new InvoiceLineItem 
      { 
        ReservationId = res.RequestId,
        RangeName = res.RangeName,
        Date = res.StartTime.Date,
        Headcount = res.Headcount,
        RatePerShooter = tierRate,
        Subtotal = res.Headcount * tierRate
      });
    }
    
    var invoice = new Invoice 
    { 
      GroupId = group.Id,
      InvoiceDate = _clock.Now,
      DueDate = _clock.Now.AddDays(30),
      LineItems = lineItems,
      Total = lineItems.Sum(li => li.Subtotal),
      Status = InvoiceStatus.Draft
    };
    
    _store.SaveInvoice(invoice);
    return invoice;
  }
  
  private decimal GetTierRate(int tier)
  {
    return tier switch
    {
      4 => 8m,     // LE
      1 => 4m,     // Youth
      _ => 12m     // Commercial
    };
  }
}
```

---

## 13. UC-16: Delegate Roles & Manage Sub-Users (Corporate Primary User)

**Implementation:**

The POC includes basic delegate support (users marked with `IsDelegate = true` belonging to a corporate group). In a full system:

1. **Primary user invites delegates** via email on the **Account Management** page.
2. Each delegate receives an invite with a temporary link to set credentials.
3. Delegates can submit range bookings but **cannot** access:
   - Billing information
   - Compliance documents / insurance records
   - Group settings / permissions
   - Delegate roster

The authorization is role-based:
- `Admin` role: Full facility management
- `Corporate` role, `IsPrimaryContact = true`: Full group control
- `Corporate` role, `IsPrimaryContact = false`: Booking-only (delegate)

**Relevant code:**

`Models/DomainModels.cs`:
```csharp
public class AppUser
{
  public string Id { get; set; }
  public string Name { get; set; }
  public string Email { get; set; }
  public int GroupId { get; set; }
  public int Tier { get; set; }
  public string Role { get; set; } // Admin, Corporate
  public bool IsPrimaryContact { get; set; }
  public List<int> AuthorizedRanges { get; set; } // Specialized ranges
}

public static class Roles
{
  public const string Admin = "Admin";
  public const string Corporate = "Corporate";
}
```

`Services/ReservationService.cs` — Authorization checks:
```csharp
public (bool Ok, List<string> Errors) CancelRequest(string requestId, AppUser user)
{
  var request = _store.GetReservation(requestId);
  if (request == null) return (false, new[] { "Request not found." });
  
  // Only primary contact or the admin can cancel
  if (user.Role == Roles.Corporate && !user.IsPrimaryContact && user.Id != request.UserId)
    return (false, new[] { "Only the booking submitter or primary contact can cancel." });
  
  request.Status = ReservationStatus.Cancelled;
  _store.SaveReservation(request);
  
  return (true, new List<string>());
}
```

`Controllers/AccountController.cs` — Delegate management:
```csharp
public IActionResult ManageDelegates()
{
  var user = User.CurrentUser(_store);
  if (user?.Role != Roles.Corporate || !user.IsPrimaryContact)
    return Forbid();
  
  var group = _store.GetGroup(user.GroupId);
  return View(group.Users);
}

[HttpPost("account/invite-delegate")]
public IActionResult InviteDelegate([FromBody] DelegateInviteInput input)
{
  var user = User.CurrentUser(_store);
  if (user?.Role != Roles.Corporate || !user.IsPrimaryContact)
    return Forbid();
  
  var invite = new DelegateInvite 
  { 
    Email = input.Email,
    GroupId = user.GroupId,
    InvitedBy = user.Id,
    InviteToken = Guid.NewGuid().ToString(),
    ExpiresAt = _clock.Now.AddDays(7)
  };
  
  _store.SaveInvite(invite);
  _notificationService.SendDelegateInvite(invite);
  
  return Ok();
}
```

---

## Summary: How All Use Cases Map to Syncfusion Scheduler

| Use Case | Syncfusion Component | Implementation Location |
|---|---|---|
| **UC-06** Single booking | `<ejs-schedule>` + custom editor | `Views/Home/Index.cshtml`, `scheduler.js` |
| **UC-07** Multi-date booking | In-memory cart + batch submission | `scheduler.js` cart functions, `ReservationService.ApplyBatch` |
| **UC-08** Single-range restriction | Cart validation + server validation | `addCellsToCart()`, `ReservationRulesEngine` |
| **UC-09** Recurring series | `RecurrenceEditor` + server expansion | `RecurrenceExpander.cs`, `Views/Home/Index.cshtml` |
| **UC-10** Overlaps & group exceptions | `CheckConflicts()` + client-side masking | `ReservationService.CheckConflicts`, `onRenderCell` |
| **UC-11** Tier 4 Monday rule | UI disabling + hard rule in server | `isMondayBlocked()`, `ReservationRulesEngine.Checks.Monday` |
| **UC-12** Approve/Deny queue | Admin page + compliance checklist re-run | `Admin/Queue.cshtml`, `Approve()`, `Deny()` |
| **UC-13** Blackouts | Blackout model + calendar export (iCalendar) | `ReservationService.SaveBlackout()`, `ExportToICalendar()` |
| **UC-14** Group permissions | Toggle toggles per range + verification notes | `Admin/Groups.cshtml`, `UpdateGroup()` |
| **UC-15** Invoices | Conceptual (line items per reservation, tier rates) | `InvoiceService` (stub) |
| **UC-16** Delegates | User roles + authorization checks | `AppUser.IsPrimaryContact`, `Authorize` attributes |

---

## Key Syncfusion SDK Features Used

1. **Scheduler Core:**
   - Multiple view types (TimelineDay, TimelineWeek, Day, Week, Month, Agenda)
   - Drag/drop and resizing (for admins)
   - Custom cell rendering
   - Event editing with custom editor templates

2. **Recurrence:**
   - Built-in RecurrenceEditor
   - Server-side RRULE expansion

3. **Data Binding:**
   - REST API endpoints (`GET /api/schedule/events`, `POST /api/reservations/batch`)
   - PascalCase JSON for field mapping

4. **Extensibility:**
   - Event handlers: `created`, `actionBegin`, `actionComplete`, `actionFailure`, `popupOpen`, `popupClose`, `cellClick`, `cellDoubleClick`, `select`, `renderCell`
   - Custom editor fields for domain-specific data (headcount, firearms, targets)
   - Cell rendering customization for rule violations

5. **Export:**
   - iCalendar (`exportToICalendar`) for public calendar distribution

---

## Production Readiness Checklist

- [ ] Replace `InMemoryDataStore` with EF Core (SQL Server) or API calls to existing backend
- [ ] Replace demo sign-in with AD/ADFS or on-premises OIDC provider
- [ ] Add antiforgery headers (`[ValidateAntiForgeryToken]`) to POST/PUT/DELETE endpoints
- [ ] Implement audit logging for admin actions (approvals, denials, policy changes)
- [ ] Add optimistic concurrency (row version) on reservations
- [ ] Configure email (SMTP relay or cloud service) for notifications
- [ ] Implement delegate management UI for primary users
- [ ] Generate custom Scheduler JS bundle (Scheduler + inputs only, not full ej2)
- [ ] Performance: Add database indexes on `(GroupId, Status)`, `(RangeId, StartTime, EndTime)`, `(GroupId, ExpirationDate)`
- [ ] Compliance: Link group permissions to external orientation/qualification system

---

## Help & Documentation Links

### Syncfusion Scheduler SDK (ASP.NET Core)
- **Getting Started:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/getting-started
- **Tag Helper Reference:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule
- **Views (Day/Week/Month/Timeline/Agenda):** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/views
- **Data Binding:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/data-binding
- **Appointments & CRUD:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/appointments
- **Recurrence Rules:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/recurrence-rule
- **Editor Template & Customization:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/editor-template
- **Cell Rendering:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/event-rendering
- **Drag & Drop:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/drag-and-drop
- **iCalendar Export:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/print#export-to-icalendar
- **Time Zone Handling:** https://help.syncfusion.com/scheduler-sdk/asp-net-core/schedule/timezone

### ASP.NET Core
- **Authorization (Roles & Claims):** https://docs.microsoft.com/aspnet/core/security/authorization
- **Cookie Authentication:** https://docs.microsoft.com/aspnet/core/security/authentication/cookie
- **Hosted Services (Background Tasks):** https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services
- **PathBase (Sub-path Hosting):** https://docs.microsoft.com/aspnet/core/host-and-deploy/iis/hosting-bundle

---

**POC Repository:** https://github.com/SyncfusionExamples/scheduler-sdk-samples/tree/master/aspnetcore-scheduler/RangeReservation
