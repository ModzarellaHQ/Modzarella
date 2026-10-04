#!/bin/sh
set -e
rid=$1
cd "$(dirname "$0")/.."
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Modzarella/Modzarella.csproj)
dotnet publish src/Modzarella -c Release -r "$rid" --self-contained -o "build/$rid" \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
mkdir -p dist
case $rid in
  osx-*)
    app="build/$rid-app/Modzarella.app"
    rm -rf "$app" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp "build/$rid/Modzarella" "$app/Contents/MacOS/"
    cp assets/branding/icon.icns "$app/Contents/Resources/"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Modzarella</string>
  <key>CFBundleDisplayName</key><string>Modzarella</string>
  <key>CFBundleIdentifier</key><string>app.modzarella</string>
  <key>CFBundleExecutable</key><string>Modzarella</string>
  <key>CFBundleIconFile</key><string>icon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key><dict><key>NSAllowsLocalNetworking</key><true/></dict>
</dict>
</plist>
PLIST
    codesign --force --deep -s - "$app"
    arch=${rid#osx-}; [ "$arch" = x64 ] && arch=intel || arch=apple-silicon
    rm -f "dist/Modzarella-mac-$arch.zip"
    ditto -c -k --keepParent "$app" "dist/Modzarella-mac-$arch.zip" ;;
  win-*) cp "build/$rid/Modzarella.exe" dist/Modzarella-windows.exe ;;
  linux-*) tar -czf dist/Modzarella-linux.tar.gz -C "build/$rid" Modzarella ;;
esac
