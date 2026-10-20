/* Admin blackout management - Syncfusion DateTimePicker, DropDownList and RecurrenceEditor */
(function () {
    const pad = n => String(n).padStart(2, '0');
    const toWall = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
    const fromWall = s => { const m = /(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(s); return new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5]); };
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    // RecurrenceEditor emits UNTIL in UTC; the server stores facility wall-clock.
    const untilToWall = rule => rule ? rule.replace(/UNTIL=(\d{8})T(\d{6})Z/i, (_, d, t) => {
        const dt = new Date(Date.UTC(+d.slice(0, 4), +d.slice(4, 6) - 1, +d.slice(6, 8), +t.slice(0, 2), +t.slice(2, 4), +t.slice(4, 6)));
        return `UNTIL=${dt.getFullYear()}${pad(dt.getMonth() + 1)}${pad(dt.getDate())}T${pad(dt.getHours())}${pad(dt.getMinutes())}${pad(dt.getSeconds())}Z`;
    }) : rule;
    const untilToUtc = rule => rule ? rule.replace(/UNTIL=(\d{8})T(\d{6})Z/i, (_, d, t) => {
        const dt = new Date(+d.slice(0, 4), +d.slice(4, 6) - 1, +d.slice(6, 8), +t.slice(0, 2), +t.slice(2, 4), +t.slice(4, 6));
        return `UNTIL=${dt.getUTCFullYear()}${pad(dt.getUTCMonth() + 1)}${pad(dt.getUTCDate())}T${pad(dt.getUTCHours())}${pad(dt.getUTCMinutes())}${pad(dt.getUTCSeconds())}Z`;
    }) : rule;

    const scopes = window.blackoutScopes;
    let editingId = null;
    const tomorrow = new Date(); tomorrow.setDate(tomorrow.getDate() + 1); tomorrow.setHours(7, 0, 0, 0);

    const title = new ej.inputs.TextBox({ placeholder: 'Title *', floatLabelType: 'Always' }); title.appendTo('#bTitle');
    const scope = new ej.dropdowns.DropDownList({ dataSource: scopes, fields: { text: 'text', value: 'value' }, value: 'all', placeholder: 'Applies to', floatLabelType: 'Always' }); scope.appendTo('#bScope');
    const start = new ej.calendars.DateTimePicker({ placeholder: 'Start', floatLabelType: 'Always', value: tomorrow, step: 30 }); start.appendTo('#bStart');
    const end = new ej.calendars.DateTimePicker({ placeholder: 'End', floatLabelType: 'Always', value: new Date(tomorrow.getTime() + 3 * 3600000), step: 30 }); end.appendTo('#bEnd');
    const allDay = new ej.buttons.CheckBox({ label: 'All day (closure)' }); allDay.appendTo('#bAllDay');
    const recurrence = new ej.schedule.RecurrenceEditor({ frequencies: ['none', 'daily', 'weekly', 'monthly', 'yearly'], startDate: tomorrow });
    recurrence.appendTo('#bRecurrence');
    start.change = e => { if (e.value) { recurrence.startDate = e.value; recurrence.dataBind(); } };
    const reason = new ej.inputs.TextBox({ placeholder: 'Reason (shown to admins)', floatLabelType: 'Always' }); reason.appendTo('#bReason');
    const toast = new ej.notifications.Toast({ position: { X: 'Right', Y: 'Top' }, showCloseButton: true }); toast.appendTo('#toast');

    function scopeOf(b) { return b.RangeId ? 'r' + b.RangeId : b.FacilityId ? 'f' + b.FacilityId : 'all'; }
    function scopeText(b) { const s = scopes.find(x => x.value === scopeOf(b)); return s ? s.text : ''; }
    function fmt(d) { return d.toLocaleString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' }); }

    async function load() {
        const res = await fetch('/api/blackouts', { credentials: 'same-origin' });
        const list = await res.json();
        const tbody = document.getElementById('bList');
        tbody.innerHTML = list.length ? '' : '<tr><td colspan="5" class="muted">No blackouts</td></tr>';
        list.forEach(b => {
            const tr = document.createElement('tr');
            const s = fromWall(b.StartTime), e = fromWall(b.EndTime);
            tr.innerHTML = `<td><strong>${esc(b.Title)}</strong><div class="muted small">${esc(b.Reason)}</div></td>
                <td>${esc(scopeText(b))}</td>
                <td>${b.IsAllDay ? s.toLocaleDateString() + ' (all day)' : fmt(s) + ' - ' + e.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</td>
                <td class="rule">${esc(b.RecurrenceRule || 'one-time')}</td>
                <td style="white-space:nowrap"><button class="e-btn e-small e-flat" data-edit="${b.Id}">Edit</button><button class="e-btn e-small e-flat" data-del="${b.Id}">Delete</button></td>`;
            tbody.appendChild(tr);
        });
        tbody.querySelectorAll('[data-del]').forEach(btn => btn.onclick = async () => {
            await fetch('/api/blackouts/' + btn.dataset.del, { method: 'DELETE', credentials: 'same-origin' });
            toast.show({ title: 'Blackout deleted', cssClass: 'e-toast-success' }); load();
        });
        tbody.querySelectorAll('[data-edit]').forEach(btn => btn.onclick = () => edit(list.find(x => x.Id === +btn.dataset.edit)));
    }

    function edit(b) {
        editingId = b.Id;
        document.getElementById('formTitle').textContent = 'Edit blackout #' + b.Id;
        title.value = b.Title; scope.value = scopeOf(b); reason.value = b.Reason || '';
        start.value = fromWall(b.StartTime); end.value = fromWall(b.EndTime); allDay.checked = b.IsAllDay;
        recurrence.startDate = start.value; recurrence.value = untilToUtc(b.RecurrenceRule) || ''; recurrence.dataBind();
        document.getElementById('bResult').innerHTML = '';
    }

    function reset() {
        editingId = null;
        document.getElementById('formTitle').textContent = 'New blackout window';
        title.value = ''; scope.value = 'all'; reason.value = ''; allDay.checked = false;
        start.value = tomorrow; end.value = new Date(tomorrow.getTime() + 3 * 3600000);
        recurrence.value = ''; recurrence.dataBind();
        document.getElementById('bResult').innerHTML = '';
    }

    async function save() {
        const sc = scope.value || 'all';
        let s = start.value, e = end.value;
        if (allDay.checked && s) {
            s = new Date(s.getFullYear(), s.getMonth(), s.getDate());
            const ed = e || s; e = new Date(ed.getFullYear(), ed.getMonth(), ed.getDate() + 1);
            if (e <= s) e = new Date(s.getFullYear(), s.getMonth(), s.getDate() + 1);
        }
        const payload = {
            Id: editingId, Title: title.value || '', Reason: reason.value || null, IsAllDay: allDay.checked,
            StartTime: s ? toWall(s) : null, EndTime: e ? toWall(e) : null,
            RecurrenceRule: untilToWall(recurrence.value) || null,
            RangeId: sc.startsWith('r') ? +sc.slice(1) : null,
            FacilityId: sc.startsWith('f') ? +sc.slice(1) : null
        };
        const res = await fetch('/api/blackouts', { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        const body = await res.json().catch(() => ({}));
        const out = document.getElementById('bResult');
        if (res.ok) {
            const impacted = body.Impacted || [];
            out.innerHTML = '<div class="msg ok">Saved. The window is now hard-blocked on the scheduler.</div>' +
                (impacted.length ? '<div class="msg warn"><strong>' + impacted.length + ' existing reservation(s) overlap this blackout - contact these customers:</strong><ul>' +
                    impacted.map(i => '<li>' + esc(i) + '</li>').join('') + '</ul></div>' : '');
            editingId = body.Blackout.Id;
            document.getElementById('formTitle').textContent = 'Edit blackout #' + editingId;
            load();
        } else {
            out.innerHTML = '<div class="msg err"><ul>' + (body.Errors || ['Could not save (' + res.status + ')']).map(x => '<li>' + esc(x) + '</li>').join('') + '</ul></div>';
        }
    }

    document.getElementById('bSave').onclick = save;
    document.getElementById('bReset').onclick = reset;
    load();
})();
