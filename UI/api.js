// ════════════════════════════════════════════════════════════
// SMMS API CLIENT — thin fetch() wrapper around the SQL-backed
// .NET REST API (api/SMMS.Api). Shared by home.html (auth) and
// app.js (dashboard data). The JWT issued at login is stored in
// sessionStorage and sent as a Bearer token on every request.
// ════════════════════════════════════════════════════════════
// const API_BASE = 'https://localhost:7253/api';
const API_BASE = '/api';

function apiGetToken(){ return sessionStorage.getItem('smms_token'); }

function apiSetSession(token, id, username, role, permissions){
  sessionStorage.setItem('smms_token', token);
  sessionStorage.setItem('smms_session', JSON.stringify({ id, username, role, permissions: permissions || {} }));
}

function apiClearSession(){
  sessionStorage.removeItem('smms_token');
  sessionStorage.removeItem('smms_session');
}

function apiGetSession(){
  try{ return JSON.parse(sessionStorage.getItem('smms_session') || 'null'); }catch(e){ return null; }
}

async function apiFetch(path, options = {}){
  const headers = Object.assign({ 'Content-Type': 'application/json' }, options.headers || {});
  const token = apiGetToken();
  if(token) headers['Authorization'] = 'Bearer ' + token;

  let res;
  try{
    res = await fetch(API_BASE + path, { ...options, headers });
  }catch(networkErr){
    throw new Error('Could not reach the SMMS server. Please check your connection and try again.');
  }

  if(res.status === 401){
    apiClearSession();
    if(!/home\.html$/i.test(window.location.pathname)){
      window.location.href = 'home.html';
    }
    throw new Error('Your session has expired. Please log in again.');
  }

  if(!res.ok){
    let message = `Request failed (${res.status})`;
    try{
      const body = await res.json();
      if(body && body.message) message = body.message;
      else if(body && body.title) message = body.title;
    }catch(e){ /* no JSON body */ }
    throw new Error(message);
  }

  if(res.status === 204) return null;
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

// Authenticated binary fetch → object URL. Used for QR codes and payment
// screenshots, which sit behind [Authorize] so a plain <img src> (no Bearer
// header) would 401. Caller is responsible for URL.revokeObjectURL().
async function apiFetchObjectUrl(path){
  const headers = {};
  const token = apiGetToken();
  if(token) headers['Authorization'] = 'Bearer ' + token;
  const res = await fetch(API_BASE + path, { headers });
  if(!res.ok) throw new Error(`Could not load image (${res.status})`);
  const blob = await res.blob();
  return URL.createObjectURL(blob);
}

// Authenticated multipart POST (FormData). Do NOT set Content-Type — the
// browser adds the multipart boundary automatically.
async function apiPostForm(path, formData){
  const headers = {};
  const token = apiGetToken();
  if(token) headers['Authorization'] = 'Bearer ' + token;
  const res = await fetch(API_BASE + path, { method: 'POST', headers, body: formData });
  if(!res.ok){
    let message = `Upload failed (${res.status})`;
    try{ const b = await res.json(); if(b && b.message) message = b.message; }catch(e){}
    throw new Error(message);
  }
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

const Api = {
  // Auth
  login: (username, password) => apiFetch('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
  signup: (payload) => apiFetch('/auth/signup', { method: 'POST', body: JSON.stringify(payload) }),
  securityQuestion: (username) => apiFetch('/auth/security-question?username=' + encodeURIComponent(username)),
  resetPassword: (payload) => apiFetch('/auth/reset-password', { method: 'POST', body: JSON.stringify(payload) }),

  // Members
  getMembers: () => apiFetch('/members'),
  createMember: (payload) => apiFetch('/members', { method: 'POST', body: JSON.stringify(payload) }),
  updateMember: (id, payload) => apiFetch(`/members/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteMember: (id) => apiFetch(`/members/${id}`, { method: 'DELETE' }),

  // Collections
  getCollections: () => apiFetch('/collections'),
  createCollection: (payload) => apiFetch('/collections', { method: 'POST', body: JSON.stringify(payload) }),
  updateCollection: (id, payload) => apiFetch(`/collections/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCollection: (id) => apiFetch(`/collections/${id}`, { method: 'DELETE' }),

  // Expenses
  getExpenses: () => apiFetch('/expenses'),
  createExpense: (payload) => apiFetch('/expenses', { method: 'POST', body: JSON.stringify(payload) }),
  updateExpense: (id, payload) => apiFetch(`/expenses/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteExpense: (id) => apiFetch(`/expenses/${id}`, { method: 'DELETE' }),

  // Settings
  getSettings: () => apiFetch('/settings'),
  updateSettings: (payload) => apiFetch('/settings', { method: 'PUT', body: JSON.stringify(payload) }),

  // Users (Admin panel)
  getUsers: () => apiFetch('/users'),
  createUser: (payload) => apiFetch('/users', { method: 'POST', body: JSON.stringify(payload) }),
  updateUser: (id, payload) => apiFetch(`/users/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  approveUser: (id) => apiFetch(`/users/${id}/approve`, { method: 'POST' }),
  resetUserPassword: (id, newPassword) => apiFetch(`/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  deleteUser: (id) => apiFetch(`/users/${id}`, { method: 'DELETE' }),

  // Audit log
  getAuditLog: () => apiFetch('/auditlog'),

  // Complaints
  getComplaints: (status) => apiFetch('/complaints' + (status ? `?status=${encodeURIComponent(status)}` : '')),
  createComplaint: (payload) => apiFetch('/complaints', { method: 'POST', body: JSON.stringify(payload) }),
  updateComplaint: (id, payload) => apiFetch(`/complaints/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteComplaint: (id) => apiFetch(`/complaints/${id}`, { method: 'DELETE' }),

  // Me (resident self-service — works for any authenticated user)
  getMe: () => apiFetch('/me'),
  updateMe: (payload) => apiFetch('/me', { method: 'PUT', body: JSON.stringify(payload) }),
  changeMyPassword: (currentPassword, newPassword) =>
    apiFetch('/me/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })
};

// ── Maintenance payments (member self-service) ──────────────────
Api.getPendingInvoices   = () => apiFetch('/member/pending-invoices');
Api.getQrPayload         = (collectionId) => apiFetch(`/member/qrcode/${collectionId}/payload`);
Api.getQrImageUrl        = (collectionId) => apiFetchObjectUrl(`/member/qrcode/${collectionId}`);
Api.uploadPaymentProof   = (formData) => apiPostForm('/member/upload-payment-proof', formData);
Api.getMyPaymentHistory  = () => apiFetch('/member/payment-history');
Api.getMyProofScreenshot = (proofId) => apiFetchObjectUrl(`/member/payment-proof/${proofId}/screenshot`);

// ── Maintenance payments (admin verification) ───────────────────
Api.getPaymentDashboard  = () => apiFetch('/admin/payment-dashboard');
Api.getPaymentProofs     = (filters = {}) => {
  const q = new URLSearchParams();
  if(filters.status) q.set('status', filters.status);
  if(filters.month)  q.set('month', filters.month);
  if(filters.year)   q.set('year', filters.year);
  if(filters.flat)   q.set('flat', filters.flat);
  const s = q.toString();
  return apiFetch('/admin/payment-proofs' + (s ? '?' + s : ''));
};
Api.approvePaymentProof  = (id) => apiFetch(`/admin/payment-proofs/${id}/approve`, { method: 'POST' });
Api.rejectPaymentProof   = (id, remarks) => apiFetch(`/admin/payment-proofs/${id}/reject`, { method: 'POST', body: JSON.stringify({ remarks }) });
Api.getAdminProofScreenshot = (id) => apiFetchObjectUrl(`/admin/payment-proofs/${id}/screenshot`);
Api.getUpiSettings       = () => apiFetch('/admin/upi-settings');
Api.saveUpiSettings      = (payload) => apiFetch('/admin/upi-settings', { method: 'PUT', body: JSON.stringify(payload) });

/* ---- Tenant logo: shows /logo/<subdomain>.png, falls back to emoji+text ---- */
function smmsTenantKey(){
  const h = (location.hostname || '').split('.')[0].toLowerCase();
  if(!h || h === 'www' || h === 'localhost' || /^\d+$/.test(h)) return '';
  return h;
}
function applyTenantLogo(){
  const key = smmsTenantKey();
  if(!key) return;                       // no subdomain -> keep placeholder
  const src = 'logo/' + key + '.png';
  [['sidebarLogo','logoFallback'], ['lpLogo','lpLogoFallback']].forEach(function(pair){
    const img = document.getElementById(pair[0]);
    const fb  = document.getElementById(pair[1]);
    if(!img) return;                     // element only exists on the relevant page
    img.onload  = function(){ img.style.display = 'block'; if(fb) fb.style.display = 'none'; };
    img.onerror = function(){ /* no file for this tenant -> keep fallback */ };
    img.src = src;
  });
}
document.addEventListener('DOMContentLoaded', applyTenantLogo);
