// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ComicReader",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "ComicReader", targets: ["ComicReader"]),
        .executable(name: "ReaderChecks", targets: ["ReaderChecks"]),
        .executable(name: "ReaderLab", targets: ["ReaderLab"]),
        .library(name: "ComicCore", targets: ["ComicCore"])
    ],
    dependencies: [
        .package(url: "https://github.com/weichsel/ZIPFoundation.git", exact: "0.9.20"),
        .package(url: "https://github.com/scinfu/SwiftSoup.git", exact: "2.6.0"),
        .package(url: "https://github.com/groue/GRDB.swift.git", exact: "7.0.0")
    ],
    targets: [
        .target(name: "ComicCore", dependencies: ["ZIPFoundation", "SwiftSoup", .product(name: "GRDB", package: "GRDB.swift")]),
        .executableTarget(name: "ComicReader", dependencies: ["ComicCore"]),
        .executableTarget(name: "ReaderChecks", dependencies: ["ComicCore", "ZIPFoundation"]),
        .executableTarget(name: "ReaderLab", dependencies: ["ComicCore"])
    ],
    swiftLanguageModes: [.v5]
)
