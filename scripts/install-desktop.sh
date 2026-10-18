#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="${1:-$root/src/WMCL.App/bin/Release/net10.0/WMCL.App}"
if [[ ! -x "$binary" ]]; then
  echo "找不到可执行文件：$binary" >&2
  echo "用法：$0 [/path/to/WMCL.App]" >&2
  exit 1
fi

data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
desktop_dir="$data_home/applications"
icon_dir="$data_home/icons/hicolor/256x256/apps"
mkdir -p "$desktop_dir" "$icon_dir"

sed "s#WMCL_APP_BINARY#$binary#g" \
  "$root/packaging/wmcl-linux.desktop" > "$desktop_dir/wmcl-linux.desktop"
cp "$root/src/WMCL.App/Assets/wmcl-icon.png" "$icon_dir/wmcl-linux.png"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$desktop_dir" >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t "$data_home/icons/hicolor" >/dev/null 2>&1 || true
fi

echo "已安装：$desktop_dir/wmcl-linux.desktop"
echo "图标：  $icon_dir/wmcl-linux.png"
