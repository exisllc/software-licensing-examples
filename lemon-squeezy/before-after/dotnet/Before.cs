// Lemon Squeezy License API calls.
using System.Text.Json;

public static class LicenseApi
{
    private const string LicenseApiBase = "https://api.lemonsqueezy.com/v1/licenses";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<JsonDocument> CallAsync(string action, Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{LicenseApiBase}/{action}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Accept.ParseAdd("application/json");
        using var res = await Http.SendAsync(req);
        // Failures carry an "error" message.
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    public static Task<JsonDocument> ActivateAsync(string licenseKey, string instanceName) =>
        CallAsync("activate", new() { ["license_key"] = licenseKey, ["instance_name"] = instanceName });

    public static Task<JsonDocument> ValidateAsync(string licenseKey, string? instanceId = null) =>
        CallAsync("validate", instanceId is null
            ? new() { ["license_key"] = licenseKey }
            : new() { ["license_key"] = licenseKey, ["instance_id"] = instanceId });

    public static Task<JsonDocument> DeactivateAsync(string licenseKey, string instanceId) =>
        CallAsync("deactivate", new() { ["license_key"] = licenseKey, ["instance_id"] = instanceId });
}
