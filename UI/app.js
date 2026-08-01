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
    societyName:'', address:'', email:'', phone:'',
    registrationNumber:'', gst:'', pan:'', logoBase64:'',
    maintenanceAmt:2000, dueDay:5, lateFee:100, graceDays:5, billingDay:1, autoGenerateInvoices:false, financialYear:'',
    floors:['1','2','3','4','5'],
    categories:['Security','Housekeeping','Electricity','Water','Repairs','Lift Maintenance','Gardening','Festival','CCTV','Miscellaneous'],
    primaryColor:'#6c63ff', secondaryColor:'#1a1f36', applicationTitle:''
  }
};
let editId   = {col:null, exp:null, mem:null, cmp:null, user:null};
let pages    = {col:1, exp:1, mem:1, audit:1, cmp:1};
let trendChart, pieChart, annualChart;
let currentUser = null;
// Resident self-service snapshot from GET /api/me (profile + payments + dues + complaint counts).
let ME = null;

// ═══════════════════════════════════════════════
// AUTH — login/signup/forgot-password all happen on home.html against the
// SQL-backed API (api/SMMS.Api). index.html only ever consumes the JWT +
// user info handed off via sessionStorage (see api.js: apiGetSession /
// apiGetToken / apiClearSession).
// ═══════════════════════════════════════════════
function isAdmin(){ return !!currentUser && currentUser.role === 'Admin'; }

// Grantable module permission system — mirrors api/SMMS.Api/Services/PermissionService.cs.
// Users/AuditLog are deliberately NOT grantable (stay Admin-only).
const PERMISSION_MODULES = ['Collections','Expenses','Members','Complaints','Settings'];
function canView(module){ return isAdmin() || (currentUser && currentUser.permissions && ['View','Edit'].includes(currentUser.permissions[module])); }
function canEdit(module){ return isAdmin() || (currentUser && currentUser.permissions && currentUser.permissions[module]==='Edit'); }

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
    societyName: s.societyName || 'Society',
    address: s.address || '',
    email: s.email || '',
    phone: s.phone || '',
    registrationNumber: s.registrationNumber || '',
    gst: s.gst || '',
    pan: s.pan || '',
    logoBase64: s.logoBase64 || '',
    maintenanceAmt: s.maintenanceAmt || 2000,
    dueDay: s.dueDay || 5,
    lateFee: s.lateFee || 100,
    graceDays: s.graceDays || 5,
    billingDay: s.billingDay || 1,
    autoGenerateInvoices: !!s.autoGenerateInvoices,
    financialYear: s.financialYear || '',
    floors: (s.floors && s.floors.length) ? s.floors : ['1','2','3','4','5'],
    towers: (s.towers && s.towers.length) ? s.towers : [],
    flatTypes: (s.flatTypes && s.flatTypes.length) ? s.flatTypes : ['1 BHK','1.5 BHK','2 BHK','2.5 BHK','3 BHK'],
    maintenanceCalcMethod: s.maintenanceCalcMethod || 'FixedAmount',
    categories: (s.categories && s.categories.length) ? s.categories : DB.settings.categories,
    theme: s.theme || 'light',
    primaryColor: s.primaryColor || '#6c63ff',
    secondaryColor: s.secondaryColor || '#1a1f36',
    applicationTitle: s.applicationTitle || ''
  };
}

function currentSettingsPayload(overrides = {}){
  return {
    societyName: DB.settings.societyName,
    address: DB.settings.address,
    email: DB.settings.email,
    phone: DB.settings.phone,
    registrationNumber: DB.settings.registrationNumber,
    gst: DB.settings.gst,
    pan: DB.settings.pan,
    logoBase64: DB.settings.logoBase64,
    maintenanceAmt: DB.settings.maintenanceAmt,
    dueDay: DB.settings.dueDay,
    lateFee: DB.settings.lateFee,
    graceDays: DB.settings.graceDays,
    billingDay: DB.settings.billingDay,
    autoGenerateInvoices: DB.settings.autoGenerateInvoices,
    financialYear: DB.settings.financialYear,
    floors: DB.settings.floors,
    towers: DB.settings.towers,
    flatTypes: DB.settings.flatTypes,
    maintenanceCalcMethod: DB.settings.maintenanceCalcMethod,
    categories: DB.settings.categories,
    theme: DB.settings.theme || 'light',
    primaryColor: DB.settings.primaryColor || '#6c63ff',
    secondaryColor: DB.settings.secondaryColor,
    applicationTitle: DB.settings.applicationTitle,
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
  if(isAdmin()){
    await Promise.all([loadMembers(), loadSettingsData()]);
    await Promise.all([loadCollections(), loadExpenses(), loadComplaints()]);
    await Promise.all([loadUsers(), loadAuditLogData()]);
  } else {
    // Residents get their self-service snapshot (/api/me) plus a READ-ONLY view
    // of society-wide finances (Dashboard/Collections/Expenses). Members hold
    // default "View" permission on these modules, so the API allows the reads;
    // edit/add buttons stay hidden because they require "Edit" (see canEdit()).
    await Promise.all([loadMe(), loadSettingsData(), loadMembers()]);
    await Promise.all([loadCollections(), loadExpenses(), loadComplaints()]);
  }
}

async function loadMe(){
  ME = await Api.getMe();
}

function applyRolePermissions(){
  const admin = isAdmin();
  document.body.classList.toggle('role-member', !admin);
  // Elements tagged data-perm="Module" require Edit on that module to be shown;
  // data-perm-view="Module" only requires View. Admin always passes both.
  document.querySelectorAll('[data-perm]').forEach(el=>{
    el.style.display = canEdit(el.getAttribute('data-perm')) ? '' : 'none';
  });
  document.querySelectorAll('[data-perm-view]').forEach(el=>{
    el.style.display = canView(el.getAttribute('data-perm-view')) ? '' : 'none';
  });
  ['set-sname','set-addr','set-email','set-phone','set-wings','new-cat'].forEach(id=>{
    const el = document.getElementById(id);
    if(el) el.disabled = !canEdit('Settings');
  });
}

function updateSidebarUserInfo(){
  if(!currentUser) return;
  const av = document.getElementById('sbUav');
  const nm = document.getElementById('sbUname');
  const rl = document.getElementById('sbUrole');
  const displayName = (!isAdmin() && ME && ME.name) ? ME.name : currentUser.username;
  if(av) av.textContent = displayName.charAt(0).toUpperCase();
  if(nm) nm.textContent = displayName;
  const ROLE_LABELS = { Admin:'Administrator', Member:'Resident' };
  if(rl) rl.textContent = ROLE_LABELS[currentUser.role] || currentUser.role;
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

// Decode a JWT payload (base64url) into a claims object. Client-side only —
// used to build a UI session from an impersonation token; the API still
// validates the signature on every request.
function decodeJwt(token){
  try{
    const part = token.split('.')[1];
    const b64 = part.replace(/-/g,'+').replace(/_/g,'/');
    const json = decodeURIComponent(escape(atob(b64)));
    return JSON.parse(json);
  }catch(e){ return null; }
}

// If the URL carries `#imp=<token>` (super-admin "Login as society admin"),
// turn it into a normal tenant session so the dashboard renders as that admin.
function consumeImpersonationToken(){
  const hash = window.location.hash || '';
  if(!hash.startsWith('#imp=')) return;
  const token = decodeURIComponent(hash.slice(5));
  const c = decodeJwt(token);
  if(!c){ return; }
  const NAME = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name';
  const ROLE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';
  const perms = {};
  Object.keys(c).forEach(k => { if(k.startsWith('perm:')) perms[k.slice(5)] = c[k]; });
  apiSetSession(token, c.sub, c[NAME] || c.unique_name || 'admin', c[ROLE] || 'Admin', perms);
  // Remember we're impersonating (and who) so we can show a banner. `imp` is
  // the super-admin username embedded in the token by the platform API.
  sessionStorage.setItem('smms_imp', c.imp || '1');
  // Scrub the token out of the address bar.
  history.replaceState(null, '', window.location.pathname + window.location.search);
}

// Show a persistent banner while impersonating, with a quick exit that clears
// the session and closes the tab (or returns to the platform console).
function maybeShowImpersonationBanner(){
  if(!sessionStorage.getItem('smms_imp')) return;
  if(document.getElementById('impBanner')) return;
  const bar = document.createElement('div');
  bar.id = 'impBanner';
  bar.style.cssText = 'position:fixed;top:0;left:0;right:0;z-index:9999;background:#e0952b;color:#231a05;'
    + 'font:600 13px/1.4 "Segoe UI",sans-serif;padding:8px 16px;display:flex;align-items:center;'
    + 'justify-content:center;gap:14px;box-shadow:0 2px 8px rgba(0,0,0,.2)';
  const who = sessionStorage.getItem('smms_imp');
  bar.innerHTML = `<span>🔓 Impersonating this society (by <b>${who}</b>). Actions are audit-logged.</span>`;
  const btn = document.createElement('button');
  btn.textContent = 'Exit impersonation';
  btn.style.cssText = 'background:#231a05;color:#fff;border:none;border-radius:6px;padding:4px 12px;cursor:pointer;font-weight:600';
  btn.onclick = () => { sessionStorage.removeItem('smms_imp'); apiClearSession(); window.close(); window.location.href = 'home.html'; };
  bar.appendChild(btn);
  document.body.appendChild(bar);
  document.body.style.paddingTop = '38px';
}

window.onload = async () => {
  // Super-admin impersonation hand-off: the platform console opens this page
  // as `index.html#imp=<tenant-token>`. Decode the JWT client-side to build a
  // session, then strip the fragment so a refresh/back-nav can't replay the
  // token from the URL. This runs BEFORE the normal session check so an
  // impersonated tab never bounces to home.html.
  consumeImpersonationToken();

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

  maybeShowImpersonationBanner();

  currentUser = {id: session.id, username: session.username, role: session.role, permissions: session.permissions || {}};
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

  if(isAdmin()){
    applySettings();
    await ensurePartial('dashboard');
    renderDashboard();
    refreshPayBadge();
  } else {
    // Residents land on their own dashboard instead of the admin overview.
    applySettings();
    updateSidebarUserInfo();
    showTab('mdash', document.getElementById('mnav-dash'));
  }
};

// Guard against the browser back/forward cache (bfcache) restoring the
// authenticated dashboard after logout: window.onload does NOT re-run on a
// bfcache restore, so re-check the session on every pageshow. If the session
// is gone (e.g. the user logged out then pressed Back), hide the dashboard
// instantly and replace this entry with the public landing page so the
// forward button can't bring it back either.
window.addEventListener('pageshow', () => {
  if(!apiGetSession() || !apiGetToken()){
    document.body.classList.add('logged-out');
    window.location.replace('home.html');
  }
});

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
/* Mobile off-canvas sidebar toggle (see .menu-toggle / .sidebar-backdrop in styles.css) */
function toggleSidebar(){
  document.querySelector('.sidebar').classList.toggle('mobile-open');
  const bd = document.getElementById('sidebarBackdrop');
  if(bd) bd.classList.toggle('show');
}
function closeSidebar(){
  document.querySelector('.sidebar').classList.remove('mobile-open');
  const bd = document.getElementById('sidebarBackdrop');
  if(bd) bd.classList.remove('show');
}
// --- Lazy partial loader (Option B) -------------------------------------
// Tabs marked with data-partial="name" have their markup in partials/name.html
// and are fetched + injected the first time the tab is opened (once only).
const _partialsLoaded = new Set();
async function ensurePartial(tab){
  const host = document.getElementById('tab-'+tab);
  if(!host) return;
  const name = host.dataset.partial;
  if(!name || _partialsLoaded.has(name)) return;      // inline tab, or already loaded
  try{
    const res = await fetch(`partials/${name}.html`, { cache: 'no-cache' });
    if(!res.ok) throw new Error(`HTTP ${res.status}`);
    host.innerHTML = await res.text();
    _partialsLoaded.add(name);
    // Newly injected data-perm / data-perm-view nodes must be re-gated for the
    // current user, since applyRolePermissions() already ran once at login.
    if(typeof applyRolePermissions === 'function') applyRolePermissions();
  }catch(err){
    host.innerHTML = `<div class="card"><p>⚠ Failed to load this section: ${err.message}</p></div>`;
    if(typeof toast === 'function') toast('Failed to load '+name+': '+err.message,'warn');
  }
}

async function showTab(t, el){
  if(t==='admin' && !isAdmin()) return toast('Admin access required.','warn');
  if(t==='auditlog' && !isAdmin()) return toast('Admin access required.','warn');
  document.querySelectorAll('.tab-content').forEach(x => x.classList.remove('active'));
  document.querySelectorAll('.nav-item').forEach(x => x.classList.remove('active'));
  document.getElementById('tab-'+t).classList.add('active');
  if(el) el.classList.add('active');
  const titles = {dashboard:'Dashboard',collections:'Collections',expenses:'Expenses',members:'Members',complaints:'Complaints',reports:'Reports & Analytics',importexport:'Export',notifications:'Notifications',auditlog:'Audit Log',settings:'Settings',maintenance:'Collection Categories',admin:'Admin Panel',payments:'Payment Verifications',mdash:'Dashboard',mpay:'My Payments',mreceipts:'Receipts',mcomplaints:'My Complaints',mnotices:'Notices',mhome:'My Home'};
  document.getElementById('pageTitle').textContent = titles[t] || t;
  closeSidebar();

  // The shared month/year filter only applies to data-driven tabs; hide it
  // elsewhere (settings, profile, etc.). Members get the same read-only filter
  // on the society tabs they can view.
  const FILTER_TABS = ['dashboard','collections','expenses','reports','notifications'];
  const tf = document.getElementById('topFilters');
  if(tf) tf.style.display = FILTER_TABS.includes(t) ? 'flex' : 'none';

  // Sync the shared month/year dropdowns to this tab's own remembered filter —
  // Dashboard defaults to All Years for the full picture, while every other
  // tab keeps its own last-used (default: current year) selection.
  syncFilterControls(t);

  // Load this tab's markup on first open (no-op for inline tabs).
  await ensurePartial(t);

  // Audit log & users are admin-only server-side — fetch the latest each
  // time these tabs are opened instead of relying on the snapshot loaded at login.
  if(t==='auditlog'){
    try{ await loadAuditLogData(); }catch(err){ toast('Failed to load audit log: '+err.message,'warn'); }
  }
  if(t==='admin'){
    try{ await loadUsers(); }catch(err){ toast('Failed to load users: '+err.message,'warn'); }
  }

  const renders = {dashboard:renderDashboard, collections:renderCollections, expenses:renderExpenses, members:renderMembers, complaints:renderComplaints, reports:renderReports, notifications:renderNotifications, auditlog:renderAudit, settings:loadSettingsUI, maintenance:(typeof renderMaintenance==='function'?renderMaintenance:null), admin:renderUsers, payments:renderPaymentsAdmin, mdash:renderMemberDashboard, mpay:renderMemberPayments, mreceipts:renderMemberReceipts, mcomplaints:renderMemberComplaints, mnotices:renderMemberNotices, mhome:renderMemberHome};
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
  if(!document.getElementById('kpi-col')) return; // partial not loaded yet
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

  const advTotal = DB.members.reduce((s,m)=> s + (+fld(m,'advanceBalance','AdvanceBalance')||0), 0);
  const advHolders = DB.members.filter(m=> (+fld(m,'advanceBalance','AdvanceBalance')||0) > 0).length;
  setText('kpi-adv','₹'+advTotal.toLocaleString('en-IN'));
  setText('kpi-adv-sub', advHolders+' member'+(advHolders===1?'':'s')+' hold credit');

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
  if(!document.getElementById('col-tbody')) return;   // partial not loaded yet
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
    <td>${canEdit('Collections') ? `<div class="act-btns">
      <button class="ic-btn" onclick="editCollection('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteCollection('${id}')">🗑️</button>
    </div>` : ''}</td>
  </tr>`;
}

async function saveCollection(){
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
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
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
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
  if(!document.getElementById('exp-tbody')) return;   // partial not loaded yet
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
    <td>${canEdit('Expenses') ? `<div class="act-btns">
      <button class="ic-btn" onclick="editExpense('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteExpense('${id}')">🗑️</button>
    </div>` : ''}</td>
  </tr>`;
}

async function saveExpense(){
  if(!canEdit('Expenses')) return toast('You do not have edit access to Expenses.','warn');
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
  if(!canEdit('Expenses')) return toast('You do not have edit access to Expenses.','warn');
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
  if(!document.getElementById('mem-tbody')) return;   // partial not loaded yet
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
  const bal = +fld(m,'advanceBalance','AdvanceBalance')||0;
  const walletBtn = canView('Collections')
    ? `<button class="ic-btn" title="Wallet${bal>0?' ₹'+bal.toLocaleString('en-IN'):''}" onclick="openWallet('${id}')"><svg viewBox="0 0 24 24" width="1em" height="1em" style="vertical-align:-.15em" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4"/><path d="M3 5v14a2 2 0 0 0 2 2h16v-5"/><path d="M18 12a2 2 0 0 0 0 4h4v-4z"/></svg>${bal>0?`<span class="wallet-dot"></span>`:''}</button>`
    : '';
  return `<tr>
    <td>${i+1}</td>
    <td>${fld(m,'name','Name')}</td>
    <td>${fld(m,'flat','Flat')}</td>
    <td>Floor ${fld(m,'floor','Floor')}</td>
    <td>${fld(m,'mobile','Mobile')}</td>
    <td>${fld(m,'email','Email')}</td>
    <td><span class="badge b-${stk}">${st}</span></td>
    <td><div class="act-btns">
      ${walletBtn}
      ${canEdit('Members') ? `<button class="ic-btn" onclick="editMember('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteMember('${id}')">🗑️</button>` : ''}
    </div></td>
  </tr>`;
}

async function saveMember(){
  if(!canEdit('Members')) return toast('You do not have edit access to Members.','warn');
  const name = document.getElementById('mem-name').value.trim();
  const flat = document.getElementById('mem-flat').value.trim();
  if(!name||!flat) return toast('Fill required fields','warn');
  const payload = {
    name, flat,
    floor: document.getElementById('mem-floor').value,
    areaSqFt: parseFloat(document.getElementById('mem-area').value) || 0,
    flatType: document.getElementById('mem-flattype').value.trim() || null,
    tower: document.getElementById('mem-tower').value.trim() || null,
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
  populateTowerDatalist();
  document.getElementById('mem-name').value   = fld(m,'name','Name');
  document.getElementById('mem-flat').value   = fld(m,'flat','Flat');
  document.getElementById('mem-floor').value  = fld(m,'floor','Floor');
  document.getElementById('mem-area').value     = fld(m,'areaSqFt','AreaSqFt')||'';
  populateFlatTypeDropdown(fld(m,'flatType','FlatType')||'');
  document.getElementById('mem-tower').value    = fld(m,'tower','Tower')||'';
  document.getElementById('mem-mobile').value = fld(m,'mobile','Mobile');
  document.getElementById('mem-email').value  = fld(m,'email','Email');
  document.getElementById('mem-status').value = fld(m,'status','Status')||'Active';
  document.getElementById('modal-mem').classList.add('open');
}

async function deleteMember(id){
  if(!canEdit('Members')) return toast('You do not have edit access to Members.','warn');
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
  if(!document.getElementById('report-defaulters')) return; // partial not loaded yet
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

  renderAdvanceReports();
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

// ── Advance wallet reports ──
let advBalancesCache=[], advDeductionsCache=[];
async function renderAdvanceReports(){
  if(!document.getElementById('report-adv-balances')) return;
  if(!canView('Collections')){
    document.getElementById('report-adv-balances').innerHTML='<tr><td colspan="4" class="empty">No access</td></tr>';
    document.getElementById('report-adv-deductions').innerHTML='<tr><td colspan="5" class="empty">No access</td></tr>';
    return;
  }
  try{
    advBalancesCache=await Api.getAdvanceBalances();
    const tot=advBalancesCache.reduce((s,r)=>s+(+fld(r,'balance','Balance')||0),0);
    document.getElementById('report-adv-balances').innerHTML=advBalancesCache.length? advBalancesCache.map(r=>`
      <tr><td>${fld(r,'name','Name')}</td><td>${fld(r,'flat','Flat')}</td><td>${fld(r,'mode','Mode')}</td>
      <td style="text-align:right">₹${(+fld(r,'balance','Balance')||0).toLocaleString('en-IN')}</td></tr>`).join('')
      +`<tr style="font-weight:700"><td colspan="3">Total</td><td style="text-align:right">₹${tot.toLocaleString('en-IN')}</td></tr>`
      : '<tr><td colspan="4" class="empty">No members hold advance credit.</td></tr>';
  }catch(err){ document.getElementById('report-adv-balances').innerHTML=`<tr><td colspan="4" class="empty">${err.message||'Failed'}</td></tr>`; }
  loadAdvanceDeductions();
}
async function loadAdvanceDeductions(){
  const el=document.getElementById('report-adv-deductions'); if(!el) return;
  const from=document.getElementById('adv-ded-from').value||'';
  const to=document.getElementById('adv-ded-to').value||'';
  try{
    advDeductionsCache=await Api.getAdvanceDeductions(from,to);
    el.innerHTML=advDeductionsCache.length? advDeductionsCache.map(r=>`
      <tr><td>${new Date(fld(r,'date','Date')).toLocaleDateString('en-IN')}</td>
      <td>${fld(r,'memberName','MemberName')} (${fld(r,'flat','Flat')})</td>
      <td style="text-align:right;color:var(--danger)">−₹${(+fld(r,'amount','Amount')||0).toLocaleString('en-IN')}</td>
      <td>${walletSourceLabel(fld(r,'source','Source'))}</td>
      <td class="muted">${fld(r,'note','Note')||''}</td></tr>`).join('')
      : '<tr><td colspan="5" class="empty">No deductions in this range.</td></tr>';
  }catch(err){ el.innerHTML=`<tr><td colspan="5" class="empty">${err.message||'Failed'}</td></tr>`; }
}
function exportAdvanceReport(type){
  const wb=XLSX.utils.book_new();
  let ws,name;
  if(type==='balances'){
    const rows=[['Member','Flat','Mode','Balance'],...advBalancesCache.map(r=>[fld(r,'name','Name'),fld(r,'flat','Flat'),fld(r,'mode','Mode'),+fld(r,'balance','Balance')||0])];
    ws=XLSX.utils.aoa_to_sheet(rows); name='Advance_Balances';
  }else{
    const rows=[['Date','Member','Flat','Amount','Balance After','Source','Note'],...advDeductionsCache.map(r=>[new Date(fld(r,'date','Date')).toLocaleDateString('en-IN'),fld(r,'memberName','MemberName'),fld(r,'flat','Flat'),+fld(r,'amount','Amount')||0,+fld(r,'balanceAfter','BalanceAfter')||0,fld(r,'source','Source'),fld(r,'note','Note')||''])];
    ws=XLSX.utils.aoa_to_sheet(rows); name='Advance_Deductions';
  }
  XLSX.utils.book_append_sheet(wb,ws,name);
  XLSX.writeFile(wb,`${name}.xlsx`);
  toast('Exported!');
}

// ═══════════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════════
function renderNotifications(){
  if(!document.getElementById('notif-list')) return;   // partial not loaded yet
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
  if(!document.getElementById('audit-tbody')) return;   // partial not loaded yet
  const search=document.getElementById('auditSearch').value.toLowerCase();
  const mod=document.getElementById('auditModF').value;
  const data=[...DB.auditLog].reverse().filter(a=>(!mod||a.module===mod)&&(!search||a.details.toLowerCase().includes(search)||a.action.toLowerCase().includes(search)));
  renderPage('audit',data,a=>`<tr><td style="font-size:10px;color:var(--sub)">${a.timestamp}</td><td>${a.user}</td><td>${a.module}</td><td>${a.action}</td><td>${a.details}</td></tr>`);
}

// ═══════════════════════════════════════════════
// SETTINGS
// ═══════════════════════════════════════════════
function loadSettingsUI(){
  if(!document.getElementById('set-sname')) return;   // partial not loaded yet
  const s=DB.settings;
  document.getElementById('set-sname').value=s.societyName||'';
  document.getElementById('set-addr').value=s.address||'';
  document.getElementById('set-email').value=s.email||'';
  document.getElementById('set-phone').value=s.phone||'';
  document.getElementById('set-regno').value=s.registrationNumber||'';
  document.getElementById('set-gst').value=s.gst||'';
  document.getElementById('set-pan').value=s.pan||'';
  document.getElementById('set-dueday').value=s.dueDay||5;
  document.getElementById('set-latefee').value=s.lateFee||100;
  document.getElementById('set-gracedays').value=s.graceDays||5;
  document.getElementById('set-billingday').value=s.billingDay||1;
  document.getElementById('set-autogen').checked=!!s.autoGenerateInvoices;
  document.getElementById('set-fy').value=s.financialYear||'';
  document.getElementById('set-wings').value=(s.floors||[]).join(',');
  if(document.getElementById('set-towers')) document.getElementById('set-towers').value=(s.towers||[]).join(',');
  if(document.getElementById('set-flattypes')) document.getElementById('set-flattypes').value=(s.flatTypes||[]).join(',');
  document.getElementById('set-theme').value=s.theme||'light';
  document.getElementById('set-apptitle').value=s.applicationTitle||'';
  document.getElementById('set-primary').value=s.primaryColor||'#6c63ff';
  document.getElementById('set-secondary').value=s.secondaryColor||'#1a1f36';
  renderLogoPreview();
  renderCatList();
}
function renderLogoPreview(){
  const el = document.getElementById('set-logo-preview');
  el.innerHTML = DB.settings.logoBase64
    ? `<img src="${DB.settings.logoBase64}" style="max-height:60px;border-radius:6px;">`
    : '';
}
function handleLogoUpload(evt){
  const file = evt.target.files && evt.target.files[0];
  if(!file) return;
  const reader = new FileReader();
  reader.onload = () => {
    DB.settings.logoBase64 = reader.result;
    renderLogoPreview();
  };
  reader.readAsDataURL(file);
}
async function saveSettings(){
  if(!canEdit('Settings')) return toast('You do not have edit access to Settings.','warn');
  const payload = currentSettingsPayload({
    societyName: document.getElementById('set-sname').value,
    address: document.getElementById('set-addr').value,
    email: document.getElementById('set-email').value,
    phone: document.getElementById('set-phone').value,
    registrationNumber: document.getElementById('set-regno').value,
    gst: document.getElementById('set-gst').value,
    pan: document.getElementById('set-pan').value,
    logoBase64: DB.settings.logoBase64,
    dueDay: +document.getElementById('set-dueday').value || 5,
    lateFee: +document.getElementById('set-latefee').value || 0,
    graceDays: +document.getElementById('set-gracedays').value || 0,
    billingDay: +document.getElementById('set-billingday').value || 1,
    autoGenerateInvoices: document.getElementById('set-autogen').checked,
    financialYear: document.getElementById('set-fy').value,
    floors: document.getElementById('set-wings').value.split(',').map(w=>w.trim()).filter(Boolean),
    towers: (document.getElementById('set-towers')?.value || '').split(',').map(w=>w.trim()).filter(Boolean),
    flatTypes: (document.getElementById('set-flattypes')?.value || '').split(',').map(w=>w.trim()).filter(Boolean),
    applicationTitle: document.getElementById('set-apptitle').value,
    primaryColor: document.getElementById('set-primary').value,
    secondaryColor: document.getElementById('set-secondary').value
  });
  try{
    await Api.updateSettings(payload);
    await loadSettingsData();
    document.getElementById('societyLogoSub').textContent = DB.settings.societyName;
    applySettings();
    toast('Settings saved!');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}
function applySettings(){
  document.getElementById('societyLogoSub').textContent=DB.settings.societyName;
  document.title = `${DB.settings.applicationTitle || DB.settings.societyName + ' – Society Maintenance Portal'}`;
  const homeName = document.getElementById('homeSocietyName');
  if(homeName) homeName.textContent = DB.settings.societyName;
  document.documentElement.style.setProperty('--accent', DB.settings.primaryColor || '#6c63ff');
  if(DB.settings.secondaryColor) document.documentElement.style.setProperty('--sidebar', DB.settings.secondaryColor);
  const logoIcon = document.getElementById('logoIcon');
  if(logoIcon){
    logoIcon.innerHTML = DB.settings.logoBase64
      ? `<img src="${DB.settings.logoBase64}" style="width:100%;height:100%;object-fit:cover;border-radius:8px;">`
      : '🏢';
  }
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
  if(!canEdit('Settings')) return toast('You do not have edit access to Settings.','warn');
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
  if(!canEdit('Settings')) return toast('You do not have edit access to Settings.','warn');
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
  if(!document.getElementById('user-tbody')) return;   // partial not loaded yet
  document.getElementById('user-tbody').innerHTML=DB.users.map((u,i)=>{
    const st = u.status || 'Active';
    const badgeClass = st==='Active' ? 'active' : st==='Pending' ? 'pending' : 'inactive';
    const approveBtn = st==='Pending' ? `<button class="ic-btn" onclick="approveUser(${u.id})" title="Approve account">✅</button>` : '';
    const permSummary = u.role==='Admin' ? 'Full access' : PERMISSION_MODULES.map(m=>`${m}:${(u.permissions&&u.permissions[m])||'View'}`).join(', ');
    return `<tr><td>${i+1}</td><td>${u.username}</td><td>${u.role}</td><td>${u.email||'-'}</td><td title="${permSummary}" style="font-size:11px;color:var(--sub);max-width:180px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${permSummary}</td><td><span class="badge b-${badgeClass}">${st}</span></td><td><div class="act-btns">${approveBtn}<button class="ic-btn" onclick="editUser(${u.id})" title="Edit user">✏️</button><button class="ic-btn" onclick="resetUserPassword(${u.id})" title="Reset password">🔑</button><button class="ic-btn" onclick="deleteUser(${u.id})">🗑️</button></div></td></tr>`;
  }).join('');
}
function renderUserPermMatrix(perms){
  document.getElementById('u-perm-tbody').innerHTML = PERMISSION_MODULES.map(m=>{
    const level = (perms && perms[m]) || 'View';
    const radio = (val,label) => `<td style="text-align:center;"><input type="radio" name="perm-${m}" value="${val}" ${level===val?'checked':''}></td>`;
    return `<tr><td>${m}</td>${radio('None')}${radio('View')}${radio('Edit')}</tr>`;
  }).join('');
}
function toggleUserPermRows(){
  const section = document.getElementById('u-perm-section');
  const isAdminRole = document.getElementById('u-role').value === 'Admin';
  section.style.display = isAdminRole ? 'none' : '';
}
function getUserPermPayload(){
  const perms = {};
  PERMISSION_MODULES.forEach(m=>{
    const checked = document.querySelector(`input[name="perm-${m}"]:checked`);
    perms[m] = checked ? checked.value : 'View';
  });
  return perms;
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
function editUser(id){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const u = DB.users.find(x=>x.id===id); if(!u) return;
  editId.user = id;
  document.getElementById('user-modal-title').textContent = 'Edit User';
  document.getElementById('u-password-row').style.display = 'none';
  document.getElementById('u-name').value = u.username;
  document.getElementById('u-role').value = u.role;
  document.getElementById('u-status').value = u.status || 'Active';
  document.getElementById('u-email').value = u.email || '';
  document.getElementById('u-mobile').value = u.mobile || '';
  document.getElementById('u-flat').value = u.flat || '';
  document.getElementById('u-floor').value = u.floor || '';
  renderUserPermMatrix(u.permissions || {});
  toggleUserPermRows();
  document.getElementById('modal-user').classList.add('open');
}
async function saveUser(){
  if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const n=document.getElementById('u-name').value.trim();
  if(!n)return toast('Enter username','warn');
  const role = document.getElementById('u-role').value;
  const permissions = role==='Admin' ? undefined : getUserPermPayload();

  if(editId.user){
    const payload = {
      username: n,
      role,
      email: document.getElementById('u-email').value,
      mobile: document.getElementById('u-mobile').value,
      flat: document.getElementById('u-flat').value,
      floor: document.getElementById('u-floor').value,
      status: document.getElementById('u-status').value,
      permissions
    };
    try{
      await Api.updateUser(editId.user, payload);
      await loadUsers();
      closeModal('user'); renderUsers(); toast('User updated!');
    }catch(err){ toast(err.message || 'Save failed','warn'); }
    return;
  }

  const pw=document.getElementById('u-password').value;
  if(!pw||pw.length<4)return toast('Password must be at least 4 characters','warn');
  const payload = {
    username: n, password: pw,
    role,
    email: document.getElementById('u-email').value,
    mobile: document.getElementById('u-mobile').value,
    flat: document.getElementById('u-flat').value,
    floor: document.getElementById('u-floor').value,
    status: document.getElementById('u-status').value,
    permissions
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
  const moduleMap = {col:'Collections', exp:'Expenses', mem:'Members'};
  if(moduleMap[type] && !canEdit(moduleMap[type])) return toast('You do not have edit access to '+moduleMap[type]+'.','warn');
  if(type==='user' && !isAdmin()) return toast('Read-only access — Admin only.','warn');
  editId[type]=null;
  if(type==='col'){ document.getElementById('col-modal-title').textContent='Add Collection'; populateMemberDropdown(); document.getElementById('col-date').value=new Date().toISOString().split('T')[0]; document.getElementById('col-month').value=new Date().getMonth()+1; document.getElementById('col-year').value=new Date().getFullYear(); document.getElementById('col-amount').value=''; document.getElementById('col-remarks').value=''; }
  if(type==='exp'){ document.getElementById('exp-modal-title').textContent='Add Expense'; populateCatDropdown(); document.getElementById('exp-date').value=new Date().toISOString().split('T')[0]; document.getElementById('exp-month').value=new Date().getMonth()+1; document.getElementById('exp-year').value=new Date().getFullYear(); document.getElementById('exp-amount').value=''; document.getElementById('exp-desc').value=''; document.getElementById('exp-vendor').value=''; document.getElementById('exp-remarks').value=''; }
  if(type==='mem'){ document.getElementById('mem-modal-title').textContent='Add Member'; populateFloorDropdown('mem-floor'); populateTowerDatalist(); document.getElementById('mem-name').value=''; document.getElementById('mem-flat').value=''; document.getElementById('mem-area').value=''; populateFlatTypeDropdown(''); document.getElementById('mem-tower').value=''; document.getElementById('mem-mobile').value=''; document.getElementById('mem-email').value=''; }
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
  if(type==='user'){
    document.getElementById('user-modal-title').textContent='Add User';
    document.getElementById('u-password-row').style.display='';
    document.getElementById('u-name').value='';
    document.getElementById('u-password').value='';
    document.getElementById('u-role').value='Member';
    document.getElementById('u-status').value='Active';
    document.getElementById('u-email').value='';
    document.getElementById('u-mobile').value='';
    document.getElementById('u-flat').value='';
    document.getElementById('u-floor').value='';
    renderUserPermMatrix({});
    toggleUserPermRows();
  }
  document.getElementById('modal-'+type).classList.add('open');
}
function closeModal(type){ document.getElementById('modal-'+type).classList.remove('open'); }

function openGenBill(){
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
  document.getElementById('gb-month').value = new Date().getMonth()+1;
  document.getElementById('gb-year').value  = new Date().getFullYear();
  document.getElementById('gb-amount').value = '';
  document.getElementById('modal-genbill').classList.add('open');
}

async function generateBilling(){
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
  const month = +document.getElementById('gb-month').value;
  const year  = +document.getElementById('gb-year').value;
  const amtRaw = document.getElementById('gb-amount').value.trim();
  const payload = { month, year, amount: amtRaw === '' ? null : +amtRaw };
  try{
    const r = await Api.generateBilling(payload);
    closeModal('genbill');
    await loadCollections();
    renderCollections(); renderDashboard();
    toast(`Generated ${r.generated} invoice(s) for ${month}/${year}, skipped ${r.skipped} already billed.`);
  }catch(err){ toast(err.message || 'Generation failed','warn'); }
}

function populateMemberDropdown(){
  document.getElementById('col-member').innerHTML=DB.members.filter(m=>(fld(m,'status','Status')||'Active')==='Active').map(m=>`<option value="${fld(m,'id','Id')}">${fld(m,'name','Name')} (${fld(m,'flat','Flat')})</option>`).join('');
}
function populateCatDropdown(){ document.getElementById('exp-cat').innerHTML=DB.settings.categories.map(c=>`<option>${c}</option>`).join(''); }
function populateFloorDropdown(id){ document.getElementById(id).innerHTML=DB.settings.floors.map(f=>`<option value="${f}">Floor ${f}</option>`).join(''); }
function populateFlatTypeDropdown(selected){
  const sel=document.getElementById('mem-flattype'); if(!sel) return;
  const types=(DB.settings.flatTypes||[]).slice();
  // Keep a legacy/free-text value visible so editing an old member doesn't silently blank it.
  if(selected && !types.includes(selected)) types.unshift(selected);
  sel.innerHTML='<option value="">\u2014 Select \u2014</option>'+types.map(t=>`<option value="${t}">${t}</option>`).join('');
  sel.value=selected||'';
}
function populateTowerDatalist(){ const dl=document.getElementById('mem-tower-list'); if(dl) dl.innerHTML=(DB.settings.towers||[]).map(t=>`<option value="${t}">`).join(''); }
function populateComplaintCatDropdown(){ document.getElementById('cmp-category').innerHTML=COMPLAINT_CATEGORIES.map(c=>`<option>${c}</option>`).join(''); }

// ── Record Payment (advance wallet) ──
function memberOutstanding(memberId){
  return DB.collections.filter(c=>{
    const st=getStatus(c).toLowerCase();
    return String(fld(c,'memberId','MemberId'))===String(memberId) && (st==='unpaid'||st==='partial'||st==='overdue');
  });
}
function payableOf(c){ return Math.max(0, getAmt(c) - (+fld(c,'amountPaid','AmountPaid')||0)); }

function openRecordPayment(){
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
  document.getElementById('rp-member').innerHTML=DB.members.filter(m=>(fld(m,'status','Status')||'Active')==='Active')
    .map(m=>`<option value="${fld(m,'id','Id')}">${fld(m,'name','Name')} (${fld(m,'flat','Flat')})</option>`).join('');
  document.getElementById('rp-amount').value='';
  document.getElementById('rp-remarks').value='';
  document.getElementById('rp-type').value='CurrentPlusArrears';
  document.getElementById('rp-mode').value='UPI';
  document.getElementById('rp-date').value=new Date().toISOString().split('T')[0];
  onRpMemberChange();
  document.getElementById('modal-rp').classList.add('open');
}

function onRpMemberChange(){
  const mid=document.getElementById('rp-member').value;
  const m=DB.members.find(x=>String(fld(x,'id','Id'))===String(mid));
  const bal=m?(+fld(m,'advanceBalance','AdvanceBalance')||0):0;
  const dues=memberOutstanding(mid);
  const outstanding=dues.reduce((s,c)=>s+payableOf(c),0);
  document.getElementById('rp-balance-hint').innerHTML=
    `Wallet balance: <b>₹${bal.toLocaleString('en-IN')}</b> · Outstanding dues: <b>₹${outstanding.toLocaleString('en-IN')}</b> (${dues.length} invoice${dues.length===1?'':'s'})`;
  renderRpPreview();
}

function renderRpPreview(){
  const mid=document.getElementById('rp-member').value;
  const amt=+document.getElementById('rp-amount').value||0;
  const type=document.getElementById('rp-type').value;
  const box=document.getElementById('rp-preview');
  if(!amt){ box.innerHTML=''; return; }
  let dues=memberOutstanding(mid).map(c=>({mo:getMonth(c),yr:getYear(c),payable:payableOf(c)})).filter(d=>d.payable>0);
  if(type==='AdvancePayment') dues=[];
  else if(type==='CurrentMonthOnly') dues=dues.sort((a,b)=> b.yr-a.yr || b.mo-a.mo).slice(0,1);
  else dues=dues.sort((a,b)=> a.yr-b.yr || a.mo-b.mo);
  let rem=amt, settled=0, part=0;
  dues.forEach(d=>{ if(rem<=0) return; const ap=Math.min(rem,d.payable); rem-=ap; if(ap>=d.payable) settled++; else part++; });
  box.innerHTML=`<div style="padding:8px 12px;background:var(--bg);border:1px solid var(--border);border-radius:8px;">
    Applies <b>₹${(amt-rem).toLocaleString('en-IN')}</b> to ${settled} invoice(s)${part?` (+${part} partial)`:''}, credits <b>₹${rem.toLocaleString('en-IN')}</b> to wallet.</div>`;
}

async function doRecordPayment(){
  if(!canEdit('Collections')) return toast('You do not have edit access to Collections.','warn');
  const mid=document.getElementById('rp-member').value;
  if(!mid) return toast('Select a member','warn');
  const amt=+document.getElementById('rp-amount').value;
  if(!amt||amt<=0) return toast('Enter a valid amount','warn');
  const payload={
    memberId:+mid,
    amount:amt,
    paymentType:document.getElementById('rp-type').value,
    paymentMode:document.getElementById('rp-mode').value,
    paymentDate:document.getElementById('rp-date').value||null,
    remarks:document.getElementById('rp-remarks').value||null
  };
  try{
    const r=await Api.recordPayment(payload);
    closeModal('rp');
    await loadMembers(); await loadCollections();
    renderCollections(); renderDashboard();
    toast(`Recorded ₹${amt.toLocaleString('en-IN')} — ₹${(r.appliedToInvoices||0).toLocaleString('en-IN')} to dues, ₹${(r.creditedToAdvance||0).toLocaleString('en-IN')} to wallet.`);
  }catch(err){ toast(err.message||'Record failed','warn'); }
}

// ── Member wallet (advance ledger view + manual adjust + refund) ──
let walletMemberId=null;
async function openWallet(id){
  if(!canView('Collections')) return toast('You do not have access to wallets.','warn');
  walletMemberId=id;
  document.getElementById('modal-wallet').classList.add('open');
  document.getElementById('wal-body').innerHTML='<div class="empty">Loading…</div>';
  document.getElementById('wal-adjust').style.display='none';
  try{ renderWallet(await Api.getAdvanceLedger(id)); }
  catch(err){ document.getElementById('wal-body').innerHTML=`<div class="empty">${err.message||'Failed to load'}</div>`; }
}
function walletSourceLabel(s){
  return ({Payment:'Payment surplus',BillAdjustment:'Applied to invoice',ManualAdmin:'Manual adjustment',Refund:'Refund'})[s]||s;
}
function renderWallet(d){
  const bal=+fld(d,'balance','Balance')||0;
  const mode=fld(d,'mode','Mode')||'Auto';
  const entries=fld(d,'entries','Entries')||[];
  const editable=canEdit('Collections');
  const rows=entries.length? entries.map(e=>{
    const t=fld(e,'type','Type'); const cr=t==='Credit';
    return `<tr>
      <td>${new Date(fld(e,'date','Date')).toLocaleDateString('en-IN')}</td>
      <td><span class="badge ${cr?'b-paid':'b-unpaid'}">${t}</span></td>
      <td style="text-align:right;color:${cr?'var(--green,#1c8a4d)':'var(--red,#b23b3b)'}">${cr?'+':'−'}₹${(+fld(e,'amount','Amount')||0).toLocaleString('en-IN')}</td>
      <td style="text-align:right">₹${(+fld(e,'balanceAfter','BalanceAfter')||0).toLocaleString('en-IN')}</td>
      <td>${walletSourceLabel(fld(e,'source','Source'))}</td>
      <td class="muted">${fld(e,'note','Note')||''}</td>
    </tr>`;
  }).join('') : `<tr><td colspan="6" class="empty">No wallet activity yet.</td></tr>`;

  document.getElementById('wal-title').textContent=`👛 ${fld(d,'memberName','MemberName')} · ${fld(d,'flat','Flat')}`;
  document.getElementById('wal-body').innerHTML=`
    <div class="wal-head">
      <div><div class="wal-bal">₹${bal.toLocaleString('en-IN')}</div><div class="muted">Wallet balance</div></div>
      <div class="wal-mode">
        <label class="muted" style="font-size:12px">Auto-settle new invoices</label>
        <label class="switch"><input type="checkbox" id="wal-mode-tgl" ${mode==='Auto'?'checked':''} ${editable?'':'disabled'} onchange="toggleWalletMode()"><span class="slider"></span></label>
      </div>
    </div>
    ${editable?`<div class="wal-actions">
      <button class="btn btn-light" onclick="showWalletAdjust('Credit')">➕ Add credit</button>
      <button class="btn btn-light" onclick="showWalletAdjust('Debit')">➖ Remove</button>
      <button class="btn btn-light" onclick="refundWallet()" ${bal>0?'':'disabled'}>↩️ Refund balance</button>
    </div>`:''}
    <div class="table-wrap"><table><thead><tr><th>Date</th><th>Type</th><th style="text-align:right">Amount</th><th style="text-align:right">Balance</th><th>Source</th><th>Note</th></tr></thead><tbody>${rows}</tbody></table></div>`;
}
function showWalletAdjust(type){
  const box=document.getElementById('wal-adjust');
  box.style.display='block';
  box.dataset.type=type;
  document.getElementById('wal-adj-title').textContent=type==='Credit'?'Add credit to wallet':'Remove from wallet';
  document.getElementById('wal-adj-amt').value='';
  document.getElementById('wal-adj-note').value='';
  document.getElementById('wal-adj-amt').focus();
}
async function doWalletAdjust(){
  if(!canEdit('Collections')) return toast('You do not have edit access.','warn');
  const type=document.getElementById('wal-adjust').dataset.type;
  const amt=+document.getElementById('wal-adj-amt').value;
  if(!amt||amt<=0) return toast('Enter a valid amount','warn');
  try{
    const d=await Api.adjustAdvance(walletMemberId,{ type, amount:amt, note:document.getElementById('wal-adj-note').value||null });
    document.getElementById('wal-adjust').style.display='none';
    renderWallet(d);
    await loadMembers(); renderMembers(); renderDashboard();
    toast(`${type==='Credit'?'Added':'Removed'} ₹${amt.toLocaleString('en-IN')}.`);
  }catch(err){ toast(err.message||'Adjust failed','warn'); }
}
async function refundWallet(){
  if(!canEdit('Collections')) return toast('You do not have edit access.','warn');
  if(!confirm('Refund the full wallet balance? This zeroes the wallet and records a Refund entry.')) return;
  try{
    const d=await Api.refundAdvance(walletMemberId,{ note:'Refund on move-out' });
    renderWallet(d);
    await loadMembers(); renderMembers(); renderDashboard();
    toast('Wallet refunded.');
  }catch(err){ toast(err.message||'Refund failed','warn'); }
}
async function toggleWalletMode(){
  const mode=document.getElementById('wal-mode-tgl').checked?'Auto':'Manual';
  try{ await Api.setAdvanceMode(walletMemberId, mode); await loadMembers(); toast(`Advance mode: ${mode}.`); }
  catch(err){ toast(err.message||'Failed','warn'); document.getElementById('wal-mode-tgl').checked=(mode!=='Auto'); }
}

// ═══════════════════════════════════════════════
// COMPLAINTS
// ═══════════════════════════════════════════════
function renderComplaints(){
  if(!document.getElementById('cmp-tbody')) return;   // partial not loaded yet
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
  const canWithdraw = !canEdit('Complaints') && String(c.raisedByUserId)===String(currentUser && currentUser.id) && c.status==='Open';
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
      ${canEdit('Complaints') ? `<button class="ic-btn" onclick="editComplaint('${c.id}')">✏️</button><button class="ic-btn" onclick="deleteComplaint('${c.id}')">🗑️</button>` : ''}
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
      if(!canEdit('Complaints')) return toast('You do not have edit access to Complaints.','warn');
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
    if(!isAdmin()) refreshMemberComplaintViews();
    toast(editId.cmp ? 'Complaint updated — member will be notified.' : 'Complaint submitted — admin will be notified.');
  }catch(err){ toast(err.message || 'Save failed','warn'); }
}

function editComplaint(id){
  if(!canEdit('Complaints')) return toast('You do not have edit access to Complaints.','warn');
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
  const msg = (c && !canEdit('Complaints')) ? 'Withdraw this complaint?' : 'Delete this complaint?';
  if(!confirm(msg)) return;
  try{
    await Api.deleteComplaint(id);
    await loadComplaints();
    renderComplaints(); updateNotifBadge();
    if(!isAdmin()) refreshMemberComplaintViews();
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

// ═══════════════════════════════════════════════════════════════
// MEMBER (RESIDENT) PORTAL — everything below drives the /api/me
// backed experience shown to non-Admin users. Data lives in the
// global `ME` snapshot (see loadMe()); complaints reuse DB.complaints
// which the API already scopes to the signed-in resident.
// ═══════════════════════════════════════════════════════════════
let mFamily = [], mVehicles = [], mPhoto = '';

function mMoney(n){ return '₹' + (Number(n)||0).toLocaleString('en-IN'); }
function mDate(iso){
  if(!iso) return '—';
  const d = new Date(iso);
  if(isNaN(d)) return '—';
  return String(d.getDate()).padStart(2,'0')+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+d.getFullYear();
}
function mParse(json){ try{ const v = JSON.parse(json||'[]'); return Array.isArray(v)?v:[]; }catch(e){ return []; } }
function mKpi(icon,label,value,sub,tone){
  return `<div class="kpi"><div class="kpi-icon">${icon}</div><div class="kpi-lbl">${label}</div>`+
         `<div class="kpi-val">${value}</div><div class="kpi-sub ${tone||''}">${sub||''}</div></div>`;
}
function mStatusBadge(st){ const k=(st||'').toLowerCase(); return `<span class="badge b-${k}">${st||'—'}</span>`; }

// ── Dashboard ──
function renderMemberDashboard(){
  if(!ME) return;
  if(!document.getElementById('m-kpi-grid')) return;   // partial not loaded yet
  const mnt = ME.maintenance || {}, cmp = ME.complaints || {};
  const hour = new Date().getHours();
  const greet = hour<12?'Good morning':hour<17?'Good afternoon':'Good evening';
  const name = ME.name || ME.username;
  const wel = document.getElementById('m-welcome');
  if(wel) wel.innerHTML = `<h2>${greet}, ${name} 👋</h2><p>Flat ${ME.flat||'—'}${ME.floor?' · Floor '+ME.floor:''} · ${ME.societyName||''}</p>`;

  const pendTone = (mnt.pendingAmount>0)?'dn':'up';
  document.getElementById('m-kpi-grid').innerHTML =
    mKpi('⏳','Pending Maintenance', mMoney(mnt.pendingAmount), (mnt.pendingMonths||0)+' month(s) due', pendTone) +
    mKpi('✅','Total Paid', mMoney(mnt.totalPaid), (mnt.totalReceipts||0)+' receipts','up') +
    mKpi('📅','Next Due Date', mDate(mnt.nextDueDate), mMoney(mnt.maintenanceAmt)+' expected') +
    mKpi('🧾','Last Payment', mDate(mnt.lastPaymentDate), '') +
    mKpi('🛠️','Open Complaints', (cmp.open||0), (cmp.total||0)+' total');

  const recent = (ME.payments||[]).slice(0,5);
  document.getElementById('m-recent-pay').innerHTML = recent.map(p=>
    `<tr><td>${MONTHS[p.month]||p.month} ${p.year}</td><td>${mMoney(p.amount)}</td><td>${mStatusBadge(p.status)}</td></tr>`
  ).join('') || '<tr><td colspan="3" class="empty">No payment records yet</td></tr>';

  const notices = memberNoticeItems().slice(0,3);
  document.getElementById('m-recent-notices').innerHTML = notices.map(mNoticeHtml).join('')
    || '<div class="empty">No notices right now 🎉</div>';

  updateMemberBadge();
}

// ── My Payments ──
function renderMemberPayments(){
  if(!ME) return;
  if(!document.getElementById('m-pay-tbody')) return;   // partial not loaded yet
  const mnt = ME.maintenance || {};
  document.getElementById('m-pay-kpi').innerHTML =
    mKpi('✅','Total Paid', mMoney(mnt.totalPaid), (mnt.totalReceipts||0)+' receipts','up') +
    mKpi('⏳','Pending', mMoney(mnt.pendingAmount), (mnt.pendingMonths||0)+' month(s)', mnt.pendingAmount>0?'dn':'up') +
    mKpi('📅','Next Due', mDate(mnt.nextDueDate), mMoney(mnt.maintenanceAmt));

  loadMemberPendingInvoices();
  loadMyWallet();

  const f = document.getElementById('mPayStatusF').value;
  const rows = (ME.payments||[]).filter(p=>!f||p.status===f);
  document.getElementById('m-pay-tbody').innerHTML = rows.map(p=>
    `<tr>
      <td>${MONTHS[p.month]||p.month} ${p.year}</td>
      <td>${mMoney(p.amount)}</td>
      <td>${mStatusBadge(p.status)}</td>
      <td>${mDate(p.paymentDate)}</td>
      <td>${p.paymentMode||'—'}</td>
      <td>${p.remarks||'—'}</td>
      <td>${p.status==='Paid' ? `<button class="ic-btn" title="Download receipt" onclick="downloadMemberReceipt(${p.id})">🧾</button>` : ''}</td>
    </tr>`
  ).join('') || '<tr><td colspan="7" class="empty">No maintenance records found</td></tr>';
}

// ── Resident advance wallet card (shown only when there's balance or history) ──
async function loadMyWallet(){
  const box=document.getElementById('m-wallet');
  if(!box) return;
  try{
    const d=await Api.getMyAdvance();
    const bal=+fld(d,'balance','Balance')||0;
    const entries=fld(d,'entries','Entries')||[];
    if(bal<=0 && !entries.length){ box.innerHTML=''; return; }
    const rows=entries.slice(0,8).map(e=>{
      const cr=fld(e,'type','Type')==='Credit';
      return `<tr>
        <td>${new Date(fld(e,'date','Date')).toLocaleDateString('en-IN')}</td>
        <td>${cr?'Credit':'Applied'}</td>
        <td style="text-align:right;color:${cr?'var(--success)':'var(--danger)'}">${cr?'+':'−'}${mMoney(+fld(e,'amount','Amount')||0)}</td>
        <td style="text-align:right">${mMoney(+fld(e,'balanceAfter','BalanceAfter')||0)}</td>
        <td class="muted">${fld(e,'note','Note')||''}</td>
      </tr>`;
    }).join('');
    box.innerHTML=`
      <div class="mwallet-card">
        <div class="mw-lbl"><svg viewBox="0 0 24 24" width="1em" height="1em" style="vertical-align:-.15em" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12V7H5a2 2 0 0 1 0-4h14v4"/><path d="M3 5v14a2 2 0 0 0 2 2h16v-5"/><path d="M18 12a2 2 0 0 0 0 4h4v-4z"/></svg> Advance in Wallet</div>
        <div class="mw-bal">${mMoney(bal)}</div>
        <div style="font-size:11px;opacity:.9;margin-top:4px;">Automatically adjusted against your upcoming maintenance bills.</div>
      </div>
      ${entries.length?`<div class="card"><div class="card-hdr"><h3>Wallet Activity</h3></div>
        <div class="tbl-wrap"><table><thead><tr><th>Date</th><th>Type</th><th style="text-align:right">Amount</th><th style="text-align:right">Balance</th><th>Note</th></tr></thead>
        <tbody>${rows}</tbody></table></div></div>`:''}`;
  }catch(err){ box.innerHTML=''; }
}

// ── Receipts ──
function renderMemberReceipts(){
  if(!ME) return;
  if(!document.getElementById('m-receipt-grid')) return;   // partial not loaded yet
  const paid = (ME.payments||[]).filter(p=>p.status==='Paid');
  document.getElementById('m-receipt-grid').innerHTML = paid.map(p=>
    `<div class="doc-tile">
      <div class="doc-ic">🧾</div>
      <div class="doc-m">${MONTHS[p.month]||p.month} ${p.year}</div>
      <div class="doc-amt">${mMoney(p.amount)}</div>
      <div class="mhint">Paid on ${mDate(p.paymentDate)} · ${p.paymentMode||'—'}</div>
      <button class="btn btn-ghost" onclick="downloadMemberReceipt(${p.id})">⬇ Download</button>
    </div>`
  ).join('') || '<div class="empty">No receipts available yet</div>';
}

function downloadMemberReceipt(id){
  const p = (ME.payments||[]).find(x=>String(x.id)===String(id));
  if(!p) return toast('Receipt not found','warn');
  const society = ME.societyName || 'Society';
  const payer = ME.name || ME.username;
  const html = `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Receipt ${id}</title>
    <style>
      body{font-family:Arial,Helvetica,sans-serif;color:#1a1f36;padding:32px;max-width:640px;margin:auto;}
      h1{font-size:20px;margin:0;} .muted{color:#718096;font-size:12px;}
      .hd{border-bottom:2px solid #6c63ff;padding-bottom:12px;margin-bottom:18px;}
      table{width:100%;border-collapse:collapse;margin-top:12px;}
      td{padding:8px 6px;border-bottom:1px solid #e2e8f0;font-size:14px;}
      td.l{color:#718096;width:45%;} .amt{font-size:22px;font-weight:800;color:#276749;}
      .ft{margin-top:24px;font-size:12px;color:#718096;}
      .stamp{display:inline-block;margin-top:16px;padding:6px 14px;border:2px solid #276749;color:#276749;border-radius:8px;font-weight:700;transform:rotate(-4deg);}
    </style></head><body>
    <div class="hd"><h1>${society}</h1><div class="muted">Maintenance Payment Receipt</div></div>
    <table>
      <tr><td class="l">Receipt No.</td><td>SMMS-${p.year}-${String(p.id).padStart(5,'0')}</td></tr>
      <tr><td class="l">Received From</td><td>${payer}</td></tr>
      <tr><td class="l">Flat / Floor</td><td>${ME.flat||'—'}${ME.floor?' · Floor '+ME.floor:''}</td></tr>
      <tr><td class="l">For Month</td><td>${MONTHS[p.month]||p.month} ${p.year}</td></tr>
      <tr><td class="l">Payment Date</td><td>${mDate(p.paymentDate)}</td></tr>
      <tr><td class="l">Payment Mode</td><td>${p.paymentMode||'—'}</td></tr>
      <tr><td class="l">Amount Paid</td><td class="amt">${mMoney(p.amount)}</td></tr>
    </table>
    <div class="stamp">PAID</div>
    <div class="ft">This is a system-generated receipt and does not require a physical signature.<br>Generated on ${mDate(new Date().toISOString())}.</div>
    <script>window.onload=function(){window.print();}<\/script>
    </body></html>`;
  const w = window.open('', '_blank');
  if(!w) return toast('Please allow pop-ups to download the receipt','warn');
  w.document.write(html); w.document.close();
}

// ═══════════════════════════════════════════════
// ONLINE PAYMENTS  (member: pay + upload proof)
// ═══════════════════════════════════════════════
let _payCtx = { collectionId: null, qrUrl: null };

async function loadMemberPendingInvoices(){
  const box = document.getElementById('m-pay-invoices');
  if(!box) return;
  box.innerHTML = '<div class="mhint">Loading…</div>';
  try{
    const list = await Api.getPendingInvoices();
    if(!list.length){ box.innerHTML = '<div class="empty">🎉 No pending dues. You are all settled up!</div>'; return; }
    box.innerHTML = list.map(inv => {
      const overdue = inv.isOverdue ? '<span class="pay-badge pay-overdue">Overdue</span>' : '';
      const action = inv.hasPendingProof
        ? '<span class="pay-badge pay-pending">⏳ Awaiting verification</span>'
        : `<button class="btn btn-primary" onclick="openPayModal(${inv.collectionId})">💳 Pay Now</button>`;
      return `<div class="pay-invoice-card">
        <div class="pay-inv-top">
          <div><div class="pay-inv-period">${inv.billingLabel}</div><div class="mhint">${inv.invoiceNumber}</div></div>
          <div class="pay-inv-amt">${mMoney(inv.amount)}</div>
        </div>
        <div class="mhint">Due ${mDate(inv.dueDate)} ${overdue}</div>
        <div class="pay-inv-actions">${action}</div>
      </div>`;
    }).join('');
  }catch(err){
    box.innerHTML = `<div class="empty">${err.message}</div>`;
  }
}

async function openPayModal(collectionId){
  try{
    const payload = await Api.getQrPayload(collectionId);
    _payCtx.collectionId = collectionId;
    document.getElementById('pay-modal-title').textContent = `Pay ${payload.invoiceNumber}`;
    document.getElementById('pay-amt').textContent = mMoney(payload.amount);
    document.getElementById('pay-payee').textContent = `To: ${payload.payeeName} · ${payload.upiId}`;
    document.getElementById('pay-upi-link').href = payload.upiUri;
    document.getElementById('pay-ref').value = '';
    document.getElementById('pay-file').value = '';

    const img = document.getElementById('pay-qr-img');
    img.src = '';
    if(_payCtx.qrUrl){ URL.revokeObjectURL(_payCtx.qrUrl); _payCtx.qrUrl = null; }
    _payCtx.qrUrl = await Api.getQrImageUrl(collectionId);
    img.src = _payCtx.qrUrl;

    document.getElementById('modal-pay').classList.add('open');
  }catch(err){
    toast(err.message, 'warn');
  }
}

async function submitPaymentProof(){
  if(!_payCtx.collectionId) return;
  const ref = document.getElementById('pay-ref').value.trim();
  const file = document.getElementById('pay-file').files[0];
  if(!ref && !file) return toast('Enter a UPI reference or attach a screenshot.','warn');
  if(file && file.size > 5 * 1024 * 1024) return toast('Screenshot must be under 5 MB.','warn');

  const btn = document.getElementById('pay-submit-btn');
  btn.disabled = true;
  try{
    const fd = new FormData();
    fd.append('collectionId', _payCtx.collectionId);
    if(ref) fd.append('upiReference', ref);
    if(file) fd.append('screenshot', file);
    await Api.uploadPaymentProof(fd);
    toast('Payment submitted for verification ✅');
    closeModal('pay');
    await loadMe();
    renderMemberPayments();
  }catch(err){
    toast(err.message, 'warn');
  }finally{
    btn.disabled = false;
  }
}

// ═══════════════════════════════════════════════
// ONLINE PAYMENTS  (admin: verify + configure)
// ═══════════════════════════════════════════════
async function renderPaymentsAdmin(){
  if(!isAdmin()) return;
  if(!document.getElementById('pay-tbody')) return;   // partial not loaded yet
  await loadUpiSettingsForm();
  try{
    const dash = await Api.getPaymentDashboard();
    document.getElementById('pay-kpi').innerHTML =
      mKpi('⏳','Pending Approvals', dash.pendingCount, mMoney(dash.pendingAmount)+' awaiting', dash.pendingCount>0?'dn':'up') +
      mKpi('📆','Collected Today', mMoney(dash.todayCollections), '','up') +
      mKpi('🗓️','Collected This Month', mMoney(dash.monthCollections), '','up');
    updatePayBadge(dash.pendingCount);
  }catch(err){ toast(err.message,'warn'); }

  const status = document.getElementById('payStatusF').value;
  const tbody = document.getElementById('pay-tbody');
  tbody.innerHTML = '<tr><td colspan="10" class="empty">Loading…</td></tr>';
  try{
    const rows = await Api.getPaymentProofs({ status });
    tbody.innerHTML = rows.map(p=>{
      const proof = p.hasScreenshot
        ? `<button class="ic-btn" title="View screenshot" onclick="viewProofScreenshot(${p.id},true)">🖼️</button>` : '—';
      const actions = p.status==='Pending'
        ? `<button class="btn btn-primary btn-sm" onclick="approveProof(${p.id})">Approve</button>
           <button class="btn btn-ghost btn-sm" onclick="rejectProof(${p.id})">Reject</button>`
        : mStatusBadge(p.status) + (p.reviewRemarks ? `<div class="mhint">${p.reviewRemarks}</div>` : '');
      return `<tr>
        <td>${mDate(p.submittedAt)}</td>
        <td>${p.flat||'—'}</td>
        <td>${p.memberName||'—'}</td>
        <td>${p.invoiceNumber}</td>
        <td>${p.billingLabel}</td>
        <td>${mMoney(p.amount)}</td>
        <td>${p.upiReference||'—'}</td>
        <td>${proof}</td>
        <td>${mStatusBadge(p.status)}</td>
        <td>${actions}</td>
      </tr>`;
    }).join('') || '<tr><td colspan="10" class="empty">No payment records found</td></tr>';
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="10" class="empty">${err.message}</td></tr>`;
  }
  renderReconciliation();
}

async function approveProof(id){
  if(!confirm('Approve this payment? The invoice will be marked Paid.')) return;
  try{ await Api.approvePaymentProof(id); toast('Payment approved ✅'); renderPaymentsAdmin(); }
  catch(err){ toast(err.message,'warn'); }
}

async function rejectProof(id){
  const remarks = prompt('Reason for rejection (optional):') ?? null;
  try{ await Api.rejectPaymentProof(id, remarks); toast('Payment rejected','info'); renderPaymentsAdmin(); }
  catch(err){ toast(err.message,'warn'); }
}

async function viewProofScreenshot(id, admin){
  try{
    const url = admin ? await Api.getAdminProofScreenshot(id) : await Api.getMyProofScreenshot(id);
    const img = document.getElementById('proof-img');
    if(img._url) URL.revokeObjectURL(img._url);
    img._url = url;
    img.src = url;
    document.getElementById('modal-proof').classList.add('open');
  }catch(err){ toast(err.message,'warn'); }
}
function closeProofModal(){
  const img = document.getElementById('proof-img');
  if(img._url){ URL.revokeObjectURL(img._url); img._url = null; }
  img.src = '';
  document.getElementById('modal-proof').classList.remove('open');
}

async function loadUpiSettingsForm(){
  try{
    const s = await Api.getUpiSettings();
    document.getElementById('upi-id').value      = s.upiId || '';
    document.getElementById('upi-payee').value   = s.upiPayeeName || '';
    document.getElementById('upi-bank').value    = s.bankName || '';
    document.getElementById('upi-acname').value  = s.bankAccountName || '';
    document.getElementById('upi-acnum').value   = s.bankAccountNumber || '';
    document.getElementById('upi-ifsc').value    = s.bankIfsc || '';
  }catch(err){ /* settings may not be loaded yet */ }
}

async function saveUpiSettingsForm(){
  const payload = {
    upiId:             document.getElementById('upi-id').value.trim(),
    upiPayeeName:      document.getElementById('upi-payee').value.trim(),
    bankName:          document.getElementById('upi-bank').value.trim(),
    bankAccountName:   document.getElementById('upi-acname').value.trim(),
    bankAccountNumber: document.getElementById('upi-acnum').value.trim(),
    bankIfsc:          document.getElementById('upi-ifsc').value.trim()
  };
  if(!payload.upiId || !payload.upiPayeeName) return toast('UPI ID and Payee Name are required.','warn');
  try{ await Api.saveUpiSettings(payload); toast('Payment setup saved ✅'); }
  catch(err){ toast(err.message,'warn'); }
}

function updatePayBadge(count){
  const b = document.getElementById('payBadge');
  if(!b) return;
  b.textContent = count || 0;
  b.style.display = count > 0 ? '' : 'none';
}

async function refreshPayBadge(){
  if(!isAdmin()) return;
  try{ const d = await Api.getPaymentDashboard(); updatePayBadge(d.pendingCount); }
  catch(err){ /* non-critical */ }
}

// ── Bank statement reconciliation ───────────────────────────────
function escHtml(s){ return String(s==null?'':s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }

async function renderReconciliation(){
  if(!isAdmin()) return;
  const tbody = document.getElementById('recon-tbody');
  if(!tbody) return;   // partial not loaded yet
  try{
    const s = await Api.getReconSummary();
    document.getElementById('recon-summary').innerHTML =
      `Unmatched: <b>${s.unmatchedCount}</b> (${mMoney(s.unmatchedAmount)}) &middot; Matched: <b>${s.matchedCount}</b> &middot; Pending proofs awaiting money: <b>${s.pendingProofCount}</b>`;
  }catch(err){ /* non-critical */ }

  const status = document.getElementById('reconStatusF').value;
  tbody.innerHTML = '<tr><td colspan="6" class="empty">Loading…</td></tr>';
  try{
    const rows = await Api.getBankTxns(status);
    tbody.innerHTML = rows.map(renderReconRow).join('') || '<tr><td colspan="6" class="empty">No transactions. Import a bank statement (CSV, Excel or PDF) to begin.</td></tr>';
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="6" class="empty">${err.message}</td></tr>`;
  }
}

function renderReconRow(t){
  let matchCell, actionCell;
  if(t.status === 'Unmatched'){
    if(t.candidates && t.candidates.length){
      const opts = t.candidates.map(c=>{
        const tag = c.confidence==='Exact' ? '✅ Exact' : '≈ Likely';
        return `<option value="${c.paymentProofId}">${tag} · Flat ${escHtml(c.flat||'—')} · ${escHtml(c.invoiceNumber)} · ${mMoney(c.amount)}</option>`;
      }).join('');
      matchCell = `<select id="recon-sel-${t.id}" style="max-width:300px;">${opts}</select>`;
      actionCell = `<button class="btn btn-primary btn-sm" onclick="confirmReconMatch(${t.id})">Confirm</button>
                    <button class="btn btn-ghost btn-sm" onclick="ignoreBankTxn(${t.id})">Ignore</button>`;
    } else {
      matchCell = '<span class="mhint">No match found</span>';
      actionCell = `<button class="btn btn-ghost btn-sm" onclick="ignoreBankTxn(${t.id})">Ignore</button>`;
    }
  } else {
    matchCell = mStatusBadge(t.status);
    actionCell = '—';
  }
  return `<tr>
    <td>${mDate(t.txnDate)}</td>
    <td class="mhint" style="max-width:320px;overflow:hidden;text-overflow:ellipsis;">${escHtml(t.narration)}</td>
    <td>${escHtml(t.reference)||'—'}</td>
    <td>${mMoney(t.amount)}</td>
    <td>${matchCell}</td>
    <td>${actionCell}</td>
  </tr>`;
}

async function importBankStatement(){
  const input = document.getElementById('recon-file');
  if(!input || !input.files || !input.files.length) return toast('Choose a bank statement file (CSV, Excel or PDF) first.','warn');
  const fd = new FormData();
  fd.append('file', input.files[0]);
  try{
    const r = await Api.importBankStatement(fd);
    toast(`Imported ${r.imported} credit(s); ${r.autoMatched} auto-matched, ${r.skippedDuplicates} duplicate(s) skipped.`);
    input.value = '';
    renderPaymentsAdmin();
  }catch(err){ toast(err.message,'warn'); }
}

async function confirmReconMatch(id){
  const sel = document.getElementById('recon-sel-'+id);
  if(!sel || !sel.value) return toast('No match selected.','warn');
  if(!confirm('Confirm this match? The resident payment will be approved and the invoice marked Paid.')) return;
  try{ await Api.confirmReconMatch(id, parseInt(sel.value,10)); toast('Payment reconciled ✅'); renderPaymentsAdmin(); }
  catch(err){ toast(err.message,'warn'); }
}

async function ignoreBankTxn(id){
  if(!confirm('Ignore this bank transaction? It will no longer appear as unmatched.')) return;
  try{ await Api.ignoreBankTxn(id); toast('Transaction ignored','info'); renderReconciliation(); }
  catch(err){ toast(err.message,'warn'); }
}

// ── My Complaints ──
function renderMemberComplaints(){
  if(!document.getElementById('m-cmp-tbody')) return;   // partial not loaded yet
  const list = DB.complaints || [];
  const open = list.filter(c=>c.status==='Open').length;
  const prog = list.filter(c=>c.status==='In Progress').length;
  const done = list.filter(c=>c.status==='Resolved'||c.status==='Closed').length;
  const sum = document.getElementById('m-cmp-summary');
  if(sum) sum.innerHTML =
    `<div style="display:flex;gap:10px;flex-wrap:wrap;align-items:center;padding:10px 14px;background:var(--bg);border-radius:10px;border:1px solid var(--border);width:100%;">
      <div style="text-align:center;padding:6px 16px;background:var(--card);border-radius:8px;border:1px solid var(--border);"><div style="font-size:10px;color:var(--sub);">Total</div><div style="font-size:16px;font-weight:800;">${list.length}</div></div>
      <div style="text-align:center;padding:6px 16px;background:#fed7d7;border-radius:8px;"><div style="font-size:10px;color:#9b2c2c;">🔴 Open</div><div style="font-size:16px;font-weight:800;color:#9b2c2c;">${open}</div></div>
      <div style="text-align:center;padding:6px 16px;background:#feebc8;border-radius:8px;"><div style="font-size:10px;color:#9c4221;">🟡 In Progress</div><div style="font-size:16px;font-weight:800;color:#9c4221;">${prog}</div></div>
      <div style="text-align:center;padding:6px 16px;background:#c6f6d5;border-radius:8px;"><div style="font-size:10px;color:#276749;">🟢 Resolved</div><div style="font-size:16px;font-weight:800;color:#276749;">${done}</div></div>
    </div>`;

  const statusColors = {'Open':'#c53030','In Progress':'#c05621','Resolved':'#276749','Closed':'#4a5568'};
  const prioColors   = {'High':'#c53030','Medium':'#c05621','Low':'#276749'};
  document.getElementById('m-cmp-tbody').innerHTML = [...list]
    .sort((a,b)=>(b.createdAt||'').localeCompare(a.createdAt||''))
    .map(c=>`<tr>
      <td>${c.createdAt||'—'}</td>
      <td title="${(c.description||'').replace(/"/g,'&quot;')}">${c.subject}</td>
      <td>${c.category}</td>
      <td><span style="color:${prioColors[c.priority]||'#4a5568'};font-weight:700;">${c.priority}</span></td>
      <td><span style="color:${statusColors[c.status]||'#4a5568'};font-weight:700;">${c.status}</span></td>
      <td>${c.resolutionNotes||'—'}</td>
      <td>${c.status==='Open' ? `<button class="ic-btn" title="Withdraw" onclick="deleteComplaint('${c.id}')">🗑️</button>` : ''}</td>
    </tr>`).join('') || '<tr><td colspan="7" class="empty">You have not raised any complaints yet</td></tr>';
}

function refreshMemberComplaintViews(){
  renderMemberComplaints();
  renderMemberNotices();
  updateMemberBadge();
}

// ── Notices ──
function memberNoticeItems(){
  const items = [];
  const mnt = (ME && ME.maintenance) || {};
  if(mnt.pendingAmount > 0){
    items.push({color:'#e53e3e', text:`⚠️ Maintenance pending: ${mMoney(mnt.pendingAmount)} across ${mnt.pendingMonths||0} month(s). Please clear at the earliest.`, time:'Payment reminder'});
  } else if(mnt.nextDueDate){
    items.push({color:'#3182ce', text:`📅 Next maintenance of ${mMoney(mnt.maintenanceAmt)} is due on ${mDate(mnt.nextDueDate)}.`, time:'Upcoming due'});
  }
  // Reuse the shared resident complaint-update feed (own complaints, non-open).
  return items.concat(complaintNotifItems());
}
function mNoticeHtml(n){
  return `<div class="notif-item"><div class="ndot" style="background:${n.color}"></div><div><div class="ntext">${n.text}</div><div class="ntime">${n.time||''}</div></div></div>`;
}
function renderMemberNotices(){
  const list = document.getElementById('m-notice-list');
  if(!list){ updateMemberBadge(); return; }   // partial not loaded — still refresh badge
  list.innerHTML = memberNoticeItems().map(mNoticeHtml).join('')
    || '<div class="empty">No notices right now 🎉</div>';
  updateMemberBadge();
}
function updateMemberBadge(){
  const el = document.getElementById('mNotifBadge');
  if(el) el.textContent = memberNoticeItems().length;
}

// ── My Home (profile) ──
function renderMemberHome(){
  if(!ME) return;
  if(!document.getElementById('m-avatar')) return;   // partial not loaded yet
  mPhoto = ME.profilePhoto || '';
  const av = document.getElementById('m-avatar');
  if(av){
    if(mPhoto){ av.style.backgroundImage = `url('${mPhoto}')`; av.textContent = ''; }
    else { av.style.backgroundImage = ''; av.textContent = (ME.name||ME.username||'R').charAt(0).toUpperCase(); }
  }
  document.getElementById('m-hero-name').textContent = ME.name || ME.username;
  document.getElementById('m-hero-sub').textContent = `Flat ${ME.flat||'—'}${ME.floor?' · Floor '+ME.floor:''}`;
  const roleLbl = ME.role === 'Member' ? 'Resident' : ME.role;
  document.getElementById('m-hero-badges').innerHTML =
    `<span class="badge b-paid">${roleLbl}</span>` + (ME.occupancyType?`<span class="badge b-pending">${ME.occupancyType}</span>`:'');

  document.getElementById('m-name').value      = ME.name || '';
  document.getElementById('m-email').value     = ME.email || '';
  document.getElementById('m-mobile').value    = ME.mobile || '';
  document.getElementById('m-occupancy').value = ME.occupancyType || '';
  document.getElementById('m-emergency').value = ME.emergencyContact || '';
  document.getElementById('m-flat').value      = ME.flat || '';
  document.getElementById('m-floor').value     = ME.floor || '';

  const prefs = (function(){ try{ return JSON.parse(ME.notifyPrefsJson||'{}')||{}; }catch(e){ return {}; } })();
  document.getElementById('m-pref-sms').checked      = prefs.sms !== false;
  document.getElementById('m-pref-email').checked    = prefs.email !== false;
  document.getElementById('m-pref-whatsapp').checked = !!prefs.whatsapp;

  mFamily   = mParse(ME.familyJson);
  mVehicles = mParse(ME.vehiclesJson);
  renderFamilyRows();
  renderVehicleRows();

  ['m-pw-current','m-pw-new','m-pw-confirm'].forEach(id=>{ const e=document.getElementById(id); if(e) e.value=''; });
}

function renderFamilyRows(){
  document.getElementById('m-family-list').innerHTML = mFamily.map((f,i)=>
    `<div class="m-row">
      <input placeholder="Relationship" value="${(f.relationship||'').replace(/"/g,'&quot;')}" oninput="mFamily[${i}].relationship=this.value">
      <input placeholder="Name" value="${(f.name||'').replace(/"/g,'&quot;')}" oninput="mFamily[${i}].name=this.value">
      <input placeholder="Mobile" value="${(f.mobile||'').replace(/"/g,'&quot;')}" oninput="mFamily[${i}].mobile=this.value">
      <button class="ic-btn" onclick="removeFamilyRow(${i})">🗑️</button>
    </div>`).join('') || '<div class="mhint">No family members added.</div>';
}
function addFamilyRow(){ mFamily.push({relationship:'',name:'',mobile:''}); renderFamilyRows(); }
function removeFamilyRow(i){ mFamily.splice(i,1); renderFamilyRows(); }

function renderVehicleRows(){
  document.getElementById('m-vehicle-list').innerHTML = mVehicles.map((v,i)=>
    `<div class="m-row">
      <input placeholder="Vehicle No." value="${(v.number||'').replace(/"/g,'&quot;')}" oninput="mVehicles[${i}].number=this.value">
      <select onchange="mVehicles[${i}].type=this.value">
        ${['Car','Bike','Scooter','Cycle','Other'].map(t=>`<option${v.type===t?' selected':''}>${t}</option>`).join('')}
      </select>
      <input placeholder="Parking slot" value="${(v.slot||'').replace(/"/g,'&quot;')}" oninput="mVehicles[${i}].slot=this.value">
      <button class="ic-btn" onclick="removeVehicleRow(${i})">🗑️</button>
    </div>`).join('') || '<div class="mhint">No vehicles added.</div>';
}
function addVehicleRow(){ mVehicles.push({number:'',type:'Car',slot:''}); renderVehicleRows(); }
function removeVehicleRow(i){ mVehicles.splice(i,1); renderVehicleRows(); }

function handleMemberPhoto(event){
  const file = event.target.files && event.target.files[0];
  if(!file) return;
  if(file.size > 1.5*1024*1024) return toast('Please choose an image under 1.5 MB','warn');
  const reader = new FileReader();
  reader.onload = e => {
    mPhoto = e.target.result;
    const av = document.getElementById('m-avatar');
    if(av){ av.style.backgroundImage = `url('${mPhoto}')`; av.textContent=''; }
  };
  reader.readAsDataURL(file);
}

async function saveMyProfile(){
  const name = document.getElementById('m-name').value.trim();
  if(!name) return toast('Name is required','warn');
  const payload = {
    name,
    email: document.getElementById('m-email').value.trim(),
    mobile: document.getElementById('m-mobile').value.trim(),
    occupancyType: document.getElementById('m-occupancy').value,
    emergencyContact: document.getElementById('m-emergency').value.trim(),
    profilePhoto: mPhoto || null,
    familyJson: JSON.stringify(mFamily.filter(f=>f.name||f.relationship||f.mobile)),
    vehiclesJson: JSON.stringify(mVehicles.filter(v=>v.number)),
    notifyPrefsJson: JSON.stringify({
      sms: document.getElementById('m-pref-sms').checked,
      email: document.getElementById('m-pref-email').checked,
      whatsapp: document.getElementById('m-pref-whatsapp').checked
    })
  };
  try{
    await Api.updateMe(payload);
    await loadMe();
    renderMemberHome();
    updateSidebarUserInfo();
    toast('Profile updated successfully');
  }catch(err){ toast(err.message || 'Could not save profile','warn'); }
}

async function changeMyPassword(){
  const cur = document.getElementById('m-pw-current').value;
  const nw  = document.getElementById('m-pw-new').value;
  const cf  = document.getElementById('m-pw-confirm').value;
  if(!cur || !nw) return toast('Enter your current and new password','warn');
  if(nw.length < 4) return toast('New password must be at least 4 characters','warn');
  if(nw !== cf) return toast('New passwords do not match','warn');
  try{
    await Api.changeMyPassword(cur, nw);
    ['m-pw-current','m-pw-new','m-pw-confirm'].forEach(id=>{ document.getElementById(id).value=''; });
    toast('Password changed successfully');
  }catch(err){ toast(err.message || 'Could not change password','warn'); }
}


