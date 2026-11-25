# Desktop app (.NET WinForms): trial → buy → activate → validate on start

**Acme Notes** is a pretend note editor that only works with a license or an active trial.

What it shows:
- **Trial on first run.** The first start asks the server about this PC with no key, and the server
  starts its trial (if your product has trial days).
- **Buy / Renew** button that opens your checkout page.
- **Activate** with key + email; the key is then bound to this PC.
- **Validate on every start**, with the server's answer: licensed, trial, trial ended, expired,
  not active on this PC.
- **Offline grace period.** If the server can't be reached, the last good answer keeps the app
  working for 7 days, with a clear "working offline" message.
- **Version check.** "Version X is available", and a hard stop if your product's minimum version
  is newer than the app.
- **Deactivate** to move the license to another PC.

## Prerequisites

- Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer.
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup). Give the product some trial days to see the trial.

## Run

```powershell
cd samples/desktop-dotnet
$env:EXISONE_ACCESS_TOKEN = "exo_at_..."   # verify-only token
$env:EXISONE_PRODUCT = "Acme Notes"        # your product's name in ExisOne
dotnet run
```

Or put the values in `appsettings.Local.json` (git-ignored), same shape as `appsettings.json`.

Try: start it (trial), **Enter license key…** (activate), restart (validated), turn off Wi-Fi and
restart (working offline), **Deactivate this PC**.

## The lines that matter

All licensing is in [`LicenseService.cs`](LicenseService.cs); the form only shows its result.

```csharp
// Start-up check. No key yet = trial check.
var (_, status, expires, features, serverVersion, minVersion) =
    await _client.ValidateAsync(_hardwareId, _settings.Product, key, AppVersion).WaitAsync(Timeout);
_cache.Save(new CachedLicense { /* status, expiry, features, LastValidatedUtc = now */ });
return FromServer(status, key, expires, features, serverVersion, minVersion);
...
catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
{
    return FromCache(cached, ServerStatus(ex));   // last good answer, if inside the grace period
}

// Activation binds the key to this PC.
var r = await _client.ActivateAsync(key, email, _hardwareId, _settings.Product, AppVersion);
if (!r.Success) return (false, r.ErrorCode ?? "Activation failed.");   // the server's message, e.g. a seat limit

// Deactivation frees the seat.
await _client.DeactivateAsync(key, _hardwareId, _settings.Product);
```

`LicenseService.cs`, `LicenseCache.cs` and `LicenseSettings.cs` have no WinForms code: copy them
into a WPF, Avalonia, MAUI or console app as they are (the [feature-flags](../feature-flags/)
sample does exactly that).

## Notes

- The access token ships inside your app, so treat it as public and give it only `verify`.
  ExisOne refuses everything else from it.
- The cache (`%LOCALAPPDATA%\Acme Notes\license-cache.json`) is a convenience for offline use, not
  copy protection: the server is asked on every start, and the cache is ignored on another PC.
- Offline (air-gapped) customers: see [offline-activation](../offline-activation/).
- This uses the native ExisOne flow (license key + email). Lemon Squeezy-style code (license key +
  `instance_name`) is in [lemon-squeezy/before-after](../../lemon-squeezy/before-after/).

More: [.NET SDK docs](https://www.exisone.com/docs-sdk.html) · [ExisOne](https://www.exisone.com)
