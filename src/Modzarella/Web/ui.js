const $ = id => document.getElementById(id);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const doing = { install: 'Installing', enable: 'Turning on', disable: 'Turning off', remove: 'Uninstalling', update: 'Updating', launch: 'Starting the game', loader: 'Installing the mod loader', unloader: 'Removing everything', reset: 'Resetting mod settings', open: 'Opening', release: 'Opening the download page' };
let busy = false, mods = [], firstLoad = true;

function tab(name) {
  $('tabMods').setAttribute('aria-selected', name === 'mods');
  $('tabSettings').setAttribute('aria-selected', name === 'settings');
  $('mods').hidden = $('search').hidden = name !== 'mods';
  $('settings').hidden = name !== 'settings';
}

function render() {
  const q = $('search').value.toLowerCase();
  const shown = mods.filter(({ mod }) => (mod.name + ' ' + mod.description).toLowerCase().includes(q));
  $('mods').innerHTML = shown.map(({ mod, state }) => {
    const on = state === 'Enabled' || state === 'UpdateAvailable';
    const installed = state !== 'NotInstalled';
    const id = esc(mod.id);
    return `<div class="mod">
      <input type="checkbox" aria-label="${esc(mod.name)}" title="${on ? 'On' : 'Off'}" ${on ? 'checked' : ''} data-id="${id}" data-on="${installed ? 'enable' : 'install'}">
      <div class="body">
        <div class="name">${esc(mod.name)}<span class="meta">${esc(mod.version)} · ${esc(mod.author)}</span>${state === 'UpdateAvailable' ? '<span class="meta update">update ready</span>' : ''}</div>
        <p title="${esc(mod.description)}">${esc(mod.description)}</p>
      </div>
      ${installed ? `<button class="link" data-act="remove" data-id="${id}">Uninstall</button>` : ''}
    </div>`;
  }).join('') || `<p class="empty">${mods.length ? 'No mods match.' : 'No mods yet. Check the mod source in Settings.'}</p>`;
}

async function load() {
  const s = await (await fetch('/api/state')).json();
  if (document.activeElement !== $('source')) $('source').value = s.source;
  if (document.activeElement !== $('gamedir')) $('gamedir').value = s.gameDir || '';
  const problem = s.error || (s.game ? '' : "Cheese Rolling wasn't found. Set the game folder in Settings.");
  $('error').textContent = problem;
  $('error').hidden = !problem;
  if (!s.game) tab('settings');
  $('game').textContent = s.game ? s.game.dir : 'Cheese Rolling not found';
  $('loader').textContent = s.game?.loader ? 'Repair mod loader' : 'Install mod loader';
  $('updateAll').hidden = !s.mods.some(m => m.state === 'UpdateAvailable');
  $('autoUpdate').checked = s.autoUpdate;
  $('appUpdate').hidden = !s.appUpdate;
  if (s.appUpdate) $('appVersion').textContent = s.appUpdate.version;
  mods = s.mods;
  $('tabMods').textContent = `Mods (${mods.length})`;
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
  $('log').classList.add('busy');
  $('log').textContent = `${doing[action] ?? 'Saving'}${id ? ' ' + id : ''}…`;
  try {
    const r = await (await fetch('/api/do', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ action, id, value }) })).json();
    $('log').textContent = r.error ? 'Error: ' + r.error : r.log.join('\n');
  } catch (e) { $('log').textContent = 'Error: ' + e; }
  $('log').classList.remove('busy');
  busy = false;
  document.querySelectorAll('button, input').forEach(b => b.disabled = false);
  await load();
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
  if (c.matches('input[type=checkbox][data-id]')) act(c.checked ? c.dataset.on : 'disable', c.dataset.id);
  else if (c.id === 'autoUpdate') act('autoupdate', null, String(c.checked));
});
$('search').addEventListener('input', render);

load();
