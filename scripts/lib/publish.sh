#!/usr/bin/env bash
# 共享的 dotnet publish 步骤：各打包脚本统一调用，避免参数漂移。
# 用法：wmcl_publish <仓库根> <RID> <输出目录>
wmcl_publish() {
  local root="$1" rid="$2" out="$3"
  dotnet publish "$root/src/WMCL.App/WMCL.App.csproj" \
    -c Release \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None \
    -o "$out"
}
