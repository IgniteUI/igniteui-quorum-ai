/**
 * MAKER Web App — app.js
 * Communicates with MAKER.McpServer via POST + SSE streaming.
 */

let API_BASE = ''; // set by init() from /api/config

// ── State ──────────────────────────────────────────────────────────────────
let currentSteps = null;      // steps produced by the last Plan call
let currentController = null; // AbortController for the in-flight request

// ── Local storage ──────────────────────────────────────────────────────────
const LS_KEY = 'maker-settings';

function saveSettings() {
  try {
    localStorage.setItem(LS_KEY, JSON.stringify({
      prompt:     promptEl.value,
      batchSize:  parseInt(batchSizeEl.value, 10),
      k:          parseInt(kEl.value, 10),
      format:     formatEl.value,
      mcpServers: cachedMcpServers
    }));
  } catch { /* quota errors, private mode, etc. */ }
}

function loadSettings() {
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (!raw) return null;
    return JSON.parse(raw);
  } catch { return null; }
}

let cachedMcpServers = [];

// ── DOM refs ───────────────────────────────────────────────────────────────
const $ = id => document.getElementById(id);

const promptEl      = $('prompt');
const batchSizeEl   = $('batchSize');
const batchSizeVal  = $('batchSizeVal');
const kEl           = $('kInput');
const kVal          = $('kVal');
const planBtn       = $('planBtn');
const executeBtn    = $('executeBtn');
const planExBtn     = $('planExecuteBtn');
const statusEl      = $('status');
const stepsEl       = $('steps');
const stepCountEl   = $('stepCount');
const resultEl      = $('result');
const feedEl        = $('feed');
const formatEl      = $('formatSelect');

const mcpToggleBtn  = $('mcpToggleBtn');
const mcpForm       = $('mcpForm');
const mcpNameEl     = $('mcpName');
const mcpUrlEl      = $('mcpUrl');
const mcpDescEl     = $('mcpDesc');
const mcpApiKeyEl   = $('mcpApiKey');
const mcpAddBtn     = $('mcpAddBtn');
const mcpListEl     = $('mcpList');
const mcpCountEl    = $('mcpCount');

// ── Slider labels ──────────────────────────────────────────────────────────
batchSizeEl.addEventListener('input', () => { batchSizeVal.textContent = batchSizeEl.value; saveSettings(); });
kEl.addEventListener('input', () => { kVal.textContent = kEl.value; saveSettings(); });
promptEl.addEventListener('input', saveSettings);
formatEl.addEventListener('input', () => { saveSettings(); syncFormat(); });

async function syncFormat() {
  try {
    await fetch(`${API_BASE}/api/format`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ format: formatEl.value })
    });
  } catch { /* best-effort */ }
}

// ── MCP Servers ────────────────────────────────────────────────────────────
mcpToggleBtn.addEventListener('click', () => {
  mcpForm.hidden = !mcpForm.hidden;
  mcpToggleBtn.textContent = mcpForm.hidden ? '+' : '−';
});

mcpAddBtn.addEventListener('click', addMcpServer);

async function loadMcpServers() {
  try {
    const res = await fetch(`${API_BASE}/api/mcp-servers`);
    if (!res.ok) return;
    renderMcpServers(await res.json());
  } catch { /* silently ignore on load */ }
}

async function addMcpServer() {
  const name = mcpNameEl.value.trim();
  const url  = mcpUrlEl.value.trim();
  if (!name || !url) return;

  try {
    const res = await fetch(`${API_BASE}/api/mcp-servers`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        name,
        url,
        description: mcpDescEl.value.trim() || null,
        apiKey: mcpApiKeyEl.value.trim() || null
      })
    });
    if (!res.ok) {
      const err = await res.json().catch(() => null);
      showToast(err?.message ?? 'Failed to add server', 'error');
      return;
    }
    renderMcpServers(await res.json());
    mcpNameEl.value = mcpUrlEl.value = mcpDescEl.value = mcpApiKeyEl.value = '';
    mcpForm.hidden = true;
    mcpToggleBtn.textContent = '+';
  } catch (err) {
    showToast(`Failed to add server: ${err.message}`, 'error');
  }
}

async function removeMcpServer(name) {
  try {
    const res = await fetch(`${API_BASE}/api/mcp-servers/${encodeURIComponent(name)}`, { method: 'DELETE' });
    if (!res.ok) return;
    renderMcpServers(await res.json());
  } catch (err) {
    showToast(`Failed to remove server: ${err.message}`, 'error');
  }
}

function renderMcpServers(servers) {
cachedMcpServers = servers;
saveSettings();
mcpListEl.innerHTML = '';
mcpCountEl.textContent = servers.length || '';
servers.forEach(s => {
    const el = document.createElement('div');
    el.className = 'mcp-item';
    el.innerHTML = `
      <div class="mcp-item__info">
        <span class="mcp-item__name">${escHtml(s.Name ?? s.name)}</span>
        <span class="mcp-item__url">${escHtml(String(s.Url ?? s.url))}</span>
      </div>
      <button class="mcp-item__remove" title="Remove">✕</button>
    `;
    el.querySelector('.mcp-item__remove').addEventListener('click', () => removeMcpServer(s.Name ?? s.name));
    mcpListEl.appendChild(el);
  });
}

// ── Button handlers ────────────────────────────────────────────────────────
planBtn.addEventListener('click',    () => run(doPlan));
executeBtn.addEventListener('click', () => run(doExecute));
planExBtn.addEventListener('click',  () => run(doPlanAndExecute));

// ── Run wrapper ────────────────────────────────────────────────────────────
async function run(fn) {
  if (currentController) currentController.abort();
  currentController = new AbortController();

  setRunning(true);
  clearOutput();

  try {
    await fn(currentController.signal);
  } catch (err) {
    if (err.name !== 'AbortError') {
      setStatus(`Error: ${err.message}`, 'error');
      logFeedItem('error', err.message);
    }
  } finally {
    setRunning(false);
  }
}

// ── Tool calls ─────────────────────────────────────────────────────────────
async function doPlan(signal) {
  setStatus('Planning…', 'active');
  await streamRequest('/api/plan', getParams(), handleEvent, signal);
}

async function doExecute(signal) {
  if (!currentSteps?.length) return;
  setStatus('Executing…', 'active');
  await streamRequest('/api/execute', { ...getParams(), stepsJson: JSON.stringify(currentSteps) }, handleEvent, signal);
}

async function doPlanAndExecute(signal) {
  setStatus('Planning & Executing…', 'active');
  await streamRequest('/api/plan-and-execute', getParams(), handleEvent, signal);
}

function getParams() {
  return {
    prompt:    promptEl.value.trim(),
    batchSize: parseInt(batchSizeEl.value, 10),
    k:         parseInt(kEl.value, 10)
  };
}

// ── SSE streaming ──────────────────────────────────────────────────────────
async function streamRequest(endpoint, body, onEvent, signal) {
  const res = await fetch(`${API_BASE}${endpoint}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal
  });

  if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
  if (!res.body) throw new Error('No response body');

  const reader  = res.body.getReader();
  const decoder = new TextDecoder();
  let   buffer  = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });

    // Split on blank lines — each SSE message ends with \n\n
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? ''; // last incomplete line stays in buffer

    let type = '', data = '';
    for (const line of lines) {
      if      (line.startsWith('event: ')) type = line.slice(7).trim();
      else if (line.startsWith('data: '))  data = line.slice(6).trim();
      else if (line === '' && type && data) {
        onEvent(type, data);
        type = ''; data = '';
      }
    }
  }
}

// ── Event dispatcher ───────────────────────────────────────────────────────
function handleEvent(type, raw) {
  logFeedItem(type, raw);

  try {
    const data = JSON.parse(raw);

    switch (type) {

      case 'steps_proposed': {
        // data = Step[]  — show as "pending" previews
        data.forEach(s => {
          if (!stepsEl.querySelector(`[data-task="${CSS.escape(s.Task)}"]`)) {
            stepsEl.appendChild(makeStepEl(s, 'proposed', '…'));
          }
        });
        break;
      }

      case 'steps_added': {
        // data = { proposed, all }  — replace list with confirmed steps
        renderSteps(data.all);
        break;
      }

      case 'steps_rejected': {
        const reasons = (data.reasons ?? []).join('; ');
        showToast(`Steps rejected — ${reasons}`, 'warning');
        break;
      }

      case 'plan_vote': {
        updateVoteBadge('plan', data);
        break;
      }

      case 'execution_started': {
        // data = { batch, completed }
        highlightBatch(data.batch ?? []);
        break;
      }

      case 'state_changed': {
        // data = { state }
        previewResult(data.state ?? '');
        break;
      }

      case 'execution_vote': {
        updateVoteBadge('exec', data);
        break;
      }

      case 'complete': {
        onComplete(data);
        break;
      }

      case 'error': {
        setStatus(`Error: ${data.message ?? raw}`, 'error');
        break;
      }

      case 'phase': {
        // raw is a JSON-encoded string — data is the parsed string
        setStatus(typeof data === 'string' ? data : raw.replace(/"/g, ''), 'active');
        break;
      }
    }
  } catch (e) {
    console.warn('handleEvent parse error:', e, type, raw);
  }
}

// ── UI helpers ─────────────────────────────────────────────────────────────
function renderSteps(steps) {
  currentSteps = steps;
  stepsEl.innerHTML = '';
  steps.forEach((s, i) => stepsEl.appendChild(makeStepEl(s, 'added', i + 1)));
  stepCountEl.textContent = steps.length;
  executeBtn.disabled = false;
}

function makeStepEl(step, state, num) {
  const el = document.createElement('div');
  el.className = `step step--${state}`;
  el.dataset.task = step.Task;

  const deps = step.RequiredSteps?.length
    ? `<span class="step-deps">deps: ${step.RequiredSteps.join(', ')}</span>`
    : '';

  el.innerHTML = `
    <span class="step-num">${escHtml(String(num))}</span>
    <span class="step-task">${escHtml(step.Task)}${deps}</span>
  `;
  return el;
}

function highlightBatch(batch) {
  stepsEl.querySelectorAll('.step').forEach(el => el.classList.remove('step--executing'));
  batch.forEach(s => {
    const el = stepsEl.querySelector(`[data-task="${CSS.escape(s.Task)}"]`);
    if (el) el.classList.add('step--executing');
  });
}

function previewResult(state) {
  resultEl.innerHTML = `<pre class="result-preview">${escHtml(state)}</pre>`;
}

function updateVoteBadge(kind, state) {
  const id = `vote-${kind}`;
  let badge = document.getElementById(id);
  if (!badge) {
    badge = document.createElement('div');
    badge.id = id;
    badge.className = 'vote-badge';
    statusEl.appendChild(badge);
  }
  const v   = state.Votes ?? {};
  const yes = v.Yes  ?? 0;
  const no  = v.No   ?? 0;
  const end = v.End  ?? 0;
  const icon = kind === 'plan' ? '📋' : '⚙️';
  badge.textContent = `${icon} Yes ${yes}  No ${no}  End ${end}  / K=${state.KValue}`;
}

function onComplete(data) {
  // Remove vote badges
  statusEl.querySelectorAll('.vote-badge').forEach(el => el.remove());

  if (Array.isArray(data)) {
    // /api/plan → complete sends Step[]
    renderSteps(data);
    setStatus(`Plan ready — ${data.length} step${data.length !== 1 ? 's' : ''}`, 'success');

  } else if (data.result !== undefined) {
    // /api/execute or /api/plan-and-execute → complete sends { steps?, result }
    if (data.steps) renderSteps(data.steps);

    // Mark all steps as done
    stepsEl.querySelectorAll('.step').forEach(el => {
      el.classList.remove('step--executing', 'step--proposed');
      el.classList.add('step--done');
    });

    resultEl.innerHTML = `<pre class="result-final">${escHtml(data.result)}</pre>`;
    setStatus('Done ✓', 'success');
  }
}

// ── Feed ───────────────────────────────────────────────────────────────────
function logFeedItem(type, raw) {
  const el   = document.createElement('div');
  el.className = `feed-item feed-item--${type}`;

  const typeEl = document.createElement('span');
  typeEl.className = 'feed-type';
  typeEl.textContent = type;

  const dataEl = document.createElement('span');
  dataEl.className = 'feed-data';
  dataEl.textContent = summarise(type, raw);

  el.appendChild(typeEl);
  el.appendChild(dataEl);
  feedEl.prepend(el);

  // Cap the feed at 60 items
  while (feedEl.children.length > 60) feedEl.removeChild(feedEl.lastChild);
}

function summarise(type, raw) {
  try {
    const d = JSON.parse(raw);
    switch (type) {
      case 'steps_proposed':    return `${d.length} step(s) proposed`;
      case 'steps_added':       return `${d.all?.length ?? '?'} total steps confirmed`;
      case 'steps_rejected':    return `Rejected — ${(d.reasons ?? []).join('; ')}`;
      case 'plan_vote':         return `Yes ${d.Votes?.Yes ?? 0}  No ${d.Votes?.No ?? 0}  End ${d.Votes?.End ?? 0}`;
      case 'execution_started': return `Executing: ${(d.batch ?? []).map(s => s.Task).join(' | ')}`;
      case 'state_changed':     return (d.state ?? '').slice(0, 100);
      case 'execution_vote':    return `Yes ${d.Votes?.Yes ?? 0}  No ${d.Votes?.No ?? 0}  End ${d.Votes?.End ?? 0}`;
      case 'complete':          return Array.isArray(d) ? `${d.length} steps` : 'finished';
      case 'error':             return d.message ?? raw;
      case 'phase':             return typeof d === 'string' ? d : raw.replace(/"/g, '');
      default:                  return raw.slice(0, 120);
    }
  } catch {
    return raw.slice(0, 120);
  }
}

// ── Misc ───────────────────────────────────────────────────────────────────
function setStatus(text, state = '') {
  let span = statusEl.querySelector('.status-text');
  if (!span) {
    span = document.createElement('span');
    span.className = 'status-text';
    statusEl.prepend(span);
  }
  span.textContent = text;
  span.className   = `status-text status--${state}`;
}

function setRunning(running) {
  planBtn.disabled   = running;
  planExBtn.disabled = running;
  if (running) executeBtn.disabled = true;
}

function clearOutput() {
  stepsEl.innerHTML  = '';
  resultEl.innerHTML = '';
  feedEl.innerHTML   = '';
  stepCountEl.textContent = '';
  statusEl.querySelectorAll('.vote-badge').forEach(el => el.remove());
  const t = statusEl.querySelector('.status-text');
  if (t) t.textContent = '';
}

function showToast(msg, type = 'info') {
  const el = document.createElement('div');
  el.className   = `toast toast--${type}`;
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => el.remove(), 4000);
}

function escHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

// ── Init ───────────────────────────────────────────────────────────────────
async function init() {
  // Restore saved settings into the DOM before anything else
  const saved = loadSettings();
  if (saved) {
    if (saved.prompt != null)    promptEl.value    = saved.prompt;
    if (saved.batchSize != null) { batchSizeEl.value = saved.batchSize; batchSizeVal.textContent = saved.batchSize; }
    if (saved.k != null)         { kEl.value = saved.k; kVal.textContent = saved.k; }
    if (saved.format != null)    formatEl.value = saved.format;
  }

  setRunning(true);
  setStatus('Loading config…', 'active');
  try {
    const res = await fetch('/api/config');
    if (!res.ok) throw new Error(`HTTP ${res.status} ${res.statusText}`);
    const config = await res.json();
    API_BASE = config.mcpServerUrl ?? 'http://localhost:5000';
    setStatus('', '');
    await syncFormat();
    await loadMcpServers();

    // If the server returned no MCP servers but we have cached ones, re-sync them
    if (cachedMcpServers.length === 0 && saved?.mcpServers?.length) {
      for (const s of saved.mcpServers) {
        try {
          const r = await fetch(`${API_BASE}/api/mcp-servers`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              name:        s.Name ?? s.name,
              url:         String(s.Url ?? s.url),
              description: s.Description ?? s.description ?? null,
              apiKey:      s.ApiKey ?? s.apiKey ?? null
            })
          });
          if (r.ok) renderMcpServers(await r.json());
        } catch { /* best-effort */ }
      }
    }
  } catch (err) {
    setStatus(`Failed to load config: ${err.message}`, 'error');
    return; // leave buttons disabled
  }
  setRunning(false);
}

init();
