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
DEB="$ROOT/artifacts/PCL-linux-$VERSION-$ARCH.deb"

rm -rf "$WORK"
mkdir -p "$DATA/opt/pcl" "$DATA/usr/bin" "$DATA/usr/share/applications" \
  "$DATA/usr/share/icons/hicolor/scalable/apps" "$DATA/usr/share/doc/pcl-linux" \
  "$CONTROL" "$PUBLISH"

dotnet publish "$ROOT/src/PCL.App/PCL.App.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o "$PUBLISH"

cp "$PUBLISH/PCL.App" "$DATA/opt/pcl/PCL"
chmod 0755 "$DATA/opt/pcl/PCL"
cp "$ROOT/packaging/pcl-linux.svg" "$DATA/usr/share/icons/hicolor/scalable/apps/pcl-linux.svg"
cp "$ROOT/LICENSE.md" "$ROOT/README.md" "$DATA/usr/share/doc/pcl-linux/"
ln -s /opt/pcl/PCL "$DATA/usr/bin/pcl"

sed 's|^Exec=.*|Exec=/opt/pcl/PCL|' \
  "$ROOT/packaging/pcl-linux.desktop" > "$DATA/usr/share/applications/pcl-linux.desktop"

INSTALLED_SIZE="$(du -sk "$DATA" | cut -f1)"

cat > "$CONTROL/control" <<EOF
Package: pcl-linux
Version: $VERSION
Section: games
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Maintainer: PCL Linux <dev@pcl.local>
Depends: libc6, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1, libgl1, libglib2.0-0
Homepage: https://github.com/Hex-Dragon/PCL2
Description: PCL Linux
 Plain Craft Launcher 2 的 Linux 优先重写版本。
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
