#!/usr/bin/env bash
# 生成 RPM：pcl-wmcl-<ver>-1.x86_64.rpm（需要 rpmbuild）
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.2.0}"
PKGREL="${2:-1}"
RID="${3:-linux-x64}"
WORK="$ROOT/artifacts/rpm-$RID"
TOPDIR="$WORK/rpmbuild"
PUBLISH="$WORK/publish"

rm -rf "$WORK"
mkdir -p "$TOPDIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS} "$PUBLISH"

source "$ROOT/scripts/lib/publish.sh"
wmcl_publish "$ROOT" "$RID" "$PUBLISH"

mkdir -p "$TOPDIR/SOURCES/app"
install -m0755 "$PUBLISH/WMCL.App" "$TOPDIR/SOURCES/app/WMCL"
install -m0644 "$ROOT/packaging/wmcl-linux.svg" "$TOPDIR/SOURCES/app/wmcl-linux.svg"
sed 's|^Exec=.*|Exec=/opt/pcl-wmcl/WMCL|' \
  "$ROOT/packaging/wmcl-linux.desktop" > "$TOPDIR/SOURCES/app/pcl-wmcl.desktop"
install -m0644 "$ROOT/LICENSE.md" "$ROOT/PCL-LICENCE.md" "$TOPDIR/SOURCES/app/"
tar -C "$TOPDIR/SOURCES" -czf "$TOPDIR/SOURCES/app.tar.gz" app

cat > "$TOPDIR/SPECS/pcl-wmcl.spec" <<SPEC
Name:           pcl-wmcl
Version:        $VERSION
Release:        $PKGREL
Summary:        Plain Craft Launcher (PCL) WMCL - Linux 优先的 Minecraft 启动器
License:        MIT
URL:            https://github.com/wenxiangL521/PCL-WMCL
Source0:        app.tar.gz
BuildArch:      x86_64
Requires:       glibc, libstdc++, fontconfig, libX11, libICE, libSM, mesa-libGL
AutoReqProv:    no
# 关闭 rpmbuild 的 strip/build-id 后处理：它会破坏 .NET 单文件包
%global __os_install_post %{nil}
%global debug_package %{nil}

%description
wenxiangL Minecraft Launcher：Linux 优先的 Minecraft 启动器。
支持原版 Minecraft 安装/校验/启动、Fabric/Forge/NeoForge、
Modrinth、实例隔离、本地 Mod 管理与自更新。

%prep
%setup -q -c

%build

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/opt/pcl-wmcl %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/scalable/apps
mkdir -p %{buildroot}/usr/share/licenses/pcl-wmcl
install -m0755 app/WMCL %{buildroot}/opt/pcl-wmcl/WMCL
ln -sf /opt/pcl-wmcl/WMCL %{buildroot}/usr/bin/pcl-wmcl
install -m0644 app/wmcl-linux.svg %{buildroot}/usr/share/icons/hicolor/scalable/apps/wmcl-linux.svg
install -m0644 app/pcl-wmcl.desktop %{buildroot}/usr/share/applications/pcl-wmcl.desktop
install -m0644 app/LICENSE.md app/PCL-LICENCE.md %{buildroot}/usr/share/licenses/pcl-wmcl/

%files
/opt/pcl-wmcl/WMCL
/usr/bin/pcl-wmcl
/usr/share/applications/pcl-wmcl.desktop
/usr/share/icons/hicolor/scalable/apps/wmcl-linux.svg
/usr/share/licenses/pcl-wmcl/LICENSE.md
/usr/share/licenses/pcl-wmcl/PCL-LICENCE.md

%changelog
SPEC

rpmbuild --define "_topdir $TOPDIR" -bb "$TOPDIR/SPECS/pcl-wmcl.spec" >/dev/null
RPM="$(find "$TOPDIR/RPMS" -name '*.rpm' | head -1)"
OUT="$ROOT/artifacts/pcl-wmcl-$VERSION-$PKGREL.x86_64.rpm"
cp "$RPM" "$OUT"
echo "打包完成：$OUT"
