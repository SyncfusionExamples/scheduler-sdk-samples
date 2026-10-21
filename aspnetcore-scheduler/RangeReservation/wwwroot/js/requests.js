/* Shared rendering for request cards (UC-12): used by the admin review queue and the group dashboard. */
window.Requests = (function () {
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = s => s ? new Date(s).toLocaleString(undefined, { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' }) : '';

    async function api(path, options) {
        const res = await fetch(appUrl(path), Object.assign({ credentials: 'same-origin', headers: { 'Content-Type': 'application/json' } }, options || {}));
        let body = null; try { body = await res.json(); } catch { }
        return { ok: res.ok, status: res.status, body };
    }

    function card(r, mode) {
        const checks = (r.Checks || []);
        const failed = checks.filter(c => !c.Passed);
        const checksHtml = checks.length
            ? `<div class="lbl">Policy compliance ${failed.length ? `<span class="status status-denied">${failed.length} issue(s)</span>` : '<span class="status status-approved">all checks passed</span>'}</div>
               <ul class="checks">${checks.map(c => `<li class="${c.Passed ? 'pass' : 'fail'}">${esc(c.Name)}${c.Messages.map(m => `<span class="detail">${esc(m)}</span>`).join('')}</li>`).join('')}</ul>`
            : '';
        const decision = r.Status === 'Denied'
            ? `<div class="msg err"><strong>Denied</strong> by ${esc(r.ReviewedBy)} on ${fmt(r.ReviewedAt)}<br/>Reason: ${esc(r.DenialReason)}</div>`
            : r.Status === 'Approved' ? `<div class="msg ok">Approved by ${esc(r.ReviewedBy)} on ${fmt(r.ReviewedAt)}</div>` : '';

        let actions = '';
        if (mode === 'admin' && r.Status === 'Pending')
            actions = `<div class="req-actions">
                <button class="e-btn e-success" data-approve="${esc(r.RequestId)}">Approve</button>
                <button class="e-btn e-danger" data-deny="${esc(r.RequestId)}">Deny…</button></div>`;
        if (mode === 'group' && r.CanCancel)
            actions = `<div class="req-actions"><button class="e-btn e-outline e-small" data-cancel="${esc(r.RequestId)}">Cancel request</button></div>`;

        return `<article class="req" data-request="${esc(r.RequestId)}">
          <div class="req-head">
            <span class="status status-${r.Status.toLowerCase()}">${esc(r.Status === 'Pending' ? 'Pending admin review' : r.Status)}</span>
            <h4>${esc(r.Subject)}</h4>
            <code>${esc(r.RequestId)}</code>
            <span class="spacer"></span>
            <span class="muted small">Submitted ${fmt(r.CreatedAt)} by ${esc(r.SubmittedByName)}</span>
          </div>
          <div class="req-grid">
            <div>
              <div class="lbl">Group</div>
              <div><strong>${esc(r.GroupName)}</strong> <span class="badge tier-${esc((r.Tier || '').replace('Tier ', ''))}">${esc(r.Tier)}</span></div>
              <div class="lbl" style="margin-top:8px">Range</div>
              <div>${esc(r.RangeName)} <span class="muted small">· ${esc(r.FacilityName)}</span></div>
              <div class="lbl" style="margin-top:8px">Details</div>
              <div>Headcount ${r.Headcount} · ${esc((r.FirearmTypes || []).join(', '))}</div>
              <div>Targets: ${esc(r.Target)}${r.RsoRequested ? ' · RSO requested' : ''}</div>
            </div>
            <div>
              <div class="lbl">${esc(r.Kind)}</div>
              <ul>${r.Dates.map(d => `<li>${esc(d)}</li>`).join('')}</ul>
              ${r.RecurrenceRule ? `<div class="rule">${esc(r.RecurrenceRule)}</div>` : ''}
            </div>
            <div>${checksHtml}${decision}</div>
          </div>
          ${actions}
          <div class="req-msg result"></div>
        </article>`;
    }

    return { api, card, esc, fmt };
})();
