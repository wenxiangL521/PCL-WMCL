# 贡献指南

## 开发

```bash
dotnet build WMCL.slnx -c Release
dotnet test  WMCL.slnx -c Release
dotnet run   --project src/WMCL.App -c Release
```

- 目标框架：.NET 10；UI：Avalonia 11。
- 参考仓库 `PCL2` / `PCL-CE` / `PCL-ME` **只读**，不要修改或提交其内容。
- 修改 UI 前先找到原版对应源码/资源，说明布局、尺寸、交互或动画依据；不要凭审美新增占位页、假按钮或空操作。

## 测试

- 测试分：Headless、静态截图、原生交互、动画录像、公共服务烟测、真实 Minecraft E2E；它们不能互相替代。
- 提交前跑全量 `dotnet test WMCL.slnx -c Release`。
- 当前验证摘要写在 [docs/VERIFICATION.md](docs/VERIFICATION.md)，不要为每次小改动新增独立 `*_VERIFICATION.md`。
- 需要保留的历史证据放到仓库外的验证目录，不提交构建产物/TRX/日志。

## 提交前检查

```bash
git diff --check
dotnet format whitespace WMCL.slnx --verify-no-changes --no-restore
```

- 不提交 `bin/`、`obj/`、`artifacts/`、`dist/`、日志、测试结果或密钥。
- 不提交真实 Azure Client ID、CurseForge Key、账号 token、Cookie。
- 二进制发布物只作为 GitHub Release asset。

## 许可

提交即表示同意以本仓库的 MIT 条款发布你的代码贡献；PCL2/PCL-CE 素材仍归原作者。
