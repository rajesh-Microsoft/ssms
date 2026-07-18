// ════════════════════════════════════════════════════════════
// SMMS API CLIENT — thin fetch() wrapper around the SQL-backed
// .NET REST API (api/SMMS.Api). Shared by home.html (auth) and
// app.js (dashboard data). The JWT issued at login is stored in
// sessionStorage and sent as a Bearer token on every request.
// ════════════════════════════════════════════════════════════
const API_BASE = 'https://localhost:7253/api';

function apiGetToken(){ return sessionStorage.getItem('smms_token'); }

function apiSetSession(token, id, username, role){
  sessionStorage.setItem('smms_token', token);
  sessionStorage.setItem('smms_session', JSON.stringify({ id, username, role }));
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
  approveUser: (id) => apiFetch(`/users/${id}/approve`, { method: 'POST' }),
  resetUserPassword: (id, newPassword) => apiFetch(`/users/${id}/reset-password`, { method: 'POST', body: JSON.stringify({ newPassword }) }),
  deleteUser: (id) => apiFetch(`/users/${id}`, { method: 'DELETE' }),

  // Audit log
  getAuditLog: () => apiFetch('/auditlog'),

  // Complaints
  getComplaints: (status) => apiFetch('/complaints' + (status ? `?status=${encodeURIComponent(status)}` : '')),
  createComplaint: (payload) => apiFetch('/complaints', { method: 'POST', body: JSON.stringify(payload) }),
  updateComplaint: (id, payload) => apiFetch(`/complaints/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),
  deleteComplaint: (id) => apiFetch(`/complaints/${id}`, { method: 'DELETE' })
};
