# Software licensing examples

**Add licensing to your app in 10 minutes**: trials, license keys, activation and device
limits, subscriptions, feature tiers and offline activation. Runnable samples for .NET, Electron,
macOS (Swift), Python, Node.js command-line tools and web backends, using
[ExisOne](https://www.exisone.com).

Moving from Lemon Squeezy? Your app changes **one hostname** and your customers keep their keys:
see [lemon-squeezy/](lemon-squeezy/).

## Pick a sample

| You're building | Sample | Language |
|---|---|---|
| A Windows desktop app | [desktop-dotnet](samples/desktop-dotnet/) | C# (WinForms) |
| A cross-platform desktop app with web tech | [desktop-electron](samples/desktop-electron/) | JavaScript |
| A Mac app | [macos-swift](samples/macos-swift/) | Swift (SwiftUI) |
| A Python desktop app | [python-app](samples/python-app/) | Python (Tkinter) |
| A command-line tool | [cli-tool](samples/cli-tool/) | Node.js |
| A web app or API (license per customer account) | [server-side](samples/server-side/) | Node.js (Express) |

| You need | Sample | Language |
|---|---|---|
| Licensing on machines with no internet | [offline-activation](samples/offline-activation/) | C# |
| One license per machine, seat limits, moving to a new machine | [hardware-locked](samples/hardware-locked/) | Python |
| Subscriptions: expiry, renewal, "renews soon" | [subscriptions](samples/subscriptions/) | Node.js |
| Features unlocked per license or plan | [feature-flags](samples/feature-flags/) | C# |

Every desktop sample does the same things, so pick by language:
- **Trial on first run.** The server starts the trial; the app just asks.
- **Buy** button, then **activate** with key + email.
- **Validate on every start.**
- **Offline grace period.** No lock-out when the network is down.
- **Version check.**
- **Deactivate** to move to another machine.

## 5-minute setup

You need an ExisOne account ([free trial](https://www.exisone.com)). In the dashboard:

1. **Products** → create a product, for example `Acme Notes`. Optionally give it trial days and
   features (`Export`, ...). The product name is what you put in `EXISONE_PRODUCT`.
2. **Access Tokens** → create a token with only **Verify license codes** ticked (the default).
   Copy it; it's shown once. That's `EXISONE_ACCESS_TOKEN`.
3. **License Keys** → generate a key for your product with your own email. That's the key you
   enter in the sample.
4. *(Offline activation only)* **Tenant Keys** (crypto keys) → copy your public key.

Then in the sample's folder copy `.env.example` to `.env` (or `appsettings.Local.json` for .NET),
fill in the values, and follow its README.

| Setting | Meaning |
|---|---|
| `EXISONE_BASE_URL` | `https://www.exisone.com` (the default) |
| `EXISONE_ACCESS_TOKEN` | Your **verify-only** token |
| `EXISONE_PRODUCT` | Product name exactly as in the dashboard |
| `EXISONE_PUBLIC_KEY` | PEM public key, only for offline codes |
| `EXISONE_BUY_URL` | Your checkout page, opened by the Buy button |
| `EXISONE_GRACE_DAYS` | Days the app keeps working offline (default 7) |

## About the access token in your app

Every app that validates with ExisOne carries an access token, and anything you ship can be
taken apart, so **treat that token as public**:
- give it only the **Verify** permission (plus **Send emails** if the app sends support tickets);
- never put a token with **Generate license codes** in an app you distribute.

ExisOne refuses account changes from API tokens (other tokens, signing keys, payment and email
settings, products), so a verify token can only check licenses. Your configuration files are
git-ignored; nothing secret is committed here.

## SDKs

The samples install the published SDKs:

| | Package | Docs |
|---|---|---|
| .NET 8+ | [`ExisOne.Client`](https://www.nuget.org/packages/ExisOne.Client) on NuGet | [docs](https://www.exisone.com/docs-sdk.html) |
| Node.js 18+ / Electron | [`exisone-client`](https://www.npmjs.com/package/exisone-client) on npm | [docs](https://www.exisone.com/docs-sdk-node.html) |
| Python 3.8+ | [`exisone-client`](https://pypi.org/project/exisone-client/) on PyPI | [docs](https://www.exisone.com/docs-sdk-python.html) |
| Swift (macOS 12+, iOS 15+) | [`ExisOne.Swift`](https://github.com/exisllc/ExisOne.Swift) via Swift Package Manager | [docs](https://www.exisone.com/docs-sdk-swift.html) |

Using another language? Every sample's calls map to a small [HTTP API](https://www.exisone.com/docs.html).

## Not supported yet

These samples only show what the SDKs do today. Not available yet:
- **A built-in validation cache and grace period in the SDKs.** The samples include a small
  helper for it (`LicenseCache`, `license.mjs`, `licensing.py`).
- **Apps without an access token.** Today every app embeds a verify-only token, as explained above
  (the Lemon Squeezy-compatible endpoints already need none).
- **Seat usage and the hardware ID override in the Swift SDK.** The .NET, Node and Python SDKs have them.
- **A Rust or Go SDK** (so no Tauri sample yet). The [HTTP API](https://www.exisone.com/docs.html)
  works from any language.
- **Self-service offline codes.** You create each offline code in the dashboard.

Want one of these, or a sample for your stack? Open an
[issue](https://github.com/exisllc/software-licensing-examples/issues/new/choose) or a
[discussion](https://github.com/exisllc/software-licensing-examples/discussions).

## Contributing, security, license

[CONTRIBUTING.md](CONTRIBUTING.md) · [SECURITY.md](SECURITY.md) (please report vulnerabilities
privately) · [MIT License](LICENSE).

[exisone.com](https://www.exisone.com) · [Documentation](https://www.exisone.com/docs.html)
