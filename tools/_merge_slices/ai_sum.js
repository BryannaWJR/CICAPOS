function normalizeAiRisks(risks) {
  return (risks || []).map(function (r) {
    if (r == null || r === '') return null;
    if (typeof r === 'object' && !Array.isArray(r)) {
      const raw = String(r.severity || r.level || r.type || r.sev || '');
      const sev = /err/i.test(raw) ? 'error' : /^tip$/i.test(raw) ? 'tip' : 'warn';
      const text = String(r.text || r.message || r.flag || '').trim();
      return text ? { sev: sev, text: text } : null;
    }
    const s = String(Array.isArray(r) ? r.join(' – ') : r).trim();
    if (!s) return null;
    if (/^error\s*:/i.test(s)) return { sev: 'error', text: s.replace(/^error\s*:\s*/i, '') };
    if (/^tip\s*:/i.test(s)) return { sev: 'tip', text: s.replace(/^tip\s*:\s*/i, '') };
    if (/^warn(ing)?\s*:/i.test(s)) return { sev: 'warn', text: s.replace(/^warn(ing)?\s*:\s*/i, '') };
    return { sev: 'warn', text: s };
  }).filter(Boolean);
}
function sanitizeAiSummary(text) {
  var s = String(text || '');
  s = s.replace(/\[\s*\{[\s\S]*?\}\s*\]/g, ' ');
  s = s.replace(/\{[^{}]{6,}\}/g, ' ');
  s = s.replace(/[\[\]{}]+/g, ' ');
  s = s.replace(/"[A-Za-z][A-Za-z0-9_]*"\s*:/g, ' ');
  return s.split(/\n+/).map(function (line) {
    line = String(line || '').replace(/\s+/g, ' ').replace(/^[\]\},;:\s]+/, '').replace(/\s*Items:\s*$/, '').trim();
    line = line.replace(/\s*AI text analysis completed[^.。]*[.。]?/gi, '').replace(/\s*a CIC officer still reviews[^.。]*[.。]?/gi, '').trim();
    if (line.length < 8) return '';
    if (/^[\[\{]|itemsJson|palJson|"paCode"/.test(line)) return '';
    if (/AI text analysis completed|officer still reviews|AI analysis (is |was )?(ok|complete)/i.test(line)) return '';
    return line;
  }).filter(Boolean).join('\n');
}
function summaryPoints(text) {
  const raw = sanitizeAiSummary(text);
  if (!raw) return [];
  const strip = function (s) { return String(s || '').replace(/^\s*\d+[.)、．]\s*/, '').replace(/\s*—\s*Warn:.*/i, '').replace(/\s*—\s*Tip.*/i, '').trim(); };
  if (/\n/.test(raw)) {
    const lines = raw.split(/\n+/).map(strip).filter(function (s) { return s.length > 6 && !/^[\[\{]/.test(s); });
    if (lines.length > 1) return lines;
  }
  const numbered = raw.split(/(?=\s*\d+[.)、．]\s+)/).map(strip).filter(function (s) { return s.length > 6; });
  if (numbered.length > 1) return numbered;
  const zh = raw.split(/[。！？；](?:\s+|$)/).map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 10; });
  if (zh.length > 1) return zh;
  return raw.split(/(?<=[.!?。])\s+(?=[A-Z“"「])/).map(function (s) { return s.trim(); }).filter(Boolean);
}
function attachRisksToPoints(pts, risks) {
  const stop = { the:1, and:1, for:1, with:1, this:1, that:1, from:1, will:1, may:1, are:1, is:1, to:1, of:1, a:1, an:1, in:1, on:1, at:1, by:1, or:1, be:1, as:1, it:1, its:1, may:1, can:1, not:1 };
  const rows = (pts || []).map(function (p) { return { text: p, risks: [] }; });
  (risks || []).forEach(function (r) {
    const toks = String(r.text || '').toLowerCase().match(/[a-z\u4e00-\u9fff]{3,}|\d+/g) || [];
    let best = -1, bestScore = 0;
    rows.forEach(function (row, i) {
      const hay = String(row.text || '').toLowerCase();
      let score = 0;
      toks.forEach(function (t) {
        if (stop[t]) return;
        if (hay.indexOf(t) >= 0) score += /\d/.test(t) ? 8 : Math.min(t.length, 10);
      });
      if (score > bestScore) { bestScore = score; best = i; }
    });
    if (best >= 0 && bestScore >= 6) rows[best].risks.push(r);
    else if (r.sev === 'tip') rows.push({ text: r.text, risks: [] });
    else if (rows.length) rows[0].risks.push(r);
    else rows.push({ text: '', risks: [r] });
  });
  return rows;
}
function renderAiRiskInline(r) {
  const prefix = r.sev === 'error'
    ? (typeof locText === 'function' ? locText('Error:') : 'Error:')
    : r.sev === 'tip'
      ? (typeof locText === 'function' ? locText('Tip:') : 'Tip:')
      : (typeof locText === 'function' ? locText('Warn:') : 'Warn:');
  const color = r.sev === 'error' ? 'var(--err)' : r.sev === 'tip' ? '#0F6CBD' : '#C47B00';
  const body = typeof locText === 'function' ? locText(r.text) : r.text;
  return ` <span style="color:${color};font-weight:600">— ${esc(prefix)} ${esc(body)}</span>`;
}
function paintSettingMismatch(shown) {
  const s = String(shown || '');
  const item = /^(?:Item \d+ .+ does not meet (?:indoor|outdoor) needs\.?|項目 \d+ .+不符合(?:室內|室外)需要。?|项目 \d+ .+不符合(?:室内|室外)需要。?)$/;
  if (item.test(s)) return `<span style="color:#C47B00;font-weight:600">${esc(s)}</span>`;
  const clause = /(Requested items do not all suit (?:indoor|outdoor) use\.?|所申請物品並非全部符合(?:室內|室外)需要。?|所申请物品并非全部符合(?:室内|室外)需要。?)/;
  const m = s.match(clause);
  if (!m) return esc(s);
  const i = m.index;
  return esc(s.slice(0, i)) + `<span style="color:#C47B00;font-weight:600">${esc(m[0])}</span>` + esc(s.slice(i + m[0].length));
}
function renderAiSummary(text, risks, score) {
  const pts = summaryPoints(text);
  const rows = attachRisksToPoints(pts, normalizeAiRisks(risks));
  if (!rows.length) return '';
  return `<ol style="margin:0;padding-left:22px;font-size:14px">${rows.map(function (row) {
    const shown = typeof locText === 'function' ? locText(row.text) : row.text;
    const isPalTip = /Function and price comparison/i.test(shown);
    const flags = (row.risks || []).filter(function (r) {
      if (!r) return false;
      if (isPalTip && r.sev === 'tip') return false;
      return r.sev === 'error' || r.sev === 'warn' || r.sev === 'tip';
    });
    const tail = isPalTip
      ? ` <span style="color:#0F6CBD;font-weight:600">— ${esc(typeof locText === 'function' ? locText('Tip') : 'Tip')}</span>`
      : flags.map(function (r) {
        return renderAiRiskInline({ sev: r.sev, text: typeof locText === 'function' ? locText(r.text) : r.text });
      }).join('');
    return `<li style="margin:0 0 8px">${paintSettingMismatch(shown)}${tail}</li>`;
  }).join('')}</ol>`;
}

