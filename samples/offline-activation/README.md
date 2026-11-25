# Offline activation: machines that never go online

**Acme Lab** is licensed on air-gapped machines (labs, factories, secure networks) with a signed
activation code instead of a server call.

How it works:
1. The app shows the machine's **hardware ID**.
2. The customer sends it to you (email, phone, USB stick).
3. You create an **Offline Activation Code** for that hardware ID on the license keys page of the
   ExisOne dashboard.
4. The customer enters the code. The app checks it **locally**, and again on every start.

Security model:
- ExisOne **signs** the code (RSA-SHA256) with your private key, which never leaves the server.
- The app only **verifies** with your **public** key, which is safe to ship.
- The code contains the product, hardware ID, expiry, email and features. Changing any of them
  breaks the signature.
- No access token is needed: this app makes no network calls at all.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer (Windows, macOS or Linux).
- Your ExisOne **public key**: dashboard → **Tenant Keys**. Save it as `public-key.pem` in this
  folder, or set `EXISONE_PUBLIC_KEY`.
- A product named `Acme Lab` (or change `ProductName` in `Program.cs`).

## Run

```bash
cd samples/offline-activation
dotnet run -- id                 # 1. show this machine's hardware ID
#                                  2-3. create an offline code for it in the dashboard
dotnet run -- enter <code>       # 4. activate
dotnet run                       # every start: verified locally
dotnet run -- remove
```

## The lines that matter

[`Program.cs`](Program.cs), `Check`:

```csharp
var r = client.ValidateOffline(code, hardwareId);   // signature, hardware ID and expiry, all local
if (!r.IsValid)
    message = r.HardwareMismatch ? "This code was made for a different machine..."
            : r.IsExpired ? $"This code expired on {r.ExpirationDate:d}..."
            : "This is not a valid activation code...";

// Every product you sell is signed with the same key, so also check the code is for THIS product.
if (!string.Equals(r.ProductName, ProductName, StringComparison.OrdinalIgnoreCase)) ...
```

Don't skip the product check: without it, a code for any of your products unlocks all of them.

## Notes

- The expiry is checked against the machine's clock, which the user controls. For high-value
  licenses, issue shorter codes and renew them.
- An offline code can't be revoked remotely. It stops working at its expiry date.
- Features added to a key later are not in an existing code; issue a new code.
- Apps that are *sometimes* online can accept both: `ValidateSmartAsync(keyOrCode, hardwareId)`
  validates offline codes locally and normal keys with the server.
- The same `ValidateOffline` exists in the Node (`validateOffline`), Python (`validate_offline`)
  and Swift (`validateOffline`) SDKs.

More: [.NET SDK docs](https://www.exisone.com/docs-sdk.html) · [ExisOne](https://www.exisone.com)
