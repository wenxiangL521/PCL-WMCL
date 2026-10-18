# PCL / PCL-CE / PCL-ME UI 对照

## 参考来源

- PCL2 源码：`Hex-Dragon/PCL2`（本仓库研究副本 `pcl2-research/pcl2`）
- PCL 社区版：<https://github.com/PCL-Community/PCL-CE>（dev 分支，已拉取；页面已本地化，新增 PageHomepage/PageTools 等）
- PCL-ME（跨平台 Avalonia）：<https://github.com/TheUnknownThing/PCL-ME>

## 已确定的 PCL2 结构常量

| 项 | 值 |
|---|---|
| 窗口 | 850×500，`ResizeMode=CanMinimize`（不可缩放） |
| 标题栏 | 48px；窗口按钮 28×28；关闭右边距 12，最小化 44 |
| 顶部导航 | 高 27px，圆角 13.5；选中白底、蓝字/蓝图标 |
| 启动页左栏 | 300px |
| 右侧内容边距 | 25px（启动页 25,25,25,10） |
| 卡片 | 圆角 6；参考 PCL-ME `PclCard` 加 1px 边线/轻阴影 |
| 页面背景 | 蓝色渐变（PCL-ME：`#96C0F9 → #E0EAFD → #D5E6FD`） |

## 当前实现

- 主窗口已固定 850×500；
- 标题栏只保留 PCL Logo；
- 顶部导航白色选中胶囊；
- 启动页 300px 左栏 + 25px 右侧日志；
- 版本选择改为独立页面（仅已安装版本）；
- 按 PCL-CE 的 `PageStackData/PageBack` 加了页面栈：版本选择/实例设置进入子页，标题栏显示返回箭头和页面名；
- 卡片统一圆角 6 / 边线 / 轻阴影；
- 页面背景改用蓝色渐变；
- 去掉了根窗口缩放、按钮 hover 缩放、页面横移等自定义效果。

## 仍待对齐

- PCL-ME 的 `PclCard` 标题栏/内容边距还未完全复刻；
- PCL-ME 启动页的账号资料卡、启动按钮样式与本项目仍有差异；
- PCL-CE 的 PageHomepage 新闻主页/PageTools 尚未实现；
- 实例页已去掉页面内重复的 300px 实例列表，改为全宽卡片，顶部用实例下拉切换；
- “更多”移除自定义“实例管理”项；实例设置按 PCL2 从版本选择进入，并作为子页入栈；
- 整合包页改为左 300 / 右自适应，并补空状态；
- 下载页加载器卡片默认收起（PCL2 卡片式），底部按钮改回 35px 普通按钮；
- 组合安装改为“基础加载器 + 可选 OptiFine”；
- 下载页版本时间改为本地日期，不再显示原始 ISO 字符串。
