# Syncfusion<sup>®</sup> Scheduler SDK Samples

A curated collection of **Syncfusion<sup>®</sup> Scheduler SDK** component samples. Each sample in this repository is a small, self-contained project that demonstrates a specific feature or integration pattern and serves as a perfect starting point for your own application.

> Official overview: <https://help.syncfusion.com/scheduler-sdk/overview>

---

## Featured Components

### 1. Scheduler

A calendar and event-management UI from Syncfusion Essential JS 2 used to organise appointments, resources, and timelines. It ships day, week, work-week, month, timeline, and agenda views, with full resource grouping, customisable work hours, and event editing.

**Key features**

- **Views** — Day, Week, Work Week, Month, Timeline Day, Timeline Week, Timeline Month, Timeline Year, and Agenda out of the box.
- **Recurrence** — Built-in RRULE-style recurrence rules with exceptions and editing support.
- **Resources** — Group the scheduler by one or more resources (people, rooms, equipment) with avatars, swimlanes, and timezone-aware rendering.
- **Editing** — Add, edit, resize, drag, and delete events inline or via the editor window.
- **Timezone** — Scheduler events in their original timezone and render them correctly for every viewer.
- **Templates** — Customise cells, event blocks, resource headers, the editor, and tooltip via `template` properties and CSS.
- **Virtualization** — Efficiently render thousands of events with built-in UI virtualization.
- **Localization & RTL** — Fully localised strings and right-to-left layouts.
- **Theming** — Drop-in themes (`tailwind3`, `bootstrap5.3`, `fluent2`, `material3`) via the EJ2 combined stylesheet.

---

## Sample Catalog

| Platform | Sample | Folder | Component | Demonstrates |
|---|---|---|---|---|
| JavaScript | Staff Roster / Roster Grid + TimelineWeek Scheduler | [`javascript-schedule/staff-roaster`](javascript-schedule/staff-roaster) | Scheduler, Grid, DatePicker, TimePicker, DropDownList | Editable 15-minute roster with in-cell per-staff `DropDownList`s; `TimelineWeek` Scheduler grouped by staff that re-renders when any grid drop-down changes; `DatePicker` + `TimePicker` for the working window; |