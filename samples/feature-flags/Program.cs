// Acme Studio: features unlocked per license. A key carries the names of the features it includes
// (set per product, per plan, or added later as a paid add-on in the ExisOne dashboard), and
// validation returns them. The app turns each name into an on/off switch.
//
//   dotnet run                          show which features this license unlocks
//   dotnet run -- export                run a feature (refused if the license doesn't include it)
//   dotnet run -- activate <key> <email>
//   dotnet run -- deactivate
using Acme.Licensing;

var settings = LicenseSettings.Load();
if (settings.Problem() is { } problem) { Console.Error.WriteLine($"Configuration problem: {problem}"); return 2; }
var license = new LicenseService(settings, "Acme Studio");

switch (args)
{
    case ["activate", var key, var email]:
        var (ok, message) = await license.ActivateAsync(key, email);
        Console.WriteLine(message);
        return ok ? 0 : 1;
    case ["deactivate"]:
        var (done, text) = await license.DeactivateAsync();
        Console.WriteLine(text);
        return done ? 0 : 1;
}

var state = await license.CheckAsync();
Console.WriteLine(state.Message);
if (!state.CanUseApp) return 1;

// Feature names must match the names in your ExisOne product exactly (compared case-insensitively here).
var unlocked = new HashSet<string>(state.Features ?? [], StringComparer.OrdinalIgnoreCase);
bool Has(string feature) => unlocked.Contains(feature);

var catalog = new (string Command, string Feature, string Description)[]
{
    ("edit",   "",           "Edit projects (every license)"),
    ("export", "Export",     "Export to PDF and PNG"),
    ("batch",  "Batch",      "Process a whole folder at once"),
    ("sync",   "Cloud Sync", "Sync projects between computers"),
};

if (args is [var requested])
{
    var entry = catalog.FirstOrDefault(c => c.Command == requested);
    if (entry.Command == null) { Console.Error.WriteLine($"Unknown command: {requested}"); return 2; }
    if (entry.Feature != "" && !Has(entry.Feature))
    {
        Console.Error.WriteLine($"\"{entry.Feature}\" is not included in your license. Upgrade to unlock it.");
        return 1;
    }
    Console.WriteLine($"{entry.Description}: done (pretend).");
    return 0;
}

Console.WriteLine();
foreach (var (command, feature, description) in catalog)
{
    var on = feature == "" || Has(feature);
    Console.WriteLine($"  [{(on ? "x" : " ")}] {command,-7} {description}{(on ? "" : "  (upgrade to unlock)")}");
}
if (unlocked.Count > 0) Console.WriteLine($"\nFeatures on this license: {string.Join(", ", unlocked)}");
return 0;
