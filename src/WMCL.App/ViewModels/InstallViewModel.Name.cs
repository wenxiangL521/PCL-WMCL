using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using WMCL.Minecraft;

namespace WMCL.App.ViewModels;

public partial class InstallViewModel
{
    private bool _isInstanceNameEdited;
    private bool _updatingInstanceName;

    public bool HasInstanceNameChange => IsExistingInstance && InstanceName != TargetInstanceId;
    public string OriginalInstanceNameText => "原名 :   " + TargetInstanceId;
    public Thickness InstallLogoMargin => IsExistingInstance ? new(15, 15, 0, 0) : new(52, 15, 0, 0);
    public Thickness InstallNameMargin => IsExistingInstance ? new(56, 16, HasInstanceNameChange ? 40 : 16, 0) : new(93, 16, 16, 0);

    [RelayCommand]
    private void RestoreInstanceName()
    {
        if (!CanEditInstall || TargetInstanceId is not { } original) return;
        _isInstanceNameEdited = false;
        SetInstanceName(original);
    }

    private void SetInstanceName(string name)
    {
        _updatingInstanceName = true;
        try { InstanceName = name; }
        finally { _updatingInstanceName = false; }
    }

    public Bitmap InstallLogo => VersionCatalogEntry.LoadIcon($"avares://WMCL.App/Assets/Blocks/{InstallLogoName}.png");

    private string InstallLogoName => SelectedBaseLoader switch
    {
        "Fabric" or "Quilt" or "LegacyFabric" => "Fabric",
        "Forge" => "Anvil",
        "NeoForge" => "NeoForge",
        _ when SelectedLiteLoader is not null => "Egg",
        "OptiFine" => "GrassPath",
        _ => Download.SelectedVersion is { } version ? VersionCatalogViewModel.Classify(version) switch
        {
            "正式版" => "Grass",
            "预览版" => "CommandBlock",
            "愚人节版" => "GoldBlock",
            _ => "CobbleStone"
        } : "Grass"
    };

    public bool HasInstanceNameError => InstanceNameError.Length > 0;

    public string InstanceNameError
    {
        get
        {
            if (Download.SelectedVersion is null) return "";
            if (IsExistingInstance && InstanceName != InstanceName.Trim()) return "实例名不能以空格开头或结尾";
            try { InstanceManager.ValidateName(InstanceName.Trim()); }
            catch (ArgumentException exception) { return exception.Message.Split('\n')[0]; }
            var target = Path.Combine(_services.Settings.GameDirectory, "versions", InstanceName.Trim());
            var samePath = IsExistingInstance && string.Equals(InstanceName.Trim(), TargetInstanceId,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (!samePath && Path.Exists(target)) return $"实例已存在：{InstanceName.Trim()}";
            return !samePath && Path.Exists(Path.Combine(_services.Settings.GameDirectory,
                "libraries", "wmcl-packs", InstanceName.Trim())) ? $"实例专属库目录已存在：{InstanceName.Trim()}" : "";
        }
    }

    partial void OnInstanceNameChanged(string value)
    {
        if (!_updatingInstanceName) _isInstanceNameEdited = true;
        NotifyInstallAction();
    }

    private void UpdateInstanceName()
    {
        OnPropertyChanged(nameof(InstallLogo));
        if (_isInstanceNameEdited) return;
        var name = Download.SelectedVersion?.Id ?? "";
        if (IsExistingInstance) name = TargetInstanceId!;
        else if (name.Length > 0)
        {
            if (IsFabricBase && Mods.SelectedLoader is { } fabric)
                name += $"-{SelectedBaseLoader} {fabric.Split('+')[0]}";
            if (IsForgelikeBase && Mods.SelectedForgelikeVersion is { } forge)
                name += $"-{SelectedBaseLoader}_{(SelectedBaseLoader == "NeoForge" ? forge.Split('+')[0] : forge)}";
            if (SelectedLiteLoader is not null) name += "-LiteLoader";
            if ((IsOptiFineBase || InstallOptiFineWithBase) && Mods.SelectedOptiFineVersion is { } optifine)
                name += "-OptiFine_" + optifine.DisplayName.Replace(optifine.Minecraft + " ", "", StringComparison.Ordinal).Trim().Replace(' ', '_');
        }
        SetInstanceName(name);
    }
}
