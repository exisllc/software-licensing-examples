// ls-capture: records how the Lemon Squeezy License API responds to every success and failure
// case, so ExisOne's LS-compatible endpoints can be matched against real behavior.
//
// Talks ONLY to the Lemon Squeezy API. Writes a single redacted JSON file locally. Nothing is sent
// anywhere else. Temporarily changes the TEST key's activation_limit / expires_at / disabled via the
// main API to reach the expired/disabled/limit cases, and always restores them (and deactivates every
// instance it created) before exiting. Refuses to run against a key that isn't in test mode.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string ToolVersion = "1.0.0";

var opt = Options.Parse(args);
if (opt == null) { Options.PrintUsage(); return 2; }

var handler = new HttpClientHandler { AllowAutoRedirect = false };
using var http = new HttpClient(handler) { BaseAddress = new Uri(opt.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd($"ls-capture/{ToolVersion}");

var redactor = new Redactor(opt.KeepIds);
redactor.AddSecret(opt.LicenseKey, "<LICENSE_KEY>");
if (opt.SecondLicenseKey != null) redactor.AddSecret(opt.SecondLicenseKey, "<LICENSE_KEY_2>");

var cases = new List<JsonObject>();
var notes = new List<string>();
var createdInstances = new List<(string Key, string InstanceId)>();
DateTime lastLicenseCall = DateTime.MinValue;
bool throttle = true;

// ---------------------------------------------------------------- helpers

async Task<Resp> Send(HttpRequestMessage req)
{
    var sw = Stopwatch.StartNew();
    using var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    sw.Stop();
    var headers = new Dictionary<string, string>();
    foreach (var h in res.Headers.Concat(res.Content.Headers))
    {
        var name = h.Key.ToLowerInvariant();
        if (Resp.InterestingHeaders.Contains(name)) headers[name] = string.Join(", ", h.Value);
    }
    JsonNode? json = null;
    try { if (!string.IsNullOrWhiteSpace(text)) json = JsonNode.Parse(text); } catch (JsonException) { }
    return new Resp((int)res.StatusCode, headers, text, json, sw.ElapsedMilliseconds);
}

async Task Throttle()
{
    // License API limit is 60 req/min. Stay well under so throttling never pollutes a case.
    if (!throttle) return;
    var wait = lastLicenseCall.AddMilliseconds(1100) - DateTime.UtcNow;
    if (wait > TimeSpan.Zero) await Task.Delay(wait);
    lastLicenseCall = DateTime.UtcNow;
}

async Task<Resp> License(string caseId, string description, string endpoint, Dictionary<string, string?> p,
    string bodyMode = "form", string? accept = "application/json", HttpMethod? method = null, string? origin = null, bool record = true)
{
    await Throttle();
    method ??= HttpMethod.Post;
    var path = $"/v1/licenses/{endpoint}";
    var req = new HttpRequestMessage(method, path);
    if (accept != null) req.Headers.Accept.ParseAdd(accept);
    if (origin != null)
    {
        req.Headers.Add("Origin", origin);
        if (method == HttpMethod.Options)
        {
            req.Headers.Add("Access-Control-Request-Method", "POST");
            req.Headers.Add("Access-Control-Request-Headers", "content-type,accept");
        }
    }
    var sent = p.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value!);
    string? contentType = null;
    if (method == HttpMethod.Post)
    {
        if (bodyMode == "json")
        {
            req.Content = new StringContent(JsonSerializer.Serialize(sent), Encoding.UTF8, "application/json");
            contentType = "application/json";
        }
        else
        {
            req.Content = new FormUrlEncodedContent(sent);
            contentType = "application/x-www-form-urlencoded";
        }
    }
    else if (method == HttpMethod.Get && sent.Count > 0)
    {
        req.RequestUri = new Uri(path + "?" + string.Join("&", sent.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")), UriKind.Relative);
    }

    var r = await Send(req);
    if (!record) { Console.WriteLine($"  {r.Status,3}  ({description})"); return r; }
    var reqParams = new JsonObject();
    foreach (var kv in sent) reqParams[kv.Key] = redactor.RedactString(kv.Value);
    cases.Add(new JsonObject
    {
        ["case_id"] = caseId,
        ["description"] = description,
        ["request"] = new JsonObject
        {
            ["method"] = method.Method,
            ["path"] = path,
            ["content_type"] = contentType,
            ["accept"] = accept,
            ["origin"] = origin,
            ["params"] = reqParams,
        },
        ["response"] = new JsonObject
        {
            ["status"] = r.Status,
            ["headers"] = JsonSerializer.SerializeToNode(r.Headers),
            ["body_json"] = r.Json == null ? null : redactor.Redact(r.Json.DeepClone()),
            ["body_text"] = r.Json == null ? redactor.RedactString(Truncate(r.Text, 4000)) : null,
        },
        ["elapsed_ms"] = r.ElapsedMs,
    });
    Console.WriteLine($"  {r.Status,3}  {caseId}");
    return r;
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...<truncated>";

string? InstanceIdOf(Resp r) => r.Json?["instance"]?["id"]?.GetValue<string>();

async Task<string?> Activate(string caseId, string description, string key, string name, bool record = true)
{
    var r = await License(caseId, description, "activate", new() { ["license_key"] = key, ["instance_name"] = name }, record: record);
    var id = InstanceIdOf(r);
    if (id != null) createdInstances.Add((key, id));
    return id;
}

async Task<Resp> MainApi(HttpMethod method, string path, JsonObject? body = null)
{
    var req = new HttpRequestMessage(method, path);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.ApiKey);
    req.Headers.Accept.ParseAdd("application/vnd.api+json");
    if (body != null)
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/vnd.api+json");
    req.Content?.Headers.ContentType?.Parameters.Clear();
    return await Send(req);
}

async Task<bool> PatchKey(string keyId, JsonObject attributes, string why)
{
    var body = new JsonObject
    {
        ["data"] = new JsonObject { ["type"] = "license-keys", ["id"] = keyId, ["attributes"] = attributes }
    };
    var r = await MainApi(HttpMethod.Patch, $"/v1/license-keys/{keyId}", body);
    var ok = r.Status is >= 200 and < 300;
    if (!ok) notes.Add($"PATCH ({why}) failed with HTTP {r.Status}: {redactor.RedactString(Truncate(r.Text, 500))}");
    Console.WriteLine($"  [main api] PATCH {why}: HTTP {r.Status}");
    return ok;
}

// ---------------------------------------------------------------- run

Console.WriteLine($"ls-capture {ToolVersion} -> {opt.BaseUrl}");
Console.WriteLine("Step 1: identify the license key");

var first = await License("validate.success.no_instance", "Valid key, no instance_id", "validate",
    new() { ["license_key"] = opt.LicenseKey });
var keyIdNode = first.Json?["license_key"]?["id"];
if (first.Status != 200 || keyIdNode == null)
{
    Console.Error.WriteLine($"Could not validate the license key (HTTP {first.Status}). Check --license-key. Nothing was changed.");
    return 1;
}
var keyId = keyIdNode.ToString();

JsonNode? original = null;
bool? testMode = null;
if (opt.ApiKey != null)
{
    var g = await MainApi(HttpMethod.Get, $"/v1/license-keys/{keyId}");
    if (g.Status != 200)
    {
        Console.Error.WriteLine($"Main API GET /v1/license-keys/{keyId} returned HTTP {g.Status}. Check --api-key (it must belong to the same store and mode as the key). Nothing was changed.");
        return 1;
    }
    original = g.Json?["data"]?["attributes"];
    var tm = original?["test_mode"];
    testMode = tm == null ? null : tm.GetValueKind() == JsonValueKind.True || (tm.GetValueKind() == JsonValueKind.Number && tm.GetValue<int>() == 1);
}

if (testMode == false)
{
    Console.Error.WriteLine("This license key is a LIVE-mode key. ls-capture only runs against test-mode keys. Nothing was changed.");
    return 1;
}
if (testMode == null && !opt.ConfirmTestKey)
{
    Console.Error.WriteLine("Could not confirm the key is in test mode (no API key given, or the API did not report test_mode).");
    Console.Error.WriteLine("If you have checked in the Lemon Squeezy dashboard that it is a TEST key, re-run with --confirm-test-key. Nothing was changed.");
    return 1;
}

int? origLimit = original?["activation_limit"] is JsonNode l && l.GetValueKind() == JsonValueKind.Number ? l.GetValue<int>() : null;
string? origExpires = original?["expires_at"]?.GetValueKind() == JsonValueKind.String ? original["expires_at"]!.GetValue<string>() : null;
bool origDisabled = original?["disabled"] is JsonNode d && (d.GetValueKind() == JsonValueKind.True || (d.GetValueKind() == JsonValueKind.Number && d.GetValue<int>() == 1));
bool canMutate = opt.ApiKey != null && original != null;
if (!canMutate) notes.Add("Run without --api-key: expired / disabled / limit-reached cases were skipped.");
if (origDisabled) notes.Add("Key was disabled at start; it was temporarily re-enabled and then restored to disabled.");

try
{
    if (canMutate)
    {
        // Make room for our test activations; the real limit is restored in finally.
        var usage = first.Json?["license_key"]?["activation_usage"]?.GetValue<int>() ?? 0;
        await PatchKey(keyId, new JsonObject { ["activation_limit"] = usage + 5, ["disabled"] = false, ["expires_at"] = null }, "room for test activations");
    }

    Console.WriteLine("Step 2: validate edge cases");
    var unknownUuid = Guid.NewGuid().ToString();
    await License("validate.invalid_key", "Unknown license key (random UUID)", "validate", new() { ["license_key"] = Guid.NewGuid().ToString() });
    await License("validate.missing_license_key", "No license_key parameter", "validate", new());
    await License("validate.empty_license_key", "license_key present but empty", "validate", new() { ["license_key"] = "" });
    var flipped = opt.LicenseKey.Any(char.IsLower) ? opt.LicenseKey.ToUpperInvariant() : opt.LicenseKey.ToLowerInvariant();
    redactor.AddSecret(flipped, "<LICENSE_KEY_CASE_FLIPPED>");
    await License("validate.key_case_changed", "Valid key with letter case flipped", "validate", new() { ["license_key"] = flipped });
    await License("validate.key_whitespace_padded", "Valid key with surrounding spaces", "validate", new() { ["license_key"] = "  " + opt.LicenseKey + " " });
    await License("validate.json_body", "Valid key sent as a JSON body instead of form", "validate", new() { ["license_key"] = opt.LicenseKey }, bodyMode: "json");
    await License("validate.no_accept_header", "Valid key without Accept: application/json", "validate", new() { ["license_key"] = opt.LicenseKey }, accept: null);
    await License("validate.get_method", "GET instead of POST", "validate", new() { ["license_key"] = opt.LicenseKey }, method: HttpMethod.Get);
    await License("validate.cors_preflight", "CORS preflight (OPTIONS) from a browser origin", "validate", new(), method: HttpMethod.Options, origin: "http://localhost:5173");
    await License("validate.cors_simple", "POST with an Origin header (Electron renderer / browser)", "validate", new() { ["license_key"] = opt.LicenseKey }, origin: "http://localhost:5173");

    Console.WriteLine("Step 3: activate");
    var inst1 = await Activate("activate.success", "Activate with a new instance_name", opt.LicenseKey, "ls-capture-1");
    var inst2 = await Activate("activate.same_instance_name_again", "Activate again with the same instance_name", opt.LicenseKey, "ls-capture-1");
    await License("activate.invalid_key", "Unknown license key", "activate", new() { ["license_key"] = Guid.NewGuid().ToString(), ["instance_name"] = "ls-capture-x" });
    await License("activate.missing_instance_name", "No instance_name", "activate", new() { ["license_key"] = opt.LicenseKey });
    await License("activate.empty_instance_name", "instance_name present but empty", "activate", new() { ["license_key"] = opt.LicenseKey, ["instance_name"] = "" });
    await License("activate.missing_license_key", "No license_key", "activate", new() { ["instance_name"] = "ls-capture-x" });
    await License("activate.missing_all_params", "No parameters at all", "activate", new());
    await License("activate.short_instance_name", "instance_name of 2 characters", "activate", new() { ["license_key"] = opt.LicenseKey, ["instance_name"] = "ab" });
    await Activate("activate.long_instance_name", "instance_name of 300 characters", opt.LicenseKey, "ls-capture-" + new string('n', 289));
    var jsonId = InstanceIdOf(await License("activate.json_body", "Activate with a JSON body instead of form", "activate",
        new() { ["license_key"] = opt.LicenseKey, ["instance_name"] = "ls-capture-json" }, bodyMode: "json"));
    if (jsonId != null) createdInstances.Add((opt.LicenseKey, jsonId));

    Console.WriteLine("Step 4: validate with instances");
    if (inst1 != null)
        await License("validate.success.with_instance", "Valid key + its instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst1 });
    await License("validate.unknown_instance", "Valid key + random UUID instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = unknownUuid });
    await License("validate.empty_instance_id", "Valid key + empty instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = "" });
    await License("validate.malformed_instance_id", "Valid key + non-UUID instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = "not-a-uuid" });

    Console.WriteLine("Step 5: deactivate");
    if (inst2 != null)
    {
        await License("deactivate.success", "Deactivate an instance", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst2 });
        createdInstances.RemoveAll(x => x.InstanceId == inst2);
        await License("deactivate.already_deactivated", "Deactivate the same instance again", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst2 });
        await License("validate.deactivated_instance", "Validate with an instance_id that was deactivated", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst2 });
    }
    await License("deactivate.unknown_instance", "Random UUID instance_id", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = unknownUuid });
    await License("deactivate.invalid_key", "Unknown license key", "deactivate", new() { ["license_key"] = Guid.NewGuid().ToString(), ["instance_id"] = unknownUuid });
    await License("deactivate.missing_instance_id", "No instance_id", "deactivate", new() { ["license_key"] = opt.LicenseKey });
    await License("deactivate.missing_license_key", "No license_key", "deactivate", new() { ["instance_id"] = unknownUuid });
    await License("deactivate.missing_all_params", "No parameters at all", "deactivate", new());

    if (opt.SecondLicenseKey != null)
    {
        Console.WriteLine("Step 6: cross-key instance checks");
        var other = await Activate("activate.success.second_key", "Activate the second key", opt.SecondLicenseKey, "ls-capture-other");
        if (other != null)
        {
            await License("validate.instance_of_other_key", "Key A + an instance_id that belongs to key B", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = other });
            await License("deactivate.instance_of_other_key", "Key A + an instance_id that belongs to key B", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = other });
        }
    }

    if (canMutate)
    {
        Console.WriteLine("Step 7: activation limit reached");
        var v = await License("validate.success.after_activations", "Validate after test activations (usage counter)", "validate", new() { ["license_key"] = opt.LicenseKey });
        var usage = v.Json?["license_key"]?["activation_usage"]?.GetValue<int>() ?? 0;
        if (await PatchKey(keyId, new JsonObject { ["activation_limit"] = usage }, "limit = current usage"))
            await Activate("activate.limit_reached", "Activate when activation_usage == activation_limit", opt.LicenseKey, "ls-capture-over-limit");
        await PatchKey(keyId, new JsonObject { ["activation_limit"] = usage + 5 }, "room again");

        Console.WriteLine("Step 8: expired key");
        var instExp = await Activate("", "setup: instance for the expired-key cases", opt.LicenseKey, "ls-capture-expired-setup", record: false);
        if (await PatchKey(keyId, new JsonObject { ["expires_at"] = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }, "expire"))
        {
            await License("validate.expired.no_instance", "Expired key, no instance_id", "validate", new() { ["license_key"] = opt.LicenseKey });
            if (inst1 != null)
                await License("validate.expired.with_instance", "Expired key + valid instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst1 });
            await Activate("activate.expired", "Activate an expired key", opt.LicenseKey, "ls-capture-expired");
            if (instExp != null)
            {
                var dr = await License("deactivate.expired", "Deactivate an instance of an expired key", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = instExp });
                if (dr.Json?["deactivated"]?.GetValueKind() == JsonValueKind.True) createdInstances.RemoveAll(x => x.InstanceId == instExp);
            }
        }
        await PatchKey(keyId, new JsonObject { ["expires_at"] = null }, "un-expire");

        Console.WriteLine("Step 9: disabled key");
        var instDis = await Activate("", "setup: instance for the disabled-key cases", opt.LicenseKey, "ls-capture-disabled-setup", record: false);
        if (await PatchKey(keyId, new JsonObject { ["disabled"] = true }, "disable"))
        {
            await License("validate.disabled.no_instance", "Disabled key, no instance_id", "validate", new() { ["license_key"] = opt.LicenseKey });
            if (inst1 != null)
                await License("validate.disabled.with_instance", "Disabled key + valid instance_id", "validate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = inst1 });
            await Activate("activate.disabled", "Activate a disabled key", opt.LicenseKey, "ls-capture-disabled");
            if (instDis != null)
            {
                var dr = await License("deactivate.disabled", "Deactivate an instance of a disabled key", "deactivate", new() { ["license_key"] = opt.LicenseKey, ["instance_id"] = instDis });
                if (dr.Json?["deactivated"]?.GetValueKind() == JsonValueKind.True) createdInstances.RemoveAll(x => x.InstanceId == instDis);
            }
        }
        await PatchKey(keyId, new JsonObject { ["disabled"] = false }, "re-enable");
    }

    if (opt.IncludeRateLimit)
    {
        Console.WriteLine("Step 10: rate limit (bursting validate until HTTP 429, max 90 requests)");
        throttle = false;
        for (int i = 0; i < 90; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/v1/licenses/validate");
            req.Headers.Accept.ParseAdd("application/json");
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["license_key"] = opt.LicenseKey });
            var r = await Send(req);
            if (r.Status == 429)
            {
                cases.Add(new JsonObject
                {
                    ["case_id"] = "ratelimit.exceeded",
                    ["description"] = $"First 429 after {i + 1} rapid validate requests",
                    ["request"] = new JsonObject { ["method"] = "POST", ["path"] = "/v1/licenses/validate", ["content_type"] = "application/x-www-form-urlencoded", ["accept"] = "application/json", ["params"] = new JsonObject { ["license_key"] = "<LICENSE_KEY>" } },
                    ["response"] = new JsonObject
                    {
                        ["status"] = r.Status,
                        ["headers"] = JsonSerializer.SerializeToNode(r.Headers),
                        ["body_json"] = r.Json == null ? null : redactor.Redact(r.Json.DeepClone()),
                        ["body_text"] = r.Json == null ? redactor.RedactString(Truncate(r.Text, 4000)) : null,
                    },
                    ["elapsed_ms"] = r.ElapsedMs,
                });
                Console.WriteLine($"  429 after {i + 1} requests");
                break;
            }
            if (i == 89) notes.Add("No HTTP 429 observed within 90 rapid validate requests.");
        }
        throttle = true;
        Console.WriteLine("  waiting 65s for the rate-limit window to reset before cleanup...");
        await Task.Delay(TimeSpan.FromSeconds(65));
    }
}
finally
{
    Console.WriteLine("Cleanup: restoring the key and deactivating test instances");
    if (canMutate)
    {
        var restore = new JsonObject
        {
            ["activation_limit"] = origLimit,      // null restores "unlimited"
            ["expires_at"] = origExpires,          // null restores "perpetual"
            ["disabled"] = false,
        };
        if (!await PatchKey(keyId, restore, "restore limit/expiry"))
            notes.Add($"RESTORE FAILED: please set activation_limit={(origLimit?.ToString() ?? "unlimited")}, expires_at={(origExpires ?? "never")} on the key in the Lemon Squeezy dashboard.");
    }
    foreach (var (key, inst) in createdInstances.ToList())
    {
        await Throttle();
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/licenses/deactivate");
        req.Headers.Accept.ParseAdd("application/json");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["license_key"] = key, ["instance_id"] = inst });
        var r = await Send(req);
        var ok = r.Json?["deactivated"]?.GetValueKind() == JsonValueKind.True;
        Console.WriteLine($"  deactivate test instance: HTTP {r.Status}{(ok ? "" : " (NOT deactivated)")}");
        if (!ok) notes.Add($"Could not deactivate a test instance (HTTP {r.Status}); remove the instance named 'ls-capture-*' from the key in the dashboard.");
    }
    if (canMutate && origDisabled && !await PatchKey(keyId, new JsonObject { ["disabled"] = true }, "restore disabled"))
        notes.Add("RESTORE FAILED: the key was disabled before the run; please disable it again in the dashboard.");
}

var output = new JsonObject
{
    ["format"] = "exisone-ls-capture/1",
    ["tool_version"] = ToolVersion,
    ["captured_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
    ["base_url"] = opt.BaseUrl,
    ["test_mode"] = testMode,
    ["redaction"] = opt.KeepIds
        ? "license keys, emails and names replaced; numeric ids kept"
        : "license keys, emails and names replaced; numeric ids replaced with consistent fake integers (types preserved)",
    ["notes"] = new JsonArray(notes.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray()),
    ["cases"] = new JsonArray(cases.Select(c => (JsonNode?)c).ToArray()),
};
var json = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
// Final safety net: the raw secrets must not appear anywhere in the file.
foreach (var secret in new[] { opt.LicenseKey, opt.SecondLicenseKey, opt.ApiKey }.Where(s => !string.IsNullOrEmpty(s)))
{
    if (json.Contains(secret!, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Internal error: a secret survived redaction. Output NOT written.");
        return 3;
    }
}
await File.WriteAllTextAsync(opt.OutPath, json);
Console.WriteLine();
Console.WriteLine($"Wrote {cases.Count} cases to {Path.GetFullPath(opt.OutPath)}");
foreach (var n in notes) Console.WriteLine("NOTE: " + n);
Console.WriteLine("Please review the file before sending it.");
return notes.Any(n => n.StartsWith("RESTORE FAILED")) ? 4 : 0;

// ---------------------------------------------------------------- types

record Resp(int Status, Dictionary<string, string> Headers, string Text, JsonNode? Json, long ElapsedMs)
{
    public static readonly HashSet<string> InterestingHeaders = new()
    {
        "content-type", "x-ratelimit-limit", "x-ratelimit-remaining", "retry-after", "location", "cache-control",
        "access-control-allow-origin", "access-control-allow-methods", "access-control-allow-headers",
        "access-control-allow-credentials", "access-control-max-age", "allow",
    };
}

sealed class Options
{
    public string BaseUrl = "https://api.lemonsqueezy.com";
    public string? ApiKey;
    public string LicenseKey = "";
    public string? SecondLicenseKey;
    public string OutPath = $"ls-capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
    public bool IncludeRateLimit;
    public bool ConfirmTestKey;
    public bool KeepIds;

    public static Options? Parse(string[] args)
    {
        var o = new Options
        {
            ApiKey = Environment.GetEnvironmentVariable("LS_API_KEY"),
            LicenseKey = Environment.GetEnvironmentVariable("LS_LICENSE_KEY") ?? "",
            SecondLicenseKey = Environment.GetEnvironmentVariable("LS_SECOND_LICENSE_KEY"),
        };
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--api-key": o.ApiKey = Next(); break;
                case "--license-key": o.LicenseKey = Next(); break;
                case "--second-license-key": o.SecondLicenseKey = Next(); break;
                case "--out": o.OutPath = Next(); break;
                case "--base-url": o.BaseUrl = Next().TrimEnd('/'); break;
                case "--include-rate-limit": o.IncludeRateLimit = true; break;
                case "--confirm-test-key": o.ConfirmTestKey = true; break;
                case "--keep-ids": o.KeepIds = true; break;
                case "-h": case "--help": return null;
                default: Console.Error.WriteLine($"Unknown option {args[i]}"); return null;
            }
        }
        if (string.IsNullOrWhiteSpace(o.LicenseKey)) { Console.Error.WriteLine("A test license key is required (LS_LICENSE_KEY or --license-key)."); return null; }
        o.LicenseKey = o.LicenseKey.Trim();
        o.SecondLicenseKey = string.IsNullOrWhiteSpace(o.SecondLicenseKey) ? null : o.SecondLicenseKey.Trim();
        if (string.IsNullOrWhiteSpace(o.ApiKey)) o.ApiKey = null;
        return o;
    }

    public static void PrintUsage() => Console.WriteLine("""
        ls-capture - record Lemon Squeezy License API responses (test mode only)

        Secrets are read from environment variables so they stay out of shell history:
          LS_API_KEY             test-mode API key (Settings > API in the LS dashboard)
          LS_LICENSE_KEY         a TEST-mode license key from a test order
          LS_SECOND_LICENSE_KEY  optional second test key (enables cross-key cases)

        Options (override the environment variables):
          --api-key <key> --license-key <key> --second-license-key <key>
          --out <file>             output path (default ls-capture-<utc>.json)
          --include-rate-limit     also burst requests until HTTP 429 (adds ~2 minutes)
          --confirm-test-key       proceed when test mode cannot be confirmed via the API
          --keep-ids               do not replace numeric ids (store/order/customer ids)
        """);
}

/// <summary>Replaces license keys, emails, names and (optionally) numeric ids, preserving JSON types.</summary>
sealed class Redactor
{
    private readonly bool _keepIds;
    private readonly List<(string Secret, string Placeholder)> _secrets = new();
    private readonly Dictionary<string, long> _idMap = new();
    private long _nextId = 1001;
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly HashSet<string> NameFields = new() { "customer_name", "user_name", "name_on_card", "store_name" };
    private static readonly HashSet<string> ProductNameFields = new() { "product_name", "variant_name" };

    public Redactor(bool keepIds) => _keepIds = keepIds;

    public void AddSecret(string secret, string placeholder)
    {
        if (!string.IsNullOrEmpty(secret)) _secrets.Add((secret, placeholder));
        _secrets.Sort((a, b) => b.Secret.Length.CompareTo(a.Secret.Length));
    }

    public string? RedactString(string? s)
    {
        if (s == null) return null;
        // Exact-case matches first (so a deliberately case-flipped key keeps its own placeholder),
        // then a case-insensitive sweep so no variant of a secret can survive.
        foreach (var (secret, placeholder) in _secrets)
            s = s.Replace(secret, placeholder, StringComparison.Ordinal);
        foreach (var (secret, placeholder) in _secrets)
            s = Regex.Replace(s, Regex.Escape(secret), placeholder, RegexOptions.IgnoreCase);
        return Email.Replace(s, "redacted@example.com");
    }

    public JsonNode? Redact(JsonNode? node, string? propertyName = null)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var prop in obj.ToList())
                    obj[prop.Key] = Redact(prop.Value?.DeepClone(), prop.Key);
                return obj;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++) arr[i] = Redact(arr[i]?.DeepClone(), propertyName);
                return arr;
            case JsonValue v when v.GetValueKind() == JsonValueKind.String:
                var s = v.GetValue<string>();
                if (propertyName == "key_short") return JsonValue.Create("XXXX-<REDACTED>");
                if (propertyName != null && NameFields.Contains(propertyName)) return JsonValue.Create("<REDACTED_NAME>");
                if (propertyName != null && ProductNameFields.Contains(propertyName)) return JsonValue.Create($"<{propertyName.ToUpperInvariant()}>");
                return JsonValue.Create(RedactString(s));
            case JsonValue v when v.GetValueKind() == JsonValueKind.Number && !_keepIds && propertyName != null
                                  && (propertyName == "id" || propertyName.EndsWith("_id")):
                var raw = v.ToJsonString();
                var mapKey = propertyName + ":" + raw;
                if (!_idMap.TryGetValue(mapKey, out var fake)) _idMap[mapKey] = fake = _nextId++;
                return JsonValue.Create(fake);
            default:
                return node;
        }
    }
}
