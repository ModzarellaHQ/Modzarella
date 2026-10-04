const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const doing = { install: 'Installing', enable: 'Turning on', disable: 'Turning off', remove: 'Uninstalling', update: 'Updating', launch: 'Starting the game', stop: 'Stopping the game', loader: 'Installing the mod loader', unloader: 'Removing everything', reset: 'Resetting mod settings', open: 'Opening', release: 'Opening the download page', link: 'Opening' };
const tabs = ['installed', 'browse', 'settings'];
let busy = false, mods = [], current = 'installed', firstLoad = true, hideLog;

function tab(name) {
  current = name;
  for (const t of tabs) $('tab' + t[0].toUpperCase() + t.slice(1)).setAttribute('aria-selected', t === name);
  $('mods').hidden = $('search').hidden = name === 'settings';
  $('settings').hidden = name !== 'settings';
  render();
}

function row({ mod, state }) {
  const id = esc(mod.id);
  const meta = `<span class="meta">${esc(mod.version)} · ${esc(mod.author)}</span>${state === 'UpdateAvailable' ? '<span class="meta update">update ready</span>' : ''}`;
  const body = `<div class="body"><div class="name">${esc(mod.name)}${meta}</div><p title="${esc(mod.description)}">${esc(mod.description)}</p></div>`;
  if (state === 'NotInstalled')
    return `<div class="mod">${body}<button class="primary action" data-act="install" data-id="${id}">Install</button></div>`;
  const on = state !== 'Disabled';
  return `<div class="mod">
    <input type="checkbox" aria-label="${esc(mod.name)}" title="${on ? 'On' : 'Off'}" ${on ? 'checked' : ''} data-id="${id}">
    ${body}
    <button class="link" data-act="remove" data-id="${id}">Uninstall</button>
  </div>`;
}

function render() {
  if (current === 'settings') return;
  const q = $('search').value.toLowerCase();
  const inTab = mods.filter(m => (current === 'installed') === (m.state !== 'NotInstalled'));
  const shown = inTab.filter(({ mod }) => (mod.name + ' ' + mod.description).toLowerCase().includes(q));
  const empty = inTab.length
    ? 'No mods match.'
    : current === 'installed' ? 'No mods installed yet. Find some in Browse.' : mods.length ? 'You have every mod.' : 'No mods found. Check the mod source in Settings.';
  $('mods').innerHTML = shown.map(row).join('') || `<p class="empty">${empty}</p>`;
}

function setRunning(running) {
  $('play').textContent = running ? '■ Stop' : '▶ Play';
  $('play').dataset.act = running ? 'stop' : 'launch';
  $('play').classList.toggle('stop', running);
}

async function load() {
  const s = await (await fetch('/api/state')).json();
  if (document.activeElement !== $('source')) $('source').value = s.source;
  if (document.activeElement !== $('gamedir')) $('gamedir').value = s.gameDir || '';
  const problem = s.error || (s.game ? '' : "Cheese Rolling wasn't found. Set the game folder in Settings.");
  $('error').textContent = problem;
  $('error').hidden = !problem;
  if (!s.game) tab('settings');
  $('version').textContent = $('aboutVersion').textContent = s.version;
  $('game').textContent = s.game ? s.game.dir : 'Cheese Rolling not found';
  $('loader').textContent = s.game?.loader ? 'Repair mod loader' : 'Install mod loader';
  $('updateAll').hidden = !s.mods.some(m => m.state === 'UpdateAvailable');
  $('autoUpdate').checked = s.autoUpdate;
  $('appUpdate').hidden = !s.appUpdate;
  if (s.appUpdate) $('appVersion').textContent = s.appUpdate.version;
  setRunning(s.running);
  mods = s.mods;
  const installed = mods.filter(m => m.state !== 'NotInstalled').length;
  $('tabInstalled').textContent = `Installed (${installed})`;
  $('tabBrowse').textContent = `Browse (${mods.length - installed})`;
  render();
  if (firstLoad && s.autoUpdate && s.mods.some(m => m.state === 'UpdateAvailable')) { firstLoad = false; await act('update'); }
  firstLoad = false;
}

function confirmed(button) {
  if (button.dataset.armed) { delete button.dataset.armed; return true; }
  const label = button.textContent;
  button.dataset.armed = '1';
  button.textContent = button.dataset.confirm;
  setTimeout(() => { delete button.dataset.armed; button.textContent = label; }, 4000);
  return false;
}

async function act(action, id, value) {
  if (busy) return;
  busy = true;
  document.querySelectorAll('button, input').forEach(b => b.disabled = true);
  clearTimeout(hideLog);
  const log = $('log');
  log.className = 'toast busy';
  log.hidden = false;
  log.textContent = `${doing[action] ?? 'Saving'}${id ? ' ' + id : ''}…`;
  let failed = false;
  try {
    const r = await (await fetch('/api/do', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ action, id, value }) })).json();
    failed = !!r.error;
    log.textContent = failed ? 'Error: ' + r.error : r.log.join('\n');
  } catch (e) { failed = true; log.textContent = 'Error: ' + e; }
  log.className = failed ? 'toast bad' : 'toast';
  hideLog = setTimeout(() => log.hidden = true, failed ? 10000 : 4000);
  busy = false;
  document.querySelectorAll('button, input').forEach(b => b.disabled = false);
  await load();
}

async function poll() {
  if (!busy && !document.hidden) {
    try { setRunning((await (await fetch('/api/status')).json()).running); } catch { }
  }
  setTimeout(poll, 2000);
}

document.addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  if (b.dataset.tab) tab(b.dataset.tab);
  else if (b.dataset.act && (!b.dataset.confirm || confirmed(b)))
    act(b.dataset.act, b.dataset.id, b.dataset.value ?? (b.dataset.from && $(b.dataset.from).value));
});
document.addEventListener('change', e => {
  const c = e.target;
  if (c.matches('input[type=checkbox][data-id]')) act(c.checked ? 'enable' : 'disable', c.dataset.id);
  else if (c.id === 'autoUpdate') act('autoupdate', null, String(c.checked));
});
$('search').addEventListener('input', render);

if (tabs.includes(location.hash.slice(1))) tab(location.hash.slice(1));
load().then(poll);
