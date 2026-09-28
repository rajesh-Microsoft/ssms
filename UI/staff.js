// ═══════════════════════════════════════════════
// STAFF SALARY (watchman)
// Monthly salary − unpaid leave − advances = what to pay. The server does the arithmetic and
// keeps paid months frozen; this screen shows it and records attendance and payments.
// ═══════════════════════════════════════════════

const STAFF_PAY_TYPES = ['Salary Advance', 'Mobile Recharge', 'Emergency Advance', 'Other'];
// An advance is salary paid early. "Other" may be a gift or a society cost, so it starts unticked.
const STAFF_PAY_DEDUCT_DEFAULT = { 'Salary Advance': true, 'Emergency Advance': true, 'Mobile Recharge': true, 'Other': false };
const STAFF_NEXT_MARK = { Present: 'Leave', Leave: 'Unpaid', Unpaid: 'Present' };

let stf = { list: [], staffId: null, year: 0, month: 0, att: null, payments: [], salary: null, detail: null, editPaymentId: null, busy: false };

function stfMoney(n){
  const v = Number(n || 0);
  return (v < 0 ? '-₹' : '₹') + Math.abs(v).toLocaleString('en-IN', { maximumFractionDigits: 2 });
}
function stfPeriod(y, m){ return `${MONTHS[m]} ${y}`; }
function stfPad(n){ return String(n).padStart(2, '0'); }
function stfIso(d){ return `${d.getFullYear()}-${stfPad(d.getMonth() + 1)}-${stfPad(d.getDate())}`; }
// API dates are calendar dates; parsing them with new Date() would shift them across the UTC line.
function stfDate(iso){
  if(!iso) return '—';
  const [y, m, d] = String(iso).slice(0, 10).split('-');
  return `${d}-${MONTHS[+m].slice(0, 3)}-${y}`;
}
function stfDays(n){ return `${n} day${n === 1 ? '' : 's'}`; }
function stfCurrent(){ return stf.list.find(s => s.id === stf.staffId) || null; }
function stfOrdinal(n){ const s = ['th','st','nd','rd'], v = n % 100; return n + (s[(v - 20) % 10] || s[v] || s[0]); }
function stfPayDayLabel(pd){ return pd ? `${stfOrdinal(pd)} of the next month` : 'last day of the month'; }
function stfOpen(id){ document.getElementById('modal-' + id).classList.add('open'); }

// ── Load ──

async function renderStaff(){
  if(!document.getElementById('stf-main')) return;   // partial not loaded yet
  try{ stf.list = await Api.getStaff() || []; }
  catch(err){ return toast(err.message || 'Failed to load staff', 'warn'); }

  const empty = document.getElementById('stf-empty');
  const main  = document.getElementById('stf-main');
  if(!stf.list.length){ empty.style.display = ''; main.style.display = 'none'; return; }
  empty.style.display = 'none';
  main.style.display = '';

  if(!stfCurrent()){
    stf.staffId = (stf.list.find(s => s.isActive) || stf.list[0]).id;
    stf.year = 0;
  }
  if(!stf.year) await stfDefaultPeriod();

  renderStaffPicker();
  stfFillHistoryYears();
  await Promise.all([loadStaffMonth(), loadStaffHistory()]);
}

/** Opens on the month that needs attention: last month while it is unpaid, otherwise this one. */
async function stfDefaultPeriod(){
  const now = new Date();
  stf.year = now.getFullYear();
  stf.month = now.getMonth() + 1;
  try{
    const row = (await Api.getStaffSummary() || []).find(r => r.staffId === stf.staffId);
    if(row){ stf.year = row.year; stf.month = row.month; }
  }catch(e){ /* the current month is a fine fallback */ }
}

function renderStaffPicker(){
  const sel = document.getElementById('stf-picker');
  document.getElementById('stf-picker-wrap').style.display = stf.list.length > 1 ? '' : 'none';
  sel.innerHTML = stf.list.map(s =>
    `<option value="${s.id}">${escHtml(s.name)} — ${escHtml(s.role)}${s.isActive ? '' : ' (left)'}</option>`).join('');
  sel.value = stf.staffId;
}

function staffPick(id){
  stf.staffId = +id;
  stf.year = 0;
  renderStaff();
}

function staffShiftMonth(delta){
  const d = new Date(stf.year, stf.month - 1 + delta, 1);
  stf.year = d.getFullYear();
  stf.month = d.getMonth() + 1;
  loadStaffMonth();
}

async function loadStaffMonth(){
  const s = stfCurrent();
  if(!s) return;
  document.getElementById('stf-period').textContent = stfPeriod(stf.year, stf.month);
  try{
    const [att, pays, sal] = await Promise.all([
      Api.getStaffAttendance(s.id, stf.year, stf.month),
      Api.getStaffPayments(s.id, stf.year, stf.month),
      Api.getStaffSalary(s.id, stf.year, stf.month)
    ]);
    stf.att = att;
    stf.payments = pays || [];
    stf.salary = sal;
  }catch(err){ return toast(err.message || 'Failed to load salary', 'warn'); }

  renderStaffHeader();
  renderStaffSalary();
  renderStaffAttendance();
  renderStaffPayments();
}

// ── Header ──

function renderStaffHeader(){
  const s = stfCurrent(), b = stf.salary;
  document.getElementById('stf-title').textContent = `🛡️ ${s.name} — ${s.role}` + (s.isActive ? '' : ' (left)');
  const bits = [
    `${stfMoney(s.monthlySalary)} a month`,
    `${stfDays(s.paidLeavePerMonth)} paid leave a month`,
    `paid on the ${stfPayDayLabel(s.payDay)}`,
    `joined ${stfDate(s.joiningDate)}`
  ];
  if(s.leavingDate) bits.push(`left ${stfDate(s.leavingDate)}`);
  if(s.mobile) bits.push(`📞 ${escHtml(s.mobile)}`);
  document.getElementById('stf-profile').innerHTML = bits.join(' · ');

  const paid = b.status === 'Paid';
  const tile = (label, value, color, sub) =>
    `<div class="card" style="flex:1;min-width:140px;padding:10px 14px;margin:0;">
       <div style="font-size:12px;color:var(--sub);">${label}</div>
       <div style="font-size:22px;font-weight:700;${color ? `color:${color};` : ''}">${value}</div>
       ${sub ? `<div style="font-size:11px;color:var(--sub);">${sub}</div>` : ''}
     </div>`;
  document.getElementById('stf-tiles').innerHTML =
    tile('Salary for the month', stfMoney(b.grossSalary), '',
      b.employedDays < b.daysInMonth ? `${b.employedDays} of ${b.daysInMonth} days employed` : '') +
    tile('Leave deduction', b.leaveDeduction ? '−' + stfMoney(b.leaveDeduction) : '₹0', b.leaveDeduction ? '#c53030' : '',
      stfDays(b.unpaidLeaveDays) + ' unpaid') +
    tile('Already paid during month', b.adjustmentsApplied ? '−' + stfMoney(b.adjustmentsApplied) : '₹0', b.adjustmentsApplied ? '#c53030' : '',
      'advances & recharges') +
    tile(paid ? 'Net salary paid' : 'Net salary to pay', stfMoney(b.netPayable), paid ? '#276749' : '#2b6cb0',
      paid ? `Paid ${stfDate(b.paidOn)}` : b.status === 'Draft' ? 'Not paid yet' : '');
}

// ── Salary card + breakdown ──

function stfStatusBadge(b){
  if(b.status === 'Paid') return '<span class="badge b-paid">PAID</span>';
  if(b.status === 'Reversed') return '<span class="badge b-inactive">REVERSED</span>';
  return b.canPay ? '<span class="badge b-partial">NOT PAID</span>' : '<span class="badge b-inactive">DRAFT</span>';
}

function renderStaffSalary(){
  const b = stf.salary;
  document.getElementById('stf-status').innerHTML = stfStatusBadge(b);
  document.getElementById('stf-breakdown').innerHTML = stfSlip(b, false);

  const admin = isAdmin();
  const payBtn = document.getElementById('stf-pay-btn');
  payBtn.style.display = admin && b.status === 'Draft' ? '' : 'none';
  payBtn.disabled = !b.canPay;
  document.getElementById('stf-rev-btn').style.display = admin && b.status === 'Paid' ? '' : 'none';

  const blocked = document.getElementById('stf-blocked');
  const why = b.status === 'Draft' && !b.canPay ? b.blockedReason : '';
  blocked.textContent = why ? '⚠ ' + why : '';
  blocked.style.display = why ? '' : 'none';
}

/** The pay slip. <detailed> adds the working behind each figure and the payment record. */
function stfSlip(b, detailed){
  const row = (label, value, cls, title) =>
    `<tr${cls ? ` class="${cls}"` : ''}${title ? ` title="${escHtml(title)}"` : ''}><td>${label}</td><td>${value}</td></tr>`;
  const sec = label => `<tr class="sal-sec"><td colspan="2">${label}</td></tr>`;
  const minus = n => n ? '−' + stfMoney(n) : '₹0';
  const leaveBeyond = b.leaveDays - b.paidLeaveDays;
  const absent = b.unpaidLeaveDays - leaveBeyond;   // days marked Unpaid, deducted whatever the allowance
  const out = [];

  out.push(row('Monthly salary', stfMoney(b.monthlySalary)));
  if(b.employedDays < b.daysInMonth){
    out.push(row(`Salary for ${stfDays(b.employedDays)} employed`, stfMoney(b.grossSalary), '',
      `${stfMoney(b.monthlySalary)} × ${b.employedDays} ÷ ${b.daysInMonth} days`));
    if(detailed) out.push(row(`${stfMoney(b.monthlySalary)} × ${b.employedDays} ÷ ${b.daysInMonth} days in ${MONTHS[b.month]}`, '', 'sal-muted'));
  }

  out.push(sec('Attendance'));
  out.push(row('Present', stfDays(b.presentDays)));
  out.push(row('Allowed paid leave', stfDays(b.paidLeaveAllowed)));
  out.push(row('Leave taken', stfDays(b.leaveDays)));
  if(absent > 0) out.push(row('Unpaid absence', stfDays(absent), '', 'Days marked Unpaid are always deducted'));
  out.push(row('<b>Unpaid days</b>', `<b>${stfDays(b.unpaidLeaveDays)}</b>`, '',
    leaveBeyond > 0 ? `${b.leaveDays} leave − ${b.paidLeaveAllowed} allowed = ${leaveBeyond} over the limit` : ''));
  if(detailed && b.unpaidLeaveDays){
    out.push(row(`Daily rate: ${stfMoney(b.monthlySalary)} ÷ ${b.daysInMonth} days = ${stfMoney(b.dailyRate)}`, '', 'sal-muted'));
    out.push(row(`${stfMoney(b.monthlySalary)} × ${b.unpaidLeaveDays} ÷ ${b.daysInMonth}, rounded to the paisa`, '', 'sal-muted'));
  }
  out.push(row('Leave deduction', minus(b.leaveDeduction), b.leaveDeduction ? 'sal-minus' : ''));

  out.push(sec('Salary adjustments'));
  const lines = b.adjustments || [];
  if(!lines.length) out.push(row('No advances deducted this month', '', 'sal-muted'));
  lines.forEach(l => {
    out.push(row(`${stfDate(l.date).slice(0, 6)} ${escHtml(l.category)}`, minus(l.amount), 'sal-minus'));
    if(detailed && l.notes) out.push(row(escHtml(l.notes), '', 'sal-muted'));
  });
  if(lines.length) out.push(row('<b>Total adjustments</b>', `<b>${minus(b.adjustmentsApplied)}</b>`, 'sal-minus'));
  if(b.carryForward > 0)
    out.push(row(`${stfMoney(b.carryForward)} is more than this month's salary and will be deducted next month`, '', 'sal-muted'));

  const netLabel = b.status === 'Paid' ? 'NET SALARY PAID' : b.status === 'Reversed' ? 'NET PAID (REVERSED)' : 'NET SALARY PAYABLE';
  out.push(`<tr class="sal-net"><td>${netLabel}</td><td>${stfMoney(b.netPayable)}</td></tr>`);

  if(detailed && b.status !== 'Draft'){
    out.push(sec('Payment'));
    out.push(row('Paid on', stfDate(b.paidOn)));
    if(b.paymentMode) out.push(row('Mode', escHtml(b.paymentMode)));
    if(b.paidBy) out.push(row('Recorded by', escHtml(b.paidBy)));
    out.push(row('Expense entry', b.expenseId ? `#${b.expenseId}` : 'None — advances covered the salary'));
    if(b.notes) out.push(row('Notes', escHtml(b.notes)));
    if(b.status === 'Reversed'){
      out.push(sec('Reversed'));
      out.push(row('Reversed on', stfDate(b.reversedOn)));
      if(b.reversedBy) out.push(row('By', escHtml(b.reversedBy)));
      out.push(row('Reason', escHtml(b.reversalReason || '')));
    }
  }
  return `<table class="sal-slip"><tbody>${out.join('')}</tbody></table>`;
}

// ── Attendance calendar ──

function renderStaffAttendance(){
  const a = stf.att;
  const marks = Object.fromEntries((a.days || []).map(d => [String(d.date).slice(0, 10), d.status]));
  const editable = canEdit('Expenses') && !a.locked;
  const today = stfIso(new Date());
  const from = a.employedFrom ? String(a.employedFrom).slice(0, 10) : null;
  const to = a.employedTo ? String(a.employedTo).slice(0, 10) : null;

  const cells = ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(d => `<div class="att-wd">${d}</div>`);
  const offset = (new Date(a.year, a.month - 1, 1).getDay() + 6) % 7;
  for(let i = 0; i < offset; i++) cells.push('<div></div>');

  for(let day = 1; day <= a.daysInMonth; day++){
    const iso = `${a.year}-${stfPad(a.month)}-${stfPad(day)}`;
    const employed = from && iso >= from && iso <= to;
    const future = iso > today;
    const mark = marks[iso] || 'Present';
    const cls = !employed || future ? 'att-off' : mark === 'Leave' ? 'att-leave' : mark === 'Unpaid' ? 'att-unpaid' : '';
    const tip = !employed ? 'Not employed' : future ? 'Future date' : mark + (editable ? ` — tap for ${STAFF_NEXT_MARK[mark]}` : '');
    const canTap = editable && employed && !future;
    cells.push(`<button type="button" class="att-day ${cls}${iso === today ? ' att-today' : ''}" title="${tip}" aria-label="${stfDate(iso)}: ${tip}"
      ${canTap ? `onclick="cycleStaffDay('${iso}')"` : 'disabled'}>${day}${employed && !future && mark !== 'Present' ? `<small>${mark}</small>` : ''}</button>`);
  }
  document.getElementById('stf-cal').innerHTML = cells.join('');

  document.getElementById('stf-att-counts').textContent =
    `Present ${a.presentDays} · Paid leave ${a.paidLeaveDays} · Unpaid ${a.unpaidLeaveDays}`;
  const s = stfCurrent();
  document.getElementById('stf-att-hint').textContent = a.locked
    ? '🔒 This month\'s salary is paid. Reverse it first to correct attendance.'
    : editable
      ? `Tap a day to change it: Present → Leave → Unpaid. Up to ${stfDays(s.paidLeavePerMonth)} of Leave a month is paid; Unpaid is always deducted.`
      : 'Read-only.';
}

async function cycleStaffDay(iso){
  if(stf.busy) return;
  const current = ((stf.att.days || []).find(d => String(d.date).slice(0, 10) === iso) || {}).status || 'Present';
  stf.busy = true;
  try{
    await Api.setStaffAttendance(stf.staffId, iso, STAFF_NEXT_MARK[current]);
    await loadStaffMonth();
  }catch(err){ toast(err.message || 'Could not update attendance', 'warn'); }
  finally{ stf.busy = false; }
}

// ── Payments during the month ──

function renderStaffPayments(){
  const tbody = document.getElementById('stf-pay-tbody');
  if(!stf.payments.length){
    tbody.innerHTML = '<tr><td colspan="7" class="empty">No payments this month.</td></tr>';
    return;
  }
  const editable = canEdit('Expenses');
  tbody.innerHTML = stf.payments.map(p => {
    const effect = !p.adjustAgainstSalary
      ? '<span style="color:var(--sub);">Not deducted</span>'
      : p.settlementId
        ? `<span class="badge b-paid">Deducted · ${escHtml(p.settledPeriod || '')}</span>`
        : '<span class="badge b-partial">Will be deducted</span>';
    const actions = !editable ? ''
      : p.settlementId
        ? '<span title="Part of a paid salary. Reverse that salary to change it.">🔒</span>'
        : p.reimbursementId
          ? `<span title="From reimbursement claim #${p.reimbursementId}. To undo it, delete the claim's liability on the Liabilities screen.">🔗</span>`
          : `<div class="act-btns">
             <button class="ic-btn" title="Edit" onclick="openStaffPayment(${p.id})">✏️</button>
             <button class="ic-btn" title="Delete" onclick="deleteStaffPayment(${p.id})">🗑️</button>
           </div>`;
    const claim = p.reimbursementId
      ? `<div style="font-size:11px;color:var(--sub);">Claim #${p.reimbursementId} · booked on the claim, not again here</div>` : '';
    return `<tr>
      <td>${stfDate(p.date)}</td>
      <td>${escHtml(p.category)}${claim}</td>
      <td><b>${stfMoney(p.amount)}</b></td>
      <td>${escHtml(p.paymentMode || '')}</td>
      <td>${effect}</td>
      <td>${escHtml(p.notes || '')}</td>
      <td>${actions}</td>
    </tr>`;
  }).join('');
}

function openStaffPayment(id){
  if(!canEdit('Expenses')) return toast('You do not have edit access to Expenses.', 'warn');
  const p = id ? stf.payments.find(x => x.id === id) : null;
  stf.editPaymentId = p ? p.id : null;

  // Default to today when viewing this month, otherwise the last day of the month on screen.
  const now = new Date();
  const viewingNow = stf.year === now.getFullYear() && stf.month === now.getMonth() + 1;
  const dflt = viewingNow ? stfIso(now) : `${stf.year}-${stfPad(stf.month)}-${stfPad(new Date(stf.year, stf.month, 0).getDate())}`;

  document.getElementById('stfpay-title').textContent = p ? 'Edit Payment' : `Payment to ${stfCurrent().name}`;
  document.getElementById('stfpay-type').innerHTML = STAFF_PAY_TYPES.map(t => `<option>${t}</option>`).join('');
  document.getElementById('stfpay-type').value = p ? p.category : 'Salary Advance';
  document.getElementById('stfpay-date').value = p ? String(p.date).slice(0, 10) : dflt;
  document.getElementById('stfpay-amount').value = p ? p.amount : '';
  document.getElementById('stfpay-mode').value = p ? (p.paymentMode || 'Cash') : 'Cash';
  document.getElementById('stfpay-notes').value = p ? (p.notes || '') : '';
  document.getElementById('stfpay-deduct').checked = p ? p.adjustAgainstSalary : true;
  stfPayHint();
  stfOpen('stfpay');
}

function stfPayTypeChanged(){
  if(!stf.editPaymentId)
    document.getElementById('stfpay-deduct').checked = !!STAFF_PAY_DEDUCT_DEFAULT[document.getElementById('stfpay-type').value];
  stfPayHint();
}

/** Says in plain words what the checkbox will do to the salary. */
function stfPayHint(){
  const on = document.getElementById('stfpay-deduct').checked;
  const amt = Number(document.getElementById('stfpay-amount').value) || 0;
  const name = (stfCurrent() || {}).name || 'the staff member';
  const el = document.getElementById('stfpay-hint');
  el.innerHTML = on
    ? `✔ <b>${amt ? stfMoney(amt) : 'This amount'}</b> will be <b>deducted</b> from ${escHtml(name)}'s next salary.`
    : `Recorded as a society expense only. ${escHtml(name)}'s salary is <b>not</b> reduced.`;
  el.style.color = on ? '#2b6cb0' : 'var(--sub)';
}

async function saveStaffPayment(){
  const payload = {
    date: document.getElementById('stfpay-date').value,
    amount: Number(document.getElementById('stfpay-amount').value),
    category: document.getElementById('stfpay-type').value,
    notes: document.getElementById('stfpay-notes').value.trim() || null,
    paymentMode: document.getElementById('stfpay-mode').value,
    adjustAgainstSalary: document.getElementById('stfpay-deduct').checked
  };
  if(!payload.date) return toast('Pick the date the money was handed over', 'warn');
  if(!(payload.amount > 0)) return toast('Amount must be greater than zero', 'warn');

  const btn = document.getElementById('stfpay-save');
  btn.disabled = true;
  try{
    if(stf.editPaymentId) await Api.updateStaffPayment(stf.editPaymentId, payload);
    else await Api.createStaffPayment(stf.staffId, payload);
    closeModal('stfpay');
    toast(stf.editPaymentId ? 'Payment updated' : 'Payment recorded and added to expenses');
    await loadStaffMonth();
  }catch(err){ toast(err.message || 'Could not save the payment', 'warn'); }
  finally{ btn.disabled = false; }
}

async function deleteStaffPayment(id){
  const p = stf.payments.find(x => x.id === id);
  if(!p) return;
  const ok = await smmsConfirm(`Delete the ${p.category.toLowerCase()} of ${stfMoney(p.amount)} on ${stfDate(p.date)}? Its expense entry is removed too.`,
    { title: 'Delete payment?' });
  if(!ok) return;
  try{
    await Api.deleteStaffPayment(id);
    toast('Payment deleted');
    await loadStaffMonth();
  }catch(err){ toast(err.message || 'Could not delete the payment', 'warn'); }
}

// ── View details / Pay ──

async function openSalaryDetails(mode, settlementId){
  let b = stf.salary;
  if(settlementId){
    try{ b = await Api.getSalarySettlement(settlementId); }
    catch(err){ return toast(err.message || 'Could not load that salary', 'warn'); }
  }
  stf.detail = b;
  const paying = mode === 'pay' && b.status === 'Draft' && b.canPay && isAdmin();

  document.getElementById('salary-title').textContent = `${stfPeriod(b.year, b.month)} Salary — ${b.staffName} (${b.role})`;
  document.getElementById('salary-body').innerHTML = stfSlip(b, true);
  document.getElementById('salary-pay-form').style.display = paying ? '' : 'none';
  document.getElementById('salary-pay-btn').style.display = paying ? '' : 'none';

  if(paying){
    document.getElementById('salary-paidon').value = stfIso(new Date());
    document.getElementById('salary-mode').value = 'Cash';
    document.getElementById('salary-notes').value = '';
    document.getElementById('salary-pay-btn').textContent = b.netPayable > 0 ? `💵 Pay ${stfMoney(b.netPayable)}` : '✔ Mark as settled';
    document.getElementById('salary-pay-note').textContent = b.netPayable > 0
      ? `A ${stfMoney(b.netPayable)} expense will be recorded. Advances already paid are not booked again.`
      : 'Advances cover the whole salary, so no cash is paid and no expense is recorded.';
  }
  stfOpen('salary');
}

async function confirmPaySalary(){
  const b = stf.detail;
  if(!b || b.status !== 'Draft') return;
  const period = stfPeriod(b.year, b.month);
  const ok = await smmsConfirm(
    b.netPayable > 0
      ? `Pay ${stfMoney(b.netPayable)} to ${b.staffName} for ${period}? The month is then locked and cannot be paid again.`
      : `Mark ${period} as settled for ${b.staffName}? Advances cover the full salary. The month is then locked.`,
    { danger: false, title: 'Confirm salary payment', confirmText: b.netPayable > 0 ? `Pay ${stfMoney(b.netPayable)}` : 'Mark settled' });
  if(!ok) return;

  const btn = document.getElementById('salary-pay-btn');
  btn.disabled = true;
  try{
    await Api.paySalary(b.staffId, {
      year: b.year,
      month: b.month,
      paidOn: document.getElementById('salary-paidon').value || null,
      paymentMode: document.getElementById('salary-mode').value,
      notes: document.getElementById('salary-notes').value.trim() || null,
      // The figure on screen. If anything changed meanwhile the server refuses instead of paying a different sum.
      expectedNetPayable: b.netPayable
    });
    closeModal('salary');
    toast(`${period} salary paid`);
  }catch(err){
    toast(err.message || 'Could not pay the salary', 'warn');
    if(err.status === 409) closeModal('salary');
  }finally{
    btn.disabled = false;
    await Promise.all([loadStaffMonth(), loadStaffHistory()]);
  }
}

// ── Reverse ──

function openReverseSalary(settlementId){
  const id = settlementId || (stf.salary && stf.salary.settlementId);
  if(!id) return;
  stf.reverseId = id;
  const b = stf.salary && stf.salary.settlementId === id ? stf.salary : null;
  document.getElementById('salrev-context').textContent = b
    ? `${stfPeriod(b.year, b.month)} — ${b.staffName}, ${stfMoney(b.netPayable)} paid on ${stfDate(b.paidOn)}.`
    : '';
  document.getElementById('salrev-reason').value = '';
  stfOpen('salrev');
}

async function saveReverseSalary(){
  const reason = document.getElementById('salrev-reason').value.trim();
  if(!reason) return toast('Give a reason so the correction is traceable', 'warn');
  const ok = await smmsConfirm('Reverse this salary? Its expense entry is withdrawn and the advances become available again. The original record stays in the history.',
    { title: 'Reverse salary?', confirmText: 'Reverse' });
  if(!ok) return;
  try{
    await Api.reverseSalary(stf.reverseId, reason);
    closeModal('salrev');
    closeModal('salary');
    toast('Salary reversed. Correct it and pay again.');
    await Promise.all([loadStaffMonth(), loadStaffHistory()]);
  }catch(err){ toast(err.message || 'Could not reverse the salary', 'warn'); }
}

// ── History ──

function stfFillHistoryYears(){
  const s = stfCurrent();
  const sel = document.getElementById('stf-hist-year');
  const first = +String(s.joiningDate).slice(0, 4) || new Date().getFullYear();
  const last = Math.max(new Date().getFullYear(), first);
  const years = [];
  for(let y = last; y >= first; y--) years.push(y);
  sel.innerHTML = '<option value="">All years</option>' + years.map(y => `<option value="${y}">${y}</option>`).join('');
  sel.value = String(stf.year);
}

async function loadStaffHistory(){
  const s = stfCurrent();
  const tbody = document.getElementById('stf-hist-tbody');
  if(!s || !tbody) return;
  const year = document.getElementById('stf-hist-year').value;
  let rows;
  try{ rows = await Api.getSalaryHistory(s.id, year || null) || []; }
  catch(err){ return toast(err.message || 'Failed to load salary history', 'warn'); }

  tbody.innerHTML = rows.map(h => `<tr${h.status === 'Reversed' ? ' style="opacity:.6;"' : ''}>
    <td><a href="#" class="tlink" onclick="openSalaryDetails(null, ${h.settlementId});return false;">${stfPeriod(h.year, h.month)}</a></td>
    <td>${stfMoney(h.grossSalary)}</td>
    <td>${h.presentDays}</td>
    <td>${h.paidLeaveDays}</td>
    <td>${h.unpaidLeaveDays}</td>
    <td>${h.leaveDeduction ? '−' + stfMoney(h.leaveDeduction) : '₹0'}</td>
    <td>${h.adjustmentsApplied ? '−' + stfMoney(h.adjustmentsApplied) : '₹0'}</td>
    <td><b>${stfMoney(h.netPaid)}</b></td>
    <td>${h.status === 'Paid' ? '<span class="badge b-paid">Paid</span>'
         : `<span class="badge b-inactive" title="${escHtml(h.reversalReason || '')}">Reversed</span>`}</td>
    <td>${stfDate(h.paidOn)}</td>
  </tr>`).join('') || '<tr><td colspan="10" class="empty">No salary paid yet.</td></tr>';
}

// ── Setup ──

function openStaffSetup(isNew){
  if(!isAdmin()) return toast('Only an Admin can change the salary setup.', 'warn');
  const s = isNew ? null : stfCurrent();
  stf.editStaffId = s ? s.id : null;

  const cats = [...new Set(['Security', ...((DB.settings && DB.settings.categories) || [])])];
  document.getElementById('staff-cat').innerHTML = cats.map(c => `<option>${escHtml(c)}</option>`).join('');
  document.getElementById('staff-payday').innerHTML = '<option value="0">Last day of the month</option>' +
    Array.from({ length: 28 }, (_, i) => `<option value="${i + 1}">${stfOrdinal(i + 1)} of the next month</option>`).join('');

  document.getElementById('staff-modal-title').textContent = s ? `Salary Setup — ${s.name}` : 'Set up Watchman';
  document.getElementById('staff-name').value = s ? s.name : '';
  document.getElementById('staff-role').value = s ? s.role : 'Watchman';
  document.getElementById('staff-mobile').value = s ? (s.mobile || '') : '';
  document.getElementById('staff-join').value = s ? String(s.joiningDate).slice(0, 10) : stfIso(new Date());
  document.getElementById('staff-leave').value = s && s.leavingDate ? String(s.leavingDate).slice(0, 10) : '';
  document.getElementById('staff-salary').value = s ? s.monthlySalary : '';
  document.getElementById('staff-leavedays').value = s ? s.paidLeavePerMonth : 2;
  document.getElementById('staff-payday').value = String(s ? s.payDay : 0);
  document.getElementById('staff-cat').value = s ? s.expenseCategory : 'Security';
  document.getElementById('staff-active').value = String(s ? s.isActive : true);
  document.getElementById('staff-notes').value = s ? (s.notes || '') : '';
  stfOpen('staff');
}

async function saveStaffSetup(){
  const leaving = document.getElementById('staff-leave').value;
  const payload = {
    name: document.getElementById('staff-name').value.trim(),
    role: document.getElementById('staff-role').value.trim(),
    mobile: document.getElementById('staff-mobile').value.trim() || null,
    joiningDate: document.getElementById('staff-join').value,
    leavingDate: leaving || null,
    monthlySalary: Number(document.getElementById('staff-salary').value),
    paidLeavePerMonth: Number(document.getElementById('staff-leavedays').value),
    payDay: Number(document.getElementById('staff-payday').value),
    expenseCategory: document.getElementById('staff-cat').value,
    isActive: document.getElementById('staff-active').value === 'true',
    notes: document.getElementById('staff-notes').value.trim() || null
  };
  if(!payload.name) return toast('Name is required', 'warn');
  if(!payload.role) return toast('Role is required', 'warn');
  if(!payload.joiningDate) return toast('Joining date is required', 'warn');
  if(document.getElementById('staff-salary').value === '' || !(payload.monthlySalary >= 0)) return toast('Enter the monthly salary (it cannot be negative)', 'warn');
  if(!Number.isInteger(payload.paidLeavePerMonth) || payload.paidLeavePerMonth < 0 || payload.paidLeavePerMonth > 31)
    return toast('Paid leave must be a whole number of days between 0 and 31', 'warn');
  if(leaving && leaving < payload.joiningDate) return toast('Leaving date cannot be before the joining date', 'warn');

  try{
    if(stf.editStaffId){
      await Api.updateStaff(stf.editStaffId, payload);
      toast('Salary setup saved. Paid months keep the salary they were paid at.');
    }else{
      const created = await Api.createStaff(payload);
      stf.staffId = created.id;
      toast(`${payload.name} added`);
    }
    closeModal('staff');
    stf.year = 0;
    await renderStaff();
  }catch(err){ toast(err.message || 'Could not save the setup', 'warn'); }
}

// ── Dashboard card ──

async function renderDashStaff(){
  const card = document.getElementById('dash-staff-card');
  if(!card) return;
  if(!canView('Expenses')){ card.style.display = 'none'; return; }
  let rows = [];
  try{ rows = await Api.getStaffSummary() || []; }catch(e){ rows = []; }
  card.style.display = rows.length ? '' : 'none';
  document.getElementById('dash-staff-tbody').innerHTML = rows.map(r => {
    const badge = r.status === 'Paid'
      ? `<span class="badge b-paid">Paid ${stfDate(r.paidOn).slice(0, 6)}</span>`
      : r.overdue
        ? `<span class="badge b-unpaid">Overdue since ${stfDate(r.dueDate).slice(0, 6)}</span>`
        : `<span class="badge b-partial">Due ${stfDate(r.dueDate).slice(0, 6)}</span>`;
    return `<tr style="cursor:pointer;" onclick="stfOpenFromDashboard(${r.staffId}, ${r.year}, ${r.month})">
      <td><strong>${escHtml(r.role)}</strong><div class="kpi-sub">${escHtml(r.name)}</div></td>
      <td>${stfPeriod(r.year, r.month)}</td>
      <td><strong>${stfMoney(r.netPayable)}</strong></td>
      <td>${badge}</td>
    </tr>`;
  }).join('');
}

function stfOpenFromDashboard(staffId, year, month){
  stf.staffId = staffId;
  stf.year = year;
  stf.month = month;
  showTab('staff', document.getElementById('nav-staff'));
}
