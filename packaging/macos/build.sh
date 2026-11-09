#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
  echo 'Build the macOS package on a Mac with .NET 10 and Xcode Command Line Tools.' >&2
  exit 1
fi
rid="${1:-osx-arm64}"
case "$rid" in
  osx-arm64) arch=arm64 ;;
  osx-x64) arch=x86_64 ;;
  *) echo 'Usage: bash packaging/macos/build.sh [osx-arm64|osx-x64]' >&2; exit 1 ;;
esac
root="$(cd "$(dirname "$0")/../.." && pwd)"
version="${FLYGUY_VERSION:-1.0.0}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo 'FLYGUY_VERSION must have the form 1.2.3.' >&2; exit 1
fi
mkdir -p "$root/artifacts/macos"
# A fresh staging directory avoids mixing publish output from different builds.
stage="$(mktemp -d "$root/artifacts/macos/stage-$rid.XXXXXX")"
app="$stage/Fly Guy.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$root/FlyGuy/FlyGuy.Mac/FlyGuy.Mac.csproj" \
  -c Release -r "$rid" --self-contained true -p:DebugType=None -p:DebugSymbols=false \
  -p:Version="$version" -o "$app/Contents/MacOS"
xcrun swiftc -swift-version 5 -O -emit-library -target "$arch-apple-macosx14.0" \
  -module-name FlyGuyCapture -framework Foundation -framework CoreGraphics -framework ScreenCaptureKit \
  -Xlinker -install_name -Xlinker @rpath/libFlyGuyCapture.dylib \
  "$root/FlyGuy/FlyGuy.Mac/native/Capture.swift" \
  -o "$app/Contents/MacOS/libFlyGuyCapture.dylib"
sed "s/@VERSION@/$version/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
chmod +x "$app/Contents/MacOS/FlyGuy.Mac"
plutil -lint "$app/Contents/Info.plist"
# Ad-hoc signing makes the arm64 bundle locally executable. This is not Developer
# ID signing or notarization; distribution signing is intentionally separate.
find "$app/Contents/MacOS" -type f \( -name '*.dylib' -o -name 'FlyGuy.Mac' \) -print0 |
while IFS= read -r -d '' binary; do codesign --force --sign - "$binary"; done
codesign --force --sign - "$app"
codesign --verify --deep --strict --verbose=2 "$app"
if [[ "$(uname -m)" == "$arch" ]]; then
  "$app/Contents/MacOS/FlyGuy.Mac" --smoke-test
fi
archive="$root/artifacts/macos/FlyGuy-$version-$rid.zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$archive"
shasum -a 256 "$archive" > "$archive.sha256"
echo "App: $app"
echo "Package: $archive"
