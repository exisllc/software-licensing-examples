using System.Text.Json;

namespace Acme.Licensing;

/// <summary>The last answer the license server gave, so the app keeps working offline for a while.</summary>
public sealed class CachedLicense
{
    public string? ActivationKey { get; set; }
    public string? Email { get; set; }
    public string Status { get; set; } = "";        // "licensed", "trial", "expired", ...
    public DateTime? ExpiresAt { get; set; }
    public string[] Features { get; set; } = [];
    public DateTime? LastValidatedUtc { get; set; }
    public string HardwareId { get; set; } = "";
}

/// <summary>
/// Stores <see cref="CachedLicense"/> as JSON under %LOCALAPPDATA%\&lt;app name&gt;. This is a convenience
/// for offline use, not copy protection: the server stays the source of truth and is asked again on
/// every start. A user who edits the file gains at most the grace period.
/// </summary>
public sealed class LicenseCache(string appName)
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName, "license-cache.json");

    public CachedLicense? Load()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<CachedLicense>(File.ReadAllText(_path)) : null; }
        catch { return null; }   // a damaged cache is the same as no cache
    }

    public void Save(CachedLicense value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* never fail the app because the cache can't be written */ }
    }

    public void Clear()
    {
        try { File.Delete(_path); } catch { }
    }
}
