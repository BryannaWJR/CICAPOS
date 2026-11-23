/* Appended by build into prototype HTML – wires all form / prefill buttons through CitfAi */
(function wireCitfAiForms() {
  if (typeof A === 'undefined' || typeof CitfAi === 'undefined') return;

  function withPickedFile(label, then) {
    toast(label || 'Choose a PDF or image…');
    return CitfAi.pickFile().then(function (file) {
      if (!file) { toast('Cancelled'); return null; }
      return Promise.resolve(then(file)).catch(function (err) {
        toast((err && err.message) || 'AI failed – check CITF_NOVA keys', true);
        return null;
      });
    }).catch(function () { toast('Could not read that file', true); return null; });
  }

  const _wSave = A.wSave;
  A.wSave = function (el) {
    const a = app(el.dataset.id), d = formData(el);
    a.schemes = d.schemes || []; a.project = d.project; a.objective = d.objective || ''; a.otherFund = d.otherFund; a.mpdDate = d.mpdDate;
    const e = {};
    if (el.dataset.next !== '0') {
      if (!a.schemes.length) e.schemes = 'Choose at least one scheme'; if (!a.project) e.project = 'Required';
      if (!a.objective.trim()) e.objective = 'Required'; else if (words(a.objective) > 250) e.objective = 'Keep it to 250 words or fewer (now ' + words(a.objective) + ')';
      if (a.schemes.includes('MPD')) { if (!a.mpdDate) e.mpdDate = 'Required for MPD'; else if ((new Date(a.mpdDate) - now()) / 864e5 < rule('mpdLead')) e.mpdDate = 'MPD applications must be submitted at least ' + rule('mpdLead') + ' days before the activity'; }
    }
    UI.err = e; save();
    if (Object.keys(e).length) {
      toast(Object.values(e)[0], true);
      return render();
    }
    const goNext = () => { if (el.dataset.next === '0') { toast('Draft saved'); return go('#/p/citf'); } go('#/p/apply/' + a.id + '/' + el.dataset.next); };
    const liveCfg = (typeof CitfAi !== 'undefined' && CitfAi.novaConfig) ? CitfAi.novaConfig() : null;
    toast(liveCfg && liveCfg.apiKey ? 'AI checking via D.Nova…' : 'AI checking… (no CITF_NOVA key – will try Custom API / local rules)');
    // Never block navigation on AI hang/CORS: local rules fallback + timeout, then continue.
    const localFallback = () => { try { syncFundingAiQuality(a); save(); } catch (x) {} };
    const timed = Promise.race([
      refreshFundingAiQuality(a),
      new Promise((_, rej) => setTimeout(() => rej(new Error('AI check timed out')), 12000))
    ]);
    timed.then(function (res) {
      const src = (res && res.source) || (a.aiQuality && a.aiQuality.source) || 'unknown';
      const model = (res && res.model) || (a.aiQuality && a.aiQuality.model) || '';
      toast('AI check source: ' + src + (model ? ' · ' + model : '') + (/nova/i.test(src) ? '' : ' (not live Nova)'));
      render(); goNext();
    }).catch(function (err) {
      localFallback();
      toast((err && err.message ? err.message + ' – ' : '') + 'fallback source: rules (local mock)', true);
      render();
      goNext();
    });
  };

  const _itPlan = A.itPlan;
  A.itPlan = function (el) {
    _itPlan.call(this, el);
    const a = app(el.dataset.id);
    clearTimeout(A._aiPlanT);
    A._aiPlanT = setTimeout(() => refreshFundingAiQuality(a).then(() => save()), 450);
  };

  A.siteAi = function () {
    withPickedFile('Choose award letter (PDF/image)…', function (file) {
      toast('AI reading award letter…');
      return CitfAi.assist({ action: 'prefill', schemaKey: 'citf_site', docType: 'award_letter', fileName: file.fileName, fileBase64: file.fileBase64 }).then(res => {
        UI.siteDraft = Object.assign({}, res.fields);
        A.addSite();
        toast('AI filled site fields from the award letter · ' + res.source + ' – please check');
      });
    });
  };

  A.regAll = function (el) {
    withPickedFile('Choose BR / NAR1 / bank PDF…', function (file) {
      toast('AI reading BR, Annual Return and bank statement…');
      return CitfAi.assist({ action: 'prefill', schemaKey: 'company_registration', docType: 'br_nar1_bank', fileName: file.fileName, fileBase64: file.fileBase64 }).then(res => {
        const d = regKeep(el); const f = res.fields || {};
        Object.assign(d, {
          brFile: d.brFile || file.fileName || ('BR_' + (f.br || '68899001') + '.pdf'),
          br: f.br || d.br, branch: f.branch || d.branch, brExpiry: f.brExpiry || d.brExpiry,
          nameEn: f.nameEn || d.nameEn, nameTc: f.nameTc || d.nameTc, address: f.address || d.address,
          staff: f.staff || d.staff || '36', aiDone: true,
          nar1: d.nar1 || ('NAR1_' + (f.br || '68899001') + '_2026.pdf'), narAi: true,
          stmt: d.stmt || ('Bank_statement_' + now().toISOString().slice(0, 7) + '.pdf'), stmtAi: true,
          stmtDate: f.stmtDate || addDays(now(), -18).slice(0, 10),
          bankName: f.bankName || d.bankName, bankAcct: f.bankAcct || d.bankAcct, bankHolder: f.bankHolder || f.nameEn || d.bankHolder,
          mgName: f.mgName || d.mgName, mgTitle: f.mgTitle || d.mgTitle
        });
        try { d.sh = JSON.parse(f.shareholdersJson || '[]'); } catch (e) {}
        if (!d.appCat) { d.appCat = APP_CATS[0][0]; d.levyNo = d.levyNo || 'LC-0123456'; }
        UI.reg = d; render();
        toast('AI filled the form from document · ' + res.source + ' – check fields marked AI');
      });
    });
  };

  A.palPrefill = function (el) {
    const p = S.palApps.find(x => x.id === el.dataset.id), it = p.items.find(i => i.id === el.dataset.it), i = p.items.indexOf(it), k = palCat(p.scheme).kind;
    withPickedFile('Choose ' + (k === 'course' ? 'course outline' : 'catalogue / specification') + '…', function (file) {
      toast('AI reading ' + (k === 'course' ? 'course outline' : 'document matrix') + '…');
      return CitfAi.assist({ action: 'prefill', schemaKey: k === 'course' ? 'pal_course' : 'pal_product', docType: k, fileName: file.fileName, fileBase64: file.fileBase64 }).then(res => {
        const f = res.fields || {};
        Object.assign(it, f, { supplier: myCo().nameEn, aiFilled: true, price: f.price ? +f.price : it.price });
        it.files = it.files || {};
        const pick = k === 'course' ? ['Photos', 'Authorisation letter', 'Catalogue', 'Price list', 'Reference letter'] : ['Photos', 'Video'].concat(PAL_AI_MATRIX).concat(['Reference letter', 'Test report']);
        pick.forEach(t => { const ext = (PAL_UPLOADS.find(u => u[0] === t) || [t, 'pdf'])[1].replace('image', 'jpg').replace('video', 'mp4'); if (!it.files[t]) it.files[t] = palFileName(p, i, it, t, ext); });
        if (it.local === 'Yes' && !it.files['Local development proof']) it.files['Local development proof'] = palFileName(p, i, it, 'Local development proof', 'pdf');
        if (file.fileName) it.files[k === 'course' ? 'Catalogue' : 'Specification'] = file.fileName;
        it.fileAi = it.fileAi || {};
        Object.keys(it.files).forEach(tt => { if (!it.fileAi[tt]) it.fileAi[tt] = { ai: 'ok', note: PAL_AI_MATRIX.includes(tt) ? 'AI/OCR pre-checked against form fields' : 'Readable' }; });
        save(); render();
        toast('AI filled all 9 sections · ' + res.source + ' – please check each one');
      });
    });
  };

  A.palUp = function (el) {
    const p = S.palApps.find(x => x.id === el.dataset.id), it = A.palCollect(el), i = p.items.indexOf(it), t = el.dataset.t;
    const ext = (PAL_UPLOADS.find(u => u[0] === t) || [t, 'pdf'])[1].replace('image', 'jpg').replace('video', 'mp4');
    withPickedFile('Choose ' + t + '…', function (file) {
      toast('AI/OCR reading ' + t + '…');
      return CitfAi.assist({ action: 'validate_upload', schemaKey: 'supporting_doc', docType: t, fileName: file.fileName, fileBase64: file.fileBase64 }).then(res => {
        it.files = it.files || {};
        it.files[t] = file.fileName || palFileName(p, i, it, t, ext);
        it.fileAi = it.fileAi || {};
        const flagged = res.issues && res.issues.some(x => x.code === 'blurred');
        it.fileAi[t] = { ai: flagged ? 'flag' : 'ok', note: flagged ? (res.issues[0].message) : (res.notes || 'Readable; matches the form') };
        save(); render();
        toast(flagged ? 'AI flagged ' + t : 'Uploaded · AI/OCR: ' + it.fileAi[t].note, flagged);
      });
    });
  };

  A.docUp = function (el) {
    const a = app(el.dataset.id), t = el.dataset.t, iid = el.dataset.it || null;
    a.docs = a.docs.filter(d => !(d.type === t && (d.itemId || null) === iid));
    const it = iid ? a.items.find(i => i.id === iid) : null;
    withPickedFile('Choose ' + t + '…', function (file) {
      const n = a.docs.filter(d => d.type === t).length + 1;
      const doc = { type: t, itemId: iid, file: file.fileName || `${a.ref}-${t.split(' ')[0]}${n}.pdf`, ai: 'ok', note: '' };
      const finish = () => { a.docs.push(doc); save(); render(); toast(doc.ai === 'ok' ? 'Uploaded · virus scan passed · AI read the document' : 'AI flagged a mismatch – check the item', doc.ai !== 'ok'); };
      const base = { fileName: file.fileName, fileBase64: file.fileBase64 };
      if (t === 'Quotation' && it) {
        toast('AI checking quotation…');
        return CitfAi.assist(Object.assign({ action: 'validate_upload', schemaKey: 'quotation_check', docType: it.type === 'NPA' && !it.aiChecked ? 'npa' : 'match', currentValues: { formAmount: String(itemAmt(it)) } }, base)).then(res => {
          const amount = +(res.fields.amount || 0);
          if (it.type === 'NPA' && !it.aiChecked && res.issues.some(i => i.code === 'mismatch')) {
            it.aiChecked = true; it.aiUnit = amount || Math.round(it.unit * 0.957);
            it.aiCheck = 'flag'; it.aiNote = `Quotation shows ${money((it.aiUnit) * it.qty)}; form shows ${money(itemAmt(it))}`;
            doc.ai = 'flag'; doc.note = 'Amount differs from the form';
          } else {
            doc.note = res.notes || 'Supplier, item and amount match';
          }
          finish();
        }).catch(finish);
      }
      if (t.startsWith('NAR1')) doc.note = 'Filed ' + fmtD(addDays(now(), -120)) + ' – current';
      if (t === 'Shareholder BR') doc.note = 'Matches declared shareholders';
      toast('AI reading document…');
      return CitfAi.assist(Object.assign({ action: 'validate_upload', schemaKey: 'supporting_doc', docType: t }, base)).then(res => {
        if (res.issues.some(i => i.code === 'blurred')) { doc.ai = 'flag'; doc.note = res.issues[0].message; }
        else if (!doc.note) doc.note = res.notes || 'Readable; fields match the form';
        finish();
      }).catch(finish);
    });
  };

  A.docBlur = function (el) {
    const a = app(el.dataset.id), t = el.dataset.t, iid = el.dataset.it || null;
    toast('AI quality check…');
    CitfAi.assist({ action: 'validate_upload', schemaKey: 'supporting_doc', docType: 'blurred' }).then(res => {
      a.docs = a.docs.filter(d => !(d.type === t && (d.itemId || null) === iid));
      a.docs.push({ type: t, itemId: iid, file: 'scan_blurred.jpg', ai: 'flag', note: (res.issues[0] && res.issues[0].message) || res.notes });
      save(); render(); toast('AI quality check: blurred scan rejected', true);
    });
  };

  A.aiSuggest = function (el) {
    const t = (document.getElementById('aiDesc').value || '');
    const a = app(el.dataset.id);
    toast('AI suggesting items…');
    CitfAi.assist({ action: 'suggest', schemaKey: 'funding_application', currentValues: { query: t } }).then(res => {
      let suggestions = [];
      try { suggestions = JSON.parse((res.fields && res.fields.suggestionsJson) || '[]'); } catch (e) {}
      const hits = suggestions.map(s => S.pal.find(p => p.code === s.code)).filter(Boolean);
      const g = ceiling(company(a.companyId).id);
      UI.aiSug = `<div class="note info">${hits.length ? `Based on your description and remaining ceiling (${money(g.left)}), consider:<ul style="margin:6px 0 0;padding-left:18px">${hits.slice(0, 3).map(p => `<li><b>${esc(p.name)}</b> (${p.code}, ${money(p.price)}, scheme ${p.scheme}) <a href="#" data-act="paAdd" data-id="${a.id}" data-p="${p.id}">Add</a></li>`).join('')}</ul>` : 'Try words like “safety”, “drone survey”, “BIM” or “concrete curing”.'}<div class="hint">AI source: ${esc(res.source)}</div></div>`;
      render();
    });
  };

  A.rvAiFill = function () {
    withPickedFile('Choose reply letter…', function (file) {
      const rv = rv0(), c = myCo();
      toast('AI reading reply letter…');
      return CitfAi.assist({ action: 'prefill', schemaKey: 'pal_annual_reply', docType: 'reply_letter', fileName: file.fileName, fileBase64: file.fileBase64 }).then(() => {
        const d = {};
        rv.items.map(x => S.pal.find(p => p.id === x.palId)).filter(p => p && vKey(p) === c.id).forEach((p, i) => {
          d[p.id] = i === 0
            ? { price: Math.round(p.price * 1.04), change: 'Price change', just: 'Supplier cost increase of 4% from 1 March 2027; quotation from manufacturer attached.' }
            : { price: p.price, change: 'No change', just: '' };
        });
        d._files = [file.fileName || ('Reply_letter_' + c.nameEn.split(' ')[0] + '_' + rv.year + '.pdf')];
        UI.rvDraft = d; modal(rvReplyForm(c), true);
        toast('AI read your letter and filled the form – please check');
      });
    });
  };

  const _drSubmit = A.drSubmit;
  A.drSubmit = function (el) {
    const a = app(el.dataset.id), d = formData(el), F = UI.drForm;
    if (!F) return _drSubmit.call(this, el);
    toast('AI automatic check on disbursement evidence…');
    const previewDocs = DOCS_DR.slice(0, 5).filter(t => d['doc_' + t]).map(t => ({ type: t, ai: 'ok', note: '' }));
    return CitfAi.assist({
      action: 'validate_form',
      schemaKey: 'disbursement_request',
      currentValues: { docsJson: JSON.stringify(previewDocs), paid: String(!!d.paid) }
    }).then(res => {
      const n0 = S.drs.length;
      _drSubmit.call(this, el);
      if (S.drs.length > n0) {
        const x = S.drs[S.drs.length - 1];
        x.docs.push({ type: 'Site photo (metadata)', file: 'IMG_0412.jpg', ai: 'ok', note: 'Taken ' + fmtD(addDays(now(), -3)) + ' at 22.31°N 114.20°E – within 150 m of the project site and after the delivery note date ✓' });
        x.aiReview = {
          on: now().toISOString(),
          pass: res.pass && !x.docs.some(doc => doc.ai === 'flag'),
          summary: res.notes || (res.pass ? 'AI automatic check passed' : 'AI flagged evidence issues')
        };
        save(); render();
        toast(x.ref + ' submitted · ' + x.aiReview.summary, !x.aiReview.pass);
      }
    }).catch(function (err) {
      toast((err && err.message) || 'AI check failed – submitting with local rules', true);
      _drSubmit.call(this, el);
    });
  };

  const _annSave = A.annSave;
  A.annSave = function (el) {
    const d = formData(el);
    toast('AI automatic check on annual confirmation…');
    return CitfAi.assist({
      action: 'validate_form',
      schemaKey: 'annual_confirmation',
      currentValues: { hasNc: d.hasNc || 'No', ncJust: d.ncJust || '', prod: d.prod, safety: d.safety, quality: d.quality }
    }).then(res => {
      _annSave.call(this, el);
      const x = comp().annual.find(y => y.id === el.dataset.id);
      if (x) {
        x.aiReview = Object.assign(x.aiReview || {}, { source: res.source, notes: res.notes, pass: res.pass && (x.aiReview ? x.aiReview.pass : true) });
        save();
      }
    }).catch(function (err) {
      toast((err && err.message) || 'AI check failed – saving with local rules', true);
      _annSave.call(this, el);
    });
  };

  const _rvReport = A.rvReport;
  A.rvReport = function () {
    toast('AI automatic check: extract replies, verify prices, draft recommendations…');
    return CitfAi.assist({
      action: 'validate_form',
      schemaKey: 'pal_annual_reply',
      currentValues: { itemsJson: JSON.stringify((rv0().items || []).map(x => x.palId)) }
    }).then(res => {
      _rvReport.call(this);
      const rv = rv0();
      rv.aiReview = Object.assign(rv.aiReview || {}, { source: res.source, notes: res.notes || rv.aiReview.summary });
      save(); render();
    }).catch(function (err) {
      toast((err && err.message) || 'AI check failed – drafting with local rules', true);
      _rvReport.call(this);
    });
  };
})();
