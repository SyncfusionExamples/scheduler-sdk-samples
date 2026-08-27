# Syncfusion<sup>®</sup> Schedule SDK Samples

A curated collection of **Syncfusion<sup>®</sup> Schedule SDK** component samples. Each sample in this repository is a small, self-contained project that demonstrates a specific feature or integration pattern and serves as a perfect starting point for your own application.

> Official overview: <https://help.syncfusion.com/scheduler-sdk/overview>

---

## Featured Components

### 1. Schedule

A calendar and event-management UI from Syncfusion Essential JS 2 used to organise appointments, resources, and timelines. It ships day, week, work-week, month, timeline, and agenda views, with full resource grouping, customisable work hours, and event editing.

**Key features**

- **Views** — Day, Week, Work Week, Month, Timeline Day, Timeline Week, Timeline Month, Timeline Year, and Agenda out of the box.
- **Recurrence** — Built-in RRULE-style recurrence rules with exceptions and editing support.
- **Resources** — Group the schedule by one or more resources (people, rooms, equipment) with avatars, swimlanes, and timezone-aware rendering.
- **Editing** — Add, edit, resize, drag, and delete events inline or via the editor window.
- **Timezone** — Schedule events in their original timezone and render them correctly for every viewer.
- **Templates** — Customise cells, event blocks, resource headers, the editor, and tooltip via `template` properties and CSS.
- **Virtualization** — Efficiently render thousands of events with built-in UI virtualization.
- **Localization & RTL** — Fully localised strings and right-to-left layouts.
- **Theming** — Drop-in themes (`tailwind3`, `bootstrap5.3`, `fluent2`, `material3`) via the EJ2 combined stylesheet.

### 2. Schedule + Grid

A frequent pairing in real apps: a tabular **roster / planning grid** drives a **Schedule** view that mirrors the same data. The `staff-roaster` sample in this repo shows the pattern — editing a drop-down in the grid re-syncs the appointment data on the Schedule.

**Key features**

- **Two-way data flow** — Editable Grid cells (e.g. per-staff task) and a Schedule that updates in the same render cycle.
- **Resource columns** — One column per staff/resource; each one hosts an in-cell `DropDownList`.
- **Range pickers** — `DatePicker` + `TimePicker` for the working window.
- **Coalesced events** — Consecutive same-task rows are merged into a single Schedule appointment.

---

## Sample Catalog

| Platform | Sample | Folder | Component | Demonstrates |
|---|---|---|---|---|
| JavaScript | Staff Roster / Roster Grid + TimelineWeek Schedule | [`javascript-schedule/staff-roaster`](javascript-schedule/staff-roaster) | Schedule, Grid, DatePicker, TimePicker, DropDownList | Editable 15-minute roster with in-cell per-staff `DropDownList`s; `TimelineWeek` Schedule grouped by staff that re-renders when any grid drop-down changes; `DatePicker` + `TimePicker` for the working window; |