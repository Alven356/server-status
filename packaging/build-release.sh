#!/bin/zsh
set -euo pipefail

ROOT_DIR="${0:A:h:h}"
VERSION="2.1.0"
PROJECT="$ROOT_DIR/native/ServerStatus/ServerStatus.csproj"
RELEASE_ROOT="$ROOT_DIR/work/release-$VERSION"
PUBLISH_ROOT="$RELEASE_ROOT/publish"
APP_ROOT="$RELEASE_ROOT/apps"
DMG_ROOT="$RELEASE_ROOT/dmg"
OUTPUT_ROOT="$ROOT_DIR/outputs"
LOCAL_DOTNET="$ROOT_DIR/work/dotnet/dotnet"
DOTNET_BIN="dotnet"
if [[ -x "$LOCAL_DOTNET" ]]; then
  DOTNET_BIN="$LOCAL_DOTNET"
fi

if [[ -e "$RELEASE_ROOT" ]]; then
  print -u2 "Release workspace already exists: $RELEASE_ROOT"
  exit 1
fi

mkdir -p "$PUBLISH_ROOT" "$APP_ROOT" "$DMG_ROOT" "$OUTPUT_ROOT"

export DOTNET_CLI_HOME="$ROOT_DIR/work/dotnet-home"
export NUGET_PACKAGES="$ROOT_DIR/work/nuget"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export AVALONIA_TELEMETRY_OPTOUT=1

for rid in osx-x64 osx-arm64 win-x64; do
  "$DOTNET_BIN" publish "$PROJECT" -c Release -r "$rid" --self-contained true \
    -p:DebugType=None -p:DebugSymbols=false \
    -o "$PUBLISH_ROOT/$rid"
done

for arch in x64 arm64; do
  app="$APP_ROOT/$arch/server-status.app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp "$ROOT_DIR/packaging/Info.plist" "$app/Contents/Info.plist"
  cp "$ROOT_DIR/build/icon.icns" "$app/Contents/Resources/icon.icns"
  cp -R "$PUBLISH_ROOT/osx-$arch/." "$app/Contents/MacOS/"
  chmod 755 "$app/Contents/MacOS/server-status"
  plutil -lint "$app/Contents/Info.plist"
  codesign --force --deep --sign - "$app"

  stage="$DMG_ROOT/$arch"
  mkdir -p "$stage"
  cp -R "$app" "$stage/server-status.app"
  ln -s /Applications "$stage/Applications"
  hdiutil create -volname "server-status $VERSION" -srcfolder "$stage" -format UDZO \
    "$OUTPUT_ROOT/server-status-$VERSION-mac-$arch.dmg"
done

NSIS_ROOT="/Users/silvialanfan/Library/Caches/electron-builder/nsis/nsis-3.0.4.1"
NSISDIR="$NSIS_ROOT" "$NSIS_ROOT/mac/makensis" \
  -DPublishDir="$PUBLISH_ROOT/win-x64" \
  -DOutputFile="$OUTPUT_ROOT/server-status-$VERSION-win-x64.exe" \
  -DIconFile="$ROOT_DIR/build/icon.ico" \
  "$ROOT_DIR/packaging/server-status.nsi"

cd "$OUTPUT_ROOT"
shasum -a 256 \
  "server-status-$VERSION-mac-x64.dmg" \
  "server-status-$VERSION-mac-arm64.dmg" \
  "server-status-$VERSION-win-x64.exe" > "SHA256SUMS-$VERSION.txt"
cp "SHA256SUMS-$VERSION.txt" SHA256SUMS.txt
