using System.Reflection;
using ExisOne.Client;

namespace Acme.Licensing;

public enum LicenseKind { Licensed, Trial, TrialEnded, Expired, NotLicensed, UpdateRequired, NeedsConnection }

public sealed record LicenseState(
    LicenseKind Kind,
    string Message,
    DateTime? ExpiresAt = null,
    string[]? Features = null,
    string? UpdateAvailable = null,
    bool Offline = false)
{
    /// <summary>True when the app may run with its licensed (or trial) functionality.</summary>
    public bool CanUseApp => Kind is LicenseKind.Licensed or LicenseKind.Trial;
}

/// <summary>
/// Everything licensing-related in one place: start-up check, activation, deactivation.
/// Rules: the server decides; a network problem never locks out a user who was licensed
/// within the last <see cref="LicenseSettings.GraceDays"/> days; every outcome has a plain message.
/// </summary>
public sealed class LicenseService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly LicenseSettings _settings;
    private readonly ExisOneClient _client;
    private readonly LicenseCache _cache;
    private readonly string _hardwareId;
    private readonly string _appName;

    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <param name="appName">Your app's name: used in messages and for the per-user cache folder.</param>
    public LicenseService(LicenseSettings settings, string appName)
    {
        _settings = settings;
        _appName = appName;
        _cache = new LicenseCache(appName);
        _client = new ExisOneClient(new ExisOneClientOptions
        {
            BaseUrl = settings.BaseUrl,
            AccessToken = settings.AccessToken,
            OfflinePublicKey = settings.PublicKey,
        });
        _hardwareId = _client.GenerateHardwareId();   // stable per machine; cached by the SDK
    }

    public string HardwareId => _hardwareId;
    public string? StoredKey => _cache.Load()?.ActivationKey;

    // ---- Start-up check ---------------------------------------------------------------------------

    public async Task<LicenseState> CheckAsync()
    {
        var cached = _cache.Load();
        if (cached != null && cached.HardwareId != _hardwareId) cached = null;   // copied from another PC
        var key = cached?.ActivationKey;

        try
        {
            // No key yet = trial check: the server starts (or continues) this machine's trial.
            var (_, status, expires, features, serverVersion, minVersion) =
                await _client.ValidateAsync(_hardwareId, _settings.Product, key, AppVersion).WaitAsync(Timeout);

            _cache.Save(new CachedLicense
            {
                ActivationKey = key, Email = cached?.Email, Status = status, ExpiresAt = expires,
                Features = features, LastValidatedUtc = DateTime.UtcNow, HardwareId = _hardwareId,
            });
            return FromServer(status, key, expires, features, serverVersion, minVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            // Network down, server unreachable, or a rejected token: fall back to the last good answer.
            return FromCache(cached, ServerStatus(ex));
        }
    }

    private LicenseState FromServer(string status, string? key, DateTime? expires, string[] features,
        string? serverVersion, string? minVersion)
    {
        var update = IsNewer(serverVersion, AppVersion) ? serverVersion : null;
        return status switch
        {
            "licensed" => new(LicenseKind.Licensed,
                expires is { Year: < 9999 } e ? $"Licensed until {e:d}." : "Licensed.", expires, features, update),
            "trial" => new(LicenseKind.Trial, $"Trial: {DaysLeft(expires)} day(s) left.", expires, features, update),
            "expired" when key == null => new(LicenseKind.TrialEnded, $"Your trial has ended. Buy a license to keep using {_appName}."),
            "expired" => new(LicenseKind.Expired, $"Your license expired on {expires:d}. Renew it to keep using {_appName}.", expires),
            "version_outdated" => new(LicenseKind.UpdateRequired,
                $"This version is no longer supported. Please update to {minVersion} or later.", UpdateAvailable: serverVersion),
            "trial_unavailable" => new(LicenseKind.NotLicensed, $"Enter a license key to use {_appName}."),
            _ when key != null => new(LicenseKind.NotLicensed, "This license is not active on this computer. Activate it again, or contact support."),
            _ => new(LicenseKind.NotLicensed, $"Enter a license key to use {_appName}."),
        };
    }

    private LicenseState FromCache(CachedLicense? c, int? serverStatus)
    {
        var usable = c is { Status: "licensed" or "trial", LastValidatedUtc: { } last }
                     && DateTime.UtcNow - last < TimeSpan.FromDays(_settings.GraceDays)
                     && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow);
        if (!usable)
            return new(LicenseKind.NeedsConnection, serverStatus is 401 or 403
                // A developer problem, not the user's: wrong token, or a token without 'verify'.
                ? "The license server refused this app's access token. Please contact Acme support."
                : "Can't reach the license server. Connect to the internet and try again.");

        var until = c!.LastValidatedUtc!.Value.AddDays(_settings.GraceDays).ToLocalTime();
        var kind = c.Status == "trial" ? LicenseKind.Trial : LicenseKind.Licensed;
        return new(kind, $"Working offline. Connect to the internet before {until:d} to keep using {_appName}.",
            c.ExpiresAt, c.Features, Offline: true);
    }

    // ---- Activation -------------------------------------------------------------------------------

    public async Task<(bool Ok, string Message)> ActivateAsync(string key, string email)
    {
        key = key.Trim();
        ActivationResult r;
        try
        {
            r = await _client.ActivateAsync(key, email.Trim(), _hardwareId, _settings.Product, AppVersion).WaitAsync(Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return (false, "Can't reach the license server. Activation needs an internet connection.");
        }

        if (!r.Success)
            return (false, r.ErrorCode == "version_outdated"
                ? $"This version is too old to activate. Please update to {r.MinimumRequiredVersion} or later."
                : r.ErrorCode ?? "Activation failed. Check the key and try again.");   // the server's message, e.g. a seat limit

        _cache.Save(new CachedLicense { ActivationKey = key, Email = email.Trim(), Status = "licensed", HardwareId = _hardwareId });
        return (true, "Activated. Thank you!");
    }

    /// <summary>Frees this computer's seat so the key can be used on another machine.</summary>
    public async Task<(bool Ok, string Message)> DeactivateAsync()
    {
        var key = StoredKey;
        if (key == null) return (false, "No license is activated on this computer.");
        try
        {
            await _client.DeactivateAsync(key, _hardwareId, _settings.Product).WaitAsync(Timeout);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            return (false, ServerStatus(ex) is null
                ? "Can't reach the license server. Deactivation needs an internet connection."
                : "The license server refused the deactivation. Please contact Acme support.");
        }
        _cache.Clear();
        return (true, "This computer is deactivated. You can now activate the key on another computer.");
    }

    private static int DaysLeft(DateTime? expires) =>
        expires is { } e ? Math.Max(0, (int)Math.Ceiling((e.ToUniversalTime() - DateTime.UtcNow).TotalDays)) : 0;

    /// <summary>The HTTP status if the server answered with an error (the SDK puts it first in the message); null for network errors.</summary>
    private static int? ServerStatus(Exception ex) =>
        ex is HttpRequestException && ex.Message.Length >= 3 && int.TryParse(ex.Message[..3], out var code) ? code : null;

    private static bool IsNewer(string? candidate, string current) =>
        Version.TryParse(candidate, out var a) && Version.TryParse(current, out var b) && a > b;
}
