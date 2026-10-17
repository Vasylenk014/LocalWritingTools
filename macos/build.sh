#!/bin/bash
set -e

# Always run from the directory containing this script
cd "$(dirname "$0")"

APP_NAME="WritingTools"
APP_DIR="${APP_NAME}.app/Contents"
MACOS_DIR="${APP_DIR}/MacOS"
RESOURCES_DIR="${APP_DIR}/Resources"
ICON_SRC="${ICON_SRC:-AppIcon.png}"

echo "Setting up Swift Package..."
cat <<EOF > Package.swift
// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "${APP_NAME}",
    platforms: [
        .macOS(.v14)
    ],
    targets: [
        .executableTarget(
            name: "${APP_NAME}",
            path: "Sources"
        )
    ]
)
EOF

echo "Building Swift Package..."
swift build -c release

echo "Creating bundle structure..."
mkdir -p "$MACOS_DIR"
mkdir -p "$RESOURCES_DIR"

# Set up AppIcon.icns
if [ -f "AppIcon.icns" ]; then
    echo "Using existing AppIcon.icns from project folder..."
    cp "AppIcon.icns" "$RESOURCES_DIR/AppIcon.icns"
elif [ -f "$ICON_SRC" ]; then
    echo "Creating AppIcon.icns from source image..."
    ICONSET="AppIcon.iconset"
    rm -rf "$ICONSET" base.png
    mkdir -p "$ICONSET"

    # Convert JPG to true PNG first
    sips -s format png "$ICON_SRC" --out base.png >/dev/null 2>&1

    sips -z 16 16     base.png --out "$ICONSET/icon_16x16.png" >/dev/null 2>&1
    sips -z 32 32     base.png --out "$ICONSET/icon_16x16@2x.png" >/dev/null 2>&1
    sips -z 32 32     base.png --out "$ICONSET/icon_32x32.png" >/dev/null 2>&1
    sips -z 64 64     base.png --out "$ICONSET/icon_32x32@2x.png" >/dev/null 2>&1
    sips -z 128 128   base.png --out "$ICONSET/icon_128x128.png" >/dev/null 2>&1
    sips -z 256 256   base.png --out "$ICONSET/icon_128x128@2x.png" >/dev/null 2>&1
    sips -z 256 256   base.png --out "$ICONSET/icon_256x256.png" >/dev/null 2>&1
    sips -z 512 512   base.png --out "$ICONSET/icon_256x256@2x.png" >/dev/null 2>&1
    sips -z 512 512   base.png --out "$ICONSET/icon_512x512.png" >/dev/null 2>&1
    sips -z 1024 1024 base.png --out "$ICONSET/icon_512x512@2x.png" >/dev/null 2>&1

    iconutil -c icns "$ICONSET" -o "$RESOURCES_DIR/AppIcon.icns"
    cp "$RESOURCES_DIR/AppIcon.icns" "AppIcon.icns"
    rm -rf "$ICONSET" base.png
    echo "AppIcon.icns created successfully."
fi

echo "Writing Info.plist..."
cat <<EOF > "${APP_DIR}/Info.plist"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>${APP_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.localwritingtools.${APP_NAME}</string>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.1</string>
    <key>CFBundleVersion</key>
    <string>2</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>LSUIElement</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>14.0</string>
</dict>
</plist>
EOF

cp ".build/release/${APP_NAME}" "${MACOS_DIR}/"

# Force Finder to refresh bundle icon
touch "${APP_NAME}.app"

echo "Success! Built ${APP_NAME}.app"
