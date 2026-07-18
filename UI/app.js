async function createBackupWorkbook(){
  try{
    const wb = buildWorkbook();
    const stamp = new Date().toISOString().replace(/[:.]/g,'-');
    XLSX.writeFile(wb, `SMMS_Backup_${stamp}.xlsx`);
    return true;
  }catch(e){
    console.error('Backup failed', e);
    return false;
  }
}

function buildWorkbook(){
 const wb=XLSX.utils.book_new();
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.members||[]),'Members');
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.collections||[]),'Collections');
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.expenses||[]),'Expenses');
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet([DB.settings||{}]),'Settings');
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.users||[]),'Users');
 XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.auditLog||[]),'AuditLog');
 return wb;
}

// ═══════════════════════════════════════════════
// CONSTANTS
// ═══════════════════════════════════════════════
const MONTHS     = ['','January','February','March','April','May','June','July','August','September','October','November','December'];
const MONTH_NUM  = {January:1,February:2,March:3,April:4,May:5,June:6,July:7,August:8,September:9,October:10,November:11,December:12};
const PER        = 10;
const COMPLAINT_CATEGORIES = ['Plumbing','Electrical','Security','Housekeeping','Parking','Noise','Lift','Cleanliness','Common Area','Other'];

// ═══════════════════════════════════════════════
// DATA STORE
// ═══════════════════════════════════════════════
let DB = {
  members: [], collections: [], expenses: [], auditLog: [], users: [], complaints: [],
  settings: {
    societyName:'NLC Aadya', address:'', email:'', phone:'',
    maintenanceAmt:2000, floors:['1','2','3','4','5'],
    categories:['Security','Housekeeping','Electricity','Water','Repairs','Lift Maintenance','Gardening','Festival','CCTV','Miscellaneous']
  }
};
let editId   = {col:null, exp:null, mem:null, cmp:null};
let pages    = {col:1, exp:1, mem:1, audit:1, cmp:1};
let trendChart, pieChart, annualChart;
let currentUser = null;

// ═══════════════════════════════════════════════
// AUTH — login/signup/forgot-password all happen on home.html against the
// SQL-backed API (api/SMMS.Api). index.html only ever consumes the JWT +
// user info handed off via sessionStorage (see api.js: apiGetSession /
// apiGetToken / apiClearSession).
// ═══════════════════════════════════════════════
function isAdmin(){ return !!currentUser && currentUser.role === 'Admin'; }

function logout(){
  currentUser = null;
  apiClearSession();
  document.body.classList.add('logged-out');
  window.location.href = 'home.html';
}

// ═══════════════════════════════════════════════
// API DATA LOADERS — replace the old Excel-file persistence. Each loader
// fetches from api/SMMS.Api and reshapes the response into the field
// names/casing the existing render functions already expect (fld(),
// getMonth(), getYear(), etc.) so the rest of the app needed minimal changes.
// ═══════════════════════════════════════════════
async function loadMembers(){
  DB.members = await Api.getMembers();
}

async function loadCollections(){
  const data = await Api.getCollections();
  DB.collections = data.map(c => {
    const mem = DB.members.find(m => String(m.id) === String(c.memberId));
    return {
      id: c.id,
      memberId: String(c.memberId),
      memberName: c.memberName || '',
      flat: c.flat || '',
      floor: mem ? mem.floor : '',
      amount: c.amount,
      month: MONTHS[c.month] || '',
      monthNum: c.month,
      year: c.year,
      paymentDate: c.paymentDate ? c.paymentDate.split('T')[0] : '',
      paymentMode: c.paymentMode || '',
      status: c.status,
      remarks: c.remarks || ''
    };
  });
}

async function loadExpenses(){
  const data = await Api.getExpenses();
  DB.expenses = data.map(e => ({
    id: e.id,
    expenseDate: e.expenseDate ? e.expenseDate.split('T')[0] : '',
    category: e.category,
    description: e.description,
    vendor: e.vendor || '',
    amount: e.amount,
    paymentMode: e.paymentMode || '',
    month: MONTHS[e.month] || '',
    monthNum: e.month,
    year: e.year,
    remarks: e.remarks || ''
  }));
}

async function loadSettingsData(){
  const s = await Api.getSettings();
  DB.settings = {
    societyName: s.societyName || 'NLC Aadya',
    address: s.address || '',
    email: s.email || '',
    phone: s.phone || '',
    maintenanceAmt: s.maintenanceAmt || 2000,
    floors: (s.floors && s.floors.length) ? s.floors : ['1','2','3','4','5'],
    categories: (s.categories && s.categories.length) ? s.categories : DB.settings.categories,
    theme: s.theme || 'light'
  };
}

function currentSettingsPayload(overrides = {}){
  return {
    societyName: DB.settings.societyName,
    address: DB.settings.address,
    email: DB.settings.email,
    phone: DB.settings.phone,
    maintenanceAmt: DB.settings.maintenanceAmt,
    floors: DB.settings.floors,
    categories: DB.settings.categories,
    theme: DB.settings.theme || 'light',
    ...overrides
  };
}

async function loadUsers(){
  DB.users = await Api.getUsers();
}

async function loadAuditLogData(){
  DB.auditLog = await Api.getAuditLog();
}

async function loadComplaints(){
  const data = await Api.getComplaints();
  DB.complaints = data.map(c => ({
    id: c.id,
    subject: c.subject,
    description: c.description,
    category: c.category,
    priority: c.priority,
    status: c.status,
    raisedByUserId: c.raisedByUserId,
    raisedByUsername: c.raisedByUsername || '',
    flat: c.flat || '',
    floor: c.floor || '',
    createdAt: c.createdAt ? c.createdAt.split('T')[0] : '',
    resolvedAt: c.resolvedAt ? c.resolvedAt.split('T')[0] : '',
    resolutionNotes: c.resolutionNotes || '',
    assignedTo: c.assignedTo || ''
  }));
}

async function loadCoreData(){
  await Promise.all([loadMembers(), loadSettingsData()]);
  await Promise.all([loadCollections(), loadExpenses(), loadComplaints()]);
  if(isAdmin()){
    await Promise.all([loadUsers(), loadAuditLogData()]);
  }
}

function applyRolePermissions(){
  const admin = isAdmin();
  document.body.classList.toggle('role-member', !admin);
  ['set-sname','set-addr','set-email','set-phone','set-mamt','set-wings','new-cat'].forEach(id=>{
    const el = document.getElementById(id);
    if(el) el.disabled = !admin;
  });
}

function updateSidebarUserInfo(){
  if(!currentUser) return;
  const av = document.getElementById('sbUav');
  const nm = document.getElementById('sbUname');
  const rl = document.getElementById('sbUrole');
  if(av) av.textContent = currentUser.username.charAt(0).toUpperCase();
  if(nm) nm.textContent = currentUser.username;
  if(rl) rl.textContent = currentUser.role === 'Admin' ? 'Administrator' : 'Member (Read-only)';
}

// ═══════════════════════════════════════════════
// FIELD ACCESSORS — handle any casing / column name from Excel
// ═══════════════════════════════════════════════
const fld = (r, ...keys) => {
  if(!r || typeof r !== 'object') return '';
  const normalized = Object.keys(r).reduce((acc, key) => {
    acc[key.toString().trim().toLowerCase()] = r[key];
    return acc;
  }, {});
  for(const k of keys){
    const nk = String(k || '').trim().toLowerCase();
    if(normalized[nk] !== undefined && normalized[nk] !== null && normalized[nk] !== '') return normalized[nk];
  }
  return '';
};

function getMonth(r){
  // try numeric first
  const n = +fld(r,'monthNum','MonthNum');
  if(n) return n;
  // try text name
  const name = fld(r,'month','Month','').toString().trim();
  return MONTH_NUM[name] || 0;
}
function getYear(r)  { return +fld(r,'year','Year') || 0; }
function getStatus(r){ return fld(r,'status','Paid','Status').toString().trim(); }
function getAmt(r)   { return Number(fld(r,'amount','Amount')) || 0; }
function getMemberId(r){ return String(fld(r,'memberId','MemberId','member_id','id','Id') || '').trim(); }

// ═══════════════════════════════════════════════
// DATE HELPER — Excel serial or string → DD-MM-YYYY
// ═══════════════════════════════════════════════
function parseExcelDate(val){
  if(val === undefined || val === null || val === '') return '';
  if(typeof val === 'string' && val.trim() !== '' && isNaN(Number(val))) return val.trim();
  const n = Number(val);
  if(n > 1000){
    const d = new Date(Math.round((n - 25569) * 86400 * 1000));
    const dd   = String(d.getUTCDate()).padStart(2,'0');
    const mm   = String(d.getUTCMonth()+1).padStart(2,'0');
    const yyyy = d.getUTCFullYear();
    return `${dd}-${mm}-${yyyy}`;
  }
  return val;
}

// ═══════════════════════════════════════════════
// INIT
// ═══════════════════════════════════════════════
window.onload = async () => {
  // Login/sign-up/forgot-password all happen on the public landing page
  // (home.html) now, against the SQL-backed API. index.html only ever
  // renders the authenticated dashboard — if there's no valid JWT session
  // handed off from home.html, send the visitor back there instead of
  // showing anything here.
  const session = apiGetSession();
  if(!session || !apiGetToken()){
    window.location.href = 'home.html';
    return;
  }

  currentUser = {id: session.id, username: session.username, role: session.role};
  document.body.classList.remove('logged-out');
  populateYearDropdown();
  syncFilterControls('dashboard'); // dashboard is the initial active tab — defaults to All Years
  applyRolePermissions();
  updateSidebarUserInfo();

  try{
    await loadCoreData();
  }catch(err){
    console.error(err);
    toast('Failed to load data from server: ' + err.message, 'warn');
  }

  applySettings();
  renderDashboard();
};

function populateYearDropdown(){
  const sel = document.getElementById('topYear');
  const cur = new Date().getFullYear();
  const allOpt = document.createElement('option');
  allOpt.value = '0'; allOpt.text = 'All Years';
  sel.appendChild(allOpt);
  for(let y = cur; y >= cur-5; y--){
    const o = document.createElement('option');
    o.value = y; o.text = y;
    if(y === cur) o.selected = true;
    sel.appendChild(o);
  }
  // Dashboard gives the full picture by default (All Years); every other tab
  // keeps the current-year default that was already selected above.
  filterState.general.year = cur;
}

// ═══════════════════════════════════════════════
// NAVIGATION
// ═══════════════════════════════════════════════
async function showTab(t, el){
  if(t==='admin' && !isAdmin()) return toast('Admin access required.','warn');
  if(t==='auditlog' && !isAdmin()) return toast('Admin access required.','warn');
  document.querySelectorAll('.tab-content').forEach(x => x.classList.remove('active'));
  document.querySelectorAll('.nav-item').forEach(x => x.classList.remove('active'));
  document.getElementById('tab-'+t).classList.add('active');
  if(el) el.classList.add('active');
  const titles = {dashboard:'Dashboard',collections:'Collections',expenses:'Expenses',members:'Members',complaints:'Complaints',reports:'Reports & Analytics',importexport:'Export',notifications:'Notifications',auditlog:'Audit Log',settings:'Settings',admin:'Admin Panel'};
  document.getElementById('pageTitle').textContent = titles[t] || t;

  // Sync the shared month/year dropdowns to this tab's own remembered filter —
  // Dashboard defaults to All Years for the full picture, while every other
  // tab keeps its own last-used (default: current year) selection.
  syncFilterControls(t);

  // Audit log & users are admin-only server-side — fetch the latest each
  // time these tabs are opened instead of relying on the snapshot loaded at login.
  if(t==='auditlog'){
    try{ await loadAuditLogData(); }catch(err){ toast('Failed to load audit log: '+err.message,'warn'); }
  }
  if(t==='admin'){
    try{ await loadUsers(); }catch(err){ toast('Failed to load users: '+err.message,'warn'); }
  }

  const renders = {dashboard:renderDashboard, collections:renderCollections, expenses:renderExpenses, members:renderMembers, complaints:renderComplaints, reports:renderReports, notifications:renderNotifications, auditlog:renderAudit, settings:loadSettingsUI, admin:renderUsers};
  if(renders[t]) renders[t]();
}

function onTopFilterChange(){
  const active = document.querySelector('.tab-content.active');
  if(!active) return;
  const t = active.id.replace('tab-','');
  // Remember this filter choice against whichever tab is currently active,
  // so navigating away and back restores it instead of leaking into other tabs.
  const bucket = filterBucketFor(t);
  bucket.month = +document.getElementById('topMonth').value;
  bucket.year  = +document.getElementById('topYear').value;

  // Defensive guard: Collections/Expenses can never filter on "All Years",
  // even if a value of 0 slips through — fall back to the current year.
  if(!allYearsAllowed(t) && !bucket.year){
    bucket.year = new Date().getFullYear();
    const yrSel = document.getElementById('topYear');
    if(yrSel) yrSel.value = bucket.year;
  }

  const renders = {dashboard:renderDashboard, collections:renderCollections, expenses:renderExpenses, reports:renderReports, notifications:renderNotifications};
  if(renders[t]) renders[t]();
}

// ═══════════════════════════════════════════════
// FILTER HELPERS
// ═══════════════════════════════════════════════
// Dashboard keeps its own filter state defaulting to "All Years" (the full
// picture); every other tab shares a single default (current year), matching
// the app's existing behaviour.
let filterState = {
  dashboard: { month: 0, year: 0 },
  general:   { month: 0, year: new Date().getFullYear() }
};

function filterBucketFor(tab){ return tab === 'dashboard' ? filterState.dashboard : filterState.general; }

// Collections & Expenses always need one concrete year of data to manage —
// "All Years" is only meaningful as a full-picture view on Dashboard/Reports/Notifications.
const NO_ALL_YEARS_TABS = ['collections','expenses'];
function allYearsAllowed(tab){ return !NO_ALL_YEARS_TABS.includes(tab); }

function syncFilterControls(tab){
  const bucket = filterBucketFor(tab);
  const moSel = document.getElementById('topMonth');
  const yrSel = document.getElementById('topYear');
  const allOpt = yrSel ? yrSel.querySelector('option[value="0"]') : null;

  if(!allYearsAllowed(tab)){
    if(allOpt) allOpt.disabled = true;
    // Never let this tab land on "All Years" — coerce to the current year.
    if(!bucket.year) bucket.year = new Date().getFullYear();
  } else if(allOpt){
    allOpt.disabled = false;
  }

  if(moSel) moSel.value = bucket.month;
  if(yrSel) yrSel.value = bucket.year;
}

function topMonth(){ return +document.getElementById('topMonth').value; }
function topYear() { return +document.getElementById('topYear').value; }

function filteredCollections(){
  const m = filterState.dashboard.month, y = filterState.dashboard.year;
  return DB.collections.filter(c => (!m || getMonth(c)===m) && (!y || getYear(c)===y));
}
function filteredExpenses(){
  const m = filterState.dashboard.month, y = filterState.dashboard.year;
  return DB.expenses.filter(e => (!m || getMonth(e)===m) && (!y || getYear(e)===y));
}

// ═══════════════════════════════════════════════
// PILLS (shared builder)
// ═══════════════════════════════════════════════
function buildPills(containerId, onClickFn){
  const el = document.getElementById(containerId);
  if(!el) return;
  const m = topMonth();
  let html = `<span class="pill${m===0?' active':''}" onclick="${onClickFn}(0)">All</span>`;
  MONTHS.slice(1).forEach((name, i) => {
    const mo = i + 1;
    html += `<span class="pill${m===mo?' active':''}" onclick="${onClickFn}(${mo})">${name.slice(0,3)}</span>`;
  });
  el.innerHTML = html;
}

function setColMonth(mo){ document.getElementById('topMonth').value = mo; filterState.general.month = mo; renderCollections(); }
function setExpMonth(mo){ document.getElementById('topMonth').value = mo; filterState.general.month = mo; renderExpenses(); }

// ═══════════════════════════════════════════════
// DASHBOARD
// ═══════════════════════════════════════════════
function renderDashboard(){
  const cols = filteredCollections();
  const exps = filteredExpenses();
  // Total Collection should only reflect money actually received (Paid),
  // not amounts that are still pending/unpaid.
  const totalCol = cols.filter(c => getStatus(c).toLowerCase() === 'paid').reduce((s,c)=>s+getAmt(c),0);
  const totalExp = exps.reduce((s,e)=>s+getAmt(e),0);
  const bal      = totalCol - totalExp;

  const unpaidRecords = cols.filter(c => getStatus(c).toLowerCase() !== 'paid');
  const pendAmt = unpaidRecords.reduce((s,c)=>s+getAmt(c),0);

  const pendingMap = {};
  unpaidRecords.forEach(c=>{
    const memberId = String(fld(c,'memberId','MemberId','member_id')).trim();
    if(!pendingMap[memberId]){
      pendingMap[memberId] = {
        memberName: fld(c,'memberName','MemberName','name','Name'),
        flat: fld(c,'flat','Flat'),
        floor: fld(c,'floor','Floor'),
        months: [],
        due: 0
      };
    }
    pendingMap[memberId].months.push((MONTHS[getMonth(c)] || fld(c,'month','Month') || '').slice(0,3));
    pendingMap[memberId].due += getAmt(c);
  });

  const pendingMembers = Object.values(pendingMap);

  setText('kpi-col','₹'+totalCol.toLocaleString('en-IN'));
  setText('kpi-exp','₹'+totalExp.toLocaleString('en-IN'));
  setText('kpi-bal','₹'+bal.toLocaleString('en-IN'));
  setText('kpi-pend','₹'+pendAmt.toLocaleString('en-IN'));
  setText('kpi-pend-sub', pendingMembers.length+' members · '+unpaidRecords.length+' unpaid records');
  setText('kpi-mem', DB.members.length);
  setText('kpi-bal-sub', bal>=0?'✅ Surplus':'⚠️ Deficit');

  document.getElementById('dash-col-tbody').innerHTML = [...DB.collections].reverse().slice(0,6).map(c=>{
    const st=getStatus(c)||'Unknown'; const stk=st.toLowerCase();
    return `<tr><td>${fld(c,'memberName','MemberName','name','Name')}</td><td>${fld(c,'flat','Flat')}</td><td>₹${getAmt(c).toLocaleString('en-IN')}</td><td><span class="badge b-${stk}">${st}</span></td></tr>`;
  }).join('') || '<tr><td colspan="4" class="empty">No data</td></tr>';

  document.getElementById('dash-pend-count').textContent = pendingMembers.length+' pending';
  document.getElementById('dash-pend-tbody').innerHTML = pendingMembers.map(m=>`<tr><td>${m.memberName}</td><td>${m.flat}</td><td>Floor ${m.floor}</td><td>${m.months.join(', ')}</td><td><span class="badge b-unpaid">₹${m.due.toLocaleString('en-IN')}</span></td></tr>`).join('')
  || '<tr><td colspan="5" class="empty">All paid! 🎉</td></tr>';

  buildTrendChart();
  buildPieChart();
  updateNotifBadge();
}

function setText(id,v){ const el=document.getElementById(id); if(el) el.textContent=v; }

function buildTrendChart(){
  // The 12-month trend chart needs one concrete year to plot; when the
  // dashboard filter is "All Years" (0), fall back to the current year.
  const y = filterState.dashboard.year || new Date().getFullYear();
  const labels=[], cArr=[], eArr=[];
  for(let mo=1;mo<=12;mo++){
    const c = DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    const e = DB.expenses.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    if(c||e){ labels.push(MONTHS[mo].slice(0,3)); cArr.push(c); eArr.push(e); }
  }
  if(trendChart) trendChart.destroy();
  trendChart = new Chart(document.getElementById('trendChart').getContext('2d'),{
    type:'bar',
    data:{labels,datasets:[
      {label:'Collection',data:cArr,backgroundColor:'rgba(108,99,255,.75)',borderRadius:5},
      {label:'Expenses',  data:eArr,backgroundColor:'rgba(252,129,129,.75)',borderRadius:5}
    ]},
    options:{responsive:true,maintainAspectRatio:false,plugins:{legend:{labels:{font:{size:10}}}},scales:{x:{grid:{display:false}},y:{ticks:{callback:v=>'₹'+(v/1000)+'k'}}}}
  });
}

function buildPieChart(){
  const catMap={};
  filteredExpenses().forEach(e=>{ const c=fld(e,'category','Category')||'Other'; catMap[c]=(catMap[c]||0)+getAmt(e); });
  const labels=Object.keys(catMap), data=Object.values(catMap);
  const colors=['#6c63ff','#f6ad55','#38b2ac','#fc8181','#68d391','#90cdf4','#b794f4','#fbd38d','#81e6d9','#feb2b2'];
  if(pieChart) pieChart.destroy();
  pieChart = new Chart(document.getElementById('pieChart').getContext('2d'),{
    type:'doughnut',
    data:{labels,datasets:[{data,backgroundColor:colors.slice(0,labels.length),borderWidth:2}]},
    options:{responsive:true,maintainAspectRatio:false,cutout:'60%',plugins:{legend:{position:'bottom',labels:{font:{size:9},boxWidth:9,padding:6}}}}
  });
}

// ═══════════════════════════════════════════════
// COLLECTIONS
// ═══════════════════════════════════════════════
function renderCollections(){
  // populate floor filter
  const flSel = document.getElementById('colFloorF');
  const flCur = flSel.value;
  flSel.innerHTML = '<option value="">All Floors</option>' + DB.settings.floors.map(f=>`<option value="${f}">Floor ${f}</option>`).join('');
  flSel.value = flCur;

  buildPills('colMonthPills','setColMonth');

  const search  = document.getElementById('colSearch').value.toLowerCase();
  const floor   = document.getElementById('colFloorF').value;
  const status  = document.getElementById('colStatusF').value.toLowerCase();
  const m = topMonth(), y = topYear();

  const data = DB.collections.filter(c=>{
    const mo  = getMonth(c);
    const yr  = getYear(c);
    const fl  = String(fld(c,'floor','Floor','')).trim();
    const st  = getStatus(c).toLowerCase();
    const nm  = fld(c,'memberName','MemberName','name','Name','').toLowerCase();
    const ft  = String(fld(c,'flat','Flat','')).toLowerCase();
    const rem = fld(c,'remarks','Notes','notes','Remarks','').toLowerCase();
    return (!m||mo===m) && (!y||yr===y) && (!floor||fl===floor) && (!status||st===status) && (!search||nm.includes(search)||ft.includes(search)||rem.includes(search));
  });

  renderPage('col', data, renderColRow);

  // summary bar
  const totalAmt   = data.reduce((s,c)=>s+getAmt(c),0);
  const paidAmt    = data.filter(c=>getStatus(c).toLowerCase()==='paid').reduce((s,c)=>s+getAmt(c),0);
  const unpaidAmt  = data.filter(c=>getStatus(c).toLowerCase()!=='paid').reduce((s,c)=>s+getAmt(c),0);
  const paidCnt    = data.filter(c=>getStatus(c).toLowerCase()==='paid').length;
  const unpaidCnt  = data.filter(c=>getStatus(c).toLowerCase()!=='paid').length;
  const mo = topMonth();
  const label = mo ? MONTHS[mo] : 'All Months';
  document.getElementById('col-summary').innerHTML = `
    <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;padding:10px 14px;background:var(--bg);border-radius:10px;border:1px solid var(--border);width:100%;">
      <span style="font-size:12px;font-weight:700;color:var(--sub);">📅 ${label} Summary</span>
      <span style="margin-left:auto"></span>
      <div style="text-align:center;padding:6px 16px;background:var(--card);border-radius:8px;border:1px solid var(--border);">
        <div style="font-size:10px;color:var(--sub);margin-bottom:2px;">Total Records</div>
        <div style="font-size:16px;font-weight:800;">${data.length}</div>
      </div>
      <div style="text-align:center;padding:6px 16px;background:#c6f6d5;border-radius:8px;">
        <div style="font-size:10px;color:#276749;margin-bottom:2px;">✅ Paid (${paidCnt})</div>
        <div style="font-size:16px;font-weight:800;color:#276749;">₹${paidAmt.toLocaleString('en-IN')}</div>
      </div>
      <div style="text-align:center;padding:6px 16px;background:#fed7d7;border-radius:8px;">
        <div style="font-size:10px;color:#9b2c2c;margin-bottom:2px;">❌ Unpaid (${unpaidCnt})</div>
        <div style="font-size:16px;font-weight:800;color:#9b2c2c;">₹${unpaidAmt.toLocaleString('en-IN')}</div>
      </div>
      <div style="text-align:center;padding:6px 16px;background:rgba(108,99,255,.12);border-radius:8px;">
        <div style="font-size:10px;color:var(--accent);margin-bottom:2px;">💰 Total</div>
        <div style="font-size:16px;font-weight:800;color:var(--accent);">₹${totalAmt.toLocaleString('en-IN')}</div>
      </div>
    </div>`;
}

function renderColRow(c, i){
  const mo     = getMonth(c);
  const yr     = getYear(c);
  const moName = MONTHS[mo] || fld(c,'month','Month') || '';
  const dt     = parseExcelDate(fld(c,'paymentDate','PaymentDate','date','Date'));
  const mode   = fld(c,'paymentMode','PaymentMode','mode','Mode') || '-';
  const flat   = fld(c,'flat','Flat');
  const floor  = fld(c,'floor','Floor');
  const name   = fld(c,'memberName','MemberName','name','Name');
  const amt    = getAmt(c);
  const st     = getStatus(c) || 'Unknown';
  const stk    = st.toLowerCase();
  const rem    = fld(c,'remarks','Notes','notes','Remarks') || '-';
  const id     = c.id || c.Id || i;
  const needsReview = /⚠\s*verify flat/i.test(rem);
  return `<tr${needsReview ? ' style="background:rgba(214,158,46,.14);"' : ''}>
    <td>${i+1}</td><td>${name}${needsReview ? ' ⚠️' : ''}</td><td>${flat}</td><td>Floor ${floor}</td>
    <td>₹${amt.toLocaleString('en-IN')}</td>
    <td>${moName} ${yr}</td>
    <td>${dt}</td>
    <td>${mode}</td>
    <td><span class="badge b-${stk}">${st}</span></td>
    <td>${rem}</td>
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editCollection('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteCollection('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

async function saveCollection(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const mid = document.getElementById('col-member').value;
  const mem = DB.members.find(m=>String(fld(m,'id','Id'))===String(mid));
  if(!mem) return toast('Select a member','warn');
  const amt = +document.getElementById('col-amount').value;
  if(!amt)  return toast('Enter amount','warn');
  const mo  = +document.getElementById('col-month').value;
  const payload = {
    memberId: +mid,
    amount: amt,
    status: document.getElementById('col-status').value,
    month: mo,
    year: +document.getElementById('col-year').value,
    paymentDate: document.getElementById('col-date').value || null,
    paymentMode: document.getElementById('col-mode').value,
    remarks: document.getElementById('col-remarks').value
  };
  try{
    if(editId.col){ await Api.updateCollection(editId.col, payload); }
    else { await Api.createCollection(payload); }
    await loadCollections();
    closeModal('col'); renderCollections(); renderDashboard(); toast('Collection saved!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

function editCollection(id){
  const c = DB.collections.find(x=>String(x.id||x.Id)===String(id)); if(!c) return;
  editId.col = id;
  document.getElementById('col-modal-title').textContent = 'Edit Collection';
  populateMemberDropdown();
  document.getElementById('col-member').value  = String(fld(c,'memberId','MemberId'));
  document.getElementById('col-amount').value  = getAmt(c);
  document.getElementById('col-month').value   = getMonth(c);
  document.getElementById('col-year').value    = getYear(c);
  document.getElementById('col-date').value    = fld(c,'paymentDate','PaymentDate','date','Date');
  document.getElementById('col-mode').value    = fld(c,'paymentMode','PaymentMode','mode','Mode');
  document.getElementById('col-status').value  = getStatus(c);
  document.getElementById('col-remarks').value = fld(c,'remarks','Notes','notes','Remarks');
  document.getElementById('modal-col').classList.add('open');
}

async function deleteCollection(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  if(!confirm('Delete this entry?')) return;
  try{
    await Api.deleteCollection(id);
    await loadCollections();
    renderCollections(); renderDashboard(); toast('Deleted!','warn');
  }catch(err){ toast(err.message || 'Delete failed','warn'); }
}

// ═══════════════════════════════════════════════
// EXPENSES
// ═══════════════════════════════════════════════
function renderExpenses(){
  // populate category filter
  const catSel = document.getElementById('expCatF');
  const catCur = catSel.value;
  catSel.innerHTML = '<option value="">All Categories</option>' + DB.settings.categories.map(c=>`<option>${c}</option>`).join('');
  if(catCur) catSel.value = catCur;

  buildPills('expMonthPills','setExpMonth');

  const search = document.getElementById('expSearch').value.toLowerCase();
  const cat    = document.getElementById('expCatF').value;
  const m = topMonth(), y = topYear();

  const data = DB.expenses.filter(e=>{
    const mo   = getMonth(e);
    const yr   = getYear(e);
    const catV = fld(e,'category','Category','');
    const desc = fld(e,'description','Description','').toLowerCase();
    const ven  = fld(e,'vendor','Vendor','').toLowerCase();
    const catL = catV.toLowerCase();
    return (!m||mo===m) && (!y||yr===y) && (!cat||catV===cat) && (!search||desc.includes(search)||ven.includes(search)||catL.includes(search));
  });

  renderPage('exp', data, renderExpRow);

  // summary bar
  const totalExp  = data.reduce((s,e)=>s+getAmt(e),0);
  const catTotals = {};
  data.forEach(e=>{ const c=fld(e,'category','Category')||'Other'; catTotals[c]=(catTotals[c]||0)+getAmt(e); });
  const topCats   = Object.entries(catTotals).sort((a,b)=>b[1]-a[1]).slice(0,3);
  const mo = topMonth();
  const label = mo ? MONTHS[mo] : 'All Months';
  document.getElementById('exp-summary').innerHTML = `
    <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;padding:10px 14px;background:var(--bg);border-radius:10px;border:1px solid var(--border);width:100%;">
      <span style="font-size:12px;font-weight:700;color:var(--sub);">📅 ${label} Summary</span>
      <span style="margin-left:auto"></span>
      <div style="text-align:center;padding:6px 16px;background:var(--card);border-radius:8px;border:1px solid var(--border);">
        <div style="font-size:10px;color:var(--sub);margin-bottom:2px;">Total Records</div>
        <div style="font-size:16px;font-weight:800;">${data.length}</div>
      </div>
      <div style="text-align:center;padding:6px 16px;background:#fed7d7;border-radius:8px;">
        <div style="font-size:10px;color:#9b2c2c;margin-bottom:2px;">🧾 Total Expenses</div>
        <div style="font-size:16px;font-weight:800;color:#9b2c2c;">₹${totalExp.toLocaleString('en-IN')}</div>
      </div>
      ${topCats.map(([cat,amt])=>`
      <div style="text-align:center;padding:6px 16px;background:rgba(108,99,255,.10);border-radius:8px;">
        <div style="font-size:10px;color:var(--accent);margin-bottom:2px;">${cat}</div>
        <div style="font-size:14px;font-weight:800;color:var(--accent);">₹${amt.toLocaleString('en-IN')}</div>
      </div>`).join('')}
    </div>`;
}

function renderExpRow(e, i){
  const dt   = parseExcelDate(fld(e,'expenseDate','ExpenseDate','date','Date'));
  const cat  = fld(e,'category','Category');
  const desc = fld(e,'description','Description');
  const ven  = fld(e,'vendor','Vendor') || '-';
  const amt  = getAmt(e);
  const mode = fld(e,'paymentMode','PaymentMode','mode','Mode') || '-';
  const mo   = getMonth(e);
  const moN  = MONTHS[mo] || fld(e,'month','Month') || '';
  const yr   = getYear(e) || fld(e,'year','Year') || '';
  const rem  = fld(e,'remarks','Remarks') || '-';
  const id   = e.id || e.Id || i;
  return `<tr>
    <td>${i+1}</td><td>${dt}</td><td>${cat}</td><td>${desc}</td><td>${ven}</td>
    <td>₹${amt.toLocaleString('en-IN')}</td><td>${mode}</td><td>${moN}</td><td>${yr}</td><td>${rem}</td>
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editExpense('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteExpense('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

async function saveExpense(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const amt  = +document.getElementById('exp-amount').value;
  const desc = document.getElementById('exp-desc').value.trim();
  if(!amt||!desc) return toast('Fill required fields','warn');
  const mo = +document.getElementById('exp-month').value;
  const payload = {
    expenseDate: document.getElementById('exp-date').value,
    category: document.getElementById('exp-cat').value,
    description: desc,
    vendor: document.getElementById('exp-vendor').value,
    amount: amt,
    paymentMode: document.getElementById('exp-mode').value,
    month: mo,
    year: +document.getElementById('exp-year').value,
    remarks: document.getElementById('exp-remarks').value
  };
  try{
    if(editId.exp){ await Api.updateExpense(editId.exp, payload); }
    else { await Api.createExpense(payload); }
    await loadExpenses();
    closeModal('exp'); renderExpenses(); renderDashboard(); toast('Expense saved!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

function editExpense(id){
  const e = DB.expenses.find(x=>String(x.id||x.Id)===String(id)); if(!e) return;
  editId.exp = id;
  document.getElementById('exp-modal-title').textContent = 'Edit Expense';
  populateCatDropdown();
  document.getElementById('exp-date').value    = fld(e,'expenseDate','ExpenseDate','date','Date');
  document.getElementById('exp-cat').value     = fld(e,'category','Category');
  document.getElementById('exp-desc').value    = fld(e,'description','Description');
  document.getElementById('exp-vendor').value  = fld(e,'vendor','Vendor');
  document.getElementById('exp-amount').value  = getAmt(e);
  document.getElementById('exp-mode').value    = fld(e,'paymentMode','PaymentMode','mode','Mode');
  document.getElementById('exp-month').value   = getMonth(e);
  document.getElementById('exp-year').value    = getYear(e);
  document.getElementById('exp-remarks').value = fld(e,'remarks','Remarks');
  document.getElementById('modal-exp').classList.add('open');
}

async function deleteExpense(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  if(!confirm('Delete this expense?')) return;
  try{
    await Api.deleteExpense(id);
    await loadExpenses();
    renderExpenses(); renderDashboard(); toast('Deleted!','warn');
  }catch(err){ toast(err.message || 'Delete failed','warn'); }
}

// ═══════════════════════════════════════════════
// MEMBERS
// ═══════════════════════════════════════════════
function renderMembers(){
  const flSel = document.getElementById('memFloorF');
  const flCur = flSel.value;
  flSel.innerHTML = '<option value="">All Floors</option>'+DB.settings.floors.map(f=>`<option value="${f}">Floor ${f}</option>`).join('');
  flSel.value = flCur;

  const search = document.getElementById('memSearch').value.toLowerCase();
  const floor  = document.getElementById('memFloorF').value;
  const status = document.getElementById('memStatusF').value;

  const data = DB.members.filter(m=>{
    const fl = String(fld(m,'floor','Floor','')).trim();
    const st = fld(m,'status','Status','Active');
    const nm = fld(m,'name','Name','').toLowerCase();
    const ft = String(fld(m,'flat','Flat','')).toLowerCase();
    const mb = fld(m,'mobile','Mobile','').toString();
    return (!floor||fl===floor) && (!status||st===status) && (!search||nm.includes(search)||ft.includes(search)||mb.includes(search));
  });
  renderPage('mem', data, renderMemRow);
}

function renderMemRow(m, i){
  const id  = m.id||m.Id||i;
  const st  = fld(m,'status','Status')||'Active';
  const stk = st.toLowerCase();
  return `<tr>
    <td>${i+1}</td>
    <td>${fld(m,'name','Name')}</td>
    <td>${fld(m,'flat','Flat')}</td>
    <td>Floor ${fld(m,'floor','Floor')}</td>
    <td>${fld(m,'mobile','Mobile')}</td>
    <td>${fld(m,'email','Email')}</td>
    <td><span class="badge b-${stk}">${st}</span></td>
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editMember('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteMember('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

async function saveMember(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const name = document.getElementById('mem-name').value.trim();
  const flat = document.getElementById('mem-flat').value.trim();
  if(!name||!flat) return toast('Fill required fields','warn');
  const payload = {
    name, flat,
    floor: document.getElementById('mem-floor').value,
    mobile: document.getElementById('mem-mobile').value,
    email:  document.getElementById('mem-email').value,
    status: document.getElementById('mem-status').value
  };
  try{
    if(editId.mem){ await Api.updateMember(editId.mem, payload); }
    else { await Api.createMember(payload); }
    await loadMembers();
    closeModal('mem'); renderMembers(); toast('Member saved!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

function editMember(id){
  const m = DB.members.find(x=>String(x.id||x.Id)===String(id)); if(!m) return;
  editId.mem = id;
  document.getElementById('mem-modal-title').textContent = 'Edit Member';
  populateFloorDropdown('mem-floor');
  document.getElementById('mem-name').value   = fld(m,'name','Name');
  document.getElementById('mem-flat').value   = fld(m,'flat','Flat');
  document.getElementById('mem-floor').value  = fld(m,'floor','Floor');
  document.getElementById('mem-mobile').value = fld(m,'mobile','Mobile');
  document.getElementById('mem-email').value  = fld(m,'email','Email');
  document.getElementById('mem-status').value = fld(m,'status','Status')||'Active';
  document.getElementById('modal-mem').classList.add('open');
}

async function deleteMember(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  if(!confirm('Delete this member?')) return;
  try{
    await Api.deleteMember(id);
    await loadMembers();
    renderMembers(); toast('Deleted!','warn');
  }catch(err){ toast(err.message || 'Delete failed','warn'); }
}

// ═══════════════════════════════════════════════
// REPORTS
// ═══════════════════════════════════════════════
function renderReports(){
  const y = topYear() || new Date().getFullYear();
  let html='', tc=0, te=0,tp=0;
  for(let mo=1;mo<=12;mo++){
    const c=DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    const e=DB.expenses.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    const p=DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y&&getStatus(x).toLowerCase()!=='paid').reduce((s,x)=>s+getAmt(x),0);
    if(c||e||p){ tc+=c; te+=e; tp+=p; html+=`<div class="stat-row"><span>${MONTHS[mo]}</span><span style="color:var(--success)">₹${c.toLocaleString('en-IN')}</span><span style="color:var(--danger)">₹${e.toLocaleString('en-IN')}</span><span><a href="#" onclick="showPendingDetails(${mo},${y});return false;" style="color:var(--warn);font-weight:700;text-decoration:underline;">₹${p.toLocaleString('en-IN')}</a></span><span>₹${(c-e).toLocaleString('en-IN')}</span></div>`; }
  }
  html+=`<div class="stat-row"><span>Total</span><span style="color:var(--success)">₹${tc.toLocaleString('en-IN')}</span><span style="color:var(--danger)">₹${te.toLocaleString('en-IN')}</span><span style="color:var(--warn)">₹${tp.toLocaleString('en-IN')}</span><span>₹${(tc-te).toLocaleString('en-IN')}</span></div>`;
  document.getElementById('report-monthly').innerHTML='<div class="stat-row" style="font-weight:700;color:var(--sub)"><span>Month</span><span>Collection</span><span>Expenses</span><span>Pending</span><span>Balance</span></div>'+html;

  const catMap={}, tot=DB.expenses.filter(e=>getYear(e)===y).reduce((s,e)=>{const c=fld(e,'category','Category')||'Other';catMap[c]=(catMap[c]||0)+getAmt(e);return s+getAmt(e);},0);
  document.getElementById('report-category').innerHTML=Object.entries(catMap).sort((a,b)=>b[1]-a[1]).map(([cat,amt])=>`
    <div style="margin-bottom:10px;">
      <div style="display:flex;justify-content:space-between;font-size:12px;margin-bottom:3px;"><span>${cat}</span><span><strong>₹${amt.toLocaleString('en-IN')}</strong> (${tot?Math.round(amt/tot*100):0}%)</span></div>
      <div class="prog-bar"><div class="prog-fill" style="width:${tot?amt/tot*100:0}%"></div></div>
    </div>`).join('')||'<div class="empty">No expense data</div>';

  const m = topMonth();
  const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&getYear(c)===y).map(c=>String(fld(c,'memberId','MemberId')).trim()));
  const deflt=DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim()));
  document.getElementById('report-defaulters').innerHTML=deflt.map(d=>`<tr><td>${fld(d,'name','Name')}</td><td>${fld(d,'flat','Flat')}</td><td>Floor ${fld(d,'floor','Floor')}</td><td><span class="badge b-pending">Pending</span></td></tr>`).join('')||'<tr><td colspan="4" class="empty">No defaulters 🎉</td></tr>';

  if(annualChart) annualChart.destroy();
  const aL=[],aC=[],aE=[];
  for(let mo=1;mo<=12;mo++){
    const c=DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    const e=DB.expenses.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
    if(c||e){aL.push(MONTHS[mo].slice(0,3));aC.push(c);aE.push(e);}
  }
  annualChart=new Chart(document.getElementById('annualChart').getContext('2d'),{type:'line',data:{labels:aL,datasets:[{label:'Collection',data:aC,borderColor:'#6c63ff',fill:false,tension:.4,pointRadius:3},{label:'Expenses',data:aE,borderColor:'#fc8181',fill:false,tension:.4,pointRadius:3}]},options:{responsive:true,maintainAspectRatio:false,plugins:{legend:{labels:{font:{size:10}}}},scales:{x:{grid:{display:false}},y:{ticks:{callback:v=>'₹'+(v/1000)+'k'}}}}});
}

function exportReport(type){
  const wb=XLSX.utils.book_new();
  const y=topYear()||new Date().getFullYear();
  let ws, name;
  if(type==='monthly'){
    const rows=[['Month','Collection','Expenses','Pending','Balance']];
    for(let mo=1;mo<=12;mo++){
      const c=DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
      const e=DB.expenses.filter(x=>getMonth(x)===mo&&getYear(x)===y).reduce((s,x)=>s+getAmt(x),0);
      const p=DB.collections.filter(x=>getMonth(x)===mo&&getYear(x)===y&&getStatus(x).toLowerCase()!=='paid').reduce((s,x)=>s+getAmt(x),0); if(c||e||p) rows.push([MONTHS[mo],c,e,p,c-e]);
    }
    ws=XLSX.utils.aoa_to_sheet(rows); name='Monthly_Report';
  } else {
    const m=topMonth();
    const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&getYear(c)===y).map(c=>String(fld(c,'memberId','MemberId')).trim()));
    const rows=[['Name','Flat','Floor','Status'],...DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim())).map(d=>[fld(d,'name','Name'),fld(d,'flat','Flat'),'Floor '+fld(d,'floor','Floor'),'Pending'])];
    ws=XLSX.utils.aoa_to_sheet(rows); name='Defaulters';
  }
  XLSX.utils.book_append_sheet(wb,ws,name);
  XLSX.writeFile(wb,`${name}_${y}.xlsx`);
  toast('Exported!');
}

// ═══════════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════════
function renderNotifications(){
  const m=topMonth(), y=topYear()||new Date().getFullYear();
  const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&(!y||getYear(c)===y)).map(c=>String(fld(c,'memberId','MemberId')).trim()));
  const pend=DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim()));
  const recent=[...DB.auditLog].reverse().slice(0,5);
  const items=[
    ...pend.map(m=>({color:'#fc8181',text:`⚠️ ${fld(m,'name','Name')} (${fld(m,'flat','Flat')}) – maintenance pending`,time:'Due this period'})),
    ...complaintNotifItems(),
    ...recent.map(a=>({color:'#6c63ff',text:`📝 ${a.action} in ${a.module}: ${a.details}`,time:a.timestamp}))
  ];
  document.getElementById('notif-list').innerHTML=items.map(n=>`<div class="notif-item"><div class="ndot" style="background:${n.color}"></div><div><div class="ntext">${n.text}</div><div class="ntime">${n.time}</div></div></div>`).join('')||'<div class="empty">No notifications 🎉</div>';
  updateNotifBadge();
}

// Complaint notifications flow in both directions: Admins get alerted about
// new/open complaints raised by Members, and Members get alerted whenever an
// Admin actions (status change / resolution) one of their own complaints.
function complaintNotifItems(){
  if(!DB.complaints || !DB.complaints.length) return [];
  if(isAdmin()){
    return [...DB.complaints]
      .filter(c=>c.status==='Open')
      .sort((a,b)=>(b.createdAt||'').localeCompare(a.createdAt||''))
      .map(c=>({
        color:'#ed8936',
        text:`🛠️ New complaint from ${c.raisedByUsername||'a member'}${c.flat?' (Flat '+c.flat+')':''}: "${c.subject}" — ${c.priority} priority`,
        time: c.createdAt || ''
      }));
  }
  return [...DB.complaints]
    .filter(c=>c.status!=='Open' && String(c.raisedByUserId)===String(currentUser && currentUser.id))
    .sort((a,b)=>((b.resolvedAt||b.createdAt||'')).localeCompare((a.resolvedAt||a.createdAt||'')))
    .map(c=>({
      color: (c.status==='Resolved'||c.status==='Closed') ? '#38a169' : '#3182ce',
      text:`🛠️ Your complaint "${c.subject}" is now ${c.status}${c.resolutionNotes ? ' — '+c.resolutionNotes : ''}`,
      time: c.resolvedAt || c.createdAt || ''
    }));
}

function updateNotifBadge(){
  const m=topMonth(),y=topYear()||new Date().getFullYear();
  const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&(!y||getYear(c)===y)).map(c=>String(fld(c,'memberId','MemberId')).trim()));
  const pendCnt=DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim())).length;
  const cmpCnt=complaintNotifItems().length;
  document.getElementById('notifBadge').textContent=pendCnt+cmpCnt;
}

// ═══════════════════════════════════════════════
// AUDIT LOG
// ═══════════════════════════════════════════════
function addAudit(module,action,details){ DB.auditLog.push({id:Date.now(),timestamp:new Date().toLocaleString(),user:(currentUser&&currentUser.username)||'Admin',module,action,details}); }

function renderAudit(){
  const search=document.getElementById('auditSearch').value.toLowerCase();
  const mod=document.getElementById('auditModF').value;
  const data=[...DB.auditLog].reverse().filter(a=>(!mod||a.module===mod)&&(!search||a.details.toLowerCase().includes(search)||a.action.toLowerCase().includes(search)));
  renderPage('audit',data,a=>`<tr><td style="font-size:10px;color:var(--sub)">${a.timestamp}</td><td>${a.user}</td><td>${a.module}</td><td>${a.action}</td><td>${a.details}</td></tr>`);
}

// ═══════════════════════════════════════════════
// SETTINGS
// ═══════════════════════════════════════════════
function loadSettingsUI(){
  const s=DB.settings;
  document.getElementById('set-sname').value=s.societyName||'';
  document.getElementById('set-addr').value=s.address||'';
  document.getElementById('set-email').value=s.email||'';
  document.getElementById('set-phone').value=s.phone||'';
  document.getElementById('set-mamt').value=s.maintenanceAmt||2000;
  document.getElementById('set-wings').value=(s.floors||[]).join(',');
  document.getElementById('set-theme').value=s.theme||'light';
  renderCatList();
}
async function saveSettings(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const payload = currentSettingsPayload({
    societyName: document.getElementById('set-sname').value,
    address: document.getElementById('set-addr').value,
    email: document.getElementById('set-email').value,
    phone: document.getElementById('set-phone').value,
    maintenanceAmt: +document.getElementById('set-mamt').value || 2000,
    floors: document.getElementById('set-wings').value.split(',').map(w=>w.trim()).filter(Boolean)
  });
  try{
    await Api.updateSettings(payload);
    await loadSettingsData();
    document.getElementById('societyLogoSub').textContent = DB.settings.societyName;
    toast('Settings saved!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}
function applySettings(){
  document.getElementById('societyLogoSub').textContent=DB.settings.societyName;
  const homeName = document.getElementById('homeSocietyName');
  if(homeName) homeName.textContent = DB.settings.societyName;
}
async function applyThemeSetting(){
  const t=document.getElementById('set-theme').value;
  document.body.classList.toggle('dark',t==='dark');
  DB.settings.theme=t;
  if(!isAdmin()) return;
  try{ await Api.updateSettings(currentSettingsPayload({theme:t})); }catch(err){ console.error('Could not save theme', err); }
}
function renderCatList(){ document.getElementById('cat-list').innerHTML=DB.settings.categories.map((c,i)=>`<span class="badge b-active" style="cursor:pointer" onclick="removeCategory(${i})">${c} ✕</span>`).join(''); }
async function addCategory(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const v=document.getElementById('new-cat').value.trim();
  if(!v) return;
  if(DB.settings.categories.includes(v)) return toast('Category already exists','warn');
  try{
    await Api.updateSettings(currentSettingsPayload({categories:[...DB.settings.categories, v]}));
    await loadSettingsData();
    document.getElementById('new-cat').value='';
    renderCatList();
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}
async function removeCategory(i){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const categories = DB.settings.categories.filter((_,idx)=>idx!==i);
  try{
    await Api.updateSettings(currentSettingsPayload({categories}));
    await loadSettingsData();
    renderCatList();
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

// ═══════════════════════════════════════════════
// ADMIN
// ═══════════════════════════════════════════════
function renderUsers(){
  document.getElementById('user-tbody').innerHTML=DB.users.map((u,i)=>{
    const st = u.status || 'Active';
    const badgeClass = st==='Active' ? 'active' : st==='Pending' ? 'pending' : 'inactive';
    const approveBtn = st==='Pending' ? `<button class="ic-btn" onclick="approveUser(${u.id})" title="Approve account">✅</button>` : '';
    return `<tr><td>${i+1}</td><td>${u.username}</td><td>${u.role}</td><td>${u.email||'-'}</td><td><span class="badge b-${badgeClass}">${st}</span></td><td><div class="act-btns">${approveBtn}<button class="ic-btn" onclick="resetUserPassword(${u.id})" title="Reset password">🔑</button><button class="ic-btn" onclick="deleteUser(${u.id})">🗑️</button></div></td></tr>`;
  }).join('');
}
async function approveUser(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  try{
    await Api.approveUser(id);
    await loadUsers();
    renderUsers();
    toast('User approved!');
  }catch(err){ toast(err.message || 'Approve failed','warn'); }
}
async function saveUser(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const n=document.getElementById('u-name').value.trim();
  const pw=document.getElementById('u-password').value;
  if(!n)return toast('Enter username','warn');
  if(!pw||pw.length<4)return toast('Password must be at least 4 characters','warn');
  const payload = {
    username: n, password: pw,
    role: document.getElementById('u-role').value,
    email: document.getElementById('u-email').value,
    status: document.getElementById('u-status').value
  };
  try{
    await Api.createUser(payload);
    document.getElementById('u-password').value='';
    await loadUsers();
    closeModal('user'); renderUsers(); toast('User added!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}
async function resetUserPassword(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const user = DB.users.find(u=>u.id===id);
  if(!user) return;
  const pw = prompt(`Enter a new password for "${user.username}":`);
  if(pw===null) return;
  if(pw.length<4) return toast('Password must be at least 4 characters','warn');
  try{
    await Api.resetUserPassword(id, pw);
    toast('Password updated!');
  }catch(err){ toast(err.message || 'Reset failed','warn'); }
}
async function deleteUser(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  if(DB.users.length<=1)return toast('Cannot delete last user','warn');
  if(!confirm('Delete?'))return;
  try{
    await Api.deleteUser(id);
    await loadUsers();
    renderUsers(); toast('Deleted','warn');
  }catch(err){ toast(err.message || 'Delete failed','warn'); }
}

// ═══════════════════════════════════════════════
// PAGINATION
// ═══════════════════════════════════════════════
function renderPage(key, data, rowFn){
  const tbody=document.getElementById(`${key}-tbody`);
  const pagDiv=document.getElementById(`${key}-pagination`);
  if(!tbody) return;
  const total=Math.max(1,Math.ceil(data.length/PER));
  if(pages[key]>total) pages[key]=1;
  const start=(pages[key]-1)*PER;
  tbody.innerHTML=data.slice(start,start+PER).map((r,i)=>rowFn(r,start+i)).join('')||`<tr><td colspan="20" class="empty">No records found</td></tr>`;
  if(!pagDiv) return;
  let pg=`<span style="font-size:11px;color:var(--sub)">${data.length} records</span>`;
  pg+=`<button class="pg-btn" onclick="changePage('${key}',-1)" ${pages[key]<=1?'disabled':''}>‹</button>`;
  for(let p=Math.max(1,pages[key]-2);p<=Math.min(total,pages[key]+2);p++) pg+=`<button class="pg-btn${p===pages[key]?' active':''}" onclick="setPage('${key}',${p})">${p}</button>`;
  pg+=`<button class="pg-btn" onclick="changePage('${key}',1)" ${pages[key]>=total?'disabled':''}>›</button>`;
  pagDiv.innerHTML=pg;
}
function changePage(k,d){ pages[k]+=d; refreshSection(k); }
function setPage(k,p)   { pages[k]=p;  refreshSection(k); }
function refreshSection(k){ if(k==='col')renderCollections(); else if(k==='exp')renderExpenses(); else if(k==='mem')renderMembers(); else if(k==='audit')renderAudit(); else if(k==='cmp')renderComplaints(); }

// ═══════════════════════════════════════════════
// MODAL HELPERS
// ═══════════════════════════════════════════════
function openModal(type){
  if((type==='col'||type==='exp'||type==='mem'||type==='user') && !isAdmin()) return toast('Read-only access — Admin only.','warn');
  editId[type]=null;
  if(type==='col'){ document.getElementById('col-modal-title').textContent='Add Collection'; populateMemberDropdown(); document.getElementById('col-date').value=new Date().toISOString().split('T')[0]; document.getElementById('col-month').value=new Date().getMonth()+1; document.getElementById('col-year').value=new Date().getFullYear(); document.getElementById('col-amount').value=''; document.getElementById('col-remarks').value=''; }
  if(type==='exp'){ document.getElementById('exp-modal-title').textContent='Add Expense'; populateCatDropdown(); document.getElementById('exp-date').value=new Date().toISOString().split('T')[0]; document.getElementById('exp-month').value=new Date().getMonth()+1; document.getElementById('exp-year').value=new Date().getFullYear(); document.getElementById('exp-amount').value=''; document.getElementById('exp-desc').value=''; document.getElementById('exp-vendor').value=''; document.getElementById('exp-remarks').value=''; }
  if(type==='mem'){ document.getElementById('mem-modal-title').textContent='Add Member'; populateFloorDropdown('mem-floor'); document.getElementById('mem-name').value=''; document.getElementById('mem-flat').value=''; document.getElementById('mem-mobile').value=''; document.getElementById('mem-email').value=''; }
  if(type==='cmp'){
    document.getElementById('cmp-modal-title').textContent='Raise Complaint';
    populateComplaintCatDropdown();
    document.getElementById('cmp-subject').value='';
    document.getElementById('cmp-priority').value='Medium';
    document.getElementById('cmp-desc').value='';
    document.getElementById('cmp-status').value='Open';
    document.getElementById('cmp-assigned').value='';
    document.getElementById('cmp-notes').value='';
    const af = document.getElementById('cmp-admin-fields');
    if(af) af.style.display = 'none';
  }
  document.getElementById('modal-'+type).classList.add('open');
}
function closeModal(type){ document.getElementById('modal-'+type).classList.remove('open'); }

function populateMemberDropdown(){
  document.getElementById('col-member').innerHTML=DB.members.filter(m=>(fld(m,'status','Status')||'Active')==='Active').map(m=>`<option value="${fld(m,'id','Id')}">${fld(m,'name','Name')} (${fld(m,'flat','Flat')})</option>`).join('');
}
function populateCatDropdown(){ document.getElementById('exp-cat').innerHTML=DB.settings.categories.map(c=>`<option>${c}</option>`).join(''); }
function populateFloorDropdown(id){ document.getElementById(id).innerHTML=DB.settings.floors.map(f=>`<option value="${f}">Floor ${f}</option>`).join(''); }
function populateComplaintCatDropdown(){ document.getElementById('cmp-category').innerHTML=COMPLAINT_CATEGORIES.map(c=>`<option>${c}</option>`).join(''); }

// ═══════════════════════════════════════════════
// COMPLAINTS
// ═══════════════════════════════════════════════
function renderComplaints(){
  const search  = document.getElementById('cmpSearch') ? document.getElementById('cmpSearch').value.toLowerCase() : '';
  const statusF = document.getElementById('cmpStatusF') ? document.getElementById('cmpStatusF').value : '';

  const data = DB.complaints.filter(c=>{
    const subj = (c.subject||'').toLowerCase();
    const desc = (c.description||'').toLowerCase();
    return (!statusF||c.status===statusF) && (!search||subj.includes(search)||desc.includes(search));
  });

  renderPage('cmp', data, renderCmpRow);

  const openCount     = DB.complaints.filter(c=>c.status==='Open').length;
  const progressCount = DB.complaints.filter(c=>c.status==='In Progress').length;
  const doneCount     = DB.complaints.filter(c=>c.status==='Resolved'||c.status==='Closed').length;
  const summaryEl = document.getElementById('cmp-summary');
  if(summaryEl){
    summaryEl.innerHTML = `
      <div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;padding:10px 14px;background:var(--bg);border-radius:10px;border:1px solid var(--border);width:100%;">
        <div style="text-align:center;padding:6px 16px;background:var(--card);border-radius:8px;border:1px solid var(--border);">
          <div style="font-size:10px;color:var(--sub);margin-bottom:2px;">Total</div>
          <div style="font-size:16px;font-weight:800;">${DB.complaints.length}</div>
        </div>
        <div style="text-align:center;padding:6px 16px;background:#fed7d7;border-radius:8px;">
          <div style="font-size:10px;color:#9b2c2c;margin-bottom:2px;">🔴 Open</div>
          <div style="font-size:16px;font-weight:800;color:#9b2c2c;">${openCount}</div>
        </div>
        <div style="text-align:center;padding:6px 16px;background:#feebc8;border-radius:8px;">
          <div style="font-size:10px;color:#9c4221;margin-bottom:2px;">🟡 In Progress</div>
          <div style="font-size:16px;font-weight:800;color:#9c4221;">${progressCount}</div>
        </div>
        <div style="text-align:center;padding:6px 16px;background:#c6f6d5;border-radius:8px;">
          <div style="font-size:10px;color:#276749;margin-bottom:2px;">🟢 Resolved</div>
          <div style="font-size:16px;font-weight:800;color:#276749;">${doneCount}</div>
        </div>
      </div>`;
  }
}

function renderCmpRow(c, i){
  const statusColors   = {'Open':'#c53030','In Progress':'#c05621','Resolved':'#276749','Closed':'#4a5568'};
  const priorityColors = {'High':'#c53030','Medium':'#c05621','Low':'#276749'};
  const sc = statusColors[c.status] || '#4a5568';
  const pc = priorityColors[c.priority] || '#4a5568';
  const canWithdraw = !isAdmin() && String(c.raisedByUserId)===String(currentUser && currentUser.id) && c.status==='Open';
  const safeDesc = (c.description||'').replace(/"/g,'&quot;');
  return `<tr>
    <td>${i+1}</td>
    <td>${c.createdAt}</td>
    <td title="${safeDesc}">${c.subject}</td>
    <td>${c.category}</td>
    <td><span style="color:${pc};font-weight:700;">${c.priority}</span></td>
    <td><span style="color:${sc};font-weight:700;">${c.status}</span></td>
    <td>${c.raisedByUsername || '-'}${c.flat ? ' (Flat '+c.flat+')' : ''}</td>
    <td><div class="act-btns">
      ${isAdmin() ? `<button class="ic-btn" onclick="editComplaint('${c.id}')">✏️</button><button class="ic-btn" onclick="deleteComplaint('${c.id}')">🗑️</button>` : ''}
      ${canWithdraw ? `<button class="ic-btn" onclick="deleteComplaint('${c.id}')">🗑️ Withdraw</button>` : ''}
    </div></td>
  </tr>`;
}

async function saveComplaint(){
  const subject = document.getElementById('cmp-subject').value.trim();
  const desc    = document.getElementById('cmp-desc').value.trim();
  if(!subject||!desc) return toast('Fill required fields','warn');

  try{
    if(editId.cmp){
      if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
      const payload = {
        subject, description: desc,
        category: document.getElementById('cmp-category').value,
        priority: document.getElementById('cmp-priority').value,
        status: document.getElementById('cmp-status').value,
        resolutionNotes: document.getElementById('cmp-notes').value,
        assignedTo: document.getElementById('cmp-assigned').value
      };
      await Api.updateComplaint(editId.cmp, payload);
    } else {
      const payload = {
        subject, description: desc,
        category: document.getElementById('cmp-category').value,
        priority: document.getElementById('cmp-priority').value
      };
      await Api.createComplaint(payload);
    }
    await loadComplaints();
    closeModal('cmp'); renderComplaints(); updateNotifBadge();
    toast(editId.cmp ? 'Complaint updated — member will be notified.' : 'Complaint submitted — admin will be notified.');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

function editComplaint(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const c = DB.complaints.find(x=>String(x.id)===String(id)); if(!c) return;
  editId.cmp = id;
  document.getElementById('cmp-modal-title').textContent = 'Manage Complaint';
  populateComplaintCatDropdown();
  document.getElementById('cmp-subject').value  = c.subject;
  document.getElementById('cmp-category').value = c.category;
  document.getElementById('cmp-priority').value  = c.priority;
  document.getElementById('cmp-desc').value     = c.description;
  document.getElementById('cmp-status').value    = c.status;
  document.getElementById('cmp-assigned').value  = c.assignedTo || '';
  document.getElementById('cmp-notes').value     = c.resolutionNotes || '';
  const af = document.getElementById('cmp-admin-fields');
  if(af) af.style.display = 'block';
  document.getElementById('modal-cmp').classList.add('open');
}

async function deleteComplaint(id){
  const c = DB.complaints.find(x=>String(x.id)===String(id));
  const msg = (c && !isAdmin()) ? 'Withdraw this complaint?' : 'Delete this complaint?';
  if(!confirm(msg)) return;
  try{
    await Api.deleteComplaint(id);
    await loadComplaints();
    renderComplaints(); updateNotifBadge();
    toast('Complaint removed.','warn');
  }catch(err){ toast(err.message || 'Delete failed','warn'); }
}

// ═══════════════════════════════════════════════
// EXPORT
// ═══════════════════════════════════════════════
function saveToExcel(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const wb=XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.members.length?DB.members:[{note:'No data'}]),'Members');
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.collections.length?DB.collections:[{note:'No data'}]),'Collections');
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.expenses.length?DB.expenses:[{note:'No data'}]),'Expenses');
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.auditLog.length?DB.auditLog:[{note:'No data'}]),'AuditLog');
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet(DB.users.length?DB.users:[{note:'No data'}]),'Users');
  XLSX.utils.book_append_sheet(wb,XLSX.utils.json_to_sheet([DB.settings]),'Settings');
  const name=`SMMS_${DB.settings.societyName.replace(/\s+/g,'_')}_${new Date().toISOString().split('T')[0]}.xlsx`;
  XLSX.writeFile(wb,name);
  addAudit('Export','Export','Full data exported');
  toast('Excel saved: '+name);
}

// ═══════════════════════════════════════════════
// THEME & TOAST
// ═══════════════════════════════════════════════
function toggleTheme(){ const d=document.body.classList.toggle('dark'); document.querySelector('.theme-btn').textContent=d?'☀️':'🌙'; }
function toast(msg,type='success'){ const t=document.getElementById('toast'); t.textContent=msg; t.style.background=type==='warn'?'#c53030':type==='info'?'#2b6cb0':'#276749'; t.style.display='block'; setTimeout(()=>t.style.display='none',2800); }

function closePendingModal(){
 document.getElementById('modal-pending').classList.remove('open');
}

function sendWhatsAppReminder(memberName, months, amount){
 const msg=`Dear ${memberName},

Maintenance dues are pending for:
${months}

Amount Due: ₹${amount}

Please clear the dues.

Regards,
Society Committee`;

 window.open('https://wa.me/?text='+encodeURIComponent(msg),'_blank');
}

function showPendingDetails(month, year){
 const pending=(DB.collections||[]).filter(c=>
   getMonth(c)===month &&
   getYear(c)===year &&
   (getStatus(c)||'').toLowerCase()!=='paid'
 );

 const grouped={};

 pending.forEach(c=>{
   const key=(c.memberId||c.MemberId||c.memberName||c.MemberName);
   if(!grouped[key]){
      grouped[key]={
        name:c.memberName||c.MemberName||'Member',
        flat:c.flat||c.Flat||'',
        months:[],
        due:0
      };
   }
   grouped[key].months.push((MONTHS[getMonth(c)]||'').substring(0,3));
   grouped[key].due += getAmt(c);
 });

 let body='';
 Object.values(grouped).forEach(m=>{
   body += `
   <div class="card">
      <div style="font-weight:700">${m.name} (Flat ${m.flat})</div>
      <div style="margin-top:6px">Pending Months: ${m.months.join(', ')}</div>
      <div style="margin-top:6px">
        Due: <span class="badge b-unpaid">₹${m.due.toLocaleString('en-IN')}</span>
      </div>
      <div style="margin-top:10px">
        <button class="btn btn-success"
         onclick="sendWhatsAppReminder('${m.name}','${m.months.join(', ')}','${m.due}')">
         📱 WhatsApp Reminder
        </button>
      </div>
   </div>`;
 });

 document.getElementById('pending-title').innerHTML=(MONTHS[month]||month)+' '+year+' Pending Details';
 document.getElementById('pending-details-body').innerHTML=body||'<div class="empty">No pending records</div>';
 document.getElementById('modal-pending').classList.add('open');
}

function pendingAmountLink(month, year, amount){
 return `<a href="#" data-pending-month="${month}" data-pending-year="${year}"
 style="color:#d69e2e;font-weight:700;text-decoration:none">₹${Number(amount||0).toLocaleString('en-IN')}</a>`;
}

document.addEventListener('click', function(e){
  const el = e.target.closest('[data-pending-month]');
  if(!el) return;
  e.preventDefault();
  showPendingDetails(
    parseInt(el.getAttribute('data-pending-month')),
    parseInt(el.getAttribute('data-pending-year'))
  );
});

(function(){

   var _set = window.setInterval(function(){

      if(typeof renderReports !== 'function')
         return;

      clearInterval(_set);

      const original = renderReports;

      window.renderReports = function(){

         original.apply(this, arguments);

         try{

            const container =
               document.getElementById('report-monthly');

            if(!container)
               return;

            container
            .querySelectorAll('.pending-amount')
            .forEach(el=>{

               const month =
                  el.getAttribute('data-month');

               const year =
                  el.getAttribute('data-year');

               el.style.cursor='pointer';

               el.onclick=function(e){

                  e.preventDefault();

                  showPendingDetails(
                     parseInt(month),
                     parseInt(year)
                  );
               };

            });

         }catch(e){
            console.log(e);
         }
      };

   },500);

})();


