// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "PrayerKit",
    defaultLocalization: "en",
    platforms: [.iOS(.v26), .macOS(.v26)],
    products: [
        .library(name: "PrayerKit", targets: ["PrayerKit"]),
    ],
    targets: [
        .target(name: "PrayerKit", swiftSettings: [.swiftLanguageMode(.v6)]),
        .testTarget(name: "PrayerKitTests", dependencies: ["PrayerKit"], swiftSettings: [.swiftLanguageMode(.v6)]),
    ]
)
