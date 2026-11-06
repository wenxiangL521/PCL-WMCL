using System.Runtime.CompilerServices;
using WMCL.App.ViewModels;

namespace WMCL.App.Tests;

/// <summary>
/// 测试环境统一提供内置 Client ID。
/// 干净克隆（没有 secrets.local.props）时构建出来的值为空，会让微软登录用例
/// 在开始阶段就返回，导致依赖登录流程的断言失败。
/// </summary>
internal static class TestClientId
{
    public const string Value = "00000000-0000-0000-0000-000000000000";

    [ModuleInitializer]
    internal static void Initialize() => MicrosoftLoginViewModel.ClientIdResolver = static () => Value;
}
