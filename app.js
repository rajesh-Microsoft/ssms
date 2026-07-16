let excelFileHandle=null;
let hasUnsavedChanges=false;


async function openExcelFile(){
 try{
  if(window.showOpenFilePicker){
   const [fileHandle]=await window.showOpenFilePicker({
    types:[{description:'Excel',accept:{'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet':['.xlsx']}}]
   });

   excelFileHandle=fileHandle;

   const file=await fileHandle.getFile();
   if(typeof importExcel==='function'){
     importExcel({target:{files:[file]}});
   }
   setSaveStatus('🟢 Synced','green');
   return;
  }

  const fileInput=document.getElementById('xlImport');
  if(fileInput){
    fileInput.value='';
    fileInput.click();
    setSaveStatus('🟡 Choose a file to import','orange');
    return;
  }

  throw new Error('No supported file picker is available in this browser.');
 }catch(e){
   console.error(e);
   setSaveStatus('🔴 Import Failed','red');
 }
}


function setSaveStatus(txt,color){
 const el=document.getElementById('saveStatus');
 if(el){el.innerHTML=txt;el.style.color=color;}
}

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

async function autoSaveExcel(){
 if(!isAdmin()) return;
 if(!excelFileHandle) return;
 try{
   setSaveStatus('🟡 Saving...','orange');
   const wb=buildWorkbook();
   const wbout=XLSX.write(wb,{bookType:'xlsx',type:'array'});
   const writable=await excelFileHandle.createWritable();
   await writable.write(wbout);
   await writable.close();
   hasUnsavedChanges=false;
   setSaveStatus('🟢 Synced','green');
 }catch(e){
   setSaveStatus('🔴 Save Failed','red');
   console.error(e);
 }
}

// ═══════════════════════════════════════════════
// CONSTANTS
// ═══════════════════════════════════════════════
const MONTHS     = ['','January','February','March','April','May','June','July','August','September','October','November','December'];
const MONTH_NUM  = {January:1,February:2,March:3,April:4,May:5,June:6,July:7,August:8,September:9,October:10,November:11,December:12};
const PER        = 10;

// ═══════════════════════════════════════════════
// DATA STORE
// ═══════════════════════════════════════════════
let DB = {
  members: [], collections: [], expenses: [], auditLog: [],
  users: [{id:1,username:'Admin',password:'8a20ecbd88068db6045ac597db8f75bffdd945fbd94db618caceac52431ce9ca',salt:'smms',role:'Admin',email:'admin@society.com',status:'Active'}],
  settings: {
    societyName:'Our Society', address:'', email:'', phone:'',
    maintenanceAmt:2000, floors:['1','2','3','4','5'],
    categories:['Security','Housekeeping','Electricity','Water','Repairs','Lift Maintenance','Gardening','Festival','CCTV','Miscellaneous']
  }
};
let editId   = {col:null, exp:null, mem:null};
let pages    = {col:1, exp:1, mem:1, audit:1};
let trendChart, pieChart, annualChart;
let currentUser = null;

// ═══════════════════════════════════════════════
// AUTH — client-side login gate (salted SHA-256 password hashes).
// Note: since this app has no server, this only gates the in-app UI/actions;
// anyone with direct access to the shared Excel file can still open it in Excel.
// ═══════════════════════════════════════════════
function randomSalt(){
  return Array.from(crypto.getRandomValues(new Uint8Array(8))).map(b=>b.toString(16).padStart(2,'0')).join('');
}
async function hashPassword(password, salt){
  const enc = new TextEncoder().encode(`${salt||''}:${password||''}`);
  const buf = await crypto.subtle.digest('SHA-256', enc);
  return Array.from(new Uint8Array(buf)).map(b=>b.toString(16).padStart(2,'0')).join('');
}
function isAdmin(){ return !!currentUser && currentUser.role === 'Admin'; }

function showHome(){
  const home = document.getElementById('homeScreen');
  const login = document.getElementById('loginScreen');
  if(login) login.style.display = 'none';
  if(home) home.style.display = 'flex';
  const errEl = document.getElementById('loginError');
  if(errEl) errEl.textContent = '';
}

function showLoginForm(intendedRole){
  const home = document.getElementById('homeScreen');
  const login = document.getElementById('loginScreen');
  if(home) home.style.display = 'none';
  if(login) login.style.display = 'flex';
  const heading = document.getElementById('loginHeading');
  const sub = document.getElementById('loginSubheading');
  // Cosmetic hint only — the actual role always comes from the matched
  // DB.users record in attemptLogin(), never from which link was clicked.
  if(heading) heading.textContent = intendedRole === 'Admin' ? 'Admin Login' : 'Member Login';
  if(sub) sub.textContent = intendedRole === 'Admin'
    ? 'Sign in with your Admin credentials'
    : 'Sign in with the credentials shared by your Admin';
  const userField = document.getElementById('login-username');
  if(userField) userField.focus();
}

async function attemptLogin(){
  const username = document.getElementById('login-username').value.trim();
  const password = document.getElementById('login-password').value;
  const errEl = document.getElementById('loginError');
  errEl.textContent = '';
  if(!username || !password){ errEl.textContent = 'Enter username and password.'; return; }

  const user = DB.users.find(u => String(u.username||'').toLowerCase() === username.toLowerCase());
  if(!user || !user.password){ errEl.textContent = '❌ Invalid username or password.'; return; }
  if(String(user.status||'Active').toLowerCase() !== 'active'){ errEl.textContent = '❌ This account is inactive. Contact your admin.'; return; }

  const hash = await hashPassword(password, user.salt || '');
  if(hash !== user.password){ errEl.textContent = '❌ Invalid username or password.'; return; }

  currentUser = {id:user.id, username:user.username, role:user.role};
  document.body.classList.remove('logged-out');
  document.getElementById('login-password').value = '';
  document.getElementById('loginScreen').style.display = 'none';
  document.getElementById('homeScreen').style.display = 'none';
  applyRolePermissions();
  updateSidebarUserInfo();
  populateYearDropdown();
  applySettings();
  renderDashboard();
  toast(`Welcome, ${user.username}!`);
}

function logout(){
  currentUser = null;
  document.body.classList.add('logged-out');
  document.getElementById('login-username').value = '';
  document.getElementById('login-password').value = '';
  document.getElementById('loginError').textContent = '';
  showHome();
}

function applyRolePermissions(){
  const admin = isAdmin();
  document.body.classList.toggle('role-member', !admin);
  ['set-sname','set-addr','set-email','set-phone','set-mamt','set-wings','new-cat'].forEach(id=>{
    const el = document.getElementById(id);
    if(el) el.disabled = !admin;
  });
}

function updateSidebarUserInfo(){
  if(!currentUser) return;
  const av = document.getElementById('sbUav');
  const nm = document.getElementById('sbUname');
  const rl = document.getElementById('sbUrole');
  if(av) av.textContent = currentUser.username.charAt(0).toUpperCase();
  if(nm) nm.textContent = currentUser.username;
  if(rl) rl.textContent = currentUser.role === 'Admin' ? 'Administrator' : 'Member (Read-only)';
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
window.onload = () => {
  populateYearDropdown();
  applySettings();
  renderDashboard();
  const homeName = document.getElementById('homeSocietyName');
  if(homeName && DB.settings && DB.settings.societyName) homeName.textContent = DB.settings.societyName;
  showHome();
};

function populateYearDropdown(){
  const sel = document.getElementById('topYear');
  const cur = new Date().getFullYear();
  for(let y = cur+1; y >= cur-5; y--){
    const o = document.createElement('option');
    o.value = y; o.text = y;
    if(y === cur) o.selected = true;
    sel.appendChild(o);
  }
}

// ═══════════════════════════════════════════════
// NAVIGATION
// ═══════════════════════════════════════════════
function showTab(t, el){
  if(t==='admin' && !isAdmin()) return toast('Admin access required.','warn');
  document.querySelectorAll('.tab-content').forEach(x => x.classList.remove('active'));
  document.querySelectorAll('.nav-item').forEach(x => x.classList.remove('active'));
  document.getElementById('tab-'+t).classList.add('active');
  if(el) el.classList.add('active');
  const titles = {dashboard:'Dashboard',collections:'Collections',expenses:'Expenses',members:'Members',reports:'Reports & Analytics',importexport:'Import / Export',notifications:'Notifications',auditlog:'Audit Log',settings:'Settings',admin:'Admin Panel'};
  document.getElementById('pageTitle').textContent = titles[t] || t;
  const renders = {dashboard:renderDashboard, collections:renderCollections, expenses:renderExpenses, members:renderMembers, reports:renderReports, notifications:renderNotifications, auditlog:renderAudit, settings:loadSettingsUI, admin:renderUsers};
  if(renders[t]) renders[t]();
}

function onTopFilterChange(){
  const active = document.querySelector('.tab-content.active');
  if(!active) return;
  const t = active.id.replace('tab-','');
  const renders = {dashboard:renderDashboard, collections:renderCollections, expenses:renderExpenses, reports:renderReports, notifications:renderNotifications};
  if(renders[t]) renders[t]();
}

// ═══════════════════════════════════════════════
// FILTER HELPERS
// ═══════════════════════════════════════════════
function topMonth(){ return +document.getElementById('topMonth').value; }
function topYear() { return +document.getElementById('topYear').value; }

function filteredCollections(){
  const m = topMonth(), y = topYear();
  return DB.collections.filter(c => (!m || getMonth(c)===m) && (!y || getYear(c)===y));
}
function filteredExpenses(){
  const m = topMonth(), y = topYear();
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

function setColMonth(mo){ document.getElementById('topMonth').value = mo; renderCollections(); }
function setExpMonth(mo){ document.getElementById('topMonth').value = mo; renderExpenses(); }

// ═══════════════════════════════════════════════
// DASHBOARD
// ═══════════════════════════════════════════════
function renderDashboard(){
  const cols = filteredCollections();
  const exps = filteredExpenses();
  const totalCol = cols.reduce((s,c)=>s+getAmt(c),0);
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
  const y = topYear() || new Date().getFullYear();
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
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editCollection('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteCollection('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

function saveCollection(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  const mid = document.getElementById('col-member').value;
  const mem = DB.members.find(m=>String(fld(m,'id','Id'))===String(mid));
  if(!mem) return toast('Select a member','warn');
  const amt = +document.getElementById('col-amount').value;
  if(!amt)  return toast('Enter amount','warn');
  const mo  = +document.getElementById('col-month').value;
  const obj = {
    id: editId.col || Date.now(),
    memberId: String(fld(mem,'id','Id')), memberName: fld(mem,'name','Name'),
    flat: fld(mem,'flat','Flat'), floor: fld(mem,'floor','Floor'),
    amount: amt, month: MONTHS[mo], monthNum: mo,
    year: +document.getElementById('col-year').value,
    paymentDate: document.getElementById('col-date').value,
    paymentMode: document.getElementById('col-mode').value,
    status: document.getElementById('col-status').value,
    remarks: document.getElementById('col-remarks').value
  };
  if(editId.col){ const idx=DB.collections.findIndex(c=>String(c.id||c.Id)===String(editId.col)); if(idx>-1) DB.collections[idx]=obj; addAudit('Collections','Edit','Edited: '+obj.memberName); }
  else { DB.collections.push(obj); addAudit('Collections','Add','Added: '+obj.memberName+' ₹'+amt); }
  closeModal('col'); renderCollections(); renderDashboard(); autoSaveExcel(); toast('Collection saved!');
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

function deleteCollection(id){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  if(!confirm('Delete this entry?')) return;
  DB.collections = DB.collections.filter(x=>String(x.id||x.Id)!==String(id));
  addAudit('Collections','Delete','Deleted collection id:'+id);
  renderCollections(); renderDashboard(); autoSaveExcel(); toast('Deleted!','warn');
}

// ═══════════════════════════════════════════════
// EXPENSES
// ═══════════════════════════════════════════════
function renderExpenses(){
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
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editExpense('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteExpense('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

function saveExpense(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  const amt  = +document.getElementById('exp-amount').value;
  const desc = document.getElementById('exp-desc').value.trim();
  if(!amt||!desc) return toast('Fill required fields','warn');
  const mo = +document.getElementById('exp-month').value;
  const obj = {
    id: editId.exp || Date.now(),
    expenseDate: document.getElementById('exp-date').value,
    category: document.getElementById('exp-cat').value,
    description: desc,
    vendor: document.getElementById('exp-vendor').value,
    amount: amt,
    paymentMode: document.getElementById('exp-mode').value,
    month: MONTHS[mo], monthNum: mo,
    year: +document.getElementById('exp-year').value,
    remarks: document.getElementById('exp-remarks').value
  };
  if(editId.exp){ const idx=DB.expenses.findIndex(x=>String(x.id||x.Id)===String(editId.exp)); if(idx>-1) DB.expenses[idx]=obj; addAudit('Expenses','Edit','Edited: '+desc); }
  else { DB.expenses.push(obj); addAudit('Expenses','Add','Added: '+desc+' ₹'+amt); }
  closeModal('exp'); renderExpenses(); renderDashboard(); autoSaveExcel(); toast('Expense saved!');
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

function deleteExpense(id){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  if(!confirm('Delete this expense?')) return;
  DB.expenses = DB.expenses.filter(x=>String(x.id||x.Id)!==String(id));
  addAudit('Expenses','Delete','Deleted expense id:'+id);
  renderExpenses(); renderDashboard(); autoSaveExcel(); toast('Deleted!','warn');
}

// ═══════════════════════════════════════════════
// MEMBERS
// ═══════════════════════════════════════════════
function renderMembers(){
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
  return `<tr>
    <td>${i+1}</td>
    <td>${fld(m,'name','Name')}</td>
    <td>${fld(m,'flat','Flat')}</td>
    <td>Floor ${fld(m,'floor','Floor')}</td>
    <td>${fld(m,'mobile','Mobile')}</td>
    <td>${fld(m,'email','Email')}</td>
    <td><span class="badge b-${stk}">${st}</span></td>
    <td><div class="act-btns">
      <button class="ic-btn" onclick="editMember('${id}')">✏️</button>
      <button class="ic-btn" onclick="deleteMember('${id}')">🗑️</button>
    </div></td>
  </tr>`;
}

function saveMember(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  const name = document.getElementById('mem-name').value.trim();
  const flat = document.getElementById('mem-flat').value.trim();
  if(!name||!flat) return toast('Fill required fields','warn');
  const obj = {
    id: editId.mem || Date.now(),
    name, flat,
    floor: document.getElementById('mem-floor').value,
    mobile: document.getElementById('mem-mobile').value,
    email:  document.getElementById('mem-email').value,
    status: document.getElementById('mem-status').value
  };
  if(editId.mem){ const idx=DB.members.findIndex(m=>String(m.id||m.Id)===String(editId.mem)); if(idx>-1) DB.members[idx]=obj; addAudit('Members','Edit','Edited: '+name); }
  else { DB.members.push(obj); addAudit('Members','Add','Added: '+name+' ('+flat+')'); }
  closeModal('mem'); renderMembers(); autoSaveExcel(); toast('Member saved!');
}

function editMember(id){
  const m = DB.members.find(x=>String(x.id||x.Id)===String(id)); if(!m) return;
  editId.mem = id;
  document.getElementById('mem-modal-title').textContent = 'Edit Member';
  populateFloorDropdown('mem-floor');
  document.getElementById('mem-name').value   = fld(m,'name','Name');
  document.getElementById('mem-flat').value   = fld(m,'flat','Flat');
  document.getElementById('mem-floor').value  = fld(m,'floor','Floor');
  document.getElementById('mem-mobile').value = fld(m,'mobile','Mobile');
  document.getElementById('mem-email').value  = fld(m,'email','Email');
  document.getElementById('mem-status').value = fld(m,'status','Status')||'Active';
  document.getElementById('modal-mem').classList.add('open');
}

function deleteMember(id){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  if(!confirm('Delete this member?')) return;
  DB.members = DB.members.filter(x=>String(x.id||x.Id)!==String(id));
  addAudit('Members','Delete','Deleted member id:'+id);
  renderMembers(); autoSaveExcel(); toast('Deleted!','warn');
}

// ═══════════════════════════════════════════════
// REPORTS
// ═══════════════════════════════════════════════
function renderReports(){
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

// ═══════════════════════════════════════════════
// NOTIFICATIONS
// ═══════════════════════════════════════════════
function renderNotifications(){
  const m=topMonth(), y=topYear()||new Date().getFullYear();
  const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&(!y||getYear(c)===y)).map(c=>String(fld(c,'memberId','MemberId')).trim()));
  const pend=DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim()));
  const recent=[...DB.auditLog].reverse().slice(0,5);
  const items=[
    ...pend.map(m=>({color:'#fc8181',text:`⚠️ ${fld(m,'name','Name')} (${fld(m,'flat','Flat')}) – maintenance pending`,time:'Due this period'})),
    ...recent.map(a=>({color:'#6c63ff',text:`📝 ${a.action} in ${a.module}: ${a.details}`,time:a.timestamp}))
  ];
  document.getElementById('notif-list').innerHTML=items.map(n=>`<div class="notif-item"><div class="ndot" style="background:${n.color}"></div><div><div class="ntext">${n.text}</div><div class="ntime">${n.time}</div></div></div>`).join('')||'<div class="empty">No notifications 🎉</div>';
  updateNotifBadge();
}

function updateNotifBadge(){
  const m=topMonth(),y=topYear()||new Date().getFullYear();
  const paidSet=new Set(DB.collections.filter(c=>getStatus(c).toLowerCase()==='paid'&&(!m||getMonth(c)===m)&&(!y||getYear(c)===y)).map(c=>String(fld(c,'memberId','MemberId')).trim()));
  const cnt=DB.members.filter(m=>!paidSet.has(String(fld(m,'id','Id')).trim())).length;
  document.getElementById('notifBadge').textContent=cnt;
}

// ═══════════════════════════════════════════════
// AUDIT LOG
// ═══════════════════════════════════════════════
function addAudit(module,action,details){ DB.auditLog.push({id:Date.now(),timestamp:new Date().toLocaleString(),user:(currentUser&&currentUser.username)||'Admin',module,action,details}); }

function renderAudit(){
  const search=document.getElementById('auditSearch').value.toLowerCase();
  const mod=document.getElementById('auditModF').value;
  const data=[...DB.auditLog].reverse().filter(a=>(!mod||a.module===mod)&&(!search||a.details.toLowerCase().includes(search)||a.action.toLowerCase().includes(search)));
  renderPage('audit',data,a=>`<tr><td style="font-size:10px;color:var(--sub)">${a.timestamp}</td><td>${a.user}</td><td>${a.module}</td><td>${a.action}</td><td>${a.details}</td></tr>`);
}

// ═══════════════════════════════════════════════
// SETTINGS
// ═══════════════════════════════════════════════
function loadSettingsUI(){
  const s=DB.settings;
  document.getElementById('set-sname').value=s.societyName||'';
  document.getElementById('set-addr').value=s.address||'';
  document.getElementById('set-email').value=s.email||'';
  document.getElementById('set-phone').value=s.phone||'';
  document.getElementById('set-mamt').value=s.maintenanceAmt||2000;
  document.getElementById('set-wings').value=(s.floors||[]).join(',');
  document.getElementById('set-theme').value=s.theme||'light';
  renderCatList();
}
function saveSettings(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  DB.settings.societyName  = document.getElementById('set-sname').value;
  DB.settings.address      = document.getElementById('set-addr').value;
  DB.settings.email        = document.getElementById('set-email').value;
  DB.settings.phone        = document.getElementById('set-phone').value;
  DB.settings.maintenanceAmt = +document.getElementById('set-mamt').value||2000;
  DB.settings.floors       = document.getElementById('set-wings').value.split(',').map(w=>w.trim()).filter(Boolean);
  document.getElementById('societyLogoSub').textContent=DB.settings.societyName;
  addAudit('Settings','Update','Settings updated');
  toast('Settings saved!');
}
function applySettings(){
  document.getElementById('societyLogoSub').textContent=DB.settings.societyName;
  const homeName = document.getElementById('homeSocietyName');
  if(homeName) homeName.textContent = DB.settings.societyName;
}
function applyThemeSetting(){ const t=document.getElementById('set-theme').value; document.body.classList.toggle('dark',t==='dark'); DB.settings.theme=t; }
function renderCatList(){ document.getElementById('cat-list').innerHTML=DB.settings.categories.map((c,i)=>`<span class="badge b-active" style="cursor:pointer" onclick="removeCategory(${i})">${c} ✕</span>`).join(''); }
function addCategory(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true; const v=document.getElementById('new-cat').value.trim(); if(!v)return; DB.settings.categories.push(v); document.getElementById('new-cat').value=''; renderCatList(); }
function removeCategory(i){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true; DB.settings.categories.splice(i,1); renderCatList(); }

// ═══════════════════════════════════════════════
// ADMIN
// ═══════════════════════════════════════════════
function renderUsers(){
  document.getElementById('user-tbody').innerHTML=DB.users.map((u,i)=>`<tr><td>${i+1}</td><td>${u.username}</td><td>${u.role}</td><td>${u.email||'-'}</td><td><span class="badge b-${u.status==='Active'?'active':'inactive'}">${u.status}</span></td><td><div class="act-btns"><button class="ic-btn" onclick="resetUserPassword(${u.id})" title="Reset password">🔑</button><button class="ic-btn" onclick="deleteUser(${u.id})">🗑️</button></div></td></tr>`).join('');
}
async function saveUser(){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
hasUnsavedChanges=true;
  const n=document.getElementById('u-name').value.trim();
  const pw=document.getElementById('u-password').value;
  if(!n)return toast('Enter username','warn');
  if(DB.users.some(u=>String(u.username||'').toLowerCase()===n.toLowerCase())) return toast('Username already exists','warn');
  if(!pw||pw.length<4)return toast('Password must be at least 4 characters','warn');
  const salt = randomSalt();
  const hash = await hashPassword(pw, salt);
  DB.users.push({id:Date.now(),username:n,password:hash,salt,role:document.getElementById('u-role').value,email:document.getElementById('u-email').value,status:document.getElementById('u-status').value});
  document.getElementById('u-password').value='';
  addAudit('Users','Add',`Added user: ${n}`);
  closeModal('user'); renderUsers(); toast('User added!');
}
async function resetUserPassword(id){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
  const user = DB.users.find(u=>u.id===id);
  if(!user) return;
  const pw = prompt(`Enter a new password for "${user.username}":`);
  if(pw===null) return;
  if(pw.length<4) return toast('Password must be at least 4 characters','warn');
  user.salt = randomSalt();
  user.password = await hashPassword(pw, user.salt);
  hasUnsavedChanges = true;
  addAudit('Users','Reset Password',`Password reset for user: ${user.username}`);
  toast('Password updated!');
}
function deleteUser(id){
if(!isAdmin()) return toast('Read-only access — Admin only.','warn');
if(DB.users.length<=1)return toast('Cannot delete last user','warn'); if(!confirm('Delete?'))return; DB.users=DB.users.filter(u=>u.id!==id); addAudit('Users','Delete','Deleted user id:'+id); renderUsers(); toast('Deleted','warn'); }

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
function refreshSection(k){ if(k==='col')renderCollections(); else if(k==='exp')renderExpenses(); else if(k==='mem')renderMembers(); else if(k==='audit')renderAudit(); }

// ═══════════════════════════════════════════════
// MODAL HELPERS
// ═══════════════════════════════════════════════
function openModal(type){
  if((type==='col'||type==='exp'||type==='mem'||type==='user') && !isAdmin()) return toast('Read-only access — Admin only.','warn');
  editId[type]=null;
  if(type==='col'){ document.getElementById('col-modal-title').textContent='Add Collection'; populateMemberDropdown(); document.getElementById('col-date').value=new Date().toISOString().split('T')[0]; document.getElementById('col-month').value=new Date().getMonth()+1; document.getElementById('col-year').value=new Date().getFullYear(); document.getElementById('col-amount').value=''; document.getElementById('col-remarks').value=''; }
  if(type==='exp'){ document.getElementById('exp-modal-title').textContent='Add Expense'; populateCatDropdown(); document.getElementById('exp-date').value=new Date().toISOString().split('T')[0]; document.getElementById('exp-month').value=new Date().getMonth()+1; document.getElementById('exp-year').value=new Date().getFullYear(); document.getElementById('exp-amount').value=''; document.getElementById('exp-desc').value=''; document.getElementById('exp-vendor').value=''; document.getElementById('exp-remarks').value=''; }
  if(type==='mem'){ document.getElementById('mem-modal-title').textContent='Add Member'; populateFloorDropdown('mem-floor'); document.getElementById('mem-name').value=''; document.getElementById('mem-flat').value=''; document.getElementById('mem-mobile').value=''; document.getElementById('mem-email').value=''; }
  document.getElementById('modal-'+type).classList.add('open');
}
function closeModal(type){ document.getElementById('modal-'+type).classList.remove('open'); }

function populateMemberDropdown(){
  document.getElementById('col-member').innerHTML=DB.members.filter(m=>(fld(m,'status','Status')||'Active')==='Active').map(m=>`<option value="${fld(m,'id','Id')}">${fld(m,'name','Name')} (${fld(m,'flat','Flat')})</option>`).join('');
}
function populateCatDropdown(){ document.getElementById('exp-cat').innerHTML=DB.settings.categories.map(c=>`<option>${c}</option>`).join(''); }
function populateFloorDropdown(id){ document.getElementById(id).innerHTML=DB.settings.floors.map(f=>`<option value="${f}">Floor ${f}</option>`).join(''); }

// ═══════════════════════════════════════════════
// IMPORT / EXPORT
// ═══════════════════════════════════════════════
function importExcel(event){
  const f=event.target.files[0]; if(!f)return;
  const reader=new FileReader();
  reader.onload=e=>{
    try{
      const wb=XLSX.read(e.target.result,{type:'binary',cellDates:false});
      let imp=0;
      if(wb.Sheets['Members'])    { DB.members    =XLSX.utils.sheet_to_json(wb.Sheets['Members']);    imp++; }
      if(wb.Sheets['Collections']){ DB.collections=XLSX.utils.sheet_to_json(wb.Sheets['Collections']); imp++; }
      if(wb.Sheets['Expenses'])   { DB.expenses   =XLSX.utils.sheet_to_json(wb.Sheets['Expenses']);    imp++; }
      if(wb.Sheets['AuditLog'])   { DB.auditLog   =XLSX.utils.sheet_to_json(wb.Sheets['AuditLog']); }
      if(wb.Sheets['Users']){
        const loadedUsers = XLSX.utils.sheet_to_json(wb.Sheets['Users']);
        if(loadedUsers.some(u=>u.password)){
          DB.users = loadedUsers;
        } else if(loadedUsers.length){
          console.warn('Users sheet has no passwords configured yet — keeping the built-in Admin login until an admin adds accounts with passwords.');
        }
        imp++;
      }
      if(wb.Sheets['Settings']){
        const settingsRows = XLSX.utils.sheet_to_json(wb.Sheets['Settings']);
        const s = settingsRows[0];
        if(s){
          const toArray = v => Array.isArray(v) ? v : String(v||'').split(',').map(x=>x.trim()).filter(Boolean);
          DB.settings = {
            ...DB.settings, ...s,
            floors: toArray(s.floors ?? DB.settings.floors),
            categories: toArray(s.categories ?? DB.settings.categories)
          };
        }
        imp++;
      }
      const status=`✅ Imported ${imp} sheet(s). Members: ${DB.members.length}, Collections: ${DB.collections.length}, Expenses: ${DB.expenses.length}`;
      document.getElementById('import-status').textContent=status;
      const loginFileStatus=document.getElementById('loginFileStatus');
      if(loginFileStatus) loginFileStatus.textContent=`✅ Loaded: ${f.name}`;
      if(isAdmin()) addAudit('Import','Import',`Imported: ${f.name}`);
      setSaveStatus('🟢 Imported','green');
      applySettings(); renderDashboard(); toast('Excel imported!');
    }catch(err){ document.getElementById('import-status').textContent='❌ Error: '+err.message; }
  };
  reader.readAsBinaryString(f);
}

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

function analyzeBankStatement(){
  if(!isAdmin()) return showBankImportStatus('❌ Admin access required.', 'danger');
  const fileInput = document.getElementById('bankStatementFile');
  const statusEl = document.getElementById('bankImportStatus');
  const previewEl = document.getElementById('bankPreview');
  previewEl.style.display = 'none';
  window.bankImportData = null;

  if(!fileInput || !fileInput.files.length){
    return showBankImportStatus('❌ Please select a bank statement file first.', 'danger');
  }

  const file = fileInput.files[0];
  const extension = file.name.split('.').pop().toLowerCase();
  const supported = ['xlsx','xls','csv'];
  if(!supported.includes(extension)){
    return showBankImportStatus(`❌ Unsupported file type: .${extension}. Upload .xlsx, .xls or .csv.`, 'danger');
  }

  showBankImportStatus('⏳ Reading bank statement file...', 'info');

  const reader = new FileReader();
  reader.onload = async (e) => {
    try {
      const data = e.target.result;
      let workbook;
      if(extension === 'csv'){
        const text = new TextDecoder('utf-8').decode(data);
        workbook = XLSX.read(text, {type:'string', raw:false});
      } else {
        workbook = XLSX.read(data, {type:'array', cellDates:true, raw:false});
      }
      if(!workbook || !workbook.SheetNames.length){
        throw new Error('No worksheet detected in the bank statement file.');
      }

      const sheetName = workbook.SheetNames[0];
      const sheet = workbook.Sheets[sheetName];
      const rows = XLSX.utils.sheet_to_json(sheet, {defval:''});
      if(!rows.length){
        throw new Error('The selected bank statement contains no rows.');
      }

      const normalizedRows = normalizeBankRows(rows);
      if(!normalizedRows.length){
        throw new Error('No debit or credit transactions were detected in the bank statement.');
      }

      const options = readBankImportOptions();
      const {collections, expenses, warnings, flagged} = mapBankRowsToSMMS(normalizedRows, options);
      window.bankImportData = {rows: normalizedRows, collections, expenses, sourceFile:file.name, sheetName, warnings};

      const html = bankImportPreviewHtml(collections, expenses, warnings, flagged, file.name, sheetName);
      previewEl.innerHTML = html;
      previewEl.style.display = 'block';

      showBankImportStatus(`✅ Analysis complete. ${collections.length} collections and ${expenses.length} expenses found${flagged.length ? ` (${flagged.length} need flat verification)` : ''}.`, warnings.length || flagged.length ? 'warning' : 'success');
    } catch (err) {
      console.error(err);
      showBankImportStatus('❌ Analysis failed: ' + (err.message || err), 'danger');
    }
  };

  reader.onerror = () => showBankImportStatus('❌ Unable to read the bank statement file.', 'danger');
  reader.readAsArrayBuffer(file);
}

function getBankImportOption(id, defaultValue = false){
  const el = document.getElementById(id);
  return el ? el.checked : defaultValue;
}

function readBankImportOptions(){
  return {
    autoCreateCollections: getBankImportOption('autoCreateCollections', true),
    autoCreateExpenses: getBankImportOption('autoCreateExpenses', true),
    matchByFlat: getBankImportOption('matchByFlat', true),
    createAuditEntry: getBankImportOption('createAuditEntry', true),
    backupWorkbook: getBankImportOption('backupWorkbook', true)
  };
}

function generateBankImport(){
  if(!isAdmin()) return showBankImportStatus('❌ Admin access required.', 'danger');
  if(!window.bankImportData){
    return showBankImportStatus('❌ No bank statement analysis available. Click Analyze first.', 'danger');
  }

  const options = readBankImportOptions();
  const {collections, expenses, warnings, flagged} = mapBankRowsToSMMS(window.bankImportData.rows, options);

  if(!collections.length && !expenses.length){
    showBankImportStatus('⚠️ Nothing new to add — every transaction was either a duplicate of an existing record or skipped.', 'warning');
    console.warn('Bank import: no new records.', warnings);
    return;
  }

  const sourceFile = window.bankImportData.sourceFile;

  (async () => {
    if(options.backupWorkbook){
      showBankImportStatus('⏳ Creating backup of current data...', 'info');
      await createBackupWorkbook();
    }

    DB.collections.push(...collections);
    DB.expenses.push(...expenses);
    hasUnsavedChanges = true;

    if(options.createAuditEntry){
      addAudit('Bank Import', 'Import',
        `Imported ${collections.length} collection(s) and ${expenses.length} expense(s) from ${sourceFile}` +
        (flagged.length ? ` (${flagged.length} flagged for flat verification).` : '.'));
    }

    renderCollections();
    renderExpenses();
    renderDashboard();
    if(typeof renderReports === 'function') renderReports();
    if(typeof renderAudit === 'function') renderAudit();

    let saveNote;
    if(excelFileHandle){
      await autoSaveExcel();
      saveNote = 'Saved to the shared master file — visible to everyone.';
    } else {
      saveToExcel();
      saveNote = 'Downloaded the updated master file — upload/replace it in the shared location so others can see it.';
    }

    const reviewNote = flagged.length ? ` ⚠️ ${flagged.length} collection(s) need manual flat verification — look for the ⚠ rows in the Collections tab.` : '';
    const dupNote = warnings.filter(w => w.includes('duplicate')).length ? ' Duplicate rows already in the system were skipped.' : '';
    showBankImportStatus(`✅ Added ${collections.length} collection(s) and ${expenses.length} expense(s). ${saveNote}${reviewNote}${dupNote}`, flagged.length ? 'warning' : 'success');
    toast(`Bank import complete: ${collections.length} collections, ${expenses.length} expenses added.`);

    window.bankImportData = null;
    const previewEl = document.getElementById('bankPreview');
    if(previewEl) previewEl.style.display = 'none';
    const fileInput = document.getElementById('bankStatementFile');
    if(fileInput) fileInput.value = '';
  })();
}

function showBankImportStatus(message, type = 'info'){
  const el = document.getElementById('bankImportStatus');
  if(!el) return;
  el.textContent = message;
  el.style.color = type === 'danger' ? '#e53e3e' : type === 'warning' ? '#d69e2e' : '#276749';
}

function bankImportPreviewHtml(collections, expenses, warnings, flagged, fileName, sheetName){
  return `
    <div style="font-size:13px;line-height:1.5;">
      <div><strong>File:</strong> ${fileName}</div>
      <div><strong>Sheet:</strong> ${sheetName}</div>
      <div><strong>Collections:</strong> ${collections.length}</div>
      <div><strong>Expenses:</strong> ${expenses.length}</div>
      ${flagged.length ? `<div style="color:#d69e2e;"><strong>⚠ Needs review:</strong> ${flagged.length} collection(s) could not be matched to a flat/member automatically.</div>` : ''}
      ${warnings.length ? `<div style="color:#d69e2e;"><strong>Warnings:</strong> ${warnings.length}. Check console for row details.</div>` : ''}
      <div style="margin-top:10px;max-height:220px;overflow:auto;">
        <div style="font-weight:700;">Collections Preview</div>
        ${collections.slice(0,6).map(c => `<div>${c.needsReview ? '⚠️' : '📥'} ${c.paymentDate || c.date} | ${c.memberName || 'Unverified'} | Flat ${c.flat || '-'} | ₹${c.amount}</div>`).join('') || '<div class="empty">No collection records found.</div>'}
        <div style="margin-top:10px;font-weight:700;">Expenses Preview</div>
        ${expenses.slice(0,6).map(e => `<div>📤 ${e.expenseDate || e.date} | ${e.category} | ₹${e.amount} | ${e.description || '-'}</div>`).join('') || '<div class="empty">No expense records found.</div>'}
      </div>
    </div>`;
}

function normalizeBankRows(rows){
  const result = [];
  rows.forEach((row, index) => {
    const normalized = normalizeBankRow(row);
    const isContinuation = !normalized.date && !normalized.credit && !normalized.debit && !normalized.amount && normalized.remarks && result.length;
    if(isContinuation){
      // Some bank exports wrap long remarks onto a blank follow-up row — stitch it onto the previous transaction.
      const prev = result[result.length - 1];
      prev.remarks = [prev.remarks, normalized.remarks].filter(Boolean).join(' ').trim();
      return;
    }
    if(!normalized.credit && !normalized.debit && !normalized.amount){
      return;
    }
    normalized.rowIndex = index + 2;
    result.push(normalized);
  });
  return result;
}

function normalizeBankRow(row){
  const keys = Object.keys(row || {});
  const map = {};
  keys.forEach(key => { map[key.toString().trim().toLowerCase()] = key; });

  const find = aliases => {
    for(const alias of aliases){
      const normalized = alias.toString().trim().toLowerCase();
      if(map[normalized] !== undefined) return map[normalized];
    }
    return undefined;
  };

  const findFuzzy = aliases => {
    for(const alias of aliases){
      const normalized = alias.toString().trim().toLowerCase();
      for(const key of keys){
        const candidate = key.toString().trim().toLowerCase();
        if(candidate === normalized || candidate.includes(normalized) || normalized.includes(candidate)){
          return key;
        }
      }
    }
    return undefined;
  };

  const dateKey = find(['Transaction Date','Value Date','Date','Txn Date','Posted Date','Post Date']) || findFuzzy(['Transaction Date','Value Date','Date','Txn Date','Posted Date','Post Date']);
  const descKey = find(['Transaction Remarks','Transaction Description','Description','Details','Narration','Particulars','Remarks','Remark']) || findFuzzy(['Transaction Remarks','Transaction Description','Description','Details','Narration','Particulars','Remarks','Remark']);
  const typeKey = find(['Transaction Type','Type','CR/DR','Txn Type','Debit/Credit','Credit/Debit','Tran Type']) || findFuzzy(['Transaction Type','Type','CR/DR','Txn Type','Debit/Credit','Credit/Debit','Tran Type']);
  const creditKey = find(['Deposit Amount(INR)','Amount Credit','Credit Amount','Credit','Cr','Deposit Amount','Amount Deposited','Inward']) || findFuzzy(['Deposit Amount(INR)','Amount Credit','Credit Amount','Credit','Cr','Deposit Amount','Amount Deposited','Inward']);
  const debitKey = find(['Withdrawal Amount(INR)','Amount Debit','Debit Amount','Debit','Dr','Withdrawal Amount','Amount Withdrawn','Outward']) || findFuzzy(['Withdrawal Amount(INR)','Amount Debit','Debit Amount','Debit','Dr','Withdrawal Amount','Amount Withdrawn','Outward']);
  const amountKey = find(['Amount','Transaction Amount','Amount (INR)','Txn Amount','Value','Txn Value','Transaction Value']) || findFuzzy(['Amount','Transaction Amount','Amount (INR)','Txn Amount','Value','Txn Value','Transaction Value']);

  const rawDate = row[dateKey] || row[find(['Value Date','Post Date','Date'])] || '';
  const rawRemarks = String(row[descKey] || '').trim();
  const rawType = String(row[typeKey] || '').trim().toLowerCase();
  const rawCredit = toNumber(row[creditKey]);
  const rawDebit = toNumber(row[debitKey]);
  const rawAmount = toNumber(row[amountKey]);

  let credit = rawCredit;
  let debit = rawDebit;

  const rowText = Object.values(row || {}).map(v => String(v || '')).join(' ').toLowerCase();
  const typeHint = rawType || rowText;

  const numericCells = Object.entries(row || {}).map(([key, value]) => ({
    key,
    value,
    amount: toNumber(value),
    text: String(value || '').trim()
  })).filter(x => x.amount !== 0);

  const looksLikeDate = text => {
    const normalized = String(text || '').replace(/[.\s\-/]/g, '');
    return /^\d{6,8}$/.test(normalized);
  };

  const amountCellByHeader = numericCells.find(cell => {
    const key = String(cell.key || '').toLowerCase();
    return /(^|\b)(amount|amt|credit|debit|dr|cr|deposit|withdrawal|withdrawn|paid|payment|value)(\b|$)/i.test(key);
  });

  const candidateAmount = numericCells.find(cell => cell.key === amountKey) ||
    amountCellByHeader ||
    numericCells.find(cell => !/(date|txn|transaction|value|posted|payment date|post date|value date|reference|ref|cheque|vouch|voucher|id|no|number|remarks|remark|description|narration|particulars)/i.test(String(cell.key).toLowerCase()) && !looksLikeDate(cell.text)) ||
    numericCells[0] || {amount:0};

  const fallbackAmount = candidateAmount.amount;

  if(!credit && !debit && fallbackAmount){
    if(/\b(dr|debit|out|withdrawal|payment)\b/.test(typeHint)){
      debit = Math.abs(fallbackAmount);
    } else if(/\b(cr|credit|in|deposit)\b/.test(typeHint)){
      credit = Math.abs(fallbackAmount);
    } else if(fallbackAmount < 0) {
      debit = Math.abs(fallbackAmount);
    } else {
      credit = Math.abs(fallbackAmount);
    }
  }

  const amount = credit || debit || Math.abs(fallbackAmount) || 0;

  return {
    date: formatDateValue(rawDate),
    remarks: rawRemarks,
    credit,
    debit,
    amount
  };
}

function toNumber(value){
  if(value === undefined || value === null || value === '') return 0;
  let text = String(value).trim();
  if(!text) return 0;
  text = text.replace(/[,₹\s]/g, '');
  const negative = /^\(?-?\d+(\.\d+)?\)?$/.test(text) && /\(|\-/.test(text) && !/CR|Cr|cr|INR/.test(value);
  text = text.replace(/[()]/g, '');
  const num = Number(text.replace(/[^0-9.\-]/g, ''));
  if(Number.isFinite(num)) return negative ? Math.abs(num) * -1 : num;
  return 0;
}

function formatDateValue(value){
  if(value === undefined || value === null || value === '') return '';
  if(value instanceof Date){
    return value.toISOString().slice(0,10);
  }
  const text = String(value).trim();
  if(!text) return '';
  const parsed = new Date(text);
  if(!Number.isNaN(parsed.getTime())){
    return parsed.toISOString().slice(0,10);
  }
  const parts = text.match(/(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})/);
  if(parts){
    let [_, d, m, y] = parts;
    if(y.length === 2){ y = Number(y) > 50 ? '19' + y : '20' + y; }
    return `${y.padStart(4,'0')}-${m.padStart(2,'0')}-${d.padStart(2,'0')}`;
  }
  return text;
}

function detectExpenseCategory(remarks){
  const text = String(remarks || '').toLowerCase();
  if(!text) return 'Miscellaneous';
  const categories = (DB.settings?.categories || []).map(c => String(c||'').toLowerCase()).filter(Boolean);
  const matched = categories.find(cat => cat && text.includes(cat));
  if(matched) return matched.replace(/(^|\s)([a-z])/g, (_,p,ch) => p + ch.toUpperCase());
  if(/electric|power|bill|\beb\b/.test(text)) return 'Electricity';
  if(/water|bore\s*well|borewell|tanker|softener/.test(text)) return 'Water';
  if(/watchman|security\s*guard|guard\s*salary|\bsecurity\b/.test(text)) return 'Security';
  if(/housekeep|clean|maid|sweep|garbage|scavenger/.test(text)) return 'Housekeeping';
  if(/repair|plumb|electrician|carpenter|painter|bore\s*work|labour|labor|fix/.test(text)) return 'Repairs';
  if(/garden|landscape|plant/.test(text)) return 'Gardening';
  if(/lift|elevator/.test(text)) return 'Lift Maintenance';
  if(/cctv|camera/.test(text)) return 'CCTV';
  if(/festival|diwali|holi|celebration|puja|navratri|ganesh/.test(text)) return 'Festival';
  return 'Miscellaneous';
}

function detectPaymentMode(remarks){
  const text = String(remarks || '').toUpperCase();
  if(text.includes('UPI')) return 'UPI';
  if(text.includes('CHEQUE') || text.includes('CHQ')) return 'Cheque';
  if(text.includes('CASH')) return 'Cash';
  return 'NEFT';
}

function extractPayerName(remarks){
  const parts = String(remarks || '').split('/').map(s => s.trim()).filter(Boolean);
  if(parts.length < 2) return '';
  const raw = parts[1];
  if(!raw || /^\d+$/.test(raw)) return '';
  return raw.toLowerCase().replace(/(^|\s)([a-z])/g, (_,p,ch) => p + ch.toUpperCase());
}

const MONTH_OVERRIDE_RE = /\b(january|jan|february|feb|march|mar|april|apr|may|june|jun|july|jul|august|aug|september|sept|sep|october|oct|november|nov|december|dec)\s*mont/i;
const MONTH_ALIAS_NUM = {jan:1,january:1,feb:2,february:2,mar:3,march:3,apr:4,april:4,may:5,jun:6,june:6,jul:7,july:7,aug:8,august:8,sep:9,sept:9,september:9,oct:10,october:10,nov:11,november:11,dec:12,december:12};

function extractMonthOverride(remarks, txnDateStr){
  const text = String(remarks || '').toLowerCase();
  const m = text.match(MONTH_OVERRIDE_RE);
  if(!m) return null;
  const monthNum = MONTH_ALIAS_NUM[m[1]];
  if(!monthNum) return null;
  const txnDate = new Date(txnDateStr);
  let year = Number.isNaN(txnDate.getTime()) ? new Date().getFullYear() : txnDate.getFullYear();
  if(!Number.isNaN(txnDate.getTime()) && monthNum > (txnDate.getMonth() + 1)) year -= 1;
  return {month: MONTHS[monthNum], monthNum, year};
}

function extractFlatFromRemarks(remarks, knownFlats){
  const text = String(remarks || '');
  // Keyword-anchored patterns are tried first (highest confidence) — tolerant of missing
  // spaces/punctuation (e.g. "Flat203", "Flat-203", "Flat No.203", "F203", "#203").
  const keywordPatterns = [
    /\bflat\s*(?:no\.?|number|#|:|-)?\s*(\d{3})\b/i,
    /\bfl\s*(?:no\.?|#|:|-)?\s*(\d{3})\b/i,
    /\bunit\s*(?:no\.?|#|:|-)?\s*(\d{3})\b/i,
    /\bapt\s*(?:no\.?|#|:|-)?\s*(\d{3})\b/i,
    /\broom\s*(?:no\.?|#|:|-)?\s*(\d{3})\b/i,
    /\bhouse\s*(?:no\.?|#|:|-)?\s*(\d{3})\b/i,
    /#\s*(\d{3})\b/,
    /\b(\d{3})\s*(?:no\.?)?\s*flat\b/i
  ];
  for(const pattern of keywordPatterns){
    const m = text.match(pattern);
    if(m && m[1]) return m[1];
  }
  // Fallback: no keyword found — scan any standalone 3-digit number and accept it only if
  // it's a real flat number in the Members sheet (avoids false positives from phone/UTR digits).
  const numberMatches = text.match(/(?<!\d)(\d{3})(?!\d)/g) || [];
  for(const num of numberMatches){
    if(knownFlats.has(num)) return num;
  }
  return '';
}

function normalizeBankRemarksForDedupe(text){
  return String(text || '').trim().toLowerCase()
    .replace(/^⚠\s*verify flat\s*-\s*bank import:\s*/, '')
    .replace(/^bank import:\s*/, '');
}

function bankDedupeKey(dateStr, amount, remarks){
  const roundedAmount = Math.round((Number(amount) || 0) * 100) / 100;
  return `${String(dateStr || '').trim()}|${roundedAmount}|${normalizeBankRemarksForDedupe(remarks)}`;
}

function findMemberByFlat(flat){
  const normalizeFlat = value => String(value || '').trim().replace(/^0+/, '').toLowerCase();
  const target = normalizeFlat(flat);
  if(!target) return null;
  return DB.members.find(member => normalizeFlat(fld(member,'flat','Flat','flatNo','FlatNo','flatNumber','FlatNumber')) === target);
}

function mapBankRowsToSMMS(rows, options = {}){
  const collections = [];
  const expenses = [];
  const warnings = [];
  const flagged = [];

  const existingCollectionKeys = new Set(DB.collections.map(c =>
    bankDedupeKey(fld(c,'paymentDate','PaymentDate'), fld(c,'amount','Amount'), fld(c,'remarks','Remarks'))
  ));
  const existingExpenseKeys = new Set(DB.expenses.map(e =>
    bankDedupeKey(fld(e,'expenseDate','ExpenseDate'), fld(e,'amount','Amount'), fld(e,'remarks','Remarks'))
  ));
  const seenCollectionKeys = new Set();
  const seenExpenseKeys = new Set();

  rows.forEach(row => {
    if(row.credit && !row.debit){
      if(!options.autoCreateCollections){
        warnings.push(`Row ${row.rowIndex}: collection creation disabled by settings.`);
        return;
      }

      const key = bankDedupeKey(row.date, row.credit, row.remarks);
      if(existingCollectionKeys.has(key) || seenCollectionKeys.has(key)){
        warnings.push(`Row ${row.rowIndex}: duplicate of an existing collection, skipped.`);
        return;
      }
      seenCollectionKeys.add(key);

      const match = matchBankRowToMember(row, options.matchByFlat !== false);
      const monthOverride = extractMonthOverride(row.remarks, row.date);
      const monthNum = monthOverride ? monthOverride.monthNum : getMonthFromDate(row.date, true);
      const year = monthOverride ? monthOverride.year : getYearFromDate(row.date);
      const isMatched = !!match.memberId;

      const collection = {
        id: `${Date.now()}_${collections.length}_${Math.random().toString(36).slice(2,6)}`,
        memberId: match.memberId || '',
        memberName: isMatched ? match.memberName : (match.memberName || 'Unverified'),
        flat: match.flat || '',
        floor: match.floor || '',
        amount: row.credit,
        month: MONTHS[monthNum] || '',
        monthNum,
        year,
        paymentDate: row.date,
        paymentMode: detectPaymentMode(row.remarks),
        status: 'Paid',
        remarks: (isMatched ? 'Bank Import: ' : '⚠ VERIFY FLAT - Bank Import: ') + row.remarks,
        needsReview: !isMatched
      };

      collections.push(collection);
      if(!isMatched) flagged.push(collection);
    } else if(row.debit && !row.credit){
      if(!options.autoCreateExpenses){
        warnings.push(`Row ${row.rowIndex}: expense creation disabled by settings.`);
        return;
      }

      const key = bankDedupeKey(row.date, row.debit, row.remarks);
      if(existingExpenseKeys.has(key) || seenExpenseKeys.has(key)){
        warnings.push(`Row ${row.rowIndex}: duplicate of an existing expense, skipped.`);
        return;
      }
      seenExpenseKeys.add(key);

      const monthNum = getMonthFromDate(row.date, true);
      expenses.push({
        id: `${Date.now()}_${expenses.length}_${Math.random().toString(36).slice(2,6)}`,
        expenseDate: row.date,
        category: detectExpenseCategory(row.remarks),
        description: row.remarks || 'Expense from bank statement',
        vendor: extractPayerName(row.remarks),
        amount: row.debit,
        paymentMode: detectPaymentMode(row.remarks),
        month: MONTHS[monthNum] || '',
        monthNum,
        year: getYearFromDate(row.date),
        remarks: 'Bank Import: ' + row.remarks
      });
    } else if(row.debit && row.credit){
      warnings.push(`Row ${row.rowIndex}: both debit and credit values found. Skipping record.`);
    } else {
      warnings.push(`Row ${row.rowIndex}: no debit or credit detected. Skipping record.`);
    }
  });

  return {collections, expenses, warnings, flagged};
}

function matchBankRowToMember(row, matchByFlat = true){
  const remarks = String(row.remarks || '').trim();
  const result = {memberId:'', memberName:'', flat:'', floor:''};

  if(matchByFlat){
    const knownFlats = new Set(DB.members.map(m => String(fld(m,'flat','Flat') || '').trim()).filter(Boolean));
    const flat = extractFlatFromRemarks(remarks, knownFlats);
    if(flat){
      result.flat = flat;
      const member = findMemberByFlat(flat);
      if(member){
        result.memberName = fld(member,'name','Name','memberName','MemberName') || '';
        result.memberId = fld(member,'id','Id','memberId','MemberId') || '';
        result.floor = fld(member,'floor','Floor') || '';
        return result;
      }
    }
  }

  // Fallback: try to match the payer's UPI display name against a known member's name.
  const payerName = extractPayerName(remarks);
  if(payerName){
    const target = payerName.toLowerCase();
    const member = DB.members.find(mem => String(fld(mem,'name','Name','memberName','MemberName') || '').toLowerCase() === target);
    if(member){
      result.memberName = fld(member,'name','Name','memberName','MemberName') || '';
      result.memberId = fld(member,'id','Id','memberId','MemberId') || '';
      result.flat = fld(member,'flat','Flat') || '';
      result.floor = fld(member,'floor','Floor') || '';
      return result;
    }
    result.memberName = payerName;
  }

  return result;
}

function getMonthFromDate(dateValue, numeric = false){
  if(!dateValue) return numeric ? 0 : '';
  const date = new Date(dateValue);
  if(Number.isNaN(date.getTime())) return numeric ? 0 : '';
  return numeric ? date.getMonth() + 1 : MONTHS[date.getMonth() + 1];
}

function getYearFromDate(dateValue){
  if(!dateValue) return '';
  const date = new Date(dateValue);
  return Number.isNaN(date.getTime()) ? '' : date.getFullYear();
}

function clearBankImportState(){
  window.bankImportData = null;
  const previewEl = document.getElementById('bankPreview');
  if(previewEl) previewEl.style.display = 'none';
  showBankImportStatus('', 'info');
}
