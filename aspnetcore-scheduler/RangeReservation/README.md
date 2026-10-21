# Arizona Range Reservations – Syncfusion ASP.NET Core Scheduler POC

A working proof of concept for a reservation app for shooting range facilities. It shows how the Syncfusion
ASP.NET Core Scheduler, combined with server-side business rules, covers the customer's requirements while
running **fully on-premises**: no cloud services, no CDN and no AI.

- **Stack:** ASP.NET Core MVC (.NET 10), `Syncfusion.EJ2.AspNet.Core` 35.1.41 (`<ejs-schedule>` tag helper), plain JS
- **Data:** in-memory store with seeded demo data, behind the same REST API a real backend would expose
- **Auth:** local cookie authentication. Roles are AZGFD Administrator and Corporate users (primary or delegate) belonging to Tier 1-4 groups

---

## 1. Run it

```bash
# Requirements: .NET 10 SDK and access to your NuGet feed (nuget.org or an internal mirror)
cd RangeReservationPoc
dotnet run
# open http://localhost:5180 and pick a persona
```

Optional: put your Syncfusion license key in `appsettings.json` → `Syncfusion:LicenseKey`, or set the
`SYNCFUSION_LICENSE_KEY` environment variable. Without a key the trial banner is shown. Validation is done
offline; the key never calls home.

Syncfusion client assets are **already included** in `wwwroot/lib/syncfusion` (`ej2.min.js` and `tailwind3.css`, version 35.1.41).
To refresh them, run `libman restore` (see `libman.json`) or copy them from the `@syncfusion/ej2` npm package.
Keep this version the same as the NuGet package version.

Dates passed to the browser are formatted with the invariant culture, so the app works on servers set to any culture,
for example en-DK or fi-FI, which use `.` as the time separator, or ar-SA, which uses the Hijri calendar.

### Hosting under a sub-path (e.g. Azure `https://<app>.azurewebsites.net/range-reservation/`)

Set the `PathBase` setting. Every link, redirect, static file, API call and the auth cookie path then uses that prefix.

- **Azure App Service:** go to *Settings → Environment variables → App settings* and add `PathBase` = `/range-reservation`.
- **Or** set `"PathBase": "/range-reservation"` in `appsettings.json`.

Where the prefix is applied:
- **Server:** `Program.cs` calls `app.UsePathBase(...)` as the first middleware.
- **Views:** use `~/` and the `asp-*` tag helpers.
- **JavaScript:** calls go through `appUrl('/api/...')`. `window.appBase` is rendered in `_Layout.cshtml`.

This works for Linux and Windows App Service. On Windows you can instead create a *Virtual application* `/range-reservation`; there the hosting module sets the prefix itself and `PathBase` can stay empty. Setting both does no harm.

### Email notifications (UC-12)
Approve / deny notices go to the submitting user and the group contact.
- **No SMTP configured (default):** written as `.eml` files to `App_Data/mail-pickup` and listed on the dashboard and review queue.
- **Internal relay:** set `Email:SmtpHost` (plus optional `Email:SmtpPort`, `Email:EnableSsl`, `Email:From`) in `appsettings.json`.

### Demo personas

| Persona | Group / role | What to show |
|---|---|---|
| Alex Morgan | **AZGFD Administrator** | Review queue (approve / deny with reason), blackouts, groups & specialized-range permissions, policies & Monday rule |
| Lt. Maria Ramirez / Sgt. Kevin Cho | Desert Valley Police Dept · **Tier 4** (LE) · Primary / Delegate | Monday access, Tactical Bay, High Power + Frankoma granted, weekly series |
| Dana Whitfield / Eli Navarro | Sonoran Security Services · **Tier 2** · Primary / Delegate | Mondays greyed out, no specialized ranges until granted, same-group double booking |
| Morgan Hale | Copper State Rifle Club · **Tier 3** · Primary | High Power granted, pending request in the queue |
| Riley Chen | Mesa Youth Shooting Sports · **Tier 1** · Primary | No recurring bookings, pending multi-date request |

---

## 2. Use case → implementation map

| Use case | How the POC does it | Where |
|---|---|---|
| **UC-06** Reserve a single slot | The full Scheduler editor collects range, date/time, **headcount**, **firearm types** (multi-select) and **target attributes** (type, distance, count), plus an RSO request. Options are limited to what the selected range allows. On submit the server validates everything and stores the request as **Pending admin review**. | `setupEditor()` in `scheduler.js`, `ReservationService.ApplyBatch` |
| **UC-07** Multi-date booking, one range | Turn on *Multi-date booking* and click slots on non-consecutive dates (Month view uses the default start time). Everything is submitted as **one request** (one `RequestId`), limited by the tier's *dates per booking*. *Check availability* does a dry run first. | cart in `scheduler.js` |
| **UC-08** Single-range restriction | The cart locks to the first range picked. Adding a different range is blocked with the exact message **"You can only select multiple dates for one range per booking."** The server enforces the same rule. | `addCellsToCart()`, `ReservationRulesEngine.SingleRangeMessage` |
| **UC-09** Recurring series | The RecurrenceEditor is limited to **weekly / monthly** and must have an end, with the latest date set to **Dec 31 of the following calendar year**. The server expands the series and checks **every instance** against bookings, blackouts and the Monday rule. | `RecurrenceExpander.cs`, `ReservationRulesEngine.RecurrenceCap` |
| **UC-10** Overlaps & group exceptions | A slot held by **another organization** (pending or approved) is **hard-blocked**. The **same group** may double-book; it gets a warning only, and an admin can switch this in Policies. Other organizations' bookings show only as "Reserved". | `CheckConflicts()`, `FacilitySettings.AllowSameGroupOverlap` |
| **UC-11** Tier 4 Monday rule | For non-Tier-4 groups, Monday cells are greyed out, and the server blocks any Monday slot or recurring instance. This applies to admins entering bookings for such groups too, even with the override on. Admins can turn the rule on or off in Policies. | `isMondayBlocked()`, `Checks.Monday` |
| **UC-12** Approve / deny queue | The **Review queue** lists pending requests with a **policy-compliance checklist** that is re-run against the live calendar: hours, range authorization, firearm, headcount, targets, availability, blackouts, Monday, recurrence and tier policy. *Approve* re-checks the hard rules. *Deny* requires a reason (Syncfusion Dialog). The status appears on the group's **My reservations** dashboard and an email notice is sent. | `Admin/Queue`, `Home/MyReservations`, `NotificationService` |
| **UC-13** Blackouts | One-time or recurring blackouts, for all facilities, one facility or one range. They show as blocked time on the internal booking calendar and on the **external public calendar** (`/Public/Calendar`, no sign-in, no customer details). The public page uses the Scheduler's built-in **iCalendar export** (`exportToICalendar`, module `ICalendarExport`) to download blackouts, or blackouts plus reserved time, as an `.ics` file for Outlook / Google / Apple calendars. Bookings in a blackout are blocked immediately, and saving a blackout lists the reservations it affects. | `Admin/Blackouts`, `Views/Public/Calendar.cshtml` |
| **UC-14** Group range permissions | **Groups & permissions** has a toggle for each specialized range (High Power Range, Frankoma Range). A verification note is required, and it is stored with who granted it and when. Until access is granted the range is **hidden** from the group's calendar and rejected by the server. | `Admin/Groups`, `ReservationService.UpdateGroup` |

### Status workflow
`Pending` (corporate submission) → `Approved` / `Denied (reason)`; groups can `Cancel`.
- Bookings an admin enters for a group are approved immediately.
- If a group edits an approved booking, it goes back to **Pending** for re-approval.
- Denied and cancelled requests free the slot.

### Rule layers (the server is authoritative; checked on submission and again on approval)
1. **Hard rules – always enforced, admins included:** other-organization conflicts, blackouts, Tier 4 Monday rule, recurring end-date cap, operating hours, past dates.
2. **Range rules – always enforced:** specialized-range permission, allowed firearm and target types, maximum target distance, headcount.
3. **Tier policy – an admin can override:** minimum tier, advance window, lead time, maximum hours, recurring allowed, dates per booking, change/cancel cutoff.

---

## 3. Suggested demo script (10 minutes)

1. **Dana (Tier 2):** Mondays are greyed out, and High Power, Frankoma and Tactical are not visible. Click a Tuesday slot on Pistol Range 1, fill in headcount, firearm and target, and save. The request is *submitted for admin review*.
2. **Dana:** Turn on **Multi-date booking** and pick three dates on Pistol Range 3, then try a slot on Pistol Range 2. It is blocked with the UC-08 message. Click **Submit for review**.
3. **Morgan (Tier 3):** Create a weekly series that runs past next year and it is rejected by the cap. A weekly Thursday series is rejected because of the Thanksgiving blackout. A weekly Tuesday series is accepted.
4. **Eli (Tier 2 delegate):** Book the same time as Dana's approved booking, which is allowed for the same group. Riley (Tier 1) tries the same slot and is hard-blocked.
5. **Maria (Tier 4):** Mondays are open; book a Monday on Tactical Training Bay.
6. **Alex (Admin):** In the **Review queue**, check the compliance list, approve one request, and deny another (a reason is required). The emails appear on the right. Add a blackout over a pending High Power request; *Approve* now refuses. In **Groups & permissions**, grant Sonoran Security High Power with a verification note; Dana now sees the range. Open the **Public calendar** and click **Export blackouts (.ics)**.

---

## 4. Project layout

```
Program.cs                          DI, JSON (PascalCase), cookie auth, PathBase, Syncfusion license
Models/DomainModels.cs              CustomerTier, CorporateGroup, RangePermission, ShootingRange, Reservation (+Status), Blackout, TierPolicy, FacilitySettings
Models/ApiModels.cs                 Batch request/result, RequestSummary + ComplianceCheck, inputs
Services/InMemoryDataStore.cs       Seed data (groups, users, ranges, blackouts), facility clock (America/Phoenix)
Services/RecurrenceExpander.cs      Server-side RRULE expansion
Services/ReservationRulesEngine.cs  All booking rules, tagged per compliance check
Services/ReservationService.cs      Calendar read model, transactional submissions, review queue, blackouts + ICS, permissions
Services/NotificationService.cs     Approval / denial emails (SMTP relay or local pickup folder)
Controllers/Api/...                 REST API
Controllers/PublicController.cs     Anonymous public availability calendar
Views/Home/Index.cshtml             <ejs-schedule> booking calendar
Views/Home/MyReservations.cshtml    Group dashboard
Views/Admin/*.cshtml                Queue, Blackouts, Groups, Policies
wwwroot/js/scheduler.js             Scheduler events, editor fields, multi-date cart, rule mirrors
wwwroot/js/requests.js              Request cards (queue + dashboard)
```

### Time handling
All times are **facility wall-clock (Arizona, UTC-7, no DST)** and are exchanged as `yyyy-MM-ddTHH:mm:ss`.
Syncfusion writes `UNTIL` and `RecurrenceException` as UTC strings, so `scheduler.js` converts them to and from wall-clock.
This keeps server-side recurrence checks correct for browsers in any time zone (tested with an IST browser).

---

## 5. Taking it to production

- Replace `InMemoryDataStore` with EF Core on SQL Server, or with calls to the existing reservation services. The controllers and rules engine stay the same. Add optimistic concurrency (a row version per reservation).
- Replace demo sign-in with AD / ADFS / an on-prem OIDC provider. Map directory groups to the `Admin` role and to the corporate `group` claim.
- Optionally generate a smaller custom script bundle (Scheduler + inputs only) instead of the full `ej2.min.js`.
- Add antiforgery headers for the JSON endpoints, and an audit log for admin overrides.
- Link group permissions (UC-14) to the orientation / qualification records system if one exists.
- Add delegate management for primary users and an audit trail of approvals, denials and overrides.
