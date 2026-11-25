# ls-capture

Records exactly how the Lemon Squeezy **License API** (`/v1/licenses/activate`, `/validate`,
`/deactivate`) responds, success and failure, and writes it to one redacted JSON file. We use it
to make ExisOne's Lemon Squeezy–compatible endpoints answer byte-for-byte like Lemon Squeezy does.

**Why it's public:** if your app depends on a Lemon Squeezy response detail you're not sure we match
(an error message, a field, a status code), run this against your own test-mode key and send us the
file (info@exisone.com, or attach it to a GitHub issue after reviewing it). It also lets you see
exactly what Lemon Squeezy returns for every case before you switch. You don't need it to migrate.

## What it does to your account

- It talks **only** to `https://api.lemonsqueezy.com`. Nothing is sent anywhere else, and no
  telemetry is collected. The output file stays on your machine until you choose to send it.
- It works **only on a test-mode license key**. It checks the key's `test_mode` flag through the
  API and stops before making any change if the key is live.
- To reach the expired, disabled, and limit-reached cases, it **temporarily** changes that test
  key's `activation_limit`, `expires_at`, and `disabled` through the API. On exit, even after an
  error, it restores the original values. It also deactivates every instance it created; their
  names all start with `ls-capture-`.
- Instances that were already on the key before the run are not touched.
- It makes about 60 License API calls, spaced at least 1.1 s apart, so it stays under Lemon
  Squeezy's 60/min limit. A normal run takes about 1–2 minutes.

If the tool prints `RESTORE FAILED`, follow the instruction it prints to fix the key in the
dashboard.

## What you need

1. The [.NET 8 SDK or runtime](https://dotnet.microsoft.com/download), or newer.
2. In your Lemon Squeezy dashboard, switch to **Test mode** and:
   - create an **API key** (Settings → API). It must be a test-mode key.
   - make a test purchase of a product that has license keys enabled, and copy the
     **license key** from that test order.
   - optionally, make a second test purchase and copy its key. This enables the
     "instance of another key" cases.

## Run it

Secrets are passed as environment variables, so they don't end up in your shell history.

macOS / Linux:
```bash
export LS_API_KEY='...'            # test-mode API key
export LS_LICENSE_KEY='...'        # test-mode license key
export LS_SECOND_LICENSE_KEY='...' # optional
dotnet run -c Release
```

Windows PowerShell:
```powershell
$env:LS_API_KEY = '...'
$env:LS_LICENSE_KEY = '...'
$env:LS_SECOND_LICENSE_KEY = '...'   # optional
dotnet run -c Release
```

Options:

| Option | Effect |
|---|---|
| `--out <file>` | Output path (default `ls-capture-<utc-timestamp>.json`) |
| `--include-rate-limit` | Also sends rapid requests until Lemon Squeezy returns HTTP 429, to record the rate-limit response. Adds about 2 minutes. |
| `--keep-ids` | Keep real numeric ids (store, order, customer). By default they are replaced. |
| `--confirm-test-key` | Run even when test mode can't be confirmed through the API. Use only after checking in the dashboard that it is a test key. |

## What's in the output file

For each case: the case id, the request parameters (redacted), the HTTP status, selected response
headers (`content-type`, rate-limit headers, CORS headers), and the response body.

**Redacted before the file is written:**
- License keys become `<LICENSE_KEY>`.
- `key_short` becomes `XXXX-<REDACTED>`.
- Emails become `redacted@example.com`.
- Customer, product, and variant names become placeholders.
- Numeric ids become consistent fake integers, unless you pass `--keep-ids`.

Timestamps, statuses, error messages, and the JSON structure are kept unchanged, because they are
what we need to compare. As a final check, the tool refuses to write the file if any key or the API
key appears anywhere in it.

Please look the file over before sending it.

## Cases covered

- **validate:**
  - success with and without `instance_id`
  - unknown, malformed, empty, or deactivated `instance_id`
  - an instance belonging to another key
  - unknown, missing, or empty key
  - key with letter case changed or surrounding spaces
  - JSON body instead of form; no `Accept` header; `GET`; CORS preflight and an `Origin` header
  - expired and disabled keys, each with and without an instance
- **activate:**
  - success, and activating again with the same name
  - unknown key
  - missing, empty, too-short, or too-long `instance_name`
  - missing key, or missing all parameters
  - JSON body
  - limit reached, expired, disabled
- **deactivate:**
  - success; already deactivated
  - unknown instance, or an instance of another key
  - unknown key
  - missing parameters
  - expired, disabled
- **Rate limit (optional):** the first HTTP 429 response.
