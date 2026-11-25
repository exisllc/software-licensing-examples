import AppKit
import SwiftUI

@main
struct AcmeNotesApp: App {
    @StateObject private var license = LicenseManager(settings: LicenseSettings.load())

    init() {
        // Started with `swift run`: make it a normal foreground app with a Dock icon and a window.
        NSApplication.shared.setActivationPolicy(.regular)
        NSApplication.shared.activate(ignoringOtherApps: true)
    }

    var body: some Scene {
        WindowGroup("Acme Notes") {
            ContentView().environmentObject(license)
        }
    }
}

struct ContentView: View {
    @EnvironmentObject private var license: LicenseManager
    @State private var notes = "Welcome to Acme Notes.\n\nThis editor only works while you have a license or an active trial."
    @State private var showActivate = false
    @State private var key = ""
    @State private var email = ""
    @State private var alert: String?

    var body: some View {
        let s = license.state
        VStack(alignment: .leading, spacing: 0) {
            if let problem = license.settings.problem {
                Text(problem).foregroundStyle(.red).padding()
            } else {
                HStack(spacing: 8) {
                    Text(s.message).foregroundStyle(s.canUse ? (s.offline ? Color.orange : Color.green) : (s.kind == .checking ? Color.secondary : Color.red))
                    if s.kind != .licensed && s.kind != .updateRequired && s.kind != .checking {
                        Button("Enter license key…") { key = license.storedKey ?? ""; showActivate = true }
                    }
                    if [.trial, .trialEnded, .expired, .notLicensed].contains(s.kind), let url = URL(string: license.settings.buyURL) {
                        Link(s.kind == .expired ? "Renew" : "Buy", destination: url)
                    }
                    if s.kind == .licensed && !s.offline {
                        Button("Deactivate this Mac") { Task { alert = await license.deactivate().message } }
                    }
                    if s.kind == .needsConnection || s.offline {
                        Button("Retry") { Task { await license.check() } }
                    }
                    if let update = s.updateAvailable {
                        Text(s.kind == .updateRequired ? "Please install the latest version." : "Version \(update) is available.")
                            .foregroundStyle(.blue)
                    }
                }
                .padding(10)
            }
            TextEditor(text: $notes)
                .font(.body)
                .disabled(!s.canUse)          // the "app" works only while licensed
                .opacity(s.canUse ? 1 : 0.4)
        }
        .frame(minWidth: 640, minHeight: 400)
        .task { if license.settings.problem == nil { await license.check() } }   // check on every start
        .sheet(isPresented: $showActivate) {
            Form {
                TextField("License key", text: $key)
                TextField("Email used to buy", text: $email)
                HStack {
                    Button("Cancel") { showActivate = false }
                    Button("Activate") {
                        Task {
                            alert = await license.activate(key: key, email: email).message
                            showActivate = false
                        }
                    }
                    .keyboardShortcut(.defaultAction)
                    .disabled(key.isEmpty || email.isEmpty)
                }
            }
            .padding()
            .frame(width: 380)
        }
        .alert(alert ?? "", isPresented: Binding(get: { alert != nil }, set: { if !$0 { alert = nil } })) {
            Button("OK") { alert = nil }
        }
    }
}
