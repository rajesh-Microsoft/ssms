// ════════════════════════════════════════════════════════════
// YuvaanSoft DevOps Center — UI.
// All data arrives through Api (js/data.js); nothing here talks to a server.
// ════════════════════════════════════════════════════════════

const $ = id => document.getElementById(id);

const state = {
  user: null,
  perms: null,
  apps: [],
  appKey: 'smms',
  view: 'dashboard',
  envId: null
};

const TIER = {
  DEV:     { cls: 'dev',     label: 'Development',          badge: 'badge-dev' },
  PREPROD: { cls: 'preprod', label: 'Pre-Production',       badge: 'badge-preprod' },
  UAT:     { cls: 'uat',     label: 'User Acceptance Testing', badge: 'badge-uat' },
  PROD:    { cls: 'prod',    label: 'Production',           badge: 'badge-prod' }
};

function esc(s){
  return String(s == null ? '' : s).replace(/[&<>"']/g, c =>
    ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
}

function when(iso){
  if(!iso) return '—';
  return new Date(iso).toLocaleString([], { day:'2-digit', month:'short', year:'numeric', hour:'2-digit', minute:'2-digit' });
}

function note(text, kind = 'info'){
  const tone = kind === 'err' ? 'alert-hazard' : 'alert-info-soft';
  $('msg').innerHTML = text ? `<div class="view pb-0"><div class="alert-soft ${tone}">${esc(text)}</div></div>` : '';
  if(text) window.scrollTo({ top: 0, behavior: 'smooth' });
}

function tierOf(env){ return TIER[env.tier] || TIER.DEV; }
function cardClass(env){ return env.hazard ? 'hazard' : tierOf(env).cls; }

// ── Auth ────────────────────────────────────────────────────
async function doLogin(){
  const username = $('username').value.trim();
  const password = $('password').value;
  $('loginBtn').disabled = true;
  try{
    state.user = await Api.login(username, password);
    state.perms = await Api.getPermissions(state.user.role);
    await startPortal();
  }catch(err){
    $('loginMsg').innerHTML = `<div class="alert-soft alert-hazard mb-3">${esc(err.message)}</div>`;
  }finally{
    $('loginBtn').disabled = false;
  }
}

function doLogout(){
  state.user = null;
  Api.logout();
  $('appView').classList.add('d-none');
  $('loginView').classList.remove('d-none');
  $('password').value = '';
}

async function startPortal(){
  $('loginView').classList.add('d-none');
  $('appView').classList.remove('d-none');

  $('userName').textContent = state.user.name;
  $('userRole').textContent = state.user.role;
  $('userInitial').textContent = state.user.name.charAt(0).toUpperCase();

  state.apps = await Api.getApplications();
  const repo = await Api.getRepository();
  $('repoChip').innerHTML = `<i class="bi bi-github me-1"></i> ${esc(repo.name)}`;
  $('branchChip').innerHTML = `<i class="bi bi-git me-1"></i> ${esc(repo.branch)}`;

  // Sample mode is stated on the login screen too, but this is the one people stare at.
  const banner = $('mockBanner');
  if(Api.isLive() && Api.authConfigured()){
    banner.classList.add('live');
    banner.innerHTML = '<i class="bi bi-check-circle-fill me-2"></i>' +
      '<strong>LIVE.</strong> State is read from each box and deployment runs deploy/promote.ps1 with its gates intact.';
  } else if(Api.isLive()){
    banner.classList.add('live');
    banner.innerHTML = '<i class="bi bi-check-circle-fill me-2"></i>' +
      '<strong>LIVE READ-ONLY DATA.</strong> No operators are configured, so deployment is refused.';
  }
  document.querySelector('.sidebar').style.paddingTop = '38px';
  document.querySelector('.content').style.paddingTop = '38px';

  renderAppNav();
  setView('dashboard');
}

// ── Navigation ──────────────────────────────────────────────
function renderAppNav(){
  $('appNav').innerHTML = state.apps.map(a => `
    <a class="nav-row ${a.key === state.appKey ? 'active' : ''} ${a.active ? '' : 'disabled'}"
       data-app="${esc(a.key)}" title="${esc(a.description)}">
      <i class="bi ${esc(a.icon)}"></i> ${esc(a.name)}
      ${a.active ? '' : '<span class="tag">soon</span>'}
    </a>`).join('');
}

function setView(view, opts = {}){
  state.view = view;
  if(opts.envId !== undefined) state.envId = opts.envId;
  note('');

  document.querySelectorAll('.nav-row[data-view]').forEach(el =>
    el.classList.toggle('active', el.dataset.view === view));

  const titles = {
    dashboard: 'Dashboard', environment: 'Environment', deploy: 'Deployment Center',
    history: 'Deployment History', servers: 'Servers', notes: 'Deployment Notes'
  };
  $('viewTitle').textContent = titles[view] || view;
  $('sidebar').classList.remove('open');

  // Probing shells out to ssh/az, so a view can take seconds. Clear first: leaving the
  // previous screen under a new title is how someone reads the wrong environment.
  $('view').innerHTML = '<div class="text-muted small"><span class="spinner-border spinner-border-sm me-2"></span>Reading from the servers…</div>';

  ({ dashboard: viewDashboard, environment: viewEnvironment, deploy: viewDeploy,
     history: viewHistory, servers: viewServers, notes: viewNotes }[view] || viewDashboard)();
}

function currentApp(){ return state.apps.find(a => a.key === state.appKey); }

function setBanner(env){
  const el = $('envBanner');
  if(!env){ el.className = 'env-banner d-none'; el.innerHTML = ''; return; }
  const t = tierOf(env);
  const hazard = env.hazard;
  el.className = `env-banner ${hazard ? 'hazard' : t.cls}`;
  el.innerHTML = hazard
    ? `<i class="bi bi-exclamation-triangle-fill"></i> Do not use — this host serves real data`
    : `<i class="bi bi-shield-fill-check"></i> ${esc(t.label)} — ${esc(env.hostname)}`;
}

// ── Dashboard ───────────────────────────────────────────────
function viewDashboard(){
  setBanner(null);
  const app = currentApp();
  if(!app.active){
    $('view').innerHTML = `<div class="alert-soft alert-info-soft">
      <strong>${esc(app.name)}</strong> has no environments yet. The portal is built to hold
      Development, Pre-Production, UAT and Production for every application, so this fills in
      when the product is provisioned.</div>`;
    return;
  }

  const cards = app.environments.map(env => {
    const t = tierOf(env);
    const badgeCls = env.hazard ? 'badge-prod' : t.badge;
    return `
    <div class="glass env-card ${cardClass(env)}" data-env="${esc(env.id)}">
      <div class="d-flex justify-content-between align-items-start gap-2">
        <div>
          <h3>${esc(env.name)}</h3>
          <div class="env-host">${esc(env.hostname)}</div>
        </div>
        <span class="badge-soft ${badgeCls}">${esc(env.badge)}</span>
      </div>

      <dl class="kv">
        <dt>Status</dt><dd><span class="dot ${esc(env.statusTone)}"></span>${esc(env.status)}</dd>
        <dt>Commit</dt><dd><code>${esc(env.commit)}</code> ${env.signedOff ? '<span class="badge-soft badge-dev ms-1">SIGNED OFF</span>' : ''}
          ${Number.isInteger(env.commitsBehind) && env.commitsBehind > 0
            ? `<span class="badge-soft badge-preprod ms-1">${env.commitsBehind} BEHIND</span>` : ''}</dd>
        <dt>Message</dt><dd>${esc(env.commitMessage)}</dd>
        <dt>Image</dt><dd class="mono">${esc(env.image)}</dd>
        <dt>Deployed</dt><dd>${when(env.deployedAt)}</dd>
        <dt>By</dt><dd>${esc(env.deployedBy)}</dd>
        <dt>Health</dt><dd>${esc(env.health)}</dd>
        <dt>Database</dt><dd>${esc(env.database)}</dd>
        <dt>Data</dt><dd>${esc(env.dataSensitivity)}</dd>
      </dl>

      ${env.hazard ? `<div class="alert-soft alert-hazard mt-3">
        <i class="bi bi-exclamation-triangle-fill me-1"></i>${esc(env.note)}</div>` : ''}

      ${env.probeState && env.probeState !== 'ok' && !env.hazard ? `<div class="alert-soft alert-info-soft mt-3">
        <i class="bi bi-question-circle me-1"></i>Could not read this box: ${esc(env.probeError || 'unknown reason')}</div>` : ''}

      <div class="d-flex gap-2 mt-3">
        <button class="btn btn-sm btn-outline-light" data-open="${esc(env.id)}">Details</button>
        ${env.deploymentEnabled
          ? `<button class="btn btn-sm btn-primary" data-deploy="${esc(env.id)}">Deploy</button>`
          : `<button class="btn btn-sm btn-outline-secondary" disabled title="Deployment is disabled here">Deploy</button>`}
      </div>
    </div>`;
  }).join('');

  $('view').innerHTML = `
    <div class="alert-soft alert-info-soft mb-4">
      <i class="bi bi-signpost-split me-1"></i>
      Dev hostnames are <code>dev-&lt;tenant&gt;.yuvaansoft.shop</code>.
      Anything of the form <code>dev-*.ssms.yuvaansoft.shop</code> is <strong>not</strong> development —
      it reaches the stack holding real data.
    </div>
    <div class="card-grid">${cards}</div>`;
}

// ── Environment detail ──────────────────────────────────────
async function viewEnvironment(){
  const env = await Api.getEnvironment(state.appKey, state.envId);
  setBanner(env);
  const t = tierOf(env);

  const rows = pairs => pairs.map(([k, v]) => `<dt>${esc(k)}</dt><dd>${v}</dd>`).join('');

  $('view').innerHTML = `
    <button class="btn btn-sm btn-outline-light mb-3" data-view-go="dashboard">
      <i class="bi bi-arrow-left"></i> Back
    </button>

    ${env.hazard ? `<div class="alert-soft alert-hazard mb-3">
      <i class="bi bi-exclamation-triangle-fill me-1"></i>${esc(env.note)}</div>` : ''}

    <div class="row g-3">
      <div class="col-lg-7">
        <div class="glass p-3">
          <div class="section-title mt-0">Build</div>
          <dl class="kv">${rows([
            ['Environment', `${esc(env.name)} <span class="badge-soft ${env.hazard ? 'badge-prod' : t.badge} ms-1">${esc(env.badge)}</span>`],
            ['Hostname', esc(env.hostname)],
            ['Host pattern', `<code>${esc(env.hostPattern)}</code>`],
            ['Purpose', esc(env.purpose)],
            ['Commit', `<code>${esc(env.commit)}</code>`],
            ['Commit message', esc(env.commitMessage)],
            ['Commit author', esc(env.commitAuthor || '—')],
            ['Behind branch', Number.isInteger(env.commitsBehind)
                ? (env.commitsBehind === 0 ? 'up to date' : `${env.commitsBehind} commit(s)`)
                : 'unknown (sha not in this clone)'],
            ['Branch', esc(env.branch)],
            ['Signed off', env.signedOff ? 'Yes' : 'No'],
            ['Docker image', `<span class="mono">${esc(env.image)}</span>`],
            ['Rollback image', `<span class="mono">${esc(env.rollbackImage || '—')}</span>`],
            ['Pipeline', `<code>${esc(env.pipeline)}</code>`],
            ['Deployed', when(env.deployedAt)],
            ['Duration', esc(env.deployDuration)],
            ['Deployed by', esc(env.deployedBy)],
            ['Migration', `<span class="mono">${esc(env.migration)}</span>`],
            ['Storage', esc(env.storageUsed)],
            ['Release notes', esc(env.releaseNotes)]
          ])}</dl>
        </div>

        <div class="glass p-3 mt-3">
          <div class="section-title mt-0">Container logs</div>
          <div class="logbox">${env.logs.length ? env.logs.map(esc).join('\n') : 'No logs in sample data.'}</div>
        </div>
      </div>

      <div class="col-lg-5">
        <div class="glass p-3">
          <div class="section-title mt-0">Runtime</div>
          <dl class="kv">${rows([
            ['Status', `<span class="dot ${esc(env.statusTone)}"></span>${esc(env.status)}`],
            ['Health', esc(env.health)],
            ['Server', esc(env.server)],
            ['Database', esc(env.database)],
            ['Data', esc(env.dataSensitivity)]
          ])}</dl>
          <div class="section-title">Containers</div>
          ${env.containers.length
            ? `<table class="table table-sm mb-0"><tbody>${env.containers.map(c =>
                `<tr><td class="mono">${esc(c.name)}</td><td>${esc(c.status)}</td></tr>`).join('')}</tbody></table>`
            : '<div class="text-muted small">None reported.</div>'}
        </div>

        <div class="glass p-3 mt-3">
          <div class="section-title mt-0">Environment variables</div>
          ${env.envVars.length
            ? `<table class="table table-sm mb-0"><tbody>${env.envVars.map(v =>
                `<tr><td class="mono">${esc(v.key)}</td><td class="mono">${esc(v.value)}${v.secret ? ' <span class="badge-soft badge-muted ms-1">SECRET</span>' : ''}</td></tr>`).join('')}</tbody></table>`
            : '<div class="text-muted small">None.</div>'}
        </div>

        <div class="glass p-3 mt-3">
          <div class="section-title mt-0">Quick links</div>
          <div class="quick-links">
            ${env.appUrl ? `<a class="btn btn-sm btn-outline-light" target="_blank" rel="noopener" href="${esc(env.appUrl)}"><i class="bi bi-box-arrow-up-right me-1"></i>Website</a>` : ''}
            ${env.apiUrl && env.apiUrl !== '—' ? `<a class="btn btn-sm btn-outline-light" target="_blank" rel="noopener" href="${esc(env.apiUrl)}"><i class="bi bi-activity me-1"></i>API health</a>` : ''}
            <a class="btn btn-sm btn-outline-light" target="_blank" rel="noopener" href="https://github.com/rrathore_microsoft/SMMS"><i class="bi bi-github me-1"></i>Repository</a>
            <a class="btn btn-sm btn-outline-light" target="_blank" rel="noopener" href="https://portal.azure.com"><i class="bi bi-cloud me-1"></i>Azure Portal</a>
          </div>
        </div>

        <div class="glass p-3 mt-3">
          <div class="section-title mt-0">Actions</div>
          ${env.deploymentEnabled
            ? `<button class="btn btn-primary w-100 mb-2" data-deploy="${esc(env.id)}"><i class="bi bi-rocket-takeoff me-1"></i>Deploy a build</button>`
            : `<button class="btn btn-outline-secondary w-100 mb-2" disabled>Deployment disabled here</button>`}
          ${state.perms.rollback && env.rollbackCommit
            ? `<button class="btn btn-outline-warning w-100" data-rollback="${esc(env.id)}">
                 <i class="bi bi-arrow-counterclockwise me-1"></i>Roll back to ${esc(env.rollbackCommit)}</button>`
            : `<button class="btn btn-outline-secondary w-100" disabled>Rollback needs Administrator</button>`}
        </div>
      </div>
    </div>`;
}

// ── Deployment Center ───────────────────────────────────────
async function viewDeploy(){
  setBanner(null);
  const app = currentApp();
  const builds = await Api.getBuilds();
  const envs = app.environments.filter(e => e.deploymentEnabled);

  $('view').innerHTML = `
    <div class="glass p-4" style="max-width:760px;">
      <div class="section-title mt-0">Deploy a build</div>

      <label class="form-label">Application</label>
      <input class="form-control mb-3" value="${esc(app.name)}" disabled/>

      <label class="form-label" for="depEnv">Environment</label>
      <select class="form-select mb-3" id="depEnv">
        ${envs.map(e => `<option value="${esc(e.id)}">${esc(e.name)} — ${esc(e.hostname)}</option>`).join('')}
      </select>

      <label class="form-label" for="depBuild">Build</label>
      <select class="form-select mb-3" id="depBuild">
        ${builds.map((b, i) => `<option value="${esc(b.commit)}">${esc(b.commit)} — ${esc(b.message)}${i === 0 ? ' (latest)' : ''}</option>`).join('')}
      </select>

      <div id="depWarn"></div>
      <button class="btn btn-primary" id="depGo"><i class="bi bi-rocket-takeoff me-1"></i>Deploy</button>
    </div>`;

  const refreshWarn = () => {
    const env = app.environments.find(e => e.id === $('depEnv').value);
    if(!env) return;
    const t = tierOf(env);
    const gate = env.requiresSignOffFrom
      ? `<div class="mt-2"><i class="bi bi-shield-lock me-1"></i>Requires a ${esc(env.requiresSignOffFrom)} sign-off at the same commit.</div>` : '';
    const allowed = state.perms.deploy.includes(env.tier);
    $('depWarn').innerHTML = `
      <div class="alert-soft ${env.tier === 'DEV' ? 'alert-info-soft' : 'alert-hazard'} mb-3">
        <strong>${esc(t.label)}</strong> — ${esc(env.dataSensitivity)}${gate}
        ${allowed ? '' : `<div class="mt-2"><i class="bi bi-x-octagon me-1"></i>Your role (${esc(state.user.role)}) cannot deploy here.</div>`}
      </div>`;
    $('depGo').disabled = !allowed;
  };
  $('depEnv').addEventListener('change', refreshWarn);
  refreshWarn();

  $('depGo').addEventListener('click', () => {
    const env = app.environments.find(e => e.id === $('depEnv').value);
    askDeploy(env, $('depBuild').value);
  });
}

// ── History ─────────────────────────────────────────────────
async function viewHistory(){
  setBanner(null);
  const history = await Api.getHistory();

  if(!history.length){
    $('view').innerHTML = `<div class="alert-soft alert-info-soft">
      <i class="bi bi-clock-history me-1"></i>
      No deployments recorded yet. Each box now appends to <code>DEPLOYED_HISTORY</code> when
      <code>promote.ps1</code> deploys to it, so this fills in from the next deployment onwards.
      Earlier deployments were never recorded and cannot be recovered.
    </div>`;
    return;
  }

  const counts = history.reduce((acc, h) => (acc[h.env] = (acc[h.env] || 0) + 1, acc), {});
  const max = Math.max(...Object.values(counts), 1);
  const bars = Object.entries(counts).map(([env, n]) => `
    <div class="bar">
      <div class="fill ${esc((TIER[env] || TIER.DEV).cls)}" style="height:${Math.round(n / max * 100)}%"></div>
      <div class="lab">${esc(env)} · ${n}</div>
    </div>`).join('');

  $('view').innerHTML = `
    <div class="glass p-3 mb-3">
      <div class="section-title mt-0">Deployments by environment</div>
      <div class="chart">${bars}</div>
    </div>

    <div class="glass p-3">
      <div class="section-title mt-0">History</div>
      <div class="table-responsive">
        <table class="table table-sm align-middle mb-0">
          <thead><tr><th>Commit</th><th>App</th><th>Environment</th><th>Message</th><th>When</th><th>By</th><th>Status</th><th></th></tr></thead>
          <tbody>${history.map(h => `
            <tr>
              <td><code>${esc(h.commit)}</code></td>
              <td>${esc(h.app)}</td>
              <td><span class="badge-soft ${esc((TIER[h.env] || TIER.DEV).badge)}">${esc(h.env)}</span></td>
              <td>${esc(h.message)}</td>
              <td>${when(h.at)}</td>
              <td>${esc(h.by)}</td>
              <td><span class="dot ok"></span>${esc(h.status)}</td>
              <td>${state.perms.rollback
                    ? `<button class="btn btn-sm btn-outline-warning" data-rollback-commit="${esc(h.commit)}" data-rollback-env="${esc(h.env)}">Roll back</button>`
                    : ''}</td>
            </tr>`).join('')}
          </tbody>
        </table>
      </div>
    </div>`;
}

// ── Servers ─────────────────────────────────────────────────
async function viewServers(){
  setBanner(null);
  const servers = await Api.getServers();
  $('view').innerHTML = `<div class="card-grid">${servers.map(s => `
    <div class="glass p-3">
      <h3 class="h6 mb-1">${esc(s.name)}</h3>
      <div class="env-host mb-3">${esc(s.ip)} · ${esc(s.role)}</div>
      ${[['CPU', s.cpu], ['RAM', s.ram], ['Disk', s.disk]].map(([label, v]) => `
        <div class="d-flex justify-content-between small mb-1"><span>${label}</span><span>${v}%</span></div>
        <div class="meter ${v > 75 ? 'hot' : ''} mb-3"><span style="width:${v}%"></span></div>`).join('')}
      <dl class="kv">
        <dt>Docker</dt><dd><span class="dot ok"></span>${esc(s.docker)}</dd>
        <dt>SQL Server</dt><dd><span class="dot ok"></span>${esc(s.sql)}</dd>
        <dt>nginx</dt><dd><span class="dot ok"></span>${esc(s.nginx)}</dd>
        <dt>SSL</dt><dd>${esc(s.ssl)}</dd>
        <dt>SSH</dt><dd>${esc(s.sshNote)}</dd>
      </dl>
    </div>`).join('')}</div>`;
}

// ── Notes ───────────────────────────────────────────────────
async function viewNotes(){
  setBanner(null);
  const notes = await Api.getNotes();
  $('view').innerHTML = `
    <div class="glass p-4" style="max-width:820px;">
      <label class="form-label" for="nToday">Today's deployment notes</label>
      <textarea class="form-control mb-3" id="nToday">${esc(notes.today)}</textarea>
      <label class="form-label" for="nIssues">Known issues</label>
      <textarea class="form-control mb-3" id="nIssues">${esc(notes.knownIssues)}</textarea>
      <label class="form-label" for="nPending">Pending tasks</label>
      <textarea class="form-control mb-3" id="nPending">${esc(notes.pending)}</textarea>
      <button class="btn btn-primary" id="nSave">Save notes</button>
    </div>`;

  $('nSave').addEventListener('click', async () => {
    await Api.saveNotes({ today: $('nToday').value, knownIssues: $('nIssues').value, pending: $('nPending').value });
    note('Notes saved for this session. They will persist once the backend is wired.');
  });
}

// ── Confirmation ────────────────────────────────────────────
let confirmAction = null;
const confirmModal = () => bootstrap.Modal.getOrCreateInstance($('confirmModal'));

function ask(title, bodyHtml, onYes, confirmWord){
  $('confirmTitle').textContent = title;
  $('confirmBody').innerHTML = bodyHtml + (confirmWord
    ? `<label class="form-label mt-2" for="confirmWord">Type <strong>${esc(confirmWord)}</strong> to continue</label>
       <input class="form-control" id="confirmWord" autocomplete="off" spellcheck="false"/>`
    : '');
  confirmAction = onYes;
  $('confirmGo').dataset.word = confirmWord || '';
  confirmModal().show();
}

function askDeploy(env, commit){
  if(!env || !env.deploymentEnabled) return note('Deployment is disabled for that environment.', 'err');
  if(!state.perms.deploy.includes(env.tier)){
    return note(`Your role (${state.user.role}) cannot deploy to ${env.tier}.`, 'err');
  }
  const t = tierOf(env);
  ask(`Deploy to ${t.label}?`, `
    <div class="alert-soft ${env.tier === 'DEV' ? 'alert-info-soft' : 'alert-hazard'} mb-3">
      You are about to deploy <code>${esc(commit)}</code> to
      <strong>${esc(env.name)}</strong> (${esc(env.hostname)}).
      <div class="mt-2">${esc(env.dataSensitivity)}</div>
    </div>
    <p class="mb-0 small text-muted">Deployment runs <code>${esc(env.pipeline)}</code> on the server,
    so its own gates still apply: a clean tree, the commit on origin/main, and the previous
    environment signed off at the same commit.</p>`,
    word => runDeploy(env, commit, word),
    env.tier === 'DEV' ? null : env.name);
}

function askRollback(env){
  ask('Roll back?', `
    <div class="alert-soft alert-hazard mb-3">
      <strong>${esc(env.name)}</strong> (${esc(env.hostname)}) will be put back on the previous image.
      <div class="mt-2">${esc(env.dataSensitivity)}</div>
    </div>
    <p class="mb-0 small text-muted">Rollback re-tags the newest
    <span class="mono">rollback-*</span> image on the box and recreates the API container.</p>`,
    word => runRollback(env, word),
    env.name);
}

async function runDeploy(env, commit, confirmWord){
  try{
    const { jobId } = await Api.deploy({ envId: env.id, commit, confirm: confirmWord });
    note(`Deployment of ${commit} to ${env.name} started.`);
    watchJob(jobId);
  }catch(err){
    note(err.message, 'err');
  }
}

async function runRollback(env, confirmWord){
  try{
    const { jobId } = await Api.rollback({ envId: env.id, confirm: confirmWord });
    note(`Rollback of ${env.name} started.`);
    watchJob(jobId);
  }catch(err){
    note(err.message, 'err');
  }
}

// Follows a running job. The output is whatever promote.ps1 printed, unedited.
async function watchJob(jobId){
  const host = $('view');
  host.innerHTML = `
    <div class="glass p-3">
      <div class="section-title mt-0">Job <span class="mono">${esc(jobId)}</span>
        <span class="badge-soft badge-muted ms-2" id="jobStatus">running</span></div>
      <div class="logbox" id="jobOutput">starting…</div>
    </div>`;

  for(;;){
    let job;
    try{ job = await Api.getJob(jobId); }
    catch(err){ note(err.message, 'err'); return; }

    const out = $('jobOutput');
    if(!out) return;                                     // the user navigated away
    out.textContent = (job.output || []).join('\n');
    out.scrollTop = out.scrollHeight;

    const badge = $('jobStatus');
    badge.textContent = job.status;
    badge.className = 'badge-soft ms-2 ' +
      (job.status === 'succeeded' ? 'badge-dev' : job.status === 'failed' ? 'badge-prod' : 'badge-muted');

    if(job.status !== 'running'){
      if(job.status === 'succeeded') note('Finished.');
      else note(job.error || 'The job failed. The output above is unedited.', 'err');
      return;
    }
    await new Promise(r => setTimeout(r, 1500));
  }
}

// ── Wiring ──────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
  Api.init().then(live => {
    const note = $('loginMsg');
    if(live && note){
      note.innerHTML = '<div class="alert-soft alert-info-soft mb-3">Connected to the read-only backend.</div>';
    }
  });

  $('loginBtn').addEventListener('click', doLogin);
  $('password').addEventListener('keydown', e => { if(e.key === 'Enter') doLogin(); });
  $('logoutBtn').addEventListener('click', doLogout);
  $('menuBtn').addEventListener('click', () => $('sidebar').classList.toggle('open'));

  $('confirmGo').addEventListener('click', () => {
    const required = $('confirmGo').dataset.word;
    if(required){
      const typed = ($('confirmWord')?.value || '').trim();
      if(typed.toLowerCase() !== required.toLowerCase()){
        $('confirmWord').classList.add('is-invalid');
        return;                                          // keep the dialog open
      }
    }
    // Blur first: hiding a modal that still holds focus trips an aria-hidden warning.
    $('confirmGo').blur();
    confirmModal().hide();
    const action = confirmAction;
    confirmAction = null;
    if(action) action(required || null);
  });

  document.addEventListener('click', async e => {
    const app = e.target.closest('[data-app]');
    if(app){
      const chosen = state.apps.find(a => a.key === app.dataset.app);
      if(!chosen.active) return note(`${chosen.name} has no environments yet.`);
      state.appKey = chosen.key;
      renderAppNav();
      return setView('dashboard');
    }

    const nav = e.target.closest('.nav-row[data-view]');
    if(nav) return setView(nav.dataset.view);

    const back = e.target.closest('[data-view-go]');
    if(back) return setView(back.dataset.viewGo);

    const open = e.target.closest('[data-open]') || e.target.closest('.env-card');
    const deployBtn = e.target.closest('[data-deploy]');
    const rollbackBtn = e.target.closest('[data-rollback]');
    const rollbackRow = e.target.closest('[data-rollback-commit]');

    if(deployBtn){
      e.stopPropagation();
      const env = currentApp().environments.find(x => x.id === deployBtn.dataset.deploy);
      const builds = await Api.getBuilds();
      return askDeploy(env, builds[0].commit);
    }
    if(rollbackBtn){
      e.stopPropagation();
      const env = currentApp().environments.find(x => x.id === rollbackBtn.dataset.rollback);
      return askRollback(env);
    }
    if(rollbackRow){
      const env = currentApp().environments.find(x => x.tier === rollbackRow.dataset.rollbackEnv);
      if(!env) return note('That environment is not configured for rollback.', 'err');
      return askRollback({ ...env, rollbackCommit: rollbackRow.dataset.rollbackCommit });
    }
    if(open){
      const id = open.dataset.open || open.dataset.env;
      if(id) return setView('environment', { envId: id });
    }
  });
});
