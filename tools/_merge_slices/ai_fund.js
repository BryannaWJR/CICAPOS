function flushItemPlans(a) {
  if (!a) return;
  try {
    document.querySelectorAll('textarea[data-act="itPlan"]').forEach(function (el) {
      const it = (a.items || []).find(i => i.id === el.dataset.it);
      if (it) it.plan = el.value;
    });
  } catch (e) {}
}
function itemPlansFilled(a) { return !!(a && a.items && a.items.length && a.items.every(it => String(it.plan || '').trim())); }
function palCatalogJson() {
  return JSON.stringify((S.pal || []).filter(function (p) { return p && p.status !== 'Delisted'; }).map(function (p) {
    return { code: p.code, name: p.name, scheme: p.scheme, price: p.price, functions: palFn(p) };
  }));
}
function fundingAiValues(a) {
  flushItemPlans(a);
  var settingNote = '';
  ((a.aiQuality && a.aiQuality.issues) || []).forEach(function (i) {
    if (i && i.code === 'work_setting' && i.message) settingNote = i.message;
  });
  if (!settingNote) settingNote = a.workSettingNote || '';
  const mapped = (a.items || []).map(it => ({ plan: it.plan || '', product: it.product || '', type: it.type || (it.paCode ? 'PA' : 'NPA'), paCode: it.paCode || '', qty: it.qty, unit: it.unit, functions: it.functions || (it.form1 && it.form1.f) || '' }));
  const npa = mapped.some(it => it.type === 'NPA' || (!it.paCode && it.type !== 'PA'));
  const itemsPlain = mapped.map(function (it, i) {
    const kind = (it.type === 'NPA' || !it.paCode) ? 'non-pre-approved' : ('pre-approved ' + it.paCode);
    return (i + 1) + '. ' + (it.product || 'item') + ' (' + kind + (it.qty ? ', qty ' + it.qty : '') + ')' + (kind === 'non-pre-approved' && it.functions ? '; functions: ' + it.functions : '') + (it.plan ? '; plan: ' + it.plan : '');
  }).join('\n') || 'No items listed';
  return { lang: 'EN', objective: a.objective || '', project: a.project || '', schemes: (a.schemes || []).join(','), otherFund: a.otherFund || '', mpdDate: a.mpdDate || '', mpdLead: String(typeof rule === 'function' ? rule('mpdLead') : 0), settingNote: settingNote, itemsPlain: itemsPlain, hasNpa: npa ? 'true' : 'false', itemsJson: JSON.stringify(mapped), palJson: npa ? palCatalogJson() : '[]' };
}
function syncFundingAiQuality(a) {
  if (!a) return [];
  if (typeof CitfAi === 'undefined') return [];
    const issues = typeof locIssues === 'function' ? locIssues(CitfAi.validateFundingLocal(fundingAiValues(a))) : CitfAi.validateFundingLocal(fundingAiValues(a));
    a.aiQuality = { pass: !issues.some(i => i.severity === 'error'), issues, source: 'rules', on: now().toISOString() };
  return issues;
}
function refreshFundingAiQuality(a) {
  if (!a || typeof CitfAi === 'undefined') return Promise.resolve(null);
  var key = fundingSummaryKey(a);
  return CitfAi.assist({ action: 'validate_form', schemaKey: 'funding_application', currentValues: fundingAiValues(a) }).then(res => {
    if (fundingSummaryKey(a) !== key) return null;
    let issues = res.issues || [];
    if (typeof CitfAi !== 'undefined' && CitfAi.mergeFundingIssues) issues = CitfAi.mergeFundingIssues(issues, fundingAiValues(a));
    if (typeof locIssues === 'function') issues = locIssues(issues);
    if (itemPlansFilled(a)) issues = issues.filter(i => i && i.code !== 'plan_missing' && !/implementation plan is missing/i.test(i.message || ''));
    const pass = !issues.some(i => i && i.severity !== 'warn');
    a.aiQuality = { pass: pass, issues: issues, source: res.source, model: res.model || null, on: now().toISOString(), hash: res.contentHash, key: key };
    try { if (a.ai) a.ai.notes = fundingAiWarnings(a); } catch (eNotes) {}
    var ws = issues.find(function (i) { return i && i.code === 'work_setting' && i.message; });
    if (ws) a.workSettingNote = ws.message;
    applyPlanAiIssues(a, issues);
    save(); return Object.assign({}, res, { pass: pass, issues: issues });
  });
}
function applyPlanAiIssues(a, issues) {
  (a.items || []).forEach(function (it, i) {
    const n = i + 1;
    const hit = (issues || []).find(function (x) {
      if (!x) return false;
      const code = String(x.code || '');
      if (code !== 'plan_missing' && code !== 'plan_too_brief' && code !== 'plan_no_quantity' && code !== 'plan_weak') return false;
      const f = String(x.field || '');
      const m = String(x.message || '');
      return f === 'item:' + n + '.plan' || new RegExp('item\\s*' + n, 'i').test(f + ' ' + m);
    });
    if (hit) it.planAi = { ok: true, checking: false, note: hit.message };
    else if (String(it.plan || '').trim()) it.planAi = { ok: true, checking: false, note: 'AI checked' };
    else it.planAi = null;
  });
}
function captureApplyForm(a) {
  if (!a) return;
  try {
    const form = document.querySelector('form[data-form]');
    if (form && form.querySelector('[name=objective], [name=schemes], [name=project]')) {
      const d = formData(form);
      a.schemes = Array.isArray(d.schemes) ? d.schemes.filter(Boolean) : (d.schemes ? [d.schemes] : a.schemes);
      if (d.project !== undefined) a.project = d.project;
      if (d.objective !== undefined) a.objective = d.objective || '';
      if (d.otherFund !== undefined) a.otherFund = d.otherFund;
      if (d.mpdDate !== undefined) a.mpdDate = d.mpdDate;
    }
    flushItemPlans(a);
  } catch (e) {}
}
function paintAiSoftStatus() {
  const obj = document.getElementById('aiObjStatus');
  if (obj) {
    const st = UI.aiSoft && UI.aiSoft.objective;
    if (st === 'checking') obj.innerHTML = '<span class="ai-spin"></span> Checking…';
    else if (st === 'done') obj.innerHTML = '✓ AI checked';
    else obj.innerHTML = 'AI checks when this field loses focus.';
  }
  document.querySelectorAll('[data-plan-ai]').forEach(function (el) {
    const id = el.getAttribute('data-plan-ai');
    const a = (function () { try { const m = String(location.hash || '').match(/#\/p\/apply\/([^/]+)/); return m ? app(m[1]) : null; } catch (e) { return null; } })();
    const it = a && (a.items || []).find(function (x) { return x.id === id; });
    if (it && it.planAi && it.planAi.checking) el.innerHTML = '<span class="ai-spin"></span> Checking…';
    else if (it && it.planAi && it.planAi.ok) el.innerHTML = '✓ AI checked';
    else el.innerHTML = 'AI checks when this field loses focus.';
  });
}
function scheduleFundingAiCheck(a, kind) {
  if (!a || typeof refreshFundingAiQuality !== 'function') return;
  captureApplyForm(a);
  UI.aiSoft = UI.aiSoft || {};
  UI.aiSoft[kind || 'all'] = 'checking';
  if (kind === 'plan') (a.items || []).forEach(function (it) {
    if (String(it.plan || '').trim()) it.planAi = Object.assign({}, it.planAi || {}, { checking: true, ok: true, note: 'Checking…' });
  });
  paintAiSoftStatus();
  const tok = (a._aiTok = (a._aiTok || 0) + 1);
  refreshFundingAiQuality(a).then(function () {
    if (tok !== a._aiTok) return;
    UI.aiSoft[kind || 'all'] = 'done';
    paintAiSoftStatus();
    save();
  }).catch(function () {
    if (tok !== a._aiTok) return;
    UI.aiSoft[kind || 'all'] = 'done';
    paintAiSoftStatus();
  });
}
function fundingSummaryKey(a) {
  try { if (typeof flushItemPlans === 'function') flushItemPlans(a); } catch (e0) {}
  const amt = typeof appAmt === 'function' ? appAmt(a) : '';
  return 'v10:' + [a && a.id, a && a.project, a && a.objective, ((a && a.schemes) || []).join(','), amt,
    ((a && a.items) || []).map(function (it) {
      return [it.product, it.plan, it.paCode || '', it.type || '', it.qty, it.unit,
        it.functions || (it.form1 && it.form1.f) || ''].join('\t');
    }).join('|')].join('::');
}
function scheduleFundingSummary(a) {
  if (!a || typeof CitfAi === 'undefined' || !CitfAi.assist) return false;
  const key = fundingSummaryKey(a);
  if (!(a.ai && a.ai.summary && a.ai.contentKey === key) && a.ai) a.ai.notes = [];
  if (a.ai && a.ai.summary && a.ai.contentKey === key) return false;
  if (UI._fundSumTok === key) return true;
  UI._fundSumTok = key;
  a._aiSumPending = key;
  CitfAi.assist({
    action: 'summarize',
    schemaKey: 'funding_application',
    currentValues: Object.assign({}, fundingAiValues(a), {
      lang: 'EN',
      amount: String(typeof appAmt === 'function' ? appAmt(a) : ''),
      grant: String(typeof grantOf === 'function' ? grantOf(a) : '')
    })
  }).then(function (sum) {
    if (a._aiSumPending !== key) return;
    const f = (sum && sum.fields) || {};
    let risks = [];
    try { risks = JSON.parse(f.risksJson || '[]'); } catch (e2) { risks = []; }
    a.ai = Object.assign(a.ai || {}, {
      summary: typeof sanitizeAiSummary === 'function' ? sanitizeAiSummary(f.summary || (sum && sum.notes) || '') : (f.summary || (sum && sum.notes) || ''),
      score: +(f.score || 80),
      risks: typeof normalizeAiRisks === 'function' ? normalizeAiRisks(risks) : risks,
      source: (sum && sum.source) || '',
      model: (sum && sum.model) || '',
      contentKey: key
    });
    try { a.ai.notes = fundingAiWarnings(a); } catch (eNotes) { a.ai.notes = a.ai.notes || []; }
    save();
    try { persistFundingSummary(a); } catch (e3) {}
    render();
  }).catch(function () {
    if (a._aiSumPending === key) UI._fundSumTok = '';
  });
  return true;
}
function isPlaceholderAiSummary(text) {
  return /All required documents read; amounts match quotations/i.test(String(text || ''));
}
function fundingSummaryRisks(a) {
  var npa = ((a && a.items) || []).some(function (it) { return it.type === 'NPA' || (!it.paCode && it.type !== 'PA'); });
  return ((a && a.ai && a.ai.risks) || []).filter(function (r) {
    var sev = r && (r.sev || r.severity);
    if (sev === 'tip' && !npa) return false;
    if (sev === 'tip') return true;
    return isFundingAiWarn((r && (r.text || r.message)) || '', '');
  });
}
function applyFundingAiResult(a, sum, key) {
  var f = (sum && sum.fields) || {};
  var risks = [];
  try { risks = JSON.parse(f.risksJson || '[]'); } catch (e2) { risks = []; }
  a.ai = Object.assign(a.ai || {}, {
    summary: typeof sanitizeAiSummary === 'function' ? sanitizeAiSummary(f.summary || (sum && sum.notes) || '') : (f.summary || (sum && sum.notes) || ''),
    score: +(f.score || 80),
    risks: typeof normalizeAiRisks === 'function' ? normalizeAiRisks(risks) : risks,
    source: (sum && sum.source) || '',
    model: (sum && sum.model) || '',
    contentKey: key
  });
  try { a.ai.notes = fundingAiWarnings(a); } catch (eNotes) { a.ai.notes = []; }
  return a.ai;
}
function ensureFundingSummary(a) {
  var key = fundingSummaryKey(a);
  var current = a && a.ai && a.ai.summary;
  if (current && a.ai.contentKey === key && !isPlaceholderAiSummary(current)) {
    try { if (!a.ai.notes) a.ai.notes = fundingAiWarnings(a); } catch (e0) {}
    return Promise.resolve(a.ai);
  }
  if (typeof CitfAi === 'undefined' || !CitfAi.assist) return Promise.reject(new Error('the AI summary on this page is not ready'));
  return CitfAi.assist({
    action: 'summarize',
    schemaKey: 'funding_application',
    currentValues: Object.assign({}, fundingAiValues(a), {
      lang: 'EN',
      amount: String(typeof appAmt === 'function' ? appAmt(a) : ''),
      grant: String(typeof grantOf === 'function' ? grantOf(a) : '')
    })
  }).then(function (sum) { return applyFundingAiResult(a, sum, key); });
}
function isFundingAiWarn(msg, code) {
  if (!msg) return false;
  if (/close to listed|close listed product|~ PA-|pre-approved list/i.test(msg)
      && !/unspecified|criteria|too brief|does not cover/i.test(msg)) return false;
  if (/does not meet indoor|does not meet outdoor|不符合室內需要|不符合室外需要/i.test(msg)
      && !/unspecified/i.test(msg)) return false;
  if (code === 'work_setting') return /unspecified|does not say whether/i.test(msg);
  if (code === 'scheme_mismatch' || code === 'weak_content' || code === 'too_brief'
      || code === 'plan_too_brief' || code === 'plan_weak' || code === 'plan_no_quantity') return true;
  return /unspecified|does not say whether|does not cover|criteria|too brief|too short/i.test(msg);
}
function fundingAiFresh(a) {
  return !!(a && a.aiQuality && a.aiQuality.key && a.aiQuality.key === fundingSummaryKey(a));
}
function scheduleFundingNotes(a) {
  if (!a || typeof refreshFundingAiQuality !== 'function') return false;
  var key = fundingSummaryKey(a);
  if (a.aiQuality && a.aiQuality.key === key) return false;
  if (a._aiNotesTok === key) return true;
  a._aiNotesTok = key;
  refreshFundingAiQuality(a).then(function () {
    if (a._aiNotesTok !== key) return;
    save();
    if (UI.dvSubmitting) return;
    render();
  }).catch(function () {
    if (a._aiNotesTok !== key) return;
    a.aiQuality = { pass: true, issues: [], source: 'rules', key: key, on: now().toISOString() };
    render();
  });
  return true;
}
function docSlotMismatchText(text) {
  var s = String(text || '');
  if (!s) return false;
  if (/claimed slot is incorrect|does not match the (claimed |document )?slot|wrong document type/i.test(s)) return true;
  if (/\bnot an?\b/i.test(s) && /catalogue|user manual|quotation|shareholder|annual return|NAR1|business registration/i.test(s)) return true;
  if (/不是|並非|并非/.test(s) && /目錄|目录|手冊|手册|報價|报价|股東|股东|周年|商業登記|商业登记/.test(s)) return true;
  return false;
}
function docAiState(d) {
  if (!d) return '';
  if (d.ai === 'flag' || d.ai === 'busy') return d.ai;
  if (d.ai === 'warn' || docSlotMismatchText(d.note)) return 'warn';
  return d.ai || '';
}
function docSlotWarnings(a) {
  return ((a && a.docs) || []).filter(function (d) { return docAiState(d) === 'warn'; }).map(function (d) {
    return (d.type || 'Document') + ': ' + (d.note || 'This file does not match the document slot.');
  });
}
function fundingAiWarnings(a) {
  if (!fundingAiFresh(a)) return [];
  const out = [];
  ((a && a.aiQuality && a.aiQuality.issues) || []).forEach(function (i) {
    if (!i || !isFundingAiWarn(i.message, i.code)) return;
    if (out.indexOf(i.message) < 0) out.push(i.message);
  });
  ((a && a.ai && a.ai.risks) || []).forEach(function (r) {
    if (r && (r.sev === 'tip' || r.severity === 'tip')) return;
    const t = (r && (r.text || r.message)) || '';
    if (!isFundingAiWarn(t, '')) return;
    if (out.indexOf(t) < 0) out.push(t);
  });
  return out;
}
function dataBlockers(a) { const b = [];
  if (!a.schemes.length) b.push('Choose at least one scheme'); if (!a.project) b.push('Choose the project / site');
  if (!a.items.length) b.push('Add at least one item');
  if (!(a.objective || '').trim()) b.push('Project objective and expected benefits are missing');
  if (a.schemes.includes('MPD') && (!a.mpdDate || (new Date(a.mpdDate) - now()) / 864e5 < rule('mpdLead'))) b.push(`MPD: the activity must start at least ${rule('mpdLead')} days after submission`);
  a.items.forEach((it, i) => {
    if (it.type === 'NPA' && !String(it.functions || (it.form1 && it.form1.f) || '').trim()) b.push(`Item ${i + 1}: core functions are required`);
    if (it.aiCheck === 'flag') b.push(`Item ${i + 1}: ${it.aiNote}`);
    if (it.lowest === false && !it.just) b.push(`Item ${i + 1}: lowest bid not selected – give a justification`);
  });
  const g = ceiling(a.companyId), mine = preSubmit(a) ? grantOf(a) : 0; if (mine > g.left) b.push(`Grant ${money(mine)} exceeds the remaining ACT + BIM cap (${money(g.left)})`);
  const s4 = sum(a.items.filter(i => i.s4), itemGrant); if (preSubmit(a) && g.s4 + s4 > g.s4cap) b.push(`4S products exceed the ${money(g.s4cap)} reserved amount`);
  reqDocs(a).forEach(([t, id]) => { const d = docFor(a, t, id); if (!d) b.push('Missing document: ' + t + (id ? ' (item ' + (a.items.findIndex(i => i.id === id) + 1) + ')' : '')); else if (d.ai === 'flag') b.push(t + ': ' + d.note); else if (d.ai === 'busy') b.push(t + ': AI is still reading this file'); });
  return b; }

