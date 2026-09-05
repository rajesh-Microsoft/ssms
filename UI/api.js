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
    // The caretaker app has its own login screen, so it opts out of the resident redirect.
    if(typeof window.SMMS_ON_UNAUTHORIZED === 'function'){
      window.SMMS_ON_UNAUTHORIZED();
    }else if(!/home\.html$/i.test(window.location.pathname)){
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
    const err = new Error(message);
    err.status = res.status;   // lets callers tell "not found yet" apart from a real failure
    throw err;
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

async function apiDownload(path, fileName){
  const headers = {};
  const token = apiGetToken();
  if(token) headers['Authorization'] = 'Bearer ' + token;
  const res = await fetch(API_BASE + path, { headers });
  if(!res.ok) throw new Error(`Download failed (${res.status})`);
  const url = URL.createObjectURL(await res.blob());
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName || 'utility-bill.html';
  link.click();
  URL.revokeObjectURL(url);
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
  signupFlats: () => apiFetch('/auth/flats'),
  securityQuestion: (username) => apiFetch('/auth/security-question?username=' + encodeURIComponent(username)),
  resetPassword: (payload) => apiFetch('/auth/reset-password', { method: 'POST', body: JSON.stringify(payload) }),

  // Members
  getMembers: () => apiFetch('/members'),
  createMember: (payload) => apiFetch('/members', { method: 'POST', body: JSON.stringify(payload) }),
  updateMember: (id, payload) => apiFetch(`/members/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteMember: (id) => apiFetch(`/members/${id}`, { method: 'DELETE' }),
  getAdvanceLedger: (id) => apiFetch(`/members/${id}/advance-ledger`),
  adjustAdvance: (id, payload) => apiFetch(`/members/${id}/advance-adjust`, { method: 'POST', body: JSON.stringify(payload) }),
  refundAdvance: (id, payload) => apiFetch(`/members/${id}/advance-refund`, { method: 'POST', body: JSON.stringify(payload || {}) }),
  setAdvanceMode: (id, mode) => apiFetch(`/members/${id}/advance-mode`, { method: 'PUT', body: JSON.stringify({ mode }) }),
  getAdvanceBalances: () => apiFetch('/members/advance/balances'),
  getAdvanceDeductions: (from, to) => apiFetch('/members/advance/deductions' + (from||to ? `?from=${from||''}&to=${to||''}` : '')),

  // Collections
  getCollections: () => apiFetch('/collections'),
  createCollection: (payload) => apiFetch('/collections', { method: 'POST', body: JSON.stringify(payload) }),
  updateCollection: (id, payload) => apiFetch(`/collections/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteCollection: (id) => apiFetch(`/collections/${id}`, { method: 'DELETE' }),
  recordPayment: (payload) => apiFetch('/collections/record-payment', { method: 'POST', body: JSON.stringify(payload) }),
  generateBilling: (payload) => apiFetch('/admin/billing/generate', { method: 'POST', body: JSON.stringify(payload) }),
  raiseOneTimeCharge: (payload) => apiFetch('/admin/billing/onetime', { method: 'POST', body: JSON.stringify(payload) }),

  // Maintenance rule engine (data-driven components)
  getComponents: () => apiFetch('/admin/maintenance-components'),
  createComponent: (payload) => apiFetch('/admin/maintenance-components', { method: 'POST', body: JSON.stringify(payload) }),
  updateComponent: (id, payload) => apiFetch(`/admin/maintenance-components/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteComponent: (id) => apiFetch(`/admin/maintenance-components/${id}`, { method: 'DELETE' }),
  previewInvoices: () => apiFetch('/admin/maintenance-components/preview'),

  // Expenses
  getExpenses: () => apiFetch('/expenses'),
  createExpense: (payload) => apiFetch('/expenses', { method: 'POST', body: JSON.stringify(payload) }),
  updateExpense: (id, payload) => apiFetch(`/expenses/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteExpense: (id) => apiFetch(`/expenses/${id}`, { method: 'DELETE' }),
  // Bills / receipts / payment screenshots backing an expense. Multipart must go through
  // apiPostForm (apiFetch forces application/json), and the download is [Authorize]d, so it
  // needs the blob helper rather than a bare href, which would send no token and 401.
  getExpenseAttachments: (id) => apiFetch(`/expenses/${id}/attachments`),
  uploadExpenseAttachment: (id, file) => { const fd = new FormData(); fd.append('file', file); return apiPostForm(`/expenses/${id}/attachments`, fd); },
  viewExpenseAttachment: (id, attId) => apiFetchObjectUrl(`/expenses/${id}/attachments/${attId}`),
  deleteExpenseAttachment: (id, attId) => apiFetch(`/expenses/${id}/attachments/${attId}`, { method: 'DELETE' }),

  // Other income (non-member society receipts: ads, shop/tower rent, interest, etc.)
  getIncome: () => apiFetch('/income'),
  createIncome: (payload) => apiFetch('/income', { method: 'POST', body: JSON.stringify(payload) }),
  updateIncome: (id, payload) => apiFetch(`/income/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteIncome: (id) => apiFetch(`/income/${id}`, { method: 'DELETE' }),

  // Budget planner (expected income/expenses for a month, and how they turned out)
  getBudget: (year, month) => apiFetch(`/budgets?year=${year}&month=${month}`),
  createBudget: (payload) => apiFetch('/budgets', { method: 'POST', body: JSON.stringify(payload) }),
  updateBudget: (id, payload) => apiFetch(`/budgets/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteBudget: (id) => apiFetch(`/budgets/${id}`, { method: 'DELETE' }),
  addBudgetItem: (id, payload) => apiFetch(`/budgets/${id}/items`, { method: 'POST', body: JSON.stringify(payload) }),
  updateBudgetItem: (itemId, payload) => apiFetch(`/budgets/items/${itemId}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteBudgetItem: (itemId) => apiFetch(`/budgets/items/${itemId}`, { method: 'DELETE' }),
  convertBudgetItem: (itemId, payload) => apiFetch(`/budgets/items/${itemId}/convert`, { method: 'POST', body: JSON.stringify(payload) }),
  getBudgetSuggestion: (year, month) => apiFetch(`/budgets/suggest?year=${year}&month=${month}`),
  getBudgetVariance: (year, month) => apiFetch(`/budgets/variance?year=${year}&month=${month}`),
  getBudgetHealth: (year, month) => apiFetch(`/budgets/health?year=${year}&month=${month}`),

  // Utility integrations. Connection mutations are Admin-only server-side; bills are read-only for members.
  getUtilityProviders: () => apiFetch('/utilities/providers'),
  getUtilityConnections: () => apiFetch('/utilities/connections'),
  createUtilityConnection: (payload) => apiFetch('/utilities/connections', { method: 'POST', body: JSON.stringify(payload) }),
  updateUtilityConnection: (id, payload) => apiFetch(`/utilities/connections/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteUtilityConnection: (id) => apiFetch(`/utilities/connections/${id}`, { method: 'DELETE' }),
  fetchUtilityBill: (id) => apiFetch(`/utilities/connections/${id}/fetch`, { method: 'POST' }),
  getUtilityBills: (connectionId) => apiFetch('/utilities/bills' + (connectionId ? `?connectionId=${connectionId}` : '')),
  markUtilityBillPaid: (id, payload) => apiFetch(`/utilities/bills/${id}/mark-paid`, { method: 'POST', body: JSON.stringify(payload) }),
  unmarkUtilityBillPaid: (id) => apiFetch(`/utilities/bills/${id}/unmark-paid`, { method: 'POST' }),
  getUtilityNotifications: () => apiFetch('/utilities/notifications'),
  downloadUtilityBill: (id, fileName) => apiDownload(`/utilities/bills/${id}/download`, fileName),

  // Society liabilities (money the society owes contributors)
  getLiabilities: (status) => apiFetch('/society-liabilities' + (status ? `?status=${encodeURIComponent(status)}` : '')),
  getLiabilitySummary: () => apiFetch('/society-liabilities/summary'),
  createLiability: (payload) => apiFetch('/society-liabilities', { method: 'POST', body: JSON.stringify(payload) }),
  settleLiability: (id, payload) => apiFetch(`/society-liabilities/${id}/settle`, { method: 'POST', body: JSON.stringify(payload) }),
  deleteLiability: (id) => apiFetch(`/society-liabilities/${id}`, { method: 'DELETE' }),

  // Reimbursement claims (a member asking for money back)
  getMyReimbursements: () => apiFetch('/reimbursements/mine'),
  getReimbursements: (status) => apiFetch('/reimbursements' + (status ? `?status=${encodeURIComponent(status)}` : '')),
  getReimbursementSummary: () => apiFetch('/reimbursements/summary'),
  getReimbursementCategories: () => apiFetch('/reimbursements/categories'),
  createReimbursement: (payload) => apiFetch('/reimbursements', { method: 'POST', body: JSON.stringify(payload) }),
  updateReimbursement: (id, payload) => apiFetch(`/reimbursements/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  withdrawReimbursement: (id) => apiFetch(`/reimbursements/${id}`, { method: 'DELETE' }),
  approveReimbursement: (id, category) => apiFetch(`/reimbursements/${id}/approve`, { method: 'POST', body: JSON.stringify({ category: category || null }) }),
  settleReimbursement: (id, payload) => apiFetch(`/reimbursements/${id}/settle`, { method: 'POST', body: JSON.stringify(payload) }),
  rejectReimbursement: (id, note) => apiFetch(`/reimbursements/${id}/reject`, { method: 'POST', body: JSON.stringify({ note }) }),
  requestInfoReimbursement: (id, note) => apiFetch(`/reimbursements/${id}/request-info`, { method: 'POST', body: JSON.stringify({ note }) }),
  getReimbAttachments: (id) => apiFetch(`/reimbursements/${id}/attachments`),
  // Multipart must go through apiPostForm: apiFetch forces application/json and the
  // boundary would be lost. The download is [Authorize]d, so it needs the blob helper
  // rather than a bare href, which would send no token and 401.
  uploadReimbAttachment: (id, file) => { const fd = new FormData(); fd.append('file', file); return apiPostForm(`/reimbursements/${id}/attachments`, fd); },
  viewReimbAttachment: (id, attId) => apiFetchObjectUrl(`/reimbursements/${id}/attachments/${attId}`),
  deleteReimbAttachment: (id, attId) => apiFetch(`/reimbursements/${id}/attachments/${attId}`, { method: 'DELETE' }),

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

  // Gate log (the caretaker's routes, which admins share)
  getGateVisitors: (search) => apiFetch('/caretaker/visitors' + (search ? `?search=${encodeURIComponent(search)}` : '')),
  getGateDeliveries: (search) => apiFetch('/caretaker/deliveries' + (search ? `?search=${encodeURIComponent(search)}` : '')),

  // Me (resident self-service — works for any authenticated user)
  getMe: () => apiFetch('/me'),
  getMyGate: () => apiFetch('/me/gate'),
  getMyAdvance: () => apiFetch('/me/advance'),
  updateMe: (payload) => apiFetch('/me', { method: 'PUT', body: JSON.stringify(payload) }),
  changeMyPassword: (currentPassword, newPassword) =>
    apiFetch('/me/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) })
};

// ── Maintenance payments (member self-service) ──────────────────
Api.getPendingInvoices   = () => apiFetch('/member/pending-invoices');
Api.getPaymentOptions    = () => apiFetch('/member/payment-options');
Api.getQrPayload         = (collectionId) => apiFetch(`/member/qrcode/${collectionId}/payload`);
Api.getQrImageUrl        = (collectionId) => apiFetchObjectUrl(`/member/qrcode/${collectionId}`);
Api.uploadPaymentProof   = (formData) => apiPostForm('/member/upload-payment-proof', formData);
Api.createRazorpayOrder  = (collectionId) => apiFetch(`/member/razorpay/order/${collectionId}`, { method: 'POST' });
Api.createPhonePeCheckout = (collectionId) => apiFetch(`/member/phonepe/checkout/${collectionId}`, { method: 'POST' });
Api.confirmPhonePePayment = (merchantOrderId) => apiFetch(`/member/phonepe/confirm/${encodeURIComponent(merchantOrderId)}`, { method: 'POST' });
Api.verifyRazorpayPayment = (payload) => apiFetch('/member/razorpay/verify', { method: 'POST', body: JSON.stringify(payload) });
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

// ── Bank statement reconciliation (admin) ───────────────────────
Api.getReconSummary      = () => apiFetch('/admin/reconciliation/summary');
Api.getBankTxns          = (status) => apiFetch('/admin/reconciliation/transactions' + (status ? '?status=' + encodeURIComponent(status) : ''));
Api.importBankStatement  = (formData) => apiPostForm('/admin/reconciliation/import', formData);
Api.confirmReconMatch    = (bankTransactionId, paymentProofId) => apiFetch('/admin/reconciliation/confirm', { method: 'POST', body: JSON.stringify({ bankTransactionId, paymentProofId }) });
Api.ignoreBankTxn        = (id) => apiFetch(`/admin/reconciliation/transactions/${id}/ignore`, { method: 'POST' });

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
