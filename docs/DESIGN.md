# 萌新工具箱（mxx1 Toolbox）· 设计 · v1.3（已实现）

> 状态：**已实现并全绿**（2026-10-04，版本 1.2.0）：53 个按钮里 51 个是真功能，其余 2 个灰色占位（禁用）。
> 本文件是外观与行为的**唯一正本**，改设计先改这里，再同步 skill。
> 界面截图：`docs/gui-shot.png`（浅色「常用设置」页：彩色 = 真功能，灰色 = 还没接）、
> `docs/dark-shot.png`（深色）、`docs/system-shot.png`（系统工具页签）；
> 外观参考图：`C:\Users\Administrator\Pictures\Snipaste_2026-10-04_10-29-34.png`（Windows系统工具箱 v1.1）。

## 1 定位

免安装的单文件 Windows 程序（C# WinForms + 系统自带 csc 编译，与 `permanent-delete-menu` 同技术栈）。
主界面是**多行多列的按钮墙**：点一下按钮 = 启动一个已经做好的程序 / 脚本 / 功能。
加按钮只是往 `tools\*.json` 丢配置，**不需要重新编译主程序**。

- 程序名 **萌新工具箱**；标题栏 `萌新工具箱 v1.2.0`
- 署名 `mxx1` / `mxx1.cn`；许可证 **GPL-3.0-or-later**；仓库 `mxx1-toolbox`
- 独立工程 `D:\萌新工具开发\toolbox\`，与 `permanent-delete-menu` **互不修改**（只调它的 exe）

## 2 已锁定的决策

| # | 项 | 决定 |
| --- | --- | --- |
| 1 | 工程形态 | 独立新程序 `toolbox\`，独立 Git 仓库（本地 + GitHub 远程；**推送前先问用户**） |
| 2 | 右键功能集成 | 调现成 `PermanentDeleteSetup.exe`，零改动、不重写删除逻辑 |
| 3 | 图标 | 每个按钮一个 16×16 PNG（`tools\Make-Icons.ps1` 生成，按页签配色 + 按名字选图形）编译时内嵌为 `icons.<id>.png`；显式 `icon` 字段 > `assets\icons\<id>.png` > 内嵌 > 实时画的占位图标。不管来源，一律经 `IconFactory.Normalize()` 归一化到 **16×15 画布**（原因见 §12 第 9 条） |
| 4 | 主题 | 默认**浅色**，可切**深色**，可**跟随系统**（`WM_SETTINGCHANGE` / `UserPreferenceChanged` 自动切） |
| 5 | 外观 | **A 型 · 紧凑小按钮**：四列纯文字按钮 + 左侧 16×16 图标（渲染画布 16×15，原因见 §12 第 9 条） |
| 6 | 按钮功能 | 真功能：常用设置 13、右键增强 1、清理优化 8、系统工具 12、我的工具 1（图形化新建）= **38 个**；其余 15 个是 `placeholder` |
| 7 | 占位按钮表现 | **默认就是灰的，而且禁止点击**（用户 2026-10-04 的要求）：`Enabled=false` + 灰底 + 浅边框 + 灰文字 + **置灰图标**（`IconFactory.GetMuted()`）。禁用控件不弹悬停提示，所以状态栏在有灰按钮的页面上带一句「灰色 N 个没接功能」。命令行（`run <id>`）仍然只解释、绝不执行 |
| 15 | 外部工具目录 | 工具箱 exe 旁边的 **`bin-tools\`**：外部 exe 丢进去就能被找到（`AppPaths.PayloadDir`），打包时 `build.ps1 -Package` 会把隔壁的安装器拷进去；设置窗口和关于窗口都有「打开工具目录」。`kind: exe` 的**相对路径按工具箱目录 / bin-tools 解析**（不再按进程当前目录） |
| 16 | 用户层 | `%LOCALAPPDATA%\mxx1-toolbox\tools.json`：图形化的新建 / 编辑 / 删除、拖拽加按钮都写这里；写入前备份 `.bak`。改完立刻重载清单 + 重算列宽（只加宽不缩窄） |
| 8 | 「推荐」红框 | 不做（那是别人截图时画的） |
| 9 | 页签 | `常用设置` / `右键增强` / `清理优化` / `系统工具` / `我的工具` |
| 10 | 底部条 | **高度按字体算**（当前 28px = 按钮 24px + 上下各 2px；24 = 文字行 16 + 边框与内边距 8，见 §12 第 8 条）：`N 个按钮 · 本页 M · 状态文字` + `[搜索] [日志] [设置] [检查更新]`。状态文字**只放短摘要**（完整内容进日志 + 悬停提示），因为标签宽度固定，长句会被截 |
| 11 | 高 DPI | **System DPI aware（`dpiAware=true`）+ `AutoScaleMode.Font`**。不用 PerMonitorV2：它要靠 app.config 开关，而 exe.config 会破坏"单文件"。列宽在运行时按最长按钮名测量，所以换 DPI / 换字号都不会截断文字 |
| 12 | 按钮来源 | 全部由 `tools\*.json` 驱动（内嵌 5 个 + 用户层覆盖） |
| 13 | 右键增强按钮数 | **只留 1 个**（用户 2026-10-04 拍板）：名字 **「永久删除工具」**，不带参数启动 `PermanentDeleteSetup.exe`（= 开它自己的窗口）。安装/卸载/状态/测试/条款/日志/更新都在那个窗口里，工具箱不重复实现。原来 8 条定义留在 `tools\rightmenu.json` 的 `_disabled` 里备用。名字为什么不用「永久删除（不进回收站）」：那个名字 128px 会把列宽顶到 170、窗口 640→704，而「永久删除工具」73px，列宽仍由「关闭实时防护与篡改」（106px）决定 |
| 14 | 系统工具怎么点 | 只读查看 Windows 自带组件：`.msc`/`.cpl`/`.exe` 走 ShellExecute，`shell:` 目录走 `explorer.exe`，`ms-settings:` 页面直接交给系统。**不改系统、不需要管理员**；组件不存在就出一句说明（家庭版没有 `gpedit.msc`），不静默失灵 |

## 3 界面规格（实现值）

```
┌─ [■] 萌新工具箱 v1.1.0 ────────────────────────────────────── [—] [×] ─┐
│  [常用设置]  右键增强  清理优化  系统工具  我的工具                    │
│ ─────────────────────────────────────────────────────────────────────  │
│  [i] 任务栏从不合并   [i] 开始菜单居左   [i] Win10 资源管理器  [i] Win10 右键菜单 │
│  [i] 任务栏始终合并   [i] 开始菜单居中   [i] Win11 资源管理器  [i] Win11 右键菜单 │
│  ...（这一页灰按钮和真按钮混着：灰的禁用、彩色的能点）                 │
│  ────────────────────────────────────────────────────────────────────  │
│  [i] 关闭实时防护与篡改 [i] Windows 激活 [i] Defender 开关设置 [i] Windows 更新开关│
│  ...                                                                   │
│  53 个按钮 · 本页 31 · 灰色 2 个没接功能 · 就绪  [搜索][日志][设置][关于][检查更新] │
└────────────────────────────────────────────────────────────────────────┘
```

| 元素 | 实现 |
| --- | --- |
| 窗口 | 高度默认 700；**宽度 = 4 × 列宽 + 24**（`ComputeCellWidth()` 当前算出 150 → 窗口 640，客户区 624）；最小 460×520；可缩放；`MinimizeBox=false`、`MaximizeBox=false` |
| 页签行 | 高 30px，5 个 `Flat` 按钮等宽（**不用 `TabControl`**，深色下不可控）；选中页签白底/深底 + 蓝边框 |
| 列宽 | `ComputeCellWidth()`：用 8.25pt 微软雅黑量出**最长按钮名**（当前是「关闭实时防护与篡改」= 106px），加 36px（图标 16 + 图文间距 + 内边距 + 余量）再加 8px 间距，钳在 104…170。网格是 `AutoSize` 的，实际渲染出来列间距 150、按钮 144×30 |
| 按钮 | **实测 144×30**（列间距 150 − 两边各 3px 外边距），字号 **8.25pt**，左 16×15 图标，`AutoEllipsis=true` 兜底 |
| 灰色占位按钮 | `placeholder: true` 的按钮：**`Enabled=false`（禁止点击）** + 底 `#EAEAEA`、边框 `#C8C8C8`、文字 `#8A8A8A`（深色 `#262629` / `#35353A` / `#7C7C82`），图标走 `IconFactory.GetMuted()`（灰度 + 往白里混 45%）。禁用控件不弹悬停提示，所以状态栏带一句「灰色 N 个没接功能」 |
| 悬停提示 | 真按钮：`名字 · 会执行的命令`；危险按钮：`名字 · 会改动系统，点击先弹确认框`；灰按钮禁用，提示改由状态栏给 |
| 网格 | `TableLayoutPanel`，**4 个绝对列（列宽 = 上面的列宽）+ 1 个 100% 的空列**（吃掉多余宽度），行高 36 |
| 段分隔线 | `Panel` 高 1px，`ColumnSpan=4`，`Margin=(3,6,3,6)`，占一行 13px |
| 内容区 | 每个页签一个 `Panel(AutoScroll=true)`，里面套 `TableLayoutPanel(Dock=Top, AutoSize)` |
| 搜索行 | 默认隐藏（行高 0），点 `[搜索]` 展开 28px，输入即筛当前页（匹配名字 / id / hint / 页签名） |
| 日志面板 | 默认收起（行高 0），展开 170px；字号 Consolas 8.25pt；`HH:mm:ss  按钮名  内容`，**最新在最上面**；空白时的提示写着"灰色的按钮表示功能还没接入" |
| 底部条 | 高 `_statusBarHeight`（= 按钮高 + 4，当前 28px）；左 `Label`（AutoEllipsis），右 4 个 `AutoSize=false` 的按钮：**高度按字体算**（`MeasureText("国").Height + 8` = 24px，`+8` 的来历见 §12 第 8 条），宽度按文字量 |
| 窗口布局 | 用一个 5 行 `TableLayoutPanel`（页签 / 搜索 / 网格 / 日志 / 底栏）—— 完全不手写坐标 |

### 配色

| 用途 | 浅色 | 深色 |
| --- | --- | --- |
| 窗体背景 | `#F0F0F0` | `#202020` |
| 按钮底 / 边框 | `#F5F5F5` / `#ADADAD` | `#2D2D30` / `#3F3F46` |
| 文字 | `#1A1A1A` | `#F0F0F0` |
| 悬停 / 按下 | `#E5F1FB` / `#CCE4F7` | `#094771` / `#0E5A94` |
| 危险按钮文字 | `#B00020` | `#FF6B6B` |
| **灰按钮底 / 边框 / 文字** | `#EAEAEA` / `#C8C8C8` / `#8A8A8A` | `#262629` / `#35353A` / `#7C7C82` |
| 底栏 / 日志底 | `#E8E8E8` / `#FFFFFF` | `#1B1B1B` / `#181818` |

墨水判据（界面回归用）：真按钮文字最暗 **26**、禁用的灰按钮最暗 **77**（WinForms 给禁用控件画字时会描 1px 更深的影子，所以不再是纯灰的 138）—— 判据是"真 ≤ 80、灰 ≥ 60、两者至少差 30"。

深色不需要自绘控件：`FlatStyle=Flat` + `FlatAppearance` 逐主题赋值即可；
标题栏用 `DwmSetWindowAttribute(hwnd, 20→19, dark)`，滚动条用 `SetWindowTheme(hwnd, "DarkMode_Explorer")`。

## 4 页签与按钮清单（共 53 个：51 真功能 + 2 灰色占位）

`[i]` = 16×16 图标；**粗体** = 危险按钮（深红文字 + 二次确认）；灰色 = `placeholder`（禁用，点不动）。

### 4.1 常用设置（31 个，两段：29 个真功能 + 2 个灰色占位）

**真功能 29 个**（点得动；`confirm` = 点之前先弹二次确认）：

| 按钮 | 怎么实现的 |
| --- | --- |
| 桌面图标设置 | `rundll32 shell32.dll,Control_RunDLL desk.cpl,,0` |
| 关闭 / 开启驱动自动安装 | `DriverSearching\SearchOrderConfig` = 0 / 1（管理员 + 确认） |
| 激活状态 | **只读**：查 `SoftwareLicensingProduct` 把授权状态列出来 |
| 禁用/启用休眠 | 读 `HibernateEnabled` 再 `powercfg /h on\|off`（管理员 + 确认） |
| 电源卓越 / 高性能 / 平衡模式 | `powercfg` 切方案（卓越模式先 `-duplicatescheme`，管理员 + 确认） |
| 重启资源管理器 | `taskkill /f /im explorer.exe` + `start explorer.exe`（确认） |
| 刷新 DNS 缓存 | `ipconfig /flushdns`（管理员） |
| hosts 修改 | 管理员身份打开 `drivers\etc\hosts`（记事本） |
| 任务栏从不合并 / 始终合并 | `HKCU\...\Explorer\Advanced\TaskbarGlomLevel` = 2 / 0（重启资源管理器后生效；**Win11 先判版本**，微软已取消这个开关，按钮会直接说明而不是假装成功） |
| 任务栏搜索设置 | 打开 `ms-settings:taskbar`（搜索框显示方式就在这一页，Win10 / Win11 都能开） |
| 开始菜单居左 / 居中 | `HKCU\...\Explorer\Advanced\TaskbarAl` = 0 / 1（**Win11 才认**；按用户定的做法照写，并在结果里说明"这台是 Win10，换到 Win11 就生效"） |
| Win10 / Win11 右键菜单 | `HKCU\Software\Classes\CLSID\{86ca1aa0-…-50c905bae2a2}\InprocServer32` 建 / 删（经典 vs 新版；同样只在 Win11 有肉眼变化） |
| 关闭 / 开启内核隔离 | `HKLM\...\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` = 0 / 1（管理员 + 确认 + **重启电脑后生效**，"关闭"是危险按钮） |
| 关闭 / 开启按流量计费 | 先试着写 `DefaultMediaCost`；**这个键被系统保护**（只有 Windows 自己的服务有写权限，管理员也不行）→ 改走官方设置页并说明原因 |
| 实时防护设置 / Defender 开关设置 / SmartScreen 设置 | **只打开官方界面，不代关系统防线**：先探测 Windows 安全中心 App（`Get-AppxPackage` → `shell:appsFolder\<PFN>!SecHealthUI`），没有就退到 `ms-settings:windowsdefender`，都没有就给一句说明 |
| 防火墙设置 | 先念一遍三个配置文件的开关（`Get-NetFirewallProfile`，没有就说明），再打开 `wf.msc`；管理组件被精简掉的系统会说明"既看不了也打不开" |
| UAC 通知设置 | 打开 `UserAccountControlSettings.exe`（程序不代改 UAC —— 关掉会影响应用商店和部分应用） |
| Windows 更新设置 | 打开 `ms-settings:windowsupdate`（程序不代停更新服务） |
| BitLocker 加密开关 | 有 `BitLockerWizard.exe` 就打开控制面板的 BitLocker 页；只剩 `manage-bde.exe` 就说明"图形界面被移除了，命令行还在" |

**灰色占位 2 个**（`module: todo`，界面上禁用、点不动）：**Win10 资源管理器 / Win11 资源管理器**。

> 这两个按钮**含义还没定**（用户 2026-10-04："先留着不管他"）—— 它可能指资源管理器外观、也可能就是
> 右键菜单风格（那就和上面那两个「右键菜单」重复了）。**想清楚之前不实现**，`hint` 里写明了原因。

> ⚠️ 这一页里"打开官方界面"的按钮有个共同规矩：**先探测、再打开、都没有就说清楚**。
> 原因是用户的这台机器是**精简版 Windows**：Windows 安全中心 App、Defender、BitLocker 图形界面、
> `wf.msc`、连 `netsh advfirewall` 都被拿掉了，`SettingsPageVisibility` 策略还把设置里的
> `windowsdefender` 页藏了。**盲写一个 URI 会让用户点了什么都不发生**（属于本项目最忌讳的"静默失灵"）。

> ⚠️ 「Windows 激活」按钮**已按要求删除**（2026-10-04）：它打开的是系统激活设置页，
> 容易被当成"帮你激活"，属于会带来合规风险的东西。只读的「激活状态」保留 —— 它只是把系统
> 自己报的状态念出来，不碰授权。

### 4.2 右键增强（1 个，**真功能**）

| 按钮 | id | 性质 |
| --- | --- | --- |
| 永久删除工具 | `permdel.gui` | `builtin:permdel/gui` —— 不带参数启动隔壁 `PermanentDeleteSetup.exe`，开它自己的窗口 |

它打开的窗口里什么都有：安装到右键 / 卸载右键菜单 / 查看状态 / 测试一下 / 使用条款 /
引擎日志 / 版本与更新 / 关于作者 —— 所以工具箱只留这一个按钮，**不重复实现**（两边不会分叉）。

找 exe 的顺序（`Launcher.FindPermanentDeleteExe`）：`settings.PermanentDeleteExe` →
环境变量 `MXX1_PERMDEL_EXE` → 工具箱同目录 → 向上三层找 `permanent-delete-menu\bin\` →
`%LOCALAPPDATA%\PermanentDelete\`。**代码里不出现本机绝对路径**（编码体检查这条）。
找不到时的提示是"没找到 PermanentDeleteSetup.exe —— 请在「设置」里指定它的路径"。

原来的 8 条分按钮定义（`install --quiet` / `uninstall --quiet` / `status` / `verify` /
`disclaimer` / `enginelog` / `checkupdate` / `about`）保留在 `tools\rightmenu.json` 的
`_disabled` 数组里；加载器只读 `tools` 数组，所以它们现在是注释性质，想恢复搬回去即可。

### 4.3 清理优化（8 个，**全是真功能**）

| 按钮 | 怎么实现的 | 安全措施 |
| --- | --- | --- |
| 一键清理垃圾 | 临时文件（`%TEMP%` + `Windows\Temp`）+ 清空回收站，报告清了多少 MB | 二次确认 + 危险（深红） |
| 清理临时文件 | 同上但不动回收站 | 二次确认（管理员才清得动 `Windows\Temp`） |
| 清空回收站 | `Clear-RecycleBin -Force` | 二次确认 + 危险 |
| 清理浏览器缓存 | 删 Edge / Chrome / Brave 各配置的 `Cache` 和 Firefox 的 `cache2`；**检测到浏览器在跑就拒绝执行**并告诉你要关哪个 | 二次确认；只删缓存目录，书签/密码/登录状态不动 |
| 磁盘清理 | 打开系统自带 `cleanmgr.exe`（清什么由你勾） | 无（程序自己不删） |
| 存储感知设置 | 打开 `ms-settings:storagesense` | 无 |
| 开机启动项管理 | 打开 `ms-settings:startupapps`（系统启动应用开关） | 无 |
| 大文件查找 | 弹文件夹选择框，扫描后列出最大的 50 个文件（**只读**） | 无 |

> 删除类动作都写清了"删的是什么"：临时文件、回收站、浏览器缓存目录 —— 不碰用户文档、不碰注册表。

### 4.4 系统工具（12 个，三段，**全是真功能**）

| 段 | 按钮 | id | 目标 | 说明 |
| --- | --- | --- | --- | --- |
| 1 | 设备管理器 | `devmgmt` | `%SystemRoot%\System32\devmgmt.msc` | |
| 1 | 打开声音设置 | `sound-settings` | `ms-settings:sound` | 交给 Windows 设置 |
| 1 | 设备和打印机 | `devices-printers` | `shell:PrintersFolder` | 走 `explorer.exe` |
| 1 | 计划任务 | `task-scheduler` | `%SystemRoot%\System32\taskschd.msc` | |
| 2 | 打开注册表 | `regedit` | `%SystemRoot%\regedit.exe` | |
| 2 | 打开服务 | `services` | `%SystemRoot%\System32\services.msc` | |
| 2 | 打开组策略 | `gpedit` | `%SystemRoot%\System32\gpedit.msc` | 家庭版没有 → 提示"这台电脑是 Windows 家庭版，没有「本地组策略编辑器」" |
| 2 | 程序和功能 | `appwiz` | `%SystemRoot%\System32\appwiz.cpl` | |
| 3 | 任务管理器 | `taskmgr` | `%SystemRoot%\System32\taskmgr.exe` | |
| 3 | 系统信息 | `sysinfo` | `%SystemRoot%\System32\msinfo32.exe` | |
| 3 | 常用链接 | `useful-links` | 本程序内的窗口 | `LinksForm`：项目主页 / 工具箱仓库 / 永久删除仓库 / Windows 更新 / 应用和功能 / 关于本机 |
| 3 | 控制面板 | `control-panel` | `shell:ControlPanelFolder` | 走 `explorer.exe` |

- 目标表在 `src\Launcher.cs` 的 `SystemTargets`：`%SystemRoot%` 这类占位符**运行时展开**，
  所以同一个 exe 在任何 Windows / 任何盘符上都对。
- 全是**只读查看**：不改系统、不需要管理员，因此没有 `danger` / `runAsAdmin` / 二次确认。
- 「常用链接」窗口自己**不联网**：只有点「打开」才把链接交给系统（网页 → 默认浏览器，
  `ms-settings:` → Windows 设置），链接字符串从 `AboutForm` 的常量取（署名/仓库只有一个来源）。
- `run <id> --dry` 可以把这 12 个目标解析一遍看对不对（不启动任何东西）。

### 4.5 我的工具（1 个内置按钮 + 用户自己加的按钮）

`[+ 新建按钮]`（`app.newtool`）是**真按钮**，三种加法：

| 怎么加 | 结果 |
| --- | --- |
| 点「+ 新建按钮」或 `Ctrl+N` | 打开图形化窗口（`NewToolForm`）：名称 / 类型 / 路径或目标 / 参数 / 说明 / 页签 / 需要管理员 / 危险按钮 |
| 把**一个** exe / 脚本 / 文件夹拖进窗口 | 打开同一个窗口并预填（可以改名再存） |
| 一次拖**多个**文件 | 直接建按钮，名字取文件名 |
| 手写 `%LOCALAPPDATA%\mxx1-toolbox\tools.json` | 同 `id` 覆盖内置按钮；图形化写入的就是这个文件的格式 |

四种类型对应已有的 `kind`：启动程序（`exe`）/ 打开文件夹·网址·系统页面（`open`）/
运行脚本文件（`script` + `path`）/ 运行一行命令（`script` + `inline`）。

- 右键用户自己加的按钮 → **编辑按钮… / 删除按钮**（只对用户层开放，内置按钮不受影响）；
  删除前二次确认，并且只删 `tools.json` 里那一条。
- 每次写入前把旧文件备份成 `tools.json.bak`（手写的注释或特殊字段万一丢了还能捞回来）。
- 存完会重新测量列宽：新按钮名字更长就把窗口加宽，**不会缩窄**。

## 5 按钮行为

1. **左键单击** → 执行（`ClickMode=double` 时改成双击）
2. **悬停/按下** → 浅蓝底 + 蓝边框 / 下沉一色（灰按钮是灰底变深一档，不变蓝）
3. **执行中** → 按钮禁用 + 图标换成转圈图标（**文字一字不改**）；结束时写日志（含退出码），有输出就弹结果窗口
4. **右键** → 运行 / 以管理员身份运行 / 打开所在文件夹 / 复制启动命令 / 查看按钮定义（编辑与排序在 P1）
5. **危险按钮** → 深红文字，点击先弹「确认执行」（默认按钮 = 取消）
6. **灰色占位按钮** → **禁用**（`Enabled=false`）：点不动、不能聚焦、不弹提示；灰底 + 灰字 + 置灰图标，
   状态栏写明「灰色 N 个没接功能」。命令行 `run <id>` 仍然只解释、绝不执行（退出码 0 + `--dry` 报 `kind=none`）。
7. **找不到目标** → 按钮可点但会明确报错（不静默失效）：系统工具缺失报"这台电脑是 Windows 家庭版…"
   这类整句，隔壁 exe 缺失报"请在「设置」里指定它的路径"。
8. **界面动作优先**（关于 / 日志 / 设置 / 新建按钮 / 常用链接）→ 先于占位判断处理（灰按钮已经点不到，这条是给命令行和界面动作留的）。

## 6 按钮的目标类型（`kind`）

| kind | 干什么 | 关键字段 |
| --- | --- | --- |
| `builtin` | 本程序内置功能 | `module`（`permdel` / `system` / `app`）+ `action` + `options` |
| `exe` | 启动外部程序 | `path`, `args`, `workdir`, `runAsAdmin`, `wait`, `timeoutSec` |
| `script` | ps1 / cmd / bat（可内联） | `shell`, `path` 或 `inline`, `runAsAdmin`, `timeoutSec` |
| `open` | 网址 / 文件夹 / 系统设置页 | `target`（支持 `shell:startup`、`ms-settings:`、`https://`） |
| `macro` | 多步串联 | `steps[]`（P1） |

`builtin` 的三个模块：`permdel`（隔壁安装器：`gui` / `install` / `uninstall` / `status` / `verify` /
`disclaimer` / `enginelog` / `checkupdate`）、`system`（12 个 Windows 组件，见 §4.4）、
`app`（本程序界面动作：`about` / `log` / `settings` / `checkupdate` / `newtool`）。

统一行为：默认隐藏黑窗口，输出异步读取（`ReadToEndAsync`，先等退出再读会在输出 >4KB 时父子互等），
超时强杀，退出码 + 输出尾部进日志。

## 7 `tools\*.json` 结构

```json
{
  "id": "permdel.gui",
  "tab": "rightmenu",
  "segment": 1,
  "order": 10,
  "name": "永久删除工具",
  "icon": "assets/icons/xxx.png",
  "kind": "builtin",
  "module": "permdel",
  "action": "gui",
  "options": "",
  "runAsAdmin": false,
  "confirm": false,
  "pinned": true,
  "danger": false,
  "placeholder": false,
  "hint": "P1"
}
```

- `placeholder: true` = 只做按钮不接功能（界面里画成灰色），`hint` 写提示里的阶段
- 内嵌：`tools\*.json`（`build.ps1` 以 `tools.<文件名>` 为资源名内嵌，编译后自检）
- 用户层：`%LOCALAPPDATA%\mxx1-toolbox\tools.json`，同 `id` 覆盖内置，升级不冲掉
- 编码 **UTF-8 无 BOM**；`%ProgramFiles%` 等占位符会展开
- 加载器只读 `tools` 数组：别的键（例如 `rightmenu.json` 里的 `_disabled`）当注释用，不会被当成按钮

## 8 本程序自身的文件与命令

| 位置 | 内容 |
| --- | --- |
| `%LOCALAPPDATA%\mxx1-toolbox\settings.ini` | 主题 / 启动方式 / 日志保留 / 二次确认 / 隐藏黑窗口 / 日志面板 / 永久删除安装器路径 |
| `%LOCALAPPDATA%\mxx1-toolbox\tools.json` | 用户自建按钮 |
| `%LOCALAPPDATA%\mxx1-toolbox\logs\toolbox-YYYY-MM-DD.log` | 运行日志 |

CLI：`list [--tab <id>]` / `run <id> [--admin] [--dry]` / `status` / `checkupdate` / `help`
（退出码 0 = 成功，1 = 执行失败，2 = 用法错误；重定向输出强制 UTF-8）。
`run --dry` 只解析目标、不启动，输出 `kind= / target= / exists= / hint=` —— 12 个系统工具
靠它做回归（不用真的把设备管理器开 12 次）。`status` 里有 `systemTargets=` / `systemMissing=`。

## 9 P0 / P1 / P2

**已完成（P0 → 1.2.0）**：窗口骨架、五个页签、四列网格 + 段分隔线、底栏五入口
（搜索 / 日志 / 设置 / 关于 / 检查更新）、浅/深/跟随系统主题、`tools\*.json` 驱动 + **53 个按钮**
（51 真功能 + 2 灰色禁用）、灰色占位规则（含置灰图标）、「右键增强」单个按钮（调隔壁安装器窗口）、
「系统工具」12 个（Windows 自带组件 + 常用链接窗口）、「清理优化」8 个、「常用设置」13 个、
**「我的工具」图形化新建 / 编辑 / 删除 + 拖拽加按钮**、外部工具目录 `bin-tools\`、
按钮悬停提示、按钮右键菜单、日志面板 + 日志窗口、
`build.ps1` / 编码体检 / 命令行回归 44 项 / 界面回归 65 项 / 截图工具。

**P1（剩下的）**：把剩下的灰色占位里安全的那些接上（一个一个来，都要走二次确认 + 日志留痕）、
按钮排序 / 隐藏 / 固定到常用、多步 `macro`。

**P2**：工具箱自身的更新检查、插件目录扫描、按钮包导入导出、`dsh` 类型按钮、
把外部工具内嵌进 exe 当兜底（见 §13 方案 ② ③）。

## 10 红线

1. **编码**：`.cs` / `.ps1` **UTF-8 带 BOM**；`.vbs` 纯 ASCII；`.md` / `.yml` / `.json` 无 BOM。
   `build.ps1` 自动补 `.cs`/`.ps1` 的 BOM，`tools\Test-Encoding.ps1 -Fix` 兜底。
2. **排版**：一律 `TableLayoutPanel` / 排版函数，**绝不手写坐标**；标签绝不允许和按钮重叠。
3. **标题栏**：`MinimizeBox=false` + `MaximizeBox=false`。
4. **字形**：界面文字不用微软雅黑缺字形的 `✓ ⚠ →`；图标走 PNG 或程序内绘制。
5. **日志**：一律"最新的在最上面"，不给正序开关。
6. **危险动作**：二次确认 + 日志留痕 + 深红文字。
7. **不联网**：工具箱自身不联网，`checkupdate` 只读版本号。
8. **署名**：`mxx1` / `mxx1.cn` / GPL-3.0-or-later，字符串只从 `AboutForm` 取。
9. **不改隔壁仓库**：只在运行时调用它的 exe。
10. **推送前问用户**。

## 11 验收标准（当前全部满足）

- `build.ps1` 一次通过，产出单文件 `bin\Mxx1Toolbox.exe`（约 136 KB = 139,264 字节，含 53 个内嵌图标），无警告
- `tools\Test-Encoding.ps1` 全绿（98 个文件）
- `tests\Test-Cli.ps1` **44/44**：中文不乱码、按钮数 53、页签分布 31/1/8/12/1、灰色占位 2、
  占位按钮写日志、`permdel.gui --dry` 解析出隔壁 exe、直接问隔壁 exe 的 `status` 拿到 `installed=`、
  **12 个系统工具全部 `--dry` 解析通过**（少了目标或没解释就红）、错误用法退出码 2
- `tests\Test-Gui.ps1` **60/60**：标题栏没有最小化/最大化方框、窗口可缩放、四列多行、
  **按钮尺寸完全一致**、**零重叠**、**文字没被裁（按渲染墨迹行数）**、**灰按钮真的灰**、
  **真按钮不是灰的（最暗 26 ≤ 80）**、**灰按钮明显比真按钮淡（差 ≥ 30）**、**真按钮图标上下居中（误差 0.5px ≤ 1px）**、
  五个页签切换后按钮数正确、搜索/日志面板开合、真点灰色按钮后日志多一行且不弹窗、
  危险按钮弹「确认执行」并能取消、深色主题正常
- `tests\Test-All.ps1` 合计 **109 项全绿**（命令行 44 + 界面 65，外加编码体检）
- 截图：`docs\gui-shot.png`（浅色 640×739）、`docs\dark-shot.png`（深色）、
  `docs\system-shot.png`（系统工具页签）

## 12 实现期间踩到并修掉的坑（别再踩）

1. **`Dock=Top` 的 `TableLayoutPanel` 会把多余宽度全塞给最后一列** —— 四列网格里
   每个第 4 个按钮都比同排宽 28px。修法：末尾加一个占 100% 的空列吃掉余量。
2. **`TableLayoutPanel` 的行默认是 AutoSize** —— 状态栏那一行按内容撑到 30px，而容器只有 24px，
   底栏按钮下边缘被裁 5px。修法：显式 `RowStyles.Add(Percent 100)`（同时按钮改成固定高 20px，
   因为 `AutoSize=true` 的按钮会自己长高）。
3. **列宽不能拍脑袋定**：按最长按钮名测量 + 留足图标余量。余量给 22px 时
   「关闭实时防护与篡改」渲染成「关闭实时防护与…」；给 36px 才够（还要加 `AutoEllipsis` 兜底）。
4. **GUI 子系统程序的 CLI 输出**：`Console.OutputEncoding` 在 stdout 是管道时改不动（setter 要真控制台），
   要么自己 `SetOut(UTF-8 writer)`；另外**不要**在 stdout 已被重定向时 `AttachConsole`，否则输出绕开管道，
   `$exe status | ...` 拿到空字符串。
5. **占位按钮的那 600ms 灰显**：界面测试要等它恢复再断言"按钮可用"，否则会把设计行为当成卡死。
6. **点击按钮时不许改文字**：原来运行中会显示 `名字…`，多出来的一个字符让"图标+文字"整组重新居中，
   每点一次图标就横跳一下（长名字还会溢出按钮被截断）。改成**换图标**（转圈，同样 16×16）+ 变灰，
   文字一动不动。回归检查 E05 直接读点击过程中的按钮文字，必须一字不变。
7. **状态栏文字必须放得下**：原来拼的是 `共 N 个按钮（本页 M） · 按钮名 · 完成：xxx 退出码 0`，
   实测 414px 塞进 392px 的标签 → 尾巴被截。改成短摘要 `N 个按钮 · 本页 M · 名字 · 完成`（最长 262px），
   完整内容进日志 + 标签悬停提示。检查：B06b（按钮文字不溢出）、D01d/E07（状态文字不溢出）。
8. **按钮高度必须按"字体 + 边框 + 内边距"算，不能只按字体算**：底栏按钮写死 20px 时装不下
   8.25pt 的一行字（用户："右下角按钮没正常显示、被挡住"）；改成 `MeasureText("国").Height + 6` = 22px
   后**还是差一行**（用户："底部按钮还是差一点的才显示全文字，主要是高度问题"）——
   因为 Flat 按钮除 1px 边框外还有约 3px 内边距，文字实际只能画在「高度 − 8」里，22px 只给到 14px。
   实测证据：底栏「检查更新」渲染出来只有 9 行墨迹，按钮墙上同字号的按钮有 10 行。
   最终公式 **`行高 + 8` = 24px（底栏 28px）**。检查：D01b（子控件落在容器内）、D01c（≥ 24px）、
   **D01e（数渲染出来的墨迹行数，必须和按钮墙一样多 —— 这条才是真正能抓住"被裁"的那个）**。
9. **WinForms 画图标的位置不等于按钮中心**：`FlatStyle=Flat` 的按钮把图片画在文字行框中心**往下 1px**，
   所以 16px 高的画布比按钮中心低 1px。实测（`PrintWindow` 抓像素，30px 的按钮）：
   图标墨迹在 9..23 行、中心 16.0，而按钮中心 14.5 —— 低 1.5px，同一按钮的文字中心恰好 14.5。
   修法：`IconFactory.Normalize()` 把**所有来源**（内嵌 PNG / 外部 PNG / 实时绘制 / 转圈图标）
   统一成 **16×15 画布**：首行整行透明时去掉那一行（等于把图案抬 1px），
   首行有墨迹的图改取 0..14 行（不切墨迹）。实测改完是 8..22、中心 15.0，误差 0.5px
   （整数画布下能做到的极限）。检查 B09（图标墨迹中心与按钮中心之差 ≤ 1px）。
   注意：`Padding` 挪不动这个偏差 —— 它把图标和文字**一起**挪（实测每 1px 底边距抬 1px），
   所以要动的是画布，不是内边距。
10. **PowerShell 数组的坑（本仓库踩过两次）**：`@(@('a','b'))` 传给函数会被解包成 `@('a','b')`，
    于是 `$p[0]` 取到的是**字符**而不是字符串 —— 用 `Replace` 批量改代码时会把整个文件改烂。
    脚本里要改文件就用两个平行数组 + 显式下标，或者干脆用编辑器逐个改。
11. **改完任何 `.ps1`/`.cs` 都要回头看 BOM**：编辑器（含各种打补丁工具）会静默吃掉 BOM，
    而 PowerShell 5.1 读无 BOM 的 `.ps1` 会按 GBK 解，中文注释直接变成语法错误
    （症状：`Missing using directive`、`Unexpected token` 之类莫名其妙的位置）。
    改完跑一次 `tools\Test-Encoding.ps1 -Fix`。
12. **PS 里逐像素扫图会慢到假死**：一个 `Get-Pixel` 函数按像素调用，几万次函数调用要几分钟，
    看起来像"卡住了"（实测被误判过一次）。要在 PS 里量像素就 `LockBits` 取一次 `byte[]`
    再纯数组循环（`tests\Test-Gui.ps1` 的 `Get-InkRows` 就是这么写的），或者干脆用 csc 编个小工具。
13. **灰度图标骗过了"图标判定"**（改灰色占位时踩到）：界面回归原来用"亮而饱和"（`max-min > 60 &&
    max > 140`）找图标，而置灰后的图标一点饱和度都没有 → 找不到图标 → `labelFrom` 退回 1 →
    把**灰色图标**当成文字算进去，墨迹行数从 10 行变成 15 行（D01e 立刻红）。
    修法：判据加一条"明显比底色暗"（`mx < bgMax - 45`），两种都认；同时把"图标上下居中"那条检查
    挪到**彩色图标**的按钮上（「右键增强」那个），别拿灰图标去量。
14. **单个 `PSCustomObject` 没有 `.Count`**：`function f { return @(单个对象) }` 会被 PowerShell 解包成
    一个对象（不是数组），而它的 `.Count` 返回**空**，于是 `$x.Count -gt 0` 恒为 False ——
    症状是"明明找到了弹窗，却判定成没找到"。调用处必须再包一层 `@(f ...)`。
    （和坑 10 是同一类：PowerShell 的数组/单值边界。界面回归的 F 组踩过。）
15. **WinForms 的 `ToolTip` 也是一个顶层窗口**：类名 `tooltips_class32`、标题为空。
    给网格按钮加了悬停提示以后，"除主窗口以外的可见窗口 = 弹窗"这个判据取到的第一个窗口
    就不是消息框了（F02/F03 直接红）。判"弹窗"要按窗口类：消息框是 **`#32770`**。
16. **界面测试不能依赖用户的 `settings.ini`**：用户把日志面板开着（`ShowLogPanel=1`）时，
    B08「默认不显示日志面板」会假红（一条"看起来像 bug"的失败）。现在界面回归一开头先写一份
    已知设置（浅色 / 单击 / 二次确认开 / 日志面板关），跑完在"现场复原"里按原样写回去。

17. **禁用的 Flat 按钮画出来的字比设定的颜色更深**：灰按钮改成 `Enabled=false` 以后，
    WinForms 用 `ControlPaint.DrawStringDisabled` 画标签，会在字下面描 1px 更深的影子 ——
    实测最暗像素从"我们设的纯灰 138"掉到 **77**，原来那条"最暗 ≥ 110"的检查立刻红了。
    修法：阈值按实测取 **≥ 60**，另外加一条**相对**判定（C01d：灰按钮必须比真按钮至少淡 30，
    真按钮实测 26）。教训：判定阈值要跟着**渲染现实**走，别跟着我们赋的值走。
18. **`BM_CLICK` 能绕过禁用状态**：界面回归是用 `PostMessage(BM_CLICK)` 点按钮的，而直接投递的消息
    不一定被"控件已禁用"挡住 —— 所以**不能**用"点一下没有反应"来证明"按钮点不动"。
    要证明禁用就读 `IsWindowEnabled`（E02/E03 就是这么做的）。
19. **把一排按钮收成一个时，先看还有没有别的东西只从这里进得去**：「右键增强」8 个按钮收敛成 1 个之后，
    **关于窗口（署名 / 许可证 / 打开设置目录）就没了唯一入口**，直到做 bin-tools 入口抽查时才发现。
    修法：底栏加第 5 个按钮「关于」。教训：删按钮 / 收页签时，先列一遍"它是不是某个功能的唯一入口"。
20. **加 / 删按钮要连带处理图标**：`tools\Make-Icons.ps1` 是从 exe 的 `list` 读清单再批量画的，
    所以顺序必须是 **build（清单进 exe）→ Make-Icons → 删掉不在清单里的孤儿 PNG → 再 build（图标嵌进去）**。
    漏删孤儿时 `build.ps1` 的自检会报"embedded icons 数对不上"。（删「Windows 激活」时踩了一次。）
21. **"相对路径按 exe 目录解析"会把 URI 也当成文件路径**（用户实际点出来的）：
    `bin-tools\` 那条改成"相对路径先按工具箱目录找"之后，`ms-settings:storagesense` 这种
    **URI 也属于"不是绝对路径"**，于是被拼成 `<工具箱目录>\ms-settings:storagesense`，
    用户点「存储感知设置」看到的是 **"打开失败：系统找不到指定的文件"**。
    修法：`AppPaths.Resolve` 先认 URI —— 冒号前是协议名、且整串既不含 `\` 也不含 `/` → 原样返回；
    凡是"给相对路径加前缀"的逻辑都要先过这一关。检查：命令行 `run storage-sense --dry` 必须打出
    `target=ms-settings:storagesense`（而不是拼过前缀的路径）。
22. **自己写 JSON 的字段拼接助手别把逗号放在第一个字段前面**（用户实际遇到的）：
    `UserTools.ToJson` 原来每个字段都拼 `", "`，于是第一个字段写成 `{ , "id": ... }` ——
    人看着只是多一个逗号，**工具箱自己的解析器直接判非法**，报"JSON 对象缺少键（位置 109）"，
    症状是"图形化新建的按钮当场消失、重开也读不回来"。修法：加一个 `First()` 助手，首个字段不加逗号。
    教训：手写序列化器必须有**回读自检**（界面回归 C13：图形化建一个按钮 → 用 `list` 读回来），
    光看写出来的文件"像不像 JSON"是发现不了的。
23. **`DllImport` 忘了 `ExactSpelling`，把整个界面回归拖死**（2026-10-04 卡了一次，用户看着屏幕说"卡在
    点击新建按钮弹出的窗口了"）：给 `SetText` 写的是 `CharSet = CharSet.Unicode` + 方法名
    `SendMessageStringW` → 运行时先去查 **`SendMessageTextWW`**（Unicode 会把 `W` 加到**入口点**上，
    不只是方法名），查不到就抛 `EntryPointNotFoundException`。后果不是"一条检查红"，而是：
    输入框一直是空的 → 「创建按钮」点不动 → **模态窗口一直开着** → 主窗口保持禁用状态 →
    后面每一组"点主窗口"的检查全部连带失败，测试看着像卡死。修法：`EntryPoint = "SendMessageW",
    ExactSpelling = true`。**两个教训**：① 跨进程写控件的检查必须**回读断言**（`[TBGui]::Text`），
    写不进去就当场判死；② 开过模态窗口的测试，**每条分支的出口都要关掉它**（`Close-StrayDialogs`
    在 C 组收尾还加了一条 C16 专门盯"有没有残留模态窗口"）。
24. **测试被打断 = 用户的按钮清单和设置被留在"暂停"状态**：界面回归/命令行回归为了数按钮数，
    会把用户自己的 `tools.json` 改名成 `.paused-by-*`，跑完再放回来；一旦中途被 Ctrl+C / 卡死 /
    沙箱杀进程，这个"放回来"就永远不会执行 —— 用户看到的是"我建的按钮凭空没了"。
    修法：**开工先自愈**（发现 `.paused-by-*` 存在而正式文件不存在，先搬回去），
    `settings.ini` 也留一份 `.before-test` 备份，收尾成功才删。
    教训：测试对用户真实数据的"临时占用"必须有**崩溃后可恢复**的设计，不能只靠 finally。
25. **`path` 展开环境变量了，`args` 却没有**（用户实际点出来的："hosts 修改没有正常打开"）：
    「hosts 修改」是 `path=%SystemRoot%\notepad.exe` + `args=%SystemRoot%\System32\drivers\etc\hosts`，
    `Resolve/Expand` 只作用在 `path` 上，参数被**原样**丢给进程 —— 记事本收到字面量
    `%SystemRoot%\...`，于是弹"找不到文件"（或问你要不要新建一个叫这个名字的文件）。
    修法：`Launcher.ExpandArgs()`，只展开 `%变量%`，开关/引号/多参数原样保留。
    教训：**一个"路径字段"的修法要同时问一遍"参数里的路径呢"**；回归 D06 把每个 `kind=exe`
    按钮的 `--dry` 都跑一遍，`command=` 里不许残留 `%变量%`。
    同一轮还发现日志行把 cmd 脚本显示成 `cmd -Command "..."`（cmd 只有 `/c`，实际执行是对的）——
    显示的字符串错了会把人往错方向带，所以显示代码也要跟着真命令一起改。
26. **模态窗口开着的时候，拖放事件仍然会落到主窗口**（用户实际遇到的："点击新建按钮后拖入程序图标后
    会打开一个新的新建按钮弹出的窗口，应该只弹一个的"）：`AllowDrop` 挂在主窗口 / 内容面板 / 网格上，
    而**模态对话框只挡住鼠标键盘，挡不住 OLE 拖放** —— 拖进来的文件照样触发网格的 `OnDragDrop`，
    于是又 `ShowDialog` 出一个「新建按钮」窗口。
    修法两层：① `MainForm._userDialogOpen` 守卫，已经开着一个就不再开（并给一句状态提示）；
    ② **让对话框自己接拖放**（`NewToolForm.EnableDrop` 递归给每个子控件 `AllowDrop = true`，
    因为拖放落在光标底下的那个控件上），拖进来直接填当前窗口。
    教训：给窗口加拖放时，要问一句"它弹出模态子窗口的时候，拖放会去哪儿"。
27. **拖进来的快捷方式（.lnk）要解析成它指向的真程序**（用户："拖入快捷方式图标程序会失败修复一下
    （直接指向目标地址）"）：原来 `.lnk` 走 `else` 分支被当成 exe，按钮里存的是**快捷方式本身的路径** ——
    快捷方式一挪走、一改名，按钮就废。修法：`DroppedFile.ResolveShortcut()` 用 `WScript.Shell`
    晚绑定（csc 没有 COM 引用，靠反射调 `CreateShortcut`）取 `TargetPath / Arguments / WorkingDirectory`；
    取不到（比如指向「此电脑」这种 shell 文件夹的快捷方式）就退回 `kind: open` 让系统当双击打开。
    另外 `Launcher` 在 `kind: exe` 启动前也补了一次解析 —— 老版本建出来的按钮照样能跑。
    测试：拖拽手势是 OLE 拖放、没法在测试里合成，所以把**决策**做成命令行 `draft <路径>`
    （只打印会变成什么按钮，不写文件），回归 G01–G06 盯它。
28. **"打开官方界面"这类按钮，盲开就是静默失灵**（做安全类那一批时发现的，影响 5 个按钮 + 2 个旧按钮）：
    用户的机器是**精简版 Windows** —— 我按常规写法准备的入口，一半在这台机器上根本不存在：

    | 入口 | 这台机器上的结果 |
    | --- | --- |
    | `ms-settings:windowsdefender` | 能打开"设置"，但 `SettingsPageVisibility` 策略把 `windowsdefender` **这一页藏了** |
    | `shell:appsFolder\…SecHealthUI` | Windows 安全中心 App **没装**（`Get-AppxPackage` 查不到），explorer 回退成了打开「文档」 |
    | `wf.msc`（高级防火墙） | **不在**（`mmc.exe` 打开的是空控制台，看着像成功） |
    | `netsh advfirewall` | **不存在**（`advfirewall` 上下文整个没了） |
    | `control.exe /name Microsoft.WindowsFirewall` / `firewall.cpl` | 退出码 0、**一个窗口都没有** |
    | `control.exe /name Microsoft.BitLockerDriveEncryption` | 同上（现有的 BitLocker 按钮一直是死的） |
    | `Get-NetFirewallProfile` / `Get-NetAdapter` | NetSecurity / NetAdapter 模块**都不在** |
    | `manage-bde -status` | Provider load failure（WMI 提供程序也没了） |
    | `UserAccountControlSettings.exe` / `desk.cpl` / `services.msc` / `gpedit.msc` | **能开**（这才是正常的那些） |

    修法：这类按钮**一律先探测再打开**（`Test-Path` / `Get-Command` / `Get-AppxPackage`），
    探测不到就给一句"这台系统里 XX 已被移除，没有能打开的界面"，而不是"点了没反应"。
    探测可用性时**别用 `Get-NetAdapter` / `Get-NetFirewallProfile` 当判据**（它们本身可能没装），
    用 WMI（`Get-CimInstance Win32_NetworkAdapter`）+ 文件存在性更稳。
    教训：**"官方界面"不是常量**，除了系统版本（Win10/Win11）还要考虑"组件被精简掉了"。
29. **回读校验分不清"我写的"和"本来就是"**（写「关闭按流量计费」时踩到）：
    脚本先试着写 `DefaultMediaCost`，失败后**读回**看是不是 1 —— 而它默认就是 1，
    于是失败被当成成功，弹出"已设为不计费"的假消息。修法：**以"写入是否抛异常"为准**
    （`try { … } catch { $ok = $false }`），读回只用来在消息里显示当前值。
    教训：**用状态值判断"操作成功"时，先问一句"这个值有没有可能本来就是这样"**；
    比较可靠的是"操作前后有没有变化"，或者干脆看操作本身有没有报错。
    同一个键还有个坑：`DefaultMediaCost` 的 ACL 只给 `SYSTEM` / `Wcmsvc` / `TrustedInstaller` 写权限，
    **管理员也只有读**（提权也写不进，逐连接的 `Cost` 值写了也不改变计费类型）——
    所以"按流量计费"在第三方程序里基本只能打开设置页让用户自己点。
## 13 外部工具放哪：`bin-tools\` 工具目录（① 层**已实现**）

**背景**：工具箱现在会调一个外部程序（隔壁的 `PermanentDeleteSetup.exe`），以后还会加别的
（解压、卸载、查硬盘…）。用户 2026-10-04 的意见是"**应该建一个子文件夹，毕竟后续还得加其他 exe 工具**"。
这一节就是照这个思路排的方案：**一个约定好的工具目录 + 统一的查找顺序**，内嵌只是一层可选兜底。

### 13.1 三层结构（可以只做第一层，也可以叠加）

| 层 | 做什么 | 代价 | 收益 |
| --- | --- | --- | --- |
| **① 工具目录（已实现：用户选的名字是 `bin-tools`）** | 工具箱目录下建 **`bin-tools\`**，外部工具按原文件名丢进去；查找顺序里加这一档；`build.ps1 -Package` 自动把隔壁的 exe 拷进 `bin-tools\`；设置窗口加「打开工具目录」，找不到工具时的提示里带上这个路径 | 小（`Launcher` 加一档查找 + 打包脚本几行 + 一个入口按钮） | 加工具 = 丢文件；工具箱目录结构清楚；杀软无感 |
| **② 内嵌兜底（可选）** | `build.ps1` 把 `bin-tools\` 里的 exe 也 `/resource:` 进去；运行时若磁盘上找不到，就释放到 `%LOCALAPPDATA%\mxx1-toolbox\bin-tools\`（比长度/哈希，变了才重写）再启动 | 中（释放逻辑 + 版本比对 + 测试） | 真·单文件：只拷 `Mxx1Toolbox.exe` 也能用 |
| **③ 工具清单自动扫（P2）** | `bin-tools\<工具名>\tool.json` 描述它的按钮（名字/图标/参数），工具箱启动时扫一遍自动生成按钮，不用改 `tools\*.json` | 大（新格式 + 扫描 + 冲突处理 + 测试） | 加一个工具 = 丢一个文件夹，按钮自己长出来 |

### 13.2 目录长这样

```
萌新工具箱\
  Mxx1Toolbox.exe            ← 主程序（按钮清单与图标都内嵌在里面）
  bin-tools\                 ← 外部工具全放这里（① 层，已实现）
    PermanentDeleteSetup.exe
    （以后）Everything.exe / GeekUninstaller.exe / ...
  README.md / LICENSE        ← 分发 zip 里带着
```

**为什么叫 `bin-tools` 而不是 `tools`**：仓库里的 `tools\` 已经是"按钮清单 JSON + 构建脚本"的意思，
而 `build.ps1 -Package` 会把仓库的 `tools\*.json` 拷到发布目录根部 —— 发布目录里再来一个
`tools\`，就会出现"同一个文件夹里既有 JSON 又有 exe"的混乱。名字也可以叫 `apps` / `bin-tools`，
**只要不与 `tools` 撞名**。

**找不到工具时把路径写进提示**（"没找到 PermanentDeleteSetup.exe —— 请把 exe 放进
`<工具箱目录>\bin-tools\`，或在「设置」里指定路径"），并且设置窗口/关于窗口给一个
**「打开工具目录」**按钮（点一下没有就自动建好并打开），这样"加工具"这件事不说自明。

### 13.3 查找顺序（最终建议）

1. 「设置」里用户指定的完整路径（`settings.ini` 的 `PermanentDeleteExe`）—— 永远最高，方便调试；
2. **工具箱目录 `bin-tools\<文件名>`**（① 层的主角，已实现）；
3. （② 层，没做）内嵌释放出来的 `%LOCALAPPDATA%\mxx1-toolbox\bin-tools\<文件名>`（② 层，只在 1、2 都没有时用；
   释放目录必须**固定**，不能放 `%TEMP%` —— 隔壁装进右键菜单后，引擎脚本虽然在
   `%LOCALAPPDATA%\PermanentDelete\`，但用户以后还会再打开这个 exe 去卸载/修复，临时目录被清就找不到了）；
4. 开发场景兜底：向上三层找 `permanent-delete-menu\bin\PermanentDeleteSetup.exe`（现在就有，留着方便开发）；
5. `%LOCALAPPDATA%\PermanentDelete\`（隔壁自己可能会放一份）。

### 13.4 相对路径（**已实现**）

`kind: "exe"` 的 `path` 以前只做环境变量展开，**相对路径按进程当前目录解析**（不可靠）。
现在 `AppPaths.Resolve()` 规定：**相对路径先找工具箱目录、再找 `bin-tools\`**，
所以清单里写 `"path": "PermanentDeleteSetup.exe"` 或 `"path": "bin-tools\Xxx.exe"` 都能用
（`icon` 字段和 `open` 的 target 走同一条路）。回归检查 F04 就是拿一个临时按钮验证这件事的。

**URI 必须先认出来再谈相对路径**（坑 21，用户实际踩到）：`ms-settings:storagesense` 这类 URI
同样"不是绝对路径"，一开始被拼上了工具箱目录，于是点「存储感知设置」报"系统找不到指定的文件"。
现在 `Resolve()` 的第一条就是 **URI 守卫**：冒号前是协议名（`colon > 0`）且整串既不含 `\` 也不含 `/`
→ **原样返回**，不加任何前缀。

### 13.5 各方案的取舍（原问题：要不要一起打包进 exe）

| 方案 | 做法 | 优点 | 缺点 |
| --- | --- | --- | --- |
| A 保持现状 | 不打包，运行时四处找 | 零维护、隔壁升级立刻生效、杀软无感 | 单独拷 `Mxx1Toolbox.exe` 到别的机器时那个按钮用不了 |
| B 内嵌 | `/resource:` 进 exe，首次点击释放到固定目录 | 真·单文件，拷贝即用 | exe 里套 exe，个别杀软会多看一眼；体积 +135 KB |
| C 工具目录 | 就是上面的 ① 层（`bin-tools\` 子文件夹） | 最简单、杀软最友好、以后加工具最省事 | "单文件"变成"一个文件夹" |


用户 2026-10-04 选了 **C / ① 工具目录**，名字定为 **`bin-tools`**，**已经实现**：
查找顺序（含工具目录档位）、`build.ps1 -Package` 自动拷入、「打开工具目录」入口、相对路径按工具目录解析。
**B（内嵌成单文件）还没做** —— 等用户说"我只想拷一个 exe"再加，查找顺序天然支持共存。
