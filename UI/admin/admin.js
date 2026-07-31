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
  audit: (take = 100) => pfetch(`/platform/audit?take=${take}`),
  pending: () => pfetch('/platform/societies/pending'),
  approve: (key, payload) => pfetch(`/platform/societies/${encodeURIComponent(key)}/approve`, { method:'POST', body: JSON.stringify(payload || {}) }),
  reject:  (key) => pfetch(`/platform/societies/${encodeURIComponent(key)}/reject`, { method:'POST' }),
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
  if(view === 'audit')     return loadAudit();
  // subscriptions / payments / users / support / settings are static placeholders (Phase 2/3)
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

// ─── boot ───
(function init(){
  if(getToken()){ enterApp(); }
  else{ document.body.classList.add('logged-out'); }
})();
