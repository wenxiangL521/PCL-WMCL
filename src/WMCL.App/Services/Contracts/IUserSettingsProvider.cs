namespace WMCL.App.Services;

/// <summary>
/// 只需要读取用户设置的 ViewModel 依赖这层最窄接口，而不是整个 <see cref="AppServices"/>。
/// <see cref="AppServices"/> 实现了它，因此现有调用点可以继续传 AppServices。
/// </summary>
public interface IUserSettingsProvider
{
    AppSettings Settings { get; }
}

/// <summary>需要读写并保存设置的场景。视图层与基础设施仍由调用方自行组合。</summary>
public interface IUserSettingsStore : IUserSettingsProvider
{
    void SaveSettings();
}
