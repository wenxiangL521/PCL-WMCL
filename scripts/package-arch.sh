#!/usr/bin/env bash
# 生成 Arch Linux 包：pcl-wmcl-bin-<ver>-1-x86_64.pkg.tar.zst
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.2.0}"
PKGREL="${2:-1}"
RID="${3:-linux-x64}"
WORK="$ROOT/artifacts/arch-$RID"
PKG="$WORK/pkg"
OUT="$ROOT/artifacts/pcl-wmcl-bin-$VERSION-$PKGREL-x86_64.pkg.tar.zst"

rm -rf "$WORK"
mkdir -p "$PKG/opt/pcl-wmcl" "$PKG/usr/bin" "$PKG/usr/share/applications" \
  "$PKG/usr/share/icons/hicolor/scalable/apps" "$PKG/usr/share/licenses/pcl-wmcl-bin"

source "$ROOT/scripts/lib/publish.sh"
wmcl_publish "$ROOT" "$RID" "$WORK/publish"

install -m0755 "$WORK/publish/WMCL.App" "$PKG/opt/pcl-wmcl/WMCL"
ln -sf /opt/pcl-wmcl/WMCL "$PKG/usr/bin/pcl-wmcl"
install -m0644 "$ROOT/packaging/wmcl-linux.svg" \
  "$PKG/usr/share/icons/hicolor/scalable/apps/wmcl-linux.svg"
install -m0644 "$ROOT/LICENSE.md" "$ROOT/PCL-LICENCE.md" "$PKG/usr/share/licenses/pcl-wmcl-bin/"
sed 's|^Exec=.*|Exec=/opt/pcl-wmcl/WMCL|' \
  "$ROOT/packaging/wmcl-linux.desktop" > "$PKG/usr/share/applications/pcl-wmcl.desktop"

SIZE="$(du -sb "$PKG" | cut -f1)"

cat > "$PKG/.PKGINFO" <<INFO
pkgname = pcl-wmcl-bin
pkgbase = pcl-wmcl-bin
pkgver = $VERSION-$PKGREL
pkgdesc = Plain Craft Launcher (PCL) WMCL - Linux 优先的 Minecraft 启动器
url = https://github.com/wenxiangL521/PCL-WMCL
builddate = $(date +%s)
packager = wenxiangL <dev@wmcl.local>
size = $SIZE
arch = x86_64
license = MIT
depend = glibc
depend = gcc-libs
depend = fontconfig
depend = libx11
depend = libice
depend = libsm
depend = mesa
provides = pcl-wmcl
conflict = pcl-wmcl
INFO

(
  cd "$PKG"
  find . -mindepth 1 -printf '%P\0' | LC_ALL=C sort -z |
    tar --owner=0 --group=0 --numeric-owner --no-recursion \
        --format=gnu --null -cf - -T -
) | zstd -q -19 -T0 -o "$OUT"

echo "打包完成：$OUT"
ls -lh "$OUT"
