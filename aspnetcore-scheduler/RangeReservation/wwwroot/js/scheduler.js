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
const tierRank = { Tier1: 1, Tier2: 2, Tier3: 3, Tier4: 4 };
const myTierRank = ctx.User.Tier ? tierRank[ctx.User.Tier] : 99;
const recurrenceCap = fromWallLate(ctx.RecurrenceCap);   // UC-09: end of the following calendar year
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
    const m = /(\d{4})-(\d{2})-(\d{2})T(\d{2})[:.](\d{2})(?:[:.](\d{2}))?/.exec(s);
    return m ? new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0)) : new Date(s);
}
// Parses facility wall-clock "yyyy-MM-ddTHH:mm:ss" (also tolerates "." as time separator) without throwing.
function fromWallLate(s) {
    const m = /(\d{4})-(\d{2})-(\d{2})T(\d{2})[:.](\d{2})(?:[:.](\d{2}))?/.exec(s || '');
    if (m) return new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +(m[6] || 0));
    const d = new Date(s);
    return isNaN(d) ? new Date(new Date().getFullYear() + 1, 11, 31, 23, 59, 59) : d;
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
        GroupId: num(e.GroupId),
        Headcount: num(e.Headcount),
        FirearmTypes: Array.isArray(e.FirearmTypes) ? e.FirearmTypes : (e.FirearmTypes ? String(e.FirearmTypes).split(',') : []),
        TargetType: e.TargetType || null,
        TargetDistanceYards: num(e.TargetDistanceYards),
        TargetCount: num(e.TargetCount),
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
        let verb;
        if (args.requestType === 'eventCreated')
            verb = result.body.Status === 'Pending' ? `Request ${result.body.RequestId} submitted for admin review` : `Reservation ${result.body.RequestId} approved`;
        else verb = args.requestType === 'eventChanged' ? 'Reservation updated' : 'Reservation cancelled';
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
        const bad = recs.find(r => !isRangeEligible(r.RangeId) || isMondayBlocked(new Date(r.StartTime)) || (!r.RecurrenceRule && isOutsideWindow(new Date(r.StartTime))));
        if (bad) {
            args.cancel = true;
            const why = isMondayBlocked(new Date(bad.StartTime)) ? 'Mondays are reserved exclusively for Tier 4 (Law Enforcement / Military) groups.'
                : rangeLabel(bad.RangeId) + ' on ' + new Date(bad.StartTime).toLocaleString() + ' is outside your group\'s booking permissions.';
            showToast('Not available for your group', [why], 'e-toast-warning');
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
        showToast('Not available', [isMondayBlocked(args.startTime) ? 'Mondays are reserved exclusively for Tier 4 (Law Enforcement / Military) groups.' : 'This slot is outside your group\'s booking window or range eligibility.'], 'e-toast-warning');
        return;
    }
    args.cancel = true;
    openNewEditor(args.startTime, args.endTime, args.groupIndex, args.isAllDay && isAdmin);
}

// Opens the full editor for a new reservation on the clicked range. Event data (not cell data) is passed so the
// Facility / Range fields are pre-filled for every row - cell data with groupIndex 0 would leave them empty.
function openNewEditor(start, end, groupIndex, isAllDay) {
    const res = scheduleObj.getResourcesByIndex(groupIndex);
    const g = (res && res.groupData) || {};
    const r = rangesById[Number(g.RangeId)];
    scheduleObj.openEditor({
        StartTime: new Date(start), EndTime: new Date(end), IsAllDay: !!isAllDay, FacilityId: g.FacilityId, RangeId: g.RangeId,
        Headcount: 1, FirearmTypes: r ? [r.AllowedFirearms[0]] : [], TargetType: r ? r.AllowedTargetTypes[0] : null
    }, 'Add', true);
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
    if (args.type === 'QuickInfo' && data.Kind === 'masked') return;   // other organizations: only "Reserved"
    if (args.type === 'QuickInfo' && data.Kind === 'reservation') {
        const content = args.element.querySelector('.e-popup-content');
        if (content) {
            content.querySelectorAll('.qi-extra').forEach(x => x.remove());
            const div = document.createElement('div');
            div.className = 'qi-extra';
            div.innerHTML = `<span class="status status-${(data.Status || '').toLowerCase()}">${escapeHtml(data.Status)}</span> <code>${escapeHtml(data.RequestId)}</code>` +
                `<div>${escapeHtml(data.GroupName || '')}${data.Tier ? ' · ' + escapeHtml(data.Tier) : ''}</div>` +
                `<div class="muted small">${data.Headcount || 0} people · ${escapeHtml((data.FirearmTypes || []).join(', '))} · ${escapeHtml(data.TargetType || '')}${data.TargetDistanceYards ? ' @ ' + data.TargetDistanceYards + ' yd' : ''}</div>` +
                `<div class="muted small">Submitted by ${escapeHtml(data.SubmittedByName || '')}</div>`;
            content.appendChild(div);
        }
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
            meta.innerHTML = (range.MinimumTierValue > 1 ? `<span class="badge tier-${range.MinimumTierValue}">${range.MinimumTierName} only</span>` : '')
                + (range.IsSpecialized ? `<span class="badge badge-qual" title="${escapeHtml(range.SpecializedRequirement)}">Specialized</span>` : '')
                + `<span class="res-hours">${range.OpenTime}-${range.CloseTime}</span>`;
            const host = args.element.querySelector('.e-resource-text') || args.element;
            host.appendChild(meta);
        }
    }
    if (isAdmin) return;
    if (args.elementType === 'workCells' || args.elementType === 'monthCells') {
        let blocked = isMondayBlocked(args.date) || isOutsideWindow(args.date, args.elementType === 'monthCells');
        if (isMondayBlocked(args.date)) args.element.classList.add('slot-monday');
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
    if (d.Status === 'Pending') el.classList.add('ev-review');
    if (d.Status === 'Approved') el.classList.add('ev-approved');
    if (!isAdmin && d.IsOwn) el.classList.add('ev-own');

    const subject = el.querySelector('.e-subject');
    if (subject && !el.querySelector('.ev-sub')) {
        const sub = document.createElement('div');
        sub.className = 'ev-sub';
        const parts = [];
        if (d.Status === 'Pending') parts.push('PENDING REVIEW');
        if (isAdmin && d.GroupName) parts.push(d.GroupName + (d.Tier ? ' · ' + d.Tier : ''));
        if (d.Headcount) parts.push(d.Headcount + ' people');
        if (d.FirearmTypes && d.FirearmTypes.length) parts.push(d.FirearmTypes.join('/'));
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
// UC-11: Monday slots are reserved for Tier 4 (Law Enforcement / Military).
function isMondayBlocked(date) {
    return !isAdmin && ctx.Settings.MondayExclusiveForTier4 && ctx.User.Tier !== 'Tier4' && date && new Date(date).getDay() === 1;
}
const rangeLabel = id => (rangesById[Number(id)] || {}).Name || 'Range';

/* ------------------------------------------------------------------------------------------
   Editor customisation (UC-06): headcount, firearm types, target attributes + tier-aware restrictions
   ------------------------------------------------------------------------------------------ */
function setupEditor(args) {
    const el = args.element;
    const data = args.data || {};
    const form = el.querySelector('.e-dialog-parent') || el.querySelector('.e-schedule-form');

    if (!el.querySelector('.custom-fields')) {
        const wrap = document.createElement('div');
        wrap.className = 'custom-fields';
        wrap.innerHTML =
            '<div class="cf-title">Reservation details</div>' +
            '<div class="cf-grid">' +
            (isAdmin ? '  <div class="cf-wide"><input id="cfGroup" /></div>' : '') +
            '  <div><input id="cfHeadcount" /></div>' +
            '  <div><input id="cfFirearms" /></div>' +
            '  <div><input id="cfTargetType" /></div>' +
            '  <div class="two-inline"><input id="cfTargetDistance" /><input id="cfTargetCount" /></div>' +
            '  <div class="cf-wide"><input id="cfRso" type="checkbox" /></div>' +
            '</div>' +
            '<div class="cf-hint" id="cfHint"></div>';
        form.appendChild(wrap);

        if (isAdmin) {
            editor.group = new ej.dropdowns.DropDownList({
                dataSource: [{ Id: 0, Name: '(Facility hold - no group)' }].concat(ctx.Groups.map(g => ({ Id: g.Id, Name: `${g.Name} (${g.TierName})` }))),
                fields: { text: 'Name', value: 'Id' }, placeholder: 'Book on behalf of (auto-approved)', floatLabelType: 'Always'
            });
            editor.group.appendTo('#cfGroup');
        }
        editor.headcount = new ej.inputs.NumericTextBox({ placeholder: 'Headcount *', floatLabelType: 'Always', min: 1, max: 100, format: 'n0', decimals: 0, validateDecimalOnType: true });
        editor.headcount.appendTo('#cfHeadcount');
        editor.firearms = new ej.dropdowns.MultiSelect({ dataSource: ctx.Firearms, placeholder: 'Firearm types *', floatLabelType: 'Always', mode: 'Box' });
        editor.firearms.appendTo('#cfFirearms');
        editor.targetType = new ej.dropdowns.DropDownList({ dataSource: ctx.TargetTypes, placeholder: 'Target type *', floatLabelType: 'Always' });
        editor.targetType.appendTo('#cfTargetType');
        editor.targetDistance = new ej.inputs.NumericTextBox({ placeholder: 'Distance (yd)', floatLabelType: 'Always', min: 1, max: 1000, format: 'n0' });
        editor.targetDistance.appendTo('#cfTargetDistance');
        editor.targetCount = new ej.inputs.NumericTextBox({ placeholder: 'Targets', floatLabelType: 'Always', min: 1, max: 200, format: 'n0' });
        editor.targetCount.appendTo('#cfTargetCount');
        editor.rso = new ej.buttons.CheckBox({ label: 'Request a Range Safety Officer (RSO)' });
        editor.rso.appendTo('#cfRso');

        // The Scheduler collects every ".e-field" with a "name" when the event is saved.
        const setField = (sel, name) => { const i = el.querySelector(sel); if (i) { i.classList.add('e-field'); i.setAttribute('name', name); } };
        if (isAdmin) setField('#cfGroup', 'GroupId');
        setField('#cfHeadcount', 'Headcount');
        setField('#cfFirearms', 'FirearmTypes');
        setField('#cfTargetType', 'TargetType');
        setField('#cfTargetDistance', 'TargetDistanceYards');
        setField('#cfTargetCount', 'TargetCount');
        setField('#cfRso', 'RsoRequested');

        const rangeInput = el.querySelector('input[name="RangeId"]');
        const rangeDdl = rangeInput && rangeInput.ej2_instances && rangeInput.ej2_instances[0];
        if (rangeDdl && rangeDdl.addEventListener) rangeDdl.addEventListener('change', e => applyRangeToEditor(e.value, false));
    }

    // The Scheduler fills ".e-field" inputs from the event data after popupOpen, so values are applied now and
    // again once it has finished (otherwise defaults for a new reservation would be cleared).
    const fill = () => {
        if (editor.group) editor.group.value = data.GroupId || 0;
        editor.headcount.value = data.Headcount || 1;
        editor.firearms.value = (data.FirearmTypes && data.FirearmTypes.length) ? data.FirearmTypes.slice() : null;
        editor.targetType.value = data.TargetType || null;
        editor.targetDistance.value = data.TargetDistanceYards || null;
        editor.targetCount.value = data.TargetCount || null;
        editor.rso.checked = !!data.RsoRequested;
        applyRangeToEditor(data.RangeId, !data.Id);
    };
    fill();
    setTimeout(fill, 0);

    const subj = el.querySelector('.e-subject');
    if (subj) subj.setAttribute('placeholder', 'Reservation title (e.g. "Quarterly qualification")');

    const allDayRow = el.querySelector('.e-all-day-container');
    if (allDayRow) allDayRow.style.display = isAdmin ? '' : 'none';

    // UC-09: weekly / monthly only, must end, capped at the end of the following calendar year.
    const recEl = el.querySelector('.e-recurrenceeditor');
    const recEditor = recEl && recEl.ej2_instances && recEl.ej2_instances[0];
    const repeatRow = recEl && (recEl.closest('.e-repeat-parent-row') || recEl.parentElement);
    const canRecur = isAdmin || (myPolicy && myPolicy.AllowRecurring);
    if (repeatRow) repeatRow.style.display = canRecur ? '' : 'none';
    if (recEditor) {
        recEditor.frequencies = ['none', 'weekly', 'monthly'];
        recEditor.endTypes = ['until', 'count'];
        recEditor.maxDate = recurrenceCap;
        recEditor.dataBind();
    }

    if (!isAdmin && myPolicy) {
        ['.e-start', '.e-end'].forEach(sel => {
            const i = el.querySelector(sel);
            const inst = i && i.ej2_instances && i.ej2_instances[0];
            if (inst) { inst.min = startOfDay(facilityNow()); inst.max = recurrenceCap; inst.dataBind(); }
        });
    }
}

// Range-specific firearm / target options and rule hint.
function applyRangeToEditor(rangeId, isNew) {
    const r = rangesById[Number(rangeId)];
    const hint = document.getElementById('cfHint');
    if (!r) { if (hint) hint.textContent = ''; return; }
    // Changing a dropdown's dataSource clears its value, so re-apply the (still valid) selection afterwards.
    const keepFirearms = (editor.firearms.value || []).filter(f => r.AllowedFirearms.includes(f));
    const keepTarget = r.AllowedTargetTypes.includes(editor.targetType.value) ? editor.targetType.value : null;
    editor.firearms.dataSource = r.AllowedFirearms; editor.firearms.dataBind();
    editor.targetType.dataSource = r.AllowedTargetTypes; editor.targetType.dataBind();
    editor.targetDistance.max = r.MaxTargetDistanceYards;
    editor.headcount.max = r.MaxHeadcount;
    editor.firearms.value = keepFirearms.length ? keepFirearms : (isNew ? [r.AllowedFirearms[0]] : null);
    editor.targetType.value = keepTarget || (isNew ? r.AllowedTargetTypes[0] : null);
    editor.firearms.dataBind(); editor.targetType.dataBind();
    const tierLimit = myPolicy ? ` · ${ctx.User.TierName}: max ${myPolicy.MaxHoursPerBooking} h, ${myPolicy.AllowRecurring ? 'weekly/monthly repeats' : 'no repeats'}` : '';
    hint.innerHTML =
        `<strong>${escapeHtml(r.Name)}</strong>: ${escapeHtml(r.AllowedFirearms.join(', '))} · targets ${escapeHtml(r.AllowedTargetTypes.join(', '))} up to ${r.MaxTargetDistanceYards} yd · ` +
        `headcount ≤ ${r.MaxHeadcount} · ${r.OpenTime}-${r.CloseTime}` + escapeHtml(tierLimit) +
        `<br/>Repeating series must end by <strong>${recurrenceCap.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}</strong>.` +
        (ctx.Settings.MondayExclusiveForTier4 && ctx.User.Tier !== 'Tier4' && !isAdmin ? ' Mondays are reserved for Tier 4 groups.' : '') +
        (isAdmin ? '' : ' <span class="warn">Submitted requests are reviewed by AZGFD before confirmation.</span>');
}

/* ------------------------------------------------------------------------------------------
   Toolbar: facility filter, admin override
   ------------------------------------------------------------------------------------------ */
function initToolbar() {
    const facilityDdl = new ej.dropdowns.DropDownList({
        dataSource: [{ Id: 0, Name: 'All facilities' }].concat(ctx.Facilities.map(f => ({ Id: f.Id, Name: `${f.Name} - ${f.City}` }))),
        fields: { text: 'Name', value: 'Id' }, value: 0, width: '320px',
        change: applyResourceFilter
    });
    facilityDdl.appendTo('#facilityFilter');
    cartUi.facility = facilityDdl;

    if (isAdmin) {
        cartUi.override = new ej.buttons.Switch({ checked: false });
        cartUi.override.appendTo('#adminOverride');
        const label = document.createElement('label');
        label.className = 'switch-label'; label.setAttribute('for', 'adminOverride');
        label.textContent = 'Override tier policy on save';
        document.getElementById('adminOverride').closest('.e-switch-wrapper').insertAdjacentElement('afterend', label);
    }
    document.getElementById('refreshBtn').addEventListener('click', () => loadEvents());
}

// Resource-specific availability views (only authorized ranges are sent to the page - UC-14).
function applyResourceFilter() {
    const fid = cartUi.facility.value || 0;
    const ranges = ctx.Ranges.filter(r => !fid || r.FacilityId === fid);
    const facIds = new Set(ranges.map(r => r.FacilityId));
    scheduleObj.resources[0].dataSource = ctx.Facilities.filter(f => facIds.has(f.Id)).map(f => ({ id: f.Id, text: f.Name, color: f.Color }));
    scheduleObj.resources[1].dataSource = ranges.map(r => ({ id: r.Id, text: r.Name, groupId: r.FacilityId, color: r.Color }));
    scheduleObj.refresh();
    setTimeout(renderDataSource, 0);
}

/* ------------------------------------------------------------------------------------------
   UC-07 / UC-08: multi-date cart for ONE range
   ------------------------------------------------------------------------------------------ */
function initCart() {
    cartUi.mode = new ej.buttons.Switch({ checked: false, change: e => document.body.classList.toggle('cart-on', e.checked) });
    cartUi.mode.appendTo('#cartMode');
    cartUi.subject = new ej.inputs.TextBox({ placeholder: 'Title for all dates', floatLabelType: 'Auto', value: 'Range session' });
    cartUi.subject.appendTo('#cartSubject');
    if (isAdmin) {
        cartUi.group = new ej.dropdowns.DropDownList({
            dataSource: ctx.Groups.map(g => ({ Id: g.Id, Name: `${g.Name} (${g.TierName})` })),
            fields: { text: 'Name', value: 'Id' }, placeholder: 'Book on behalf of', floatLabelType: 'Auto', value: ctx.Groups[0].Id
        });
        cartUi.group.appendTo('#cartGroup');
    }
    cartUi.headcount = new ej.inputs.NumericTextBox({ placeholder: 'People', floatLabelType: 'Auto', min: 1, max: 100, value: 1, format: 'n0' });
    cartUi.headcount.appendTo('#cartHeadcount');
    cartUi.targetCount = new ej.inputs.NumericTextBox({ placeholder: 'Targets', floatLabelType: 'Auto', min: 1, max: 200, format: 'n0' });
    cartUi.targetCount.appendTo('#cartTargetCount');
    cartUi.firearms = new ej.dropdowns.MultiSelect({ dataSource: ctx.Firearms, placeholder: 'Firearm types', floatLabelType: 'Auto', mode: 'Box', value: ['Handgun'] });
    cartUi.firearms.appendTo('#cartFirearms');
    cartUi.targetType = new ej.dropdowns.DropDownList({ dataSource: ctx.TargetTypes, placeholder: 'Target', floatLabelType: 'Auto', value: 'Paper' });
    cartUi.targetType.appendTo('#cartTargetType');
    cartUi.targetDistance = new ej.inputs.NumericTextBox({ placeholder: 'Yards', floatLabelType: 'Auto', min: 1, max: 1000, value: 15, format: 'n0' });
    cartUi.targetDistance.appendTo('#cartTargetDistance');
    cartUi.rso = new ej.buttons.CheckBox({ label: 'Request RSO' });
    cartUi.rso.appendTo('#cartRso');
    cartUi.dayStart = new ej.calendars.TimePicker({ placeholder: 'Start', floatLabelType: 'Auto', value: new Date(2000, 0, 1, 9, 0), step: 30 });
    cartUi.dayStart.appendTo('#cartDayStart');
    cartUi.duration = new ej.inputs.NumericTextBox({ placeholder: 'Hours', floatLabelType: 'Auto', min: 1, max: 8, value: 2, format: 'n0' });
    cartUi.duration.appendTo('#cartDuration');

    document.getElementById('cartCheck').addEventListener('click', () => submitCart(true));
    document.getElementById('cartBook').addEventListener('click', () => submitCart(false));
    document.getElementById('cartClear').addEventListener('click', () => { cart = []; renderCart(); renderDataSource(); setCartResult(''); });
    renderCart();
}

function addCellsToCart(cells) {
    let added = 0;
    for (const c of cells) {
        const res = scheduleObj.getResourcesByIndex(c.groupIndex);
        if (!res || !res.groupData) continue;
        let start = new Date(c.startTime), end = new Date(c.endTime);
        if (c.isAllDay || (end - start) >= 24 * 3600000) {        // month view / all-day cell -> use default time
            const t = cartUi.dayStart.value || new Date(2000, 0, 1, 9, 0);
            start = new Date(start.getFullYear(), start.getMonth(), start.getDate(), t.getHours(), t.getMinutes());
            end = new Date(start.getTime() + (cartUi.duration.value || 1) * 3600000);
        }
        const rangeId = res.groupData.RangeId, facilityId = res.groupData.FacilityId;
        const key = `${rangeId}|${start.getTime()}|${end.getTime()}`;
        const existing = cart.findIndex(i => i.key === key);
        if (existing >= 0) { cart.splice(existing, 1); continue; }   // clicking again toggles the slot off

        // UC-08: one range per multi-date booking.
        if (cart.length && cart[0].rangeId !== rangeId) {
            showToast('Cannot add this slot', [ctx.SingleRangeMessage], 'e-toast-danger');
            setCartResult(`<div class="msg err">${escapeHtml(ctx.SingleRangeMessage)}</div>`);
            continue;
        }
        if (isMondayBlocked(start)) { showToast('Not available', ['Mondays are reserved exclusively for Tier 4 (Law Enforcement / Military) groups.'], 'e-toast-warning'); continue; }
        if (!cart.length) applyRangeToCart(rangeId);
        cart.push({ key, id: --cartSeq, rangeId, facilityId, start, end });
        added++;
    }
    cart.sort((a, b) => a.start - b.start);
    renderCart();
    renderDataSource();
    if (added) setCartResult('');
}

function applyRangeToCart(rangeId) {
    const r = rangesById[Number(rangeId)];
    if (!r) return;
    cartUi.firearms.dataSource = r.AllowedFirearms; cartUi.firearms.dataBind();
    cartUi.firearms.value = [r.AllowedFirearms[0]];
    cartUi.targetType.dataSource = r.AllowedTargetTypes; cartUi.targetType.dataBind();
    cartUi.targetType.value = r.AllowedTargetTypes[0];
    cartUi.targetDistance.max = r.MaxTargetDistanceYards;
    if (cartUi.targetDistance.value > r.MaxTargetDistanceYards) cartUi.targetDistance.value = r.MaxTargetDistanceYards;
}

function cartItemToEvent(i) {
    return { Id: i.id, Subject: 'Selected', StartTime: i.start, EndTime: i.end, RangeId: i.rangeId, FacilityId: i.facilityId, Kind: 'pending', IsReadonly: true };
}

function renderCart() {
    const ul = document.getElementById('cartList');
    const rangeEl = document.getElementById('cartRange');
    rangeEl.innerHTML = cart.length ? `Range: <strong>${escapeHtml(rangeLabel(cart[0].rangeId))}</strong> (locked for this booking)` : 'Range: <em>set by the first slot you pick</em>';
    if (!cart.length) { ul.innerHTML = '<li class="empty">No dates selected</li>'; }
    else {
        ul.innerHTML = cart.map(i =>
            `<li><span>${i.start.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })}, ` +
            `${i.start.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })} - ${i.end.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })}</span>` +
            `<button class="e-btn e-flat e-small" data-key="${i.key}" title="Remove">✕</button></li>`).join('');
        ul.querySelectorAll('button[data-key]').forEach(b => b.addEventListener('click', () => {
            cart = cart.filter(i => i.key !== b.dataset.key); renderCart(); renderDataSource();
        }));
    }
    const limit = myPolicy ? ` / ${myPolicy.MaxDatesPerTransaction} max` : '';
    document.getElementById('cartBook').textContent = `${isAdmin ? 'Book all' : 'Submit for review'} (${cart.length}${limit})`;
}

function setCartResult(html) { document.getElementById('cartResult').innerHTML = html; }

async function submitCart(dryRun) {
    if (!cart.length) { setCartResult('<div class="msg warn">Turn on multi-date booking and click some slots first.</div>'); return; }
    const added = cart.map(i => toServer({
        Subject: cartUi.subject.value || 'Range session', StartTime: i.start, EndTime: i.end, RangeId: i.rangeId, FacilityId: i.facilityId,
        GroupId: cartUi.group ? cartUi.group.value : null, Headcount: cartUi.headcount.value, FirearmTypes: cartUi.firearms.value || [],
        TargetType: cartUi.targetType.value, TargetDistanceYards: cartUi.targetDistance.value, TargetCount: cartUi.targetCount.value,
        RsoRequested: cartUi.rso.checked
    }));
    const result = await submitBatch({ Added: added, Changed: [], Deleted: [], DryRun: dryRun, AdminOverride: isAdmin && cartUi.override ? cartUi.override.checked : false });
    const warn = result.warnings && result.warnings.length ? '<ul class="tiny">' + result.warnings.map(w => '<li>' + escapeHtml(w) + '</li>').join('') + '</ul>' : '';
    if (result.ok) {
        if (dryRun) {
            setCartResult(`<div class="msg ok">All ${cart.length} date(s) are available and comply with the booking rules.${warn}</div>`);
        } else {
            const pending = result.body.Status === 'Pending';
            setCartResult(`<div class="msg ok">${cart.length} date(s) ${pending ? 'submitted for admin review' : 'booked'} as request <code>${escapeHtml(result.body.RequestId)}</code>. ` +
                (pending ? `Track it in <a href="${appUrl('/Home/MyReservations')}">My reservations</a>.` : '') + `${warn}</div>`);
            cart = []; renderCart(); await loadEvents();
            showToast(pending ? 'Request submitted for review' : 'Booking confirmed', [], 'e-toast-success');
        }
    } else {
        setCartResult('<div class="msg err"><strong>' + (dryRun ? 'Not bookable as selected:' : 'Nothing was submitted (all-or-nothing):') + '</strong><ul>' +
            result.errors.map(e => '<li>' + escapeHtml(e) + '</li>').join('') + '</ul></div>');
    }
}

/* ------------------------------------------------------------------------------------------
   Policy card
   ------------------------------------------------------------------------------------------ */
function renderPolicyCard() {
    const card = document.getElementById('policyCard');
    const capText = recurrenceCap.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
    if (isAdmin) {
        card.innerHTML = `<h3>AZGFD Administrator</h3>
          <ul class="kv">
            <li>Bookings you enter for a group are approved immediately (and the group is emailed)</li>
            <li>Drag, resize and edit any reservation</li>
            <li>Optional tier-policy override - conflicts, blackouts, Monday rule and range permissions always apply</li>
            <li><a href="${appUrl('/Admin/Queue')}">Review queue</a> · <a href="${appUrl('/Admin/Blackouts')}">Blackouts</a> · <a href="${appUrl('/Admin/Groups')}">Permissions</a></li>
          </ul>`;
        return;
    }
    const p = myPolicy;
    const specialized = ctx.Ranges.filter(r => r.IsSpecialized).map(r => r.Name);
    card.innerHTML = `<h3>${escapeHtml(ctx.User.GroupName)}</h3>
      <p class="small"><span class="badge tier-${myTierRank}">${escapeHtml(ctx.User.TierName)}</span> <span class="muted">${escapeHtml(p.Audience)}</span> · ${escapeHtml(ctx.User.GroupRole)} user</p>
      <ul class="kv">
        <li><span>Book ahead</span><strong>up to ${p.AdvanceBookingDays} days</strong></li>
        <li><span>Lead time</span><strong>${p.MinLeadHours} h</strong></li>
        <li><span>Max per booking</span><strong>${p.MaxHoursPerBooking} h</strong></li>
        <li><span>Dates per booking</span><strong>${p.MaxDatesPerTransaction} (one range)</strong></li>
        <li><span>Recurring</span><strong>${p.AllowRecurring ? 'weekly / monthly, until ' + capText : 'not available'}</strong></li>
        <li><span>Mondays</span><strong>${ctx.Settings.MondayExclusiveForTier4 ? (ctx.User.Tier === 'Tier4' ? 'reserved for your tier' : 'Tier 4 only') : 'open'}</strong></li>
        <li><span>Specialized ranges</span><strong>${specialized.length ? escapeHtml(specialized.join(', ')) : 'none granted'}</strong></li>
        <li><span>Change / cancel cutoff</span><strong>${p.CancellationCutoffHours} h</strong></li>
      </ul>
      <p class="muted tiny">All requests are reviewed by AZGFD. You'll get an email when a request is approved or denied.</p>`;
}

/* ------------------------------------------------------------------------------------------ */
toastObj = new ej.notifications.Toast({ position: { X: 'Right', Y: 'Top' }, showCloseButton: true, newestOnTop: true, width: '380px' });
toastObj.appendTo('#toast');
