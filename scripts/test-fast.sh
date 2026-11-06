#!/usr/bin/env bash
# 按 Kind Trait 并行跑 App 测试：Headless 与 Unit 各一片，Motion/UiInteraction 由串行组单独跑。
# 用法：scripts/test-fast.sh [附加过滤，如 Kind!=Integration]
# 之所以按 Kind 而不是类名首字母：Kind 由测试自己声明，增删测试不会让分片失真。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXTRA_FILTER="${1:-}"
PROJECT="$ROOT/tests/WMCL.App.Tests/WMCL.App.Tests.csproj"
LOG_DIR="$ROOT/artifacts/test-fast"
mkdir -p "$LOG_DIR"
rm -f "$LOG_DIR"/shard-*.log "$LOG_DIR"/shard-*.exit "$LOG_DIR"/letters.txt

SHARD_KINDS=(Headless Unit)

pids=()
for i in "${!SHARD_KINDS[@]}"; do
  kind="${SHARD_KINDS[$i]}"
  (
    filter="Kind=${kind}"
    if [ -n "$EXTRA_FILTER" ]; then filter="($filter)&$EXTRA_FILTER"; fi
    dotnet test "$PROJECT" -c Release --no-build --nologo --filter "$filter" \
      > "$LOG_DIR/shard-$i.log" 2>&1
    echo "$?" > "$LOG_DIR/shard-$i.exit"
  ) &
  pids+=($!)
done

failed=0
for pid in "${pids[@]}"; do wait "$pid" || failed=1; done

total=0; passed=0; failedTests=0
for i in "${!SHARD_KINDS[@]}"; do
  log="$LOG_DIR/shard-$i.log"
  [ -f "$log" ] || continue
  line=$(grep -E "Passed!|Failed!" "$log" | tail -1)
  [ -z "$line" ] && continue
  echo "分片 ${SHARD_KINDS[$i]}: $line"
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
