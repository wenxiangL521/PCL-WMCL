#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.1.0}"
ARCH="${2:-amd64}"
RID="${3:-linux-x64}"

WORK="$ROOT/artifacts/deb-$ARCH"
DATA="$WORK/data"
CONTROL="$WORK/control"
PUBLISH="$WORK/publish"
DEB="$ROOT/artifacts/WMCL-linux-$VERSION-$ARCH.deb"

rm -rf "$WORK"
mkdir -p "$DATA/opt/wmcl" "$DATA/usr/bin" "$DATA/usr/share/applications" \
  "$DATA/usr/share/icons/hicolor/scalable/apps" "$DATA/usr/share/doc/wmcl-linux" \
  "$CONTROL" "$PUBLISH"

dotnet publish "$ROOT/src/WMCL.App/WMCL.App.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o "$PUBLISH"

cp "$PUBLISH/WMCL.App" "$DATA/opt/wmcl/WMCL"
chmod 0755 "$DATA/opt/wmcl/WMCL"
cp "$ROOT/packaging/wmcl-linux.svg" "$DATA/usr/share/icons/hicolor/scalable/apps/wmcl-linux.svg"
cp "$ROOT/LICENSE.md" "$ROOT/README.md" "$DATA/usr/share/doc/wmcl-linux/"
ln -s /opt/wmcl/WMCL "$DATA/usr/bin/wmcl"

sed 's|^Exec=.*|Exec=/opt/wmcl/WMCL|' \
  "$ROOT/packaging/wmcl-linux.desktop" > "$DATA/usr/share/applications/wmcl-linux.desktop"

INSTALLED_SIZE="$(du -sk "$DATA" | cut -f1)"

cat > "$CONTROL/control" <<EOF
Package: wmcl-linux
Version: $VERSION
Section: games
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Maintainer: WMCL <dev@wmcl.local>
Depends: libc6, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1, libgl1, libglib2.0-0
Description: WMCL
 wenxiangL Minecraft Launcher：Linux 优先的 Minecraft 启动器。
 支持原版 Minecraft 安装/校验/启动、Fabric/Forge/NeoForge、
 Modrinth、实例隔离、本地 Mod 管理与自更新。
EOF

(
  cd "$DATA"
  find opt usr -type f -print0 | xargs -0 md5sum > "$CONTROL/md5sums"
)

echo "2.0" > "$WORK/debian-binary"

(
  cd "$CONTROL"
  tar --owner=0 --group=0 -czf "$WORK/control.tar.gz" control md5sums
)

(
  cd "$DATA"
  tar --owner=0 --group=0 -czf "$WORK/data.tar.gz" opt usr
)

(
  cd "$WORK"
  rm -f "$DEB"
  ar r "$DEB" debian-binary control.tar.gz data.tar.gz
)

echo
echo "打包完成：$DEB"
ls -lh "$DEB"
