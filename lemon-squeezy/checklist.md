# Lemon Squeezy → ExisOne migration checklist

Work top to bottom. Nothing here touches your Lemon Squeezy store until the last step (the
import only reads from it), and every step before cutover can be repeated.

## 1. Import

- [ ] Create your ExisOne account and products; under **Tools → Import from Lemon Squeezy**
      map each Lemon Squeezy product (variant) to an ExisOne product.
- [ ] Run a **dry run**. It writes nothing and lists what would be imported, plus any blockers
      (for example a subscription key whose expiry needs a decision).
- [ ] Run the **import** with **Keep in sync** on. It writes nothing to Lemon Squeezy. Re-running
      is safe; it only changes what changed in Lemon Squeezy.

## 2. Prepare your app (start early; it takes your users longest)

- [ ] **Make the license host a setting, not a constant.** Read the base URL from your config
      (with `https://licenses.exisone.com/v1/licenses` as the default), so a future change doesn't
      need another app update.
- [ ] **Optional: put a hostname you control in front.** If your app calls, say,
      `licenses.yourapp.com`, you can move vendors later without an app update. It must be a
      reverse proxy that forwards `/v1/licenses/*` to `licenses.exisone.com` (a plain CNAME won't
      work: the TLS certificate has to match your hostname). Note that ExisOne's rate limit
      (60/min) counts per client IP, and behind a proxy every user shares the proxy's IPs. Talk to
      us before sending real traffic through one.
- [ ] **Cache the last good answer and allow a grace period.** Store the last successful
      validation (status, expiry, time) and keep the app working offline for a few days. Never
      lock a paying user out because a request failed. See [the samples](../samples/) for a
      ready-made helper in .NET, Node, Python and Swift.
- [ ] **Keep checking `meta.store_id` / `product_id`** if you already do: imported keys keep the
      Lemon Squeezy values.
- [ ] **Switch the hostname** (see [before-after](before-after/)) and test against a key you
      imported in step 1.
- [ ] **Ship the update** as early as you can. Old versions keep calling Lemon Squeezy, which is
      fine while your store is still running.

## 3. Compare (shadow mode)

- [ ] On the import page, run **Compare with Lemon Squeezy**. It sends `validate` for a sample of
      your keys to both services and reports every difference, explaining the expected ones
      (for example a key you disabled in ExisOne). Investigate anything flagged.
- [ ] Spot-check by hand: validate, activate and deactivate a few real keys with the new app.

## 4. Run both

- [ ] Start selling through ExisOne checkout (Stripe or PayPal). Keys sold through ExisOne only
      work in app versions that call ExisOne, so switch checkout once your download page serves
      the updated version, or keep selling through Lemon Squeezy until then.
- [ ] Keep sync on while old app versions are still in use and while subscriptions still bill in
      Lemon Squeezy. Watch for plan-limit alerts on the dashboard.
- [ ] Move subscription customers (see [Subscriptions](README.md#subscriptions)).

## 5. Cut over

- [ ] When most active users run the new version and subscriptions are handled: **Cut over** on
      the import page. ExisOne runs a final sync, stops syncing, and deletes your stored Lemon
      Squeezy API key.
- [ ] Only after that, wind down your Lemon Squeezy store.
