/* ==========================================================================================
   Arizona Range Reservations POC - Syncfusion Scheduler client logic
   ------------------------------------------------------------------------------------------
   - Data is loaded from / saved to the app's REST API (/api/schedule/events, /api/reservations/batch).
   - The server is authoritative for every rule; the UI mirrors rules for a better experience
     (greyed-out cells, recurrence options, date limits, client-side overlap prevention).
   - All times are facility wall-clock (Arizona). Dates are exchanged as "yyyy-MM-ddTHH:mm:ss".
   ========================================================================================== */

const ctx = window.appContext;

// Domain wording for the built-in Scheduler texts (localization keys).
ej.base.L10n.load({ 'en-US': { schedule: { newEvent: 'New Reservation', editEvent: 'Edit Reservation', deleteEvent: 'Cancel Reservation', title: 'Reservation title' } } });
const isAdmin = !!ctx.User.IsAdmin;
const myPolicy = ctx.Policy;                       // null for admins
const tierRank = { Bronze: 1, Silver: 2, Gold: 3, Platinum: 4 };
const myTierRank = ctx.User.Tier ? tierRank[ctx.User.Tier] : 99;
const rangesById = Object.fromEntries(ctx.Ranges.map(r => [r.Id, r]));
const loadedAt = Date.now();

let scheduleObj = null;
let toastObj = null;
let cart = [];
let cartSeq = 0;
let serverEvents = [];
let isSyncing = false;
const editor = {};   // custom editor field instances
const cartUi = {};   // multi-slot panel controls

/* ------------------------------------------------------------------------------------------
   Date helpers (facility wall-clock <-> JS Date, and Syncfusion's UTC recurrence strings)
   ------------------------------------------------------------------------------------------ */
const pad = n => String(n).padStart(2, '0');
function toWall(d) {
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}
function fromWall(s) {
    if (!s) return null;
    if (s instanceof Date) return s;
    const m = /(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?/.exec(s);
    return m ? new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0)) : new Date(s);
}
function facilityNow() { return new Date(fromWall(ctx.Now).getTime() + (Date.now() - loadedAt)); }
function addDays(d, n) { const x = new Date(d); x.setDate(x.getDate() + n); return x; }
function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }

// Syncfusion writes UNTIL / RecurrenceException as UTC ("yyyyMMddTHHmmssZ"). The server stores facility
// wall-clock, so convert on the way out and back on the way in.
function parseRec(s) {
    const m = /^(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2})(\d{2})?)?Z?$/i.exec((s || '').trim());
    return m ? { y: +m[1], mo: +m[2] - 1, d: +m[3], h: +(m[4] || 0), mi: +(m[5] || 0), s: +(m[6] || 0), hasTime: !!m[4] } : null;
}
const fmtRec = (y, mo, d, h, mi, s) => `${y}${pad(mo + 1)}${pad(d)}T${pad(h)}${pad(mi)}${pad(s)}Z`;
function recUtcToWall(str) {
    const p = parseRec(str); if (!p || !p.hasTime) return str;
    const dt = new Date(Date.UTC(p.y, p.mo, p.d, p.h, p.mi, p.s));
    return fmtRec(dt.getFullYear(), dt.getMonth(), dt.getDate(), dt.getHours(), dt.getMinutes(), dt.getSeconds());
}
function recWallToUtc(str) {
    const p = parseRec(str); if (!p || !p.hasTime) return str;
    const dt = new Date(p.y, p.mo, p.d, p.h, p.mi, p.s);
    return fmtRec(dt.getUTCFullYear(), dt.getUTCMonth(), dt.getUTCDate(), dt.getUTCHours(), dt.getUTCMinutes(), dt.getUTCSeconds());
}
const mapRule = (rule, fn) => rule ? rule.replace(/UNTIL=([^;]+)/i, (_, v) => 'UNTIL=' + fn(v)) : rule;
const mapEx = (ex, fn) => ex ? ex.split(',').map(x => x.trim()).filter(Boolean).map(fn).join(',') : ex;

function fromServer(e) {
    return Object.assign({}, e, {
        StartTime: fromWall(e.StartTime),
        EndTime: fromWall(e.EndTime),
        RecurrenceRule: mapRule(e.RecurrenceRule, recWallToUtc),
        RecurrenceException: mapEx(e.RecurrenceException, recWallToUtc)
    });
}
function toServer(e) {
    const num = v => (v === null || v === undefined || v === '') ? null : Number(v);
    return {
        Id: num(e.Id),
        Subject: e.Subject || '',
        StartTime: toWall(new Date(e.StartTime)),
        EndTime: toWall(new Date(e.EndTime)),
        IsAllDay: !!e.IsAllDay,
        RecurrenceRule: mapRule(e.RecurrenceRule, recUtcToWall) || null,
        RecurrenceID: num(e.RecurrenceID),
        RecurrenceException: mapEx(e.RecurrenceException, recUtcToWall) || null,
        FacilityId: num(e.FacilityId),
        RangeId: num(e.RangeId),
        CustomerId: e.CustomerId || null,
        FirearmType: e.FirearmType || null,
        Caliber: e.Caliber || null,
        Participants: num(e.Participants),
        QualificationId: e.QualificationId || null,
        RsoRequested: e.RsoRequested === true || e.RsoRequested === 'true',
        Description: e.Description || null
    };
}

/* ------------------------------------------------------------------------------------------
   API
   ------------------------------------------------------------------------------------------ */
async function api(url, options) {
    const res = await fetch(appUrl(url), Object.assign({ headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin' }, options || {}));
    let body = null;
    try { body = await res.json(); } catch { /* empty body */ }
    return { ok: res.ok, status: res.status, body };
}

async function loadEvents() {
    const res = await api('/api/schedule/events');
    if (!res.ok) { showToast('Could not load reservations', ['Server returned ' + res.status], 'e-toast-danger'); return; }
    serverEvents = res.body.map(fromServer);
    renderDataSource();
}

function renderDataSource() {
    isSyncing = true;
    scheduleObj.eventSettings.dataSource = serverEvents.concat(cart.map(cartItemToEvent));
    scheduleObj.dataBind();
    isSyncing = false;
}

async function submitBatch(payload) {
    const res = await api('/api/reservations/batch', { method: 'POST', body: JSON.stringify(payload) });
    if (res.status === 401 || res.status === 403) {
        return { ok: false, errors: ['Your session has expired or you are not allowed to do this.'] };
    }
    const b = res.body || {};
    return { ok: res.ok, errors: b.Errors || (res.ok ? [] : ['Unexpected server error (' + res.status + ').']), warnings: b.Warnings || [], body: b };
}

/* ------------------------------------------------------------------------------------------
   Toast / messages
   ------------------------------------------------------------------------------------------ */
function escapeHtml(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
function showToast(title, lines, cssClass) {
    const content = (lines && lines.length) ? '<ul class="toast-list">' + lines.map(l => '<li>' + escapeHtml(l) + '</li>').join('') + '</ul>' : '';
    toastObj.show({ title: escapeHtml(title), content, cssClass: cssClass || 'e-toast-success', timeOut: cssClass === 'e-toast-danger' ? 12000 : 4000 });
}

/* ------------------------------------------------------------------------------------------
   Scheduler events
   ------------------------------------------------------------------------------------------ */
function onScheduleCreated() {
    scheduleObj = document.getElementById('schedule').ej2_instances[0];
    initToolbar();
    initCart();
    renderPolicyCard();
    loadEvents();
}

// Every create / edit / delete (editor, quick popup, drag & drop, resize, occurrence and series edits) ends up here
// with the exact added / changed / deleted records Syncfusion produced. We persist them through the API as ONE
// transaction; if the server rejects it we reload from the server, which reverts the optimistic UI change.
async function onActionComplete(args) {
    if (isSyncing) return;
    if (!['eventCreated', 'eventChanged', 'eventRemoved'].includes(args.requestType)) return;

    const strip = list => (list || []).filter(e => e && e.Kind !== 'pending' && e.Kind !== 'blackout');
    const added = strip(args.addedRecords), changed = strip(args.changedRecords), deleted = strip(args.deletedRecords);
    if (!added.length && !changed.length && !deleted.length) return;

    const payload = {
        Added: added.map(toServer), Changed: changed.map(toServer), Deleted: deleted.map(toServer),
        AdminOverride: isAdmin && cartUi.override ? cartUi.override.checked : false
    };
    const result = await submitBatch(payload);
    await loadEvents();

    if (result.ok) {
        const verb = args.requestType === 'eventCreated' ? 'Reservation booked' : args.requestType === 'eventChanged' ? 'Reservation updated' : 'Reservation cancelled';
        showToast(verb, result.warnings, 'e-toast-success');
    } else {
        showToast('Request rejected by booking rules', result.errors, 'e-toast-danger');
        // Re-open the editor for a rejected single booking so the user can fix it instead of starting over.
        if (args.requestType === 'eventCreated' && added.length === 1 && !added[0].RecurrenceID) {
            const retry = Object.assign({}, added[0]); delete retry.Id; delete retry.Guid;
            setTimeout(() => scheduleObj.openEditor(retry, 'Add', true), 300);
        }
    }
}

function onActionBegin(args) {
    // Customers cannot create events in cells the UI has marked unavailable (server re-checks anyway).
    if (args.requestType === 'eventCreate' && !isAdmin) {
        const recs = args.addedRecords || [];
        const bad = recs.find(r => !isRangeEligible(r.RangeId) || isOutsideWindow(new Date(r.StartTime)));
        if (bad) {
            args.cancel = true;
            showToast('Not available for your tier', [rangeLabel(bad.RangeId) + ' on ' + new Date(bad.StartTime).toLocaleString() + ' is outside your booking permissions.'], 'e-toast-warning');
        }
    }
}

function onActionFailure(args) {
    showToast('Scheduler error', [String(args && args.error && (args.error.message || args.error))], 'e-toast-danger');
}

function onCellClick(args) {
    if (cartUi.mode && cartUi.mode.checked) {
        args.cancel = true;
        addCellsToCart([{ startTime: args.startTime, endTime: args.endTime, isAllDay: args.isAllDay, groupIndex: args.groupIndex }]);
        return;
    }
    // Always open the full editor (custom fields are required), not the quick "add title" popup.
    if (!isAdmin && args.element && args.element.classList.contains('slot-disabled')) {
        args.cancel = true;
        showToast('Not available', ['This slot is outside your tier\'s booking window or range eligibility.'], 'e-toast-warning');
        return;
    }
    args.cancel = true;
    scheduleObj.openEditor({ startTime: args.startTime, endTime: args.endTime, isAllDay: args.isAllDay && isAdmin, groupIndex: args.groupIndex }, 'Add');
}

function onCellDoubleClick(args) {
    if (cartUi.mode && cartUi.mode.checked) args.cancel = true;
}

// Drag-selecting several cells while multi-slot mode is on adds the whole selection as one slot.
function onSelect(args) {
    if (!(cartUi.mode && cartUi.mode.checked) || args.requestType !== 'cellSelect') return;
    const cells = scheduleObj.getSelectedElements ? scheduleObj.getSelectedElements() : [];
    if (cells && cells.length > 1) {
        const d = scheduleObj.getCellDetails(cells);
        if (d) addCellsToCart([{ startTime: d.startTime, endTime: d.endTime, isAllDay: d.isAllDay, groupIndex: d.groupIndex }]);
    }
    args.showQuickPopup = false;
}

function onPopupOpen(args) {
    const data = args.data || {};
    if ((cartUi.mode && cartUi.mode.checked) && !data.Id && (args.type === 'QuickInfo' || args.type === 'Editor' || args.type === 'ViewEventInfo')) {
        args.cancel = true; return;
    }
    if (data.Kind === 'pending') {
        args.cancel = true; return;
    }
    if (args.type === 'QuickInfo' && data.Kind === 'masked') {
        // Public users only learn that the slot is taken.
        return;
    }
    if (args.type === 'Editor') {
        if (data.Kind === 'masked' || data.Kind === 'blackout') { args.cancel = true; return; }
        setupEditor(args);
    }
}

function onPopupClose() { /* no-op - kept for extension (e.g. analytics / audit) */ }

function onDragStart(args) { if (!isAdmin) args.cancel = true; else args.interval = 30; }
function onResizeStart(args) { if (!isAdmin) args.cancel = true; else args.interval = 30; }

function onRenderCell(args) {
    if (args.elementType === 'resourceHeader' && args.element && args.groupIndex !== undefined) {
        const res = scheduleObj.getResourcesByIndex(args.groupIndex);
        const range = res && res.resourceData && rangesById[res.resourceData.id];
        // Parent (facility) header cells share the group index of their first range - only decorate the range's own header.
        if (range && args.element.textContent.indexOf(range.Name) >= 0 && !args.element.classList.contains('e-parent-node')) {
            const meta = document.createElement('div');
            meta.className = 'res-meta';
            const tierCls = 'tier-' + range.MinimumTier.toLowerCase();
            meta.innerHTML = `<span class="badge ${tierCls}">${range.MinimumTier}+</span>`
                + (range.RequiredQualification ? `<span class="badge badge-qual" title="Qualification required">${escapeHtml(range.RequiredQualification)} cert</span>` : '')
                + `<span class="res-hours">${range.OpenTime}-${range.CloseTime}</span>`;
            const host = args.element.querySelector('.e-resource-text') || args.element;
            host.appendChild(meta);
        }
    }
    if (isAdmin) return;
    if (args.elementType === 'workCells' || args.elementType === 'monthCells') {
        let blocked = isOutsideWindow(args.date, args.elementType === 'monthCells');
        if (!blocked && args.groupIndex !== undefined) {
            const res = scheduleObj.getResourcesByIndex(args.groupIndex);
            if (res && res.groupData && !isRangeEligible(res.groupData.RangeId)) blocked = true;
        }
        if (blocked) args.element.classList.add('slot-disabled');
    }
}

function onEventRendered(args) {
    const d = args.data;
    const el = args.element;
    if (d.Kind === 'blackout') { el.classList.add('ev-blackout'); return; }
    if (d.Kind === 'masked') { el.classList.add('ev-masked'); el.style.backgroundColor = ''; return; }
    if (d.Kind === 'pending') { el.classList.add('ev-pending'); return; }
    if (!isAdmin && d.IsOwn) el.classList.add('ev-own');

    const subject = el.querySelector('.e-subject');
    if (subject && !el.querySelector('.ev-sub')) {
        const sub = document.createElement('div');
        sub.className = 'ev-sub';
        const parts = [];
        if (isAdmin && d.CustomerName) parts.push(d.CustomerName + (d.Tier ? ' · ' + d.Tier : ''));
        if (d.Participants) parts.push(d.Participants + (d.Participants === 1 ? ' shooter' : ' shooters'));
        if (d.FirearmType) parts.push(d.FirearmType + (d.Caliber ? ' ' + d.Caliber : ''));
        if (d.BookingGroupId) parts.push('multi-slot');
        if (d.AdminOverride) parts.push('override');
        sub.textContent = parts.join(' · ');
        subject.insertAdjacentElement('afterend', sub);
    }
}

/* ------------------------------------------------------------------------------------------
   Rule mirrors (UX only - the server enforces the same rules)
   ------------------------------------------------------------------------------------------ */
function isRangeEligible(rangeId) {
    if (isAdmin) return true;
    const r = rangesById[Number(rangeId)];
    return !!r && r.MinimumTierValue <= myTierRank;
}
function isOutsideWindow(date, wholeDay) {
    if (isAdmin || !myPolicy) return false;
    const now = facilityNow();
    const lastDay = addDays(startOfDay(now), myPolicy.AdvanceBookingDays + 1);
    if (date >= lastDay) return true;
    if (wholeDay) return date < startOfDay(now);
    return date < new Date(now.getTime() + myPolicy.MinLeadHours * 3600000);
}
const rangeLabel = id => (rangesById[Number(id)] || {}).Name || 'Range';

/* ------------------------------------------------------------------------------------------
   Editor customisation: extra reservation attributes + tier-aware restrictions
   ------------------------------------------------------------------------------------------ */
function setupEditor(args) {
    const el = args.element;
    const data = args.data || {};
    const form = el.querySelector('.e-dialog-parent') || el.querySelector('.e-schedule-form');

    if (!el.querySelector('.custom-fields')) {
        const wrap = document.createElement('div');
        wrap.className = 'custom-fields';
        wrap.innerHTML =
            '<div class="cf-title">Range & firearm details</div>' +
            '<div class="cf-grid">' +
            '  <div><input id="cfFirearm" /></div>' +
            '  <div><input id="cfCaliber" /></div>' +
            '  <div><input id="cfParticipants" /></div>' +
            '  <div><input id="cfQualification" /></div>' +
            (isAdmin ? '  <div class="cf-wide"><input id="cfCustomer" /></div>' : '') +
            '  <div class="cf-wide"><input id="cfRso" type="checkbox" /></div>' +
            '</div>' +
            '<div class="cf-hint" id="cfHint"></div>';
        form.appendChild(wrap);

        editor.firearm = new ej.dropdowns.DropDownList({ dataSource: ctx.Firearms, placeholder: 'Firearm type *', floatLabelType: 'Always' });
        editor.firearm.appendTo('#cfFirearm');
        editor.caliber = new ej.inputs.TextBox({ placeholder: 'Caliber / gauge', floatLabelType: 'Always' });
        editor.caliber.appendTo('#cfCaliber');
        editor.participants = new ej.inputs.NumericTextBox({ placeholder: 'Participants *', floatLabelType: 'Always', min: 1, max: 50, format: 'n0', decimals: 0, validateDecimalOnType: true });
        editor.participants.appendTo('#cfParticipants');
        editor.qualification = new ej.inputs.TextBox({ placeholder: 'Qualification / cert ID', floatLabelType: 'Always' });
        editor.qualification.appendTo('#cfQualification');
        if (isAdmin) {
            editor.customer = new ej.dropdowns.DropDownList({
                dataSource: [{ Id: '', Name: '(Facility hold - no customer)' }].concat(ctx.Customers.map(c => ({ Id: c.Id, Name: `${c.Name} (${c.Tier})` }))),
                fields: { text: 'Name', value: 'Id' }, placeholder: 'Book on behalf of', floatLabelType: 'Always'
            });
            editor.customer.appendTo('#cfCustomer');
        }
        editor.rso = new ej.buttons.CheckBox({ label: 'Request a Range Safety Officer (RSO)' });
        editor.rso.appendTo('#cfRso');

        // Scheduler collects every ".e-field" with a "name" when the event is saved.
        const setField = (sel, name) => { const i = el.querySelector(sel); if (i) { i.classList.add('e-field'); i.setAttribute('name', name); } };
        setField('#cfFirearm', 'FirearmType');
        setField('#cfCaliber', 'Caliber');
        setField('#cfParticipants', 'Participants');
        setField('#cfQualification', 'QualificationId');
        setField('#cfRso', 'RsoRequested');
        if (isAdmin) setField('#cfCustomer', 'CustomerId');

        // Update rule hint when the range changes.
        const rangeInput = el.querySelector('input[name="RangeId"]');
        const rangeDdl = rangeInput && rangeInput.ej2_instances && rangeInput.ej2_instances[0];
        if (rangeDdl && rangeDdl.addEventListener) rangeDdl.addEventListener('change', e => updateEditorHint(e.value));
    }

    const range = rangesById[Number(data.RangeId)];
    editor.firearm.value = data.FirearmType || (range ? range.AllowedFirearms[0] : null);
    editor.caliber.value = data.Caliber || '';
    editor.participants.value = data.Participants || 1;
    editor.qualification.value = data.QualificationId || '';
    editor.rso.checked = !!data.RsoRequested;
    if (editor.customer) editor.customer.value = data.CustomerId || '';
    updateEditorHint(data.RangeId);

    // Subject placeholder
    const subj = el.querySelector('.e-subject');
    if (subj) subj.setAttribute('placeholder', 'Reservation title (e.g. "Pistol practice")');
    if (!data.Subject && subj && !data.Id) subj.value = '';

    // ---- Tier-aware restrictions on the built-in controls ----
    const allDayRow = el.querySelector('.e-all-day-container');
    if (allDayRow) allDayRow.style.display = isAdmin ? '' : 'none';

    const recEl = el.querySelector('.e-recurrenceeditor');
    const recEditor = recEl && recEl.ej2_instances && recEl.ej2_instances[0];
    const repeatRow = recEl;
    const canRecur = isAdmin || (myPolicy && myPolicy.AllowRecurring);
    if (repeatRow) repeatRow.style.display = canRecur ? '' : 'none';
    if (recEditor && !isAdmin && myPolicy) {
        recEditor.endTypes = ['count', 'until'];                 // customers must give recurring bookings an end
        recEditor.maxDate = addDays(startOfDay(facilityNow()), myPolicy.AdvanceBookingDays);
        recEditor.dataBind();
    }

    if (!isAdmin && myPolicy) {
        const maxDate = addDays(startOfDay(facilityNow()), myPolicy.AdvanceBookingDays + 1);
        ['.e-start', '.e-end'].forEach(sel => {
            const i = el.querySelector(sel);
            const inst = i && i.ej2_instances && i.ej2_instances[0];
            if (inst) { inst.min = startOfDay(facilityNow()); inst.max = maxDate; inst.dataBind(); }
        });
    }
}

function updateEditorHint(rangeId) {
    const hint = document.getElementById('cfHint');
    const r = rangesById[Number(rangeId)];
    if (!hint) return;
    if (!r) { hint.textContent = ''; return; }
    const tierLimit = myPolicy ? ` · your tier: max ${myPolicy.MaxHoursPerBooking}h, ${myPolicy.AllowRecurring ? 'up to ' + myPolicy.MaxRecurringOccurrences + ' repeats' : 'no repeats'}` : '';
    hint.innerHTML =
        `<strong>${escapeHtml(r.Name)}</strong>: ${escapeHtml(r.AllowedFirearms.join(', '))} · max ${r.MaxParticipants} participants · max ${r.MaxHoursPerBooking}h · ${r.OpenTime}-${r.CloseTime} · ${r.MinimumTier}+ tier` +
        (r.RequiredQualification ? ` · <span class="warn">requires ${escapeHtml(r.RequiredQualification)} qualification ID</span>` : '') +
        escapeHtml(tierLimit);
}

/* ------------------------------------------------------------------------------------------
   Toolbar: facility filter, eligible-range filter, admin override
   ------------------------------------------------------------------------------------------ */
function initToolbar() {
    const facilityDdl = new ej.dropdowns.DropDownList({
        dataSource: [{ Id: 0, Name: 'All facilities' }].concat(ctx.Facilities.map(f => ({ Id: f.Id, Name: `${f.Name} - ${f.City}` }))),
        fields: { text: 'Name', value: 'Id' }, value: 0, width: '320px',
        change: applyResourceFilter
    });
    facilityDdl.appendTo('#facilityFilter');
    cartUi.facility = facilityDdl;

    if (!isAdmin) {
        cartUi.eligible = new ej.buttons.CheckBox({ label: 'Only ranges I can book', change: applyResourceFilter });
        cartUi.eligible.appendTo('#eligibleOnly');
    } else {
        cartUi.override = new ej.buttons.Switch({ checked: false });
        cartUi.override.appendTo('#adminOverride');
        const label = document.createElement('label');
        label.className = 'switch-label'; label.setAttribute('for', 'adminOverride');
        label.textContent = 'Override tier rules on save';
        document.getElementById('adminOverride').closest('.e-switch-wrapper').insertAdjacentElement('afterend', label);
    }
    document.getElementById('refreshBtn').addEventListener('click', () => loadEvents());
}

// Resource-specific availability views: filter facilities / ranges shown in the scheduler.
function applyResourceFilter() {
    const fid = cartUi.facility.value || 0;
    const eligibleOnly = cartUi.eligible ? cartUi.eligible.checked : false;
    const ranges = ctx.Ranges
        .filter(r => !fid || r.FacilityId === fid)
        .filter(r => !eligibleOnly || isRangeEligible(r.Id));
    const facIds = new Set(ranges.map(r => r.FacilityId));
    scheduleObj.resources[0].dataSource = ctx.Facilities.filter(f => facIds.has(f.Id)).map(f => ({ id: f.Id, text: f.Name, color: f.Color }));
    scheduleObj.resources[1].dataSource = ranges.map(r => ({ id: r.Id, text: r.Name, groupId: r.FacilityId, color: r.Color }));
    scheduleObj.refresh();
    setTimeout(renderDataSource, 0);
}

/* ------------------------------------------------------------------------------------------
   Multi-slot / multi-date cart (one transaction)
   ------------------------------------------------------------------------------------------ */
function initCart() {
    cartUi.mode = new ej.buttons.Switch({ checked: false, change: e => document.body.classList.toggle('cart-on', e.checked) });
    cartUi.mode.appendTo('#cartMode');
    cartUi.subject = new ej.inputs.TextBox({ placeholder: 'Title for all slots', floatLabelType: 'Auto', value: 'Range session' });
    cartUi.subject.appendTo('#cartSubject');
    cartUi.firearm = new ej.dropdowns.DropDownList({ dataSource: ctx.Firearms, placeholder: 'Firearm type', floatLabelType: 'Auto', value: 'Handgun' });
    cartUi.firearm.appendTo('#cartFirearm');
    cartUi.caliber = new ej.inputs.TextBox({ placeholder: 'Caliber', floatLabelType: 'Auto' });
    cartUi.caliber.appendTo('#cartCaliber');
    cartUi.participants = new ej.inputs.NumericTextBox({ placeholder: 'Participants', floatLabelType: 'Auto', min: 1, max: 50, value: 1, format: 'n0' });
    cartUi.participants.appendTo('#cartParticipants');
    cartUi.qualification = new ej.inputs.TextBox({ placeholder: 'Qualification ID (if required)', floatLabelType: 'Auto' });
    cartUi.qualification.appendTo('#cartQualification');
    if (isAdmin) {
        cartUi.customer = new ej.dropdowns.DropDownList({
            dataSource: ctx.Customers.map(c => ({ Id: c.Id, Name: `${c.Name} (${c.Tier})` })),
            fields: { text: 'Name', value: 'Id' }, placeholder: 'Book on behalf of', floatLabelType: 'Auto', value: ctx.Customers[0].Id
        });
        cartUi.customer.appendTo('#cartCustomer');
    }
    cartUi.rso = new ej.buttons.CheckBox({ label: 'Request RSO' });
    cartUi.rso.appendTo('#cartRso');
    cartUi.dayStart = new ej.calendars.TimePicker({ placeholder: 'Day start', floatLabelType: 'Auto', value: new Date(2000, 0, 1, 9, 0), step: 30 });
    cartUi.dayStart.appendTo('#cartDayStart');
    cartUi.duration = new ej.inputs.NumericTextBox({ placeholder: 'Hours', floatLabelType: 'Auto', min: 1, max: 8, value: 1, format: 'n0' });
    cartUi.duration.appendTo('#cartDuration');

    document.getElementById('cartCheck').addEventListener('click', () => submitCart(true));
    document.getElementById('cartBook').addEventListener('click', () => submitCart(false));
    document.getElementById('cartClear').addEventListener('click', () => { cart = []; renderCart(); renderDataSource(); setCartResult(''); });
    renderCart();
}

function addCellsToCart(cells) {
    let added = 0;
    cells.forEach(c => {
        const res = scheduleObj.getResourcesByIndex(c.groupIndex);
        if (!res || !res.groupData) return;
        let start = new Date(c.startTime), end = new Date(c.endTime);
        if (c.isAllDay || (end - start) >= 24 * 3600000) {        // month view / all-day cell -> use default time
            const t = cartUi.dayStart.value || new Date(2000, 0, 1, 9, 0);
            start = new Date(start.getFullYear(), start.getMonth(), start.getDate(), t.getHours(), t.getMinutes());
            end = new Date(start.getTime() + (cartUi.duration.value || 1) * 3600000);
        }
        const rangeId = res.groupData.RangeId, facilityId = res.groupData.FacilityId;
        const key = `${rangeId}|${start.getTime()}|${end.getTime()}`;
        const existing = cart.findIndex(i => i.key === key);
        if (existing >= 0) { cart.splice(existing, 1); return; }   // clicking again toggles the slot off
        cart.push({ key, id: --cartSeq, rangeId, facilityId, start, end });
        added++;
    });
    cart.sort((a, b) => a.start - b.start || a.rangeId - b.rangeId);
    renderCart();
    renderDataSource();
    if (added) setCartResult('');
}

function cartItemToEvent(i) {
    return { Id: i.id, Subject: 'Selected', StartTime: i.start, EndTime: i.end, RangeId: i.rangeId, FacilityId: i.facilityId, Kind: 'pending', IsReadonly: true };
}

function renderCart() {
    const ul = document.getElementById('cartList');
    if (!cart.length) { ul.innerHTML = '<li class="empty">No slots selected</li>'; }
    else {
        ul.innerHTML = cart.map(i =>
            `<li><span><strong>${escapeHtml(rangeLabel(i.rangeId))}</strong><br/>${i.start.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })}, ` +
            `${i.start.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })} - ${i.end.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</span>` +
            `<button class="e-btn e-flat e-small" data-key="${i.key}" title="Remove">✕</button></li>`).join('');
        ul.querySelectorAll('button[data-key]').forEach(b => b.addEventListener('click', () => {
            cart = cart.filter(i => i.key !== b.dataset.key); renderCart(); renderDataSource();
        }));
    }
    const limit = myPolicy ? ` / ${myPolicy.MaxSlotsPerTransaction} max` : '';
    document.getElementById('cartBook').textContent = `Book all (${cart.length}${limit})`;
}

function setCartResult(html) { document.getElementById('cartResult').innerHTML = html; }

async function submitCart(dryRun) {
    if (!cart.length) { setCartResult('<div class="msg warn">Turn on multi-slot booking and click some slots first.</div>'); return; }
    const added = cart.map(i => toServer({
        Subject: cartUi.subject.value || 'Range session', StartTime: i.start, EndTime: i.end, RangeId: i.rangeId, FacilityId: i.facilityId,
        FirearmType: cartUi.firearm.value, Caliber: cartUi.caliber.value, Participants: cartUi.participants.value,
        QualificationId: cartUi.qualification.value, RsoRequested: cartUi.rso.checked,
        CustomerId: cartUi.customer ? cartUi.customer.value : null
    }));
    const result = await submitBatch({ Added: added, Changed: [], Deleted: [], DryRun: dryRun, AdminOverride: isAdmin && cartUi.override ? cartUi.override.checked : false });
    if (result.ok) {
        if (dryRun) {
            setCartResult(`<div class="msg ok">All ${cart.length} slot(s) are available and comply with the booking rules (${result.body.OccurrencesValidated} occurrence(s) checked).</div>`);
        } else {
            setCartResult(`<div class="msg ok">Booked ${cart.length} slot(s) as transaction <code>${escapeHtml(result.body.BookingGroupId || 'single')}</code>.</div>`);
            cart = []; renderCart(); await loadEvents();
            showToast('Multi-slot booking confirmed', [], 'e-toast-success');
        }
    } else {
        setCartResult('<div class="msg err"><strong>' + (dryRun ? 'Not bookable as selected:' : 'Nothing was booked (all-or-nothing):') + '</strong><ul>' +
            result.errors.map(e => '<li>' + escapeHtml(e) + '</li>').join('') + '</ul></div>');
    }
}

/* ------------------------------------------------------------------------------------------
   Policy card
   ------------------------------------------------------------------------------------------ */
function renderPolicyCard() {
    const card = document.getElementById('policyCard');
    if (isAdmin) {
        card.innerHTML = `<h3>Administrator</h3>
          <ul class="kv">
            <li>Drag, resize and edit any reservation</li>
            <li>Book on behalf of any customer</li>
            <li>Optional tier-rule override (conflicts, blackouts and safety rules always apply)</li>
            <li>Manage <a href="${appUrl('/Admin/Blackouts')}">blackouts</a> and <a href="${appUrl('/Admin/Policies')}">tier policies</a></li>
          </ul>`;
        return;
    }
    const p = myPolicy;
    card.innerHTML = `<h3>Your booking policy <span class="badge tier-${p.Tier.toLowerCase()}">${p.Tier}</span></h3>
      <p class="muted small">${escapeHtml(p.Audience)}</p>
      <ul class="kv">
        <li><span>Book ahead</span><strong>up to ${p.AdvanceBookingDays} days</strong></li>
        <li><span>Lead time</span><strong>${p.MinLeadHours} h</strong></li>
        <li><span>Max per booking</span><strong>${p.MaxHoursPerBooking} h</strong></li>
        <li><span>Bookings per day</span><strong>${p.MaxBookingsPerDay}</strong></li>
        <li><span>Upcoming bookings</span><strong>${p.MaxActiveFutureBookings}</strong></li>
        <li><span>Hours per month</span><strong>${p.MaxHoursPerMonth}</strong></li>
        <li><span>Recurring</span><strong>${p.AllowRecurring ? 'up to ' + p.MaxRecurringOccurrences + ' occurrences' : 'not allowed'}</strong></li>
        <li><span>Slots per transaction</span><strong>${p.MaxSlotsPerTransaction}</strong></li>
        <li><span>Change / cancel cutoff</span><strong>${p.CancellationCutoffHours} h</strong></li>
      </ul>`;
}

/* ------------------------------------------------------------------------------------------ */
toastObj = new ej.notifications.Toast({ position: { X: 'Right', Y: 'Top' }, showCloseButton: true, newestOnTop: true, width: '380px' });
toastObj.appendTo('#toast');
