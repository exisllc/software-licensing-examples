// The window only shows the license state and asks the main process to act.
const $ = (id) => document.getElementById(id);
const canUse = (s) => s.kind === 'licensed' || s.kind === 'trial';

async function refresh() {
  $('status').textContent = 'Checking license…';
  render(await window.license.check());
}

function render(s) {
  $('status').textContent = s.message;
  $('status').className = canUse(s) ? (s.offline ? 'warn' : 'ok') : 'bad';
  $('canvas').classList.toggle('locked', !canUse(s));          // the "app" works only when licensed
  $('activate').hidden = s.kind === 'licensed' || s.kind === 'update-required';
  $('buy').hidden = !['trial', 'trial-ended', 'expired', 'not-licensed'].includes(s.kind);
  $('buy').textContent = s.kind === 'expired' ? 'Renew' : 'Buy';
  $('deactivate').hidden = !(s.kind === 'licensed' && !s.offline);
  $('retry').hidden = !(s.kind === 'needs-connection' || s.offline);
  $('update').hidden = !s.updateAvailable;
  $('update').textContent = s.kind === 'update-required' ? 'Please install the latest version.' : `Version ${s.updateAvailable} is available.`;
}

$('activate').onclick = () => { $('activateForm').style.display = 'flex'; $('key').focus(); };
$('cancel').onclick = () => { $('activateForm').style.display = 'none'; };
$('buy').onclick = () => window.license.buy();
$('retry').onclick = refresh;
$('deactivate').onclick = async () => {
  if (!confirm('Deactivate Acme Sketch on this computer? You can then activate the key on another one.')) return;
  alert((await window.license.deactivate()).message);
  refresh();
};
$('activateForm').onsubmit = async (e) => {
  e.preventDefault();
  $('status').textContent = 'Activating…';
  const r = await window.license.activate($('key').value, $('email').value);
  alert(r.message);
  if (r.ok) $('activateForm').style.display = 'none';
  refresh();
};

// A tiny drawing surface standing in for the real app.
const ctx = $('canvas').getContext('2d');
let drawing = false;
$('canvas').onmousedown = (e) => { drawing = true; ctx.beginPath(); ctx.moveTo(e.offsetX, e.offsetY); };
$('canvas').onmousemove = (e) => { if (drawing) { ctx.lineTo(e.offsetX, e.offsetY); ctx.stroke(); } };
window.onmouseup = () => { drawing = false; };

refresh(); // check on every start
