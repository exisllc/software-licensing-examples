#!/usr/bin/env node
// acme-cli: a pretend command-line tool whose real commands need a license or an active trial.
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs } from 'node:util';
import { LicenseManager, loadSettings, canUse } from './license.mjs';

const APP_VERSION = '1.0.0';
const here = path.dirname(fileURLToPath(import.meta.url));

// Per-user location for the saved license state (APPDATA on Windows, ~/.config elsewhere).
const configHome = process.env.APPDATA || process.env.XDG_CONFIG_HOME || path.join(os.homedir(), '.config');
const cacheFile = path.join(configHome, 'acme-cli', 'license-cache.json');

const HELP = `acme-cli ${APP_VERSION}

Usage:
  acme-cli convert <file>                 the licensed command (needs a license or trial)
  acme-cli status                         show the license state
  acme-cli activate <key> --email <addr>  activate this machine
  acme-cli deactivate                     free this machine's seat
`;

async function main() {
  const { positionals, values } = parseArgs({
    allowPositionals: true,
    options: { email: { type: 'string' }, help: { type: 'boolean', short: 'h' } },
  });
  const [command, arg] = positionals;
  if (!command || values.help) { console.log(HELP); return 0; }

  let license;
  try {
    license = new LicenseManager(loadSettings(path.join(here, '..')), { appVersion: APP_VERSION, cacheFile });
  } catch (err) {
    console.error(`Configuration problem: ${err.message}`);
    return 2;
  }

  switch (command) {
    case 'activate': {
      if (!arg || !values.email) { console.error('Usage: acme-cli activate <key> --email <addr>'); return 2; }
      const r = await license.activate(arg, values.email);
      console.log(r.message);
      return r.ok ? 0 : 1;
    }
    case 'deactivate': {
      const r = await license.deactivate();
      console.log(r.message);
      return r.ok ? 0 : 1;
    }
    case 'status': {
      const s = await license.check();
      console.log(s.message);
      if (s.features?.length) console.log(`Features: ${s.features.join(', ')}`);
      if (s.updateAvailable) console.log(`Version ${s.updateAvailable} is available.`);
      return canUse(s) ? 0 : 1;
    }
    case 'convert': {
      // The license gate: one check before the real work. Offline inside the grace period still passes.
      const s = await license.check();
      if (!canUse(s)) {
        console.error(s.message);
        if (s.kind !== 'needs-connection') console.error('Activate with: acme-cli activate <key> --email <addr>');
        return 1;
      }
      if (s.offline || s.kind === 'trial') console.error(s.message); // a hint on stderr, keeps stdout clean
      console.log(`Converted ${arg ?? '(nothing)'} (pretend).`);
      return 0;
    }
    default:
      console.error(`Unknown command: ${command}\n\n${HELP}`);
      return 2;
  }
}

process.exitCode = await main();
