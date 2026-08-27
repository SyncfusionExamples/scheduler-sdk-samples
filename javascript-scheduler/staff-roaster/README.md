# Syncfusion<sup>®</sup> JavaScript Scheduler Samples

JavaScript (vanilla, ES5+) samples for the **Syncfusion<sup>®</sup> Scheduler SDK**. Each sample in this directory is a small, self-contained project that demonstrates a specific Scheduler feature and serves as a starting point for your own application.

> Official Scheduler SDK overview: <https://help.syncfusion.com/scheduler-sdk/overview>
>
> Back to repo root: [schedule-sdk-samples](../../README.md)

---

## Sample Catalog

| Sample | Folder | Component | Demonstrates |
|---|---|---|---|
| Staff Roster / Roster Grid + TimelineWeek Scheduler | [staff-roaster](.) | Scheduler, Grid, DatePicker, TimePicker, DropDownList | Editable 15-minute roster with in-cell per-staff `DropDownList`s; `TimelineWeek` Scheduler grouped by staff that re-renders when any grid drop-down changes; `DatePicker` + `TimePicker` for the working window; |

This is the **only sample currently maintained** in this directory. New JavaScript Scheduler samples will be added to the table above.

---

## Prerequisites (common to every sample)

- A modern browser (Chrome, Edge, Firefox, Safari).
- **Syncfusion license key** — register once before app bootstrap:

  ```html
  <script>
      ej.base.registerLicense('YOUR_LICENSE_KEY');
  </script>
  ```

  Get a free **Community License** at <https://www.syncfusion.com/products/communitylicense>.
- Internet access so the EJ2 CDN (`cdn.syncfusion.com`) can deliver `ej2.min.js` and the theme stylesheet. No `npm install` is required.

---

## Run a sample (manual step-by-step)

The following steps work for **every sample** under this folder.

### 1. Open the sample folder

```text
javascript-schedule/<sample-folder>/
├── index.html
├── index.js
└── README.md                # This file
```

Open `index.html` directly in a browser, or — preferred — continue to step 2 and serve the folder over HTTP.

### 2. Serve the folder over HTTP (recommended)

```bash
cd javascript-schedule/staff-roaster

# Any static server works. Pick one:
npx serve .
# or
python -m http.server 8080
```

Then open the URL the server prints (typically `http://localhost:3000` for `npx serve` or `http://localhost:8080` for `python -m http.server`).

> Opening `index.html` straight from the filesystem via `file://` *usually* works, but some browsers restrict CDN/module behaviour from `file://` origins. A static server is the safe option.

### 3. Build for production (optional)

There is no `npm run build` for these CDN-based samples — the entire app is already static. To deploy:

```bash
# Copy the folder into any static host:
public/  ← index.html
        ← index.js
```

Hosts that work out of the box: Vercel, Netlify, GitHub Pages, S3, Azure Static Web Apps, IIS.

> Static page-level CSS is inlined inside `index.html` — there is no separate stylesheet to bundle.

---

## Documentation

Use the official user guide as the reference while exploring or extending any sample in this folder.

- **JavaScript Scheduler UG** — <https://help.syncfusion.com/scheduler-sdk/javascript/schedule/getting-started>

---

## Sample walkthrough — `staff-roaster`

This is the sample currently in this folder.

### Layout

The page is laid out as five vertical sections inside a single HTML file:

1. **Header bar** — title (`Syncfusion Roster`).
2. **Picker bar** — a `DatePicker`, a start-time `TimePicker`, and an end-time `TimePicker`.
3. **Ratio legend** — color-coded pills for `Below 50%`, `50–79%`, `80–99%`, `100%`. The `Ratios` column in the grid is filled with the same colors via `queryCellInfo`.
4. **Roster Grid** — one row per 15-minute slot between **08:45** and **19:00**. Columns: `Time`, `U2 , O2` (staff count), `Require...`, `Ratios`, then one editable column per staff member (drop-down of activities).
5. **Staff Scheduler** — a `Scheduler` in `TimelineWeek` view, grouped by date and then by `Staffs` resource, showing each staff member's day as colour-coded blocks.

### Tech stack

| Item        | Value                                                                                              |
| ----------- | -------------------------------------------------------------------------------------------------- |
| Framework   | JavaScript                                                                     |
| UI library  | Syncfusion Essential JS 2 (`ej2.min.js`)                                                           |
| Theme       | `tailwind3` (loaded from CDN)                                                                      |
| Components  | `ej.schedule.Schedule`, `ej.grids.Grid`, `ej.calendars.DatePicker`, `ej.calendars.TimePicker`, `ej.dropdowns.DropDownList` |

The combined script and theme are both pulled from:

```html
<link href="https://cdn.syncfusion.com/ej2/34.1.29/tailwind3.css" rel="stylesheet" />
<script src="https://cdn.syncfusion.com/ej2/34.1.29/dist/ej2.min.js"></script>
```

### How to use the page

1. Pick a **date** between `Mon 29 Jun 2026` and `Sun 5 Jul 2026` using the date picker. The grid and scheduler jump to that day.
2. Adjust the **Start Time** and **End Time** to filter the grid to a time window. The scheduler stays full-day, but the grid only shows rows inside the window.
3. In the grid, change any staff member's drop-down to assign a different activity for that 15-minute slot. The corresponding colour-coded block on the scheduler updates immediately.
4. Use the **Ratios** legend to interpret the colour of the `Ratios` column in each row.
