// Lemon Squeezy License API calls.
import Foundation

enum LicenseAPI {
    static let base = URL(string: "https://licenses.exisone.com/v1/licenses")!

    static func call(_ action: String, _ params: [String: String]) async throws -> [String: Any] {
        var req = URLRequest(url: base.appendingPathComponent(action))
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        var form = URLComponents()
        form.queryItems = params.map { URLQueryItem(name: $0.key, value: $0.value) }
        req.httpBody = form.percentEncodedQuery?.data(using: .utf8)
        let (data, _) = try await URLSession.shared.data(for: req)
        // Failures carry an "error" message.
        return (try JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
    }

    static func activate(key: String, instanceName: String) async throws -> [String: Any] {
        try await call("activate", ["license_key": key, "instance_name": instanceName])
    }

    static func validate(key: String, instanceId: String? = nil) async throws -> [String: Any] {
        var p = ["license_key": key]
        if let instanceId { p["instance_id"] = instanceId }
        return try await call("validate", p)
    }

    static func deactivate(key: String, instanceId: String) async throws -> [String: Any] {
        try await call("deactivate", ["license_key": key, "instance_id": instanceId])
    }
}
