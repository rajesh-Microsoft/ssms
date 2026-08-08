// ════════════════════════════════════════════════════════════
// YuvaanSoft DevOps Center — data layer.
//
// Everything the UI renders comes through `Api` below. Today it is served from
// the SAMPLE object in this file; swapping in the ASP.NET Core backend means
// replacing the body of each Api method with a fetch() and nothing else.
//
// SAMPLE.meta.live MUST stay false until the calls are real. The whole point of
// this portal is to stop people acting on the wrong environment, so the UI shows
// a standing warning while the numbers are made up.
// ════════════════════════════════════════════════════════════

const SAMPLE = {
  meta: {
    live: false,                       // flip to true only when Api talks to a real backend
    source: 'Sample data bundled with the portal',
    // Facts below were true on 2026-08-08; they are a plausible snapshot, not a live read.
    snapshotTaken: '2026-08-08T09:55:00Z'
  },

  // Demo sign-in only. The real portal will use Microsoft Entra ID and this list disappears.
  users: [
    { username: 'dev',    password: 'dev',    name: 'Developer',     role: 'Developer' },
    { username: 'tester', password: 'tester', name: 'Tester',        role: 'Tester' },
    { username: 'admin',  password: 'admin',  name: 'Administrator', role: 'Administrator' }
  ],

  // What each role may do. Deploying anywhere always needs confirmation on top of this.
  permissions: {
    Developer:     { deploy: ['DEV'],                       rollback: false, logs: true },
    Tester:        { deploy: ['DEV', 'UAT'],                rollback: false, logs: true },
    Administrator: { deploy: ['DEV', 'PREPROD', 'UAT', 'PROD'], rollback: true,  logs: true }
  },

  applications: [
    {
      key: 'smms',
      name: 'SMMS',
      icon: 'bi-buildings',
      description: 'Society Maintenance Management',
      active: true,
      environments: [
        {
          id: 'smms-dev',
          tier: 'DEV',
          name: 'Development',
          hostname: 'dev-demo.yuvaansoft.shop',
          hostPattern: 'dev-<tenant>.yuvaansoft.shop',
          purpose: 'Current development build',
          status: 'Running',
          statusTone: 'ok',
          badge: 'ACTIVE DEV',
          deploymentEnabled: true,
          dataSensitivity: 'Cloned production data',
          commit: '70fefa4',
          commitMessage: 'Make the caretaker pick a flat instead of typing one',
          branch: 'main',
          signedOff: true,
          image: 'smms-dev-smms-api:latest',
          rollbackImage: 'smms-dev-smms-api:rollback-20260808T094820Z',
          rollbackCommit: '38a1028',
          containers: [
            { name: 'smms-dev-api', status: 'Up 52 minutes' },
            { name: 'smms-dev-ui',  status: 'Up 3 hours' },
            { name: 'smms-dev-sql', status: 'Up 3 hours' }
          ],
          deployedAt: '2026-08-08T09:48:20Z',
          deployedBy: 'RajMSFT',
          health: 'Healthy',
          appUrl: 'https://dev-demo.yuvaansoft.shop/',
          apiUrl: 'https://dev-demo.yuvaansoft.shop/api/health',
          database: 'SmmsDb_<tenant> (7 tenants)',
          server: 'ssms-webserver (135.235.195.132)',
          pipeline: 'deploy/promote.ps1 -Environment dev',
          deployDuration: '1m 12s',
          migration: '20260807171241_AddCaretakerOperations',
          storageUsed: '15.2 MB archive',
          releaseNotes: 'Caretaker gate features, Gate Log tab, UTC timestamps, My Gate, flat dropdown.',
          envVars: [
            { key: 'ASPNETCORE_ENVIRONMENT', value: 'Development' },
            { key: 'Jwt__Issuer', value: 'SMMS.Api.Dev' },
            { key: 'Jwt__Key', value: '••••••••', secret: true },
            { key: 'ControlPlane__TenantConnectionTemplate', value: '••••••••', secret: true }
          ],
          logs: [
            '[09:48:26] Database is ready for society demo',
            '[09:48:27] Database is ready for society aadya',
            '[09:48:28] Application started. Listening on http://[::]:8080',
            '[09:49:02] Gate log queried by admin'
          ]
        },
        {
          id: 'smms-pprod',
          tier: 'PREPROD',
          name: 'Pre-Production',
          hostname: 'pprod-aadya.ssms.yuvaansoft.shop',
          hostPattern: 'pprod-<tenant>.ssms.yuvaansoft.shop',
          purpose: 'Release candidate verification',
          status: 'Running',
          statusTone: 'warn',
          badge: 'PRE-PROD',
          deploymentEnabled: true,
          requiresSignOffFrom: 'DEV',
          dataSensitivity: 'Full clone of production, real PII',
          commit: '0912c8c',
          commitMessage: 'Let an admin actually pick the Caretaker role',
          branch: 'main',
          signedOff: false,
          image: 'smms-pprod-smms-api:latest',
          rollbackImage: 'smms-pprod-smms-api:rollback-20260807T233253Z',
          rollbackCommit: 'e332474',
          containers: [
            { name: 'smms-pprod-api', status: 'Up 8 hours' },
            { name: 'smms-pprod-ui',  status: 'Up 13 hours' },
            { name: 'smms-pprod-sql', status: 'Up 24 hours' }
          ],
          deployedAt: '2026-08-07T23:32:53Z',
          deployedBy: 'RajMSFT',
          health: 'Healthy',
          appUrl: 'https://pprod-aadya.ssms.yuvaansoft.shop/',
          apiUrl: 'https://pprod-aadya.ssms.yuvaansoft.shop/api/health',
          database: 'SmmsDb_<tenant> (7 tenants)',
          server: 'smms-pprod-vm (20.219.1.173)',
          pipeline: 'deploy/promote.ps1 -Environment pprod',
          deployDuration: '3m 40s',
          migration: '20260807171241_AddCaretakerOperations',
          storageUsed: '15.2 MB archive',
          releaseNotes: 'Caretaker app for the gate, admin role picker.',
          note: 'SSH from a workstation is blocked; this box is driven through az vm run-command.',
          envVars: [
            { key: 'ASPNETCORE_ENVIRONMENT', value: 'Production' },
            { key: 'Jwt__Issuer', value: 'SMMS.Api.PProd' },
            { key: 'Jwt__Key', value: '••••••••', secret: true }
          ],
          logs: [
            '[23:33:01] Database is ready for society aadya',
            '[23:33:04] Application started. Listening on http://[::]:8080'
          ]
        },
        {
          id: 'smms-uat',
          tier: 'UAT',
          name: 'UAT',
          hostname: 'demo.yuvaansoft.shop',
          hostPattern: '<tenant>.yuvaansoft.shop',
          purpose: 'User acceptance testing — societies with real balances',
          status: 'Running',
          statusTone: 'info',
          badge: 'UAT',
          deploymentEnabled: true,
          requiresSignOffFrom: 'PREPROD',
          dataSensitivity: 'REAL resident data and real money records',
          commit: '0912c8c',
          commitMessage: 'Let an admin actually pick the Caretaker role',
          branch: 'main',
          signedOff: false,
          image: 'smms-smms-api:latest',
          rollbackImage: 'smms-smms-api:rollback-20260807T222922Z',
          rollbackCommit: 'e93f670',
          containers: [
            { name: 'smms-api', status: 'Up 5 days' },
            { name: 'smms-ui',  status: 'Up 5 days' },
            { name: 'sqlserver', status: 'Up 5 days' }
          ],
          deployedAt: '2026-08-07T22:29:22Z',
          deployedBy: 'RajMSFT',
          health: 'Healthy',
          appUrl: 'https://demo.yuvaansoft.shop/',
          apiUrl: 'https://demo.yuvaansoft.shop/api/health',
          database: 'SmmsDb_<tenant> (7 tenants)',
          server: 'ssms-webserver (135.235.195.132)',
          pipeline: 'deploy/promote.ps1 -Environment uat',
          deployDuration: '2m 05s',
          migration: '20260802101758_AddLiabilitySettlementReference',
          storageUsed: '15.2 MB archive',
          releaseNotes: 'Caretaker app for the gate, admin role picker.',
          envVars: [
            { key: 'ASPNETCORE_ENVIRONMENT', value: 'Development' },
            { key: 'Jwt__Issuer', value: 'SMMS.Api' },
            { key: 'Jwt__Key', value: '••••••••', secret: true }
          ],
          logs: [
            '[22:29:30] Database is ready for society aadya',
            '[22:29:33] Application started. Listening on http://[::]:8080'
          ]
        },
        {
          // Kept deliberately: this hostname LOOKS like dev and is not. Showing it as a
          // hazard is the point — hiding it is what let someone sign in here by mistake.
          id: 'smms-lookalike',
          tier: 'UAT',
          name: 'Look-alike host (do not use)',
          hostname: 'dev-aadya.ssms.yuvaansoft.shop',
          hostPattern: 'dev-*.ssms.yuvaansoft.shop',
          purpose: 'Mis-typed host that falls through to the UAT/production stack',
          status: 'Hazard',
          statusTone: 'danger',
          badge: 'DO NOT USE',
          deploymentEnabled: false,
          hazard: true,
          dataSensitivity: 'REAL resident data — this is NOT a development environment',
          commit: '0912c8c',
          commitMessage: 'Serves the UAT/production build',
          branch: 'main',
          signedOff: false,
          image: 'smms-smms-api:latest',
          containers: [],
          deployedAt: '2026-08-07T22:29:22Z',
          deployedBy: '—',
          health: 'Serving UAT',
          appUrl: 'https://dev-aadya.ssms.yuvaansoft.shop/',
          apiUrl: '—',
          database: 'SmmsDb_Aadya (real data)',
          server: 'ssms-webserver (135.235.195.132)',
          pipeline: '—',
          deployDuration: '—',
          migration: '—',
          storageUsed: '—',
          releaseNotes: 'None. This is not a deployable environment.',
          note: 'The dev hostname scheme is dev-<tenant>.yuvaansoft.shop with no ".ssms.". ' +
                'Anything matching dev-*.ssms.* reaches the stack holding real data. ' +
                'Recommended fix: reject this pattern at nginx so it stops resolving.',
          envVars: [],
          logs: []
        },
        {
          id: 'smms-prod',
          tier: 'PROD',
          name: 'Production',
          hostname: '(not provisioned)',
          hostPattern: '—',
          purpose: 'Awaiting a business-owned subscription',
          status: 'Not provisioned',
          statusTone: 'muted',
          badge: 'PRODUCTION',
          deploymentEnabled: false,
          dataSensitivity: 'REAL customer data',
          commit: '—',
          commitMessage: '—',
          branch: '—',
          signedOff: false,
          image: '—',
          containers: [],
          deployedAt: null,
          deployedBy: '—',
          health: 'Unknown',
          appUrl: '',
          apiUrl: '',
          database: '—',
          server: '—',
          pipeline: '—',
          deployDuration: '—',
          migration: '—',
          storageUsed: '—',
          releaseNotes: '—',
          note: 'Production must not run on the current subscription.',
          envVars: [],
          logs: []
        }
      ]
    },
    { key: 'cms',       name: 'Clinic Management', icon: 'bi-heart-pulse',   description: 'Clinic Management System', active: false, environments: [] },
    { key: 'schoolerp', name: 'School ERP',        icon: 'bi-mortarboard',   description: 'School ERP',               active: false, environments: [] },
    { key: 'hrms',      name: 'HRMS',              icon: 'bi-people',        description: 'Human Resources',          active: false, environments: [] },
    { key: 'inventory', name: 'Inventory',         icon: 'bi-box-seam',      description: 'Inventory Management',     active: false, environments: [] },
    { key: 'crm',       name: 'CRM',               icon: 'bi-graph-up-arrow', description: 'Customer Relationships',  active: false, environments: [] }
  ],

  history: [
    { commit: '70fefa4', app: 'SMMS', env: 'DEV',     message: 'Make the caretaker pick a flat instead of typing one', at: '2026-08-08T09:48:20Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: '38a1028', app: 'SMMS', env: 'DEV',     message: 'Tell the resident what is waiting at the gate',        at: '2026-08-08T09:33:30Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: '0c298b2', app: 'SMMS', env: 'DEV',     message: 'Send timestamps as UTC so the browser shows local',    at: '2026-08-08T09:04:31Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: '6ffd7be', app: 'SMMS', env: 'DEV',     message: 'Show the committee what happens at the gate',          at: '2026-08-08T08:46:29Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: '0912c8c', app: 'SMMS', env: 'PREPROD', message: 'Let an admin actually pick the Caretaker role',        at: '2026-08-07T23:32:53Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: '0912c8c', app: 'SMMS', env: 'UAT',     message: 'Let an admin actually pick the Caretaker role',        at: '2026-08-07T22:29:22Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: 'e332474', app: 'SMMS', env: 'PREPROD', message: 'Give the caretaker an app for the gate',               at: '2026-08-07T22:29:22Z', by: 'RajMSFT', status: 'Succeeded' },
    { commit: 'e93f670', app: 'SMMS', env: 'UAT',     message: 'Add practical training programme page',                at: '2026-08-06T18:10:00Z', by: 'RajMSFT', status: 'Succeeded' }
  ],

  builds: [
    { commit: '70fefa4', branch: 'main', message: 'Make the caretaker pick a flat instead of typing one', by: 'RajMSFT', at: '2026-08-08T09:45:00Z' },
    { commit: '38a1028', branch: 'main', message: 'Tell the resident what is waiting at the gate',        by: 'RajMSFT', at: '2026-08-08T09:30:00Z' },
    { commit: '0c298b2', branch: 'main', message: 'Send timestamps as UTC so the browser shows local',    by: 'RajMSFT', at: '2026-08-08T09:00:00Z' },
    { commit: '6ffd7be', branch: 'main', message: 'Show the committee what happens at the gate',          by: 'RajMSFT', at: '2026-08-08T08:40:00Z' },
    { commit: '0912c8c', branch: 'main', message: 'Let an admin actually pick the Caretaker role',        by: 'RajMSFT', at: '2026-08-07T17:48:00Z' }
  ],

  repository: {
    name: 'rrathore_microsoft/SMMS',
    url: 'https://github.com/rrathore_microsoft/SMMS',
    branch: 'main',
    visibility: 'Private'
  },

  servers: [
    {
      name: 'ssms-webserver', ip: '135.235.195.132', role: 'DEV + UAT',
      cpu: 34, ram: 61, disk: 72,
      docker: 'Running', sql: 'Running', nginx: 'Running',
      ssl: 'Valid to 2026-10-29', sshNote: 'Port 22 is JIT-gated'
    },
    {
      name: 'smms-pprod-vm', ip: '20.219.1.173', role: 'PRE-PROD',
      cpu: 22, ram: 48, disk: 39,
      docker: 'Running', sql: 'Running', nginx: 'Running',
      ssl: 'Valid to 2026-11-05', sshNote: 'SSH blocked — use az vm run-command'
    }
  ],

  notes: {
    today: 'Caretaker gate features signed off on DEV at 70fefa4. Not promoted onward.',
    knownIssues: 'Deleting a user who ever raised a complaint returns 500 (FK on soft-deleted complaints).',
    pending: 'Add an nginx rule rejecting dev-*.ssms.yuvaansoft.shop.'
  }
};

// ── The seam ────────────────────────────────────────────────
// If the read-only backend is reachable the portal shows real state; otherwise it
// falls back to SAMPLE and says so. It never silently mixes the two.
const API_BASE = 'http://127.0.0.1:5099/api';

const TIER_BADGE = { DEV: 'ACTIVE DEV', PREPROD: 'PRE-PROD', UAT: 'UAT', PROD: 'PRODUCTION' };

// The backend reports only what it could actually read. Anything it could not is
// left as an em dash here rather than filled with something plausible.
function toUiEnvironment(s){
  return {
    id: s.id,
    tier: s.tier,
    name: s.name,
    hostname: s.hostname,
    hostPattern: '—',
    purpose: s.note || '',
    status: s.hazard ? 'Hazard' : (s.probeState === 'ok' ? 'Running' : s.health),
    statusTone: s.hazard ? 'danger' : (s.health === 'Healthy' ? 'ok' : (s.health === 'Unknown' ? 'muted' : 'warn')),
    badge: s.hazard ? 'DO NOT USE' : (TIER_BADGE[s.tier] || s.tier),
    deploymentEnabled: s.deploymentEnabled,
    hazard: s.hazard,
    dataSensitivity: s.dataSensitivity,
    commit: s.commit ? s.commit.slice(0, 7) : '—',
    commitMessage: s.commitMessage || '—',
    commitAuthor: s.commitAuthor || '—',
    commitDate: s.commitDate || null,
    commitsBehind: s.commitsBehind,
    branch: '—',
    signedOff: s.signedOff,
    image: s.image || '—',
    rollbackImage: null,
    rollbackCommit: null,
    containers: s.containers || [],
    deployedAt: null,
    deployedBy: '—',
    health: s.health,
    appUrl: s.appUrl || '',
    apiUrl: s.apiUrl || '',
    database: s.database || '—',
    server: s.server || '—',
    pipeline: s.pipeline || '—',
    deployDuration: '—',
    migration: '—',
    storageUsed: '—',
    releaseNotes: '—',
    note: s.note,
    envVars: [],
    logs: [],
    probeState: s.probeState,
    probeError: s.probeError,
    checkedAt: s.checkedAt
  };
}

const Api = {
  _live: false,
  _meta: null,
  _token: null,
  _session: null,

  _delay(value, ms = 180) {
    return new Promise(resolve => setTimeout(() => resolve(structuredClone(value)), ms));
  },

  _headers() {
    const headers = { 'Accept': 'application/json', 'Content-Type': 'application/json' };
    if (this._token) headers['Authorization'] = 'Bearer ' + this._token;
    return headers;
  },

  async _get(path) {
    const res = await fetch(API_BASE + path, { headers: this._headers() });
    if (!res.ok) throw new Error(`Backend returned ${res.status} for ${path}`);
    return res.json();
  },

  async _post(path, body) {
    const res = await fetch(API_BASE + path, { method: 'POST', headers: this._headers(), body: JSON.stringify(body) });
    const text = await res.text();
    const data = text ? JSON.parse(text) : {};
    if (!res.ok) throw new Error(data.message || `Backend returned ${res.status}`);
    return data;
  },

  authConfigured() { return this._meta?.authConfigured === true; },

  /// Decides once, at start-up, whether this session shows real data.
  async init() {
    try {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), 2500);
      const res = await fetch(API_BASE + '/meta', { signal: controller.signal });
      clearTimeout(timer);
      if (!res.ok) throw new Error('meta not ok');
      this._meta = await res.json();
      this._live = this._meta.live === true;
    } catch {
      this._live = false;
      this._meta = { live: false, readOnly: true, source: SAMPLE.meta.source };
    }
    return this._live;
  },

  isLive() { return this._live; },
  meta() { return this._meta || { live: false }; },

  // POST /api/auth/login. Real when the backend has operators configured; otherwise the
  // local list, and the portal says so.
  async login(username, password) {
    if (this._live && this.authConfigured()) {
      const res = await this._post('/auth/login', { username, password });
      this._token = res.token;
      this._session = res;
      return { username: res.username, name: res.name, role: res.role };
    }
    const user = SAMPLE.users.find(u => u.username === username && u.password === password);
    if (!user) throw new Error('Wrong username or password.');
    return this._delay({ username: user.username, name: user.name, role: user.role });
  },

  logout() { this._token = null; this._session = null; },

  // GET /api/applications  + /api/environments
  async getApplications() {
    if (!this._live) return this._delay(SAMPLE.applications);

    const [apps, envs] = await Promise.all([this._get('/applications'), this._get('/environments')]);
    return apps.map(a => ({
      key: a.key,
      name: a.name,
      icon: a.icon,
      description: a.description,
      active: a.active,
      environments: envs.filter(e => e.app === a.key).map(toUiEnvironment)
    }));
  },

  async getEnvironment(appKey, envId) {
    if (!this._live) {
      const app = SAMPLE.applications.find(a => a.key === appKey);
      const env = app?.environments.find(e => e.id === envId);
      if (!env) throw new Error('Environment not found.');
      return this._delay(env);
    }
    return toUiEnvironment(await this._get('/environments/' + encodeURIComponent(envId)));
  },

  // Deployment history comes from DEPLOYED_HISTORY on each box, which promote.ps1
  // started appending to. Boxes deployed before that have a shorter history.
  async getHistory() {
    if (!this._live) return this._delay(SAMPLE.history);
    const rows = await this._get('/deployments');
    return rows.map(r => ({
      commit: r.record.shortCommit,
      app: 'SMMS',
      env: r.tier,
      message: r.record.message || '(not in this clone)',
      at: r.record.at,
      by: r.record.by,
      status: 'Succeeded'
    }));
  },

  async getBuilds() {
    if (!this._live) return this._delay(SAMPLE.builds);
    const commits = await this._get('/builds');
    return commits.map(c => ({ commit: c.shortSha, branch: '—', message: c.subject, by: c.author, at: c.date }));
  },

  async getServers() { return this._delay(SAMPLE.servers); },

  async getRepository() {
    if (!this._live) return this._delay(SAMPLE.repository);
    const repo = await this._get('/repository');
    return { name: repo.name, url: repo.url, branch: repo.branch, visibility: 'Private' };
  },
  async getNotes() { return this._delay(SAMPLE.notes); },
  async saveNotes(notes) { Object.assign(SAMPLE.notes, notes); return this._delay(true); },

  // The server decides this; the copy held here only shapes the UI.
  async getPermissions(role) {
    if (this._session) {
      return { deploy: this._session.deploy || [], rollback: this._session.rollback === true, logs: true };
    }
    return this._delay(SAMPLE.permissions[role] || SAMPLE.permissions.Developer);
  },

  async deploy({ envId, commit, confirm }) {
    if (!this._live || !this.authConfigured()) {
      throw new Error('This portal is running on sample data, so nothing was deployed.');
    }
    return this._post('/deployments', { environmentId: envId, commit, confirm });
  },

  async rollback({ envId, confirm }) {
    if (!this._live || !this.authConfigured()) {
      throw new Error('This portal is running on sample data, so nothing was rolled back.');
    }
    return this._post('/deployments/rollback', { environmentId: envId, confirm });
  },

  async getJob(id) { return this._get('/jobs/' + encodeURIComponent(id)); },

  async getAudit() { return this._live ? this._get('/audit') : this._delay([]); }
};

