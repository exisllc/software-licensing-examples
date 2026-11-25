// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AcmeNotesMac",
    platforms: [.macOS(.v13)],
    dependencies: [
        .package(url: "https://github.com/exisllc/ExisOne.Swift", from: "0.7.0"),
    ],
    targets: [
        .executableTarget(
            name: "AcmeNotesMac",
            dependencies: [.product(name: "ExisOne", package: "ExisOne.Swift")]
        ),
    ]
)
