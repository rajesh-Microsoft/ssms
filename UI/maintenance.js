// ════════════════════════════════════════════════════════════
// SMMS Maintenance Rule Engine — admin UI for the data-driven
// maintenance components (api/admin/maintenance-components).
// Loaded after app.js; relies on its globals (Api, toast, DB,
// closeModal, canEdit). All functions are global by design so the
// lazy-loaded partial's inline onclick handlers can reach them.
// ════════════════════════════════════════════════════════════

let _components = [];

const METHOD_LABELS = {
  FixedAmount: 'Fixed Amount',
  PerSquareFoot: 'Per Square Foot',
  Percentage: 'Percentage',
  PerFlatType: 'Flat Type',
  PerTower: 'Tower / Block',
  PerFloor: 'Floor',
  CustomPerFlat: 'Custom Per Flat'
};

function mEsc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function mMoney(n){ return '₹' + (Number(n)||0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }

// Members are loaded by app.js at login; normalise the casing the API uses.
function mMembers(){
  const list = (typeof DB !== 'undefined' && Array.isArray(DB.members)) ? DB.members : [];
  return list.map(m => ({
    id: m.id ?? m.Id,
    flat: m.flat ?? m.Flat,
    floor: m.floor ?? m.Floor,
    flatType: m.flatType ?? m.FlatType,
    tower: m.tower ?? m.Tower
  }));
}

function mDistinct(field){
  return [...new Set(mMembers().map(m => m[field]).filter(v => v != null && String(v).trim() !== ''))];
}

// Prefill keyed-rate rows from configured settings (towers/floors) first, then any
// distinct values already present on members.
function keysForMethod(method, field){
  const s = (typeof DB !== 'undefined' && DB.settings) ? DB.settings : {};
  let configured = [];
  if(method === 'PerTower') configured = s.towers || [];
  else if(method === 'PerFloor') configured = s.floors || [];
  return [...new Set([...configured.map(String), ...mDistinct(field).map(String)])];
}

// ─── list ───
async function renderMaintenance(){
  const body = document.getElementById('cmp-tbody');
  if(!body) return;
  body.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:16px;">Loading…</td></tr>`;
  try{
    _components = await Api.getComponents();
  }catch(err){
    body.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:16px;">⚠ ${mEsc(err.message)}</td></tr>`;
    return;
  }
  if(_components.length === 0){
    body.innerHTML = `<tr><td colspan="7" style="text-align:center;padding:16px;">No components yet. Click “Add Component”.</td></tr>`;
    return;
  }
  body.innerHTML = _components.map((c, i) => `
    <tr>
      <td>${i + 1}</td>
      <td><strong>${mEsc(c.name)}</strong>${c.description ? `<br><small>${mEsc(c.description)}</small>` : ''}</td>
      <td>${METHOD_LABELS[c.method] || c.method}</td>
      <td>${describeRate(c)}</td>
      <td>${c.applyToAllFlats ? 'All flats' : 'Optional'}</td>
      <td>${c.isActive ? '<span style="color:#16a34a;font-weight:600;">Yes</span>' : '<span style="color:#94a3b8;">No</span>'}</td>
      <td>
        <button class="btn btn-ghost btn-sm" data-perm="Settings" onclick="openComponentModal(${c.id})">✏️</button>
        <button class="btn btn-ghost btn-sm" data-perm="Settings" onclick="removeComponent(${c.id})">🗑️</button>
      </td>
    </tr>`).join('');
  if(typeof applyRolePermissions === 'function') applyRolePermissions();
}

function describeRate(c){
  switch(c.method){
    case 'FixedAmount':   return mMoney(c.amount);
    case 'PerSquareFoot': return mMoney(c.amount) + ' / sq ft';
    case 'Percentage':    return (c.percentageValue ?? 0) + '%';
    case 'CustomPerFlat': return 'default ' + mMoney(c.amount);
    default:              return (c.rates || []).map(r => `${mEsc(r.key)}: ${mMoney(r.amount)}`).join(', ') || '—';
  }
}

// ─── modal ───
function openComponentModal(id){
  const editing = _components.find(c => String(c.id) === String(id));
  document.getElementById('comp-modal-title').textContent = editing ? 'Edit Component' : 'Add Component';
  document.getElementById('modal-comp').dataset.editId = editing ? editing.id : '';

  document.getElementById('comp-name').value    = editing ? editing.name : '';
  document.getElementById('comp-method').value  = editing ? editing.method : 'FixedAmount';
  document.getElementById('comp-amount').value  = editing ? editing.amount : 0;
  document.getElementById('comp-percent').value = editing?.percentageValue ?? 0;
  document.getElementById('comp-sort').value    = editing ? editing.sortOrder : (_components.length + 1) * 10;
  document.getElementById('comp-active').value  = editing ? String(editing.isActive) : 'true';
  document.getElementById('comp-scope').value   = editing ? String(editing.applyToAllFlats) : 'true';

  populatePercentBase(editing);
  document.getElementById('comp-percent-base').value = editing?.percentageBaseComponentId ?? '';

  buildRates(editing);
  buildOverrides(editing);
  onComponentMethodChange();

  document.getElementById('modal-comp').classList.add('open');
}

function populatePercentBase(editing){
  const sel = document.getElementById('comp-percent-base');
  const opts = ['<option value="">All prior components (subtotal)</option>'];
  for(const c of _components){
    if(editing && c.id === editing.id) continue;   // can't base a percentage on itself
    opts.push(`<option value="${c.id}">${mEsc(c.name)}</option>`);
  }
  sel.innerHTML = opts.join('');
}

// Field visibility per selected method.
function onComponentMethodChange(){
  const method = document.getElementById('comp-method').value;
  const show = (id, on) => document.getElementById(id).style.display = on ? '' : 'none';

  const needsAmount = ['FixedAmount', 'PerSquareFoot', 'CustomPerFlat'].includes(method);
  const isPercent   = method === 'Percentage';
  const isKeyed     = ['PerFlatType', 'PerTower', 'PerFloor'].includes(method);

  show('comp-amount-wrap', needsAmount);
  show('comp-percent-wrap', isPercent);
  show('comp-percent-base-wrap', isPercent);
  show('comp-rates-wrap', isKeyed);

  document.getElementById('comp-amount-label').textContent =
    method === 'PerSquareFoot' ? 'Rate per sq ft (₹)' : (method === 'CustomPerFlat' ? 'Default amount (₹)' : 'Amount (₹)');

  if(isKeyed){
    const field = method === 'PerFlatType' ? 'flatType' : (method === 'PerTower' ? 'tower' : 'floor');
    document.getElementById('comp-rates-label').textContent =
      (method === 'PerFlatType' ? 'Flat type' : method === 'PerTower' ? 'Tower' : 'Floor') + ' rates';
    if(document.getElementById('comp-rates').children.length === 0){
      keysForMethod(method, field).forEach(k => addRateRow(k, 0));
      if(document.getElementById('comp-rates').children.length === 0) addRateRow('', 0);
    }
  }

  // Per-flat table needed for optional scope or the custom-per-flat method.
  const optional = document.getElementById('comp-scope').value === 'false';
  document.getElementById('comp-overrides-wrap').style.display = (optional || method === 'CustomPerFlat') ? '' : 'none';
  document.getElementById('comp-overrides-label').textContent =
    method === 'CustomPerFlat' ? 'Per-flat amounts' : 'Applicable flats';
}

// ─── keyed rates editor ───
function buildRates(editing){
  document.getElementById('comp-rates').innerHTML = '';
  (editing?.rates || []).forEach(r => addRateRow(r.key, r.amount));
}
function addRateRow(key = '', amount = 0){
  const wrap = document.getElementById('comp-rates');
  const row = document.createElement('div');
  row.className = 'rate-row';
  row.style.cssText = 'display:flex;gap:6px;margin-bottom:6px;';
  row.innerHTML = `
    <input type="text" class="rate-key" placeholder="Key (e.g. 2BHK)" value="${mEsc(key)}" style="flex:1;">
    <input type="number" step="0.01" min="0" class="rate-amt" placeholder="Amount" value="${amount}" style="width:130px;">
    <button class="btn btn-ghost btn-sm" type="button" onclick="this.parentElement.remove()">✕</button>`;
  wrap.appendChild(row);
}
function collectRates(){
  return [...document.querySelectorAll('#comp-rates .rate-row')]
    .map(r => ({ key: r.querySelector('.rate-key').value.trim(), amount: parseFloat(r.querySelector('.rate-amt').value) || 0 }))
    .filter(r => r.key !== '');
}

// ─── per-flat overrides editor ───
function buildOverrides(editing){
  const tbody = document.getElementById('comp-overrides');
  const existing = {};
  (editing?.flatOverrides || []).forEach(o => existing[o.memberId] = o);
  tbody.innerHTML = mMembers().map(m => {
    const o = existing[m.id];
    return `<tr data-member="${m.id}">
      <td>${mEsc(m.flat)}</td>
      <td style="text-align:center;"><input type="checkbox" class="ovr-app" ${o ? (o.isApplicable ? 'checked' : '') : ''}></td>
      <td><input type="number" step="0.01" min="0" class="ovr-amt" style="width:120px;" value="${o && o.amount != null ? o.amount : ''}"></td>
    </tr>`;
  }).join('');
}
function collectOverrides(){
  const method = document.getElementById('comp-method').value;
  const optional = document.getElementById('comp-scope').value === 'false';
  if(!(optional || method === 'CustomPerFlat')) return [];
  return [...document.querySelectorAll('#comp-overrides tr')].map(tr => {
    const amt = tr.querySelector('.ovr-amt').value;
    return {
      memberId: Number(tr.dataset.member),
      isApplicable: tr.querySelector('.ovr-app').checked,
      amount: amt === '' ? null : parseFloat(amt)
    };
  }).filter(o => o.isApplicable || o.amount != null);
}

// ─── save / delete ───
async function saveComponent(){
  if(typeof canEdit === 'function' && !canEdit('Settings')) return toast('You do not have edit access to Settings.', 'warn');
  const method = document.getElementById('comp-method').value;
  const name = document.getElementById('comp-name').value.trim();
  if(!name) return toast('Component name is required.', 'warn');

  const percent = parseFloat(document.getElementById('comp-percent').value) || 0;
  if(method === 'Percentage' && (percent < 0 || percent > 100)) return toast('Percentage must be 0–100.', 'warn');

  const payload = {
    name,
    description: null,
    method,
    amount: parseFloat(document.getElementById('comp-amount').value) || 0,
    percentageValue: method === 'Percentage' ? percent : null,
    percentageBaseComponentId: method === 'Percentage'
      ? (document.getElementById('comp-percent-base').value ? Number(document.getElementById('comp-percent-base').value) : null)
      : null,
    applyToAllFlats: document.getElementById('comp-scope').value === 'true',
    isActive: document.getElementById('comp-active').value === 'true',
    sortOrder: Number(document.getElementById('comp-sort').value) || 0,
    rates: collectRates(),
    flatOverrides: collectOverrides()
  };

  const editId = document.getElementById('modal-comp').dataset.editId;
  try{
    if(editId){ await Api.updateComponent(editId, payload); }
    else { await Api.createComponent(payload); }
    closeModal('comp');
    await renderMaintenance();
    toast('Component saved!');
  }catch(err){ toast(err.message || 'Save failed', 'warn'); }
}

async function removeComponent(id){
  if(typeof canEdit === 'function' && !canEdit('Settings')) return toast('You do not have edit access to Settings.', 'warn');
  const c = _components.find(x => String(x.id) === String(id));
  if(!confirm(`Delete component “${c ? c.name : id}”?`)) return;
  try{
    await Api.deleteComponent(id);
    await renderMaintenance();
    toast('Component deleted.');
  }catch(err){ toast(err.message || 'Delete failed', 'warn'); }
}

// ─── preview ───
async function previewMaintenance(){
  const card = document.getElementById('preview-card');
  const table = document.getElementById('preview-table');
  card.style.display = '';
  table.querySelector('thead').innerHTML = '';
  table.querySelector('tbody').innerHTML = `<tr><td style="padding:16px;text-align:center;">Calculating…</td></tr>`;
  try{
    const data = await Api.previewInvoices();
    const heads = [...new Set(data.flatMap(f => f.lines.map(l => l.componentName)))];
    table.querySelector('thead').innerHTML =
      `<tr><th>Flat</th>${heads.map(h => `<th>${mEsc(h)}</th>`).join('')}<th>Total</th></tr>`;
    table.querySelector('tbody').innerHTML = data.length === 0
      ? `<tr><td colspan="${heads.length + 2}" style="padding:16px;text-align:center;">No active flats.</td></tr>`
      : data.map(f => {
          const cells = heads.map(h => {
            const line = f.lines.find(l => l.componentName === h);
            return `<td>${line ? mMoney(line.amount) : '—'}</td>`;
          }).join('');
          return `<tr><td>${mEsc(f.flatNumber)}</td>${cells}<td><strong>${mMoney(f.total)}</strong></td></tr>`;
        }).join('');
  }catch(err){
    table.querySelector('tbody').innerHTML = `<tr><td style="padding:16px;text-align:center;">⚠ ${mEsc(err.message)}</td></tr>`;
  }
}

// Rebuild the per-flat table when the scope toggles between all/optional.
document.addEventListener('change', e => {
  if(e.target && e.target.id === 'comp-scope') onComponentMethodChange();
});
