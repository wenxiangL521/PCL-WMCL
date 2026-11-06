using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Controls;
using WMCL.App.Services;
using WMCL.App.Services.Infrastructure;
using WMCL.App.ViewModels;
using WMCL.App.Views;

namespace WMCL.App.Tests;

[Trait("Kind", "Headless")]
public sealed class WindowPresentationTests
{
    [AvaloniaTheory]
    [InlineData("light", 0.8)]
    [InlineData("light", 1.0)]
    [InlineData("light", 1.5)]
    [InlineData("dark", 0.8)]
    [InlineData("dark", 1.0)]
    [InlineData("dark", 1.5)]
    public async Task MainSettingsUseRealLayoutScalingAndShortcutInputsStayInsideThePage(string theme, double scale)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var settings = new AppSettings { GameDirectory = Path.Combine(WMCL.Tests.TestEnvironment.Root, "presentation-" + Guid.NewGuid().ToString("N")), FontScale = scale };
        using var model = new MainWindowViewModel(new AppServices(settings, saveSettings: _ => { }));
        model.NavigateTo("settings");
        model.SelectedSubTab = model.SubTabs.Single(tab => tab.Key == "settings-ui");
        model.Settings.ShortcutSearch = "Ctrl+Shift+F";
        var window = new MainWindow { DataContext = model, Width = 850, Height = 500 };
        try
        {
            window.Show();
            await PumpAsync();
            var host = WindowPresentationManager.GetScaleHost(window);
            Assert.NotNull(host);
            Assert.Equal(scale, Assert.IsType<ScaleTransform>(host.LayoutTransform).ScaleX);
            var settingsView = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            // 快捷键已收进折叠组，先展开再定位输入框
            var shortcutSection = settingsView.GetVisualDescendants().OfType<Expander>()
                .Single(item => (item.Header as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "快捷键");
            shortcutSection.IsExpanded = true;
            await PumpAsync();
            var shortcut = Assert.Single(settingsView.GetVisualDescendants().OfType<TextBox>(), text => text.Text == "Ctrl+Shift+F");
            shortcut.BringIntoView();
            await PumpAsync();
            var logicalOrigin = shortcut.TranslatePoint(default, window)!.Value;
            var corner = shortcut.TranslatePoint(new Point(shortcut.Bounds.Width, shortcut.Bounds.Height), window)!.Value;
            Assert.Equal(shortcut.Bounds.Width * scale, corner.X - logicalOrigin.X, 4);
            Assert.Equal(shortcut.Bounds.Height * scale, corner.Y - logicalOrigin.Y, 4);
            Assert.True(shortcut.Bounds.Width >= 110);
            Assert.True(logicalOrigin.X >= 0);
            Assert.True(corner.X <= window.ClientSize.Width + 1);
            Assert.Equal(scale > 1.2, window.Classes.Contains("compactHeader"));
            Capture(window, $"{theme}-personalization-scale-{scale:0.0}");
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previous == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    [AvaloniaFact]
    public async Task DraggingTheUiScaleSliderAppliesLayoutOnReleaseWithoutMovingTheTrackUnderThePointer()
    {
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        model.NavigateTo("settings-ui");
        var window = new MainWindow { DataContext = model, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            await PumpAsync();
            var view = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            // UI 缩放已收进「外观微调」折叠组，先展开
            var appearance = view.GetVisualDescendants().OfType<Expander>()
                .Single(item => (item.Header as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault()?.Text == "外观微调");
            appearance.IsExpanded = true;
            await PumpAsync();
            var slider = view.FindControl<Slider>("UiScaleSlider")!;
            slider.BringIntoView();
            await PumpAsync();
            var thumb = Assert.Single(slider.GetVisualDescendants().OfType<Thumb>());
            var position = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            input.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [position, Avalonia.Input.RawInputModifiers.None]);
            input.GetMethod("MouseDown")!.Invoke(window.PlatformImpl, [position, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None]);
            var target = position + new Vector(40, 0);
            input.GetMethod("MouseMove")!.Invoke(window.PlatformImpl, [target, Avalonia.Input.RawInputModifiers.LeftMouseButton]);
            Assert.True(model.Settings.FontScale > 1);
            Assert.Equal(1, Assert.IsType<ScaleTransform>(WindowPresentationManager.GetScaleHost(window)!.LayoutTransform).ScaleX);
            input.GetMethod("MouseUp")!.Invoke(window.PlatformImpl, [target, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None]);
            await PumpAsync();
            Assert.Equal(model.Settings.FontScale, Assert.IsType<ScaleTransform>(WindowPresentationManager.GetScaleHost(window)!.LayoutTransform).ScaleX, 6);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ScaleAndApplicationOpacityApplyImmediatelyAndOwnedWindowsFollowWithoutDoubleScaling()
    {
        using var model = new MainWindowViewModel(new AppServices(new AppSettings(), saveSettings: _ => { }));
        model.NavigateTo("settings");
        var owner = new MainWindow { DataContext = model, Width = 850, Height = 500 };
        var child = new Window { Content = new TextBox { Width = 240, Text = "owned" }, Width = 400, Height = 250 };
        try
        {
            owner.Show();
            var dialog = child.ShowDialog(owner);
            await PumpAsync();
            model.Settings.FontScale = 1.5;
            model.Settings.ApplicationOpacity = 65;
            model.Settings.WindowBackdropBlur = true;
            await PumpAsync();
            Assert.Equal(1.5, Assert.IsType<ScaleTransform>(WindowPresentationManager.GetScaleHost(owner)!.LayoutTransform).ScaleX);
            Assert.Equal(1.5, Assert.IsType<ScaleTransform>(WindowPresentationManager.GetScaleHost(child)!.LayoutTransform).ScaleX);
            // Translucency is always applied (PCL-ME mapping); the compositor decides whether
            // it also blurs what is behind.
            const double expected = 0.9125;
            Assert.Equal(expected, WindowPresentationManager.GetOpacity(owner), 6);
            Assert.Equal(expected, WindowPresentationManager.GetOpacity(child), 6);
            Assert.Equal(expected, WindowPresentationManager.GetBackgroundOpacity(owner), 6);
            Assert.Equal(expected, WindowPresentationManager.GetBackgroundOpacity(child), 6);
            Assert.Contains(WindowTransparencyLevel.Blur, owner.TransparencyLevelHint);
            owner.FindControl<Border>("RootFrame")!.Opacity = 1;
            Assert.Equal(expected, WindowPresentationManager.GetOpacity(owner), 6);
            Assert.Equal(expected, WindowPresentationManager.GetBackgroundOpacity(owner), 6);
            model.Settings.FontScale = 1;
            await PumpAsync();
            Assert.Equal(1, Assert.IsType<ScaleTransform>(WindowPresentationManager.GetScaleHost(child)!.LayoutTransform).ScaleX);
            Assert.Single(child.GetVisualDescendants().OfType<LayoutTransformControl>());
            child.Close();
            await dialog;
        }
        finally { child.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0.8)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task ComboPopupScalesItsLayoutOnceAndRetainsSelection(double scale)
    {
        var combo = new ComboBox { Width = 240, ItemsSource = new[] { "自动", "Java 25", "Java 11" }, SelectedIndex = 0 };
        var window = new Window { Content = new StackPanel { Children = { combo } }, Width = 850, Height = 500 };
        try
        {
            WindowPresentationManager.Apply(window, new WindowPresentation(scale, 100, false));
            window.Show();
            await PumpAsync();
            combo.Focus();
            combo.IsDropDownOpen = true;
            await PumpAsync();
            var popup = combo.GetVisualDescendants().OfType<Popup>().SingleOrDefault()
                ?? combo.GetLogicalDescendants().OfType<Popup>().Single();
            var originalChild = popup.Child;
            Assert.True(popup.InheritsTransform);
            Assert.NotNull(popup.Host);
            Assert.Equal(scale, Assert.IsType<ScaleTransform>(popup.Host.Transform).ScaleX);
            Assert.True(Assert.IsAssignableFrom<Control>(popup.Host).Bounds.Width >= combo.Bounds.Width * scale - 2);
            var input = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.IHeadlessWindow")!;
            foreach (var key in new[] { Avalonia.Input.Key.Down, Avalonia.Input.Key.Enter })
            {
                input.GetMethod("KeyPress")!.Invoke(window.PlatformImpl, [key, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null]);
                input.GetMethod("KeyRelease")!.Invoke(window.PlatformImpl, [key, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null]);
            }
            await PumpAsync();
            Assert.Equal(1, combo.SelectedIndex);
            Assert.False(combo.IsDropDownOpen);
            combo.IsDropDownOpen = true;
            await PumpAsync();
            combo.SelectedIndex = 2;
            combo.IsDropDownOpen = false;
            combo.IsDropDownOpen = true;
            await PumpAsync();
            Assert.Equal(2, combo.SelectedIndex);
            Assert.Same(originalChild, popup.Child);
            Assert.Equal(scale, Assert.IsType<ScaleTransform>(popup.Host!.Transform).ScaleX);
            Capture(window, $"combo-popup-scale-{scale:0.0}");
        }
        finally { combo.IsDropDownOpen = false; window.Close(); }
    }

    [AvaloniaFact]
    public async Task EffectsAreSavedButNiriIsNeverWrittenWithoutConfirmation()
    {
        var beforeConfig = Environment.GetEnvironmentVariable("NIRI_CONFIG");
        var beforeDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var path = Path.Combine(WMCL.Tests.TestEnvironment.Root, "denied-niri-" + Guid.NewGuid().ToString("N") + ".kdl");
        Environment.SetEnvironmentVariable("NIRI_CONFIG", path);
        Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "niri");
        var confirmations = 0;
        var settings = new AppSettings();
        using var model = new SettingsViewModel(new AppServices(settings, saveSettings: _ => { }), (_, _) => { confirmations++; return Task.FromResult(false); });
        try
        {
            model.ApplicationOpacity = 75;
            model.FontScale = 1.3;
            model.WindowBackdropBlur = true;
            model.SaveCommand.Execute(null);
            Assert.Equal(75, settings.ApplicationOpacity);
            Assert.Equal(1.3, settings.FontScale);
            Assert.True(settings.WindowBackdropBlur);
            var roundTrip = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(settings));
            Assert.Equal(75, roundTrip.ApplicationOpacity);
            Assert.True(roundTrip.WindowBackdropBlur);
            await model.ApplyNiriEffectsCommand.ExecuteAsync(null);
            Assert.Equal(OperatingSystem.IsLinux() ? 1 : 0, confirmations);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NIRI_CONFIG", beforeConfig);
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", beforeDesktop);
        }
    }
    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
    private static async Task PumpAsync()
    {
        for (var frame = 0; frame < 5; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(20);
        }
    }
}
