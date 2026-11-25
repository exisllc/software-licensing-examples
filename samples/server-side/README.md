# Server side (Node.js / Express): license each customer account of your API

**Acme Reports API** is a pretend SaaS API. Each customer account activates a license key once,
and the licensed endpoints check it on every request.

What it shows:
- **One seat per customer account.** On a server there's no machine to fingerprint, so the seat id
  is a stable string per account (`acme-reports:account:42`). The SDK accepts any string as the
  hardware ID.
- **Express middleware** `gate.require()`: `402` when the account has no valid license, `403`
  when the license lacks a feature (`gate.require('Export')`).
- **No license-server call per request.** Answers are cached in memory for 10 minutes.
- **No outage when the license server is down.** The last good answer is used for up to 24 hours.
  After that the API returns `503`, not a misleading "not licensed".
- Activate and release endpoints for the account.

## Prerequisites

- Node.js 20.12 or newer.
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup). Add a feature named `Export` to the product
  to try the feature check.

## Run

```bash
cd samples/server-side
cp .env.example .env      # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
npm install
npm start
```

```bash
curl -H "X-Account-Id: 42" localhost:3000/reports                       # 402: no license
curl -H "X-Account-Id: 42" -H "Content-Type: application/json" \
     -d '{"key":"XXXX-XXXX-XXXX-XXXX","email":"you@example.com"}' localhost:3000/license/activate
curl -H "X-Account-Id: 42" localhost:3000/reports                       # 200
curl -H "X-Account-Id: 42" localhost:3000/reports/export                # 200 or 403 (feature)
curl -X POST -H "X-Account-Id: 42" localhost:3000/license/deactivate
```

`X-Account-Id` stands in for your real authentication.

## The lines that matter

[`src/license-gate.mjs`](src/license-gate.mjs):

```js
seatId(accountId) { return `acme-reports:account:${accountId}`; }   // stable: changing it needs a new activation

const r = await this.client.validate({
  activationKey: key, hardwareId: this.seatId(accountId), productName: this.product, version: this.appVersion,
});
...
} catch (err) {
  // License server unreachable: keep serving on the last good answer inside the grace window.
  if (hit && hit.status === 'licensed' && Date.now() - hit.checkedAt < this.graceMs) return view(hit, true);
```

[`src/server.mjs`](src/server.mjs):

```js
app.get('/reports', gate.require(), handler);
app.get('/reports/export', gate.require('Export'), handler);
```

## Notes

- Here the token stays on your server, but it still only needs `verify`.
- Use a **corporate (multi-seat) key** to let one customer use several accounts or servers; each
  seat id uses one seat.
- For Docker or multi-user apps that use the SDK's own hardware ID, the .NET, Node and Python SDKs
  also have a `hardwareIdOverride` option. See the [Docker guide](https://www.exisone.com/docs-sdk-ai-docker.html).
- The in-memory cache is per process. With several instances, put it in Redis or your database.

More: [Node.js SDK docs](https://www.exisone.com/docs-sdk-node.html) · [ExisOne](https://www.exisone.com)
