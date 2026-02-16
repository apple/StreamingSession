// swift-tools-version:6.2

import PackageDescription

let package = Package(
    name: "StreamingSession",
    platforms: [
        .iOS(.v18)
    ],
    products: [
        .library(
            name: "StreamingSession",
            targets: ["StreamingSession", "CloudXRKitWrapper"]
        ),
    ],
    dependencies: [
        .package(url: "https://github.com/NVIDIA/cloudxr-framework", from: "6.0.2")
    ],
    targets: [
        .binaryTarget(
            name: "StreamingSession",
            path: "StreamingSession.xcframework"
        ),
        .target(
            name: "CloudXRKitWrapper",
            dependencies: [
                .product(name: "CloudXRKit", package: "cloudxr-framework")
            ],
            path: "Sources/CloudXRKitWrapper"
        ),
    ]
)
