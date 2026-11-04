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

# 用首字母把测试类分组，避免依赖命名空间清单
LETTERS=(A B C D E F G H I J K L M N O P Q R S T U V W X Y Z)
PER=$(( (${#LETTERS[@]} + SHARDS - 1) / SHARDS ))

pids=()
for ((s=0; s<SHARDS; s++)); do
  pattern=""
  for ((i=s*PER; i<(s+1)*PER && i<${#LETTERS[@]}; i++)); do
    pattern+="FullyQualifiedName~WMCL.App.Tests.${LETTERS[$i]}|"
  done
  pattern="${pattern%|}"
  [ -z "$pattern" ] && continue
  (
    filter="$pattern"
    if [ -n "$EXTRA_FILTER" ]; then filter="$filter&$EXTRA_FILTER"; fi
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

echo
echo "合计：通过 $passed，失败 $failedTests，共 $total"
[ "$failedTests" -eq 0 ] || exit 1
