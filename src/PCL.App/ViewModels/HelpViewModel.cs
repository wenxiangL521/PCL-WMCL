using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

public sealed record HelpTopic(string Title, string Summary, string Body, string Keywords);

public partial class HelpViewModel : ViewModelBase
{
    public HelpViewModel()
    {
        Topics =
        [
            new HelpTopic("安装原版游戏", "从版本列表下载并校验完整游戏文件。",
                "打开“下载 → 原版游戏”，选择版本后点击“安装所选版本”。\n\n"
                + "启动器会下载客户端 jar、libraries、assets 与日志配置，并校验 SHA1。"
                + "下载中断后重新安装会断点续传。",
                "下载 安装 原版 版本 校验"),
            new HelpTopic("Java 与内存", "选择 Java、调整内存、避免内存不足崩溃。",
                "在“设置 → 启动”中扫描 Java，或手动填写 Java 路径。\n\n"
                + "内存建议：1.12.2 约 2 GB，1.20+ 约 4 GB，大型整合包 6–8 GB。"
                + "内存不是越大越好，超过物理内存会频繁 GC。",
                "java 内存 崩溃 启动 参数"),
            new HelpTopic("正版与离线账户", "微软设备码登录，或使用离线玩家名。",
                "启动页顶部选择“正版”会打开微软设备码登录；选择“离线”使用本地玩家名。\n\n"
                + "微软登录需要 Azure 公共 Client ID，可在“设置 → 其他”填写。"
                + "离线账户不能进入正版服务器。",
                "登录 正版 离线 微软 账户"),
            new HelpTopic("安装 Fabric / Forge / NeoForge", "在加载器页为原版版本安装 Mod 加载器。",
                "打开“下载 → 加载器”，选择游戏版本与加载器版本后安装。\n\n"
                + "安装完成后会在 versions/ 下创建独立实例，mods 位于该实例目录。"
                + "Forge/NeoForge 需要联网运行官方安装器。",
                "fabric forge neoforge 加载器 安装"),
            new HelpTopic("安装 Mod 与整合包", "从 Modrinth 搜索，或导入本地整合包。",
                "“下载 → Mod”搜索并安装单个 Mod；“下载 → 整合包”搜索 `.mrpack` 或选择本地文件。\n\n"
                + "安装 Mod 前先选中目标实例；CurseForge 搜索需要在设置中填写 API Key。",
                "mod 整合包 modrinth curseforge mrpack"),
            new HelpTopic("资源包、光影与存档", "管理实例资源，备份世界。",
                "“下载 → 资源包/光影包/存档”按类型管理。\n\n"
                + "资源包启用状态写入 options.txt；存档可备份为 zip，导入时解压到 saves/。",
                "资源包 光影 存档 备份 导入"),
            new HelpTopic("崩溃与日志", "查看启动日志与崩溃诊断。",
                "启动页右侧实时显示日志；启动失败时会出现崩溃分析卡片。\n\n"
                + "常见原因：Java 版本不匹配、内存不足、Mod 冲突、缺少依赖、权限不足。"
                + "把完整日志附在反馈中能更快定位问题。",
                "崩溃 日志 报错 诊断 内存 mod"),
            new HelpTopic("代理与下载源", "配置 HTTP 代理或 BMCLAPI 镜像。",
                "“设置 → 其他”填写 HTTP 代理，重启启动器生效。\n\n"
                + "“设置 → 启动”可切换下载源；BMCLAPI 适合国内网络，官方源适合海外网络。",
                "代理 网络 下载 慢 bmclapi 镜像")
        ];
        FilteredTopics = new ObservableCollection<HelpTopic>(Topics);
        SelectedTopic = Topics[0];
    }

    public ObservableCollection<HelpTopic> Topics { get; }
    public ObservableCollection<HelpTopic> FilteredTopics { get; }

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    public bool HasSelectedTopic => SelectedTopic is not null;
    public string Status => $"{FilteredTopics.Count} 个帮助主题";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedTopicChanged(HelpTopic? value) =>
        OnPropertyChanged(nameof(HasSelectedTopic));

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredTopics.Clear();
        foreach (var topic in Topics)
        {
            if (query.Length == 0 ||
                topic.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                topic.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                topic.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
                FilteredTopics.Add(topic);
        }

        if (SelectedTopic is not null && !FilteredTopics.Contains(SelectedTopic))
            SelectedTopic = FilteredTopics.FirstOrDefault();
        OnPropertyChanged(nameof(Status));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";
}
