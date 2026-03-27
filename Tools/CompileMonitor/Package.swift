// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "CompileMonitor",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(
            name: "CompileMonitor",
            path: "Sources"
        )
    ]
)
