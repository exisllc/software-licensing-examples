using System.Text.Json;

namespace Acme.Licensing;

/// <summary>
/// Settings come from appsettings.json, then appsettings.Local.json (git-ignored), then EXISONE_*
/// environment variables; later sources win. Nothing secret is committed to the repository.
/// </summary>
public sealed class LicenseSettings
{
    public string BaseUrl { get; set; } = "https://www.exisone.com";
    public string AccessToken { get; set; } = "";   // verify-only token: it ships in the app, so it is public
    public string Product { get; set; } = "";
    public string? PublicKey { get; set; }          // PEM; only needed for offline codes
    public string BuyUrl { get; set; } = "";
    public int GraceDays { get; set; } = 7;          // how long the app keeps working offline

    public static LicenseSettings Load()
    {
        var s = new LicenseSettings();
        foreach (var file in new[] { "appsettings.json", "appsettings.Local.json" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(path)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("ExisOne", out var e)) continue;
            s.BaseUrl = Str(e, "BaseUrl") ?? s.BaseUrl;
            s.AccessToken = Str(e, "AccessToken") ?? s.AccessToken;
            s.Product = Str(e, "Product") ?? s.Product;
            s.PublicKey = Str(e, "PublicKey") ?? s.PublicKey;
            s.BuyUrl = Str(e, "BuyUrl") ?? s.BuyUrl;
            if (e.TryGetProperty("GraceDays", out var g) && g.TryGetInt32(out var days)) s.GraceDays = days;
        }
        s.BaseUrl = Env("EXISONE_BASE_URL") ?? s.BaseUrl;
        s.AccessToken = Env("EXISONE_ACCESS_TOKEN") ?? s.AccessToken;
        s.Product = Env("EXISONE_PRODUCT") ?? s.Product;
        s.PublicKey = Env("EXISONE_PUBLIC_KEY") ?? s.PublicKey;
        s.BuyUrl = Env("EXISONE_BUY_URL") ?? s.BuyUrl;
        return s;
    }

    public string? Problem() =>
        string.IsNullOrWhiteSpace(AccessToken) ? "EXISONE_ACCESS_TOKEN is not set. See the sample's README." :
        string.IsNullOrWhiteSpace(Product) ? "EXISONE_PRODUCT is not set." : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
