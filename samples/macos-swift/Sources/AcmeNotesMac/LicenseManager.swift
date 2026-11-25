import ExisOne
import Foundation

/// Settings from EXISONE_* environment variables, or a `.env` file in the working directory.
struct LicenseSettings {
    var baseURL = "https://www.exisone.com"
    var accessToken = ""   // verify-only token: it ships in the app, so it is public
    var product = ""
    var publicKey: String? // PEM, only for offline codes
    var buyURL = ""
    var graceDays = 7

    static func load() -> LicenseSettings {
        var values: [String: String] = [:]
        if let text = try? String(contentsOfFile: ".env", encoding: .utf8) {
            for line in text.split(separator: "\n") where !line.hasPrefix("#") {
                let parts = line.split(separator: "=", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespaces) }
                if parts.count == 2 { values[parts[0]] = parts[1] }
            }
        }
        let env = ProcessInfo.processInfo.environment
        func get(_ key: String) -> String? { (env[key]?.isEmpty == false ? env[key] : nil) ?? values[key] }
        var s = LicenseSettings()
        s.baseURL = get("EXISONE_BASE_URL") ?? s.baseURL
        s.accessToken = get("EXISONE_ACCESS_TOKEN") ?? ""
        s.product = get("EXISONE_PRODUCT") ?? ""
        s.publicKey = get("EXISONE_PUBLIC_KEY")
        s.buyURL = get("EXISONE_BUY_URL") ?? ""
        s.graceDays = Int(get("EXISONE_GRACE_DAYS") ?? "") ?? 7
        return s
    }

    var problem: String? {
        if accessToken.isEmpty { return "EXISONE_ACCESS_TOKEN is not set. Copy .env.example to .env and fill it in." }
        if product.isEmpty { return "EXISONE_PRODUCT is not set." }
        return nil
    }
}

enum LicenseKind { case checking, licensed, trial, trialEnded, expired, notLicensed, updateRequired, needsConnection }

struct LicenseState {
    var kind: LicenseKind
    var message: String
    var expiresAt: Date? = nil
    var features: [String] = []
    var updateAvailable: String? = nil
    var offline = false
    var canUse: Bool { kind == .licensed || kind == .trial }
}

/// The last answer the license server gave, so the app keeps working offline for a while.
/// A convenience, not copy protection: the server is asked again on every start.
private struct CachedLicense: Codable {
    var activationKey: String?
    var email: String?
    var status: String
    var expiresAt: Date?
    var features: [String]
    var lastValidated: Date?
    var hardwareId: String
}

/// Start-up check, activation, deactivation. The server decides; a network problem never locks out
/// a user who was licensed within the last `graceDays` days; every outcome has a plain message.
@MainActor
final class LicenseManager: ObservableObject {
    @Published private(set) var state = LicenseState(kind: .checking, message: "Checking license…")

    let settings: LicenseSettings
    let appVersion = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0"
    private let client: ExisOneClient?
    private let hardwareId = ExisOneClient.generateHardwareId()   // stable per machine
    private let cacheURL: URL

    init(settings: LicenseSettings) {
        self.settings = settings
        client = try? ExisOneClient(options: ExisOneClientOptions(
            baseURL: settings.baseURL, accessToken: settings.accessToken,
            offlinePublicKey: settings.publicKey, timeout: 10))
        let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        cacheURL = support.appendingPathComponent("Acme Notes/license-cache.json")
    }

    var storedKey: String? { loadCache()?.activationKey }

    func check() async {
        guard let client else { state = LicenseState(kind: .needsConnection, message: "The license server address is invalid."); return }
        state = LicenseState(kind: .checking, message: "Checking license…")
        var cached = loadCache()
        if cached?.hardwareId != hardwareId { cached = nil }   // copied from another Mac
        let key = cached?.activationKey

        do {
            // No key = trial check: the server starts (or continues) this Mac's trial.
            let r = try await client.validate(hardwareId: hardwareId, productName: settings.product,
                                              activationKey: key, version: appVersion)
            saveCache(CachedLicense(activationKey: key, email: cached?.email, status: r.status, expiresAt: r.expirationDate,
                                    features: r.features, lastValidated: Date(), hardwareId: hardwareId))
            state = fromServer(r, key: key)
        } catch {
            state = fromCache(cached, error: error)   // network down, or the server refused the token
        }
    }

    private func fromServer(_ r: ValidationResult, key: String?) -> LicenseState {
        let exp = r.expirationDate
        let update = isNewer(r.serverVersion, than: appVersion) ? r.serverVersion : nil
        switch r.status {
        case "licensed":
            let text = exp.map { Calendar.current.component(.year, from: $0) < 9999 ? "Licensed until \(day($0))." : "Licensed." } ?? "Licensed."
            return LicenseState(kind: .licensed, message: text, expiresAt: exp, features: r.features, updateAvailable: update)
        case "trial":
            let days = exp.map { max(0, Int(ceil($0.timeIntervalSinceNow / 86_400))) } ?? 0
            return LicenseState(kind: .trial, message: "Trial: \(days) day(s) left.", expiresAt: exp, features: r.features, updateAvailable: update)
        case "expired" where key == nil:
            return LicenseState(kind: .trialEnded, message: "Your trial has ended. Buy a license to keep using Acme Notes.")
        case "expired":
            return LicenseState(kind: .expired, message: "Your license expired on \(exp.map(day) ?? "?"). Renew it to keep using Acme Notes.", expiresAt: exp)
        case "version_outdated":
            return LicenseState(kind: .updateRequired, message: "This version is no longer supported. Please update to \(r.minimumRequiredVersion ?? "the latest version").",
                                updateAvailable: r.serverVersion)
        default:
            return LicenseState(kind: .notLicensed, message: key == nil ? "Enter a license key to use Acme Notes."
                                : "This license is not active on this Mac. Activate it again, or contact support.")
        }
    }

    private func fromCache(_ c: CachedLicense?, error: Error) -> LicenseState {
        let now = Date()
        if let c, ["licensed", "trial"].contains(c.status), let last = c.lastValidated,
           now.timeIntervalSince(last) < Double(settings.graceDays) * 86_400,
           c.expiresAt.map({ $0 > now }) ?? true {
            let until = last.addingTimeInterval(Double(settings.graceDays) * 86_400)
            return LicenseState(kind: c.status == "trial" ? .trial : .licensed,
                                message: "Working offline. Connect to the internet before \(day(until)) to keep using Acme Notes.",
                                expiresAt: c.expiresAt, features: c.features, offline: true)
        }
        if case ExisOneError.httpError(let code, _) = error, code == 401 || code == 403 {
            return LicenseState(kind: .needsConnection, message: "The license server refused this app's access token. Please contact Acme support.")
        }
        return LicenseState(kind: .needsConnection, message: "Can't reach the license server. Connect to the internet and try again.")
    }

    /// Returns a message to show; refreshes `state` afterwards.
    func activate(key: String, email: String) async -> (ok: Bool, message: String) {
        guard let client else { return (false, "The license server address is invalid.") }
        let key = key.trimmingCharacters(in: .whitespacesAndNewlines)
        let email = email.trimmingCharacters(in: .whitespacesAndNewlines)
        let r = await client.activate(activationKey: key, email: email, hardwareId: hardwareId,
                                      productName: settings.product, version: appVersion)
        guard r.success else {
            if r.errorCode == "version_outdated" {
                return (false, "This version is too old to activate. Please update to \(r.minimumRequiredVersion ?? "the latest version").")
            }
            if r.errorCode == nil, r.errorMessage?.hasPrefix("Network error") == true {   // the SDK reports these instead of throwing
                return (false, "Can't reach the license server. Activation needs an internet connection.")
            }
            return (false, r.errorCode ?? r.errorMessage ?? "Activation failed. Check the key and try again.")
        }
        saveCache(CachedLicense(activationKey: key, email: email, status: "licensed", expiresAt: nil, features: [],
                                lastValidated: nil, hardwareId: hardwareId))
        await check()
        return (true, "Activated. Thank you!")
    }

    /// Frees this Mac's seat so the key can be activated elsewhere.
    func deactivate() async -> (ok: Bool, message: String) {
        guard let client, let key = storedKey else { return (false, "No license is activated on this Mac.") }
        do {
            _ = try await client.deactivate(activationKey: key, hardwareId: hardwareId, productName: settings.product)
        } catch ExisOneError.httpError {
            return (false, "The license server refused the deactivation. Please contact Acme support.")
        } catch {
            return (false, "Can't reach the license server. Deactivation needs an internet connection.")
        }
        try? FileManager.default.removeItem(at: cacheURL)
        await check()
        return (true, "This Mac is deactivated. You can now activate the key on another computer.")
    }

    private func loadCache() -> CachedLicense? {
        guard let data = try? Data(contentsOf: cacheURL) else { return nil }
        return try? JSONDecoder().decode(CachedLicense.self, from: data)
    }

    private func saveCache(_ value: CachedLicense) {
        try? FileManager.default.createDirectory(at: cacheURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? JSONEncoder().encode(value).write(to: cacheURL)   // never fail the app over the cache
    }
}

private func day(_ d: Date) -> String { d.formatted(date: .abbreviated, time: .omitted) }

private func isNewer(_ candidate: String?, than current: String) -> Bool {
    guard let candidate else { return false }
    return candidate.compare(current, options: .numeric) == .orderedDescending
}
