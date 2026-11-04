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

source "$ROOT/scripts/lib/publish.sh"
wmcl_publish "$ROOT" "$RID" "$PUBLISH"

cp "$PUBLISH/WMCL.App" "$STAGE/WMCL"
chmod +x "$STAGE/WMCL"
sed 's|^Exec=.*|Exec=WMCL|' \
  "$ROOT/packaging/wmcl-linux.desktop" > "$STAGE/WMCL.desktop"
cp "$ROOT/packaging/wmcl-linux.svg" "$STAGE/wmcl-linux.svg"
cp "$ROOT/README.md" "$ROOT/LICENSE.md" "$ROOT/PCL-LICENCE.md" "$STAGE/"

TARBALL="$ARTIFACTS/PCL-WMCL-linux-x64-$VERSION.tar.gz"
tar -C "$STAGE" -czf "$TARBALL" WMCL WMCL.desktop wmcl-linux.svg README.md LICENSE.md PCL-LICENCE.md

echo
echo "打包完成：$TARBALL"
