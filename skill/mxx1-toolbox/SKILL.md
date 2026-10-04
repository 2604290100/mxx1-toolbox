---
name: mxx1-toolbox
description: Use when working on "萌新工具箱 / mxx1 Toolbox" — the Windows button-wall launcher whose main window is a multi-row, multi-column grid of small buttons that each start another program, script, or feature. Covers its interface rules (four column compact buttons, top tabs, separator segments, light/dark theme, computed column width, grey "not wired up yet" buttons), the tools\*.json button registry, how buttons launch things (exe / script / open / builtin / macro), how the 26 系统工具 buttons open Windows components, how the 常用设置 registry switches record the original value so every change can be undone, how a run reports success or failure, plus build.ps1, screenshot tool and the GUI/CLI test suites.
---

# 萌新工具箱（mxx1 Toolbox）

**主界面 = 多行多列的小按钮墙**，点一下按钮就启动一个已经做好的程序 / 脚本 / 功能。
加按钮只是往 `tools\*.json` 丢配置，**不需要重新编译主程序**。

- 程序名 **萌新工具箱**，标题栏 `萌新工具箱 v1.4.0`；署名 `mxx1` / `mxx1.cn`；GPL-3.0-or-later
- 工程目录 **`D:\萌新工具开发\toolbox\`**，与隔壁 `permanent-delete-menu` **互不修改**（只调它的 exe）
- 外观参考：`C:\Users\Administrator\Pictures\Snipaste_2026-10-04_10-29-34.png`（那种紧凑按钮墙）

> **接手 / 新会话先做两件事**：读 `docs\DESIGN.md`（外观与行为的**唯一正本**）和本文件。
> 设计一改先改 `DESIGN.md`，再同步本 skill —— 两份分叉就会出现"两套行为"。

## 当前状态（2026-10-04，v1.4.0）

- ✅ **测试 187 项全绿**：命令行回归 96 + 界面回归 91（外加编码体检 158 个文件、内联脚本与清单体检
  34 个脚本 / 7 个清单）。产物 `bin\Mxx1Toolbox.exe`（约 260 KB 单文件），
  七个 `tools.*.json` + 105 个 `icons.*.png` 已内嵌。
- ✅ **八个页签、105 个内置按钮，全部是真功能，灰色占位一个不剩**：`常用`（置顶 + 最近使用，
  算出来的）/ `常用设置` 33 / `系统工具` 26 / `隐私设置` 29（11 组成对开关 + 4 个权限入口 +
  状态/优化/还原）/ `应用管理` 5 / `清理优化` 8 / `右键增强` 1 / `我的工具` 3（新建 / 导出 / 导入，真）。
  灰色规则本身还在（用户自己写 `placeholder:true` 会灰掉、点不动）：两套测试会**临时往用户层
  注入一个占位按钮**来盯住它，跑完必删。
- ✅ **灰色 = 功能还没接入 = 禁止点击**（用户 2026-10-04 改的规则）：2 个占位按钮 `Enabled=false`、
  灰底灰字 + **置灰图标**（`IconFactory.GetMuted()`）—— 点不动、不能聚焦、不弹提示；
  禁用控件不显示 tooltip，所以状态栏在有灰按钮的页面上带一句「灰色 N 个没接功能」。
- ✅ **「系统工具」26 个 + 「清理优化」8 个 + 「常用设置」33 个都是真功能**；
  `run <id> --dry` 能把它们的目标解析一遍（缺组件给整句说明，家庭版没有 gpedit）。
- ✅ **「常用设置」里 12 个写注册表的开关都能一键还原**（v1.4.0）：全部走 `src\RegEngine.cs`
  （和隐私设置**共用同一份**"记原值 → 写入 → 读回核对 → 还原"的实现），记录写在
  `%LOCALAPPDATA%\mxx1-toolbox\sysreg-original.tsv`；另有「查看设置改动 / 还原设置改动」两个按钮。
  6 组开关：任务栏合并方式 / 开始菜单对齐 / 驱动自动安装 / 内核隔离 HVCI /
  Win10-Win11 资源管理器 / Win10-Win11 右键菜单。**命令行没有写入口**（Test-Cli 的 L07 盯着）。
- ✅ **窗口默认固定尺寸**（v1.4.0，用户定的）：高度 620，宽度 = 4 × 列宽 + 24 并且**粘在
  `settings.ini` 的 `WindowWidth` 上**（第一次量出来就写进去）；名字超长的按钮改用省略号
  （悬停提示里是全名），**不许把窗口撑宽**。「高度跟随当前页签的内容」降级成设置里的选项。
- ✅ **跑完必有反馈**（v1.4.0，用户报的「点击确认以后也没有成功或者失败的反馈」）：页签下面一条
  绿/红结果条（8 秒后自动收，点它看日志）+ 有输出就开结果窗口（标题写「成功/失败（用时 X 秒）」）
  + 底栏「运行中（已 X 秒）」。确认改用自家的 `src\ConfirmForm.cs`，不再用 `MessageBox` 甩命令。
- ✅ **搜索跨全部八个页签**（结果按页签分块），页签顺序按使用频率排过（`常用` 在最前）。
- ⚠️ **这台机器是精简版 Windows（2026-10-04 实测）**：Windows 安全中心 App 没装、Defender 组件被移除、
  `SettingsPageVisibility` 策略藏了设置里的 `windowsdefender` 页、BitLocker 的 `BitLockerWizard.exe`
  不在、`wf.msc` 不在、`netsh advfirewall` 不存在、`firewall.cpl` 与 `control.exe /name …` 打开是空的、
  NetSecurity / NetAdapter 模块都没有。所以：
  **凡是"打开某个官方界面"的按钮，一律先探测再打开，探测不到就说明原因**
  （探测用 `Test-Path` / `Get-Command` / `Get-AppxPackage` / WMI，别用 `Get-NetAdapter` 当判据）。
  安全类按钮（实时防护 / Defender / SmartScreen / 防火墙 / UAC / 更新）**只打开官方界面，绝不代关系统防线**。
- ✅ **「右键增强」只剩 1 个按钮「永久删除工具」**：不带参数启动隔壁 `PermanentDeleteSetup.exe`
  = 开它自己的窗口（安装/卸载/状态/测试/条款/日志/更新都在那里）。原来 8 条定义留在
  `tools\rightmenu.json` 的 `_disabled` 数组里当注释（加载器只读 `tools`）。
- ✅ **本地 Git 仓库已建**（提交都在本地）；⬜ **GitHub 远程还没建**，**推送前必须问用户**。
- ✅ 按钮图标：103 个 16×16 PNG 由 `tools\Make-Icons.ps1` 生成并内嵌（`icons.<id>.png`；
  用户自建按钮的图标**不**生成，免得把别人机器上的东西提交进仓库），
  全部经 `IconFactory.Normalize()` 归一化成 16×15 画布（见"界面硬规则"里那条）。
  优先级：清单里的 `icon` > `assets\icons\<id>.png` > 内嵌 > 程序内实时画的占位图标。
- ✅ **外部工具目录 `bin-tools\` 已实现**（用户定的名字）：查找顺序里加一档、`build.ps1 -Package` 自动拷入、
  设置 / 关于窗口有「打开工具目录」、`kind: exe` 的相对路径按「工具箱目录 → `bin-tools\`」解析。
  ⬜ 还没做：把工具内嵌进 exe 当兜底、`bin-tools\<工具>\tool.json` 自动扫按钮（`docs\DESIGN.md` §13 的 ② ③）。
- ⬜ P1：剩下的都做完了，只有「Win10 / Win11 资源管理器」两个继续灰着（含义待用户定）、按钮排序 / 隐藏 / 固定到常用、多步 `macro`。

## 结构

```
D:\萌新工具开发\toolbox\
  build.ps1                        一键编译（系统自带 csc.exe，不需要 .NET SDK）
  bin\Mxx1Toolbox.exe              交付物：单文件 GUI+CLI（不入仓）
  src\Program.cs                   CLI 入口（list / run [--dry] / draft / status / checkupdate / help）
  src\MainForm.cs                  主窗口：页签 + 四列网格 + 底栏 + 搜索 + 日志面板 + 键盘
  src\ToolButton.cs                紧凑按钮（Flat + 主题配色 + 16×15 图标画布 + 灰色占位 + Flash/SetBusy）
  src\IconFactory.cs               图标：有 PNG 用 PNG，没有就实时画；一律 Normalize 成 16×15；GetMuted 出灰版
  src\ToolItem.cs / ToolRegistry.cs 按钮模型 + 读内嵌 tools\*.json + 用户层 tools.json（只认 tools 数组）
  src\Launcher.cs                  按 kind 启动；SystemTargets 表（26 个系统工具）；找隔壁 exe / bin-tools；
                                   `LaunchElevatedCopy`（自己提权再起一遍 = 静默、不弹黑窗口）；UTF-8 输出
  src\RegEngine.cs                 "记原值 → 写入 → 读回核对 → 一键还原"的唯一实现（Privacy 与 SysReg 共用）
  src\SysReg.cs                    6 组系统设置开关（12 个按钮）+ 查看/还原改动 + selftest（TSV 记录）
  src\ConfirmForm.cs               自家的确认窗口「请确认」（不再用 MessageBox 甩命令，见 §"运行反馈"）
  src\UserTools.cs                 用户层 tools.json 的读写（最小 JSON writer，写入前备份 .bak）
  src\NewToolForm.cs               图形化「新建按钮 / 编辑按钮」窗口（4 种类型）
  src\LinksForm.cs                 「常用链接」窗口（项目主页/仓库/几个 ms-settings 入口）
  src\Json.cs                      自带的小 JSON 解析器（不依赖 Newtonsoft / System.Web）
  src\Theme.cs / Native.cs         浅深主题配色（含灰色占位三色）+ DWM 深色标题栏 / 滚动条
  src\Settings.cs / Logger.cs / AppPaths.cs
  src\AboutForm.cs                 署名、站点、仓库、许可证常量的唯一来源
  src\LogForm.cs / OutputForm.cs   程序内日志窗口（最新在最上）/ 命令输出窗口
  src\SettingsForm.cs              设置窗口
  tools\*.json                     105 个按钮的内置定义（编译时内嵌，资源名 tools.<文件名>）；7 个文件 =
                                 common / system / privacy / apps / cleanup / rightmenu / mine
  tools\Test-Encoding.ps1          编码红线体检（-Fix 修 BOM）
  tools\Make-Screenshots.ps1       拍 docs\gui-shot.png / dark-shot.png / system-shot.png（PrintWindow）
  tools\Make-Icons.ps1             批量画图标（先从 exe 的 list 读清单，所以**先 build 再跑它**）
  assets\icons\*.png               105 个图标（编译时内嵌成 icons.<id>.png）
  tests\Test-All.ps1               一条命令跑完全部
  tools\Test-InlineSyntax.ps1      内联脚本语法 + 清单 JSON 体检（46 个脚本 / 7 个清单）
tests\Test-Cli.ps1               命令行回归 96 项（L 组 12 项盯 sysreg：成对 / 只读 / 无写入口 / 自检 / 合规底线）
  tests\Test-Gui.ps1               界面回归 91 项（要交互式桌面，无桌面返回 3 = 跳过；H 组盯固定尺寸、E04b 盯窗口位置）
  docs\DESIGN.md                   设计正本（含"踩过的坑"清单 + §13 打包方案）
  docs\gui-shot.png / dark-shot.png / system-shot.png  界面截图
```

## 命令

```powershell
powershell -File build.ps1                    # 编译 → bin\Mxx1Toolbox.exe
powershell -File build.ps1 -Package            # 额外打 zip，并把隔壁 exe 拷进 bin-tools\
powershell -File tools\Test-Encoding.ps1      # 编码体检（改完文件必跑；-Fix 修 BOM）
powershell -File tests\Test-All.ps1           # 全套（无桌面加 -SkipGui）
powershell -File tests\Test-Cli.ps1           # 命令行回归
powershell -File tests\Test-Gui.ps1           # 界面回归（要交互式桌面）
powershell -File tools\Make-Screenshots.ps1   # 重拍文档截图（浅色 / 深色 / 系统工具页签）
powershell -File tools\Make-Icons.ps1         # 重生成 PNG 图标（先 build 再跑，改完还要再 build）

& bin\Mxx1Toolbox.exe list [--tab system]     # 列按钮（tab 分隔：id / 页签 / 名称 / 类型）
& bin\Mxx1Toolbox.exe run devmgmt --dry       # 只解析按钮指向哪里，不真的启动（灰按钮回 kind=none）
& bin\Mxx1Toolbox.exe run permdel.gui         # 跑一个按钮（和界面同一条路径）
& bin\Mxx1Toolbox.exe status                  # key=value（含 systemTargets / systemMissing / admin / pinned）
& bin\Mxx1Toolbox.exe tip [id]                # 打印按钮的悬停说明（界面交给 ToolTip 的就是这一串）
& bin\Mxx1Toolbox.exe privacy status          # 只读列隐私开关状态；selftest 自检「原值→写入→还原」
& bin\Mxx1Toolbox.exe sysreg  status          # 只读列 6 组系统设置开关的现状；items / selftest 同上
& bin\Mxx1Toolbox.exe tip <id>                # 打印按钮的悬停说明（界面交给 ToolTip 的就是这一串）
& bin\Mxx1Toolbox.exe pin / unpin <id>        # 置顶 / 取消置顶（pinned.txt）
& bin\Mxx1Toolbox.exe export / import <文件>  # 导出 / 导入「我的工具」
```

## 「我的工具」：图形化加按钮（已实现）

| 入口 | 做什么 |
| --- | --- |
| `[+ 新建按钮]` 或 `Ctrl+N` | 打开 `NewToolForm`：名称 / 类型 / 路径或目标 / 参数 / 说明 / 页签 / 需要管理员 / 危险按钮 |
| 拖**一个** exe / 脚本 / 文件夹进窗口 | 打开同一个窗口并预填（可以改名再存） |
| 一次拖**多个**文件 | 直接按文件名建好 |
| 右键按钮 → 编辑按钮… / 删除按钮 | **只对用户层**（`ToolItem.UserLayer`）开放；删除前二次确认 |

- 四种类型 = `exe` / `open` / `script`+文件 / `script`+一行命令；存进
  `%LOCALAPPDATA%\mxx1-toolbox\tools.json`（`src\UserTools.cs` 负责读写，**写入前备份 `.bak`**）。
- 存完 `ReloadAfterUserEdit()`：重载清单 → 重算列宽（**只加宽不缩窄**）→ 重建网格。
- 用户层的 id 由名字生成（`mine.xxx`，重名自动加序号）；`ToolItem.UserLayer` 由 `ToolRegistry` 标记。

## 外部工具目录 `bin-tools\`

- 位置：**工具箱 exe 旁边**的 `bin-tools\`（`AppPaths.PayloadDir`）；`%LOCALAPPDATA%\mxx1-toolbox\bin-tools\`
  是备用（exe 在只读目录时用，将来内嵌兜底也释放到那）。
- 查找顺序：设置里指定的路径 → `bin-tools\<文件名>` → exe 同目录同名文件 → 用户目录 `bin-tools\` →
  向上三层找隔壁仓库 `permanent-delete-menu\bin\`（开发用）→ `%LOCALAPPDATA%\PermanentDelete\`。
- **相对路径按「工具箱目录 → `bin-tools\`」解析**（`AppPaths.Resolve`），所以清单里只写文件名就行；
  `icon` 字段和 `open` 的 target 走同一条路。
- 入口：设置窗口 + 关于窗口的「打开工具目录」（没有就自动建）；找不到工具时的提示里直接写出这个路径。
- 打包：`build.ps1 -Package` 会把隔壁的 `PermanentDeleteSetup.exe` 拷进发布目录的 `bin-tools\`
  （找不到就留空并打印一行说明，不报错）。
## 界面硬规则（违反就是"看着像 bug"那类问题）

| 规则 | 违反后的症状 |
| --- | --- |
| 网格列宽**运行时测量**（`ComputeCellWidth`：最长按钮名 + 44px 图标余量，钳 104…170），**再用 `ClampCellWidthToWindow()` 卡在窗口宽度允许的上限内** | 拍脑袋定 108px 时名字会被渲染成「关闭实时防护与…」。当前最长名 103px → 列宽 147、按钮 141×30 |
| **窗口默认固定尺寸**：高度 620、宽度 = 4 × 列宽 + 24（最窄 520），宽度**第一次量出来就写进 `settings.ini` 的 `WindowWidth`**，之后加多长的按钮名都不改宽度 | 只禁用 AutoSize 不够：列宽按最长名字量，用户加一个 32 字的按钮 → 下次开机窗口 628 → 720（还是"跟着内容变"）。判据：Test-Gui 的 H03/H04/H05 |
| **名字比按钮宽就出省略号**（`ToolButton.AutoEllipsis`），全名在悬停提示里 | 强行加宽按钮 = 撑宽窗口 + 把同排别的按钮挤出去 |
| **跑完必须有反馈**：① 页签下面一条绿/红结果条（8 秒，点它看日志）② 有输出就开结果窗口，标题写「成功/失败（用时 X 秒）」③ 只有"失败且无输出"才弹消息框；运行中底栏写「运行中（已 X 秒）」 | 用户原话「点击确认以后也没有成功或者失败的反馈」——点了按钮完全不知道成没成 |
| **需要确认的按钮用自家的 `ConfirmForm`，不要用 `MessageBox`** | `MessageBox` 只能塞纯文本，内联脚本按钮会摊出 700 多字的命令（顶出屏幕、也没人看得懂）。用户原话「点击按钮后弹出的确认执行的提示没做好」 |
| **已经提权就不要再 `runas`**（`Launcher`：`if (elevate && IsAdmin()) elevate = false;`），脚本按钮要提权走 `LaunchElevatedCopy` | `runas` 一定会带一个控制台 → 每次点按钮都蹦一个 PowerShell 黑窗口（用户原话「每次都会弹出 powershell 体验不好」） |
| `Dock=Top` 的 `TableLayoutPanel` **末尾要加一个 100% 空列** | 多余宽度全被塞给最后一列 → 每行第 4 个按钮比同排宽 28px |
| `TableLayoutPanel` 的每一行都要显式 `RowStyles`（要填满就 `Percent 100`） | 行按内容 AutoSize → 底栏按钮 30px 挤在 24px 条里，下边缘被裁 5px |
| 底栏按钮 `AutoSize=false`，**高度 = 文字行高 + 8**（`MeasureText("国").Height + 8` = 24px），底栏高 = 按钮高 + 4。那个 `+8` 是 Flat 按钮的 1px 边框 + 约 3px 内边距 ×2，**不是**随手留的余量 | 写死 20px → 文字下半截被裁（用户："右下角按钮没正常显示、被挡住"）；按 `行高 + 6` = 22px **还是差一行**（用户："底部按钮还是差一点的才显示全文字，主要是高度问题"）——22px 只给文字 14px，实测底栏「检查更新」只剩 9 行墨迹 |
| 图标一律经 `IconFactory.Normalize()` 变成 **16×15 画布**（首行整行透明就砍掉首行，否则取 0..14 行），PNG / 实时绘制 / 转圈 / 灰版都走同一条路 | WinForms 把图片画在文字行框中心**往下 1px** → 16px 画布比按钮中心低 1.5px（实测图标墨迹 9..23、中心 16.0，按钮中心 14.5），用户看到"图标没有上下居中"。**用 `Padding` 调没用**：它把图标和文字一起挪（每 1px 底边距抬 1px） |
| **`placeholder: true` 的按钮是灰的、而且禁止点击**：`Enabled=false` + 灰底灰字（`Theme.Placeholder*`）+ **置灰图标**（`IconFactory.GetMuted`：Rec.601 亮度再往白里混 45%）。禁用控件没有 tooltip，所以状态栏带「灰色 N 个没接功能」 | 用户先说"没做功能的按钮默认灰色"，又说"应该是禁止点击的"。实测：真按钮文字最暗 **26**、灰按钮 **77**（禁用控件画字会描 1px 深影）→ 判据"真 ≤ 80、灰 ≥ 60、差 ≥ 30" |
| **按钮运行中不许改文字**：禁用 + 换成同一个画布的转圈图标（`IconFactory.Busy()`）即可 | 追加 "…" 会让"图标+文字"整组重新居中，每点一次图标横跳；长名字还会溢出被截 |
| **"灰"不等于"点了没反应"**：界面动作（关于 / 日志 / 设置 / 新建按钮 / 常用链接）要在占位判断**之前**处理 | 「+ 新建按钮」是灰的，但点它必须还能弹出"怎么手动加按钮"的说明 |
| 状态栏文字**只放短摘要**（`N 个按钮 · 本页 M · 名字 · 完成`），完整内容进日志 + 悬停提示 | 标签宽度固定，长句（旧格式实测 414px vs 392px）尾巴被截 |
| 一律 `TableLayoutPanel` / 排版函数，**绝不手写坐标**；`Label` 绝不与按钮重叠 | 缩放/DPI 一变就错位；标签会吃掉鼠标点击，按钮"点了没反应" |
| 页签用一排 `Flat` 按钮，**不用 `TabControl`** | `TabControl` 深色主题下不可控（白底标签刺眼） |
| `MinimizeBox=false` + `MaximizeBox=false` | 标题栏多一个**灰掉的**最大化方框，点了没反应 |
| 界面文字不用 `✓ ⚠ →`（微软雅黑没字形）；图标走 PNG 或程序内绘制 | 渲染成空白 |
| 日志显示**最新在最上面**，不给正序开关 | 想看刚点的那次结果得滚到底 |
| DPI：清单里 `dpiAware=true`（System aware）+ `AutoScaleMode.Font` | 不要用 PerMonitorV2：它需要 app.config，而 exe.config 会破坏单文件 |
| **非模态窗口（结果 / 日志 / 常用链接）必须手工居中**：`StartPosition = CenterParent` **只对 `ShowDialog()` 的模态窗口有效**，`Show()` 出来的窗口 Windows 会放在屏幕左上角 → 用 `WindowPlacement.ShowCentered(f, owner)`（对 owner 居中 + 夹进工作区 + 多开时错开 28px） | 用户报的「点击激活状态为什么会弹到左上角窗口」。判据：Test-Gui 的 E04b，比的是结果窗口与主窗口的**重叠比例**（要 ≥ 50%，居中时实测 99%） |

深色不需要自绘控件：`FlatStyle=Flat` + `FlatAppearance.BorderColor / MouseOverBackColor / MouseDownBackColor`
+ `BackColor` / `ForeColor` 逐主题赋值即可；标题栏 `DwmSetWindowAttribute(hwnd, 20 → 19, dark)`，
滚动条 `SetWindowTheme(hwnd, "DarkMode_Explorer")`。跟随系统读 `AppsUseLightTheme` + `UserPreferenceChanged`。

## 加一个按钮

1. 用户层：写 `%LOCALAPPDATA%\mxx1-toolbox\tools.json`（同 `id` 覆盖内置，升级不冲掉，**不用重编译**）；
2. 内置层：改 `tools\*.json` 后**必须重新编译**才会内嵌进 exe（`build.ps1` 编译后会自检 5 个资源名）；
3. 字段：`id / tab / segment / order / name / kind` + 对应类型的字段；
   `tab` 可写 id（`common`/`rightmenu`/`cleanup`/`system`/`mine`）或中文页签名；`danger: true` 变深红 + 二次确认；
   `placeholder: true` = 只做按钮不接功能（**界面里就是灰的**，点击 = 状态栏 + 日志 + 灰 0.6 秒）；
4. 类型：`builtin`（`module`+`action`）/ `exe`（`path`+`args`）/ `script`（`shell`+`inline` 或 `path`）/
   `open`（`target`，支持 `shell:startup`、`ms-settings:`、`https://`）；`macro` 在 P1；
   `builtin` 的三个 module：`permdel`（隔壁安装器）、`system`（12 个 Windows 组件）、`app`（本程序界面动作）；
5. 编码 **UTF-8 无 BOM**；`%ProgramFiles%` 这类占位符会展开；
6. 改完跑 `tests\Test-All.ps1`；界面有变化补 `tests\Test-Gui.ps1`。
7. **加/删按钮后图标要跟着走**：先 `build.ps1`（`Make-Icons.ps1` 从 exe 的 `list` 读清单）→
   `tools\Make-Icons.ps1` → 删掉不在清单里的孤儿 PNG → 再 `build.ps1`。

## 「隐私设置」怎么点（29 个按钮 = 11 组成对开关 + 4 个权限入口 + 状态 / 优化 / 还原）

后端 `src\Privacy.cs`：一张「开关 → 注册表值」的表（一个开关可能对应好几个值，「系统广告」一组
就是 8 个）。三条底线：① **只碰隐私 / 广告 / 遥测开关**，Defender / 防火墙 / UAC / SmartScreen
一概不碰（测试 `I05` 盯着）；② **写之前先把原值记进 `privacy-original.tsv`**，而且只记第一次；
③ **写完读回核对**。需要写 HKLM 的开关走「把自己以管理员身份再起一遍」（`run <id> --admin`），
结果是 winexe 没有控制台，所以写进 `last-elevated-result.txt`、父进程过几秒读出来弹窗口。

## 「常用设置」怎么点（33 个；其中 12 个注册表开关都能一键还原）

三段：段 1 = 6 组「关 X / 开 X」成对开关，段 2 = 安全入口 / 电源 / 系统维护
（**只打开官方界面，绝不代关系统防线**），段 3 = 改动的记录与还原。

- 6 组注册表开关（走 `src\SysReg.cs` + `src\RegEngine.cs`，**和隐私设置共用同一份引擎**）：
  `taskbar-combine`（`TaskbarGlomLevel` 2 / 0，Win11 已取消这个开关 → `MaxBuild 22000`）、
  `startmenu-align`（`TaskbarAl` 0 / 1）、`driver-install`（HKLM `SearchOrderConfig` 1 / 0）、
  `core-isolation`（HKLM HVCI `Enabled` 1 / 0，重启后生效）、
  `explorer-classic`（3 个 CLSID 键树，Win11 专属）、`ctxmenu-classic`（`{86ca1aa0-…}\InprocServer32`）。
- 三条底线和隐私设置一样：**写之前记原值**（`sysreg-original.tsv`，只记第一次）→ 写入 → **读回核对**
  → 「还原设置改动」按记录逐条写回（记录里"原来整个键都不存在"的，还原时连键一起删掉）。
  **不碰 Defender / 防火墙 / UAC / SmartScreen / 实时防护 / 篡改保护**（Test-Cli 的 L11 盯着）。
- **命令行故意没有写入口**：只有 `sysreg status` / `items` / `selftest` 三个只读（自检在临时键里
  走完"记原值 → 写入 → 读回 → 还原"），L07 盯着这条。
- 加一个开关：`SysReg.cs` 的表里加一行 + `tools\common.json` 里加两条同 `options` 的按钮（on / off）
  + 两个图标（`tools\Make-Icons.ps1`），然后 `build.ps1` → `Make-Icons.ps1` → `build.ps1`。

## 「应用管理」怎么点（5 个，只读或单个操作）

查看已安装应用 / 查看启动项（都是只读脚本）/ 默认应用 / 应用和功能（`ms-settings:`）/
卸载单个应用（图形化挑一个 + 二次确认 + `Remove-AppxPackage`，只影响当前用户）。
**故意不做**批量卸载、卸载 Edge、卸载 Xbox/天气/邮件/地图 —— `J06`–`J08` 盯着这条底线。

## 「系统工具」怎么点（26 个真功能，含 12 个 Windows 组件 + 13 个修复诊断 + 系统体检）

目标表在 `src\Launcher.cs` 的 `SystemTargets`，**只写占位符路径**（`%SystemRoot%\System32\devmgmt.msc`），
运行时展开 —— 代码里不出现本机绝对路径（编码体检会拦）。三种目标：

| 目标形态 | 怎么启动 |
| --- | --- |
| 文件（`.msc` / `.cpl` / `.exe`） | `Process.Start` + `UseShellExecute=true`，Windows 自己决定宿主（mmc / 控制面板） |
| `shell:` 目录（设备和打印机、控制面板） | 走 `explorer.exe <shell:...>`（直接起会闪控制台窗口） |
| `ms-settings:` 页面（声音设置） | 直接交给系统 |
| 本程序内的窗口（常用链接） | 返回 `LaunchResult.UiAction`，由 `MainForm` 开 `LinksForm` |

- **组件不在就给整句说明**（`SystemTarget.Missing`），比如家庭版没有 `gpedit.msc`；
  绝不静默失灵。`status` 里的 `systemTargets=` / `systemMissing=` 就是数这个。
- 全是只读查看：**不加 `danger` / `runAsAdmin` / 二次确认**。
- 想加一个系统工具：在 `SystemTargets` 数组里加一行 + `tools\system.json` 里加一条同 `action` 的按钮。

## 「右键增强」怎么接现成 exe（零改动集成）

只剩一个按钮：**「永久删除工具」`permdel.gui`** = 不带参数启动隔壁 exe = 开它自己的窗口
（安装 / 卸载 / 查看状态 / 测试一下 / 使用条款 / 引擎日志 / 版本与更新 / 关于作者都在那个窗口里）。
工具箱**不重新实现**任何删除逻辑，也不重复它那套界面。

- 找 exe 的顺序：`settings.PermanentDeleteExe` → 环境变量 `MXX1_PERMDEL_EXE` → 工具箱同目录 →
  向上三层找 `permanent-delete-menu\bin\PermanentDeleteSetup.exe` → `%LOCALAPPDATA%\PermanentDelete\`。
  **代码里不许出现本机绝对路径**（编码体检会拦）。
- 想让它直接调 CLI（`install --quiet` / `status` / `verify` …）：把 `tools\rightmenu.json` 里
  `_disabled` 那 8 条搬回 `tools` 数组即可，`Launcher` 的那套分支都还在。
- 它是 **GUI 子系统**程序：直接 `&` 调用拿不到输出，要用 `ProcessStartInfo` 重定向，并且
  **边跑边读**（`ReadToEndAsync`）；先 `WaitForExit` 再 `ReadToEnd` 在输出 >4KB 时会父子互等。
- 隔壁装进右键菜单后，引擎脚本部署在 `%LOCALAPPDATA%\PermanentDelete\`（注册表 verb 是
  `wscript.exe "<...>\PermanentDelete.vbs" %V`）—— **菜单不依赖这个 exe 在哪**，
  所以以后就算把 exe 内嵌/释放到别处，也不会把已装好的菜单搞坏。

## 编码红线（踩过两次，能静默毁功能）

- `.cs` / `.ps1` 必须 **UTF-8 带 BOM**（csc 与 PS 5.1 都按 ANSI 解码无 BOM 的文件）；
  `.md` / `.yml` / `.json` **无 BOM**。`build.ps1` 会给 `src\*.cs` / `tests\*.ps1` / `tools\*.ps1` 自动补 BOM，
  并去掉 `tools\*.json` 的 BOM；提交前跑 `tools\Test-Encoding.ps1`（`-Fix` 可修）。
- **没有 BOM 的临时脚本 + 中文注释 = PS 5.1 按 GBK 读，注释可能吞掉换行**（本仓库踩过：
  补丁脚本解析报一堆莫名其妙的错）。写一次性脚本要么纯 ASCII 注释，要么用 `pwsh`（7）跑。
- **C# 里别用 `File` / `Shell` / `Url` 这种方法名**：它们会盖住 `System.IO.File`，
  于是类里每个 `File.Exists` 都编译不过（CS0119）。用 `FileTarget` / `ShellTarget` / `UrlTarget`。

## 测试怎么用

- `tests\Test-Gui.ps1` 用 Win32 探针，不看截图：`EnumChildWindows` + `GetWindowRect` 判重叠
  （先排除"完整包住别人"的容器）、`GetWindowLong(GWL_STYLE)` 判标题栏、
  `PostMessage(BM_CLICK)` 真点按钮、`WM_GETTEXT` 跨进程读文字。
  按钮清单**从 `Mxx1Toolbox.exe list` 读**，两边必须一致（别在测试里写死名单）。
- `tests\Test-Cli.ps1` 里 `run <id> --dry` 会把 12 个系统工具全解析一遍（不许真的开 12 个窗口），
  并且直接问隔壁 `PermanentDeleteSetup.exe status` 拿 `installed=` —— "找得到 + 真能跑"都证明一次。
- 容易写错的断言（本仓库都踩过）：
  1. **灰按钮是禁用的**（`Enabled=false`）：要断言"点不动"就读 `IsWindowEnabled`，
     **别用 `PostMessage(BM_CLICK)` 证明** —— 直接投递的消息不一定被禁用状态挡住（踩过，见 DESIGN §12 坑 18）；
  2. WinForms `Label` 是有窗口句柄的（`STATIC` 类），所以"标签压按钮"能被枚举出来；
  3. 读 `status` 的 `key=value` 时**别让行尾 `\r` 混进值里**（`([^\r\n]*)` + `Trim()`），
     否则 `Test-Path` 会报"路径含非法字符"；
  4. "按钮/文字被裁"这类问题**用 `GetParent` + 矩形包含**来判（D01b），别靠肉眼看截图；
  5. "文字超长"这类问题用**测量**来判（B06b / D01d / E07），别等用户截图来报；
  6. 验证"点击后有没有跳动"直接**读点击过程中的控件文字**（E05），别做像素 diff；
  7. **"文字被裁了没有 / 图标居中不居中 / 灰按钮真灰不灰"只有渲染结果能判**：
     `PrintWindow(PW_RENDERFULLCONTENT)` 抓像素 → `LockBits` 拿 `byte[]` →
     背景 = 内区出现最多的颜色 → 按行数墨迹判定。
     判据：图标是**亮而饱和**（`max-min > 60 && max > 140`）**或明显比底色暗**（`mx < bgMax - 45`）
     的色块（第二条是为了**灰图标**，只认第一条时灰图标会被当成背景，文字行数会算进图标 → 15 行）；
     文字是暗墨迹。灰度判定用"最暗墨迹"：**真按钮 26、灰按钮 77**（禁用控件画字会描 1px 深影，
     所以不再是纯灰的 138）→ 判据"真 ≤ 80、灰 ≥ 60、差 ≥ 30"。
  8. **判"有没有弹窗"要按窗口类 `#32770`**：WinForms 的 `ToolTip` 也是顶层窗口
     （`tooltips_class32`、标题空），按"除主窗口外的可见窗口"判会把 tooltip 当成弹窗；
  9. **`function f { return @(单个对象) }` 会被解包成单值**，而单个 `PSCustomObject` **没有 `.Count`**，
     所以 `$x.Count -gt 0` 恒为 False —— 调用处再包一层 `@(f ...)`；
  10. **界面回归不能依赖用户的 `settings.ini`**：一开头先写一份已知设置（浅色 / 单击 / 二次确认开 /
      日志面板关），跑完在"现场复原"里按原样写回。用户开着日志面板时 B08 会假红（踩过一次）。
  11. **跨进程往输入框填字要用 `WM_SETTEXT`，而且 `DllImport` 必须写 `ExactSpelling = true`**：
      `CharSet = CharSet.Unicode` 会把 `W` 追加到**入口点**上（不只是方法名），写法不对就抛
      `EntryPointNotFoundException`。填完**一定要回读**（`[TBGui]::Text`）再往下走 ——
      填不进去就点不动「创建按钮」，**模态窗口一直开着会把主窗口压成禁用**，后面每一组检查全部连带失败，
      看着像"测试卡死"（2026-10-04 真卡了一次）。
      开过模态窗口的测试，**每条分支出口都要关掉它**：`Close-StrayDialogs` 收尾 + C16 专门盯残留。
  12. **测试对用户真实数据是"临时占用"**：为了数按钮数会把用户的 `tools.json` 改名成 `.paused-by-*`。
      中途被 Ctrl+C / 卡死，这个"放回来"就永远不执行 → 用户看到"我建的按钮没了"。
      所以**开工先自愈**（发现 `.paused-by-*` 在而正式文件不在，先搬回去），
      `settings.ini` 另留一份 `.before-test` 备份，收尾成功才删。
  13. **函数定义必须放在第一次调用之前**：PowerShell 边解析边执行，`Switch-Tab` 定义在第 735 行、
      第 566 行就调用 → `CommandNotFoundException` 当场终止脚本，**收尾那段"把用户 tools.json 放回来"
      根本没跑**（2026-10-04 真发生：用户的按钮文件被留在 `.paused-by-gui-test` 状态）。
      除了把定义提前，还要在脚本开头挂**脚本级 `trap`** 调 `Restore-UserLayer`（复原设置 / 用户
      `tools.json` / 杀掉 `$script:Procs` 里自己拉起来的界面进程）**只杀自己起的进程**。
  14. **改 `settings.ini` 之前必须先关掉那个正在跑的窗口**：窗口关闭时会把**当前停留的页签**写回
      `LastTab`，先写设置再关窗口 = 被覆盖 → 下一个实例开在别的页签上，后面一组检查全假红
      （深色组 G03/G05 就是这么红的）。顺序：**先关窗口 → 再写设置 → 再 Start-Gui**。
  15. **改完清单要"严格"校验 JSON**：`Json.cs` 严格解析，多一个逗号整份清单被丢掉（105 → 73），
      而 PS 7 的 `ConvertFrom-Json` 对尾随逗号很宽容、验不出来。用
      `[System.Text.Json.JsonDocument]::Parse(...)` 或直接 `Mxx1Toolbox.exe list --tab <页签>` 数一遍。
  16. **H 组注入长名字按钮时，要把原来的占位按钮一起留着**：`$commonNames` 是带着占位按钮读出来的，
      换掉它会让 H05 数按钮数少一个而假红。
- **别在 PowerShell 里按像素调函数**：一个 `Get-Pixel` 每像素调一次，几万次调用要几分钟，
  看起来像卡死（踩过一次）。要么 `LockBits` 取一次 `byte[]` 再纯数组循环（`Get-InkRows` 的写法），
  要么用 csc 编个临时小工具（`local\InkDiag.cs` 那种）。
- **改完任何 `.ps1` / `.cs` 都回头看一眼 BOM**：`edit` 类工具会静默吃掉 BOM，而 PowerShell 5.1
  读无 BOM 的 `.ps1` 按 GBK 解 → 中文注释变成语法错误，报错位置还完全不相干。
  改完跑 `tools\Test-Encoding.ps1 -Fix`，再 `build.ps1`。
- **重编时如果 `bin\Mxx1Toolbox.exe` 被占用，不要杀进程**：`build.ps1` 会自动把旧 exe 改名成
  `Mxx1Toolbox.exe.old-<时分秒>` 再编（用户可能正开着界面在用）。

## 待办 / 别自己替他决定

1. **Git：本地仓库已经建好**；**GitHub 远程仓库还没建**，推送必须先问用户
   （用户原话："推送的时候不要每次都推送，太卡了要问过我才行"）。
2. **外部工具目录 `bin-tools\`**：用户 2026-10-04 选了这个方案并定了名字，**已经实现**（见 `docs\DESIGN.md` §13）。
   还没做的只有两层：② 把 `bin-tools\` 里的 exe 内嵌进 exe、首次点击释放到固定目录当兜底；
   ③ 扫 `bin-tools\<工具>\tool.json` 自动长出按钮。**这两层等用户说要再做，别自己开工。**
3. P1 的范围（先接哪个页签的真功能）要问用户，别自己挑。
4. 图标要改样式就动 `tools\Make-Icons.ps1` 的关键词映射 / 配色，然后按
   `build.ps1 → Make-Icons.ps1 → 删孤儿 → build.ps1` 的顺序跑。
5. skill 三处同步：`D:\萌新工具开发\.dsh\skills\mxx1-toolbox\SKILL.md`、
   仓库内 `toolbox\skill\mxx1-toolbox\SKILL.md`（两份必须**字节一致**，用哈希核对）、
   `%USERPROFILE%\.dsh\skills\mxx1-toolbox\SKILL.md`（**尚未建**）。
