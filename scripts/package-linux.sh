#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-0.1.0}"
RID="${2:-linux-x64}"
ARTIFACTS="$ROOT/artifacts"
STAGE="$ARTIFACTS/$RID"
PUBLISH="$STAGE/publish"

rm -rf "$STAGE"
mkdir -p "$PUBLISH"

dotnet publish "$ROOT/src/PCL.App/PCL.App.csproj" \
  -c Release \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=None \
  -o "$PUBLISH"

cp "$PUBLISH/PCL.App" "$STAGE/PCL"
chmod +x "$STAGE/PCL"
cp "$ROOT/packaging/pcl-linux.desktop" "$STAGE/PCL.desktop"
cp "$ROOT/packaging/pcl-linux.svg" "$STAGE/pcl-linux.svg"
cp "$ROOT/README.md" "$ROOT/LICENSE.md" "$STAGE/"

TARBALL="$ARTIFACTS/PCL-linux-x64-$VERSION.tar.gz"
tar -C "$STAGE" -czf "$TARBALL" PCL PCL.desktop pcl-linux.svg README.md LICENSE.md
sha256sum "$TARBALL" > "$TARBALL.sha256"

echo
echo "打包完成：$TARBALL"
cat "$TARBALL.sha256"
