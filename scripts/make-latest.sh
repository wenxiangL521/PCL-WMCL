#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?用法: make-latest.sh <版本> <安装包> [下载URL] [说明]}"
PACKAGE="${2:?缺少安装包路径}"
URL="${3:-}"
NOTES="${4:-}"
OUTPUT="${OUTPUT:-artifacts/latest.json}"

if [ ! -f "$PACKAGE" ]; then
  echo "安装包不存在：$PACKAGE" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUTPUT")"
SHA256="$(sha256sum "$PACKAGE" | cut -d' ' -f1)"
SIZE="$(stat -c%s "$PACKAGE")"

python3 - "$VERSION" "$PACKAGE" "$URL" "$NOTES" "$OUTPUT" "$SHA256" "$SIZE" <<'PY'
import json, os, sys
version, package, url, notes, output, sha256, size = sys.argv[1:]
if not url:
    url = os.path.basename(package)
data = {
    "version": version,
    "url": url,
    "sha256": sha256,
    "notes": notes,
    "size": int(size),
}
with open(output, "w", encoding="utf-8") as handle:
    json.dump(data, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
print(json.dumps(data, ensure_ascii=False, indent=2))
PY

echo
echo "已生成：$OUTPUT"
