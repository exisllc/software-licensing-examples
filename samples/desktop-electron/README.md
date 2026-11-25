# Desktop app (Electron): licensing in the main process

**Acme Sketch** is a pretend drawing app that only works with a license or an active trial.
Same flow as the [.NET desktop sample](../desktop-dotnet/): trial on first run → Buy → activate
→ validate on every start, offline grace period, version check, deactivate.

What's Electron-specific:
- **The SDK runs in the main process.** It needs Node APIs (hardware ID, files), and keeping it
  there keeps the access token out of the web page.
- **The window gets four functions and nothing else** (`check`, `activate`, `deactivate`, `buy`)
  through a `contextBridge` preload, with `contextIsolation` and `sandbox` on.
- **The license cache** lives in Electron's per-user `userData` folder.

## Prerequisites

- Node.js 20.12 or newer.
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/desktop-electron
cp .env.example .env      # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
npm install
npm start
```

## The lines that matter

[`src/main.mjs`](src/main.mjs): licensing lives in the main process.

```js
license = new LicenseManager(settings, {
  appVersion: app.getVersion(),
  cacheFile: path.join(app.getPath('userData'), 'license-cache.json'),
});
ipcMain.handle('license:check', () => license.check());
ipcMain.handle('license:activate', (_e, key, email) => license.activate(String(key), String(email)));
```

[`src/preload.cjs`](src/preload.cjs): the only licensing surface the page can reach.

```js
contextBridge.exposeInMainWorld('license', {
  check: () => ipcRenderer.invoke('license:check'),
  activate: (key, email) => ipcRenderer.invoke('license:activate', key, email),
  ...
});
```

[`src/license.mjs`](src/license.mjs) (`check()`): ask the server, fall back to the cache.

```js
const r = await this.client.validate({ activationKey: key ?? '', hardwareId: this.hardwareId,
  productName: this.settings.product, version: this.appVersion });   // empty key = trial check
...
} catch (err) {
  return this.#fromCache(cached, serverStatus(err));   // offline: last good answer inside the grace period
}
```

## Notes

- In a packaged app, set the configuration at build time (or bundle a config file). `.env` is a
  development convenience.
- The access token ships inside your app, so treat it as public and give it only `verify`.
- `license.mjs` is the same file as in [cli-tool](../cli-tool/) and [subscriptions](../subscriptions/).

More: [Node.js SDK docs](https://www.exisone.com/docs-sdk-node.html) · [ExisOne](https://www.exisone.com)
