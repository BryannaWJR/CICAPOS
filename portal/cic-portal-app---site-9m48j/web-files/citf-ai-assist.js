/**
 * CICAPOS AI Assist client – mirrors plugins/CICAPOS.Plugins/Ai
 * Actions: prefill | validate_upload | validate_form | suggest
 * Default: real D.Nova OCR + Chat (window.CITF_NOVA / localStorage) or Custom API.
 * Demo catalog only with ?ai=demo.
 */
(function (global) {
  "use strict";

  var ACTIONS = {
    Prefill: "prefill",
    ValidateUpload: "validate_upload",
    ValidateForm: "validate_form",
    Suggest: "suggest",
    Summarize: "summarize"
  };

  var SCHEMAS = {
    company_registration: "company_registration",
    citf_site: "citf_site",
    funding_application: "funding_application",
    funding_item_plan: "funding_item_plan",
    pal_product: "pal_product",
    pal_course: "pal_course",
    pal_annual_reply: "pal_annual_reply",
    quotation_check: "quotation_check",
    supporting_doc: "supporting_doc",
    disbursement_request: "disbursement_request",
    annual_confirmation: "annual_confirmation"
  };

  var OBJECTIVE_MIN = 12;
  var OBJECTIVE_MAX = 250;
  var cache = Object.create(null);

  function words(t) {
    return (String(t || "").trim().match(/\S+/g) || []).length;
  }

  function hasBenefit(t) {
    return /benefit|improv|reduc|sav|safer|safety|productiv|efficien|cut |lower|enhance|quality|incident/i.test(t || "");
  }

  function hasImplement(t) {
    return /implement|deploy|install|monitor|train|use |using|cover|apply|roll.?out|operate|site|automat|tying|zone|crane|camera|survey|licence|license/i.test(t || "");
  }

  function shaLite(s) {
    var h = 2166136261;
    s = String(s || "");
    for (var i = 0; i < s.length; i++) {
      h ^= s.charCodeAt(i);
      h = Math.imul(h, 16777619);
    }
    return (h >>> 0).toString(16);
  }

  function demoPrefill(schemaKey, docType) {
    if (schemaKey === SCHEMAS.company_registration) {
      return {
        br: "68899001",
        branch: "000",
        brExpiry: "2027-12-31",
        nameEn: "Lantau Smart Construction Limited",
        nameTc: "大嶼山智慧建造有限公司",
        address: "Unit 12, 8/F, Lantau Commerce Centre, Tung Chung, New Territories",
        staff: "36",
        bankName: "004 The Hongkong and Shanghai Banking Corporation",
        bankAcct: "511-278830-838",
        bankHolder: "Lantau Smart Construction Limited",
        stmtDate: "",
        shareholdersJson: JSON.stringify([
          { name: "Lantau Holdings Limited", kind: "Company", br: "60011223", pct: "70" },
          { name: "Yeung Tak Man", kind: "Individual", br: "", pct: "30" }
        ]),
        mgName: "Yeung Tak Man",
        mgTitle: "Director"
      };
    }
    if (schemaKey === SCHEMAS.citf_site) {
      return {
        no: "C/2027/031",
        n: "Tung Chung New Town Extension – Site 5 Foundation Works",
        emp: "Civil Engineering and Development Department",
        val: "186500000",
        s: "2027-02-01",
        e: "2029-07-31"
      };
    }
    if (schemaKey === SCHEMAS.pal_product) {
      return {
        name: "SiteEye AI Safety Camera System (4S)",
        model: "SE-400",
        version: "2026.1",
        spec: "IP66 camera with edge AI for helmet/vest and exclusion-zone detection; 24/7 monitoring with supervisor alerts within 2 seconds.",
        mf: "SmartSite Robotics HK Limited",
        mfCountry: "Hong Kong",
        origin: "Hong Kong",
        local: "Yes",
        functions: "Watches the work area 24/7; detects missing helmets and vests and people inside crane or plant exclusion zones; alerts the supervisor’s phone and the site speaker within 2 seconds; keeps a daily safety log.",
        innovation: "On-device vision model tuned for Hong Kong construction PPE and tower-crane exclusion zones.",
        merits: "Supports CITF Smart Site Safety System objectives by reducing struck-by incidents and providing auditable alerts.",
        price: "86000",
        unit: "per set",
        launch: "2024-03-01"
      };
    }
    if (schemaKey === SCHEMAS.pal_course) {
      return {
        name: "BIM for Site Engineers",
        model: "BIMT-SE-01",
        version: "2026 syllabus",
        spec: "24 hours over 6 sessions, classroom with hands-on labs; entry level: site engineers with no BIM experience; assessment: practical test, 70% pass mark.",
        mf: "SmartSite Academy",
        origin: "Hong Kong",
        local: "Yes",
        functions: "Participants can read and navigate federated models, check quantities, raise issues in a CDE and use models in site inspections.",
        innovation: "Uses the participant’s own live project models in labs.",
        merits: "Over 300 engineers trained; 92% pass rate; employers report faster model-based inspections.",
        price: "7800",
        unit: "per participant",
        launch: "2023-09-01"
      };
    }
    if (schemaKey === SCHEMAS.quotation_check) {
      var mismatch = String(docType || "") !== "match";
      return {
        supplier: "SmartSite Robotics HK Limited",
        product: "AutoRebar Tying Robot ART-2",
        unitPrice: mismatch ? "402000" : "420000",
        qty: "1",
        amount: mismatch ? "402000" : "420000",
        formAmount: "420000"
      };
    }
    if (schemaKey === SCHEMAS.supporting_doc) {
      var blurred = String(docType || "").toLowerCase() === "blurred";
      var matrixNotes = {
        "Authorisation letter": "OCR: company chop and authorised signatory detected; matches vendor",
        "Specification": "OCR: key parameters match the technical specification section",
        "Catalogue": "OCR: product name, model and features match the form",
        "Patent documents": "OCR: patent number and title extracted",
        "Price list": "OCR: unit rate matches the price section",
        "Performance comparison": "OCR: claimed merits cross-checked against comparison figures",
        "Invoice": "Invoice amounts match the expense lines entered",
        "Payment receipt": "Receipt cites the invoice number · signature and company chop detected",
        "Bank transfer record": "Bank transfer amount and payee match the invoice",
        "Purchase order": "PO matches the approved item",
        "Delivery note": "Delivered quantity matches"
      };
      return {
        docType: docType || "Document",
        readable: blurred ? "false" : "true",
        note: blurred
          ? "Image is blurred – text cannot be read. Please upload a clearer copy."
          : (matrixNotes[docType] || "Readable; fields match the form")
      };
    }
    return {};
  }

  function validateFunding(values) {
    var issues = [];
    var objective = values.objective || "";
    if (!String(objective).trim()) {
      issues.push({ field: "objective", code: "missing", message: "Project objective and expected benefits are missing", severity: "error" });
    } else {
      var w = words(objective);
      if (w > OBJECTIVE_MAX) issues.push({ field: "objective", code: "too_long", message: "Project objective is over 250 words (now " + w + ") – AI quality check", severity: "error" });
      else if (w < OBJECTIVE_MIN) issues.push({ field: "objective", code: "too_brief", message: "Project objective is too brief (minimum 12 words, now " + w + ") – describe benefits and implementation (AI quality check)", severity: "error" });
      else if (!hasBenefit(objective) || !hasImplement(objective)) issues.push({ field: "objective", code: "weak_content", message: "Project objective should cover expected benefits and how you will implement (AI quality check)", severity: "error" });
    }
    var items = [];
    try { items = JSON.parse(values.itemsJson || "[]"); } catch (e) { items = []; }
    items.forEach(function (it, i) {
      var plan = (it && it.plan) || "";
      if (!String(plan).trim()) issues.push({ field: "item:" + (i + 1) + ".plan", code: "plan_missing", message: "Item " + (i + 1) + ": implementation plan is missing", severity: "error" });
      else if (!/\d/.test(plan)) issues.push({ field: "item:" + (i + 1) + ".plan", code: "plan_no_quantity", message: "Item " + (i + 1) + ": implementation plan has no quantitative detail (e.g. number of workers, floors, area or expected savings) – AI quality check", severity: "error" });
    });
    return issues;
  }

  function validateQuotation(fields) {
    var amount = Number(String(fields.amount || "").replace(/[^\d.]/g, ""));
    var formAmount = Number(String(fields.formAmount || "").replace(/[^\d.]/g, ""));
    if (!amount) return [{ field: "amount", code: "unreadable", message: "Quotation amount could not be read (AI quality check)", severity: "error" }];
    if (!formAmount) return [{ field: "formAmount", code: "missing", message: "Form amount is missing for quotation comparison", severity: "error" }];
    if (Math.abs(amount - formAmount) >= 0.5) {
      return [{ field: "amount", code: "mismatch", message: "Quotation shows HK$" + amount.toLocaleString("en-HK") + "; form shows HK$" + formAmount.toLocaleString("en-HK"), severity: "error" }];
    }
    return [];
  }

  function localExecute(req) {
    var action = String(req.action || "").toLowerCase();
    var schemaKey = req.schemaKey;
    var current = req.currentValues || {};
    var hash = req.contentHash || shaLite(JSON.stringify(req));
    if (cache[hash]) return Promise.resolve(cache[hash]);

    var fields = {};
    var issues = [];
    var source = "demo";
    var notes = "";

    if (action === ACTIONS.Prefill) {
      fields = demoPrefill(schemaKey, req.docType);
      notes = "Prefill (demo catalog – no cloud keys = demo; optional NOVA_BASE_URL+NOVA_API_KEY)";
    } else if (action === ACTIONS.ValidateForm) {
      if (schemaKey === SCHEMAS.disbursement_request) {
        var docs = [];
        try { docs = JSON.parse(current.docsJson || "[]"); } catch (e) { docs = []; }
        if (!docs.length) issues.push({ field: "docs", code: "missing", message: "No evidence documents for AI check", severity: "error" });
        docs.forEach(function (d) { if (d.ai === "flag") issues.push({ field: d.type || "doc", code: "flagged", message: d.note || "Evidence flagged", severity: "error" }); });
        notes = issues.length ? "AI evidence issues found" : "AI automatic check passed for disbursement evidence";
      } else if (schemaKey === SCHEMAS.annual_confirmation) {
        if (String(current.hasNc || "") === "Yes" && !String(current.ncJust || "").trim()) {
          issues.push({ field: "ncJust", code: "missing", message: "Non-compliance justification is incomplete (AI quality check)", severity: "error" });
        }
        ["prod", "safety", "quality"].forEach(function (k) {
          var n = Number(current[k] || 0);
          if (!(n >= 1 && n <= 5)) issues.push({ field: k, code: "range", message: "Impact score " + k + " must be 1–5 (AI quality check)", severity: "error" });
        });
        notes = issues.length ? "AI quality issues found" : "AI automatic check passed for annual confirmation";
      } else if (schemaKey === SCHEMAS.pal_annual_reply) {
        notes = "AI automatic check: replies extracted and price vs P-rate verified";
      } else {
        issues = validateFunding(current);
        notes = issues.length ? "AI quality issues found" : "AI completeness check passed";
      }
      source = "rules";
    } else if (action === ACTIONS.ValidateUpload) {
      if (schemaKey === SCHEMAS.quotation_check) {
        fields = demoPrefill(SCHEMAS.quotation_check, req.docType);
        if (current.formAmount) fields.formAmount = String(current.formAmount);
        issues = validateQuotation(fields);
        notes = issues.length ? "Quotation flagged" : "Quotation matches the form";
      } else {
        fields = demoPrefill(SCHEMAS.supporting_doc, req.docType);
        if (String(fields.readable) === "false") {
          issues = [{ field: "readable", code: "blurred", message: fields.note, severity: "error" }];
        }
        notes = fields.note;
      }
    } else if (action === ACTIONS.Suggest) {
      var q = String(current.query || req.documentText || "").toLowerCase();
      var suggestions = [];
      if (/safety|crane|camera/.test(q)) suggestions = [{ code: "PA-ACT-0412", name: "SiteEye AI Safety Camera System (4S)" }, { code: "PA-ACT-0441", name: "Smart Lifting Monitoring System (4S)" }];
      else if (/drone|survey/.test(q)) suggestions = [{ code: "PA-ACT-0377", name: "DroneScan Site Survey Kit" }];
      else if (/bim/.test(q)) suggestions = [{ code: "PA-BIM-0105", name: "BIM Authoring Suite (1-year licence)" }];
      fields = { suggestionsJson: JSON.stringify(suggestions) };
      notes = "Demo suggestions";
    } else if (action === ACTIONS.Summarize) {
      var nWords = words(current.objective || "");
      fields = {
        summary: "Applicant submitted a " + (current.schemes || "CITF") + " funding application for " + (current.project || "the nominated site")
          + ". Objective (" + nWords + " words) is checked against the 12–250 word range. Items: " + (current.itemsJson || "[]")
          + ". AI text analysis completed on submit; a CIC officer still reviews the case.",
        score: "82",
        risksJson: "[]"
      };
      notes = fields.summary;
    } else {
      return Promise.reject(new Error("Unsupported AI action: " + action));
    }

    var res = {
      pass: !issues.some(function (i) { return i.severity === "error"; }),
      source: source,
      model: null,
      fields: fields,
      issues: issues,
      notes: notes,
      contentHash: hash
    };
    cache[hash] = res;
    return Promise.resolve(res);
  }

  var SCHEMA_KEYS = {
    company_registration: ["br", "branch", "brExpiry", "nameEn", "nameTc", "address", "staff", "bankName", "bankAcct", "bankHolder", "stmtDate", "shareholdersJson", "mgName", "mgTitle"],
    citf_site: ["no", "n", "emp", "val", "s", "e"],
    funding_application: ["objective", "itemsJson"],
    pal_product: ["name", "model", "version", "spec", "mf", "mfCountry", "origin", "local", "functions", "innovation", "merits", "price", "unit", "launch"],
    pal_course: ["name", "model", "version", "spec", "mf", "origin", "local", "functions", "innovation", "merits", "price", "unit", "launch"],
    quotation_check: ["supplier", "product", "unitPrice", "qty", "amount", "formAmount"],
    supporting_doc: ["docType", "readable", "note"],
    pal_annual_reply: ["change", "just", "price"]
  };

  function novaConfig() {
    var c = global.CITF_NOVA;
    if (c && c.apiKey) return normalizeNova(c);
    try {
      var raw = localStorage.getItem("CITF_NOVA");
      if (raw) {
        var parsed = JSON.parse(raw);
        if (parsed && parsed.apiKey) return normalizeNova(parsed);
      }
    } catch (e) {}
    return null;
  }

  function normalizeNova(c) {
    return {
      apiKey: c.apiKey || c.NOVA_API_KEY || "",
      ocrApiKey: c.ocrApiKey || c.NOVA_OCR_API_KEY || "",
      baseUrl: String(c.baseUrl || c.NOVA_BASE_URL || "https://nova.deloitte.com.cn/v1").replace(/\/$/, ""),
      ocrBaseUrl: String(c.ocrBaseUrl || c.NOVA_OCR_BASE_URL || "https://nova.deloitte.com.cn/nova-cloud/ocr/ocr/v1").replace(/\/$/, ""),
      model: c.model || c.NOVA_MODEL || "DeepSeek-V4-Flash"
    };
  }

  function saveNovaConfig(cfg) {
    var n = normalizeNova(cfg || {});
    global.CITF_NOVA = n;
    try { localStorage.setItem("CITF_NOVA", JSON.stringify(n)); } catch (e) {}
    return n;
  }

  function extractJsonObject(text) {
    var t = String(text || "").trim();
    if (t.charAt(0) === "{") return t;
    var a = t.indexOf("{");
    var b = t.lastIndexOf("}");
    return a >= 0 && b > a ? t.substring(a, b + 1) : t;
  }

  function chatCompletions(cfg, systemPrompt, userPrompt) {
    var url = cfg.baseUrl.indexOf("/chat/completions") >= 0 ? cfg.baseUrl : cfg.baseUrl + "/chat/completions";
    return fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: "Bearer " + cfg.apiKey
      },
      body: JSON.stringify({
        model: cfg.model,
        temperature: 0,
        messages: [
          { role: "system", content: systemPrompt },
          { role: "user", content: userPrompt }
        ]
      })
    }).then(function (r) {
      return r.text().then(function (t) {
        if (!r.ok) throw new Error("D.Nova Chat HTTP " + r.status + ": " + t.slice(0, 240));
        var data;
        try { data = JSON.parse(t); } catch (e) { throw new Error("D.Nova Chat bad JSON"); }
        var content = (((data.choices || [])[0] || {}).message || {}).content || "";
        if (!content) throw new Error("D.Nova Chat returned empty content");
        return extractJsonObject(content);
      });
    });
  }

  function dataUrlToBlob(dataUrl) {
    var parts = String(dataUrl || "").split(",");
    var meta = parts[0] || "";
    var b64 = parts[1] || parts[0] || "";
    var mime = (meta.match(/data:([^;]+)/) || [])[1] || "application/octet-stream";
    var bin = atob(b64);
    var arr = new Uint8Array(bin.length);
    for (var i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
    return new Blob([arr], { type: mime });
  }

  function ocrExtract(cfg, fileBase64, fileName) {
    if (!cfg.ocrApiKey) return Promise.reject(new Error("NOVA_OCR_API_KEY (sk-) missing – required for document prefill"));
    var blob = dataUrlToBlob(fileBase64);
    var fd = new FormData();
    fd.append("file", blob, fileName || "upload.pdf");
    fd.append("fileInfo", JSON.stringify({ apiKey: cfg.ocrApiKey, callbackFlag: "0" }));
    return fetch(cfg.ocrBaseUrl + "/callOcrFileParse", { method: "POST", body: fd }).then(function (r) {
      return r.text().then(function (t) {
        if (!r.ok) throw new Error("D.Nova OCR HTTP " + r.status + ": " + t.slice(0, 240));
        var text = tryOcrText(t);
        if (text) return text;
        var id = (t.match(/"(?:fileId|file_id|taskId|task_id|id)"\s*:\s*"?([^",}\s]+)/) || [])[1];
        if (!id) throw new Error("D.Nova OCR did not return text or task id");
        return pollOcr(cfg, id, 0);
      });
    });
  }

  function tryOcrText(raw) {
    if (!raw) return "";
    var keys = ["content", "text", "ocrText", "resultText", "markdown", "md"];
    for (var i = 0; i < keys.length; i++) {
      var re = new RegExp('"' + keys[i] + '"\\s*:\\s*"((?:\\\\.|[^"\\\\])*)"');
      var m = raw.match(re);
      if (m && m[1] && m[1].length > 20) {
        return m[1].replace(/\\n/g, "\n").replace(/\\"/g, '"').replace(/\\\\/g, "\\");
      }
    }
    if (raw.trim().charAt(0) !== "{" && raw.length > 40) return raw;
    return "";
  }

  function pollOcr(cfg, id, attempt) {
    if (attempt > 30) return Promise.reject(new Error("D.Nova OCR timed out"));
    return new Promise(function (resolve) { setTimeout(resolve, 1000); }).then(function () {
      return fetch(cfg.ocrBaseUrl + "/queryStatus/" + encodeURIComponent(id), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: "{}"
      }).then(function (r) {
        return r.text().then(function (t) {
          var text = tryOcrText(t);
          if (text) return text;
          var st = ((t.match(/"(?:status|state|taskStatus)"\s*:\s*"?([^",}\s]+)/) || [])[1] || "").toLowerCase();
          if (/fail|error|cancel/.test(st)) throw new Error("D.Nova OCR failed: " + t.slice(0, 200));
          return pollOcr(cfg, id, attempt + 1);
        });
      });
    });
  }

  function stripHtml(html) {
    var t = String(html || "");
    t = t.replace(/<script[\s\S]*?<\/script>/gi, " ").replace(/<style[\s\S]*?<\/style>/gi, " ").replace(/<[^>]+>/g, " ");
    return t.replace(/\s+/g, " ").trim();
  }

  function resolveDocumentText(cfg, req) {
    if (req.documentText && String(req.documentText).trim()) return Promise.resolve({ text: String(req.documentText), usedOcr: false });
    var pageUrl = (req.currentValues && (req.currentValues.pageUrl || req.currentValues.url)) || "";
    if (pageUrl) {
      return fetch(String(pageUrl), { credentials: "omit" }).then(function (r) {
        return r.text().then(function (html) {
          if (!r.ok) throw new Error("Webpage HTTP " + r.status);
          var text = stripHtml(html);
          if (!text || text.length < 40) throw new Error("Product webpage did not return readable text");
          return { text: text.slice(0, 12000), usedOcr: false };
        });
      });
    }
    if (!req.fileBase64) return Promise.resolve({ text: "", usedOcr: false });
    return ocrExtract(cfg, req.fileBase64, req.fileName).then(function (text) {
      return { text: text, usedOcr: true };
    });
  }

  function novaDirectExecute(req) {
    var cfg = novaConfig();
    if (!cfg || !cfg.apiKey) return Promise.reject(new Error("D.Nova Chat key missing. Set CITF_NOVA (apiKey ak- + ocrApiKey sk-)."));

    var action = String(req.action || "").toLowerCase();
    var schemaKey = req.schemaKey;
    var current = req.currentValues || {};
    var keys = SCHEMA_KEYS[schemaKey] || [];
    var hash = req.contentHash || shaLite(JSON.stringify({ a: action, s: schemaKey, f: (req.fileBase64 || "").length, d: req.documentText || "", c: current }));

    if (action === ACTIONS.ValidateForm) {
      return chatCompletions(
        cfg,
        "Return JSON only with pass and issues array for CITF quality checking.",
        "Count words in the project objective. If fewer than 12 words use code too_brief; if more than 250 words use code too_long. The filled objective MUST sit in that min/max range. Also check benefits/implementation and that each item plan has a quantitative detail. Data: " + JSON.stringify(current)
      ).then(function (raw) {
        var parsed = {};
        try { parsed = JSON.parse(raw); } catch (e) { parsed = {}; }
        var issues = Array.isArray(parsed.issues) ? parsed.issues : [];
        var hard = validateFunding(current);
        hard.forEach(function (h) {
          if (!issues.some(function (i) { return i.field === h.field && i.code === h.code; })) issues.push(h);
        });
        return {
          pass: !issues.some(function (i) { return i.severity === "error"; }),
          source: "nova-compatible",
          model: cfg.model,
          fields: current,
          issues: issues,
          notes: issues.length ? "AI quality issues found" : "AI completeness check passed",
          contentHash: hash
        };
      });
    }

    if (action === ACTIONS.Suggest) {
      var q = String(current.query || req.documentText || "");
      return chatCompletions(cfg, "Suggest up to 3 CITF pre-approved product codes/names as JSON {suggestions:[{code,name}]}.", "User need: " + q).then(function (raw) {
        var parsed = {};
        try { parsed = JSON.parse(raw); } catch (e) { parsed = {}; }
        return {
          pass: true,
          source: "nova-compatible",
          model: cfg.model,
          fields: { suggestionsJson: JSON.stringify(parsed.suggestions || []) },
          issues: [],
          notes: "AI suggestions",
          contentHash: hash
        };
      });
    }

    if (action === ACTIONS.Summarize) {
      return chatCompletions(
        cfg,
        "Return JSON only with summary, score and risks for a CITF funding submission.",
        "Write a 120-180 word factual submission summary. JSON {summary, score, risks:[]}. Data: " + JSON.stringify(current)
      ).then(function (raw) {
        var parsed = {};
        try { parsed = JSON.parse(raw); } catch (e) { parsed = {}; }
        return {
          pass: true,
          source: "nova-compatible",
          model: cfg.model,
          fields: {
            summary: parsed.summary || "",
            score: String(parsed.score || 80),
            risksJson: JSON.stringify(parsed.risks || [])
          },
          issues: [],
          notes: parsed.summary || "AI submission summary",
          contentHash: hash
        };
      });
    }

    return resolveDocumentText(cfg, req).then(function (doc) {
      if (!doc.text || !String(doc.text).trim()) {
        throw new Error(req.fileBase64 ? "OCR returned empty text" : "Upload a PDF/image or provide documentText");
      }
      var system = action === ACTIONS.ValidateUpload && schemaKey === SCHEMAS.quotation_check
        ? "Extract quotation supplier, product, qty, unit price and total amount. Return JSON only."
        : action === ACTIONS.ValidateUpload
          ? "Assess document readability for CITF upload checks. Return JSON with docType, readable (true/false), note."
          : "You extract CITF portal form fields. Return a flat JSON object only.";
      var user = action === ACTIONS.ValidateUpload && schemaKey !== SCHEMAS.quotation_check
        ? "Document type: " + (req.docType || schemaKey) + "\nText sample:\n" + String(doc.text).slice(0, 4000)
        : "Extract fields. Return ONLY JSON with keys: " + keys.join(", ") + ". Empty string if unknown.\nDocument:\n" + String(doc.text).slice(0, 12000)
          + (schemaKey === SCHEMAS.quotation_check ? "\nForm amount to compare: " + (current.formAmount || "") : "");

      return chatCompletions(cfg, system, user).then(function (raw) {
        var fields = {};
        try { fields = JSON.parse(raw); } catch (e) { fields = {}; }
        var issues = [];
        var notes = "Document read (" + (doc.usedOcr ? "nova-ocr+llm" : "nova-compatible") + ")";
        if (action === ACTIONS.ValidateUpload && schemaKey === SCHEMAS.quotation_check) {
          if (current.formAmount) fields.formAmount = String(current.formAmount);
          issues = validateQuotation(fields);
          notes = issues.length ? "Quotation flagged" : "Quotation matches the form";
        } else if (action === ACTIONS.ValidateUpload) {
          if (!fields.docType) fields.docType = req.docType || schemaKey;
          if (String(fields.readable) === "false") {
            issues = [{ field: "readable", code: "blurred", message: fields.note || "Unreadable", severity: "error" }];
          }
          notes = fields.note || notes;
        }
        return {
          pass: !issues.some(function (i) { return i.severity === "error"; }),
          source: doc.usedOcr ? "nova-ocr+llm" : "nova-compatible",
          model: cfg.model,
          fields: fields,
          issues: issues,
          notes: notes,
          contentHash: hash
        };
      });
    });
  }

  function portalToken() {
    return new Promise(function (resolve) {
      try {
        var input = document.querySelector("input[name='__RequestVerificationToken']");
        if (input && input.value) { resolve(input.value); return; }
      } catch (e0) {}
      try {
        if (global.shell && typeof global.shell.getTokenDeferred === "function") {
          global.shell.getTokenDeferred().done(function (t) { resolve(t || ""); }).fail(function () { resolve(""); });
          return;
        }
      } catch (e1) {}
      resolve("");
    });
  }

  function liveExecute(req) {
    // Power Pages Web API cannot call unbound actions (404). Write a job row;
    // a pre-create plugin calls D.Nova and returns the result on the same record.
    var payload = {
      cicapos_name: "AI " + new Date().toISOString().slice(0, 19),
      cicapos_action: req.action || "",
      cicapos_schemakey: req.schemaKey || "",
      cicapos_doctype: req.docType || "",
      cicapos_documenttext: req.documentText || "",
      cicapos_filename: req.fileName || "",
      cicapos_filebase64: req.fileBase64 || "",
      cicapos_currentvaluesjson: JSON.stringify(req.currentValues || {}),
      cicapos_contenthash: req.contentHash || ""
    };
    var url = "/_api/cicapos_aiassistjobs";
    if (global.Xrm && global.Xrm.Utility && global.Xrm.Utility.getGlobalContext) {
      try {
        var ver = global.Xrm.Utility.getGlobalContext().getVersion().split(".");
        url = "/api/data/v" + ver[0] + "." + ver[1] + "/cicapos_aiassistjobs";
      } catch (e) {}
    }
    return portalToken().then(function (token) {
    var headers = {
        "Content-Type": "application/json; charset=utf-8",
        Accept: "application/json",
        Prefer: "return=representation",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
    };
    if (token) headers["__RequestVerificationToken"] = token;
    return fetch(url, {
      method: "POST",
      headers: headers,
      credentials: "same-origin",
      body: JSON.stringify(payload)
    });
    }).then(function (r) {
      if (!r.ok) return r.text().then(function (t) { throw new Error("AI API HTTP " + r.status + (t ? " " + t.slice(0, 180) : "")); });
      return r.json();
    }).then(function (data) {
      var fields = {};
      var issues = [];
      try { fields = JSON.parse(data.cicapos_fieldsjson || data.fieldsjson || "{}"); } catch (e1) {}
      try { issues = JSON.parse(data.cicapos_issuesjson || data.issuesjson || "[]"); } catch (e2) {}
      return {
        pass: !!(data.cicapos_pass != null ? data.cicapos_pass : data.pass),
        source: data.cicapos_source || data.source || "nova",
        model: data.cicapos_model || data.model || null,
        fields: fields,
        issues: issues,
        notes: data.cicapos_notes || data.notes || "",
        contentHash: data.cicapos_contenthash || data.contentHash || ""
      };
    });
  }

  function isPortalHost() {
    try {
      return /powerappsportals\.com$/i.test(location.hostname || "") || /dynamics\.com$/i.test(location.hostname || "");
    } catch (e) { return false; }
  }

  function isCorsOrNetwork(err) {
    var msg = String((err && err.message) || err || "").toLowerCase();
    return /failed to fetch|load failed|networkerror|access-control|cors|not allowed by access-control/i.test(msg);
  }

  function assist(req) {
    req = req || {};
    var forceDemo = /(?:^|[?&])ai=demo(?:&|$)/.test(location.search || "") || global.CITF_AI_DEMO === true;
    if (forceDemo) return localExecute(req);

    // Power Pages: browser→Nova is CORS-blocked. Prefer same-origin Custom API (plugin calls Nova server-side).
    // Elsewhere: Nova direct first, then Custom API on CORS/network failure.
    var preferApi = isPortalHost() || global.CITF_AI_PREFER_API === true;
    var allowApiFallback = global.CITF_AI_FALLBACK_API !== false; // default ON

    function viaApi() {
      return liveExecute(req);
    }
    function viaNova() {
      if (!novaConfig()) return Promise.reject(new Error("D.Nova Chat key missing"));
      return novaDirectExecute(req);
    }
    function fail(err) {
      var msg = (err && err.message) || String(err);
      throw new Error(
        "Live AI failed (" + msg + "). Portal needs Custom API cicapos_AiAssist (server-side Nova) because browser→nova.deloitte.com.cn is CORS-blocked. Or set ?ai=demo."
      );
    }

    if (preferApi) {
      return viaApi().catch(function (apiErr) {
        if (!novaConfig()) throw apiErr;
        return viaNova().catch(function (novaErr) {
          if (allowApiFallback && isCorsOrNetwork(novaErr)) throw apiErr;
          throw novaErr;
        }).catch(fail);
      });
    }

    if (novaConfig()) {
      return viaNova().catch(function (err) {
        if (allowApiFallback && (isCorsOrNetwork(err) || global.CITF_AI_FALLBACK_API === true)) {
          return viaApi().catch(function () { throw err; });
        }
        throw err;
      }).catch(fail);
    }
    return viaApi().catch(fail);
  }

  /** Open a file picker and return { fileName, fileBase64 (data URL), mime } or null if cancelled. */
  function pickFile(accept) {
    return new Promise(function (resolve, reject) {
      var input = document.createElement("input");
      input.type = "file";
      input.accept = accept || ".pdf,.png,.jpg,.jpeg,.tif,.tiff,.bmp,.webp";
      input.style.display = "none";
      document.body.appendChild(input);
      var done = false;
      function cleanup() {
        if (input.parentNode) input.parentNode.removeChild(input);
      }
      input.onchange = function () {
        done = true;
        var file = input.files && input.files[0];
        cleanup();
        if (!file) { resolve(null); return; }
        var reader = new FileReader();
        reader.onload = function () {
          resolve({ fileName: file.name, fileBase64: String(reader.result || ""), mime: file.type || "" });
        };
        reader.onerror = function () { reject(reader.error || new Error("read failed")); };
        reader.readAsDataURL(file);
      };
      // Some browsers fire focus back without change when cancelled.
      setTimeout(function () {
        window.addEventListener("focus", function onFocus() {
          window.removeEventListener("focus", onFocus);
          setTimeout(function () {
            if (!done) { cleanup(); resolve(null); }
          }, 400);
        }, { once: true });
      }, 0);
      input.click();
    });
  }

  function issueMessages(issues) {
    return (issues || []).filter(function (i) { return i.severity !== "warn"; }).map(function (i) { return i.message; });
  }

  // Live OCR+Chat is the default (no local demo unless ?ai=demo).
  if (global.CITF_AI_LIVE == null) global.CITF_AI_LIVE = true;

  global.CitfAi = {
    ACTIONS: ACTIONS,
    SCHEMAS: SCHEMAS,
    assist: assist,
    pickFile: pickFile,
    novaConfig: novaConfig,
    saveNovaConfig: saveNovaConfig,
    words: words,
    issueMessages: issueMessages,
    validateFundingLocal: validateFunding,
    clearCache: function () { cache = Object.create(null); }
  };
})(typeof window !== "undefined" ? window : globalThis);
