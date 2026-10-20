# Arizona Range Reservations – Syncfusion ASP.NET Core Scheduler POC

A working proof of concept for a reservation app for shooting range facilities. It shows how the Syncfusion
ASP.NET Core Scheduler, combined with server-side business rules, covers the customer's requirements while
running **fully on-premises**: no cloud services, no CDN and no AI.

- **Stack:** ASP.NET Core MVC (.NET 10), `Syncfusion.EJ2.AspNet.Core` 35.1.41 (`<ejs-schedule>` tag helper), plain JS
- **Data:** in-memory store with seeded demo data, behind the same REST API a real backend would expose
- **Auth:** local cookie authentication with an Admin role and four customer tiers

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

### Hosting under a sub-path (e.g. Azure `https://<app>.azurewebsites.net/range-reservation/`)

Set the `PathBase` setting. Every link, redirect, static file, API call and the auth cookie path then uses that prefix.

- **Azure App Service:** go to *Settings → Environment variables → App settings* and add `PathBase` = `/range-reservation`.
- **Or** set `"PathBase": "/range-reservation"` in `appsettings.json`.

Where the prefix is applied:
- **Server:** `Program.cs` calls `app.UsePathBase(...)` as the first middleware.
- **Views:** use `~/` and the `asp-*` tag helpers.
- **JavaScript:** calls go through `appUrl('/api/...')`. `window.appBase` is rendered in `_Layout.cshtml`.

This works for Linux and Windows App Service. On Windows you can instead create a *Virtual application* `/range-reservation`; there the hosting module sets the prefix itself and `PathBase` can stay empty. Setting both does no harm.

### Demo personas

| Persona | Role / tier | What to show |
|---|---|---|
| Alex Morgan | **Admin** | Drag/resize any booking, book for a customer, tier-rule override, Blackouts and Tier policies pages |
| Bailey Brooks | **Bronze** (public day pass) | 7-day window, 2 h max, 1 booking/day, no recurring, pistol bays only |
| Sam Rivera | **Silver** (member) | 14 days, rifle 100 yd and trap allowed, up to 4 recurring occurrences |
| Jordan Lee | **Gold** (premium) | 60 days, Long Range range with qualification `LRQ-1001` |
| Casey Nguyen | **Platinum** (LE / instructor) | 366-day (annual) window, Tactical Bay with `TAC-2001`, up to 52 recurring occurrences |

Mock qualification registry: `LRQ-1001` (Jordan, Long Range, valid), `LRQ-1002` (Sam, **expired**),
`TAC-2001` (Casey, Tactical, valid), `TAC-2002` (Casey, Tactical, expires Dec 15 2026).

---

## 2. Requirement → implementation map

| Requirement (priority) | How the POC does it | Where |
|---|---|---|
| **Reservation scheduling** (H) | `<ejs-schedule>` with Timeline Day/Week, Day, Week, Month and Agenda views. Customers book in the full editor. Admins can also drag and resize. | `Views/Home/Index.cshtml`, `wwwroot/js/scheduler.js` |
| **Tier-based booking rules – 4 tiers** (H) | `TierPolicy` per tier sets: advance window, lead time, max hours, bookings per day, upcoming-booking cap, monthly hours, recurring allowed and its cap, slots per transaction, and the change/cancel cutoff. Admins can edit policies at runtime. The UI mirrors the rules: out-of-window and ineligible cells are greyed out, editor date limits are set, and the recurrence options are restricted. | `Services/ReservationRulesEngine.cs`, `Admin/Policies` |
| **Blackout periods** (H) | Blackouts are set by admins and can be one-time or recurring (RRULE). They can cover all facilities, one facility or one range. They render as Syncfusion **block events** (`IsBlock`) and are re-checked on the server for every occurrence. Saving a blackout lists existing reservations that it overlaps. | `Admin/Blackouts`, `ReservationService.SaveBlackout` |
| **Multi-date / multi-slot reservations** (H) | Turn on "Multi-slot booking", then click slots on any range and any date, or drag-select a range of cells. Use **Check availability** for a dry run, or **Book all**. The whole selection is **one all-or-nothing transaction**, limited by the per-tier and per-range slot caps, and the bookings share one `BookingGroupId`. | `scheduler.js` (cart), `ReservationService.ApplyBatch` |
| **Recurring reservations / annual scheduling / caps** (H) | Uses the built-in RecurrenceEditor (daily, weekly, monthly, yearly). The server expands the RRULE and checks every occurrence. It enforces a tier occurrence cap and requires customer series to have an end. Platinum gets a 366-day window. Editing or cancelling a single occurrence (exceptions) is supported. | `Services/RecurrenceExpander.cs` |
| **Conflict prevention** (M) | Client: `allowOverlap="false"`. Server: hard block checked per range across all occurrences, including other items in the same transaction. Rejected changes are rolled back in the UI. | `ReservationRulesEngine.CheckConflicts` |
| **Custom attributes** (M) | Extra editor fields: firearm type, caliber, participants, qualification ID and RSO request. Admins also get "book on behalf of". The server validates firearm type per range, capacity, and the qualification against the registry (type, expiry and holder). | `setupEditor()` in `scheduler.js` |
| **Resource-based scheduling** (M) | Two-level grouping Facility → Range (`byGroupID`). Range headers show the minimum tier, required certification and opening hours. A facility filter and an "only ranges I can book" filter give resource-specific views. | `Index.cshtml`, `applyResourceFilter()` |
| **Role-based access** | Admin vs Customer through cookie claims. Customers see other people's bookings only as "Reserved" (masked and read-only). They can change only their own bookings, within the cutoff, and cannot drag or resize. Admin API endpoints use `[Authorize(Roles="Admin")]`. | `ReservationService.GetEvents`, controllers |
| **Backend API integration** | The Scheduler loads data from `GET /api/schedule/events` and saves through `POST /api/reservations/batch`. The batch uses the same added/changed/deleted shape Syncfusion produces. Every create, edit, drag, occurrence edit and delete goes through this one transactional endpoint, so it can sit in front of existing services. | `Controllers/Api/ReservationsApiController.cs` |
| **On-prem, no cloud, no AI** | All scripts and styles are served locally. The theme's Google Fonts `@import` was removed. Syncfusion's anonymous usage telemetry is **explicitly disabled** with `ej.base.Telemetry.disable()` in `_Layout.cshtml`. The license is validated offline. No AI features are referenced. | `_Layout.cshtml` |

### Rule layers (server is authoritative)

1. **Hard blocks – always enforced, admins included:** conflicts, blackouts, operating hours, past dates.
2. **Range safety / compliance – always enforced:** allowed firearm types, participant capacity, required qualification (valid type, not expired at the last occurrence, belongs to the customer).
3. **Tier policy – enforced for customers; an admin can override:** range eligibility, advance window, lead time, duration, recurring cap, per-day / upcoming / monthly caps, slots per transaction, change/cancel cutoff.

---

## 3. Suggested demo script (10 minutes)

1. **Bailey (Bronze):** Rifle and Tactical rows are hatched. Click a slot 10 days out and it is refused. Book Pistol Bay A for 1 h, which succeeds. Try a second booking the same day and the *1 booking/day* rule rejects it. Open the editor and note there is no Repeat option.
2. **Jordan (Gold):** Book Rifle 300 yd on a Wednesday 8 AM and the *weekly berm maintenance blackout* blocks it. Book Thursday without a qualification ID, which fails; with `LRQ-1002` it fails as *expired*; with `LRQ-1001` it succeeds.
3. **Jordan:** Turn on **Multi-slot booking**, then click 3 slots across two days and two ranges. Click **Check availability**, then **Book all**. Add a slot that conflicts and nothing is booked (all-or-nothing).
4. **Casey (Platinum):** Create a weekly Tactical Bay series with 10 occurrences, which succeeds. Try 60 occurrences and hit the cap and the annual window. Try 11 AM–1 PM and hit the *daily cleaning blackout*.
5. **Alex (Admin):** Use Timeline Week to drag a booking onto another one; it is rejected and rolled back. Book Rifle 100 yd for Bailey, which is refused, then turn on *Override tier rules* and it succeeds. Edit one occurrence of Casey's series. Go to **Blackouts**, add a recurring closure and see the list of affected reservations. Go to **Tier policies** and change Bronze to 14 days.

---

## 4. Project layout

```
Program.cs                         DI, JSON (PascalCase), cookie auth, Syncfusion license
Models/DomainModels.cs             Tier, User, Facility, ShootingRange, Reservation, Blackout, TierPolicy
Models/ApiModels.cs                ScheduleEventDto, ReservationBatchRequest, BatchResult, BlackoutInput
Services/InMemoryDataStore.cs      Seed data, facility clock (America/Phoenix), mock qualification registry
Services/RecurrenceExpander.cs     Server-side RRULE expansion (DAILY/WEEKLY/MONTHLY/YEARLY, BYDAY, BYSETPOS, COUNT, UNTIL, exceptions)
Services/ReservationRulesEngine.cs All booking rules
Services/ReservationService.cs     Read model (masking, blackout blocks) + transactional batch writes
Controllers/Api/...                REST API used by the Scheduler
Views/Home/Index.cshtml            <ejs-schedule> configuration
wwwroot/js/scheduler.js            Scheduler events, editor fields, multi-slot cart, filters
wwwroot/js/blackouts.js            Blackout admin (DateTimePicker, DropDownList, RecurrenceEditor)
```

### Time handling
All times are **facility wall-clock (Arizona, UTC-7, no DST)** and are exchanged as `yyyy-MM-ddTHH:mm:ss`.
Syncfusion writes `UNTIL` and `RecurrenceException` as UTC strings, so `scheduler.js` converts them to and from wall-clock.
This keeps server-side recurrence checks correct for browsers in any time zone (tested with an IST browser).

---

## 5. Taking it to production

- Replace `InMemoryDataStore` with EF Core on SQL Server, or with calls to the existing reservation services. The controllers and rules engine stay the same. Add optimistic concurrency (a row version per reservation).
- Replace demo sign-in with AD / ADFS / an on-prem OIDC provider. Map groups to `Admin` and to the `tier` claim.
- Add antiforgery headers for the JSON endpoints, and an audit log for admin overrides.
- Replace `MockQualificationRegistry` with the real qualification or certification system.
- Optionally generate a smaller custom script bundle (Scheduler + inputs only) instead of the full `ej2.min.js`.
