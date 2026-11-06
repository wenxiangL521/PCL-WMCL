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
    private async Task StartCustomDownloadAsync()
    {
        if (!CanStartCustomDownload) return;

        if (!Uri.TryCreate(CustomDownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            CustomDownloadStatus = "请输入有效的 http/https URL";
            return;
        }

        var fileName = string.IsNullOrWhiteSpace(CustomDownloadFileName)
            ? Path.GetFileName(uri.LocalPath)
            : CustomDownloadFileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "download.bin";

        var folder = string.IsNullOrWhiteSpace(CustomDownloadFolder)
            ? Path.Combine(WmclPaths.DataDirectory, "downloads")
            : WmclPaths.Expand(CustomDownloadFolder);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, Path.GetFileName(fileName));

        IsCustomDownloading = true;
        CustomDownloadProgress = 0;
        CustomDownloadStatus = "开始下载…";
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        try
        {
            using var response = await Http.GetAsync(
                uri, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(_downloadCts.Token);
            await using var output = File.Create(destination);
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, _downloadCts.Token)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), _downloadCts.Token);
                read += count;
                if (total is > 0)
                    CustomDownloadProgress = read * 100d / total.Value;
                CustomDownloadStatus = total is > 0
                    ? $"{read / 1024d / 1024d:0.0} / {total.Value / 1024d / 1024d:0.0} MB"
                    : $"{read / 1024d / 1024d:0.0} MB";
            }
            CustomDownloadProgress = 100;
            CustomDownloadStatus = "下载完成：" + destination;
            Status = "自定义下载完成：" + fileName;
        }
        catch (OperationCanceledException)
        {
            CustomDownloadStatus = "已取消下载";
        }
        catch (Exception ex)
        {
            CustomDownloadStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsCustomDownloading = false;
            _downloadCts.Dispose();
            _downloadCts = null;
        }
    }

    [RelayCommand]
    private void CancelCustomDownload()
    {
        _downloadCts?.Cancel();
        CustomDownloadStatus = "正在取消…";
    }
}
