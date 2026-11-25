# Command-line tool (Node.js): a license check before each command

**acme-cli** is a pretend tool whose real command (`convert`) needs a license or an active trial.

What it shows:
- **One gate before the real work**, and a non-zero exit code when it fails, so scripts can tell.
- **Messages for humans**: the reason it refused, and how to fix it.
- **No noise on success.** Offline and trial hints go to stderr, so stdout stays clean for piping.
- `activate`, `deactivate` and `status` subcommands.
- Offline use inside the grace period (7 days by default) still passes the gate.

## Prerequisites

- Node.js 20.12 or newer.
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/cli-tool
cp .env.example .env      # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
npm install
node src/cli.mjs status                                    # trial, or "enter a license key"
node src/cli.mjs activate XXXX-XXXX-XXXX-XXXX --email you@example.com
node src/cli.mjs convert report.txt                        # licensed command
node src/cli.mjs deactivate
```

Exit codes: `0` ok, `1` not licensed or the call failed, `2` usage or configuration problem.

## The lines that matter

[`src/cli.mjs`](src/cli.mjs), the gate:

```js
case 'convert': {
  const s = await license.check();
  if (!canUse(s)) {
    console.error(s.message);
    return 1;
  }
  ...
```

[`src/license.mjs`](src/license.mjs) does the work: it validates with the server, saves the
answer, and falls back to the saved answer for up to `EXISONE_GRACE_DAYS` when the server can't
be reached. It's the same file as in [desktop-electron](../desktop-electron/).

## Notes

- A tool that runs many times a minute should not call the server every time: reuse the saved
  answer for a few hours when it's recent (add a freshness check in `check()`).
- The access token ships with your tool, so treat it as public and give it only `verify`.

More: [Node.js SDK docs](https://www.exisone.com/docs-sdk-node.html) · [ExisOne](https://www.exisone.com)
