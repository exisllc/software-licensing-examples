// Licensing for a Node app: start-up check, activation, deactivation, offline grace period.
// This file is identical in samples/cli-tool, samples/desktop-electron and samples/subscriptions. Rules:
//   - the server decides; ask it on every start;
//   - a network problem never locks out a user who was licensed within the last GRACE_DAYS days;
//   - every outcome comes with a message you can show as-is.
import fs from 'node:fs';
import path from 'node:path';
import { ExisOneClient } from 'exisone-client';

/** Settings from EXISONE_* environment variables, or a git-ignored .env file next to package.json. */
export function loadSettings(dir = process.cwd()) {
  try { process.loadEnvFile(path.join(dir, '.env')); } catch { /* no .env file: use the environment */ }
  const s = {
    baseUrl: process.env.EXISONE_BASE_URL || 'https://www.exisone.com',
    accessToken: process.env.EXISONE_ACCESS_TOKEN || '', // verify-only token: it ships in the app, so it is public
    product: process.env.EXISONE_PRODUCT || '',
    publicKey: process.env.EXISONE_PUBLIC_KEY || undefined, // PEM, only for offline codes
    buyUrl: process.env.EXISONE_BUY_URL || '',
    graceDays: Number(process.env.EXISONE_GRACE_DAYS || 7),
  };
  if (!s.accessToken) throw new Error('EXISONE_ACCESS_TOKEN is not set. Copy .env.example to .env and fill it in.');
  if (!s.product) throw new Error('EXISONE_PRODUCT is not set.');
  return s;
}

const DAY = 24 * 60 * 60 * 1000;

export class LicenseManager {
  /**
   * @param settings   from loadSettings()
   * @param appVersion your app's version, so the server can enforce a minimum version
   * @param cacheFile  where the last server answer is kept (a per-user app-data path)
   */
  constructor(settings, { appVersion, cacheFile }) {
    this.settings = settings;
    this.appVersion = appVersion;
    this.cacheFile = cacheFile;
    this.client = new ExisOneClient({
      baseUrl: settings.baseUrl,
      accessToken: settings.accessToken,
      offlinePublicKey: settings.publicKey,
      timeout: 10_000,
    });
    this.hardwareId = this.client.generateHardwareId(); // stable per machine
  }

  get storedKey() { return this.#load()?.activationKey ?? null; }

  /** Ask the server; fall back to the cached answer inside the grace period. */
  async check() {
    let cached = this.#load();
    if (cached && cached.hardwareId !== this.hardwareId) cached = null; // copied from another machine
    const key = cached?.activationKey ?? null;

    try {
      // An empty key is a trial check: the server starts (or continues) this machine's trial.
      const r = await this.client.validate({
        activationKey: key ?? '', hardwareId: this.hardwareId,
        productName: this.settings.product, version: this.appVersion,
      });
      this.#save({
        activationKey: key, email: cached?.email ?? null, status: r.status,
        expiresAt: r.expirationDate?.toISOString() ?? null, features: r.features,
        lastValidatedUtc: new Date().toISOString(), hardwareId: this.hardwareId,
      });
      return this.#fromServer(r, key);
    } catch (err) {
      return this.#fromCache(cached, serverStatus(err)); // network down, or the server refused the token
    }
  }

  async activate(key, email) {
    key = key.trim();
    const r = await this.client.activate({
      activationKey: key, email: email.trim(), hardwareId: this.hardwareId,
      productName: this.settings.product, version: this.appVersion,
    }); // the SDK returns network errors as { success: false } instead of throwing
    if (!r.success) {
      if (r.errorCode === 'version_outdated') {
        return { ok: false, message: `This version is too old to activate. Please update to ${r.minimumRequiredVersion} or later.` };
      }
      if (!r.errorCode && r.errorMessage?.startsWith('Network error')) {
        return { ok: false, message: "Can't reach the license server. Activation needs an internet connection." };
      }
      return { ok: false, message: r.errorCode || r.errorMessage || 'Activation failed. Check the key and try again.' };
    }
    this.#save({ activationKey: key, email: email.trim(), status: 'licensed', hardwareId: this.hardwareId });
    return { ok: true, message: 'Activated. Thank you!' };
  }

  /** Frees this machine's seat so the key can be activated elsewhere. */
  async deactivate() {
    const key = this.storedKey;
    if (!key) return { ok: false, message: 'No license is activated on this computer.' };
    try {
      await this.client.deactivate({ activationKey: key, hardwareId: this.hardwareId, productName: this.settings.product });
    } catch (err) {
      return { ok: false, message: serverStatus(err) == null
        ? "Can't reach the license server. Deactivation needs an internet connection."
        : 'The license server refused the deactivation. Please contact support.' };
    }
    try { fs.unlinkSync(this.cacheFile); } catch { }
    return { ok: true, message: 'This computer is deactivated. You can now activate the key on another computer.' };
  }

  #fromServer(r, key) {
    const expires = r.expirationDate ?? null;
    const update = isNewer(r.serverVersion, this.appVersion) ? r.serverVersion : null;
    const base = { expiresAt: expires, features: r.features ?? [], updateAvailable: update, offline: false };
    switch (r.status) {
      case 'licensed':
        return { ...base, kind: 'licensed', message: expires && expires.getUTCFullYear() < 9999 ? `Licensed until ${day(expires)}.` : 'Licensed.' };
      case 'trial':
        return { ...base, kind: 'trial', message: `Trial: ${daysLeft(expires)} day(s) left.` };
      case 'expired':
        return key
          ? { ...base, kind: 'expired', message: `Your license expired on ${day(expires)}. Renew it to keep going.` }
          : { ...base, kind: 'trial-ended', message: 'Your trial has ended. Buy a license to keep going.' };
      case 'version_outdated':
        return { ...base, kind: 'update-required', updateAvailable: r.serverVersion,
          message: `This version is no longer supported. Please update to ${r.minimumRequiredVersion} or later.` };
      default: // 'invalid', 'trial_unavailable', ...
        return { ...base, kind: 'not-licensed', message: key
          ? 'This license is not active on this computer. Activate it again, or contact support.'
          : 'Enter a license key to continue.' };
    }
  }

  #fromCache(c, status) {
    const now = Date.now();
    const usable = c && (c.status === 'licensed' || c.status === 'trial') && c.lastValidatedUtc
      && now - Date.parse(c.lastValidatedUtc) < this.settings.graceDays * DAY
      && (!c.expiresAt || Date.parse(c.expiresAt) > now);
    if (!usable) {
      return { kind: 'needs-connection', features: [], offline: false, message: status === 401 || status === 403
        ? "The license server refused this app's access token. Please contact support."
        : "Can't reach the license server. Connect to the internet and try again." };
    }
    const until = new Date(Date.parse(c.lastValidatedUtc) + this.settings.graceDays * DAY);
    return {
      kind: c.status === 'trial' ? 'trial' : 'licensed', offline: true,
      expiresAt: c.expiresAt ? new Date(c.expiresAt) : null, features: c.features ?? [],
      message: `Working offline. Connect to the internet before ${day(until)} to keep going.`,
    };
  }

  // The cache is a convenience for offline use, not copy protection: the server is asked again on
  // every start, so editing this file gains a user at most the grace period.
  #load() {
    try { return JSON.parse(fs.readFileSync(this.cacheFile, 'utf8')); } catch { return null; }
  }
  #save(value) {
    try {
      fs.mkdirSync(path.dirname(this.cacheFile), { recursive: true });
      fs.writeFileSync(this.cacheFile, JSON.stringify(value, null, 2));
    } catch { /* never fail the app because the cache can't be written */ }
  }
}

/** Can the app be used in this state? */
export const canUse = (state) => state.kind === 'licensed' || state.kind === 'trial';

/** HTTP status when the server answered with an error (the SDK puts it first); null for network errors. */
function serverStatus(err) {
  const m = /^(\d{3}) /.exec(err?.message ?? '');
  return m ? Number(m[1]) : null;
}

function isNewer(a, b) {
  if (!a || !b) return false;
  const pa = a.split('.').map(Number), pb = b.split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const x = pa[i] || 0, y = pb[i] || 0;
    if (Number.isNaN(x) || Number.isNaN(y)) return false;
    if (x !== y) return x > y;
  }
  return false;
}

const day = (d) => (d ? new Date(d).toLocaleDateString() : '?');
const daysLeft = (d) => (d ? Math.max(0, Math.ceil((d.getTime() - Date.now()) / DAY)) : 0);
