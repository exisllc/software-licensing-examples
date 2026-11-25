# Subscriptions: expiry, renewal and grace in the app

How a subscription license looks from inside your app, and what the app should do as the end
date approaches and passes.

How ExisOne handles subscriptions (Stripe or PayPal):
- **A renewal keeps the same key** and moves its expiry date forward on the server. The customer
  never re-enters anything.
- **The expiry already includes a few days of slack** past the next charge, so a slightly late
  renewal never cuts anyone off.
- **Loyalty unlock (optional, per product).** After N payments a key can become perpetual: the
  subscription is cancelled and the key no longer expires.

So the app only has to:
1. **Re-validate regularly** (on start, and daily for apps that stay open) to pick up the new
   expiry after a renewal.
2. **Show the right message**: active, renews or ends soon, ended.
3. **Not lock anyone out because of a network problem** (the offline grace period).

## Prerequisites

- Node.js 20.12 or newer.
- An ExisOne product sold as a subscription, a `verify` access token and a key: see
  [the 5-minute setup](../../README.md#5-minute-setup). To see "renews soon" without waiting, set a
  test key's expiry a few days out on its license page in the dashboard.

## Run

```bash
cd samples/subscriptions
npm install
npm run simulate                 # the messages for sample dates; no server or token needed
cp .env.example .env             # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
node src/index.mjs activate XXXX-XXXX-XXXX-XXXX you@example.com
node src/index.mjs status
CHECK_INTERVAL_MINUTES=1 node src/index.mjs watch   # change the expiry in the dashboard and watch it notice
```

`npm run simulate` prints:

```
30 days left           [ok] Subscription active until 2026-07-01.
3 days left            [warn] Your subscription renews or ends in 3 day(s), on 2026-06-04. If your payment method changed, update it to avoid an interruption.
3 days left, offline   [warn] ... (offline: last confirmed by the server earlier)
ended                  [error] Your subscription ended on 2026-05-30. Renew to keep using Acme Notes; your data is safe.
perpetual              [ok] Licensed. No renewal needed.
```

## The lines that matter

[`src/subscription.mjs`](src/subscription.mjs): a pure function from license state to message,
easy to test.

```js
const daysLeft = Math.ceil((expires - now) / DAY);
if (daysLeft <= RENEWS_SOON_DAYS) return { level: 'warn', text: `Your subscription renews or ends in ${daysLeft} day(s)...` };
```

[`src/index.mjs`](src/index.mjs) (`watch`): re-check periodically and notice a renewal.

```js
const next = await license.check();
if (wasRenewed(last, next)) console.log(`Renewed: thank you! New end date ...`);
```

## Notes

- Don't add your own grace period on top of the expiry date: the server's expiry already has it.
  The app's grace period is only for being offline.
- A subscription's key can't tell the app whether it will auto-renew, hence "renews or ends".
- `license.mjs` is the same file as in [cli-tool](../cli-tool/).

More: [Node.js SDK docs](https://www.exisone.com/docs-sdk-node.html) · [ExisOne](https://www.exisone.com)
