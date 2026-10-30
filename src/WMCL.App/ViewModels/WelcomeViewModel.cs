using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.Core.Localization;

namespace WMCL.App.ViewModels;

/// <summary>首次运行欢迎引导的 ViewModel。</summary>
public partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    /// <summary>引导步骤数（与 locales yaml 中 welcome.step.N.* 键对应）。</summary>
    private const int StepCount = 3;

    private bool _disposed;

    public WelcomeViewModel()
    {
        Step = 0;
        UpdateStep();
    }

    [ObservableProperty] private int _step;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _stepTitle = "";
    [ObservableProperty] private string _stepDescription = "";
    [ObservableProperty] private string _stepProgress = "";
    [ObservableProperty] private string _backText = "";
    [ObservableProperty] private string _nextText = "";
    [ObservableProperty] private string _skipText = "";

    public bool CanGoBack => Step > 0;

    /// <summary>用户完成或跳过引导时触发。</summary>
    public Action? OnCompleted { get; set; }

    private void UpdateStep()
    {
        Title = Localizer.T("welcome.title");
        Subtitle = Localizer.T("welcome.subtitle");
        StepTitle = Localizer.T("welcome.step." + (Step + 1) + ".title");
        StepDescription = Localizer.T("welcome.step." + (Step + 1) + ".description");
        // 缺键（locale 未加载）时 T 返回键名本身，fallback 提供内置中文兜底。
        StepProgress = Localizer.T("welcome.progress",
            $"第 {Step + 1} / {StepCount} 步", Step + 1, StepCount);
        BackText = Localizer.T("welcome.back");
        NextText = Step == StepCount - 1 ? Localizer.T("welcome.finish") : Localizer.T("welcome.next");
        SkipText = Localizer.T("welcome.skip");
    }

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount - 1)
        {
            Step++;
            UpdateStep();
        }
        else
        {
            OnCompleted?.Invoke();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
            UpdateStep();
        }
    }

    [RelayCommand]
    private void Skip()
    {
        OnCompleted?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnCompleted = null;
    }
}
