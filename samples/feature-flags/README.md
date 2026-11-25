# Feature flags: unlock features per license

**Acme Studio** (a pretend console app) turns the features on a license key into on/off switches.

How features work in ExisOne:
- You define **features** on a product (for example `Export`, `Batch`, `Cloud Sync`).
- A key carries the names of the features it includes:
  - the product's default features at issue time;
  - plus any a **plan** grants (for tiers like Basic / Pro);
  - plus paid **add-ons** bought later for that key.
- **Validation returns the names.** The app checks the name, never a price or plan name.

What it shows:
- A menu with each feature unlocked or locked ("upgrade to unlock").
- A feature command that's refused, with a clear message, when the license lacks it.
- Offline: features come from the last good answer inside the grace period.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer (Windows, macOS or Linux).
- An ExisOne product with features named `Export`, `Batch` and `Cloud Sync`, a `verify` access
  token, and a key that includes some of them. See [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/feature-flags
export EXISONE_ACCESS_TOKEN=exo_at_...     # PowerShell: $env:EXISONE_ACCESS_TOKEN = "..."
export EXISONE_PRODUCT="Acme Studio"
dotnet run -- activate XXXX-XXXX-XXXX-XXXX you@example.com
dotnet run                                 # which features this license unlocks
dotnet run -- export                       # allowed or "not included in your license"
```

```
Licensed until 11/25/2027.

  [x] edit    Edit projects (every license)
  [x] export  Export to PDF and PNG
  [ ] batch   Process a whole folder at once  (upgrade to unlock)
  [ ] sync    Sync projects between computers  (upgrade to unlock)
```

## The lines that matter

[`Program.cs`](Program.cs):

```csharp
var state = await license.CheckAsync();   // validates; Features = names on this key
var unlocked = new HashSet<string>(state.Features ?? [], StringComparer.OrdinalIgnoreCase);
bool Has(string feature) => unlocked.Contains(feature);

if (!Has("Export")) { Console.Error.WriteLine("\"Export\" is not included in your license. Upgrade to unlock it."); return 1; }
```

The licensing files (`LicenseService.cs`, `LicenseCache.cs`, `LicenseSettings.cs`) are copied
unchanged from [desktop-dotnet](../desktop-dotnet/).

## Notes

- During a trial, validation returns the product's default features, so trial users see what
  they'd get.
- Check features where the feature runs, not only in the UI.
- Offline codes embed the features they were issued with (see [offline-activation](../offline-activation/)).

More: [.NET SDK docs](https://www.exisone.com/docs-sdk.html) · [ExisOne](https://www.exisone.com)
