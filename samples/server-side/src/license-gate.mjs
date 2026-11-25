// Server-side licensing: each customer account of your web app/API uses one seat of a license key.
// The SDK's hardware ID is just a string, so on a server we use a stable id per customer account
// instead of a machine fingerprint. Validation results are cached in memory so the license server
// is not called on every request, and a short outage never takes your API down.
import { ExisOneClient } from 'exisone-client';

const MINUTE = 60_000;

export class LicenseGate {
  /**
   * @param store   where each account's license key is kept (your database; a Map here)
   * @param options { baseUrl, accessToken, product, appVersion, freshMinutes, graceMinutes }
   */
  constructor(store, { baseUrl, accessToken, product, appVersion, freshMinutes = 10, graceMinutes = 24 * 60 }) {
    this.store = store;
    this.product = product;
    this.appVersion = appVersion;
    this.freshMs = freshMinutes * MINUTE;  // re-ask the license server after this long
    this.graceMs = graceMinutes * MINUTE;  // keep serving on the last good answer this long if it's down
    this.client = new ExisOneClient({ baseUrl, accessToken, timeout: 5_000 });
    this.cache = new Map();                // accountId -> { status, expiresAt, features, checkedAt }
  }

  /** The seat id for an account. Keep it stable: changing it would need a new activation. */
  seatId(accountId) { return `acme-reports:account:${accountId}`; }

  async activate(accountId, key, email) {
    const r = await this.client.activate({
      activationKey: key.trim(), email: email.trim(), hardwareId: this.seatId(accountId),
      productName: this.product, version: this.appVersion,
    });
    if (!r.success) return { ok: false, message: r.errorCode || r.errorMessage || 'Activation failed.' };
    this.store.set(accountId, key.trim());
    this.cache.delete(accountId);
    return { ok: true, message: 'License activated for this account.' };
  }

  async deactivate(accountId) {
    const key = this.store.get(accountId);
    if (!key) return { ok: false, message: 'This account has no license.' };
    await this.client.deactivate({ activationKey: key, hardwareId: this.seatId(accountId), productName: this.product });
    this.store.delete(accountId);
    this.cache.delete(accountId);
    return { ok: true, message: 'License released; the key can be used by another account.' };
  }

  /** { licensed, status, features, expiresAt, stale } for an account. */
  async check(accountId) {
    const key = this.store.get(accountId);
    if (!key) return { licensed: false, status: 'no_license', features: [] };

    const hit = this.cache.get(accountId);
    if (hit && Date.now() - hit.checkedAt < this.freshMs) return view(hit, false);

    try {
      const r = await this.client.validate({
        activationKey: key, hardwareId: this.seatId(accountId), productName: this.product, version: this.appVersion,
      });
      const entry = { status: r.status, expiresAt: r.expirationDate ?? null, features: r.features ?? [], checkedAt: Date.now() };
      this.cache.set(accountId, entry);
      return view(entry, false);
    } catch (err) {
      // License server unreachable: keep serving on the last good answer inside the grace window.
      if (hit && hit.status === 'licensed' && Date.now() - hit.checkedAt < this.graceMs) return view(hit, true);
      console.warn(`license check failed for account ${accountId}: ${err.message}`);
      return { licensed: false, status: 'unavailable', features: [] };
    }
  }

  /** Express middleware: 402 unless the caller's account is licensed (and has `feature`, if given). */
  require(feature) {
    return async (req, res, next) => {
      const lic = await this.check(req.accountId);
      if (!lic.licensed) {
        const status = lic.status === 'unavailable' ? 503 : 402;
        return res.status(status).json({ error: lic.status, message: MESSAGES[lic.status] ?? MESSAGES.invalid });
      }
      if (feature && !lic.features.includes(feature)) {
        return res.status(403).json({ error: 'feature_not_licensed', message: `Your plan does not include "${feature}".` });
      }
      req.license = lic;
      next();
    };
  }
}

const view = (e, stale) => ({
  licensed: e.status === 'licensed' && (!e.expiresAt || new Date(e.expiresAt) > new Date()),
  status: e.status, features: e.features, expiresAt: e.expiresAt, stale,
});

const MESSAGES = {
  no_license: 'This account has no license. POST /license/activate with your key.',
  expired: 'Your license has expired. Renew it to keep using the API.',
  unavailable: 'The license could not be checked right now. Please retry shortly.',
  invalid: 'This license is not active for this account.',
};
