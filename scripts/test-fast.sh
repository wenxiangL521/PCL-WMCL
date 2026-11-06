#!/usr/bin/env bash
# 把 App 测试按命名空间切片，多进程并发跑，缩短墙钟时间。
# 用法：scripts/test-fast.sh [切片数=4] [附加过滤，如 Kind!=Integration]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHARDS="${1:-4}"
EXTRA_FILTER="${2:-}"
PROJECT="$ROOT/tests/WMCL.App.Tests/WMCL.App.Tests.csproj"
LOG_DIR="$ROOT/artifacts/test-fast"
mkdir -p "$LOG_DIR"

# 按「测试数量」把首字母均衡分配到各分片：先统计每个字母的用例数，再做贪心装箱。
# 之前的固定字母切分在用例数分布不均时会出现个别分片明显偏慢。
LETTER_STATS="$LOG_DIR/letters.txt"
if [ ! -s "$LETTER_STATS" ]; then
  dotnet test "$PROJECT" -c Release --no-build --nologo --list-tests 2>/dev/null \
    | awk '/^    WMCL\.App\.Tests\./ {split($1,a,"("); print a[1]}' \
    | sed 's/^WMCL\.App\.Tests\.//' \
    | grep -oE '^.?' \
    | tr -d '.' \
    | sort \
    | uniq -c \
    | awk '{print $2, $1}' > "$LETTER_STATS" || true
fi

mapfile -t SHARD_LETTERS < <(python3 - "$SHARDS" "$LETTER_STATS" <<'PYEOF'
import sys
shards = max(1, int(sys.argv[1]))
stats = []
try:
    for line in open(sys.argv[2]):
        parts = line.split()
        if len(parts) == 2 and parts[1].isdigit():
            stats.append((parts[0].upper(), int(parts[1])))
except FileNotFoundError:
    pass
if not stats:
    stats = [(chr(c), 1) for c in range(ord('A'), ord('Z') + 1)]
stats.sort(key=lambda item: -item[1])
buckets = [[0, []] for _ in range(shards)]
for letter, count in stats:
    target = min(buckets, key=lambda b: b[0])
    target[0] += count
    target[1].append(letter)
for _, letters in buckets:
    print(" ".join(letters))
PYEOF
)

pids=()
for ((s=0; s<SHARDS; s++)); do
  pattern=""
  [ "${SHARD_LETTERS[$s]:-}" = " " ] && continue
  for letter in ${SHARD_LETTERS[$s]:-}; do
    pattern+="FullyQualifiedName~WMCL.App.Tests.${letter}|"
  done
  pattern="${pattern%|}"
  [ -z "$pattern" ] && continue
  (
    filter="$pattern"
    if [ -n "$EXTRA_FILTER" ]; then filter="($filter)&$EXTRA_FILTER"; fi
    dotnet test "$PROJECT" -c Release --no-build --nologo --filter "$filter" \
      > "$LOG_DIR/shard-$s.log" 2>&1
    echo "$?" > "$LOG_DIR/shard-$s.exit"
  ) &
  pids+=($!)
done

failed=0
for pid in "${pids[@]}"; do wait "$pid" || failed=1; done

total=0; passed=0; failedTests=0
for ((s=0; s<SHARDS; s++)); do
  log="$LOG_DIR/shard-$s.log"
  [ -f "$log" ] || continue
  line=$(grep -E "Passed!|Failed!" "$log" | tail -1)
  [ -z "$line" ] && continue
  echo "分片 $s: $line"
  p=$(echo "$line" | sed -n 's/.*Passed: *\([0-9]*\).*/\1/p')
  f=$(echo "$line" | sed -n 's/.*Failed: *\([0-9]*\).*/\1/p')
  total=$(( total + ${p:-0} + ${f:-0} ))
  passed=$(( passed + ${p:-0} ))
  failedTests=$(( failedTests + ${f:-0} ))
done

if [ "$failedTests" -gt 0 ]; then
  echo
  echo "失败用例："
  grep -h "^  Failed " "$LOG_DIR"/shard-*.log | sed 's/^  Failed /  - /' | head -40
fi

echo
echo "合计：通过 $passed，失败 $failedTests，共 $total"
[ "$failedTests" -eq 0 ] || exit 1
