// ══════════════════════════════════════════════════════════
// SMMS Platform Console (super-admin) — self-contained client.
// Talks to the control-plane API (api/platform/*) via same-origin
// /api (nginx proxies admin.localhost → smms-api with Host header,
// which the API resolves to the control plane). The platform JWT
// lives in sessionStorage, scoped to this origin (admin.*) only.
// ══════════════════════════════════════════════════════════
const API = '/api';
const TOKEN_KEY = 'smms_platform_token';
const USER_KEY  = 'smms_platform_user';

// ─── session ───
function getToken(){ return sessionStorage.getItem(TOKEN_KEY); }
function getUser(){ try{ return JSON.parse(sessionStorage.getItem(USER_KEY) || 'null'); }catch(e){ return null; } }
function setSession(token, user){
  sessionStorage.setItem(TOKEN_KEY, token);
  sessionStorage.setItem(USER_KEY, JSON.stringify(user));
}
function clearSession(){
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(USER_KEY);
}

// ─── fetch wrapper ───
async function pfetch(path, options = {}){
  const headers = Object.assign({ 'Content-Type': 'application/json' }, options.headers || {});
  const token = getToken();
  if(token) headers['Authorization'] = 'Bearer ' + token;

  let res;
  try{
    res = await fetch(API + path, { ...options, headers });
  }catch(e){
    throw new Error('Could not reach the platform server.');
  }
  if(res.status === 401 || res.status === 403){
    // Platform token missing/expired/insufficient — force re-login.
    clearSession();
    document.body.classList.add('logged-out');
    throw new Error('Session expired. Please sign in again.');
  }
  if(!res.ok){
    let msg = `Request failed (${res.status})`;
    try{ const b = await res.json(); if(b && b.message) msg = b.message; else if(b && b.title) msg = b.title; }catch(e){}
    throw new Error(msg);
  }
  if(res.status === 204) return null;
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

const Platform = {
  login:  (username, password) => pfetch('/platform/auth/login', { method:'POST', body: JSON.stringify({ username, password }) }),
  dashboard: () => pfetch('/platform/dashboard'),
  societies: () => pfetch('/platform/societies'),
  onboard: (payload) => pfetch('/platform/societies', { method:'POST', body: JSON.stringify(payload) }),
  suspend: (key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/suspend`, { method:'POST' }),
  activate:(key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/activate`, { method:'POST' }),
  renew:  (key, expiryDate, plan) => pfetch(`/platform/societies/${encodeURIComponent(key)}/renew`, { method:'POST', body: JSON.stringify({ expiryDate, plan }) }),
  impersonate:(key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/impersonate`, { method:'POST' }),
  generateDemo:(key, preset) => pfetch(`/platform/societies/${encodeURIComponent(key)}/generate-demo?preset=${encodeURIComponent(preset)}`, { method:'POST' }),
  resetDemo:(key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/reset-demo`, { method:'POST' }),
  audit: (take = 100) => pfetch(`/platform/audit?take=${take}`),
  pending: () => pfetch('/platform/societies/pending'),
  approve: (key, payload) => pfetch(`/platform/societies/${encodeURIComponent(key)}/approve`, { method:'POST', body: JSON.stringify(payload || {}) }),
  reject:  (key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/reject`, { method:'POST' }),
  deletePermanently: (key, confirmKey) => pfetch(`/platform/societies/${encodeURIComponent(key)}`, { method:'DELETE', body: JSON.stringify({ confirmKey }) }),
  plans:            () => pfetch('/platform/plans'),
  createPlan:       (p) => pfetch('/platform/plans', { method:'POST', body: JSON.stringify(p) }),
  updatePlan:       (id, p) => pfetch(`/platform/plans/${id}`, { method:'PUT', body: JSON.stringify(p) }),
  togglePlan:       (id) => pfetch(`/platform/plans/${id}/toggle`, { method:'POST' }),
  invoices:         (q = '') => pfetch('/platform/invoices' + q),
  invoiceSummary:   () => pfetch('/platform/invoices/summary'),
  generateInvoice:  (p) => pfetch('/platform/invoices', { method:'POST', body: JSON.stringify(p) }),
  payInvoice:       (id, ref) => pfetch(`/platform/invoices/${id}/pay`, { method:'POST', body: JSON.stringify({ paymentReference: ref }) }),
  voidInvoice:      (id) => pfetch(`/platform/invoices/${id}/void`, { method:'POST' }),
  users:            () => pfetch('/platform/users'),
  createUser:       (p) => pfetch('/platform/users', { method:'POST', body: JSON.stringify(p) }),
  toggleUser:       (id) => pfetch(`/platform/users/${id}/toggle`, { method:'POST' }),
  resetUserPassword:(id, pw) => pfetch(`/platform/users/${id}/reset-password`, { method:'POST', body: JSON.stringify({ newPassword: pw }) }),
  tickets:          (q = '') => pfetch('/platform/support' + q),
  ticket:           (id) => pfetch(`/platform/support/${id}`),
  ticketSummary:    () => pfetch('/platform/support/summary'),
  createTicket:     (p) => pfetch('/platform/support', { method:'POST', body: JSON.stringify(p) }),
  ticketStatus:     (id, status, note) => pfetch(`/platform/support/${id}/status`, { method:'POST', body: JSON.stringify({ status, note }) }),
  ticketReply:      (id, body) => pfetch(`/platform/support/${id}/messages`, { method:'POST', body: JSON.stringify({ body }) }),
  settings:         () => pfetch('/platform/settings'),
  saveSettings:     (p) => pfetch('/platform/settings', { method:'PUT', body: JSON.stringify(p) }),
};

// ─── toast ───
let toastTimer;
function toast(msg, kind = ''){
  const el = document.getElementById('toast');
  el.textContent = msg;
  el.className = 'toast show ' + kind;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.className = 'toast ' + kind, 3200);
}

// ─── helpers ───
function esc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function fmtDate(d){ if(!d) return '—'; const t = new Date(d); return isNaN(t) ? '—' : t.toLocaleDateString(undefined,{year:'numeric',month:'short',day:'numeric'}); }
function statusBadge(s){ const k = String(s || '').toLowerCase(); return `<span class="badge ${k}">${esc(s)}</span>`; }

// Build the tenant subdomain URL for the current environment by swapping the
// leading "admin." label for the society key (works for admin.localhost:8080
// and admin.smms.local etc).
function tenantUrl(key){
  const host = window.location.host;               // admin.localhost:8080
  const base = host.replace(/^admin\./, '');       // localhost:8080
  return `${window.location.protocol}//${key}.${base}/`;
}

// ─── auth ───
async function platformLogin(){
  const u = document.getElementById('login-username').value.trim();
  const p = document.getElementById('login-password').value;
  const err = document.getElementById('loginError');
  err.textContent = '';
  if(!u || !p){ err.textContent = 'Enter username and password.'; return; }
  try{
    const res = await Platform.login(u, p);
    setSession(res.token, { username: res.username, displayName: res.displayName });
    document.getElementById('login-password').value = '';
    enterApp();
  }catch(e){
    err.textContent = '❌ ' + (e.message || 'Invalid credentials.');
  }
}

function platformLogout(){
  clearSession();
  document.body.classList.add('logged-out');
}

function enterApp(){
  const user = getUser();
  document.getElementById('userName').textContent = user?.displayName || user?.username || 'Super Admin';
  document.getElementById('userAvatar').textContent = (user?.displayName || user?.username || 'S').charAt(0).toUpperCase();
  document.body.classList.remove('logged-out');
  showView('dashboard', document.querySelector('[data-view=dashboard]'));
  refreshPendingBadge();
}

// ─── view routing ───
let currentView = 'dashboard';
const TITLES = { dashboard:'Dashboard', pending:'Pending Society Requests', societies:'Approved Societies', onboard:'Onboard Society', subscriptions:'Subscriptions', payments:'Payments', users:'Users', support:'Support', settings:'Settings', audit:'Audit Logs' };

function showView(view, navEl){
  currentView = view;
  document.querySelectorAll('.view').forEach(v => v.classList.remove('active'));
  document.getElementById('view-' + view).classList.add('active');
  document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
  if(navEl) navEl.classList.add('active');
  else document.querySelector(`[data-view=${view}]`)?.classList.add('active');
  document.getElementById('viewTitle').textContent = TITLES[view] || view;
  loadView(view);
}

function refreshCurrentView(){ loadView(currentView); }

function loadView(view){
  if(view === 'dashboard') return loadDashboard();
  if(view === 'pending')   return loadPending();
  if(view === 'societies') return loadSocieties();
  if(view === 'subscriptions') return loadPlans();
  if(view === 'payments')  return loadPayments();
  if(view === 'users')     return loadUsers();
  if(view === 'support')   return loadSupport();
  if(view === 'settings')  return loadSettings();
  if(view === 'audit')     return loadAudit();
}

// ─── dashboard ───
async function loadDashboard(){
  const grid = document.getElementById('kpiGrid');
  const tbl = document.getElementById('dashSocieties');
  grid.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const d = await Platform.dashboard();
    grid.innerHTML = `
      ${kpi(d.totalSocieties, 'Total societies', '')}
      ${kpi(d.activeSocieties, 'Active', 'green')}
      ${kpi(d.suspendedSocieties, 'Suspended', 'red')}
      ${kpi(d.expiredSocieties, 'Expired', 'red')}
      ${kpi(d.expiringSoonSocieties, 'Expiring soon', 'amber')}
      ${kpi(d.trialSocieties, 'Trial', 'amber')}
      ${kpi(d.totalMembers, 'Total members', '')}`;
    tbl.innerHTML = societyTable(d.societies, false);
  }catch(e){
    grid.innerHTML = `<div class="empty">${esc(e.message)}</div>`;
    tbl.innerHTML = '';
  }
}
function kpi(val, lbl, kind){
  return `<div class="kpi ${kind}"><div class="k-val">${val ?? 0}</div><div class="k-lbl">${lbl}</div></div>`;
}

// ─── societies ───
async function loadSocieties(){
  const el = document.getElementById('societiesTable');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const list = await Platform.societies();
    const approved = (list || []).filter(s => String(s.status).toLowerCase() !== 'pending');
    el.innerHTML = societyTable(approved, true);
  }catch(e){
    el.innerHTML = `<div class="empty">${esc(e.message)}</div>`;
  }
}

function societyTable(list, withActions){
  if(!list || !list.length) return '<div class="empty">No societies yet.</div>';
  const rows = list.map(s => {
    const status = String(s.status).toLowerCase();
    const suspended = status === 'suspended';
    const expired = s.isExpired || status === 'expired';
    const blocked = suspended || expired;
    const actions = withActions ? `
      <td><div class="row-actions">
        <button class="btn btn-light btn-sm" onclick="doImpersonate('${esc(s.key)}')" ${blocked ? 'disabled title="Renew/activate first"' : ''}>🔓 Impersonate</button>
        ${expired
          ? `<button class="btn btn-primary btn-sm" onclick="doRenew('${esc(s.key)}')">♻ Renew</button>`
          : suspended
            ? `<button class="btn btn-light btn-sm" onclick="doActivate('${esc(s.key)}')">▶ Activate</button>`
            : `<button class="btn btn-light btn-sm" onclick="doRenew('${esc(s.key)}')">♻ Renew</button>
               <button class="btn btn-light btn-sm" onclick="doSuspend('${esc(s.key)}')">⏸ Suspend</button>`}
        <a class="btn btn-light btn-sm" href="${tenantUrl(s.key)}" target="_blank" rel="noopener">↗ Open</a>
        ${s.isDemo ? `<button class="btn btn-light btn-sm" onclick="openDemoTools('${esc(s.key)}')">🧪 Demo Tools</button>` : ''}
        ${blocked ? `<button class="btn btn-danger btn-sm" onclick="doDeletePermanently('${esc(s.key)}','${esc(s.displayName)}')">🗑 Delete forever</button>` : ''}
      </div></td>` : '';
    return `<tr>
      <td><div class="society-name">${esc(s.displayName)}</div><div class="sub-key">${esc(s.key)}.localhost</div></td>
      <td>${statusBadge(expired ? 'Expired' : s.status)}</td>
      <td>${esc(s.plan || '—')}</td>
      <td>${s.memberCount ?? 0}</td>
      <td>${s.flatCount ?? 0}</td>
      <td>${expiryCell(s)}</td>
      <td>${fmtDate(s.createdAt)}</td>
      ${actions}
    </tr>`;
  }).join('');
  return `<table>
    <thead><tr>
      <th>Society</th><th>Status</th><th>Plan</th><th>Members</th><th>Flats</th><th>Expiry</th><th>Created</th>
      ${withActions ? '<th>Actions</th>' : ''}
    </tr></thead>
    <tbody>${rows}</tbody>
  </table>`;
}

// Expiry date plus a derived pill: "Expired", "Xd left" (amber when ≤14 days).
function expiryCell(s){
  const base = fmtDate(s.expiryDate);
  if(s.expiryDate == null) return base;
  const d = s.daysUntilExpiry;
  if(s.isExpired || (typeof d === 'number' && d < 0))
    return `${base} <span class="pill red">Expired</span>`;
  if(typeof d === 'number' && d <= 14)
    return `${base} <span class="pill amber">${d}d left</span>`;
  return base;
}

async function doSuspend(key){
  if(!confirm(`Suspend "${key}"? Its members and admins will be blocked from logging in.`)) return;
  try{ await Platform.suspend(key); toast(`Suspended ${key}`, 'warn'); refreshCurrentView(); }
  catch(e){ toast(e.message, 'warn'); }
}
async function doActivate(key){
  try{ await Platform.activate(key); toast(`Activated ${key}`, 'ok'); refreshCurrentView(); }
  catch(e){ toast(e.message, 'warn'); }
}

// Two gates on purpose. The first states plainly what is lost; the second makes the operator
// type the key, so muscle memory on a confirm dialog cannot destroy a tenant.
async function doDeletePermanently(key, displayName){
  const warning =
    `PERMANENTLY DELETE "${displayName}" (${key})?\n\n` +
    `This drops the entire tenant database: members, residents' logins, collections,\n` +
    `payments, expenses and complaints.\n\n` +
    `There is no backup and no undo. Nobody can recover this, including support.`;
  if(!confirm(warning)) return;

  const typed = prompt(`Type the society key "${key}" to confirm permanent deletion:`);
  if(typed === null) return;
  if(typed !== key){ toast('The key did not match. Nothing was deleted.', 'warn'); return; }

  try{
    const r = await Platform.deletePermanently(key, typed);
    const d = r.destroyed || {};
    toast(r.databaseExisted
      ? `Deleted ${key}: ${d.members ?? 0} members, ${d.collections ?? 0} collections destroyed.`
      : `Deleted ${key} (no database existed).`, 'ok');
    refreshCurrentView();
  }catch(e){ toast(e.message, 'warn'); }
}
async function doRenew(key){
  const def = new Date(); def.setFullYear(def.getFullYear() + 1);
  const input = prompt(`Renew "${key}" — enter new expiry date (YYYY-MM-DD):`, def.toISOString().slice(0,10));
  if(!input) return;
  const when = new Date(input + 'T00:00:00');
  if(isNaN(when)){ toast('Invalid date. Use YYYY-MM-DD.', 'warn'); return; }
  try{ await Platform.renew(key, when.toISOString(), null); toast(`Renewed ${key} to ${input}`, 'ok'); refreshCurrentView(); }
  catch(e){ toast(e.message, 'warn'); }
}
async function doImpersonate(key){
  try{
    const res = await Platform.impersonate(key);
    // Hand the short-lived tenant token to the society's own subdomain via the
    // URL fragment (not a query param → not sent to the server / not logged).
    const url = tenantUrl(key) + 'index.html#imp=' + encodeURIComponent(res.token);
    window.open(url, '_blank', 'noopener');
    toast(`Opened ${key} as ${res.adminUsername}`, 'ok');
  }catch(e){ toast(e.message, 'warn'); }
}

// ══════════════ DEMO TOOLS (demo/sandbox societies only) ══════════════
function openDemoTools(key){
  openModal(`
    <h3>🧪 Demo Data — ${esc(key)}</h3>
    <p class="modal-sub">Generate a rich, realistic dataset for this <b>demo</b> society, or wipe it clean.
    These tools only run against demo tenants — real societies are never touched.</p>
    <div class="field"><label>Dataset size</label>
      <select id="demoPreset">
        <option value="small">Small — 40 flats</option>
        <option value="medium">Medium — 120 flats</option>
        <option value="large">Large — 400 flats</option>
      </select>
    </div>
    <p class="modal-sub">⚠ Generating replaces all existing residents, invoices, expenses and complaints in this demo.</p>
    <div class="modal-actions">
      <button class="btn btn-light" onclick="closeModal()">Cancel</button>
      <button class="btn btn-light" onclick="doResetDemo('${esc(key)}')">🗑 Reset (wipe)</button>
      <button class="btn btn-primary" onclick="doGenerateDemo('${esc(key)}')">✨ Generate</button>
    </div>`);
}
async function doGenerateDemo(key){
  const preset = document.getElementById('demoPreset')?.value || 'small';
  toast(`Generating ${preset} demo data…`);
  try{
    const r = await Platform.generateDemo(key, preset);
    closeModal();
    toast(`Generated: ${r.members} members, ${r.collections} invoices, ${r.complaints} complaints`, 'ok');
    refreshCurrentView();
  }catch(e){ toast(e.message, 'warn'); }
}
async function doResetDemo(key){
  if(!confirm(`Wipe ALL demo data for ${key}? This cannot be undone.`)) return;
  try{
    await Platform.resetDemo(key);
    closeModal();
    toast(`Demo data wiped for ${key}`, 'warn');
    refreshCurrentView();
  }catch(e){ toast(e.message, 'warn'); }
}

// ─── onboard ───
function onKeyInput(){
  const v = document.getElementById('ob-key').value.trim().toLowerCase();
  document.getElementById('ob-preview').textContent = v || 'key';
}
async function submitOnboard(){
  const btn = document.getElementById('ob-submit');
  const status = document.getElementById('ob-status');
  const payload = {
    key: document.getElementById('ob-key').value.trim().toLowerCase(),
    displayName: document.getElementById('ob-name').value.trim(),
    plan: document.getElementById('ob-plan').value || null,
    flatCount: parseInt(document.getElementById('ob-flats').value, 10) || 0,
    expiryDate: document.getElementById('ob-expiry').value || null,
    adminUsername: document.getElementById('ob-adminuser').value.trim() || null,
    adminPassword: document.getElementById('ob-adminpass').value || null,
  };
  status.textContent = '';
  status.className = 'form-status';
  if(!payload.key){ status.textContent = 'Society key is required.'; status.className = 'form-status err'; return; }
  if(!payload.displayName){ status.textContent = 'Display name is required.'; status.className = 'form-status err'; return; }

  btn.disabled = true;
  status.textContent = 'Provisioning database…';
  try{
    const s = await Platform.onboard(payload);
    status.textContent = `✔ ${s.displayName} created (${s.dbName}).`;
    status.className = 'form-status ok';
    toast(`Onboarded ${s.displayName}`, 'ok');
    // Reset the form for the next society.
    ['ob-key','ob-name','ob-flats','ob-expiry','ob-adminuser','ob-adminpass'].forEach(id => document.getElementById(id).value = '');
    document.getElementById('ob-flats').value = '0';
    document.getElementById('ob-preview').textContent = 'key';
    setTimeout(() => showView('societies', document.querySelector('[data-view=societies]')), 900);
  }catch(e){
    status.textContent = '❌ ' + e.message;
    status.className = 'form-status err';
  }finally{
    btn.disabled = false;
  }
}

// ─── audit ───
async function loadAudit(){
  const el = document.getElementById('auditTable');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const logs = await Platform.audit(150);
    if(!logs.length){ el.innerHTML = '<div class="empty">No audit entries yet.</div>'; return; }
    el.innerHTML = `<table>
      <thead><tr><th>When</th><th>Actor</th><th>Action</th><th>Target</th><th>Details</th></tr></thead>
      <tbody>${logs.map(l => `<tr>
        <td>${fmtDateTime(l.timestamp)}</td>
        <td>${esc(l.actorUsername)}</td>
        <td><span class="badge active">${esc(l.action)}</span></td>
        <td>${esc(l.targetKey || l.targetType || '—')}</td>
        <td>${esc(l.details || '')}</td>
      </tr>`).join('')}</tbody>
    </table>`;
  }catch(e){
    el.innerHTML = `<div class="empty">${esc(e.message)}</div>`;
  }
}
function fmtDateTime(d){ const t = new Date(d); return isNaN(t) ? '—' : t.toLocaleString(); }

// ─── pending society requests ───
async function loadPending(){
  const el = document.getElementById('pendingList');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const list = await Platform.pending();
    updatePendingBadge(list.length);
    if(!list.length){ el.innerHTML = '<div class="empty">🎉 No pending requests — all caught up.</div>'; return; }
    el.innerHTML = list.map(pendingCard).join('');
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}

function pendingCard(s){
  const nm = esc(s.displayName).replace(/'/g, "\\'");
  return `<div class="pending-card">
    <div class="pc-head">
      <div><div class="society-name">${esc(s.displayName)}</div>
        <div class="sub-key">${esc(s.key)}.ssms.yuvaansoft.shop</div></div>
      ${statusBadge('Pending')}
    </div>
    <div class="pc-grid">
      <div><span class="pc-l">Contact</span>${esc(s.adminName || '—')}</div>
      <div><span class="pc-l">Email</span>${esc(s.adminEmail || '—')}</div>
      <div><span class="pc-l">Phone</span>${esc(s.phone || '—')}</div>
      <div><span class="pc-l">Plan</span>${esc(s.plan || '—')}</div>
      <div><span class="pc-l">Flats</span>${s.flatCount ?? 0}</div>
      <div><span class="pc-l">Requested</span>${fmtDate(s.createdAt)}</div>
      ${s.address ? `<div class="pc-wide"><span class="pc-l">Address</span>${esc(s.address)}</div>` : ''}
    </div>
    <div class="pc-actions">
      <button class="btn btn-primary btn-sm" onclick="openApprove('${esc(s.key)}','${nm}')">✔ Approve &amp; provision</button>
      <button class="btn btn-light btn-sm" onclick="doReject('${esc(s.key)}','${nm}')">✖ Reject</button>
    </div>
  </div>`;
}

function openApprove(key, name){
  const ny = new Date(); ny.setFullYear(ny.getFullYear() + 1);
  openModal(`
    <h3>Approve “${esc(name)}”</h3>
    <p class="modal-sub">Provisions an isolated database and admin account. Leave credentials blank to use the default username <code>admin</code> with an auto-generated password.</p>
    <div class="field"><label>Admin username</label><input id="ap-user" placeholder="admin (default)" autocomplete="off"></div>
    <div class="field"><label>Admin password</label><input id="ap-pass" placeholder="auto-generated" autocomplete="off"></div>
    <div class="field"><label>Subscription expiry</label><input id="ap-exp" type="date" value="${ny.toISOString().slice(0,10)}"></div>
    <p id="ap-status" class="form-status"></p>
    <div class="modal-actions">
      <button class="btn btn-light" onclick="closeModal()">Cancel</button>
      <button class="btn btn-primary" id="ap-go" onclick="doApprove('${esc(key)}')">Approve &amp; provision</button>
    </div>`);
}

async function doApprove(key){
  const btn = document.getElementById('ap-go');
  const status = document.getElementById('ap-status');
  const payload = {
    adminUsername: document.getElementById('ap-user').value.trim() || null,
    adminPassword: document.getElementById('ap-pass').value || null,
    expiryDate: document.getElementById('ap-exp').value || null,
  };
  btn.disabled = true; status.className = 'form-status'; status.textContent = 'Provisioning database…';
  try{
    const r = await Platform.approve(key, payload);
    openModal(`
      <h3>✅ ${esc(r.society.displayName)} is live</h3>
      <p class="modal-sub">Share these one-time credentials with the society admin — they won't be shown again.</p>
      <div class="cred"><span>Portal</span><code>${esc(r.portalUrl)}</code><button class="btn btn-light btn-sm" onclick="copyText('${esc(r.portalUrl)}')">Copy</button></div>
      <div class="cred"><span>Username</span><code>${esc(r.adminUsername)}</code><button class="btn btn-light btn-sm" onclick="copyText('${esc(r.adminUsername)}')">Copy</button></div>
      <div class="cred"><span>Password</span><code>${esc(r.adminPassword)}</code><button class="btn btn-light btn-sm" onclick="copyText('${esc(r.adminPassword)}')">Copy</button></div>
      <div class="modal-actions"><button class="btn btn-primary" onclick="closeModal()">Done</button></div>`);
    toast(`Approved ${key}`, 'ok');
    refreshPendingBadge();
  }catch(e){
    btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message;
  }
}

async function doReject(key, name){
  if(!confirm(`Reject and delete the pending request for “${name || key}”? This cannot be undone.`)) return;
  try{ await Platform.reject(key); toast(`Rejected ${key}`, 'warn'); loadPending(); }
  catch(e){ toast(e.message, 'warn'); }
}

async function refreshPendingBadge(){
  try{ const list = await Platform.pending(); updatePendingBadge(list.length); }
  catch(e){ /* silent — badge is best-effort */ }
}
function updatePendingBadge(n){
  const b = document.getElementById('pendingCount');
  if(!b) return;
  b.textContent = n; b.hidden = !n;
}

// ─── modal + clipboard ───
function openModal(html){
  const root = document.getElementById('modalRoot');
  root.innerHTML = `<div class="modal-overlay" onclick="if(event.target===this)closeModal()"><div class="modal-card">${html}</div></div>`;
  root.classList.add('show');
}
function closeModal(){ const r = document.getElementById('modalRoot'); r.classList.remove('show'); r.innerHTML = ''; }
function copyText(t){ if(navigator.clipboard) navigator.clipboard.writeText(t).then(() => toast('Copied', 'ok')); }
function fmtMoney(v, cur){ const n = Number(v || 0); return (cur || 'INR') + ' ' + n.toLocaleString(undefined, { minimumFractionDigits:0, maximumFractionDigits:2 }); }

// ══════════════ SUBSCRIPTIONS (plans) ══════════════
let _plans = [];
async function loadPlans(){
  const el = document.getElementById('plansTable');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    _plans = await Platform.plans();
    if(!_plans.length){ el.innerHTML = '<div class="empty">No plans yet. Create your first plan.</div>'; return; }
    el.innerHTML = `<table>
      <thead><tr><th>Code</th><th>Name</th><th>Price</th><th>Billing</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>${_plans.map(planRow).join('')}</tbody></table>`;
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
function planRow(p){
  return `<tr>
    <td><code>${esc(p.code)}</code></td>
    <td>${esc(p.name)}</td>
    <td>${fmtMoney(p.price, p.currency)}</td>
    <td>${p.billingPeriodMonths} mo</td>
    <td>${p.isActive ? '<span class="badge active">Active</span>' : '<span class="badge suspended">Inactive</span>'}</td>
    <td><div class="row-actions">
      <button class="btn btn-light btn-sm" onclick="openPlanModal(${p.id})">✎ Edit</button>
      <button class="btn btn-light btn-sm" onclick="doTogglePlan(${p.id})">${p.isActive ? '⏸ Deactivate' : '▶ Activate'}</button>
    </div></td></tr>`;
}
function openPlanModal(id){
  const p = id ? _plans.find(x => x.id === id) : null;
  openModal(`
    <h3>${p ? 'Edit plan' : 'New plan'}</h3>
    <div class="field"><label>Code ${p ? '(locked)' : ''}</label><input id="pl-code" value="${p ? esc(p.code) : ''}" placeholder="STANDARD" ${p ? 'disabled' : ''}></div>
    <div class="field"><label>Name</label><input id="pl-name" value="${p ? esc(p.name) : ''}" placeholder="Standard"></div>
    <div class="grid2">
      <div class="field"><label>Price</label><input id="pl-price" type="number" min="0" step="0.01" value="${p ? p.price : 0}"></div>
      <div class="field"><label>Billing months</label><input id="pl-months" type="number" min="1" value="${p ? p.billingPeriodMonths : 12}"></div>
    </div>
    <div class="grid2">
      <div class="field"><label>Currency</label><input id="pl-cur" value="${p ? esc(p.currency) : 'INR'}" maxlength="3"></div>
      <div class="field"><label>Status</label><select id="pl-active"><option value="true" ${(!p || p.isActive) ? 'selected' : ''}>Active</option><option value="false" ${(p && !p.isActive) ? 'selected' : ''}>Inactive</option></select></div>
    </div>
    <p id="pl-status" class="form-status"></p>
    <div class="modal-actions">
      <button class="btn btn-light" onclick="closeModal()">Cancel</button>
      <button class="btn btn-primary" id="pl-go" onclick="savePlan(${p ? p.id : 'null'})">${p ? 'Save' : 'Create'}</button>
    </div>`);
}
async function savePlan(id){
  const status = document.getElementById('pl-status'), btn = document.getElementById('pl-go');
  const payload = {
    code: document.getElementById('pl-code').value.trim(),
    name: document.getElementById('pl-name').value.trim(),
    price: parseFloat(document.getElementById('pl-price').value) || 0,
    billingPeriodMonths: parseInt(document.getElementById('pl-months').value, 10) || 12,
    currency: document.getElementById('pl-cur').value.trim() || 'INR',
    isActive: document.getElementById('pl-active').value === 'true',
  };
  status.className = 'form-status'; status.textContent = 'Saving…'; btn.disabled = true;
  try{
    if(id) await Platform.updatePlan(id, payload); else await Platform.createPlan(payload);
    closeModal(); toast('Plan saved', 'ok'); loadPlans();
  }catch(e){ btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message; }
}
async function doTogglePlan(id){
  try{ await Platform.togglePlan(id); toast('Plan updated', 'ok'); loadPlans(); }
  catch(e){ toast(e.message, 'warn'); }
}

// ══════════════ PAYMENTS (invoices) ══════════════
async function loadPayments(){
  const kp = document.getElementById('payKpis'), el = document.getElementById('invoicesTable');
  kp.innerHTML = ''; el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const [sum, list] = await Promise.all([Platform.invoiceSummary(), Platform.invoices()]);
    kp.innerHTML = `
      ${kpi(fmtMoney(sum.collectedAmount, sum.currency), 'Collected', 'green')}
      ${kpi(fmtMoney(sum.outstandingAmount, sum.currency), 'Outstanding', 'amber')}
      ${kpi(sum.paid, 'Paid', 'green')}
      ${kpi(sum.overdue, 'Overdue', 'red')}
      ${kpi(sum.issued, 'Issued', '')}
      ${kpi(sum.total, 'Total invoices', '')}`;
    if(!list.length){ el.innerHTML = '<div class="empty">No invoices yet. Generate one to bill a society.</div>'; return; }
    el.innerHTML = `<table>
      <thead><tr><th>Invoice</th><th>Society</th><th>Plan</th><th>Amount</th><th>Period</th><th>Due</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>${list.map(invoiceRow).join('')}</tbody></table>`;
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
function invoiceRow(i){
  const st = String(i.status).toLowerCase();
  const actionable = st !== 'paid' && st !== 'void';
  return `<tr>
    <td><code>${esc(i.invoiceNumber)}</code></td>
    <td>${esc(i.societyName || i.societyKey)}</td>
    <td>${esc(i.planName)}</td>
    <td>${fmtMoney(i.amount, i.currency)}</td>
    <td>${fmtDate(i.periodStart)} → ${fmtDate(i.periodEnd)}</td>
    <td>${fmtDate(i.dueDate)}</td>
    <td><span class="badge ${st}">${esc(i.status)}</span></td>
    <td><div class="row-actions">
      ${actionable ? `<button class="btn btn-primary btn-sm" onclick="doPayInvoice(${i.id})">✔ Mark paid</button>
                      <button class="btn btn-light btn-sm" onclick="doVoidInvoice(${i.id})">✖ Void</button>` : ''}
      ${st === 'paid' && i.paymentReference ? `<span class="pill green">ref: ${esc(i.paymentReference)}</span>` : ''}
    </div></td></tr>`;
}
async function openInvoiceModal(){
  openModal('<h3>Generate invoice</h3><p class="empty">Loading…</p>');
  try{
    const [socs, plans] = await Promise.all([Platform.societies(), Platform.plans()]);
    const active = (socs || []).filter(s => String(s.status).toLowerCase() !== 'pending');
    const activePlans = (plans || []).filter(p => p.isActive);
    openModal(`
      <h3>Generate invoice</h3>
      <div class="field"><label>Society</label><select id="in-soc">${active.map(s => `<option value="${esc(s.key)}">${esc(s.displayName)} (${esc(s.key)})</option>`).join('')}</select></div>
      <div class="field"><label>Plan</label><select id="in-plan">${activePlans.map(p => `<option value="${p.id}">${esc(p.name)} — ${fmtMoney(p.price, p.currency)}/${p.billingPeriodMonths}mo</option>`).join('')}</select></div>
      <div class="grid2">
        <div class="field"><label>Period start</label><input id="in-start" type="date" value="${new Date().toISOString().slice(0,10)}"></div>
        <div class="field"><label>Due in (days)</label><input id="in-due" type="number" min="1" value="14"></div>
      </div>
      <div class="field"><label>Notes</label><input id="in-notes" placeholder="optional"></div>
      <p id="in-status" class="form-status"></p>
      <div class="modal-actions">
        <button class="btn btn-light" onclick="closeModal()">Cancel</button>
        <button class="btn btn-primary" id="in-go" onclick="doGenerateInvoice()">Generate</button>
      </div>`);
    if(!active.length || !activePlans.length){
      const s = document.getElementById('in-status'); s.className = 'form-status err';
      s.textContent = !activePlans.length ? 'No active plans — create one under Subscriptions first.' : 'No societies available.';
      document.getElementById('in-go').disabled = true;
    }
  }catch(e){
    openModal(`<h3>Generate invoice</h3><p class="form-status err">${esc(e.message)}</p><div class="modal-actions"><button class="btn btn-light" onclick="closeModal()">Close</button></div>`);
  }
}
async function doGenerateInvoice(){
  const status = document.getElementById('in-status'), btn = document.getElementById('in-go');
  const payload = {
    societyKey: document.getElementById('in-soc').value,
    planId: parseInt(document.getElementById('in-plan').value, 10),
    periodStart: document.getElementById('in-start').value || null,
    dueInDays: parseInt(document.getElementById('in-due').value, 10) || 14,
    notes: document.getElementById('in-notes').value.trim() || null,
  };
  if(!payload.societyKey || !payload.planId){ status.className = 'form-status err'; status.textContent = 'Select a society and plan.'; return; }
  status.className = 'form-status'; status.textContent = 'Generating…'; btn.disabled = true;
  try{ await Platform.generateInvoice(payload); closeModal(); toast('Invoice generated', 'ok'); loadPayments(); }
  catch(e){ btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message; }
}
async function doPayInvoice(id){
  const ref = prompt('Payment reference (optional — UPI txn id, cheque no, etc.):', '');
  if(ref === null) return;
  try{ await Platform.payInvoice(id, ref.trim() || null); toast('Marked paid', 'ok'); loadPayments(); }
  catch(e){ toast(e.message, 'warn'); }
}
async function doVoidInvoice(id){
  if(!confirm('Void this invoice? This cannot be undone.')) return;
  try{ await Platform.voidInvoice(id); toast('Invoice voided', 'warn'); loadPayments(); }
  catch(e){ toast(e.message, 'warn'); }
}

// ══════════════ USERS (platform operators) ══════════════
async function loadUsers(){
  const el = document.getElementById('usersTable');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const users = await Platform.users();
    el.innerHTML = `<table>
      <thead><tr><th>Username</th><th>Display name</th><th>Status</th><th>Created</th><th>Last login</th><th>Actions</th></tr></thead>
      <tbody>${users.map(userRow).join('')}</tbody></table>`;
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
function userRow(u){
  const me = getUser();
  const isMe = me && me.username && me.username.toLowerCase() === u.username.toLowerCase();
  return `<tr>
    <td>${esc(u.username)}${isMe ? ' <span class="pill neutral">you</span>' : ''}</td>
    <td>${esc(u.displayName || '—')}</td>
    <td>${u.isActive ? '<span class="badge active">Active</span>' : '<span class="badge suspended">Inactive</span>'}</td>
    <td>${fmtDate(u.createdAt)}</td>
    <td>${u.lastLoginAt ? fmtDateTime(u.lastLoginAt) : '—'}</td>
    <td><div class="row-actions">
      <button class="btn btn-light btn-sm" onclick="doResetPassword(${u.id})">🔑 Reset password</button>
      <button class="btn btn-light btn-sm" onclick="doToggleUser(${u.id})" ${isMe ? 'disabled title="You cannot deactivate yourself"' : ''}>${u.isActive ? '⏸ Deactivate' : '▶ Activate'}</button>
    </div></td></tr>`;
}
function openUserModal(){
  openModal(`
    <h3>Add super-admin</h3>
    <div class="field"><label>Username</label><input id="us-name" placeholder="username" autocomplete="off"></div>
    <div class="field"><label>Display name</label><input id="us-disp" placeholder="Full name (optional)" autocomplete="off"></div>
    <div class="field"><label>Password</label><input id="us-pass" type="text" placeholder="min 6 characters" autocomplete="off"></div>
    <p id="us-status" class="form-status"></p>
    <div class="modal-actions">
      <button class="btn btn-light" onclick="closeModal()">Cancel</button>
      <button class="btn btn-primary" id="us-go" onclick="saveUser()">Create</button>
    </div>`);
}
async function saveUser(){
  const status = document.getElementById('us-status'), btn = document.getElementById('us-go');
  const payload = {
    username: document.getElementById('us-name').value.trim(),
    displayName: document.getElementById('us-disp').value.trim() || null,
    password: document.getElementById('us-pass').value,
  };
  if(!payload.username){ status.className = 'form-status err'; status.textContent = 'Username is required.'; return; }
  if(!payload.password || payload.password.length < 6){ status.className = 'form-status err'; status.textContent = 'Password must be at least 6 characters.'; return; }
  status.className = 'form-status'; status.textContent = 'Creating…'; btn.disabled = true;
  try{ await Platform.createUser(payload); closeModal(); toast('User created', 'ok'); loadUsers(); }
  catch(e){ btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message; }
}
async function doToggleUser(id){
  try{ await Platform.toggleUser(id); toast('User updated', 'ok'); loadUsers(); }
  catch(e){ toast(e.message, 'warn'); }
}
async function doResetPassword(id){
  const pw = prompt('New password (min 6 characters):', '');
  if(pw === null) return;
  if(pw.length < 6){ toast('Password must be at least 6 characters.', 'warn'); return; }
  try{ await Platform.resetUserPassword(id, pw); toast('Password reset', 'ok'); }
  catch(e){ toast(e.message, 'warn'); }
}

// ══════════════ SUPPORT (tickets) ══════════════
let _tickets = [];
const TICKET_STATUS_LABEL = { Open:'Open', InProgress:'In progress', Resolved:'Resolved', Closed:'Closed' };
function ticketStatusClass(s){ return String(s || '').toLowerCase(); }

async function loadSupport(){
  const kp = document.getElementById('supportKpis'), el = document.getElementById('ticketsTable');
  kp.innerHTML = ''; el.innerHTML = '<div class="empty">Loading…</div>';
  const status = document.getElementById('supportStatusFilter')?.value || '';
  try{
    const [sum, list] = await Promise.all([
      Platform.ticketSummary(),
      Platform.tickets(status ? `?status=${encodeURIComponent(status)}` : ''),
    ]);
    kp.innerHTML = `
      ${kpi(sum.open, 'Open', 'amber')}
      ${kpi(sum.inProgress, 'In progress', '')}
      ${kpi(sum.resolved, 'Resolved', 'green')}
      ${kpi(sum.closed, 'Closed', '')}
      ${kpi(sum.total, 'Total', '')}`;
    _tickets = list || [];
    if(!_tickets.length){ el.innerHTML = '<div class="empty">No tickets found.</div>'; return; }
    el.innerHTML = `<table>
      <thead><tr><th>Ticket</th><th>Subject</th><th>Society</th><th>Category</th><th>Priority</th><th>Status</th><th>Updated</th><th></th></tr></thead>
      <tbody>${_tickets.map(ticketRow).join('')}</tbody></table>`;
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
function ticketRow(t){
  const pr = String(t.priority || '').toLowerCase();
  return `<tr>
    <td><code>${esc(t.ticketNumber)}</code></td>
    <td>${esc(t.subject)}</td>
    <td>${t.societyName ? esc(t.societyName) : '<span class="pill neutral">General</span>'}</td>
    <td>${esc(t.category)}</td>
    <td><span class="pill ${pr}">${esc(t.priority)}</span></td>
    <td><span class="badge ${ticketStatusClass(t.status)}">${esc(TICKET_STATUS_LABEL[t.status] || t.status)}</span></td>
    <td>${fmtDate(t.updatedAt)}</td>
    <td><button class="btn btn-light btn-sm" onclick="openTicketDetail(${t.id})">Open</button></td></tr>`;
}
async function openTicketModal(){
  openModal('<h3>New ticket</h3><p class="empty">Loading…</p>');
  try{
    const socs = await Platform.societies();
    const active = (socs || []).filter(s => String(s.status).toLowerCase() !== 'pending');
    const opt = (v, l) => `<option value="${esc(v)}">${esc(l)}</option>`;
    openModal(`
      <h3>New ticket</h3>
      <div class="field"><label>Society</label><select id="tk-soc">
        <option value="">— General / platform —</option>
        ${active.map(s => `<option value="${esc(s.key)}">${esc(s.displayName)} (${esc(s.key)})</option>`).join('')}
      </select></div>
      <div class="field"><label>Subject</label><input id="tk-subj" placeholder="Short summary" autocomplete="off"></div>
      <div class="field"><label>Description</label><textarea id="tk-desc" rows="4" placeholder="What's the issue?"></textarea></div>
      <div class="grid2">
        <div class="field"><label>Category</label><select id="tk-cat">
          ${opt('General','General')}${opt('Billing','Billing')}${opt('Technical','Technical')}${opt('Onboarding','Onboarding')}</select></div>
        <div class="field"><label>Priority</label><select id="tk-pri">
          ${opt('Low','Low')}${opt('Normal','Normal')}${opt('High','High')}${opt('Urgent','Urgent')}</select></div>
      </div>
      <div class="grid2">
        <div class="field"><label>Contact name</label><input id="tk-cname" placeholder="optional" autocomplete="off"></div>
        <div class="field"><label>Contact email</label><input id="tk-cmail" placeholder="optional" autocomplete="off"></div>
      </div>
      <p id="tk-status" class="form-status"></p>
      <div class="modal-actions">
        <button class="btn btn-light" onclick="closeModal()">Cancel</button>
        <button class="btn btn-primary" id="tk-go" onclick="doCreateTicket()">Create</button>
      </div>`);
    document.getElementById('tk-pri').value = 'Normal';
  }catch(e){
    openModal(`<h3>New ticket</h3><p class="form-status err">${esc(e.message)}</p><div class="modal-actions"><button class="btn btn-light" onclick="closeModal()">Close</button></div>`);
  }
}
async function doCreateTicket(){
  const status = document.getElementById('tk-status'), btn = document.getElementById('tk-go');
  const payload = {
    societyKey: document.getElementById('tk-soc').value || null,
    subject: document.getElementById('tk-subj').value.trim(),
    description: document.getElementById('tk-desc').value.trim(),
    category: document.getElementById('tk-cat').value,
    priority: document.getElementById('tk-pri').value,
    contactName: document.getElementById('tk-cname').value.trim() || null,
    contactEmail: document.getElementById('tk-cmail').value.trim() || null,
  };
  if(!payload.subject){ status.className = 'form-status err'; status.textContent = 'Subject is required.'; return; }
  if(!payload.description){ status.className = 'form-status err'; status.textContent = 'Description is required.'; return; }
  status.className = 'form-status'; status.textContent = 'Creating…'; btn.disabled = true;
  try{ await Platform.createTicket(payload); closeModal(); toast('Ticket created', 'ok'); loadSupport(); }
  catch(e){ btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message; }
}
async function openTicketDetail(id){
  openModal('<h3>Ticket</h3><p class="empty">Loading…</p>');
  try{
    const t = await Platform.ticket(id);
    renderTicketDetail(t);
  }catch(e){
    openModal(`<h3>Ticket</h3><p class="form-status err">${esc(e.message)}</p><div class="modal-actions"><button class="btn btn-light" onclick="closeModal()">Close</button></div>`);
  }
}
function renderTicketDetail(t){
  const pr = String(t.priority || '').toLowerCase();
  const thread = (t.messages || []).map(m => `
    <div class="msg"><div class="msg-meta">${esc(m.authorUsername)} · ${fmtDateTime(m.createdAt)}</div>${esc(m.body)}</div>`).join('')
    || '<div class="empty">No replies yet.</div>';
  const statusBtn = (val, label) => t.status === val ? '' :
    `<button class="btn btn-light btn-sm" onclick="doTicketStatus(${t.id},'${val}')">${label}</button>`;
  openModal(`
    <h3>${esc(t.ticketNumber)} <span class="badge ${ticketStatusClass(t.status)}">${esc(TICKET_STATUS_LABEL[t.status] || t.status)}</span></h3>
    <p style="margin:.2rem 0 .6rem"><b>${esc(t.subject)}</b></p>
    <div class="cred" style="margin-bottom:.6rem">
      <div><span>Society</span><code>${t.societyName ? esc(t.societyName) : 'General'}</code></div>
      <div><span>Category</span><code>${esc(t.category)}</code></div>
      <div><span>Priority</span><span class="pill ${pr}">${esc(t.priority)}</span></div>
      ${t.contactEmail ? `<div><span>Contact</span><code>${esc(t.contactName || '')} ${esc(t.contactEmail)}</code></div>` : ''}
    </div>
    <p style="white-space:pre-wrap;margin:0 0 .6rem">${esc(t.description)}</p>
    <div class="thread">${thread}</div>
    <div class="field"><label>Add reply / note</label><textarea id="tk-reply" rows="2" placeholder="Type a message…"></textarea></div>
    <p id="tk-dstatus" class="form-status"></p>
    <div class="modal-actions" style="flex-wrap:wrap;gap:6px">
      ${statusBtn('Open','Reopen')}${statusBtn('InProgress','In progress')}${statusBtn('Resolved','Resolve')}${statusBtn('Closed','Close')}
      <span style="flex:1"></span>
      <button class="btn btn-light" onclick="closeModal()">Close</button>
      <button class="btn btn-primary" id="tk-reply-go" onclick="doTicketReply(${t.id})">Send reply</button>
    </div>`);
}
async function doTicketReply(id){
  const body = document.getElementById('tk-reply').value.trim();
  const status = document.getElementById('tk-dstatus');
  if(!body){ status.className = 'form-status err'; status.textContent = 'Enter a message.'; return; }
  document.getElementById('tk-reply-go').disabled = true;
  try{ const t = await Platform.ticketReply(id, body); renderTicketDetail(t); toast('Reply added', 'ok'); loadSupport(); }
  catch(e){ status.className = 'form-status err'; status.textContent = '❌ ' + e.message; document.getElementById('tk-reply-go').disabled = false; }
}
async function doTicketStatus(id, status){
  try{ const t = await Platform.ticketStatus(id, status, null); renderTicketDetail(t); toast('Status updated', 'ok'); loadSupport(); }
  catch(e){ toast(e.message, 'warn'); }
}

// ══════════════ SETTINGS (platform config) ══════════════
async function loadSettings(){
  const el = document.getElementById('settingsForm');
  el.innerHTML = '<div class="empty">Loading…</div>';
  try{
    const s = await Platform.settings();
    const plans = await Platform.plans().catch(() => []);
    const planOpts = (plans || []).map(p =>
      `<option value="${esc(p.code)}" ${s.defaultPlanCode === p.code ? 'selected' : ''}>${esc(p.name)} (${esc(p.code)})</option>`).join('');
    el.innerHTML = `
      <div class="section-label">Branding</div>
      <div class="grid2">
        <div class="field"><label>Brand name</label><input id="st-brand" value="${esc(s.brandName || '')}"></div>
        <div class="field"><label>Support email</label><input id="st-supmail" value="${esc(s.supportEmail || '')}"></div>
      </div>
      <div class="section-label">Billing</div>
      <div class="grid2">
        <div class="field"><label>Default plan</label><select id="st-plan"><option value="">— none —</option>${planOpts}</select></div>
        <div class="field"><label>Expiry warning (days)</label><input id="st-warn" type="number" min="0" max="365" value="${s.expiryWarningDays}"></div>
      </div>
      <div class="section-label">Email (SMTP)</div>
      <div class="grid2">
        <div class="field"><label>Host</label><input id="st-smtphost" value="${esc(s.smtpHost || '')}"></div>
        <div class="field"><label>Port</label><input id="st-smtpport" type="number" min="1" max="65535" value="${s.smtpPort ?? ''}"></div>
      </div>
      <div class="grid2">
        <div class="field"><label>From email</label><input id="st-smtpfrom" value="${esc(s.smtpFromEmail || '')}"></div>
        <div class="field"><label>Username</label><input id="st-smtpuser" value="${esc(s.smtpUsername || '')}" autocomplete="off"></div>
      </div>
      <div class="grid2">
        <div class="field"><label>Password ${s.smtpPasswordSet ? '<span class="pill green">set</span>' : ''}</label><input id="st-smtppass" type="password" placeholder="${s.smtpPasswordSet ? 'leave blank to keep' : 'not set'}" autocomplete="new-password"></div>
        <div class="field"><label>Use SSL/TLS</label><select id="st-smtpssl"><option value="true" ${s.smtpEnableSsl ? 'selected' : ''}>Yes</option><option value="false" ${!s.smtpEnableSsl ? 'selected' : ''}>No</option></select></div>
      </div>
      <p id="st-status" class="form-status"></p>
      <div class="modal-actions" style="justify-content:flex-end">
        <button class="btn btn-primary" id="st-go" onclick="saveSettings()">Save settings</button>
      </div>`;
  }catch(e){ el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
async function saveSettings(){
  const status = document.getElementById('st-status'), btn = document.getElementById('st-go');
  const portVal = document.getElementById('st-smtpport').value.trim();
  const payload = {
    brandName: document.getElementById('st-brand').value.trim() || null,
    supportEmail: document.getElementById('st-supmail').value.trim() || null,
    defaultPlanCode: document.getElementById('st-plan').value || null,
    expiryWarningDays: parseInt(document.getElementById('st-warn').value, 10),
    smtpHost: document.getElementById('st-smtphost').value.trim() || null,
    smtpPort: portVal ? parseInt(portVal, 10) : null,
    smtpUsername: document.getElementById('st-smtpuser').value.trim() || null,
    smtpFromEmail: document.getElementById('st-smtpfrom').value.trim() || null,
    smtpEnableSsl: document.getElementById('st-smtpssl').value === 'true',
    smtpPassword: document.getElementById('st-smtppass').value || null,
  };
  if(isNaN(payload.expiryWarningDays)) payload.expiryWarningDays = null;
  status.className = 'form-status'; status.textContent = 'Saving…'; btn.disabled = true;
  try{ await Platform.saveSettings(payload); toast('Settings saved', 'ok'); loadSettings(); }
  catch(e){ btn.disabled = false; status.className = 'form-status err'; status.textContent = '❌ ' + e.message; }
}

// ─── boot ───
(function init(){
  if(getToken()){ enterApp(); }
  else{ document.body.classList.add('logged-out'); }
})();
