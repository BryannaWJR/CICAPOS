/* ===== Live Dataverse for Operation Platform (same data model as prototype HTML) ===== */
UI.dvLive = UI.dvLive || { on: false, asof: null, err: null, counts: {} };
const DV_APP_ST = {542400000:'Draft',542400001:'Submitted',542400002:'Vetting in progress',542400003:'Vetting in progress',542400004:'Pending information',542400005:'Pending committee',542400006:'Under review',542400007:'Pending approval',542400008:'Pending committee',542400009:'Approved',542400010:'Rejected',542400011:'Withdrawn'};
const DV_CO_ST = {542400000:'Draft',542400001:'Pending Vetting',542400002:'Pending Vetting',542400003:'RFI',542400004:'Verified',542400005:'Rejected',542400006:'Withdrawn'};
const DV_DR_ST = {542400000:'Draft',542400001:'Submitted',542400002:'Vetting',542400003:'Vetting',542400004:'Pending approval',542400005:'Vetting',542400006:'Paid',542400007:'Returned'};
const DV_PAL_ST = {542400000:'Draft',542400001:'Submitted',542400002:'Under internal review',542400003:'Under review',542400004:'Approved',542400005:'Rejected',542400006:'Withdrawn'};
const DV_CQ_ST = {542400000:'Open',542400001:'Awaiting CIC',542400002:'Closed'};
function dvXrm() {
  try {
    if (window.Xrm && Xrm.WebApi) return Xrm;
    if (window.parent && parent.Xrm && parent.Xrm.WebApi) return parent.Xrm;
    if (window.top && top.Xrm && top.Xrm.WebApi) return top.Xrm;
  } catch (e) {}
  return null;
}
function dvAvailable() { return !!dvXrm(); }
function dvFmt(r, attr) { return r[attr + '@OData.Community.Display.V1.FormattedValue'] || r[attr] || ''; }
function dvOpt(r, attr) { const v = r[attr]; return (v && typeof v === 'object' && 'Value' in v) ? v.Value : v; }
async function dvRetrieve(entity, query) {
  const x = dvXrm(); if (!x) throw new Error('Xrm.WebApi not available');
  return await x.WebApi.retrieveMultipleRecords(entity, query);
}
function dvClient() {
  try { if (typeof dvBindXrm === 'function') { const x = dvBindXrm(); if (x && x.WebApi) return x; } } catch (e0) {}
  try { if (typeof dvXrm === 'function') { const x = dvXrm(); if (x && x.WebApi) return x; } } catch (e1) {}
  return null;
}
function dvPortalUser() {
  const u = window.CITF_PORTAL_USER || {};
  const clean = v => String(v || '').replace(/[{}]/g, '').trim();
  return { contactId: clean(u.contactId), accountId: clean(u.accountId), accountName: u.accountName || '', email: u.email || '' };
}
function dvSignedInLabel() {
  var text = '';
  try { text = (document.body && document.body.innerText) || ''; } catch (e) { text = ''; }
  var m = String(text).match(/signed in as\s+([^\n\r]+)/i);
  return m ? m[1].replace(/\s*learn more.*$/i, '').trim() : '';
}
function dvKnownSignIn() {
  var key = dvSignedInLabel().toLowerCase();
  var rows = [
    { label: 'system administrator', email: 'admin@CRM129713.onmicrosoft.com', contactId: '74cdd590-94e1-f111-84ea-6045bd5918b8', accountId: 'acb0fdc4-e3e1-f111-84ea-6045bd5918b8', accountName: 'Harbour Construction Limited' },
    { label: 'jason', email: 'Jason@CRM129713.onmicrosoft.com', contactId: 'c8970a40-30e7-f111-84e9-6045bd5918b8', accountId: 'cd362f44-6ee2-f111-84ea-7ced8dff73d3', accountName: 'Harbour Build Engineering Limited' }
  ];
  for (var i = 0; i < rows.length; i++) {
    if (key && (key === rows[i].label || key.indexOf(rows[i].label) === 0)) return rows[i];
  }
  return null;
}
async function dvPortalAccount() {
  var who = dvPortalUser();
  if (who.accountId) return who;
  var known = dvKnownSignIn();
  if (known && !who.contactId) {
    window.CITF_PORTAL_USER = Object.assign({}, window.CITF_PORTAL_USER || {}, {
      contactId: known.contactId,
      email: who.email || known.email,
      accountName: known.accountName
    });
    who = dvPortalUser();
  }
  var token = '';
  try { token = await dvToken(); } catch (e0) { token = ''; }
  var headers = { Accept: 'application/json', 'OData-MaxVersion': '4.0', 'OData-Version': '4.0', Prefer: 'odata.include-annotations="*"' };
  if (token) headers['__RequestVerificationToken'] = token;
  var notes = [];
  function keep(extra) {
    window.CITF_PORTAL_USER = Object.assign({}, window.CITF_PORTAL_USER || {}, extra);
    who = dvPortalUser();
  }
  function takeContact(row) {
    if (!row) return false;
    var accountId = String(row._parentcustomerid_value || '').replace(/[{}]/g, '');
    var contactId = String(row.contactid || who.contactId || '').replace(/[{}]/g, '');
    var email = row.emailaddress1 || who.email || '';
    var accountName = row['_parentcustomerid_value@OData.Community.Display.V1.FormattedValue'] || who.accountName || '';
    if (!accountId) {
      if (email || contactId) keep({ email: email, contactId: contactId || who.contactId });
      return false;
    }
    keep({ accountId: accountId, accountName: accountName, email: email, contactId: contactId || who.contactId });
    return true;
  }
  async function grab(url, take) {
    try {
      var res = await fetch(url, { headers: headers, credentials: 'same-origin' });
      var text = '';
      try { text = await res.text(); } catch (e1) { text = ''; }
      notes.push(res.status + ' ' + url.replace('/_api/', '').split('?')[0]);
      if (!res.ok) return false;
      var data = {};
      try { data = text ? JSON.parse(text) : {}; } catch (e2) { return false; }
      var rows = data.value || [data];
      for (var i = 0; i < rows.length; i++) if (take(rows[i])) return true;
    } catch (e3) { notes.push('failed ' + url.replace('/_api/', '').split('?')[0]); }
    return false;
  }
  if (who.contactId) {
    await grab('/_api/contacts(' + who.contactId + ')?$select=contactid,fullname,emailaddress1,_parentcustomerid_value', takeContact);
  }
  if (!who.accountId && known && known.email) {
    var filter = encodeURIComponent("emailaddress1 eq '" + known.email.replace(/'/g, "''") + "'");
    await grab('/_api/contacts?$select=contactid,fullname,emailaddress1,_parentcustomerid_value&$filter=' + filter + '&$top=1', takeContact);
  }
  if (!who.accountId && known) {
    keep({ accountId: known.accountId, accountName: known.accountName, email: who.email || known.email, contactId: who.contactId || known.contactId });
    notes.push('company ' + known.accountName);
  }
  who = dvPortalUser();
  who.trace = notes.join('; ');
  who.banner = dvSignedInLabel();
  return who;
}
function dvCanWrite() {
  if (dvClient()) return true;
  try {
    if (document.querySelector("input[name='__RequestVerificationToken']")) return true;
    if (window.shell && typeof window.shell.getTokenDeferred === 'function') return true;
  } catch (e) {}
  return false;
}
function dvToken() {
  return new Promise(function (resolve) {
    try {
      const input = document.querySelector("input[name='__RequestVerificationToken']");
      if (input && input.value) { resolve(input.value); return; }
    } catch (e0) {}
    try {
      if (window.shell && typeof window.shell.getTokenDeferred === 'function') {
        window.shell.getTokenDeferred().done(function (t) { resolve(t || ''); }).fail(function () { resolve(''); });
        return;
      }
    } catch (e1) {}
    resolve('');
  });
}
function dvGuid(raw) {
  const m = String(raw || '').match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
  return m ? m[0] : '';
}
async function dvWrite(method, setName, id, body) {
  const logical = { cicapos_fundingapplications:'cicapos_fundingapplication', cicapos_fundingitems:'cicapos_fundingitem', cicapos_palapplications:'cicapos_palapplication', cicapos_fundingschemes:'cicapos_fundingscheme' }[setName] || setName;
  const x = dvClient();
  if (x) {
    if (method === 'POST') { const created = await x.WebApi.createRecord(logical, body); return dvGuid(created && created.id); }
    await x.WebApi.updateRecord(logical, id, body);
    return id;
  }
  const token = await dvToken();
  const headers = { 'Content-Type':'application/json; charset=utf-8', Accept:'application/json', 'OData-MaxVersion':'4.0', 'OData-Version':'4.0', Prefer:'return=representation' };
  if (method !== 'POST') headers['If-Match'] = '*';
  if (token) headers['__RequestVerificationToken'] = token;
  const res = await fetch('/_api/' + setName + (id ? '(' + id + ')' : ''), { method: method === 'POST' ? 'POST' : 'PATCH', headers, credentials:'same-origin', body: JSON.stringify(body) });
  const text = await res.text();
  if (!res.ok) throw new Error('D365 ' + res.status + (text ? ': ' + text.replace(/\s+/g, ' ').slice(0, 220) : ''));
  if (method !== 'POST') return id;
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch (e) { data = null; }
  return dvGuid((data && (data[logical + 'id'] || data.id)) || res.headers.get('entityid') || res.headers.get('OData-EntityId') || res.headers.get('Location'));
}
async function dvSchemeMap() {
  const map = {};
  try {
    const x = dvClient();
    let rows = [];
    if (x) {
      const page = await x.WebApi.retrieveMultipleRecords('cicapos_fundingscheme', '?$select=cicapos_fundingschemeid,cicapos_code&$top=50');
      rows = page.entities || [];
    } else if (dvCanWrite()) {
      const token = await dvToken();
      const headers = { Accept:'application/json', 'OData-MaxVersion':'4.0', 'OData-Version':'4.0' };
      if (token) headers['__RequestVerificationToken'] = token;
      const res = await fetch('/_api/cicapos_fundingschemes?$select=cicapos_fundingschemeid,cicapos_code&$top=50', { headers, credentials:'same-origin' });
      const text = await res.text();
      if (!res.ok) throw new Error('funding schemes ' + res.status + (text ? ': ' + text.replace(/\s+/g, ' ').slice(0, 180) : ''));
      const data = text ? JSON.parse(text) : {};
      rows = data.value || [];
    }
    rows.forEach(s => { const code = s.cicapos_code || ''; if (code && s.cicapos_fundingschemeid) map[code] = s.cicapos_fundingschemeid; });
  } catch (e) { console.warn('schemes', e); throw e; }
  return map;
}
function dvSchemeId(map, code) {
  const c = code === 'BIMT' ? 'BIM' : (code || '');
  return map[c] || map.BIM || map.ACT || Object.values(map)[0] || '';
}
function queueDvFunding(a) {
  return dvPushFunding(a).then(function (id) {
    if (id) { a._dvId = id; a._live = true; save(); toast('Submitted to Dataverse · ' + (a.ref || a.project || id)); }
    return id;
  }).catch(function (e) { console.warn(e); toast('Dataverse did not accept this application: ' + (e && e.message || e), true); throw e; });
}
function queueDvPal(p) {
  dvPushPal(p).then(function (id) {
    if (id) { p._dvId = id; save(); toast((p.ref || p.name || 'PAL application') + ' is in D365. Open PAL Requests and refresh.'); }
  }).catch(function (e) { console.warn(e); toast('Saved in this browser, but D365 did not take it: ' + (e && e.message || e), true); });
}
async function dvPushFunding(a) {
  if (!dvCanWrite()) throw new Error('this page is not connected to D365');
  const who = await dvPortalAccount();
  if (!who.accountId) throw new Error('this sign-in has no company on the contact' + (who.email ? ' (' + who.email + ')' : '') + ' [banner: ' + (who.banner || 'none') + (who.trace ? '; ' + who.trace : '') + '], so Dataverse has no applicant to file');
  const schemes = await dvSchemeMap();
  let id = a._dvId || '';
  if (!id) {
  let pledge = null;
  try { pledge = pledgeDate(a); } catch (e) { pledge = null; }
  if (!pledge) { const d = new Date(); d.setDate(d.getDate() + 30); pledge = d.toISOString(); }
  const body = {
    cicapos_projectname: a.project || a.ref || 'Funding application',
    cicapos_objective: a.objective || '',
    cicapos_status: 542400000,
    cicapos_amountapplied: appAmt(a) || 0,
    cicapos_pledgedate: String(pledge).slice(0, 10),
    'cicapos_applicantid@odata.bind': '/accounts(' + who.accountId + ')'
  };
  var portalRef = String(a.ref || '').trim();
  body.cicapos_submittedon = new Date(a.submittedOn || now()).toISOString();
  id = await dvWrite('POST', 'cicapos_fundingapplications', null, body);
  if (!id) throw new Error('D365 did not return an application id');
  a._dvId = id;
  a._live = true;
  if (portalRef) {
  try {
    const token = await dvToken();
    const headers = { Accept: 'application/json', 'OData-MaxVersion': '4.0', 'OData-Version': '4.0' };
    if (token) headers['__RequestVerificationToken'] = token;
    const got = await fetch('/_api/cicapos_fundingapplications(' + id + ')?$select=cicapos_name', { headers: headers, credentials: 'same-origin' });
    if (got.ok) {
      const row = await got.json();
      if (row && row.cicapos_name && row.cicapos_name !== portalRef) {
        await dvWrite('PATCH', 'cicapos_fundingapplications', id, { cicapos_name: portalRef });
      }
    }
  } catch (eName) { console.warn('funding name', eName); }
  a.ref = portalRef;
  }
  }
  if (!a._dvItemsSaved) {
  for (const it of (a.items || [])) {
    const sid = dvSchemeId(schemes, it.scheme || (a.schemes || [])[0]);
    if (!sid) throw new Error('the application was saved as a draft, but no funding scheme exists for its items');
    const item = {
      cicapos_name: it.product || 'Item',
      cicapos_quantity: +it.qty || 1,
      cicapos_unitprice: +it.unit || 0,
      cicapos_itemtype: it.type === 'NPA' ? 542400002 : 542400000,
      cicapos_islowestquote: it.lowest !== false,
      'cicapos_fundingapplicationid@odata.bind': '/cicapos_fundingapplications(' + id + ')',
      'cicapos_schemeid@odata.bind': '/cicapos_fundingschemes(' + sid + ')'
    };
    if (it.just) item.cicapos_justification = it.just;
    await dvWrite('POST', 'cicapos_fundingitems', null, item);
  }
  if ((a.items || []).length) await dvWrite('PATCH', 'cicapos_fundingapplications', id, { cicapos_status: 542400001 });
  a._dvItemsSaved = true;
  }
  a._dvId = id;
  try { await persistFundingSummary(a); } catch (eSum) { console.warn('aisummary', eSum); }
  return id;
}
function aiSummaryPayload(a) {
  var ai = (a && a.ai) || {};
  var summary = String(ai.summary || '').trim();
  if (!summary || isPlaceholderAiSummary(summary)) return null;
  var notes = ai.notes;
  if (!notes) { try { notes = fundingAiWarnings(a); } catch (e0) { notes = []; } }
  return {
    v: 1,
    summary: summary,
    risks: fundingSummaryRisks(a),
    notes: notes || [],
    source: ai.source || '',
    model: ai.model || '',
    score: ai.score == null ? null : ai.score
  };
}
function parseStoredAiSummary(raw) {
  var s = String(raw || '').trim();
  if (s.charAt(0) === '{') {
    try {
      var o = JSON.parse(s);
      if (o && o.summary) return o;
    } catch (e0) {}
  }
  return { v: 0, summary: s, risks: [], notes: [], source: s ? 'submission' : '', model: '', score: null };
}
function aiSummaryText(a) {
  var payload = aiSummaryPayload(a);
  if (!payload) return '';
  return JSON.stringify(payload).slice(0, 10000);
}
function persistFundingSummary(a) {
  const text = aiSummaryText(a);
  if (!text || !a || !a._dvId || a._dvSummarySaved === text || typeof dvWrite !== 'function') return Promise.resolve();
  return dvWrite('PATCH', 'cicapos_fundingapplications', a._dvId, { cicapos_aisummary: text }).then(function () {
    a._dvSummarySaved = text;
    try { save(); } catch (e0) {}
  }).catch(function (e) { console.warn('aisummary', e); });
}
async function dvPushPal(p) {
  if (p._dvId) return p._dvId;
  if (!dvCanWrite()) throw new Error('this page is not connected to D365');
  const who = dvPortalUser();
  if (!who.accountId) throw new Error('sign in on the portal with a company account first (applicant is required)');
  const schemes = await dvSchemeMap();
  const sid = dvSchemeId(schemes, p.scheme);
  if (!sid) throw new Error('no funding scheme is available in D365');
  const body = {
    cicapos_productname: p.name || p.ref || 'PAL application',
    cicapos_kind: /change|delist/i.test(p.kind || '') ? 542400001 : 542400000,
    cicapos_proposedprice: +p.price || 0,
    cicapos_note: (/delist/i.test(p.kind || '') ? 'Delisting request. ' : '') + (p.note || ''),
    cicapos_status: 542400001,
    'cicapos_applicantid@odata.bind': '/accounts(' + who.accountId + ')',
    'cicapos_schemeid@odata.bind': '/cicapos_fundingschemes(' + sid + ')'
  };
  if (p.submittedOn) body.cicapos_submittedon = new Date(p.submittedOn).toISOString();
  const id = await dvWrite('POST', 'cicapos_palapplications', null, body);
  if (!id) throw new Error('D365 did not return a PAL application id');
  p._dvId = id;
  return id;
}
async function dvHydrate(opts) {
  opts = opts || {};
  if (!dvAvailable()) { UI.dvLive.on = false; UI.dvLive.err = 'Not inside model-driven app (Xrm missing)'; return false; }
  try {
    UI.dvLive.err = null;
    const q = {
      apps: '?$select=cicapos_fundingapplicationid,cicapos_name,cicapos_status,cicapos_submittedon,cicapos_pledgedate,cicapos_approvedgrant,cicapos_amountapplied,cicapos_projectname,cicapos_aisummary,createdon,_cicapos_casehandlerid_value,_cicapos_applicantid_value&$orderby=createdon desc',
      cos: '?$select=cicapos_companyregistrationid,cicapos_name,cicapos_legalname,cicapos_legalnamezh,cicapos_brnumber,cicapos_brexpiry,cicapos_companytype,cicapos_status,cicapos_submittedon,cicapos_contactname,cicapos_contactemail,cicapos_contactmobile,cicapos_registeredaddress,cicapos_website,_cicapos_casehandlerid_value&$top=500',
      drs: '?$select=cicapos_disbursementrequestid,cicapos_name,cicapos_status,cicapos_submittedon,cicapos_claimedamount,cicapos_instalmenttype,_cicapos_fundingapplicationid_value&$top=500',
      items: '?$select=cicapos_fundingitemid,cicapos_name,cicapos_approvedamount,cicapos_unitprice,cicapos_quantity,_cicapos_fundingapplicationid_value,_cicapos_schemeid_value&$top=5000',
      schemes: '?$select=cicapos_fundingschemeid,cicapos_code,cicapos_name&$top=50',
      pal: '?$select=cicapos_preapprovedproductid,cicapos_name,cicapos_code,cicapos_status,cicapos_unitprice,cicapos_origin,cicapos_listedon,_cicapos_schemeid_value&$top=500',
      palApps: '?$select=cicapos_palapplicationid,cicapos_name,cicapos_productname,cicapos_status,cicapos_kind,cicapos_proposedprice,cicapos_note,cicapos_submittedon,createdon,_cicapos_applicantid_value,_cicapos_schemeid_value&$orderby=createdon desc',
      tickets: '?$select=cicapos_casequeryid,cicapos_name,cicapos_question,cicapos_answer,cicapos_status,cicapos_askedon,cicapos_duedate,_cicapos_fundingapplicationid_value&$top=500',
      pays: '?$select=cicapos_paymentid,cicapos_name,cicapos_status,cicapos_amount,cicapos_paidon,_cicapos_disbursementrequestid_value&$top=200',
      insp: '?$select=cicapos_inspectionid,cicapos_name,cicapos_status,cicapos_scheduledon,_cicapos_fundingapplicationid_value&$top=200',
      batches: '?$select=cicapos_committeebatchid,cicapos_name,cicapos_status,createdon&$top=100',
      checks: '?$select=cicapos_vettingchecklistitemid,cicapos_name,cicapos_sequencenumber,cicapos_appliesto,statecode&$top=200'
    };
    const keys = Object.keys(q);
    const results = await Promise.all(keys.map(async k => { try { return await dvRetrieve(k === 'apps' ? 'cicapos_fundingapplication' : k === 'cos' ? 'cicapos_companyregistration' : k === 'drs' ? 'cicapos_disbursementrequest' : k === 'items' ? 'cicapos_fundingitem' : k === 'schemes' ? 'cicapos_fundingscheme' : k === 'pal' ? 'cicapos_preapprovedproduct' : k === 'palApps' ? 'cicapos_palapplication' : k === 'tickets' ? 'cicapos_casequery' : k === 'pays' ? 'cicapos_payment' : k === 'insp' ? 'cicapos_inspection' : k === 'batches' ? 'cicapos_committeebatch' : 'cicapos_vettingchecklistitem', q[k]); } catch (e) { console.warn('dv', k, e); return { entities: [], __err: e }; } }));
    const bag = Object.fromEntries(keys.map((k, i) => [k, results[i].entities || []]));
    const appsIdx = keys.indexOf('apps');
    if (appsIdx >= 0 && results[appsIdx].__err) {
      try {
        const retry = await dvRetrieve('cicapos_fundingapplication', '?$select=cicapos_fundingapplicationid,cicapos_name,cicapos_status,cicapos_submittedon,cicapos_pledgedate,cicapos_approvedgrant,cicapos_amountapplied,cicapos_projectname,createdon,_cicapos_casehandlerid_value,_cicapos_applicantid_value&$orderby=createdon desc');
        bag.apps = retry.entities || [];
      } catch (e2) { console.warn('dv apps retry', e2); }
    }

    const schemes = {};
    bag.schemes.forEach(s => { schemes[s.cicapos_fundingschemeid] = s.cicapos_code || s.cicapos_name || '—'; });
    const itemsByApp = {};
    bag.items.forEach(it => { const aid = it._cicapos_fundingapplicationid_value; if (!aid) return; (itemsByApp[aid] = itemsByApp[aid] || []).push(it); });

    const companies = {};
    bag.cos.forEach(c => {
      const id = c.cicapos_companyregistrationid;
      const type = dvFmt(c,'cicapos_companytype') || 'Contractor';
      companies[id] = {
        id, nameEn: c.cicapos_legalname || c.cicapos_name || 'Company', nameTc: c.cicapos_legalnamezh || '',
        br: c.cicapos_brnumber || '', brExpiry: c.cicapos_brexpiry || '', branch: '', address: c.cicapos_registeredaddress || '', website: c.cicapos_website || '',
        status: DV_CO_ST[dvOpt(c,'cicapos_status')] || dvFmt(c,'cicapos_status') || 'Pending Vetting',
        submittedOn: c.cicapos_submittedon || now().toISOString(), risk: 'Medium',
        vetter: c['_cicapos_casehandlerid_value@OData.Community.Display.V1.FormattedValue'] || '',
        pendingChange: null, companyId: c.cicapos_name || '',
        contact: { name: c.cicapos_contactname || '', email: c.cicapos_contactemail || '', mobile: c.cicapos_contactmobile || '' },
        ents: { citf: true, pal: true }, docs: [], shareholders: [], sites: [], bank: null, history: [],
        ai: { flags: ['Loaded from Dataverse'] }, type, levy: '', _live: true
      };
    });
    // Keep at least one company key for refs
    if (!Object.keys(companies).length) companies.c1 = (S.companies && S.companies.c1) || { id: 'c1', nameEn: '(no company registrations)', status: 'Verified', contact: { name: '', email: '' }, ents: { citf: true, pal: true }, docs: [], shareholders: [], sites: [], bank: null, history: [], type: 'Contractor' };

    const companyIds = Object.keys(companies);
    const apps = bag.apps.map(a => {
      const id = a.cicapos_fundingapplicationid;
      const st = DV_APP_ST[dvOpt(a,'cicapos_status')] || dvFmt(a,'cicapos_status') || 'Submitted';
      const officer = a['_cicapos_casehandlerid_value@OData.Community.Display.V1.FormattedValue'] || '';
      const its = itemsByApp[id] || [];
      const schemesOnApp = [...new Set(its.map(i => schemes[i._cicapos_schemeid_value]).filter(Boolean))];
      const approved = a.cicapos_approvedgrant != null ? +a.cicapos_approvedgrant : sum(its, i => +(i.cicapos_approvedamount || 0));
      const coId = a._cicapos_applicantid_value && companies[a._cicapos_applicantid_value] ? a._cicapos_applicantid_value : companyIds[0];
      return {
        id, ref: a.cicapos_name || 'APP', companyId: coId, schemes: schemesOnApp.length ? schemesOnApp : ['ACT'], project: a.cicapos_projectname || '',
        status: st, submittedOn: a.cicapos_submittedon || null, createdOn: a.createdon || null, officer, priority: 'Normal', checklist: {}, queries: [], docs: [], history: [{ on: a.createdon || a.cicapos_submittedon || now().toISOString(), what: 'Loaded from Dataverse' }],
        items: its.length ? its.map(i => item({ product: i.cicapos_name || 'Item', qty: +(i.cicapos_quantity || 1), unit: +(i.cicapos_unitprice || 0), approved: +(i.cicapos_approvedamount || 0), paCode: '' })) : [item({ product: a.cicapos_projectname || 'Funding item', qty: 1, unit: +(a.cicapos_amountapplied || approved || 0), approved })],
        decision: st === 'Approved' ? { by: officer || 'Approver', on: a.cicapos_submittedon, result: 'Approved' } : null,
        ai: (function () { var kept = parseStoredAiSummary(a.cicapos_aisummary); return kept.summary ? { summary: kept.summary, risks: kept.risks || [], notes: kept.notes || [], source: kept.source || 'submission', model: kept.model || '', score: kept.score } : null; })(),
        _dvGrant: approved, _pledge: a.cicapos_pledgedate || null, _live: true
      };
    });

    const DR_TYPE = {542400000:'First instalment',542400001:'Progress instalment',542400002:'Final instalment'};
    const drs = bag.drs.map(d => ({
      id: d.cicapos_disbursementrequestid, ref: d.cicapos_name || 'DR', appId: d._cicapos_fundingapplicationid_value || (apps[0] && apps[0].id) || '',
      type: DR_TYPE[dvOpt(d,'cicapos_instalmenttype')] || 'First instalment',
      status: DV_DR_ST[dvOpt(d,'cicapos_status')] || dvFmt(d,'cicapos_status') || 'Submitted',
      submittedOn: d.cicapos_submittedon || now().toISOString(), officer: '',
      lines: [{ itemId: 'x', claim: +(d.cicapos_claimedamount || 0) }], docs: [], history: [], queries: [], _live: true
    }));

    const pal = bag.pal.map(p => ({
      id: p.cicapos_preapprovedproductid, code: p.cicapos_code || p.cicapos_name, name: p.cicapos_name || p.cicapos_code,
      scheme: schemes[p._cicapos_schemeid_value] || 'ACT', price: +(p.cicapos_unitprice || 0), origin: p.cicapos_origin || '',
      status: (dvFmt(p,'cicapos_status') || 'Listed'), apps: 0, listedOn: (p.cicapos_listedon || now().toISOString()).toString().slice(0,10), vendors: [], channel: '', tech: '', safety: 'General', list: 'ACT products', web: { on: p.cicapos_listedon || null, state: /listed/i.test(dvFmt(p,'cicapos_status') || 'Listed') ? 'Published' : 'Removed' }, _live: true
    }));
    const PAL_KIND = {542400000:'New listing', 542400001:'Change request'};
    const palApps = bag.palApps.map(r => {
      const coId = r._cicapos_applicantid_value || companyIds[0] || null;
      const applicantName = r['_cicapos_applicantid_value@OData.Community.Display.V1.FormattedValue'] || '';
      if (coId && !companies[coId]) companies[coId] = { id: coId, nameEn: applicantName, nameTc: '', address: '', status: '', type: '', br: '', contact: { name: '', email: '', phone: '', mobile: '', title: '' }, docs: [], history: [], shareholders: [], sites: [], ents: {}, bank: null, ai: { flags: [] }, checklist: {}, _live: true };
      return {
        id: r.cicapos_palapplicationid, ref: r.cicapos_name || '', companyId: coId, name: r.cicapos_productname || r.cicapos_name || '',
        scheme: schemes[r._cicapos_schemeid_value] || '', price: r.cicapos_proposedprice == null ? null : +r.cicapos_proposedprice,
        status: r['cicapos_status@OData.Community.Display.V1.FormattedValue'] || DV_PAL_ST[dvOpt(r,'cicapos_status')] || '',
        submittedOn: r.cicapos_submittedon || r.createdon || null,
        kind: r['cicapos_kind@OData.Community.Display.V1.FormattedValue'] || PAL_KIND[dvOpt(r,'cicapos_kind')] || '',
        note: r.cicapos_note || '', items: [], decl: {}, queries: [], history: [], _live: true
      };
    });

    // Tickets / case queries → S.tickets shape used by Q&A
    const tickets = bag.tickets.map(t => {
      const st = DV_CQ_ST[dvOpt(t,'cicapos_status')] || dvFmt(t,'cicapos_status') || 'Open';
      const qtext = t.cicapos_question || t.cicapos_name || 'Query';
      return {
        id: t.cicapos_casequeryid, ref: t.cicapos_name || 'TQ', subject: qtext,
        status: st === 'Open' ? 'Awaiting CIC' : (st === 'Answered' ? 'Awaiting applicant' : 'Closed'),
        companyId: companyIds[0], caseId: t._cicapos_fundingapplicationid_value || (apps[0] && apps[0].id) || '', caseType: 'Funding',
        msgs: [{ on: t.cicapos_askedon || now().toISOString(), by: 'Applicant', text: qtext }].concat(t.cicapos_answer ? [{ on: now().toISOString(), by: 'CIC', text: t.cicapos_answer }] : []),
        _live: true
      };
    });

    const inspections = bag.insp.map(x => ({
      id: x.cicapos_inspectionid, appId: x._cicapos_fundingapplicationid_value || '', status: dvFmt(x,'cicapos_status') || 'Scheduled',
      date: (x.cicapos_scheduledon || now().toISOString()).slice(0,10), officer: '', result: '', method: 'Dataverse', _live: true
    }));

    // Payments → map into S.drs paid or keep separate if prototype uses S.payments - check
    // Batches
    const batches = bag.batches.map(b => ({ id: b.cicapos_committeebatchid, no: b.cicapos_name || 'Batch', committee: 'VSC', cases: [], on: b.createdon || now().toISOString(), status: dvFmt(b,'cicapos_status') || 'Open', _live: true }));

    // Vetting checklist master → S.masters.checklists (CITF Setup left-menu tab)
    if (bag.checks.length) {
      const mapped = bag.checks.map((c, i) => ({ id: c.cicapos_vettingchecklistitemid || ('ck' + i), name: c.cicapos_name || 'Checklist', scheme: dvFmt(c,'cicapos_appliesto') || 'All', seq: c.cicapos_sequencenumber || ((i + 1) * 10), active: c.statecode === 0 ? 'Active' : 'Inactive' }));
      S.masters = S.masters || {};
      S.masters.checklists = mapped;
      if (S.setup) S.setup.checklists = mapped;
    }
    // Funding schemes → masters.schemes codes used by dashboard util
    if (bag.schemes.length) {
      S.masters = S.masters || {};
      const keep = S.masters.schemes || [];
      const byCode = Object.fromEntries(keep.map(s => [s.code, s]));
      bag.schemes.forEach(s => {
        const code = s.cicapos_code || s.cicapos_name;
        if (!code) return;
        if (byCode[code]) { byCode[code].name = s.cicapos_name || byCode[code].name; byCode[code].id = s.cicapos_fundingschemeid || byCode[code].id; }
        else keep.push({ id: s.cicapos_fundingschemeid || ('sch_' + code), name: s.cicapos_name || code, code, cap: 0, ratio: 0.7, pledge: 30, active: 'Active' });
      });
      S.masters.schemes = keep;
    }
    // Payments → attach to matching DRs for Payments (Finance) tab
    if (bag.pays.length) {
      const byDr = {};
      bag.pays.forEach(p => { const id = p._cicapos_disbursementrequestid_value; if (id) (byDr[id] = byDr[id] || []).push(p); });
      drs.forEach(d => {
        const ps = byDr[d.id] || [];
        if (!ps.length) return;
        const p0 = ps[0];
        d.pay = { ref: p0.cicapos_name || 'PAY', amount: +(p0.cicapos_amount || 0), on: p0.cicapos_paidon || now().toISOString(), status: dvFmt(p0,'cicapos_status') || 'Paid' };
        if (/paid|完成|已付/i.test(String(d.pay.status)) || p0.cicapos_paidon) d.status = 'Paid';
      });
    }

    if (Object.keys(companies).length) {
      const keep = {};
      (S.users || []).forEach(u => { const id = u && u.companyId; if (id && S.companies[id] && !companies[id]) keep[id] = S.companies[id]; });
      ['c1', 'c2', 'c3'].forEach(id => { if (S.companies[id] && !companies[id]) keep[id] = S.companies[id]; });
      S.companies = Object.assign(keep, companies);
    }
    try { ensureDemoPortalAccounts(); } catch (eKeep) {}
    if (apps.length) S.apps = apps;
    if (drs.length) S.drs = drs;
    if (pal.length) { S.pal = pal; try { palInit(); } catch (e) {} }
    try { ensureDemoPal(); palInit(); } catch (e) {}
    if (palApps.length) S.palApps = palApps;
    if (tickets.length) S.tickets = tickets;
    if (inspections.length) S.inspections = inspections;
    if (batches.length) S.batches = batches;

    UI.dvLive = { on: true, asof: now().toISOString(), err: null, counts: { apps: apps.length, companies: Object.keys(companies).length, drs: drs.length, items: bag.items.length, pal: pal.length, tickets: tickets.length } };
    if (!opts.silent) toast('Operation Platform data loaded from Dataverse · ' + apps.length + ' apps · ' + tickets.length + ' tickets');
    return true;
  } catch (e) {
    UI.dvLive = { on: false, asof: null, err: String(e && e.message || e), counts: {} };
    if (!opts.silent) toast('Dataverse load failed: ' + (e && e.message || e), true);
    console.warn('dvHydrate', e);
    return false;
  }
}

async function portalHydrateInternal() {
  if (dvAvailable() || UI.portalAppsLoaded || !dvCanWrite()) return false;
  UI.portalAppsLoaded = true;
  try {
    const token = await dvToken();
    const headers = { Accept: 'application/json', 'OData-MaxVersion': '4.0', 'OData-Version': '4.0', Prefer: 'odata.include-annotations="*",odata.maxpagesize=200' };
    if (token) headers['__RequestVerificationToken'] = token;
    async function portalRows(path) {
      const res = await fetch(path, { headers: headers, credentials: 'same-origin' });
      if (!res.ok) return null;
      const data = await res.json();
      return data.value || [];
    }
    var apps = await portalRows('/_api/cicapos_fundingapplications?$select=cicapos_fundingapplicationid,cicapos_name,cicapos_status,cicapos_submittedon,cicapos_amountapplied,cicapos_projectname,cicapos_aisummary,createdon,_cicapos_casehandlerid_value,_cicapos_applicantid_value&$orderby=createdon desc&$top=200');
    if (!apps) apps = await portalRows('/_api/cicapos_fundingapplications?$select=cicapos_fundingapplicationid,cicapos_name,cicapos_status,cicapos_submittedon,cicapos_amountapplied,cicapos_projectname,createdon&$orderby=createdon desc&$top=200');
    if (!apps) { UI.portalAppsLoaded = false; return false; }
    var itemRows = await portalRows('/_api/cicapos_fundingitems?$select=cicapos_name,cicapos_unitprice,cicapos_quantity,_cicapos_fundingapplicationid_value,_cicapos_schemeid_value&$top=500') || [];
    var schemeRows = await portalRows('/_api/cicapos_fundingschemes?$select=cicapos_fundingschemeid,cicapos_code,cicapos_name&$top=50') || [];
    var schemeById = {};
    schemeRows.forEach(function (s) { schemeById[s.cicapos_fundingschemeid] = s.cicapos_code || s.cicapos_name || ''; });
    var itemsByApp = {};
    itemRows.forEach(function (it) {
      var aid = it._cicapos_fundingapplicationid_value;
      if (!aid) return;
      (itemsByApp[aid] = itemsByApp[aid] || []).push(it);
    });
    var who = dvPortalUser();
    var added = 0;
    apps.forEach(function (a) {
      var ref = a.cicapos_name || '';
      var id = a.cicapos_fundingapplicationid;
      var existing = (S.apps || []).find(function (x) { return x._dvId === id || x.ref === ref || x.id === id; });
      if (existing) {
        if (ref && existing.ref !== ref) existing.ref = ref;
        if (a.cicapos_aisummary && isPlaceholderAiSummary(existing.ai && existing.ai.summary)) {
          var kept = parseStoredAiSummary(a.cicapos_aisummary);
          if (kept.summary && !isPlaceholderAiSummary(kept.summary)) {
            existing.ai = { summary: kept.summary, risks: kept.risks || [], notes: kept.notes || [], source: kept.source || 'submission', model: kept.model || '', score: kept.score, contentKey: existing.ai && existing.ai.contentKey };
            added++;
          }
        }
        return;
      }
      if (!ref) return;
      var st = DV_APP_ST[dvOpt(a, 'cicapos_status')] || dvFmt(a, 'cicapos_status') || 'Submitted';
      if (preSubmit({ status: st })) return;
      var officer = a['_cicapos_casehandlerid_value@OData.Community.Display.V1.FormattedValue'] || '';
      var coId = a._cicapos_applicantid_value || ('live-' + id);
      var coName = a['_cicapos_applicantid_value@OData.Community.Display.V1.FormattedValue'] || '';
      if (!coName && who.accountId && String(coId).toLowerCase() === String(who.accountId).toLowerCase()) coName = who.accountName || '';
      if (!coName) coName = 'Applicant';
      if (!S.companies[coId]) S.companies[coId] = { id: coId, nameEn: coName, nameTc: '', br: '', status: 'Verified', contact: { name: '', email: '' }, ents: { citf: true }, docs: [], shareholders: [], sites: [], bank: null, history: [], ai: { flags: [] }, type: 'Contractor', companyId: '', _live: true };
      var its = itemsByApp[id] || [];
      var schemes = [...new Set(its.map(function (i) { return schemeById[i._cicapos_schemeid_value] || i['_cicapos_schemeid_value@OData.Community.Display.V1.FormattedValue'] || ''; }).filter(Boolean))];
      var amount = +(a.cicapos_amountapplied || 0);
      var items = its.length ? its.map(function (i) { return { id: i.cicapos_fundingitemid || ('it-' + id), type: 'PA', paCode: '', product: i.cicapos_name || 'Item', model: '', qty: +(i.cicapos_quantity || 1), unit: +(i.cicapos_unitprice || 0), site: '', plan: '', aiCheck: 'ok', approved: null, local: false, rental: false, s4: false, lowest: true, just: '' }; }) : [{ id: 'it-' + id, type: 'PA', paCode: '', product: a.cicapos_projectname || 'Funding item', model: '', qty: 1, unit: amount, site: '', plan: '', aiCheck: 'ok', approved: null, local: false, rental: false, s4: false, lowest: true, just: '' }];
      S.apps.push({
        id: id, ref: ref, companyId: coId, schemes: schemes.length ? schemes : ['ACT'], project: a.cicapos_projectname || '',
        status: st, submittedOn: a.cicapos_submittedon || a.createdon || null, officer: officer, priority: 'Normal',
        checklist: {}, queries: [], docs: [], history: [{ on: a.cicapos_submittedon || a.createdon || now().toISOString(), what: 'Submitted via the company portal' }],
        items: items,
        ai: (function () {
          var kept = parseStoredAiSummary(a.cicapos_aisummary);
          return kept.summary ? { summary: kept.summary, risks: kept.risks || [], notes: kept.notes || [], source: kept.source || 'submission', model: kept.model || '', score: kept.score } : null;
        })(),
        objective: '', otherFund: 'None', _live: true
      });
      added++;
    });
    return added > 0;
  } catch (e) {
    UI.portalAppsLoaded = false;
    console.warn('portalHydrateInternal', e);
    return false;
  }
}

async function portalSyncSubmitted() {
  if (dvAvailable() || !dvCanWrite()) return false;
  var wrote = false;
  var pending = (S.apps || []).filter(function (a) {
    return a && !a._dvId && !preSubmit(a) && (a.history || []).some(function (h) { return /snapshot saved/i.test(String(h && h.what || '')); });
  });
  for (var i = 0; i < pending.length; i++) {
    try {
      var id = await dvPushFunding(pending[i]);
      if (id) { pending[i]._dvId = id; pending[i]._live = true; wrote = true; }
    } catch (e) { console.warn('portalSyncSubmitted', pending[i].ref, e); }
  }
  if (wrote) { try { save(); } catch (eSave) {} }
  var added = false;
  try { added = await portalHydrateInternal(); } catch (eHydrate) { console.warn(eHydrate); }
  return wrote || added;
}

const RENDER_WITHOUT_DASH = render;
render = function () {
  const r = route();
  if (r.area === 'd' && (!r.parts[0] || r.parts[0] === 'dash')) {
    CUR = '#/p/internal';
    try { history.replaceState(null, '', CUR); } catch (e) {}
  }
  return RENDER_WITHOUT_DASH();
};

load(); compTick(true); rvTick(false); palInit(); purgeDrafts(false); opsInit(); calcInit(); trackInit();
try { document.body.classList.add('citf-flow', 'citf-proto'); } catch (e) {}
try {
  ['modalRoot','toastRoot'].forEach(function (id) {
    var el = document.getElementById(id);
    if (el && el.parentElement !== document.body) document.body.appendChild(el);
  });
} catch (e) {}
(function () {
  var q = {};
  try { q = Object.fromEntries(new URLSearchParams(location.search)); } catch (e) {}
  var email = String(q.email || '').trim().toLowerCase();
  var d365 = q.d365 ? String(q.d365) : '';
  var hash = '';
  try { hash = location.hash || ''; } catch (e) {}
  if (d365) {
    UI.d365User = d365;
    var insideCrm = /WebResources|d365ops|main\.aspx/i.test(String(location.href || ''));
    if (hash.indexOf('#/p/internal') === 0) CUR = hash;
    else if (!insideCrm && (!hash || hash === '#' || hash === '#/d/dash')) CUR = '#/p/internal';
    else if (!hash || hash === '#' || hash.indexOf('#/d/') !== 0) CUR = '#/p/internal';
  } else if (email) {
    var u = S.users.find(function (x) { return x.email.toLowerCase() === email; });
    if (u) {
      S.sessions.portal = u.id;
      u.last = now().toISOString();
      save();
      if (!hash || hash === '#' || hash.indexOf('#/p/') !== 0) {
        var c = company(u.companyId);
        CUR = u.kind === 'vendor' ? '#/p/pal' : (c && c.status === 'Verified' ? '#/p/citf' : '#/p/company');
      }
    }
  }
})();
if (!CUR) CUR = curHash() || DEFAULT_ROUTE;
try { history.replaceState(null, '', CUR); } catch (e) {}
render();
try {
  if (CUR && String(CUR).indexOf('#/p/internal') === 0) { /* internal user view, not the CRM dashboard */ }
  else if (/WebResources|d365ops/i.test(location.href) || (dvAvailable() && /main\.aspx/i.test(String(location.href)))) {
    if (!CUR || CUR.indexOf('#/d/') !== 0 || CUR === '#/d/dash') {
      CUR = '#/p/internal';
      try { history.replaceState(null, '', CUR); } catch (e3) {}
      render();
    }
  }
} catch (e4) {}
if (dvAvailable()) { dvHydrate({ silent: true }).then(function (ok) { if (ok) render(); }); }
else { portalSyncSubmitted().then(function (ok) { if (ok) render(); }); }




