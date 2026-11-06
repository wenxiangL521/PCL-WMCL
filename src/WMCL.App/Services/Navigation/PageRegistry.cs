using WMCL.App.ViewModels;

namespace WMCL.App.Services.Navigation;

/// <summary>
/// 页面注册表：登记「页面 key → 构造工厂」，首次取用时才实例化。
/// 首屏页面仍可提前创建并登记实例，其余页面按需创建。
/// </summary>
public sealed class PageRegistry
{
    private readonly Dictionary<string, Func<ViewModelBase>> _factories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ViewModelBase> _instances = new(StringComparer.Ordinal);

    public void Register(string key, Func<ViewModelBase> factory) => _factories[key] = factory;

    public void Register(string key, ViewModelBase instance) => _instances[key] = instance;

    public bool IsCreated(string key) => _instances.ContainsKey(key);

    public ViewModelBase Get(string key)
    {
        if (_instances.TryGetValue(key, out var existing)) return existing;
        if (!_factories.TryGetValue(key, out var factory))
            throw new InvalidOperationException($"未注册的页面：{key}");
        var created = factory();
        _instances[key] = created;
        return created;
    }

    public T Get<T>(string key) where T : ViewModelBase => (T)Get(key);
}
