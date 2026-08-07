// ════════════════════════════════════════════════════════════
// Caretaker portal. One screen at a time, big buttons, and as
// little typing as possible — this runs on a cheap phone at a
// gate, often one-handed and in sunlight.
// ════════════════════════════════════════════════════════════

const PURPOSES = ['Guest', 'Delivery', 'Courier', 'Maid', 'Electrician', 'Plumber', 'Cab', 'Other'];
const COURIERS = ['Amazon', 'Flipkart', 'Swiggy', 'Zomato', 'Courier', 'Medicine', 'Milk', 'Other'];

let purpose = 'Guest';
let courier = 'Amazon';
let issue = null;
let flats = [];

const $ = id => document.getElementById(id);

// api.js would bounce a 401 to the resident login; keep the caretaker here instead.
window.SMMS_ON_UNAUTHORIZED = () => showLogin('Your session expired. Please sign in again.');

function note(text, kind){
  const box = $('msg');
  if(!text){ box.innerHTML = ''; return; }
  box.innerHTML = `<div class="msg ${kind || 'ok'}">${escapeHtml(text)}</div>`;
  // The save buttons sit at the bottom of a long form, so the caretaker is looking
  // well below the message strip when it appears.
  window.scrollTo({ top: 0, behavior: 'smooth' });
  if(kind !== 'err') setTimeout(() => { box.innerHTML = ''; }, 3000);
}

function escapeHtml(s){
  return String(s == null ? '' : s).replace(/[&<>"']/g, c =>
    ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
}

function time(iso){
  if(!iso) return '';
  const d = new Date(iso);
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// ── Auth ──

function showLogin(message){
  $('appView').classList.add('hidden');
  $('loginView').classList.remove('hidden');
  $('loginMsg').innerHTML = message ? `<div class="msg err">${escapeHtml(message)}</div>` : '';
}

async function login(){
  const username = $('username').value.trim();
  const password = $('password').value;
  if(!username || !password){ return showLogin('Enter your username and password.'); }

  $('loginBtn').disabled = true;
  try{
    const res = await apiFetch('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) });
    if(res.role !== 'Caretaker' && res.role !== 'Admin'){
      apiClearSession();
      return showLogin('This login is not a caretaker account.');
    }
    apiSetSession(res.token, res.userId, res.username, res.role, res.permissions);
    await startApp();
  }catch(err){
    showLogin(err.message);
  }finally{
    $('loginBtn').disabled = false;
  }
}

function logout(){
  apiClearSession();
  $('password').value = '';
  showLogin();
}

// ── Navigation ──

const SCREENS = {
  home:        { el: 'homeScreen',        title: 'Today' },
  visitors:    { el: 'visitorsScreen',    title: 'Visitor entry' },
  deliveries:  { el: 'deliveriesScreen',  title: 'Deliveries' },
  issue:       { el: 'issueScreen',       title: 'Report a problem' },
  checklist:   { el: 'checklistScreen',   title: 'Daily checklist' }
};

async function go(name){
  Object.values(SCREENS).forEach(s => $(s.el).classList.add('hidden'));
  $(SCREENS[name].el).classList.remove('hidden');
  $('screenTitle').textContent = SCREENS[name].title;
  note('');
  window.scrollTo(0, 0);

  if(name === 'home') await loadSummary();
  if(name === 'visitors') await loadVisitors();
  if(name === 'deliveries') await loadDeliveries();
  if(name === 'issue') await loadIssues();
  if(name === 'checklist') await loadChecklist();
}

// ── Dashboard ──

async function loadSummary(){
  try{
    const s = await apiFetch('/caretaker/summary');
    $('cVisitors').textContent = s.visitorsToday;
    $('cInside').textContent = s.visitorsInside;
    $('cParcels').textContent = s.deliveriesWaiting;
    $('cIssues').textContent = s.openComplaints;
    $('whoAmI').textContent = s.caretakerName;

    const parcels = $('parcelBadge');
    parcels.textContent = s.deliveriesWaiting;
    parcels.classList.toggle('hidden', s.deliveriesWaiting === 0);

    $('checkBadge').classList.toggle('hidden', s.checklistDoneToday);
  }catch(err){ note(err.message, 'err'); }
}

async function loadFlats(){
  try{
    flats = await apiFetch('/caretaker/flats') || [];
    const options = flats.map(f => `<option value="${escapeHtml(f)}"></option>`).join('');
    $('flatList').innerHTML = options;
    $('flatList2').innerHTML = options;
  }catch(err){ /* the field still accepts free text */ }
}

// ── Visitors ──

async function loadVisitors(){
  try{
    const rows = await apiFetch('/caretaker/visitors') || [];
    $('visitorList').innerHTML = rows.length ? rows.map(v => `
      <div class="card">
        <div class="row">
          <div class="grow">
            <h3>${escapeHtml(v.name)} <span class="pill ${v.outAt ? 'out' : 'in'}">${v.outAt ? 'Left' : 'Inside'}</span></h3>
            <div class="sub">Flat ${escapeHtml(v.flat)} · ${escapeHtml(v.purpose)} · in ${time(v.inAt)}${v.outAt ? ' · out ' + time(v.outAt) : ''}</div>
            ${v.vehicleNumber ? `<div class="sub">${escapeHtml(v.vehicleNumber)}</div>` : ''}
          </div>
          ${v.outAt ? '' : `<button class="btn small" data-exit="${v.id}">Mark out</button>`}
        </div>
      </div>`).join('') : '<div class="empty">No visitors yet today.</div>';
  }catch(err){ note(err.message, 'err'); }
}

async function saveVisitor(){
  const name = $('vName').value.trim();
  const flat = $('vFlat').value.trim();
  if(!name) return note('Enter the visitor name.', 'err');
  if(!flat) return note('Enter the flat they are going to.', 'err');

  $('vSave').disabled = true;
  try{
    await apiFetch('/caretaker/visitors', {
      method: 'POST',
      body: JSON.stringify({
        name, flat, purpose,
        mobile: $('vMobile').value.trim() || null,
        vehicleNumber: $('vVehicle').value.trim() || null,
        notes: null
      })
    });
    ['vName', 'vFlat', 'vMobile', 'vVehicle'].forEach(id => { $(id).value = ''; });
    note('Visitor saved.');
    await loadVisitors();
  }catch(err){ note(err.message, 'err'); }
  finally{ $('vSave').disabled = false; }
}

async function markOut(id){
  try{
    await apiFetch(`/caretaker/visitors/${id}/exit`, { method: 'POST' });
    note('Marked out.');
    await loadVisitors();
  }catch(err){ note(err.message, 'err'); }
}

// ── Deliveries ──

async function loadDeliveries(){
  try{
    const rows = await apiFetch('/caretaker/deliveries') || [];
    $('deliveryList').innerHTML = rows.length ? rows.map(d => `
      <div class="card">
        <div class="row">
          <div class="grow">
            <h3>${escapeHtml(d.courier)} <span class="pill ${d.status === 'Collected' ? 'out' : 'in'}">${escapeHtml(d.status)}</span></h3>
            <div class="sub">Flat ${escapeHtml(d.flat)} · ${time(d.receivedAt)}${d.collectedBy ? ' · taken by ' + escapeHtml(d.collectedBy) : ''}</div>
          </div>
          ${d.status === 'Collected' ? '' : `<button class="btn small" data-collect="${d.id}">Collected</button>`}
        </div>
      </div>`).join('') : '<div class="empty">No parcels right now.</div>';
  }catch(err){ note(err.message, 'err'); }
}

async function saveDelivery(){
  const flat = $('dFlat').value.trim();
  if(!flat) return note('Enter the flat this parcel is for.', 'err');

  $('dSave').disabled = true;
  try{
    await apiFetch('/caretaker/deliveries', {
      method: 'POST',
      body: JSON.stringify({ courier, flat, notes: null })
    });
    $('dFlat').value = '';
    note('Parcel saved.');
    await loadDeliveries();
  }catch(err){ note(err.message, 'err'); }
  finally{ $('dSave').disabled = false; }
}

async function markCollected(id){
  try{
    await apiFetch(`/caretaker/deliveries/${id}/collected`, {
      method: 'POST',
      body: JSON.stringify({ collectedBy: null })
    });
    note('Handed over.');
    await loadDeliveries();
  }catch(err){ note(err.message, 'err'); }
}

// ── Issues ──

async function loadIssueTypes(){
  try{
    const types = await apiFetch('/caretaker/issue-types') || [];
    $('issueChips').innerHTML = types.map(t =>
      `<button type="button" data-issue="${escapeHtml(t)}">${escapeHtml(t)}</button>`).join('');
  }catch(err){ note(err.message, 'err'); }
}

async function loadIssues(){
  try{
    const rows = await apiFetch('/caretaker/complaints') || [];
    $('issueList').innerHTML = rows.length ? rows.map(c => `
      <div class="card">
        <h3>${escapeHtml(c.subject)} ${c.hasPhoto ? '📷' : ''}</h3>
        <div class="sub">${escapeHtml(c.status)} · ${escapeHtml(c.priority)} priority · ${new Date(c.createdAt).toLocaleDateString()}</div>
      </div>`).join('') : '<div class="empty">Nothing reported yet.</div>';
  }catch(err){ note(err.message, 'err'); }
}

async function saveIssue(){
  if(!issue) return note('Choose what the problem is.', 'err');

  const form = new FormData();
  form.append('issue', issue);
  const where = $('iWhere').value.trim();
  const desc = $('iNote').value.trim();
  if(where) form.append('location', where);
  if(desc) form.append('description', desc);
  const file = $('iPhoto').files[0];
  if(file) form.append('photo', file);

  $('iSave').disabled = true;
  try{
    await apiPostForm('/caretaker/complaints', form);
    issue = null;
    document.querySelectorAll('#issueChips button').forEach(b => b.classList.remove('on'));
    $('iWhere').value = '';
    $('iNote').value = '';
    $('iPhoto').value = '';
    note('Sent to the committee.');
    await loadIssues();
  }catch(err){ note(err.message, 'err'); }
  finally{ $('iSave').disabled = false; }
}

// ── Checklist ──

async function loadChecklist(){
  try{
    const c = await apiFetch('/caretaker/checklist');
    $('checklistState').textContent = c.submitted
      ? `Submitted at ${time(c.submittedAt)}${c.submittedBy ? ' by ' + c.submittedBy : ''}. You can correct it below.`
      : 'Walk the premises and tick what you checked.';
    $('cNotes').value = c.notes || '';
    $('checkItems').innerHTML = c.items.map((i, n) => `
      <label class="check ${i.done ? 'done' : ''}">
        <input type="checkbox" data-check="${n}" ${i.done ? 'checked' : ''}/>
        <span>${escapeHtml(i.label)}</span>
      </label>`).join('');
  }catch(err){ note(err.message, 'err'); }
}

async function saveChecklist(){
  const items = [...document.querySelectorAll('#checkItems .check')].map(row => ({
    label: row.querySelector('span').textContent,
    done: row.querySelector('input').checked,
    remark: null
  }));
  if(!items.some(i => i.done)) return note('Tick at least one item.', 'err');

  $('cSave').disabled = true;
  try{
    await apiFetch('/caretaker/checklist', {
      method: 'POST',
      body: JSON.stringify({ notes: $('cNotes').value.trim() || null, items })
    });
    note('Round submitted.');
    await loadChecklist();
  }catch(err){ note(err.message, 'err'); }
  finally{ $('cSave').disabled = false; }
}

// ── Wiring ──

function chip(containerId, values, onPick, initial){
  $(containerId).innerHTML = values.map(v =>
    `<button type="button" data-val="${escapeHtml(v)}" class="${v === initial ? 'on' : ''}">${escapeHtml(v)}</button>`).join('');
  $(containerId).addEventListener('click', e => {
    const btn = e.target.closest('button[data-val]');
    if(!btn) return;
    $(containerId).querySelectorAll('button').forEach(b => b.classList.remove('on'));
    btn.classList.add('on');
    onPick(btn.dataset.val);
  });
}

async function startApp(){
  $('loginView').classList.add('hidden');
  $('appView').classList.remove('hidden');
  await loadFlats();
  await loadIssueTypes();
  await go('home');
}

document.addEventListener('DOMContentLoaded', () => {
  chip('purposeChips', PURPOSES, v => { purpose = v; }, purpose);
  chip('courierChips', COURIERS, v => { courier = v; }, courier);

  $('loginBtn').addEventListener('click', login);
  $('password').addEventListener('keydown', e => { if(e.key === 'Enter') login(); });
  $('logoutBtn').addEventListener('click', logout);
  $('vSave').addEventListener('click', saveVisitor);
  $('dSave').addEventListener('click', saveDelivery);
  $('iSave').addEventListener('click', saveIssue);
  $('cSave').addEventListener('click', saveChecklist);

  document.addEventListener('click', e => {
    const nav = e.target.closest('[data-go]');
    if(nav) return go(nav.dataset.go);

    const exit = e.target.closest('[data-exit]');
    if(exit) return markOut(exit.dataset.exit);

    const collect = e.target.closest('[data-collect]');
    if(collect) return markCollected(collect.dataset.collect);

    const pick = e.target.closest('#issueChips button[data-issue]');
    if(pick){
      document.querySelectorAll('#issueChips button').forEach(b => b.classList.remove('on'));
      pick.classList.add('on');
      issue = pick.dataset.issue;
    }
  });

  // Ticking an item recolours the row immediately, so a glance confirms the round.
  document.addEventListener('change', e => {
    const box = e.target.closest('#checkItems input[type=checkbox]');
    if(box) box.closest('.check').classList.toggle('done', box.checked);
  });

  const session = apiGetSession();
  if(session && apiGetToken()) startApp().catch(() => showLogin());
  else showLogin();
});
