using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PCL.App.ViewModels;

public sealed record HelpTopic(string Category, string Title, string Summary, string Body, string Keywords);

public partial class HelpViewModel : ViewModelBase
{
    public HelpViewModel()
    {
        Topics =
        [
            new HelpTopic("安装", "安装原版游戏", "从版本列表下载并校验完整游戏文件。",
                "打开“下载 → 原版游戏”，选择版本后点击“安装所选版本”。\n\n"
                + "启动器会下载客户端 jar、libraries、assets 与日志配置，并校验 SHA1。"
                + "下载中断后重新安装会断点续传。",
                "下载 安装 原版 版本 校验"),
            new HelpTopic("启动", "Java 与内存", "选择 Java、调整内存、避免内存不足崩溃。",
                "在“设置 → 启动”中扫描 Java，或手动填写 Java 路径。\n\n"
                + "内存建议：1.12.2 约 2 GB，1.20+ 约 4 GB，大型整合包 6–8 GB。"
                + "内存不是越大越好，超过物理内存会频繁 GC。",
                "java 内存 崩溃 启动 参数"),
            new HelpTopic("账户", "正版与离线账户", "微软设备码登录，或使用离线玩家名。",
                "启动页顶部选择“正版”会打开微软设备码登录；选择“离线”使用本地玩家名。\n\n"
                + "微软登录需要 Azure 公共 Client ID，可在“设置 → 其他”填写。"
                + "离线账户不能进入正版服务器。",
                "登录 正版 离线 微软 账户"),
            new HelpTopic("安装", "安装 Fabric / Forge / NeoForge", "在加载器页为原版版本安装 Mod 加载器。",
                "打开“下载 → 加载器”，选择游戏版本与加载器版本后安装。\n\n"
                + "安装完成后会在 versions/ 下创建独立实例，mods 位于该实例目录。"
                + "Forge/NeoForge 需要联网运行官方安装器。",
                "fabric forge neoforge 加载器 安装"),
            new HelpTopic("资源", "安装 Mod 与整合包", "从 Modrinth 搜索，或导入本地整合包。",
                "“下载 → Mod”搜索并安装单个 Mod；“下载 → 整合包”搜索 `.mrpack` 或选择本地文件。\n\n"
                + "安装 Mod 前先选中目标实例；CurseForge 搜索需要在设置中填写 API Key。",
                "mod 整合包 modrinth curseforge mrpack"),
            new HelpTopic("资源", "资源包、光影与存档", "管理实例资源，备份世界。",
                "“下载 → 资源包/光影包/存档”按类型管理。\n\n"
                + "资源包启用状态写入 options.txt；存档可备份为 zip，导入时解压到 saves/。",
                "资源包 光影 存档 备份 导入"),
            new HelpTopic("故障", "崩溃与日志", "查看启动日志与崩溃诊断。",
                "启动页右侧实时显示日志；启动失败时会出现崩溃分析卡片。\n\n"
                + "常见原因：Java 版本不匹配、内存不足、Mod 冲突、缺少依赖、权限不足。"
                + "把完整日志附在反馈中能更快定位问题。",
                "崩溃 日志 报错 诊断 内存 mod"),
            new HelpTopic("故障", "代理与下载源", "配置 HTTP 代理或 BMCLAPI 镜像。",
                "“设置 → 其他”填写 HTTP 代理，重启启动器生效。\n\n"
                + "“设置 → 启动”可切换下载源；BMCLAPI 适合国内网络，官方源适合海外网络。",
                "代理 网络 下载 慢 bmclapi 镜像"),
            new HelpTopic("安装", "组合安装与加载器冲突", "一次安装 Minecraft + 加载器，按 PCL2 规则阻止冲突。",
                "安装页底栏选择“无 / Fabric / Forge / NeoForge / OptiFine / Fabric+OptiFine / Forge+OptiFine”。\n\n"
                + "Forge 与 Fabric/NeoForge 互斥；Forge+OptiFine 需要精确 Forge 版本；Fabric+OptiFine 仅 1.20.4 及以下，并自动安装 OptiFabric/Fabric API。",
                "组合安装 冲突 fabric forge neoforge optifine optifabric"),
            new HelpTopic("实例", "版本隔离", "控制 Mod/存档放在版本目录还是 .minecraft 根目录。",
                "设置 → 启动 → 版本隔离：自动/隔离/不隔离；实例设置可覆盖为跟随全局/隔离/不隔离。\n\n"
                + "自动模式仅在可装 Mod 的版本隔离。修改隔离不会迁移已有文件。",
                "隔离 版本隔离 mods 存档 目录"),
            new HelpTopic("实例", "服务器列表", "编辑 servers.dat，添加/删除服务器。",
                "实例页 → 服务器列表：填写名称与地址:端口后添加，或删除选中项。\n\n"
                + "保存会重写 servers.dat，仅保留 name/ip/icon 字段。",
                "服务器 servers.dat 地址 端口"),
            new HelpTopic("账户", "第三方登录与皮肤", "Authlib/Yggdrasil 登录、多账号、实例认证服务器。",
                "启动页“第三方”输入认证服务器、账号、密码；登录后账号进入下拉列表。\n\n"
                + "实例可指定认证服务器，启动时自动匹配对应账号并注入 authlib-injector；微软/第三方皮肤会尝试加载到头像。",
                "第三方 authlib yggdrasil 皮肤 authlib-injector nide"),
            new HelpTopic("联机", "EasyTier 手动联机（实验性）", "同一房间码 + 公共中继建立虚拟网络。",
                "联机页填写房间码与公共中继，启动 EasyTier core。\n\n"
                + "注意：这不是 PCL2 官方房间服务；公共中继/DNS/TUN 权限都可能影响连接。30 秒没有虚拟 IP 会提示失败。",
                "联机 easytier 房间 中继 虚拟ip"),
            new HelpTopic("启动", "启动前命令与优先级", "全局/实例命令、进程优先级、高性能显卡。",
                "设置 → 启动可配置全局启动前命令；实例设置可追加实例命令。支持 {java}/{minecraft}/{verpath}/{name}。\n\n"
                + "进程优先级与 DRI_PRIME/NVIDIA 环境变量也在此配置。",
                "启动前命令 优先级 显卡 环境变量"),
            new HelpTopic("实例", "导出与导入", "备份实例、导出整合包、控制导出内容。",
                "实例页 → 导出选项勾选 Mod/配置/资源包/光影/存档/PCL/options；导出实例为 zip，或导出 .mrpack。\n\n"
                + "导入实例 zip 会创建新版本目录；隔离设置会影响游戏目录判断。",
                "导出 导入 备份 zip mrpack"),
            new HelpTopic("账户", "自定义主页与主题", "HSL 主题、背景、Logo、音乐、本地 XAML 主页。",
                "设置 → 个性化：色彩预设/HSL、单图或背景文件夹、Logo、音乐、自定义 .axaml 主页。\n\n"
                + "自定义主页是可执行 XAML，请只加载可信文件；保存后重启生效。",
                "主题 背景 logo 音乐 主页 axaml")
        ];
        FilteredTopics = new ObservableCollection<HelpTopic>(Topics);
        SelectedTopic = Topics[0];
    }

    public ObservableCollection<HelpTopic> Topics { get; }
    public ObservableCollection<HelpTopic> FilteredTopics { get; }
    public ObservableCollection<string> Categories { get; } =
        ["全部", "安装", "启动", "账户", "资源", "实例", "联机", "故障"];

    [ObservableProperty]
    private string _selectedCategory = "全部";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private HelpTopic? _selectedTopic;

    public bool HasSelectedTopic => SelectedTopic is not null;
    public string Status => $"{FilteredTopics.Count} 个帮助主题";

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    partial void OnSelectedTopicChanged(HelpTopic? value) =>
        OnPropertyChanged(nameof(HasSelectedTopic));

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        FilteredTopics.Clear();
        foreach (var topic in Topics)
        {
            if (SelectedCategory != "全部" && topic.Category != SelectedCategory) continue;
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
