# 自动生成的界面截图

本目录由测试用例自动写入，同时纳入版本管理，用于在 GitHub 上直接查看界面效果。

- 生成器：[tests/WMCL.App.Tests/PageScreenshotGenerator.cs](../tests/WMCL.App.Tests/PageScreenshotGenerator.cs)
- 命令：`dotnet test tests/WMCL.App.Tests/WMCL.App.Tests.csproj -c Release --filter "FullyQualifiedName~PageScreenshotGenerator"`
- 内容：22 个页面，每个页面在浅色/深色两套主题下各一张静态图 + 一张滚动后截图
- 覆盖输出目录：设置环境变量 `WMCL_SHOT_DIR=/path/to/dir`，未设置时写回本目录

修改界面后请重新运行生成器并提交更新后的 PNG，保证截图与当前代码一致。
