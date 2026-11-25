// Acme Lab: licensing on a machine that never goes online (labs, factories, secure networks).
//
// 1. The app shows this machine's hardware ID.            dotnet run -- id
// 2. The customer sends it to you (email, phone, USB stick).
// 3. You create an Offline Activation Code for that hardware ID in the ExisOne dashboard.
// 4. The customer enters the code; the app checks it locally. dotnet run -- enter <code>
// 5. On every start the app re-checks the saved code locally. dotnet run
//
// No network and no access token: the code is signed on the ExisOne server with your private key,
// and the app only verifies the signature with your PUBLIC key. The private key never leaves the server.
using ExisOne.Client;

const string ProductName = "Acme Lab";   // must match the product the code was created for

var publicKey = LoadPublicKey();
if (publicKey == null)
{
    Console.Error.WriteLine("No public key. Put your PEM public key (dashboard: Tenant Keys) in public-key.pem next to this project, or set EXISONE_PUBLIC_KEY.");
    return 2;
}

// BaseUrl is required by the SDK but is never called here; offline validation is local.
var client = new ExisOneClient(new ExisOneClientOptions { BaseUrl = "https://www.exisone.com", OfflinePublicKey = publicKey });
var hardwareId = client.GenerateHardwareId();
var saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Acme Lab", "offline-code.txt");

switch (args)
{
    case ["id"]:
        Console.WriteLine("Send this hardware ID to Acme to get your activation code:");
        Console.WriteLine(hardwareId);
        return 0;

    case ["enter", .. var parts] when parts.Length > 0:
        var code = string.Concat(parts).Trim();   // codes are long; tolerate them being split by the shell
        if (!Check(code, out var message)) { Console.Error.WriteLine(message); return 1; }
        Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
        File.WriteAllText(saved, code);
        Console.WriteLine($"Activated. {message}");
        return 0;

    case ["remove"]:
        File.Delete(saved);
        Console.WriteLine("Offline code removed from this machine.");
        return 0;

    case []:
        if (!File.Exists(saved))
        {
            Console.WriteLine("Acme Lab is not activated on this machine.");
            Console.WriteLine($"Hardware ID: {hardwareId}");
            Console.WriteLine("Send it to Acme, then run: dotnet run -- enter <code>");
            return 1;
        }
        if (!Check(File.ReadAllText(saved).Trim(), out var status)) { Console.Error.WriteLine(status); return 1; }
        Console.WriteLine(status);
        return 0;

    default:
        Console.Error.WriteLine("Usage: dotnet run -- [id | enter <code> | remove]");
        return 2;
}

// ---- The lines that matter ----------------------------------------------------------------------
bool Check(string code, out string message)
{
    var r = client.ValidateOffline(code, hardwareId);   // signature, hardware ID and expiry, all local
    if (!r.IsValid)
    {
        message = r.HardwareMismatch ? "This code was made for a different machine. Send Acme the hardware ID shown by 'dotnet run -- id'."
                : r.IsExpired ? $"This code expired on {r.ExpirationDate:d}. Ask Acme for a new one."
                : "This is not a valid activation code. Check that it was copied completely.";
        return false;
    }
    // Every product you sell is signed with the same key, so also check the code is for THIS product.
    if (!string.Equals(r.ProductName, ProductName, StringComparison.OrdinalIgnoreCase))
    {
        message = $"This code is for \"{r.ProductName}\", not {ProductName}.";
        return false;
    }
    var until = r.ExpirationDate is { Year: < 9999 } e ? $"until {e:d}" : "with no expiry";
    var features = r.Features is { Length: > 0 } f ? $" Features: {string.Join(", ", f)}." : "";
    message = $"Licensed to {r.Email} {until}.{features}";
    return true;
}

static string? LoadPublicKey()
{
    if (Environment.GetEnvironmentVariable("EXISONE_PUBLIC_KEY") is { Length: > 0 } env) return env;
    foreach (var dir in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var path = Path.Combine(dir, "public-key.pem");
        if (File.Exists(path)) return File.ReadAllText(path);
    }
    return null;
}
