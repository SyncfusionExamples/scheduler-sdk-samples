var scheduleObj;
var grid;
var EVENT_ID = 0;

var hourOptions = [
    { Task: 'Select' },
    { Task: 'Contact Hours' },
    { Task: 'Discretionary hour' },
    { Task: 'Non-contact hours' },
    { Task: 'Teacher Professional Time' },
    { Task: 'Administration' },
    { Task: 'Meeting' },
    { Task: 'Tea Break' }
];

var staff = [
    { id: 1, name: 'Abigail Phaste', field: 'Abigail', header: 'Abigail Pha...', color: '#1f8be2' },
    { id: 2, name: 'Daisy Dyer', field: 'Daisy', header: 'Daisy Dyer', color: '#d6a23a' },
    { id: 3, name: 'Monique Green', field: 'Monique', header: 'Monique Green', color: '#4cae6f' },
    { id: 4, name: 'Pinky Wadhwani', field: 'Pinky', header: 'Pinky Wadhw...', color: '#a16fcf' },
    { id: 5, name: 'Raquet Neur', field: 'Raquet', header: 'Raquet Neur', color: '#e07a5f' },
    { id: 6, name: 'Serena Jack', field: 'Serena', header: 'Serena Jack', color: '#3aa6a0' },
    { id: 7, name: 'Shannon Tan', field: 'Shannon', header: 'Shannon Tan', color: '#d9534f' }
];


var ROSTER_DATE = new Date(2026, 6, 1);
var WEEK_START = new Date(2026, 5, 29);
function pad(n) { return n < 10 ? '0' + n : '' + n; }
function fmt(h, m) { return pad(h) + ':' + pad(m); }

function makeRow(totalMinutes, ratio, u2, o2) {
    return {
        Time: fmt(Math.floor(totalMinutes / 60), totalMinutes % 60) + ' - ' +
            fmt(Math.floor((totalMinutes + 15) / 60), (totalMinutes + 15) % 60),
        Require: '5',
        Ratios: ratio,
        U2_O2: u2 + ' (' + (u2) + '), ' + o2 + ' (' + o2 + ')',
        Abigail: 'Contact Hours',
        Daisy: 'Contact Hours',
        Monique: 'Contact Hours',
        Pinky: 'Contact Hours',
        Raquet: 'Contact Hours',
        Serena: 'Contact Hours',
        Shannon: 'Contact Hours'
    };
}

function buildWeekData() {
    var week = [];
    var START_MIN = 8 * 60 + 45;
    var END_MIN = 19 * 60;
    for (var t = START_MIN; t <= END_MIN; t += 15) {
        var ratio, u2, o2;
        if (t < 540) { ratio = 100; u2 = (t > 525 ? 1 : 0); o2 = 26 + (t > 525 ? 9 : 0); }
        else if (t < 600) { ratio = 100; u2 = 1; o2 = 35; }
        else if (t < 630) { ratio = 80; u2 = 1; o2 = 36; }
        else if (t < 690) { ratio = 80; u2 = 2; o2 = 37; }
        else if (t < 810) { ratio = 60; u2 = 2; o2 = 37; }
        else if (t < 900) { ratio = 60; u2 = 2; o2 = 36; }
        else if (t < 990) { ratio = 80; u2 = 1; o2 = 35; }
        else if (t < 1080) { ratio = 100; u2 = 1; o2 = 32; }
        else { ratio = 100; u2 = 0; o2 = 28; }
        var row = makeRow(t, ratio, u2, o2);
        week.push(row);
    }

    week[0].Shannon = 'Select';
    week[0].Monique = 'Select';
    week[8].Pinky = 'Tea Break';
    week[8].Monique = 'Discretionary hour';
    week[18].Shannon = 'Discretionary hour';
    week[24].Abigail = 'Meeting';
    week[24].Daisy = 'Administration';
    week[36].Raquet = 'Tea Break';
    week[40].Pinky = 'Meeting';
    week[40].Monique = 'Non-contact hours';

    var weekDays = [];
    for (var d = 0; d < 7; d++) {
        var copy = week.map(function (r) { return Object.assign({}, r); });
        if (d === 1) {
            copy[12].Shannon = 'Non-contact hours';
            copy[12].Abigail = 'Non-contact hours';
            copy[36].Pinky   = 'Tea Break';
        } else if (d === 2) {
            // already has explicit overrides above
        } else if (d === 3) {
            copy[6].Monique  = 'Discretionary hour';
            copy[20].Pinky   = 'Meeting';
            copy[32].Daisy   = 'Administration';
        } else if (d === 4) {
            copy[10].Shannon = 'Non-contact hours';
            copy[10].Raquet  = 'Tea Break';
            copy[28].Abigail = 'Meeting';
            copy[28].Daisy   = 'Administration';
        } else if (d === 5) {
            copy[4].Shannon  = 'Select';
            copy[4].Abigail  = 'Select';
            copy[30].Monique = 'Non-contact hours';
        } else {
            copy[4].Shannon  = 'Select';
            copy[4].Abigail  = 'Select';
            copy[4].Pinky    = 'Select';
            copy[4].Daisy    = 'Select';
            copy[4].Raquet   = 'Select';
            copy[4].Monique  = 'Select';
            copy[4].Serena   = 'Select';
        }
        weekDays.push(copy);
    }
    return weekDays;
}

var weekDaysData = buildWeekData();

function rosterRow(dayIdx, rowIdx) {
    return weekDaysData[dayIdx] && weekDaysData[dayIdx][rowIdx];
}

var rosterData = weekDaysData[2];

function dayDate(dayIdx) {
    var d = new Date(WEEK_START.getTime());
    d.setDate(d.getDate() + dayIdx);
    d.setHours(0, 0, 0, 0);
    return d;
}

function slotDate(dayIdx, totalMinutes) {
    var d = dayDate(dayIdx);
    d.setHours(Math.floor(totalMinutes / 60), totalMinutes % 60, 0, 0);
    return d;
}

function buildAppointmentsFromGrid() {
    var appts = [];
    EVENT_ID = 0;
    var START_MIN = 8 * 60 + 45;

    if (!grid || !grid.dataSource) return appts;

    staff.forEach(function (staffMember) {
        var field = staffMember.field;
        var staffId = staffMember.id;

        for (var day = 0; day < 7; day++) {
            var runStart = -1;
            var runSubject = null;

            for (var row = 0; row <= grid.dataSource.length; row++) {
                var subject = null;

                if (day === 2 && row < grid.dataSource.length) {
                    subject = grid.dataSource[row][field];
                } else if (row < weekDaysData[day].length) {
                    subject = weekDaysData[day][row][field];
                }

                if (subject === 'Select' || subject === null || subject === undefined) {
                    subject = null;
                }

                if (subject === runSubject) continue;

                if (runSubject != null && runStart >= 0) {
                    var subjectStr = String(runSubject);
                    var appt = {
                        Id: ++EVENT_ID,
                        Subject: subjectStr,
                        StartTime: slotDate(day, START_MIN + runStart * 15),
                        EndTime: slotDate(day, START_MIN + row * 15),
                        IsAllDay: false,
                        staffId: staffId,
                    };
                    appts.push(appt);
                }

                runStart = row;
                runSubject = subject;
            }
        }
    });

    return appts;
}

function buildAppointments() {
    var appts = [];
    EVENT_ID = 0;
    var START_MIN = 8 * 60 + 45;

    for (var d = 0; d < 7; d++) {
        var dayRows = weekDaysData[d];
        staff.forEach(function (s) {
            var runStart = -1;
            var runSubject = null;
            for (var r = 0; r <= dayRows.length; r++) {
                var subject = r < dayRows.length ? dayRows[r][s.field] : null;
                if (subject === 'Select' || subject === null || subject === undefined) {
                    subject = null;
                }
                if (subject === runSubject) continue;

                if (runSubject != null && runStart >= 0) {
                    var subjectStr = String(runSubject);
                    var appt = {
                        Id: ++EVENT_ID,
                        Subject: subjectStr,
                        StartTime: slotDate(d, START_MIN + runStart * 15),
                        EndTime: slotDate(d, START_MIN + r * 15),
                        IsAllDay: false,
                        staffId: s.id,
                    };
                    appts.push(appt);
                }
                runStart = r;
                runSubject = subject != null ? subject : null;
            }
        });
    }
    return appts;
}

var appointmentData = buildAppointments();

function createSchedule() {
    scheduleObj = new ej.schedule.Schedule({
        width: '100%',
        height: '520px',
        currentView: 'TimelineWeek',
        views: ['TimelineDay', 'TimelineWeek'],
        selectedDate: new Date(2026, 6, 1),
        workHours: {
            start: '08:00',
            end: '20:00'
        },
        resourceHeaderTitle: 'Staff',
        group: { byDate: true, resources: ['Staffs'] },
        resources: [{
            field: 'staffId',
            title: 'Staff',
            name: 'Staffs',
            allowMultiple: false,
            dataSource: staff.map(function (s) {
                return { id: s.id, text: s.name, color: s.color };
            }),
            textField: 'text',
            idField: 'id',
            colorField: 'color'
        }],
        eventSettings: {
            dataSource: appointmentData,
            fields: {
                id: 'Id',
                subject: { name: 'Subject', default: 'Select' },
                startTime: { name: 'StartTime' },
                endTime: { name: 'EndTime' },
                isAllDay: { name: 'IsAllDay', default: false },
                description: { name: 'Description', default: '' },
                location: { name: 'Location', default: '' },
                startTimezone: { name: 'StartTimezone', default: '' },
                endTimezone: { name: 'EndTimezone', default: '' }
            }
        },

    });
    scheduleObj.appendTo('#Schedule');
}

function dayIndexFromDate(jsDate) {
    var d = new Date(jsDate.getFullYear(), jsDate.getMonth(), jsDate.getDate());
    var diff = Math.round((d - WEEK_START) / (24 * 60 * 60 * 1000));
    if (diff < 0 || diff > 6) return -1;
    return diff;
}

var startTimePicker;
var endTimePicker;
var datePickerObj;

function createPickers() {
    datePickerObj = new ej.calendars.DatePicker({
        value: new Date(2026, 6, 1),
        format: 'dd-MMM-yyyy',
        placeholder: 'Select date',
        change: function (e) {
            if (e.value) {
                loadRosterByDate(e.value);
            }
        }
    });
    datePickerObj.appendTo('#datePicker');

    startTimePicker = new ej.calendars.TimePicker({
        value: new Date(2026, 6, 1, 8, 45),
        format: 'hh:mm a',
        placeholder: 'Start time',
        min: new Date(2026, 6, 1, 8, 0),
        max: new Date(2026, 6, 1, 19, 0),
        strictMode: false,
        change: function (e) {
            if (e.value) {
                if (endTimePicker && endTimePicker.value && e.value >= endTimePicker.value) {
                    startTimePicker.value = new Date(endTimePicker.value.getTime() - 15 * 60 * 1000);
                    startTimePicker.dataBind();
                }
                applyTimeRange();
            }
        }
    });
    startTimePicker.appendTo('#startTime');

    endTimePicker = new ej.calendars.TimePicker({
        value: new Date(2026, 6, 1, 19, 0),
        format: 'hh:mm a',
        placeholder: 'End time',
        min: new Date(2026, 6, 1, 8, 0),
        max: new Date(2026, 6, 1, 19, 0),
        strictMode: false,
        change: function (e) {
            if (e.value) {
                if (startTimePicker && startTimePicker.value && e.value <= startTimePicker.value) {
                    endTimePicker.value = new Date(startTimePicker.value.getTime() + 15 * 60 * 1000);
                    endTimePicker.dataBind();
                }
                applyTimeRange();
            }
        }
    });
    endTimePicker.appendTo('#endTime');
}

function applyTimeRange() {
    if (!scheduleObj || !startTimePicker || !endTimePicker || !datePickerObj || !grid) return;

    var startMin = startTimePicker.value.getHours() * 60 + startTimePicker.value.getMinutes();
    var endMin = endTimePicker.value.getHours() * 60 + endTimePicker.value.getMinutes();
    var selectedDate = datePickerObj.value;

    var dayIdx = dayIndexFromDate(selectedDate);
    if (dayIdx >= 0 && dayIdx < 7) {
        for (var r = 0; r < grid.dataSource.length && r < weekDaysData[dayIdx].length; r++) {
            var gridRow = grid.dataSource[r];
            var weekRow = weekDaysData[dayIdx][r];
            staff.forEach(function (s) {
                if (gridRow.hasOwnProperty(s.field)) {
                    weekRow[s.field] = gridRow[s.field];
                }
            });
        }

        var fullSlice = weekDaysData[dayIdx];
        var filtered = fullSlice.filter(function (row) {
            var parts = row.Time.split(' - ');
            var tParts = parts[0].split(':');
            var rowStartMin = parseInt(tParts[0], 10) * 60 + parseInt(tParts[1], 10);
            return (rowStartMin >= startMin && rowStartMin < endMin);
        });

        grid.dataSource = filtered;
        grid.refresh();
        attachDropdowns();
    }

}

function loadRosterByDate(selectedDate) {
    if (!grid || !scheduleObj) return;

    var dayIdx = dayIndexFromDate(selectedDate);

    var useWeekData = (dayIdx >= 0 && dayIdx <= 6);
    scheduleObj.selectedDate = selectedDate;
    scheduleObj.dataBind();
    if (useWeekData) {
        rosterData = weekDaysData[dayIdx];
        grid.dataSource = rosterData;
        grid.refresh();
        attachDropdowns();
    }
    appointmentData = buildAppointments();
    scheduleObj.eventSettings.dataSource = appointmentData;
    scheduleObj.dataBind();
}

var columns = [
    { field: 'Time', headerText: 'Time', width: 130, textAlign: 'Left', allowSorting: false },
    { field: 'U2_O2', headerText: 'U2 , O2', width: 120, textAlign: 'Left', allowSorting: false },
    { field: 'Require', headerText: 'Require...', width: 110, textAlign: 'Left', allowSorting: false },
    { field: 'Ratios', headerText: 'Ratios', width: 90, textAlign: 'Left', allowSorting: false }
];

staff.forEach(function (s) {
    columns.push({
        field: s.field,
        headerText: s.header,
        width: 140,
        textAlign: 'Left',
        allowSorting: false,
        template: '<div class="dropdown-cell" data-staff="' + s.field + '"></div>'
    });
});

grid = new ej.grids.Grid({
    dataSource: rosterData,
    columns: columns,
    height: 420,
    enableHover: false,
    allowSelection: false,
    cssClass: 'roster-grid',
    rowHeight: 38,
    headerRowHeight: 42,
    queryCellInfo: cellInfoHandler,
    actionComplete: function () { attachDropdowns(); },
    dataBound: function () { attachDropdowns(); }
});
grid.appendTo('#Grid');

function cellInfoHandler(args) {
    if (args.column.field === 'Ratios' && args.data) {
        var r = parseInt(args.data.Ratios, 10);
        if (r < 50) args.cell.classList.add('cell-below');
        else if (r < 80) args.cell.classList.add('cell-50');
        else if (r < 100) args.cell.classList.add('cell-80');
        else args.cell.classList.add('cell-100');
    }
}

function attachDropdowns() {
    var cells = document.querySelectorAll('.e-grid .dropdown-cell');
    cells.forEach(function (el) {
        if (el.dataset.bound === '1') { refreshDropdownValue(el); return; }
        el.dataset.bound = '1';

        var tr = el.closest('tr');
        var rowIndex = tr ? parseInt(tr.getAttribute('aria-rowindex'), 10) : -1;
        var dataRowIdx = rowIndex - 1;
        var field = el.dataset.staff;
        var value = (grid.dataSource[dataRowIdx] && grid.dataSource[dataRowIdx][field]) || 'Select';

        var ddl = new ej.dropdowns.DropDownList({
            dataSource: hourOptions,
            fields: { text: 'Task', value: 'Task' },
            value: value,
            cssClass: 'cell-dropdown',
            change: function (e) {
                if (e.itemData == null) return;

                var idx = dataRowIdx;
                if (idx < 0 || !grid.dataSource[idx]) return;

                var taskValue = (typeof e.itemData === 'object' && e.itemData.Task)
                    ? e.itemData.Task
                    : String(e.itemData);

                grid.dataSource[idx][field] = taskValue;

                readGridAndSyncSchedule();
            }
        });
        ddl.appendTo(el);
    });
}

function refreshDropdownValue(el) {
    var tr = el.closest('tr');
    var dataRowIdx = tr ? parseInt(tr.getAttribute('aria-rowindex'), 10) - 1 : -1;
    if (dataRowIdx < 0 || !grid.dataSource[dataRowIdx]) return;
    var field = el.dataset.staff;
    var ddl = el.querySelector('.e-dropdownlist') && ej.getInstance(el.querySelector('.e-dropdownlist'), ej.dropdowns.DropDownList);
    if (ddl && ddl.value !== grid.dataSource[dataRowIdx][field]) {
        ddl.value = grid.dataSource[dataRowIdx][field];
        ddl.dataBind();
    }
}

function readGridAndSyncSchedule() {
    if (!scheduleObj || !grid) return;

    var dayIdx = rosterDayIdx();
    if (dayIdx >= 0 && dayIdx < 7) {
        for (var r = 0; r < grid.dataSource.length && r < weekDaysData[dayIdx].length; r++) {
            var gridRow = grid.dataSource[r];
            var weekRow = weekDaysData[dayIdx][r];
            staff.forEach(function (s) {
                if (gridRow.hasOwnProperty(s.field)) {
                    weekRow[s.field] = gridRow[s.field];
                }
            });
        }
    }

    appointmentData = buildAppointments();
    scheduleObj.eventSettings.dataSource = appointmentData;
    scheduleObj.dataBind();
}

function rosterDayIdx() {
    if (datePickerObj && datePickerObj.value) {
        var idx = dayIndexFromDate(datePickerObj.value);
        if (idx >= 0 && idx <= 6) return idx;
    }
    return 2;
}

createSchedule();
createPickers();

grid.addEventListener('created', function () {
    var header = document.querySelector('.e-grid .e-headercell');
    if (header) header.classList.add('header-row');
});