#!/usr/bin/env bash
# 本地一键构建全部平台产物，并生成统一的 SHA256SUMS。
# 用法：scripts/make-release.sh 0.2.1
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?用法: make-release.sh <版本，例如 0.2.1>}"
ARTIFACTS="$ROOT/artifacts"

mkdir -p "$ARTIFACTS"

echo "==> Linux 通用包"
bash "$ROOT/scripts/package-linux.sh" "$VERSION"

echo "==> Debian / Ubuntu"
bash "$ROOT/scripts/package-deb.sh" "$VERSION" amd64 linux-x64

echo "==> Arch Linux"
bash "$ROOT/scripts/package-arch.sh" "$VERSION" 1 linux-x64

echo "==> Fedora / RHEL / openSUSE"
if command -v rpmbuild >/dev/null 2>&1; then
  bash "$ROOT/scripts/package-rpm.sh" "$VERSION" 1 linux-x64
else
  echo "    跳过：本机没有 rpmbuild（可在 Fedora 系发行版或容器中单独构建）"
fi

echo "==> Windows x64"
dotnet publish "$ROOT/src/WMCL.App/WMCL.App.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$ARTIFACTS/win-x64" >/dev/null
mv -f "$ARTIFACTS/win-x64/WMCL.App.exe" "$ARTIFACTS/WMCL.exe"

echo "==> SHA256SUMS"
cd "$ARTIFACTS"
rm -f SHA256SUMS
for file in \
  "WMCL.exe" \
  "PCL-WMCL-linux-x64-$VERSION.tar.gz" \
  "PCL-WMCL-linux-$VERSION-amd64.deb" \
  "pcl-wmcl-bin-$VERSION-1-x86_64.pkg.tar.zst" \
  "pcl-wmcl-$VERSION-1.x86_64.rpm"; do
  [ -f "$file" ] && sha256sum "$file" >> SHA256SUMS
done

echo
echo "全部产物位于 $ARTIFACTS："
ls -lh "$ARTIFACTS" | grep -vE '^d|^total'
