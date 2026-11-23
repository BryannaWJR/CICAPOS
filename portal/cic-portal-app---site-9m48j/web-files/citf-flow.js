(function () {
  if (location.pathname.toLowerCase().indexOf("/flow") !== 0) return;
  document.body.classList.add("citf-flow");

  var money = function (n) {
    n = Math.round(+n || 0);
    return "HK$" + n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ",");
  };
  var esc = function (s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  };
  var pill = function (t) {
    var ok = ["Verified", "Approved", "Paid", "Listed", "Finance signed off", "Endorsed"];
    var info = ["Submitted", "Vetting in progress", "Under review", "Pending committee", "DR approved – pending transfer", "Awaiting CIC"];
    var warn = ["Pending Vetting", "Pending information", "Pending endorsement", "Pending approval", "Reply needed", "Awaiting applicant"];
    var err = ["Rejected", "High"];
    var k = ok.indexOf(t) >= 0 ? "ok" : info.indexOf(t) >= 0 ? "info" : warn.indexOf(t) >= 0 ? "warn" : err.indexOf(t) >= 0 ? "err" : "";
    var label = t === "Vetting in progress" || t === "Pending Vetting" || t === "Vetting" ? "Under review" : t === "In review" ? "In review" : t;
    return '<span class="pill ' + k + '">' + esc(label) + "</span>";
  };

  var state = {
    checks: { a46: {} },
    apps: {},
    drs: {},
    toast: ""
  };

  var companies = {
    hb: {
      name: "Harbour Build Engineering Limited",
      zh: "海港建築工程有限公司",
      type: "Main contractor",
      br: "61234567",
      expiry: "2 Jan 2027",
      id: "CIC-C-002301",
      status: "Verified",
      address: "18/F, Harbour Centre, 25 Harbour Road, Wan Chai",
      web: "https://harbourbuild.example.com",
      contact: "Carmen Lau",
      title: "Project Director",
      email: "carmen.lau@harbourbuild.example.com",
      phone: "2811 2233",
      mobile: "9123 4567",
      bank: "004 HSBC · ••••89001",
      levy: "Levy-paying contractor (DMP match)",
      users: [
        ["Carmen Lau", "carmen.lau@harbourbuild.example.com", "Company Admin", "CITF, Pre-approved List"],
        ["Kenny Wong", "kenny.wong@harbourbuild.example.com", "Company User", "CITF"]
      ]
    },
    kmic: {
      name: "Kowloon MiC Solutions Limited",
      zh: "九龍組裝合成方案有限公司",
      type: "Specialist subcontractor",
      br: "72345678",
      expiry: "31 May 2027",
      id: "Not issued yet",
      status: "Pending Vetting",
      address: "Unit 9, 12/F, Kwun Tong Industrial Centre, Kwun Tong",
      web: "https://kmic.example.com",
      contact: "Ivan Cheng",
      title: "Director",
      email: "ivan.cheng@kmic.example.com",
      phone: "2344 5566",
      mobile: "6234 5678",
      bank: "Not provided",
      levy: "Not verified",
      risk: "Medium",
      ai: "Company name on BR differs by one word (“Solution” / “Solutions”). BR expiry is valid.",
      users: [["Ivan Cheng", "ivan.cheng@kmic.example.com", "Company Admin", "Waiting for CompanyID"]]
    },
    ss: {
      name: "SmartSite Robotics HK Limited",
      zh: "智地機械人香港有限公司",
      type: "Materials or product supplier",
      br: "53456789",
      expiry: "15 Aug 2027",
      id: "CIC-C-002288",
      status: "Verified",
      address: "Unit 501, Building 19W, Science Park, Sha Tin",
      web: "https://smartsite.example.com",
      contact: "Grace Ho",
      title: "Sales Manager",
      email: "grace.ho@smartsite.example.com",
      phone: "3555 1200",
      mobile: "9555 1200",
      bank: "Not used for grants",
      levy: "Vendor",
      users: [["Grace Ho", "grace.ho@smartsite.example.com", "Company Admin", "Pre-approved List"]]
    }
  };

  var apps = [
    { id: "a46", ref: "CITF-2027-00046", co: "hb", schemes: "ACT", project: "Tung Chung New Town Extension – Site 3", amount: 764000, grant: 534800, status: "Vetting in progress", officer: "Mandy Chan", pledge: "30 days", priority: "High" },
    { id: "a48", ref: "CITF-2027-00048", co: "hb", schemes: "BIM", project: "Kai Tak Sports Park Phase 2", amount: 353000, grant: 247100, status: "Pending committee", officer: "Mandy Chan", pledge: "Passed", priority: "Normal" },
    { id: "a31", ref: "CITF-2027-00031", co: "hb", schemes: "ACT", project: "Kai Tak Sports Park Phase 2", amount: 258000, grant: 180600, status: "Approved", officer: "Tony Lee", pledge: "Decided", priority: "Normal" },
    { id: "a49", ref: "CITF-2027-00049", co: "hb", schemes: "BIM", project: "Kai Tak Sports Park Phase 2", amount: 136000, grant: 95200, status: "Pending endorsement", officer: "", pledge: "–", priority: "Normal" },
    { id: "a50", ref: "CITF-2027-00050", co: "hb", schemes: "MiC", project: "Tung Chung New Town Extension – Site 3", amount: 0, grant: 0, status: "Draft", officer: "", pledge: "–", priority: "Normal" }
  ];
  var drs = [
    { id: "dr1", ref: "DR-2027-00113", app: "a31", type: "First instalment", claim: 144480, status: "Submitted", officer: "Winnie Ho" }
  ];
  var queue = [
    ["Lantau Marine Works Limited", "Main contractor", "New – unassigned", "High", "Today"],
    ["Kowloon MiC Solutions Limited", "Specialist subcontractor", "In review", "Medium", "2 days"],
    ["Sai Kung Scaffolding Company Limited", "Specialist subcontractor", "New – unassigned", "Low", "1 day"],
    ["Apex BIM Consultants Limited", "Consultant", "In review", "Low", "3 days"],
    ["Shatin Precast Components Limited", "Materials or product supplier", "Awaiting information (RFI)", "Medium", "9 days"],
    ["Golden Bay Engineering Limited", "Main contractor", "In review", "High", "6 days"]
  ];
  var pal = [
    ["PA-ACT-0412", "SiteEye AI Safety Camera System (4S)", "ACT", "HK$86,000", "Listed", "SmartSite Robotics HK Limited"],
    ["PA-ACT-0398", "RoboLayout Total-station Robot", "ACT", "HK$238,000", "Listed", "SmartSite Robotics HK Limited"],
    ["PA-BIM-0105", "BIM Authoring Suite (1-year licence)", "BIM", "HK$24,500", "Listed", "Asia Digital Construction Ltd"]
  ];

  var roles = {
    "company-admin": { name: "Carmen Lau", title: "Company Admin", area: "portal", co: "hb", home: "company" },
    "company-user": { name: "Kenny Wong", title: "Company User", area: "portal", co: "hb", home: "endorse" },
    vendor: { name: "Grace Ho", title: "Vendor · Company Admin", area: "portal", co: "ss", home: "pal" },
    pending: { name: "Ivan Cheng", title: "Company Admin", area: "portal", co: "kmic", home: "pending" },
    "funding-officer": { name: "Mandy Chan", title: "Funding Application Review Officer", area: "d365", home: "cases" },
    "pal-officer": { name: "Kelly Lau", title: "PAL Application Review Officer", area: "d365", home: "palreq" },
    "disbursement-officer": { name: "Winnie Ho", title: "Disbursement Review Officer", area: "d365", home: "drs" },
    approver: { name: "Peter Yip", title: "Approver / Reviewer", area: "d365", home: "approve" },
    finance: { name: "Eric Lam", title: "Finance Department", area: "d365", home: "pay" },
    admin: { name: "Admin", title: "Administrator", area: "d365", home: "security" }
  };

  function role() {
    var id = new URLSearchParams(location.search).get("role") || "company-admin";
    return roles[id] ? Object.assign({ id: id }, roles[id]) : Object.assign({ id: "company-admin" }, roles["company-admin"]);
  }
  function view() {
    var h = (location.hash || "").replace("#", "");
    return h || role().home;
  }
  function go(v) {
    location.hash = v;
  }
  function appStatus(a) {
    return state.apps[a.id] || a.status;
  }
  function drStatus(d) {
    return state.drs[d.id] || d.status;
  }
  function toast(msg) {
    state.toast = msg;
    render();
    setTimeout(function () {
      state.toast = "";
      var t = document.getElementById("citf-toast");
      if (t) t.remove();
    }, 2400);
  }

  function demoBar(r) {
    var opts = Object.keys(roles).map(function (id) {
      var x = roles[id];
      return '<option value="' + id + '"' + (id === r.id ? " selected" : "") + ">" + esc(x.name) + " · " + esc(x.title) + "</option>";
    }).join("");
    return '<div class="demo" role="region" aria-label="Demo controls"><b>CIC APOS · interactive prototype</b>' +
      '<span class="seg"><a href="/" class="">Portal sign-in</a><a href="/flow/?role=' + encodeURIComponent(r.id) + "#" + encodeURIComponent(r.home) + '" class="on">' + (r.area === "portal" ? "Portal flow" : "Back office flow") + "</a></span>" +
      '<span class="sp"></span><label class="who">Signed in as <select data-act="switchRole">' + opts + "</select></label>" +
      '<a class="btn sm ghost" href="/" style="color:#F5F7FA;border-color:#3A4A5E">All demo accounts</a></div>';
  }

  function portalHead(r, tab) {
    var items = r.id === "vendor"
      ? [["pal", "Pre-approved List"], ["messages", "Messages"]]
      : r.id === "pending"
        ? [["pending", "My Company"], ["track", "Track registration"]]
        : [["company", "My Company"], ["citf", "CITF Applications"], ["messages", "Messages"]];
    if (r.id === "company-user") items.splice(1, 0, ["endorse", "Endorsement"]);
    return demoBar(r) + '<header class="p-head"><div class="in"><a class="logo" href="/"><span class="mark">LOGO</span><span class="logo-text"><b>CIC Company Portal</b><small>CITF Application Portal &amp; Pre-approved List · Power Pages</small></span></a><nav class="p-nav">' +
      items.map(function (it) {
        return '<a href="#' + it[0] + '" class="' + (tab === it[0] ? "on" : "") + '">' + it[1] + "</a>";
      }).join("") +
      '<span class="who">| ' + esc(r.name) + "</span>" +
      '<a href="/">Sign out</a></nav></div></header>';
  }

  function d365Shell(r, on, body) {
    var links = [
      ["cases", "Funding Applications"],
      ["case", "Open case"],
      ["queue", "Company Registrations"],
      ["drs", "Disbursement Requests"],
      ["pay", "Payments (Finance)"],
      ["palreq", "PAL Requests"],
      ["approve", "Approvals"],
      ["security", "Security and Governance"]
    ];
    var initials = r.name.split(" ").map(function (p) { return p[0]; }).join("").slice(0, 2);
    return demoBar(r) + '<div class="u"><header class="u-top"><span class="u-waffle">▦</span><span class="brand">Dynamics 365</span><span class="app">CITF Operation Platform</span><span class="search">⌕ Search records</span><span class="who">' + esc(r.name) + " (" + esc(r.title) + ')</span><span class="u-av">' + esc(initials) + '</span></header><div class="u-wrap"><nav class="u-map"><div class="grp">This role</div>' +
      links.map(function (it) {
        return '<a href="#' + it[0] + '" class="' + (on === it[0] ? "on" : "") + '">' + it[1] + "</a>";
      }).join("") +
      '</nav><main class="u-main"><div class="u-cmd"><a class="btn sm ghost" href="/">← Demo sign-in</a></div><div class="u-body"><div class="p-main">' + body + "</div></div></main></div></div>";
  }

  function companyCard(c, ro) {
    return '<div class="row between"><div><h1>' + esc(c.name) + '</h1><p class="sub">' + esc(c.zh) + " · " + esc(c.type) + "</p></div>" + pill(c.status) + "</div>" +
      (c.status !== "Verified" ? '<div class="note warn">Your company is <b>' + esc(c.status) + "</b>. CIC services unlock once a CIC Officer verifies the registration.</div>" : "") +
      '<div class="kpis"><div class="kpi"><small>CIC CompanyID</small><b style="font-size:20px">' + esc(c.id) + '</b></div><div class="kpi"><small>Active users</small><b>' + c.users.length + '</b></div><div class="kpi"><small>Profile completeness</small><b>100%</b></div><div class="kpi"><small>BR expiry</small><b style="font-size:20px">' + esc(c.expiry) + "</b></div></div>" +
      (c.status === "Verified" ? '<h2>Your CIC services</h2><div class="grid g2"><a class="card row" href="#citf"><span class="kicker">▣</span><div><h3>CITF Application Portal</h3><span class="sub">Apply for funding, claim disbursements, Q&amp;A</span></div></a><a class="card row" href="#messages"><span class="kicker">▣</span><div><h3>Messages</h3><span class="sub">Questions on your cases</span></div></a></div>' : "") +
      '<div class="card"><h2>Company profile</h2><div class="kv"><span>Legal name</span><span>' + esc(c.name) + "</span><span>BR number</span><span>" + esc(c.br) + "</span><span>Address</span><span>" + esc(c.address) + "</span><span>Contact</span><span>" + esc(c.contact) + " · " + esc(c.email) + "</span><span>Telephone</span><span>" + esc(c.phone) + "</span><span>Bank</span><span>" + esc(c.bank) + "</span><span>Eligibility</span><span>" + esc(c.levy) + "</span></div>" +
      (ro ? '<span class="hint">Only the Company Admin can edit.</span>' : '<button class="btn pri" type="button" data-act="save">Save changes</button>') + "</div>" +
      '<div class="card"><h2>Users and application access</h2><div class="scroll"><table class="tbl"><thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Access</th></tr></thead><tbody>' +
      c.users.map(function (u) { return "<tr><td>" + esc(u[0]) + "</td><td>" + esc(u[1]) + "</td><td>" + esc(u[2]) + "</td><td>" + esc(u[3]) + "</td></tr>"; }).join("") +
      "</tbody></table></div></div>";
  }

  function citfPage(r) {
    var mine = apps.filter(function (a) { return a.co === "hb"; });
    var rows = mine.map(function (a) {
      return "<tr><td><a href=\"#case\">" + esc(a.ref) + "</a></td><td>" + esc(a.schemes) + "</td><td>" + esc(a.project) + "</td><td>" + money(a.amount) + "</td><td>" + pill(appStatus(a)) + "</td><td>" + esc(a.pledge) + "</td></tr>";
    }).join("");
    return '<div class="row between"><div><h1>Welcome back, ' + esc(companies.hb.name) + '</h1><p class="sub">Here is where your CITF applications stand today · ' + esc(companies.hb.id) + '</p></div>' +
      (r.id === "company-admin" ? '<a class="btn pri" href="/funding-application/">+ New funding application</a>' : "") + "</div>" +
      '<div class="kpis"><div class="kpi"><small>Active applications</small><b>3</b></div><div class="kpi"><small>Claims in progress</small><b>1</b></div><div class="kpi"><small>Need your action</small><b style="color:var(--warn)">1</b></div><div class="kpi"><small>Remaining ACT + BIM cap</small><b style="font-size:20px">HK$6,965,200</b></div></div>' +
      '<div class="card"><h2>ACT + BIM funding cap</h2><div class="bar"><i style="width:7%"></i></div><div class="row" style="font-size:13px"><span>Approved ' + money(180600) + "</span><span>Provisional (in review) " + money(781900) + "</span><b>Remaining HK$6,965,200 of HK$7,500,000</b></div></div>" +
      '<div class="card"><h2>My applications</h2><div class="scroll"><table class="tbl"><thead><tr><th>Application</th><th>Schemes</th><th>Project</th><th>Amount applied</th><th>Status</th><th>Pledged result</th></tr></thead><tbody>' + rows + "</tbody></table></div></div>" +
      '<div class="card"><h2>Disbursement requests</h2><div class="scroll"><table class="tbl"><thead><tr><th>DR</th><th>Application</th><th>Type</th><th>Claimed</th><th>Status</th></tr></thead><tbody><tr><td><a href="#dr">DR-2027-00113</a></td><td>CITF-2027-00031</td><td>First instalment</td><td>' + money(144480) + "</td><td>" + pill(drStatus(drs[0])) + "</td></tr></tbody></table></div></div>";
  }

  function casePage(who) {
    var a = apps[0];
    var st = appStatus(a);
    var checks = ["Applicant eligibility verified (DMP / lists)", "Quotation count by value and lowest bid, or justification", "Connected-party / double funding check", "Product on Pre-approved List or catalogue attached", "Implementation plan meets CITF objectives"];
    var done = state.checks.a46;
    var all = checks.every(function (_, i) { return done[i]; });
    var officer = who === "officer" || who === "approver";
    return '<div class="row between"><div><h1>' + esc(a.ref) + "</h1><p class=\"sub\">" + esc(companies.hb.name) + " · " + esc(a.project) + "</p></div>" + pill(st) + "</div>" +
      '<ol class="steps"><li class="done"><i></i>Submitted</li><li class="' + (st === "Approved" ? "done" : "cur") + '"><i></i>Review</li><li class="' + (st === "Pending approval" ? "cur" : st === "Approved" ? "done" : "") + '"><i></i>Approval</li><li class="' + (st === "Approved" ? "done" : "") + '"><i></i>Result</li></ol>' +
      (st === "Approved" ? "" : '<div class="note info">AI review has checked this application. A CIC officer reviews those findings. AI review does not approve the application.</div>') +
      '<div class="card"><h2>Items</h2><div class="scroll"><table class="tbl"><thead><tr><th>Item</th><th>Qty</th><th>Amount</th><th>Note</th></tr></thead><tbody>' +
      "<tr><td>SiteEye AI Safety Camera System (4S)<br><small>PA-ACT-0412 · pre-approved</small></td><td>4</td><td>" + money(344000) + "</td><td>" + pill("Listed") + "</td></tr>" +
      "<tr><td>AutoRebar Tying Robot<br><small>Non-pre-approved</small></td><td>1</td><td>" + money(420000) + '</td><td><span class="pill warn">Quotation is HK$402,000</span></td></tr>' +
      "</tbody></table></div></div>" +
      '<div class="card"><h2>Copilot case summary</h2><p class="sub" style="margin:0">Main contractor applying under ACT for 4 SiteEye cameras (HK$344,000) and 1 AutoRebar robot (HK$420,000). The robot quotation is HK$18,000 below the form. Related company Harbour Holdings has no open CITF case.</p><p><b>Eligibility pre-screen 82</b>/100</p></div>' +
      (officer ? '<div class="card"><h2>Review checklist</h2><p class="sub">AI review has already run. These checks confirm the findings.</p>' + checks.map(function (c, i) {
        return '<label class="chk"><input type="checkbox" data-act="check" data-i="' + i + '"' + (done[i] ? " checked" : "") + (who === "officer" && st === "Vetting in progress" ? "" : " disabled") + "> " + esc(c) + "</label>";
      }).join("") + (who === "officer" ? '<div class="row"><button class="btn pri" type="button" data-act="recommend"' + (all ? "" : " disabled") + ">Recommend</button><span class=\"hint\">" + (all ? "Ready to send to the reviewer." : "Complete the checklist first.") + "</span></div>" : "") +
        (who === "approver" ? '<div class="row"><button class="btn pri" type="button" data-act="approve">Approve</button><button class="btn danger" type="button" data-act="reject">Reject</button></div>' : "") + "</div>" : '<div class="note info">CIC is reviewing this application. AI review has already run. Assigned officer: Mandy Chan. Pledged result: 30 days from submission.</div>');
  }

  function messagesPage() {
    return '<h1>Messages</h1><p class="sub">Questions on your CITF cases. Replies by email are not accepted.</p><div class="card"><div class="thread"><div class="tm me"><div class="tmh"><b>You · Carmen Lau</b><small>2 days ago</small></div>When is the first disbursement paid for the approved drone kits?</div><div class="tm cic"><div class="tmh"><b>CIC · Tony Lee</b><small>Awaiting reply</small></div>Open. The first instalment is paid after the purchase order, invoice, delivery note and bank record are accepted.</div></div></div>' +
      '<div class="card"><h2>Ask CIC a question</h2><div class="f"><label>Application</label><select><option>CITF-2027-00031 · Approved</option><option>CITF-2027-00046 · Under review</option></select></div><div class="f"><label>Question</label><textarea rows="3" placeholder="Write the question"></textarea></div><button class="btn pri" type="button" data-act="ask">Send to CIC</button></div>';
  }

  function palPage() {
    return '<div class="row between"><div><h1>Pre-approved List</h1><p class="sub">' + esc(companies.ss.name) + " · " + esc(companies.ss.id) + '</p></div><a class="btn pri" href="/pre-approved-list/">+ Apply to list a product</a></div>' +
      '<div class="card"><h2>Your listed items</h2><div class="scroll"><table class="tbl"><thead><tr><th>Code</th><th>Item</th><th>List</th><th>Approved price</th><th>Status</th></tr></thead><tbody>' +
      pal.filter(function (p) { return p[5].indexOf("SmartSite") === 0; }).map(function (p) {
        return "<tr><td>" + esc(p[0]) + "</td><td>" + esc(p[1]) + "</td><td>" + esc(p[2]) + "</td><td>" + esc(p[3]) + "</td><td>" + pill(p[4]) + "</td></tr>";
      }).join("") + "</tbody></table></div></div>" +
      '<div class="card"><h2>Your requests</h2><div class="scroll"><table class="tbl"><thead><tr><th>Request</th><th>Kind</th><th>Item</th><th>Status</th></tr></thead><tbody><tr><td>PAL-2027-00088</td><td>Change request</td><td>SiteEye AI Safety Camera System v5 · price HK$86,000 → HK$92,000</td><td>' + pill("Under review") + "</td></tr></tbody></table></div><span class=\"hint\">A PAL Officer reviews the request. The public CITF list updates after approval.</span></div>";
  }

  function endorsePage() {
    return '<h1>Endorsement</h1><p class="sub">Kenny Wong prepared an application. A Company Admin must endorse it before CIC receives it.</p><div class="card"><div class="row between"><h2>CITF-2027-00049</h2>' + pill(appStatus(apps[3])) + '</div><p>BIM · Common Data Environment Cloud · 20 users · ' + money(136000) + " · Kai Tak Sports Park Phase 2</p>" +
      (appStatus(apps[3]) === "Pending endorsement"
        ? '<button class="btn pri" type="button" data-act="endorse">Endorse and submit to CIC</button>'
        : '<div class="note ok">Endorsed. CIC has the application.</div>') + "</div>";
  }

  function pendingPage() {
    var c = companies.kmic;
    return companyCard(c, true) + '<div class="card"><h2>Registration</h2><p>Reference <b>CR-KMIC</b> · submitted 2 days ago · risk ' + pill("Medium") + "</p>" +
      '<ol class="steps" style="grid-template-columns:repeat(4,1fr)"><li class="done"><i></i>Submitted</li><li class="cur"><i></i>Review</li><li><i></i>CompanyID issued</li><li><i></i>Use CIC services</li></ol>' +
      '<div class="note warn">' + esc(c.ai) + '</div><a class="btn pri" href="/track/">Track my registration</a></div>';
  }

  function table(heads, rows) {
    return '<div class="scroll"><table class="tbl"><thead><tr>' + heads.map(function (h) { return "<th>" + esc(h) + "</th>"; }).join("") + "</tr></thead><tbody>" + rows + "</tbody></table></div>";
  }

  function dCases() {
    var rows = apps.filter(function (a) { return a.status !== "Draft" && a.status !== "Pending endorsement"; }).map(function (a) {
      return "<tr><td><a href=\"#case\">" + esc(a.ref) + "</a></td><td>" + esc(companies[a.co].name) + "</td><td>" + esc(a.schemes) + "</td><td>" + money(a.grant) + "</td><td>" + esc(a.officer || "–") + "</td><td>" + pill(appStatus(a)) + "</td></tr>";
    }).join("");
    return "<h1>Funding Applications</h1><p class=\"sub\">Mandy Chan’s queue. Open CITF-2027-00046 to complete the checklist and recommend.</p>" + table(["Application", "Applicant", "Scheme", "Indicative grant", "Officer", "Status"], rows);
  }
  function dQueue() {
    var rows = queue.map(function (q) {
      var link = q[0].indexOf("Kowloon") === 0 ? "<a href=\"#company\">" + esc(q[0]) + "</a>" : esc(q[0]);
      return "<tr><td>" + link + "</td><td>" + esc(q[1]) + "</td><td>" + pill(q[2]) + "</td><td>" + pill(q[3]) + "</td><td>" + esc(q[4]) + "</td></tr>";
    }).join("");
    return "<h1>Company Registrations</h1><div class=\"kpis\"><div class=\"kpi\"><small>In the queue</small><b>6</b></div><div class=\"kpi\"><small>New – unassigned</small><b>2</b></div><div class=\"kpi\"><small>High risk</small><b>2</b></div><div class=\"kpi\"><small>Awaiting applicant</small><b>1</b></div></div>" + table(["Company", "Type", "Queue", "Risk", "Waiting"], rows);
  }
  function dCompany() {
    var c = companies.kmic;
    return "<h1>" + esc(c.name) + "</h1><p class=\"sub\">" + pill(c.status) + " " + pill(c.risk) + "</p><div class=\"card\"><h2>AI check on the BR</h2><p>" + esc(c.ai) + "</p><div class=\"kv\"><span>BR</span><span>" + esc(c.br) + "</span><span>Contact</span><span>" + esc(c.contact) + " · " + esc(c.email) + "</span><span>Address</span><span>" + esc(c.address) + "</span></div><div class=\"row\"><button class=\"btn pri\" type=\"button\" data-act=\"coapprove\">Approve and issue CompanyID</button><button class=\"btn ghost\" type=\"button\" data-act=\"corfi\">Request information</button></div></div>";
  }
  function dDrs() {
    return "<h1>Disbursement Requests</h1><p class=\"sub\">Winnie Ho checks evidence, then recommends. Peter Yip approves. Eric Lam signs off the payment.</p>" +
      table(["DR", "Application", "Type", "Claimed", "Officer", "Status"], "<tr><td><a href=\"#dr\">DR-2027-00113</a></td><td>CITF-2027-00031</td><td>First instalment</td><td>" + money(144480) + "</td><td>Winnie Ho</td><td>" + pill(drStatus(drs[0])) + "</td></tr>");
  }
  function dDr(who) {
    var st = drStatus(drs[0]);
    return "<h1>DR-2027-00113</h1><p class=\"sub\">CITF-2027-00031 · DroneScan Site Survey Kit · " + pill(st) + "</p><div class=\"card\"><h2>Evidence</h2>" +
      table(["Document", "AI check"], "<tr><td>Purchase order PO-7731.pdf</td><td>Matches approved item and quantity</td></tr><tr><td>Invoice INV-88213.pdf</td><td>HK$258,000 = 2 × HK$129,000</td></tr><tr><td>Delivery note DN-5512.pdf</td><td><span class=\"pill warn\">Delivery date is earlier than the invoice</span></td></tr><tr><td>Bank transfer HSBC_transfer.pdf</td><td>HK$258,000 matches the invoice</td></tr>") +
      '<div class="row">' +
      (who === "disbursement" ? '<button class="btn pri" type="button" data-act="drrec">Recommend approval</button>' : "") +
      (who === "approver" ? '<button class="btn pri" type="button" data-act="drapprove">Approve</button>' : "") +
      (who === "finance" ? '<button class="btn pri" type="button" data-act="finsign">Finance sign-off</button>' : "") +
      "</div></div>";
  }
  function dPay() {
    var st = drStatus(drs[0]);
    return "<h1>Payments (Finance)</h1><p class=\"sub\">Sign off the package, then the bank file can be generated.</p>" +
      table(["DR", "Applicant", "Amount", "Status"], "<tr><td>DR-2027-00113</td><td>Harbour Build Engineering Limited</td><td>" + money(144480) + "</td><td>" + pill(st) + "</td></tr>") +
      '<div class="row" style="margin-top:12px"><button class="btn pri" type="button" data-act="finsign">Finance sign-off</button><button class="btn ghost" type="button" data-act="bank">Generate bank file</button></div>';
  }
  function dPal() {
    return "<h1>PAL Requests</h1><p class=\"sub\">Kelly Lau reviews vendor changes before they reach the approver.</p>" +
      table(["Request", "Vendor", "Kind", "Item", "Status"], "<tr><td>PAL-2027-00088</td><td>SmartSite Robotics HK Limited</td><td>Change request</td><td>SiteEye v5 · HK$92,000</td><td>" + pill("Under review") + "</td></tr><tr><td>PAL-2027-00084</td><td>SmartSite Robotics HK Limited</td><td>New listing</td><td>RoboLayout Mini</td><td>" + pill("Draft") + "</td></tr>");
  }
  function dApprove() {
    return "<h1>Approvals</h1><p class=\"sub\">Peter Yip reviews vetted funding cases and disbursements.</p>" +
      table(["Record", "Applicant", "Stage", "Amount", ""], "<tr><td><a href=\"#case\">CITF-2027-00048</a></td><td>Harbour Build Engineering Limited</td><td>" + pill(appStatus(apps[1])) + "</td><td>" + money(247100) + "</td><td><button class=\"btn sm pri\" type=\"button\" data-act=\"approve48\">Approve</button></td></tr><tr><td><a href=\"#dr\">DR-2027-00113</a></td><td>Harbour Build Engineering Limited</td><td>" + pill(drStatus(drs[0])) + "</td><td>" + money(144480) + "</td><td><a href=\"#dr\">Open</a></td></tr>");
  }
  function dSecurity() {
    var rows = [
      ["Company registrations", "View, edit", "View", "View", "View, edit, approve", "–", "View, edit"],
      ["Funding applications", "View, edit", "View", "View", "View, edit, approve", "–", "View, edit"],
      ["Disbursement requests", "View", "View, edit", "View", "View, edit, approve", "View", "View, edit"],
      ["Payments", "–", "View", "–", "View", "View, edit, approve", "View, edit"],
      ["Pre-approved List", "View", "View", "View, edit", "View, edit, approve", "–", "View, edit"]
    ];
    return "<h1>Security and Governance</h1><p class=\"sub\">Personas used on the sign-in page. An administrator changes who can view, edit and approve.</p>" +
      table(["Module", "Funding officer", "Disbursement officer", "PAL officer", "Approver", "Finance", "Administrator"], rows.map(function (r) {
        return "<tr>" + r.map(function (c) { return "<td>" + esc(c) + "</td>"; }).join("") + "</tr>";
      }).join("")) +
      '<div class="note info">Workflows configured here: company review, funding review, disbursement, payment, and Pre-approved List review. Stage mail is drafted in Power Automate and stays off for this demo.</div>';
  }

  function portalBody(r, v) {
    if (v === "citf") return citfPage(r);
    if (v === "case" || v === "app") return casePage(r.id === "funding-officer" ? "officer" : r.id === "approver" ? "approver" : "applicant");
    if (v === "messages") return messagesPage();
    if (v === "pal") return palPage();
    if (v === "endorse") return endorsePage();
    if (v === "pending" || v === "track") return pendingPage();
    if (v === "dr") return "<h1>DR-2027-00113</h1><p class=\"sub\">First instalment of CITF-2027-00031 · " + pill(drStatus(drs[0])) + "</p><div class=\"card\"><p>Claimed " + money(144480) + " for two DroneScan kits. CIC is checking the invoice and delivery note.</p></div>";
    if (v === "company" && r.id === "pending") return pendingPage();
    if (v === "company" && r.id === "vendor") return companyCard(companies.ss, false);
    return companyCard(companies[r.co], r.id !== "company-admin");
  }

  function d365Body(r, v) {
    if (v === "queue" || v === "company") return v === "company" ? dCompany() : dQueue();
    if (v === "case") return casePage(r.id === "approver" ? "approver" : "officer");
    if (v === "drs") return dDrs();
    if (v === "dr") return dDr(r.id === "approver" ? "approver" : r.id === "finance" ? "finance" : "disbursement");
    if (v === "pay") return dPay();
    if (v === "palreq") return dPal();
    if (v === "approve") return dApprove();
    if (v === "security") return dSecurity();
    if (v === "cases") return dCases();
    return dCases();
  }

  function render() {
    var root = document.getElementById("citf-flow");
    if (!root) return;
    var r = role();
    var v = view();
    var inner = r.area === "portal" ? portalHead(r, v) + '<main class="p-main">' + portalBody(r, v) + "</main>" : d365Shell(r, v, d365Body(r, v));
    root.innerHTML = inner + (state.toast ? '<div class="toast" id="citf-toast">' + esc(state.toast) + "</div>" : "");
  }

  document.addEventListener("change", function (e) {
    var el = e.target.closest("[data-act]");
    if (!el || !document.getElementById("citf-flow")) return;
    var act = el.getAttribute("data-act");
    if (act === "check") {
      state.checks.a46[el.getAttribute("data-i")] = el.checked;
      render();
      return;
    }
    if (act === "switchRole") {
      var next = el.value;
      var home = roles[next] ? roles[next].home : "company";
      location.href = "/flow/?role=" + encodeURIComponent(next) + "#" + home;
    }
  });

  document.addEventListener("click", function (e) {
    var el = e.target.closest("[data-act]");
    if (!el || !document.getElementById("citf-flow")) return;
    var act = el.getAttribute("data-act");
    if (act === "check" || act === "switchRole") return;
    e.preventDefault();
    if (act === "save") toast("Saved. Legal-name changes would wait for CIC to re-verify.");
    if (act === "ask") toast("Question sent to CIC. It appears on the officer’s Q&A list.");
    if (act === "endorse") { state.apps.a49 = "Submitted"; toast("Endorsed and submitted to CIC."); }
    if (act === "recommend") { state.apps.a46 = "Under review"; toast("Recommended. The reviewer is notified."); }
    if (act === "approve") { state.apps.a46 = "Approved"; toast("Approved. The result is ready for the applicant."); }
    if (act === "approve48") { state.apps.a48 = "Approved"; toast("CITF-2027-00048 approved."); }
    if (act === "reject") { state.apps.a46 = "Rejected"; toast("Rejected. The applicant can read the result in the portal."); }
    if (act === "drrec") { state.drs.dr1 = "Pending approval"; toast("Disbursement sent to the approver."); }
    if (act === "drapprove") { state.drs.dr1 = "DR approved – pending transfer"; toast("Approved. Finance can sign off the payment."); }
    if (act === "finsign") { state.drs.dr1 = "Finance signed off"; toast("Finance signed off."); }
    if (act === "bank") toast("Bank file prepared for this demo. It is not sent to a bank.");
    if (act === "coapprove") toast("CompanyID CIC-C-002311 issued for this demo.");
    if (act === "corfi") toast("Request for information recorded for the applicant.");
  });
  window.addEventListener("hashchange", render);
  render();
})();
