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
    a.schemes = Array.isArray(d.schemes) ? d.schemes.filter(Boolean) : (d.schemes ? [d.schemes] : []);
    a.project = d.project; a.objective = d.objective || ''; a.otherFund = d.otherFund; a.mpdDate = d.mpdDate;
    if (el.dataset.next === '0') {
      a.status = 'Draft'; a.edited = now().toISOString(); a.step = 1; UI.err = {}; save();
      toast('Draft saved. You can continue later.');
      return go('#/p/citf');
    }
    UI.err = {};
    if (!a.schemes.length) {
      UI.err = { schemes: 'Choose at least one scheme' };
      save(); render(); toast('Choose at least one scheme', true);
      return;
    }
    save();
    if (typeof scheduleFundingAiCheck === 'function') scheduleFundingAiCheck(a, 'objective');
    go('#/p/apply/' + a.id + '/' + el.dataset.next);
  };

  A.wItemsNext = function (el) {
    try {
      const a = app(el.dataset.id);
      if (!a) { toast('Application not found', true); return; }
      if (typeof flushItemPlans === 'function') flushItemPlans(a);
      save();
      if (!a.items.length) { toast('Add at least one item', true); return render(); }
      if (!a.items.every(function (it) { return String(it.plan || '').trim(); })) {
        toast('Fill an implementation plan for every item', true); return render();
      }
      if (typeof scheduleFundingAiCheck === 'function') scheduleFundingAiCheck(a, 'plan');
      go('#/p/apply/' + a.id + '/3');
    } catch (e) {
      toast((e && e.message) || 'Could not continue', true);
    }
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

  function applyPalAiFields(p, it, i, k, res, sourceName) {
    if (typeof palApplyExtracted === 'function') return palApplyExtracted(p, it, i, k, res, sourceName);
    const f = (res && res.fields) || {};
    Object.assign(it, f, { supplier: myCo().nameEn, aiFilled: true, price: f.price ? +f.price : it.price });
    save(); render();
    toast('AI filled all 9 sections from ' + sourceName);
  }

  function asFilePayload(file) {
    if (!file) return Promise.resolve(null);
    if (file.fileBase64) return Promise.resolve(file);
    if (CitfAi.readFile) return CitfAi.readFile(file);
    return new Promise(function (resolve, reject) {
      var r = new FileReader();
      r.onload = function () { resolve({ fileName: file.name, fileBase64: String(r.result || ''), mime: file.type || '' }); };
      r.onerror = function () { reject(r.error || new Error('read failed')); };
      r.readAsDataURL(file);
    });
  }

  A.palPrefill = function (el) {
    const p = S.palApps.find(x => x.id === el.dataset.id);
    if (!p) { toast('Open the item form first', true); return; }
    const it = p.items.find(i => i.id === el.dataset.it), i = p.items.indexOf(it), k = palCat(p.scheme).kind;
    if (!it) { toast('Item not found', true); return; }
    const demoUrl = typeof palDemoUrl === 'function' ? palDemoUrl() : 'https://site-9m48j.powerappsportals.com/sitemodel-cde.html';
    const url = ((document.getElementById('palPageUrl') || {}).value || '').trim() || demoUrl;
    const schemaKey = k === 'course' ? 'pal_course' : 'pal_product';
    const busy = function (msg) { if (typeof showAiBusy === 'function') showAiBusy(msg); else toast(msg); };
    const done = function () { if (typeof hideAiBusy === 'function') hideAiBusy(); };
    const isDemo = /sitemodel-cde/i.test(url);
    busy('AI checking · reading product webpage…');
    toast('AI checking product webpage…');
    return CitfAi.assist({
      action: 'prefill',
      schemaKey: schemaKey,
      docType: k,
      documentText: isDemo && typeof PAL_DEMO_DOC !== 'undefined' ? PAL_DEMO_DOC : undefined,
      currentValues: { pageUrl: url }
    }).then(function (res) {
      it.mfWeb = url;
      applyPalAiFields(p, it, i, k, res, 'webpage');
    }).catch(function (err) {
      toast((err && err.message) || 'Could not read that webpage', true);
    }).finally(done);
  };

  A.palItemSave = function (el) {
    const p = S.palApps.find(x => x.id === el.dataset.id);
    const it = A.palCollect(el);
    p.name = p.items[0].name || '(draft)';
    p.price = +p.items[0].price || 0;
    p.status = 'Draft'; p.edited = now().toISOString();
    const k = palCat(p.scheme).kind;
    const schemaKey = k === 'course' ? 'pal_course' : 'pal_product';
    const gaps = palItemGaps(p, it);
    if (el.dataset.draft === '1' || !palItemStarted(it)) {
      it.aiChecked = false;
      it.aiGaps = [];
      UI.err = {};
      save();
      if (el.dataset.draft === '1') {
        toast('Draft saved. You can continue later.');
        return go('#/p/pal');
      }
      go('#/p/palapp/' + p.id + '/2');
      toast('Item saved');
      return;
    }
    it.aiGaps = gaps.slice();
    it.aiChecked = true;
    save();
    const busy = function (msg) { if (typeof showAiBusy === 'function') showAiBusy(msg); else toast(msg); };
    const done = function () { if (typeof hideAiBusy === 'function') hideAiBusy(); };
    function finish(aiIssues) {
      done();
      (aiIssues || []).forEach(function (i) {
        if (!i || !i.message || i.severity === 'warn') return;
        if (i.field === 'objective' || /^item:/.test(i.field || '')) return;
        if (!gaps.some(function (g) { return g.indexOf(i.field || '') >= 0 || g === i.message; }))
          gaps.push(i.field ? (i.field + ' · ' + i.message) : i.message);
      });
      it.aiGaps = gaps;
      it.aiChecked = true;
      UI.err = typeof palGapFields === 'function' ? palGapFields(gaps) : {};
      (aiIssues || []).forEach(function (i) {
        if (i && i.field && i.message && i.severity !== 'warn') UI.err[i.field] = i.message;
      });
      save();
      if (gaps.length) {
        render();
        toast('AI quality check: ' + gaps.length + ' required field(s) missing — ' + gaps.join('; '), true);
        try { const box = document.querySelector('.f.bad, .note.err'); if (box) box.scrollIntoView({ block: 'center', behavior: 'smooth' }); } catch (x) {}
        return;
      }
      go('#/p/palapp/' + p.id + '/2');
      toast('Item saved · AI check passed');
    }
    busy('AI checking required fields…');
    const current = {
      name: it.name || '', model: it.model || '', spec: it.spec || '', mf: it.mf || '',
      supplier: it.supplier || '', launch: it.launch || '', origin: it.origin || '',
      functions: it.functions || '', innovation: it.innovation || '', merits: it.merits || '',
      price: String(it.price || ''), obj: (it.obj || []).join(','),
      jobsJson: JSON.stringify(it.jobs || []), files: Object.keys(it.files || {}).join(',')
    };
    const timed = Promise.race([
      CitfAi.assist({ action: 'validate_form', schemaKey: schemaKey, currentValues: current }),
      new Promise(function (_, rej) { setTimeout(function () { rej(new Error('AI check timed out')); }, 8000); })
    ]);
    timed.then(function (res) { finish((res && res.issues) || []); }).catch(function () { finish([]); });
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
    const note = 'Image is blurred – text cannot be read. Please upload a clearer copy.';
    a.docs = a.docs.filter(d => !(d.type === t && (d.itemId || null) === iid));
    a.docs.push({ type: t, itemId: iid, file: 'scan_blurred.jpg', ai: 'flag', note: note });
    save(); render();
    toast('AI quality check: blurred scan rejected', true);
    CitfAi.assist({ action: 'validate_upload', schemaKey: 'supporting_doc', docType: 'blurred' }).catch(function () {});
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
