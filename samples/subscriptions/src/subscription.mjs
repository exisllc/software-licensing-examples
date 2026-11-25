// What a subscription looks like from inside the app.
//
// With ExisOne, a subscription renewal (Stripe or PayPal) keeps the SAME license key and moves its
// expiry date forward on the server. The server already gives a few days of slack past the next
// charge, so a renewal that is a little late never cuts the customer off. The app's only jobs are:
//   1. re-validate regularly, so it sees the new expiry after a renewal;
//   2. show the right message as the expiry approaches or passes;
//   3. not lock anyone out because of a network problem (the offline grace period in license.mjs).

const DAY = 24 * 60 * 60 * 1000;
export const RENEWS_SOON_DAYS = 7;

/**
 * Turns a license state into the subscription message to show.
 * Pure function of (state, now) so the messages can be tested without a server.
 */
export function describeSubscription(state, now = new Date()) {
  const expires = state.expiresAt ? new Date(state.expiresAt) : null;
  const perpetual = expires && expires.getUTCFullYear() >= 9999;

  if (state.kind === 'needs-connection') return { level: 'error', text: state.message };
  if (state.kind === 'expired') {
    return { level: 'error', text: `Your subscription ended on ${fmt(expires)}. Renew to keep using Acme Notes; your data is safe.` };
  }
  if (state.kind !== 'licensed') return { level: 'error', text: state.message };
  if (!expires || perpetual) return { level: 'ok', text: 'Licensed. No renewal needed.' };

  const daysLeft = Math.ceil((expires - now) / DAY);
  if (daysLeft <= 0) return { level: 'error', text: `Your subscription ended on ${fmt(expires)}.` };
  const offline = state.offline ? ' (offline: last confirmed by the server earlier)' : '';
  if (daysLeft <= RENEWS_SOON_DAYS) {
    return { level: 'warn', text: `Your subscription renews or ends in ${daysLeft} day(s), on ${fmt(expires)}. ` +
      `If your payment method changed, update it to avoid an interruption.${offline}` };
  }
  return { level: 'ok', text: `Subscription active until ${fmt(expires)}.${offline}` };
}

/** True when a re-validation shows the key was renewed (same key, later expiry). */
export function wasRenewed(before, after) {
  return Boolean(before?.expiresAt && after?.expiresAt && new Date(after.expiresAt) > new Date(before.expiresAt));
}

const fmt = (d) => (d ? d.toISOString().slice(0, 10) : '?');
