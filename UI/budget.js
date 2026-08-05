// ═══════════════════════════════════════════════
// BUDGET PLANNER
// Plans a month's bills before they arrive, then books the real bill against the plan.
// ═══════════════════════════════════════════════

let budState = { budget: null, variance: null, suggestions: [], editItemId: null, convertItemId: null };

function budMoney(n){
  const v = Number(n || 0);
  // Sign goes outside the symbol: -₹520, not ₹-520.
  return (v < 0 ? '-₹' : '₹') + Math.abs(v).toLocaleString('en-IN', { maximumFractionDigits: 2 });
}
function budEsc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({ '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c])); }
function budPeriod(){ return { month: topMonth() || (new Date().getMonth() + 1), year: topYear() || new Date().getFullYear() }; }
function budOpen(id){ document.getElementById('modal-' + id).classList.add('open'); }

async function renderBudget(){
  if(!document.getElementById('bud-items-tbody')) return;   // partial not loaded yet

  // A budget is always for one month, so "All Months" is meaningless here. Pin the shared filter
  // to the current month rather than quietly showing one month while the dropdown claims another.
  const moSel = document.getElementById('topMonth');
  if(moSel && !+moSel.value){
    const cur = new Date().getMonth() + 1;
    moSel.value = cur;
    filterBucketFor('budget').month = cur;
  }

  const { month, year } = budPeriod();

  try{
    budState.budget = await Api.getBudget(year, month);
  }catch(err){
    if(err.status === 404){ budState.budget = null; }
    else { return toast(err.message || 'Failed to load budget', 'warn'); }
  }

  const empty = document.getElementById('bud-empty');
  const main  = document.getElementById('bud-main');

  await renderBudgetHealth(year, month);

  if(!budState.budget){
    document.getElementById('bud-empty-title').textContent = `No budget for ${MONTHS[month]} ${year} yet`;
    empty.style.display = '';
    main.style.display = 'none';
    return;
  }

  empty.style.display = 'none';
  main.style.display = '';

  const b = budState.budget;
  document.getElementById('bud-period').textContent = `· ${MONTHS[b.month]} ${b.year}`;
  document.getElementById('bud-opening').textContent    = budMoney(b.openingBalance);
  document.getElementById('bud-collection').textContent = budMoney(b.expectedCollection);
  document.getElementById('bud-expense').textContent    = budMoney(b.expectedExpense);
  document.getElementById('bud-closing').textContent    = budMoney(b.expectedClosingBalance);
  document.getElementById('bud-deficit').textContent    = budMoney(b.deficit);

  const statusEl = document.getElementById('bud-status');
  statusEl.textContent = b.status;
  statusEl.className = 'badge ' + (b.status === 'Active' ? 'b-active' : b.status === 'Closed' ? 'b-inactive' : 'b-partial');

  const booked = b.items.filter(i => i.status === 'Actual').length;
  document.getElementById('bud-expense-sub').textContent = `${b.items.length} planned · ${booked} booked`;
  document.getElementById('bud-collection-sub').textContent = 'expected receipts';

  const closingEl = document.getElementById('bud-closing');
  closingEl.style.color = b.expectedClosingBalance < 0 ? 'var(--danger)' : '';
  document.getElementById('bud-closing-sub').textContent = b.expectedClosingBalance < 0 ? 'projected shortfall' : 'projected cash left';

  const deficitEl = document.getElementById('bud-deficit');
  deficitEl.style.color = b.deficit > 0 ? 'var(--danger)' : 'var(--success)';
  document.getElementById('bud-deficit-sub').textContent = b.deficit > 0 ? 'costs exceed collection' : 'collection covers costs';

  // The alert fires on the month standing alone, since a healthy opening balance can mask a
  // society that is quietly spending more than it collects every month.
  const alert = document.getElementById('bud-alert');
  if(b.deficit > 0){
    alert.style.display = '';
    document.getElementById('bud-alert-text').innerHTML =
      `Expected collection <b>${budMoney(b.expectedCollection)}</b> is short of expected expenses <b>${budMoney(b.expectedExpense)}</b> by <b>${budMoney(b.deficit)}</b>.` +
      (b.expectedClosingBalance >= 0
        ? ' Reserves cover it this month, but the gap will repeat unless collections rise or costs fall.'
        : ' Reserves will not cover it — raise maintenance or defer non-urgent spending.');
  } else {
    alert.style.display = 'none';
  }

  renderBudgetItems();
  await renderBudgetVariance();
}

const BUD_BANDS = {
  excellent: { icon: '🟢', label: 'Excellent',  bg: '#f0fff4', bd: '#9ae6b4', fg: '#22543d' },
  healthy:   { icon: '🟢', label: 'Healthy',    bg: '#f0fff4', bd: '#9ae6b4', fg: '#22543d' },
  warning:   { icon: '🟠', label: 'Warning',    bg: '#fffaf0', bd: '#fbd38d', fg: '#7b341e' },
  critical:  { icon: '🔴', label: 'Critical',   bg: '#fff5f5', bd: '#feb2b2', fg: '#742a2a' },
  unknown:   { icon: '⚪', label: 'Unknown', bg: 'var(--bg)', bd: 'var(--bd)', fg: 'var(--sub)' }
};

async function renderBudgetHealth(year, month){
  const card = document.getElementById('bud-health');
  if(!card) return;

  let h;
  try{
    h = await Api.getBudgetHealth(year, month);
  }catch(err){
    card.style.display = 'none';
    return;
  }
  card.style.display = '';

  document.getElementById('bud-bank').textContent = budMoney(h.bankBalance);
  document.getElementById('bud-recv').textContent = budMoney(h.receivables);
  document.getElementById('bud-pay').textContent  = budMoney(h.payables);

  const net = document.getElementById('bud-net');
  net.textContent = budMoney(h.netCashPosition);
  net.style.color = h.netCashPosition < 0 ? 'var(--danger)' : '';

  // Say where the balance came from, because a derived one is only as complete as the ledger.
  document.getElementById('bud-bank-sub').textContent = h.balanceAnchored
    ? 'entered opening + this month'
    : 'derived from the ledger';
  document.getElementById('bud-health-note').textContent = h.balanceAnchored
    ? ''
    : 'No plan for this month, so the balance is derived from recorded entries only.';
  document.getElementById('bud-pay-sub').textContent = h.payables > 0 ? 'planned, not yet booked' : 'nothing outstanding';

  const acc = document.getElementById('bud-acc');
  acc.textContent = h.accuracy === null ? '—' : `${h.accuracy}%`;
  document.getElementById('bud-acc-sub').textContent = h.accuracy === null
    ? 'needs a finished month'
    : `estimates vs actuals over ${h.accuracyMonths} month${h.accuracyMonths === 1 ? '' : 's'}`;

  const band = BUD_BANDS[h.band] || BUD_BANDS.unknown;
  const banner = document.getElementById('bud-health-band');
  banner.style.background  = band.bg;
  banner.style.borderColor = band.bd;
  document.getElementById('bud-health-icon').textContent = band.icon;
  const title = document.getElementById('bud-health-title');
  title.textContent = band.label;
  title.style.color = band.fg;

  const msg = document.getElementById('bud-health-msg');
  msg.style.color = band.fg;
  const burn = budMoney(Math.round(h.avgMonthlyExpense));
  if(!h.balanceAnchored && h.bankBalance <= 0){
    msg.textContent = 'Cash position unknown. The ledger totals less than zero, which normally means the '
      + 'opening corpus was never recorded. Create a budget and enter the real bank balance to get a score.';
  } else if(h.monthsOfCover === null){
    msg.textContent = 'No spending recorded yet, so there is nothing to measure cover against.';
  } else if(h.bankBalance <= 0){
    msg.textContent = `No cash cover — recorded spending of ${burn} a month has nothing behind it.`;
  } else if(h.monthsOfCover < 2){
    // Under two months, days are the number a committee can actually act on.
    msg.textContent = `Cash available for only ${Math.round(h.monthsOfCover * 30)} days at ${burn} a month.`;
  } else {
    msg.textContent = `Cash available for ${h.monthsOfCover} months of expenses, at ${burn} a month.`;
  }
}

function renderBudgetItems(){
  const rows = budState.budget.items;
  const tbody = document.getElementById('bud-items-tbody');
  if(!rows.length){
    tbody.innerHTML = '<tr><td colspan="8" class="empty">No expected expenses yet. Add them, or pull suggestions from history.</td></tr>';
    return;
  }
  tbody.innerHTML = rows.map(i => {
    const isActual = i.status === 'Actual';
    const variance = i.variance;
    const vTxt = variance === null || variance === undefined ? '—'
      : `<span style="color:${variance > 0 ? 'var(--danger)' : variance < 0 ? 'var(--success)' : 'inherit'};font-weight:700;">${variance > 0 ? '+' : ''}${budMoney(variance)}</span>`;
    const due = i.dueDate ? new Date(i.dueDate).toLocaleDateString('en-IN', { day: '2-digit', month: 'short' }) : '—';
    // Gated here rather than via data-perm: applyRolePermissions() only runs on partial load, not per row.
    const actions = isActual
      ? '<span style="font-size:11px;color:var(--sub);">booked</span>'
      : (canEdit('Budgets')
        ? `<button class="ic-btn" title="Bill arrived" onclick="openBudgetConvert(${i.id})">✅</button>
         <button class="ic-btn" title="Edit" onclick="openBudgetItem(${i.id})">✏</button>
         <button class="ic-btn" title="Remove" onclick="removeBudgetItem(${i.id})">🗑</button>`
        : '');
    return `<tr>
      <td>${budEsc(i.category)}</td>
      <td>${budEsc(i.description) || '—'}</td>
      <td>${due}</td>
      <td>${budMoney(i.estimatedAmount)}</td>
      <td>${i.actualAmount === null || i.actualAmount === undefined ? '—' : budMoney(i.actualAmount)}</td>
      <td>${vTxt}</td>
      <td><span class="badge ${isActual ? 'b-paid' : 'b-pending'}">${i.status}</span></td>
      <td>${actions}</td>
    </tr>`;
  }).join('');
  if(typeof applyRolePermissions === 'function') applyRolePermissions();
}

async function renderBudgetVariance(){
  const { month, year } = budPeriod();
  const tbody = document.getElementById('bud-variance-tbody');
  try{
    budState.variance = await Api.getBudgetVariance(year, month);
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="5" class="empty">Could not load comparison: ${budEsc(err.message)}</td></tr>`;
    return;
  }
  const v = budState.variance;
  if(!v.rows.length){
    tbody.innerHTML = '<tr><td colspan="5" class="empty">Nothing budgeted or spent in this month yet.</td></tr>';
    return;
  }
  tbody.innerHTML = v.rows.map(r => {
    const over = r.difference > 0;
    const diff = `<span style="color:${over ? 'var(--danger)' : r.difference < 0 ? 'var(--success)' : 'inherit'};font-weight:700;">${over ? '+' : ''}${budMoney(r.difference)}</span>`;
    // Nothing spent against a planned category is "not yet spent", not an underspend — the bill is
    // most likely still to come.
    const verdict = r.actual === 0 && r.budgeted > 0 ? 'not yet spent'
      : over ? 'over budget' : r.difference < 0 ? 'under budget' : 'on budget';
    return `<tr>
      <td>${budEsc(r.category)}${r.unbudgeted ? ' <span class="badge b-unpaid" style="margin-left:4px;">unbudgeted</span>' : ''}</td>
      <td>${budMoney(r.budgeted)}</td>
      <td>${budMoney(r.actual)}</td>
      <td>${diff}</td>
      <td style="font-size:11px;color:var(--sub);">${verdict}</td>
    </tr>`;
  }).join('') + `<tr style="font-weight:800;background:var(--bg);">
      <td>Total</td><td>${budMoney(v.totalBudgeted)}</td><td>${budMoney(v.totalActual)}</td>
      <td style="color:${v.totalDifference > 0 ? 'var(--danger)' : 'var(--success)'};">${v.totalDifference > 0 ? '+' : ''}${budMoney(v.totalDifference)}</td><td></td>
    </tr>`;
}

// ── Plan setup ──────────────────────────────────

async function openBudgetSetup(isEdit){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const { month, year } = budPeriod();
  document.getElementById('budset-title').textContent = isEdit ? `Edit Budget · ${MONTHS[month]} ${year}` : `Create Budget · ${MONTHS[month]} ${year}`;
  const hint = document.getElementById('budset-hint');

  if(isEdit && budState.budget){
    document.getElementById('budset-opening').value    = budState.budget.openingBalance;
    document.getElementById('budset-collection').value = budState.budget.expectedCollection;
    document.getElementById('budset-status').value     = budState.budget.status;
    document.getElementById('budset-notes').value      = budState.budget.notes || '';
    hint.textContent = '';
  } else {
    document.getElementById('budset-status').value = 'Draft';
    document.getElementById('budset-notes').value  = '';
    hint.textContent = 'Loading suggestions…';
    try{
      const s = await Api.getBudgetSuggestion(year, month);
      document.getElementById('budset-opening').value    = s.suggestedOpeningBalance;
      document.getElementById('budset-collection').value = s.suggestedExpectedCollection;
      hint.innerHTML = s.suggestedOpeningBalance < 0
        ? '<b style="color:var(--danger);">The derived opening balance is negative.</b> That usually means the society\'s starting corpus was never entered as income, not that you are overdrawn — recorded spending simply exceeds recorded receipts. Replace it with your actual bank balance.'
        : 'Opening balance is derived from everything recorded so far, and expected collection from this month\'s invoices. Adjust both if your bank says otherwise.';
    }catch(err){
      hint.textContent = 'Could not load suggestions — enter the figures manually.';
    }
  }
  budOpen('budset');
}

async function saveBudgetSetup(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const { month, year } = budPeriod();
  const payload = {
    month, year,
    openingBalance: +document.getElementById('budset-opening').value || 0,
    expectedCollection: +document.getElementById('budset-collection').value || 0,
    status: document.getElementById('budset-status').value,
    notes: document.getElementById('budset-notes').value.trim() || null
  };
  try{
    if(budState.budget) await Api.updateBudget(budState.budget.id, payload);
    else await Api.createBudget(payload);
    closeModal('budset');
    await renderBudget();
    toast('Budget saved!');
  }catch(err){ toast(err.message || 'Save failed', 'warn'); }
}

async function deleteBudget(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  if(!budState.budget) return;
  if(!confirm(`Delete the budget for ${MONTHS[budState.budget.month]} ${budState.budget.year}? Booked expenses stay in the ledger.`)) return;
  try{
    await Api.deleteBudget(budState.budget.id);
    await renderBudget();
    toast('Budget deleted');
  }catch(err){ toast(err.message || 'Delete failed', 'warn'); }
}

// ── Expected expense lines ──────────────────────

function openBudgetItem(id){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  budState.editItemId = id || null;
  const cats = (DB.settings && DB.settings.categories) || [];
  document.getElementById('budit-cat').innerHTML = cats.map(c => `<option>${budEsc(c)}</option>`).join('');
  document.getElementById('budit-title').textContent = id ? 'Edit Expected Expense' : 'Add Expected Expense';

  if(id){
    const i = budState.budget.items.find(x => x.id === id);
    if(!i) return;
    document.getElementById('budit-cat').value    = i.category;
    document.getElementById('budit-amount').value = i.estimatedAmount;
    document.getElementById('budit-due').value    = i.dueDate ? i.dueDate.substring(0, 10) : '';
    document.getElementById('budit-desc').value   = i.description || '';
  } else {
    document.getElementById('budit-amount').value = '';
    document.getElementById('budit-desc').value   = '';
    // Default the due date to month end, the commonest case for salaries and AMCs.
    const { month, year } = budPeriod();
    document.getElementById('budit-due').value = new Date(Date.UTC(year, month, 0)).toISOString().substring(0, 10);
  }
  budOpen('budit');
}

async function saveBudgetItem(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const amount = +document.getElementById('budit-amount').value;
  const category = document.getElementById('budit-cat').value;
  if(!category) return toast('Pick a category', 'warn');
  if(!amount || amount <= 0) return toast('Enter an estimated amount', 'warn');

  const due = document.getElementById('budit-due').value;
  const payload = {
    category,
    description: document.getElementById('budit-desc').value.trim() || null,
    estimatedAmount: amount,
    dueDate: due || null
  };
  try{
    if(budState.editItemId) await Api.updateBudgetItem(budState.editItemId, payload);
    else await Api.addBudgetItem(budState.budget.id, payload);
    closeModal('budit');
    await renderBudget();
    toast('Saved!');
  }catch(err){ toast(err.message || 'Save failed', 'warn'); }
}

async function removeBudgetItem(id){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  if(!confirm('Remove this expected expense from the plan?')) return;
  try{
    await Api.deleteBudgetItem(id);
    await renderBudget();
    toast('Removed');
  }catch(err){ toast(err.message || 'Delete failed', 'warn'); }
}

// ── Booking the real bill ───────────────────────

function openBudgetConvert(id){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const i = budState.budget.items.find(x => x.id === id);
  if(!i) return;
  budState.convertItemId = id;
  document.getElementById('budconv-summary').innerHTML =
    `<b>${budEsc(i.category)}</b>${i.description ? ' · ' + budEsc(i.description) : ''}<br>Estimated <b>${budMoney(i.estimatedAmount)}</b>`;
  document.getElementById('budconv-amount').value = i.estimatedAmount;
  document.getElementById('budconv-date').value = new Date().toISOString().substring(0, 10);
  document.getElementById('budconv-vendor').value = '';
  document.getElementById('budconv-mode').value = '';
  document.getElementById('budconv-remarks').value = '';
  updateBudgetConvertVariance();
  document.getElementById('budconv-amount').oninput = updateBudgetConvertVariance;
  budOpen('budconv');
}

function updateBudgetConvertVariance(){
  const i = budState.budget.items.find(x => x.id === budState.convertItemId);
  if(!i) return;
  const actual = +document.getElementById('budconv-amount').value || 0;
  const v = actual - i.estimatedAmount;
  const el = document.getElementById('budconv-variance');
  if(!actual){ el.textContent = ''; return; }
  el.innerHTML = v === 0
    ? '<span style="color:var(--sub);">Exactly on budget</span>'
    : `<span style="color:${v > 0 ? 'var(--danger)' : 'var(--success)'};">Variance ${v > 0 ? '+' : ''}${budMoney(v)} — ${v > 0 ? 'over' : 'under'} budget</span>`;
}

async function saveBudgetConvert(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const amount = +document.getElementById('budconv-amount').value;
  if(!amount || amount <= 0) return toast('Enter the actual amount', 'warn');
  const payload = {
    actualAmount: amount,
    expenseDate: document.getElementById('budconv-date').value || null,
    vendor: document.getElementById('budconv-vendor').value.trim() || null,
    paymentMode: document.getElementById('budconv-mode').value || null,
    remarks: document.getElementById('budconv-remarks').value.trim() || null
  };
  try{
    await Api.convertBudgetItem(budState.convertItemId, payload);
    closeModal('budconv');
    // The ledger changed, so refresh the cached expenses the rest of the app renders from.
    if(typeof loadExpenses === 'function'){ try{ await loadExpenses(); }catch(e){ /* non-fatal */ } }
    await renderBudget();
    toast('Expense booked!');
  }catch(err){ toast(err.message || 'Could not book the expense', 'warn'); }
}

// ── Suggestions ─────────────────────────────────

async function openBudgetSuggest(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const { month, year } = budPeriod();
  const tbody = document.getElementById('budsug-tbody');
  tbody.innerHTML = '<tr><td colspan="5" class="empty">Loading…</td></tr>';
  budOpen('budsug');
  try{
    const s = await Api.getBudgetSuggestion(year, month);
    budState.suggestions = s.categories || [];
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="5" class="empty">${budEsc(err.message)}</td></tr>`;
    return;
  }
  if(!budState.suggestions.length){
    tbody.innerHTML = '<tr><td colspan="5" class="empty">No expense history yet to forecast from.</td></tr>';
    return;
  }
  // Categories already in the plan are shown but unticked, so nothing gets duplicated by accident.
  // A single month of history is just as likely to be a one-off (a repair, an AMC) as a recurring
  // bill, so those are listed but left unticked rather than silently planned for again.
  const planned = new Set(budState.budget.items.map(i => i.category));
  tbody.innerHTML = budState.suggestions.map((c, idx) => {
    const oneOff = c.monthsOfHistory < 2;
    const tick = !planned.has(c.category) && !oneOff;
    const note = planned.has(c.category)
      ? ' <span class="badge b-inactive">already planned</span>'
      : oneOff ? ' <span class="badge b-partial">seen once — may be a one-off</span>' : '';
    return `<tr>
      <td><input type="checkbox" class="budsug-chk" data-idx="${idx}" ${tick ? 'checked' : ''} style="width:auto;"></td>
      <td>${budEsc(c.category)}${note}</td>
      <td>${budMoney(c.lastAmount)}</td>
      <td style="font-size:11px;color:var(--sub);">${c.monthsOfHistory} month${c.monthsOfHistory === 1 ? '' : 's'}</td>
      <td><input type="number" step="0.01" class="budsug-amt" data-idx="${idx}" value="${c.suggestedAmount}" style="width:110px;"></td>
    </tr>`;
  }).join('');
}

async function applyBudgetSuggestions(){
  if(!canEdit('Budgets')) return toast('Permission denied', 'warn');
  const picked = Array.from(document.querySelectorAll('.budsug-chk')).filter(c => c.checked);
  if(!picked.length) return toast('Nothing selected', 'warn');

  const { month, year } = budPeriod();
  const monthEnd = new Date(Date.UTC(year, month, 0)).toISOString().substring(0, 10);
  let added = 0, failed = 0;
  for(const chk of picked){
    const idx = +chk.dataset.idx;
    const amtEl = document.querySelector(`.budsug-amt[data-idx="${idx}"]`);
    const amount = +amtEl.value;
    if(!amount || amount <= 0){ failed++; continue; }
    try{
      await Api.addBudgetItem(budState.budget.id, {
        category: budState.suggestions[idx].category,
        description: null,
        estimatedAmount: amount,
        dueDate: monthEnd
      });
      added++;
    }catch(err){ failed++; }
  }
  closeModal('budsug');
  await renderBudget();
  toast(failed ? `Added ${added}, ${failed} failed` : `Added ${added} expected expense${added === 1 ? '' : 's'}`, failed ? 'warn' : 'success');
}
