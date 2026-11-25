# macOS app (SwiftUI): trial → buy → activate → validate on start

**Acme Notes for Mac** is a pretend note editor that only works with a license or an active trial.
Same flow as the [.NET desktop sample](../desktop-dotnet/), built with SwiftUI and the
[ExisOne Swift package](https://github.com/exisllc/ExisOne.Swift):
- trial on first run, Buy / Renew, activate with key + email;
- validate on every start;
- offline grace period (7 days) with a clear message;
- version check;
- deactivate to move to another Mac.

## Prerequisites

- macOS 13 or newer, Xcode 15 or newer (Swift 5.9).
- An ExisOne product, a `verify` access token and a test key: see
  [the 5-minute setup](../../README.md#5-minute-setup).

## Run

```bash
cd samples/macos-swift
cp .env.example .env      # then fill in EXISONE_ACCESS_TOKEN and EXISONE_PRODUCT
swift run
```

Or open the folder in Xcode (File → Open… → `Package.swift`) and set the `EXISONE_*` values as
environment variables in the scheme.

## The lines that matter

[`LicenseManager.swift`](Sources/AcmeNotesMac/LicenseManager.swift):

```swift
do {
    // No key = trial check: the server starts (or continues) this Mac's trial.
    let r = try await client.validate(hardwareId: hardwareId, productName: settings.product,
                                      activationKey: key, version: appVersion)
    saveCache(...)                         // status, expiry, features, time of this answer
    state = fromServer(r, key: key)
} catch {
    state = fromCache(cached, error: error)   // offline: last good answer inside the grace period
}
```

```swift
let r = await client.activate(activationKey: key, email: email, hardwareId: hardwareId,
                              productName: settings.product, version: appVersion)
guard r.success else { return (false, r.errorCode ?? r.errorMessage ?? "Activation failed.") }
```

The SwiftUI view ([`AcmeNotesApp.swift`](Sources/AcmeNotesMac/AcmeNotesApp.swift)) only shows
`license.state` and calls `activate` / `deactivate`.

## Notes

- For a shipped app, put the configuration in `Info.plist` or your build settings instead of `.env`.
- The access token ships inside your app, so treat it as public and give it only `verify`.
- The Swift package (0.7.0) does not have seat usage or a hardware ID override yet; the .NET,
  Node and Python SDKs do.
- The cache (`~/Library/Application Support/Acme Notes/license-cache.json`) is a convenience for
  offline use, not copy protection.

More: [Swift SDK docs](https://www.exisone.com/docs-sdk-swift.html) · [ExisOne](https://www.exisone.com)
