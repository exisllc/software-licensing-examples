# Moving from Lemon Squeezy to ExisOne

Your app calls the Lemon Squeezy **License API** to activate, validate and deactivate license
keys. ExisOne answers the same three calls in the same format, so the change in your app is
**one hostname**:

```diff
- https://api.lemonsqueezy.com/v1/licenses/validate
+ https://licenses.exisone.com/v1/licenses/validate
```

Your customers keep their license keys. You import them (with their activations) from Lemon
Squeezy, so a key bought last year keeps working in the new version of your app with no action
from the customer.

[Before/after code](before-after/) · [Migration checklist](checklist.md) · [Response capture tool](capture-tool/)

**Migrating from Lemon Squeezy? ExisOne is free through at least 2027-04-30** while you move.
Want help planning it? [Talk to the founder](https://exisone.com/contact.html).

## What changes

- **The hostname** your app calls: `licenses.exisone.com` instead of `api.lemonsqueezy.com`.
  Paths, fields (`license_key`, `instance_name`, `instance_id`), JSON bodies and HTTP status codes
  follow Lemon Squeezy's documented behavior, with the same rate limit (60 requests per minute).
  Before you switch, the **Compare with Lemon Squeezy** tool on the import page checks your own
  keys against Lemon Squeezy. If you find any difference, [report it](https://github.com/exisllc/software-licensing-examples/issues/new/choose)
  and we'll fix it.
- **Where new keys come from.** New sales are made through ExisOne checkout (Stripe or PayPal),
  and keys for products you map to Lemon Squeezy products are issued in the Lemon Squeezy format
  (uppercase UUID), so your app's key-format checks keep passing.

## What stays the same

- **License keys.** Imported keys keep their exact strings.
- **Activations.** Imported instances keep their `instance_id`; activation counts and limits carry over.
- **`meta` ids.** `store_id`, `product_id` and `variant_id` stay the Lemon Squeezy values, so an app
  that checks them (as Lemon Squeezy recommends) keeps working.
- **Expiry and disabled keys.** Expiry dates and the disabled flag come across in the import.
- **No access token.** Like Lemon Squeezy, these three endpoints need no API key in your app.

## What is not covered

- The Lemon Squeezy **main API** (`/v1/license-keys`, `/v1/orders`, ...) and Lemon Squeezy-format
  **webhooks**. If your *server* uses those, it needs changes; ExisOne has its own
  [API](https://www.exisone.com/docs.html) and webhooks.
- **Billing.** Subscriptions are not moved for you; see the next section.

## Subscriptions

Subscription billing stays in Lemon Squeezy until you move those customers.

- With **Keep in sync** turned on during the import, ExisOne keeps following Lemon Squeezy:
  renewals extend the key's expiry, keys you disable or re-enable there follow, and keys sold in
  Lemon Squeezy after the import are picked up (also on first use, when an unknown key is
  validated).
- At **cutover**, sync stops. Subscription keys then keep the last expiry date ExisOne knew, so
  decide before then how those customers continue: for example a new ExisOne subscription
  (Stripe or PayPal; it issues a new key, and its renewals keep extending that key), or extending
  the imported key's expiry from the license page in the dashboard.

## When to migrate

Early. The part that takes longest is not the import, it's your users installing the app update
that contains the new hostname. Ship that update well before you stop using Lemon Squeezy. Old
versions keep calling Lemon Squeezy, so keep your Lemon Squeezy store running, and sync on, until
most of your active users have updated.
The [checklist](checklist.md) has the order of steps.

## How to start

1. Create an ExisOne account at [exisone.com](https://www.exisone.com).
2. In the dashboard: **Tools → Import from Lemon Squeezy**. Paste a Lemon Squeezy API key, map
   your products and run a **dry run**: it reports what would be
   imported and any problems, and writes nothing.
3. Follow the [checklist](checklist.md).

**Your Lemon Squeezy API key.** Lemon Squeezy keys can't be limited to read-only, so create a
dedicated key for the import and revoke it in Lemon Squeezy after cutover. ExisOne only reads
with it, and you can only import a store that key can see. It is stored (encrypted) only if you
turn on **Keep in sync**, and deleted when you cut over, stop syncing, or Lemon Squeezy rejects it.

Questions: [ExisOne docs](https://www.exisone.com/docs.html) or open a
[discussion](https://github.com/exisllc/software-licensing-examples/discussions).
