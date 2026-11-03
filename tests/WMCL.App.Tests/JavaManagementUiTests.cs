using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WMCL.App.Services;
using WMCL.App.ViewModels;
using WMCL.App.Views;
using WMCL.Launch.Java;

namespace WMCL.App.Tests;

public sealed class JavaManagementUiTests
{
    [AvaloniaTheory]
    [InlineData("light", 850)]
    [InlineData("dark", 850)]
    [InlineData("light", 660)]
    [InlineData("dark", 660)]
    public void JavaRowsAndGroupedInstallControlsFitNarrowSettingsWithoutChangingAutoPreference(string theme, int width)
    {
        var previous = Application.Current!.RequestedThemeVariant;
        ThemeService.Apply(theme);
        var settings = new AppSettings { JavaPath = "" };
        var services = new AppServices(settings, saveSettings: _ => { });
        using var model = new SettingsViewModel(services);
        model.SetSection("settings-java");
        model.JavaRuntimes.Add(new JavaRuntime("/fixture/very-long-user-selected-java-path/bin/java", new Version(25, 0, 1),
            25, "aarch64", "/fixture/very-long-user-selected-java-path", "Fixture"));
        model.JavaRuntimes.Add(new JavaRuntime("/fixture/older/bin/java", new Version(11, 0, 27), 11, "x86_64", "/fixture/older", "Fixture"));
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 500 };
        try
        {
            window.Show();
            Pump();
            var runtimes = view.FindControl<ComboBox>("JavaRuntimeSelection")!;
            var versions = view.FindControl<ComboBox>("JavaInstallVersionSelection")!;
            Assert.Equal(new[] { "8", "11", "17", "21", "25" }, model.JavaInstallVersions);
            Assert.True(runtimes.Bounds.Width >= 200);
            Assert.True(versions.Bounds.Width >= 200);
            Assert.Null(runtimes.SelectedItem);
            Assert.Equal("", model.JavaPath);
            Assert.Equal("", settings.JavaPath);
            model.JavaInstallVersion = "11";
            Assert.Equal("11", versions.SelectedItem);
            model.JavaInstallVersion = "25";
            Assert.Equal("25", versions.SelectedItem);
            Assert.False(view.FindControl<Button>("CancelJavaInstallButton")!.IsEffectivelyVisible);
            Assert.Contains("本启动器", ToolTip.GetTip(view.FindControl<Button>("CleanDownloadedJavaButton")!)!.ToString());
            model.SelectedJavaRuntime = model.JavaRuntimes[0];
            Pump();
            Assert.Contains(runtimes.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Java 25.0.1");
            Assert.Contains(runtimes.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "aarch64");
            var pathText = Assert.Single(runtimes.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.JavaRuntimes[0].Home);
            Assert.Equal(TextTrimming.CharacterEllipsis, pathText.TextTrimming);
            Assert.Equal(model.JavaRuntimes[0].Path, ToolTip.GetTip(runtimes));
            foreach (var button in new[] { "InstallJavaButton", "CleanDownloadedJavaButton" }.Select(name => view.FindControl<Button>(name)!))
            {
                Assert.IsType<WrapPanel>(button.Parent);
                var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), window)!.Value.X;
                Assert.True(right <= window.ClientSize.Width);
            }
            model.IsInstallingJava = true;
            Pump();
            Assert.True(view.FindControl<Button>("CancelJavaInstallButton")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<Button>("InstallJavaButton")!.IsEnabled);
            Assert.False(view.FindControl<Button>("CleanDownloadedJavaButton")!.IsEnabled);
            Assert.False(versions.IsEnabled);
            var screenshotDirectory = Environment.GetEnvironmentVariable("WMCL_UI_AUDIT_SHOT_DIR");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(screenshotDirectory, $"{theme}-java-{width}.png"));
            }
            model.IsInstallingJava = false;
            model.UseAutomaticJavaCommand.Execute(null);
            Assert.Null(model.SelectedJavaRuntime);
            Assert.Equal("", model.JavaPath);
        }
        finally
        {
            window.Close();
            ThemeService.Apply(previous == ThemeVariant.Dark ? "dark" : "light");
            Application.Current.RequestedThemeVariant = previous;
        }
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
