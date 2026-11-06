using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WMCL.App.Services;
using WMCL.Core;
using WMCL.Minecraft;

namespace WMCL.App.Features.More;

public partial class ToolboxViewModel
{

    [RelayCommand]
    private async Task PreviewSkinAsync()
    {
        if (string.IsNullOrWhiteSpace(SkinUrl)) return;
        try
        {
            using var response = await Http.GetAsync(SkinUrl);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            SkinPreview = new Bitmap(stream);
            Status = "皮肤预览已加载";
        }
        catch (Exception ex)
        {
            Status = "加载皮肤失败：" + ex.Message;
        }
    }

    // ── 抽奖 ──
}
