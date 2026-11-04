using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WMCL.App.ViewModels;

public sealed record HelpTopic(string Category, string Title, string Summary, string Body, string Keywords);

public partial class HelpViewModel : ViewModelBase
{
    public HelpViewModel()
    {
        Topics =
        [
            new HelpTopic("安装", "安装原版游戏", "从版本列表下载并校验完整游戏文件。",
                "打开“下载 → 原版游戏”，左侧版本目录会从 Mojang 官方 manifest 加载所有正式版、快照与实验版。\n\n"
                + "勾选需要的版本后点击“安装所选版本”（或在启动页直接点“下载游戏”）。启动器会下载客户端 jar、libraries、assets 索引与对象、以及日志配置，并按 SHA1 校验；下载中断后重新安装会断点续传，已完好的文件不会重复下载。\n\n"
                + "安装前可在“下载源”里切换官方源或 BMCLAPI 镜像（国内网络建议镜像）。安装完成后版本会标记为“已安装”，启动页可直接选择。\n\n"
                + "安装不依赖 Java；但启动时需要匹配的 Java。游戏文件校验会在每次启动前自动执行，缺损文件会联网补全。",
                "下载 安装 原版 版本 校验 manifest 断点续传"),
            new HelpTopic("启动", "Java 与内存", "选择 Java、调整内存、避免内存不足崩溃。",
                "在“设置 → Java”扫描系统已安装的 Java，或手动填写 java 可执行文件路径；也可在线安装 Temurin 8/11/17/21/25（按当前进程架构精确下载，校验 SHA256 后才提交）。\n\n"
                + "内存建议：1.12.2 约 2 GB，1.16–1.20 约 4 GB，大型整合包 6–8 GB；内存不是越大越好，超过物理内存会频繁 GC 甚至崩溃。\n\n"
                + "其它：进程优先级（高/正常/低）、高性能显卡切换（DRI_PRIME / NVIDIA 环境变量）、额外 JVM 参数与游戏参数。实例设置可覆盖全局 Java 与内存。\n\n"
                + "自动启动时按版本兼容性选择 Java，不会因为“更高版本兼容”悄悄换成其它主版本；已有对应兼容运行时会直接复用。",
                "java 内存 崩溃 启动 参数 temurin 优先级 显卡"),
            new HelpTopic("账户", "正版与离线账户", "微软设备码登录，或使用离线玩家名。",
                "启动页顶部选择“正版”会打开微软设备码登录：启动器生成设备码并自动复制到剪贴板、打开微软官方验证页，每会话只自动执行一次；剪贴板/浏览器不可用时登录页会保留“复制设备码”“打开登录页”和可选代码。\n\n"
                + "正版登录需要 Azure 公共 Client ID（“设置 → 其他 → 微软 Client ID”），使用设备码 + 公共客户端，不保存 Client Secret；新应用默认不能调用 Minecraft 服务，需按 Minecraft 帮助中心申请白名单。\n\n"
                + "选择“离线”使用本地玩家名，不能进入正版服务器；首次启动正版/第三方账号时会提示支持正版 Minecraft。\n\n"
                + "启动后会用刷新令牌校验已保存的微软账号，失效时提示重新登录并显示重新登录按钮；网络问题不会常驻该按钮。",
                "登录 正版 离线 微软 账户 设备码 azure client id 白名单"),
            new HelpTopic("安装", "安装 Fabric / Forge / NeoForge", "在加载器页为原版版本安装 Mod 加载器。",
                "打开“下载 → 加载器”，选择游戏版本与加载器版本后安装。安装完成后会在 versions/ 下创建独立实例，mods 位于该实例目录。\n\n"
                + "Forge/NeoForge 需要联网运行官方安装器；Fabric/Quilt/LegacyFabric 由启动器直接安装。\n\n"
                + "支持组合安装：在下载页的组件卡里选择基础加载器，再勾选“同时安装 OptiFine”“同时安装 Fabric API”等，一次安装到同一实例，不会生成多个实例。\n\n"
                + "Forge 与 Fabric/NeoForge 互斥；Forge + OptiFine 需要匹配的 Forge 版本；Fabric + OptiFine 仅 1.20.4 及以下，需要 OptiFabric；1.14/1.15 的 OptiFine 需手动安装 OptiFabric Origins。",
                "fabric forge neoforge quilt legacyfabric 加载器 安装 组合"),
            new HelpTopic("资源", "安装 Mod 与整合包", "从 Modrinth 搜索，或导入本地整合包。",
                "“下载 → Mod”：搜索 Modrinth/CurseForge，按游戏版本与加载器过滤，查看详情、版本、依赖后安装到选中的目标实例。\n\n"
                + "“下载 → 整合包”：搜索 .mrpack 或选择本地文件，导入后创建新实例并下载其中全部文件；支持 Modrinth/CurseForge/HMCL/Mcbbs 格式。\n\n"
                + "CurseForge 搜索/下载需要在“设置 → 其他”填写 API Key（见 console.curseforge.com）；Modrinth 无需。\n\n"
                + "安装 Mod 前先选中目标实例；实例页 → Mod 管理可批量启用/禁用/删除、搜索（名称/文件名/ModId/版本详情）、查看图标；整合包更新会在启动前自动检查。",
                "mod 整合包 modrinth curseforge mrpack mcbbs mcbbs 依赖"),
            new HelpTopic("资源", "资源包、光影与存档", "管理实例资源，备份世界。",
                "“下载 → 资源包/光影包/存档”按类型管理；也可在实例页的对应分区管理。\n\n"
                + "资源包启用状态写入该实例的 options.txt；光影包放入 shaderpacks/；存档位于 saves/。\n\n"
                + "存档可备份为 zip，导入时解压到 saves/；玩家存档不做原地更新，只提供“安装为新存档”的路径。\n\n"
                + "资源包/光影支持按 SHA1 反查 Modrinth 来源并检查兼容更新（筛选相同 Minecraft 版本），未识别来源会明确标注“未识别”，不会猜测项目。解压目录形式的资源包/光影不支持自动检查更新。",
                "资源包 光影 存档 备份 导入 options.txt sha1 更新"),
            new HelpTopic("故障", "崩溃与日志", "查看启动日志与崩溃诊断。",
                "启动页右侧实时显示启动日志（限制高度，可滚动）；启动失败时会出现崩溃分析卡片，可打开 crash-report 与 latest.log、打开日志目录。\n\n"
                + "常见原因：Java 版本不匹配、内存不足/溢出、Mod 冲突或缺少依赖、文件损坏、权限不足、启动前命令失败、代理/TUN 问题。\n\n"
                + "首页“启动日志”卡片与“设置 → 日志”页都可一键导出诊断包，包含环境信息、脱敏设置、启动器日志、实例日志与崩溃报告。\n\n"
                + "把完整日志/崩溃报告附在反馈中能更快定位问题；导出时已存在的目标文件不会被覆盖，读取实例日志时同时覆盖隔离与非隔离两种目录布局。",
                "崩溃 日志 报错 诊断 内存 mod 诊断包 crash-report"),
            new HelpTopic("故障", "代理与下载源", "配置 HTTP 代理或 BMCLAPI 镜像。",
                "“设置 → 其他”填写 HTTP 代理地址与端口，重启启动器后生效；代理密码等敏感信息不会写入诊断包。\n\n"
                + "“设置 → 启动”可切换下载源：官方源或 BMCLAPI 镜像；BMCLAPI 适合国内网络，官方源适合海外网络。\n\n"
                + "下载线程数可在设置中调整（1–64），影响并发下载速度；断点续传按 SHA1 校验，无 SHA1 的旧 .part 会完整重下；切换镜像时也会重新校验。"
                + "下载提交前验证已知文件长度，206 范围与实际正文必须一致。",
                "代理 网络 下载 慢 bmclapi 镜像 线程 断点续传"),
            new HelpTopic("安装", "组合安装与加载器冲突", "一次安装 Minecraft + 加载器，按 PCL2 规则阻止冲突。",
                "先在下载目录选择 Minecraft，再展开组件卡选择加载器版本；使用“同时安装 OptiFine”组合安装，或点击“仅安装原版”。\n\n"
                + "支持的基础加载器：无 / Fabric / Quilt / LegacyFabric / Forge / NeoForge / OptiFine；可附加 Fabric API、OptiFabric、LiteLoader。\n\n"
                + "互斥规则：Forge 与 Fabric/NeoForge 互斥；Forge + OptiFine 需要匹配的 Forge 版本；Fabric + OptiFine 仅 1.20.4 及以下且需要 OptiFabric；1.14/1.15 的 OptiFine 需手动安装 OptiFabric Origins。\n\n"
                + "API 卡片可选择具体版本并独立清除；安装时会检查 API 声明的 Loader 要求，冲突时请更换 API 或 Loader。所有组件装入同一实例，mods 位于该实例目录。",
                "组合安装 冲突 fabric forge neoforge optifine optifabric quilt legacyfabric api"),
            new HelpTopic("实例", "版本隔离", "控制 Mod/存档放在版本目录还是 .minecraft 根目录。",
                "设置 → 启动 → 版本隔离：自动/隔离/不隔离；实例设置可覆盖为跟随全局/隔离/不隔离。\n\n"
                + "自动模式仅在可装 Mod 的版本隔离；隔离模式下 mods、config、saves 等放在 versions/<id>/，不隔离则放在 .minecraft 根目录。\n\n"
                + "修改隔离设置不会迁移已有文件，也不会改变整合包 profile；实例页的资源管理按实际隔离目录读取。",
                "隔离 版本隔离 mods 存档 目录 versions"),
            new HelpTopic("实例", "服务器列表", "编辑 servers.dat，添加/删除服务器。",
                "实例页 → 服务器：填写名称与 地址:端口 后添加，或删除选中项。\n\n"
                + "保存会重写 servers.dat（未压缩 NBT，兼容 gzip 读取），仅保留 name / ip / icon 三个字段，其它未知字段在保存时丢失。\n\n"
                + "地址支持 IP 或域名，端口默认 25565；图标字段可留空；保存前会校验地址非空。",
                "服务器 servers.dat 地址 端口 nbt"),
            new HelpTopic("账户", "第三方登录与皮肤", "Authlib/Yggdrasil 登录、多账号、实例认证服务器。",
                "启动页“第三方”：输入认证服务器（Authlib/Yggdrasil/Nide）、账号、密码；登录后账号进入下拉列表，支持多账号切换。\n\n"
                + "实例可指定认证服务器，启动时自动匹配对应账号并注入 authlib-injector；微软/第三方皮肤会尝试加载到头像。\n\n"
                + "微软账号支持上传皮肤（64×64 或 64×32 PNG，classic/slim）、刷新、披风选择与应用；像素编辑器可本地编辑后上传；模型可自动识别或手动选择。\n\n"
                + "Authlib 皮肤衣柜目前打开网页/独立窗口，披风按 PCL2 不做站内管理；第三方账号下拉只在“第三方”登录方式下显示。",
                "第三方 authlib yggdrasil 皮肤 authlib-injector nide 微软 披风"),
            new HelpTopic("联机", "EasyTier 手动联机（实验性）", "同一房间码 + 公共中继建立虚拟网络。",
                "联机页切换到 EasyTier：填写房间码与公共中继，启动 easytier-core。\n\n"
                + "这不是 PCL2 官方房间服务；公共中继/DNS/TUN 权限都可能影响连接。Linux 需要 /dev/net/tun 与 CAP_NET_ADMIN，缺权限时启动器会尝试 pkexec 提权。\n\n"
                + "30 秒内没有拿到虚拟 IP 会提示失败；失败可切换到陶瓦。陶瓦由 burningtnt 开发（AGPL-3.0），通过本地 HTTP API 调用未修改的二进制；可用 WMCL_TERRACOTTA_PATH 指定本地文件。",
                "联机 easytier 房间 中继 虚拟ip tun pkexec 陶瓦 terracotta"),
            new HelpTopic("启动", "启动前命令与优先级", "全局/实例命令、进程优先级、高性能显卡。",
                "设置 → 启动可配置全局启动前命令；实例设置可追加实例命令。支持占位符 {java} {minecraft} {verpath} {name}，命令输出会显示在日志。\n\n"
                + "进程优先级（高/正常/低）与高性能显卡（DRI_PRIME / NVIDIA 环境变量）也在此配置；额外 JVM 参数与游戏参数可分别填写。\n\n"
                + "启动前命令在游戏启动前执行，失败会阻止启动；CLI 默认不运行启动前命令。启动前命令、Java 路径、额外参数等字段不会通过外部配置导入启用。",
                "启动前命令 优先级 显卡 环境变量 jvm 参数"),
            new HelpTopic("实例", "导出与导入", "备份实例、导出整合包、控制导出内容。",
                "实例页 → 导出：勾选 Mod/配置/资源包/光影/存档/WMCL/options 等内容，导出为实例 zip 或 .mrpack/.mcbpack。\n\n"
                + "可选择按文件/文件夹导出；维护工具折叠；导出路径为空时默认写到实例目录 `<id>-export.mrpack`。\n\n"
                + "导入实例 zip 会创建新版本目录并迁移 profile 与安装清单；隔离设置会影响游戏目录判断；WMCL/PCL 备份标记会路由到 InstanceImporter。\n\n"
                + "ZIP/mrpack/mcbbs 导出会拒绝源目录及其祖先、符号链接/重解析点；已存在的输出文件不会被覆盖；未导出的共享依赖不保证离线完整。",
                "导出 导入 备份 zip mrpack mcbpack 选择内容"),
            new HelpTopic("账户", "自定义主页与主题", "HSL 主题、背景、Logo、音乐、本地 XAML 主页。",
                "设置 → 个性化：色彩预设/HSL、单图或背景文件夹、Logo、音乐、自定义 .axaml 主页。\n\n"
                + "UI 缩放 80%~150%（按 5% 调整，鼠标拖动松开后更新整个布局，键盘修改立即生效）；窗口透明度 0~100% 映射为不透明度下限~100%；桌面背景模糊向合成器请求。\n\n"
                + "功能隐藏：勾选写入 AppSettings.HiddenFeatures，立即生效并持久化；按住 F12 临时显示被隐藏项，松开恢复，不写回设置。\n\n"
                + "自定义主页是可执行 XAML，请只加载可信文件；保存后重启生效。窗口大小设置已移除，改为自由缩放并记忆尺寸；Windows 版主色相固定为蓝色。",
                "主题 背景 logo 音乐 主页 axaml 缩放 透明度 模糊 功能隐藏")
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
