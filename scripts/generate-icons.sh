#!/usr/bin/env bash
# 从 packaging/wmcl-linux.svg（原图标的矢量描摹版）生成多尺寸 PNG、托盘图标与 Windows .ico。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SVG="$ROOT/packaging/wmcl-linux.svg"
ICONS="$ROOT/packaging/icons"
ASSETS="$ROOT/src/WMCL.App/Assets"

command -v rsvg-convert >/dev/null || { echo "需要 rsvg-convert" >&2; exit 1; }
mkdir -p "$ICONS"
for size in 16 24 32 48 64 128 256 512; do
  rsvg-convert -w "$size" -h "$size" "$SVG" -o "$ICONS/wmcl-linux-$size.png"
done

cp "$ICONS/wmcl-linux-512.png" "$ASSETS/wmcl-icon.png"
cp "$ICONS/wmcl-linux-64.png" "$ASSETS/wmcl-tray.png"
if command -v magick >/dev/null; then
  magick "$ICONS/wmcl-linux-512.png" -define icon:auto-resize=256,128,64,48,32,16 \
    "$ASSETS/wmcl-icon.ico"
fi
echo "图标已生成：$ICONS"
