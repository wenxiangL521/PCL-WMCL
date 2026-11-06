#!/usr/bin/env bash
# 按 Kind Trait 跑 App 测试：Headless 与 Unit 各一片，Motion/UiInteraction 由串行组单独跑。
# 用法：scripts/test-fast.sh [附加过滤，如 Kind!=Integration]
# 默认顺序执行：Avalonia 12 + xUnit v3 下 headless 用例对 CPU 争用敏感，两片并行会出现
# 无意义的时序抖动；Headless 本身占满大部分耗时，顺序跑总时长基本不变。
# 本地想并行可设 WMCL_TEST_PARALLEL=1。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXTRA_FILTER="${1:-}"
PROJECT="$ROOT/tests/WMCL.App.Tests/WMCL.App.Tests.csproj"
LOG_DIR="$ROOT/artifacts/test-fast"
mkdir -p "$LOG_DIR"
rm -f "$LOG_DIR"/shard-*.log "$LOG_DIR"/shard-*.exit "$LOG_DIR"/letters.txt

SHARD_KINDS=(Headless Unit)

run_shard() {
  local i="$1" kind="$2"
  local filter="Kind=${kind}"
  if [ -n "$EXTRA_FILTER" ]; then filter="($filter)&$EXTRA_FILTER"; fi
  dotnet test "$PROJECT" -c Release --no-build --nologo --filter "$filter" \
    > "$LOG_DIR/shard-$i.log" 2>&1
  echo "$?" > "$LOG_DIR/shard-$i.exit"
}

failed=0
if [ "${WMCL_TEST_PARALLEL:-0}" = "1" ]; then
  pids=()
  for i in "${!SHARD_KINDS[@]}"; do
    run_shard "$i" "${SHARD_KINDS[$i]}" &
    pids+=($!)
  done
  for pid in "${pids[@]}"; do wait "$pid" || failed=1; done
else
  for i in "${!SHARD_KINDS[@]}"; do
    run_shard "$i" "${SHARD_KINDS[$i]}" || failed=1
  done
fi

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
