// Acme Notes (subscription edition), console version.
//   node src/index.mjs activate <key> <email>   activate this machine
//   node src/index.mjs status                    one check, with the subscription message
//   node src/index.mjs watch                     long-running app: re-check every N minutes, notice renewals
//   node src/index.mjs simulate                  show the messages for sample dates (no server needed)
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { LicenseManager, loadSettings } from './license.mjs';
import { describeSubscription, wasRenewed } from './subscription.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const [command = 'status', ...args] = process.argv.slice(2);

if (command === 'simulate') {
  const now = new Date('2026-06-01T12:00:00Z');
  const at = (days) => new Date(now.getTime() + days * 86_400_000);
  for (const [label, state] of [
    ['30 days left', { kind: 'licensed', expiresAt: at(30) }],
    ['3 days left', { kind: 'licensed', expiresAt: at(3) }],
    ['3 days left, offline', { kind: 'licensed', expiresAt: at(3), offline: true }],
    ['ended', { kind: 'expired', expiresAt: at(-2) }],
    ['perpetual', { kind: 'licensed', expiresAt: new Date('9999-12-31T23:59:59Z') }],
  ]) {
    const m = describeSubscription(state, now);
    console.log(`${label.padEnd(22)} [${m.level}] ${m.text}`);
  }
  process.exit(0);
}

let license;
try {
  const configHome = process.env.APPDATA || process.env.XDG_CONFIG_HOME || path.join(os.homedir(), '.config');
  license = new LicenseManager(loadSettings(path.join(here, '..')), {
    appVersion: '1.0.0', cacheFile: path.join(configHome, 'acme-notes-subscription', 'license-cache.json'),
  });
} catch (err) {
  console.error(`Configuration problem: ${err.message}`);
  process.exit(2);
}

if (command === 'activate') {
  const [key, email] = args;
  if (!key || !email) { console.error('Usage: node src/index.mjs activate <key> <email>'); process.exit(2); }
  const r = await license.activate(key, email);
  console.log(r.message);
  process.exit(r.ok ? 0 : 1);
}

if (command === 'status') {
  const m = describeSubscription(await license.check());
  console.log(`[${m.level}] ${m.text}`);
  process.exit(m.level === 'error' ? 1 : 0);
}

if (command === 'watch') {
  // A long-running app re-checks periodically (daily is plenty; shorter here so you can watch it).
  const minutes = Number(process.env.CHECK_INTERVAL_MINUTES || 60);
  let last = await license.check();
  console.log(`[${describeSubscription(last).level}] ${describeSubscription(last).text}`);
  setInterval(async () => {
    const next = await license.check();
    if (wasRenewed(last, next)) console.log(`Renewed: thank you! New end date ${next.expiresAt.toISOString().slice(0, 10)}.`);
    const m = describeSubscription(next);
    console.log(`[${new Date().toLocaleTimeString()}] [${m.level}] ${m.text}`);
    last = next;
  }, minutes * 60_000);
} else {
  console.error(`Unknown command: ${command}`);
  process.exit(2);
}
