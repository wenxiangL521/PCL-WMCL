#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.1.0}"
RID="${2:-linux-x64}"
WORK="$ROOT/artifacts/appimage-$RID"
APPDIR="$WORK/PCL.AppDir"
PUBLISH="$WORK/publish"
OUT="$ROOT/artifacts/PCL-linux-x64-$VERSION.AppImage"
TOOL="${APPIMAGETOOL:-}"

rm -rf "$WORK"
mkdir -p "$APPDIR"

dotnet publish "$ROOT/src/PCL.App/PCL.App.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o "$PUBLISH"

cp "$PUBLISH/PCL.App" "$APPDIR/PCL"
chmod +x "$APPDIR/PCL"
cp "$ROOT/packaging/pcl-linux.svg" "$APPDIR/pcl-linux.svg"
sed 's|^Exec=.*|Exec=PCL|' \
  "$ROOT/packaging/pcl-linux.desktop" > "$APPDIR/pcl-linux.desktop"

cat > "$APPDIR/AppRun" <<'RUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/PCL" "$@"
RUN
chmod +x "$APPDIR/AppRun"

if [ -z "$TOOL" ]; then
  if command -v appimagetool >/dev/null 2>&1; then
    TOOL="$(command -v appimagetool)"
  else
    TOOL="$HOME/.cache/PCL/appimagetool.AppImage"
    if [ ! -x "$TOOL" ]; then
      mkdir -p "$(dirname "$TOOL")"
      echo "下载 appimagetool…"
      curl -L --retry 3 --retry-delay 2 \
        -o "$TOOL" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
      chmod +x "$TOOL"
    fi
  fi
fi

ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 \
  "$TOOL" --no-appstream "$APPDIR" "$OUT"

chmod +x "$OUT"
echo
echo "打包完成：$OUT"
ls -lh "$OUT"
