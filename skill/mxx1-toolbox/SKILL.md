---
name: mxx1-toolbox
description: Use when working on "萌新工具箱 / mxx1 Toolbox" — the Windows button-wall launcher whose main window is a multi-row, multi-column grid of small buttons that each start another program, script, or feature. Covers its interface rules (four column compact buttons, top tabs, separator segments, light/dark theme, computed column width), the tools\*.json button registry, how buttons launch things (exe / script / open / builtin / macro), how the "永久删除（不进回收站）" right-click tool is wired in through PermanentDeleteSetup.exe, plus build.ps1, screenshot tool and the GUI/CLI test suites.
---

# 萌新工具箱（mxx1 Toolbox）

**主界面 = 多行多列的小按钮墙**，点一下按钮就启动一个已经做好的程序 / 脚本 / 功能。
加按钮只是往 `tools\*.json` 丢配置，**不需要重新编译主程序**。

- 程序名 **萌新工具箱**，标题栏 `萌新工具箱 v1.0.0`；署名 `mxx1` / `mxx1.cn`；GPL-3.0-or-later
- 工程目录 **`D:\萌新工具开发\toolbox\`**，与隔壁 `permanent-delete-menu` **互不修改**（只调它的 exe）
- 外观参考：`C:\Users\Administrator\Pictures\Snipaste_2026-10-04_10-29-34.png`（那种紧凑按钮墙）

> **接手 / 新会话先做两件事**：读 `docs\DESIGN.md`（外观与行为的**唯一正本**）和本文件。
> 设计一改先改 `DESIGN.md`，再同步本 skill —— 两份分叉就会出现"两套行为"。

## 当前状态（2026-10-04）

- ✅ **P0 已实现，测试 65 项全绿**：命令行回归 26 + 界面回归 39（外加编码体检）。
  产物 `bin\Mxx1Toolbox.exe`（108,544 字节单文件），五个 `tools.*.json` + 61 个 `icons.*.png` 已内嵌。
- ✅ 「右键增强」8 个按钮是**真功能**，实测能读到隔壁引擎的 `installed=` / `fileVisible=`；
  其余 52 个按钮是 `placeholder`（点击 = 状态栏提示 + 日志 + 灰 0.6 秒，不弹窗、不改系统）。
- ⬜ **Git 仓库还没建**（本地 + GitHub 远程 `mxx1-toolbox` 都还没做）；**推送前必须问用户**。
- ✅ 按钮图标：61 个 16×16 PNG 由 `tools\Make-Icons.ps1` 生成并内嵌（`icons.<id>.png`）。
  优先级：清单里的 `icon` > `assets\icons\<id>.png` > 内嵌 > 程序内实时画的占位图标。
- ⬜ P1：其它页签接真功能、图形化「新建按钮」、拖拽新增、编辑/排序、多步 `macro`。

## 结构

```
D:\萌新工具开发\toolbox\
  build.ps1                        一键编译（系统自带 csc.exe，不需要 .NET SDK）
  bin\Mxx1Toolbox.exe              交付物：单文件 GUI+CLI（不入仓）
  src\Program.cs                   CLI 入口（list / run / status / checkupdate / help）
  src\MainForm.cs                  主窗口：页签 + 四列网格 + 底栏 + 搜索 + 日志面板 + 键盘
  src\ToolButton.cs                紧凑按钮（Flat + 主题配色 + 16×16 图标 + 600ms 灰显）
  src\IconFactory.cs               图标：有 PNG 用 PNG，没有就实时画
  src\ToolItem.cs / ToolRegistry.cs 按钮模型 + 读内嵌 tools\*.json + 用户层 tools.json
  src\Launcher.cs                  按 kind 启动；找隔壁 PermanentDeleteSetup.exe；UTF-8 输出
  src\Json.cs                      自带的小 JSON 解析器（不依赖 Newtonsoft / System.Web）
  src\Theme.cs / Native.cs         浅深主题配色 + DWM 深色标题栏 / 深色滚动条 / 控制台附加
  src\Settings.cs / Logger.cs / AppPaths.cs
  src\AboutForm.cs                 署名与许可证常量的唯一来源
  src\LogForm.cs / OutputForm.cs   程序内日志窗口（最新在最上）/ 命令输出窗口
  src\SettingsForm.cs              设置窗口
  tools\*.json                     61 个按钮的内置定义（编译时内嵌，资源名 tools.<文件名>）
  tools\Test-Encoding.ps1          编码红线体检（-Fix 修 BOM）
  tools\Make-Screenshots.ps1       拍 docs\gui-shot.png / dark-shot.png（PrintWindow）
  tools\Make-Icons.ps1             批量画 61 个 16x16 PNG 图标（页签配色 + 名字关键词选图形）
  assets\icons\*.png               61 个图标（编译时内嵌成 icons.<id>.png）
  tests\Test-All.ps1               一条命令跑完全部
  tests\Test-Cli.ps1               命令行回归 26 项
  tests\Test-Gui.ps1               界面回归 39 项（要交互式桌面，无桌面返回 3 = 跳过）
  docs\DESIGN.md                   设计正本（含"踩过的坑"清单）
  docs\gui-shot.png / dark-shot.png 界面截图
```

## 命令

```powershell
powershell -File build.ps1                    # 编译 → bin\Mxx1Toolbox.exe
powershell -File tools\Test-Encoding.ps1      # 编码体检（改完文件必跑；-Fix 修 BOM）
powershell -File tests\Test-All.ps1           # 全套（无桌面加 -SkipGui）
powershell -File tests\Test-Cli.ps1           # 命令行回归
powershell -File tests\Test-Gui.ps1           # 界面回归（要交互式桌面）
powershell -File tools\Make-Screenshots.ps1   # 重拍文档截图
powershell -File tools\Make-Icons.ps1         # 重生成 61 个 PNG 图标（改完要重新编译）

& bin\Mxx1Toolbox.exe list [--tab rightmenu]  # 列按钮（tab 分隔：id / 页签 / 名称 / 类型）
& bin\Mxx1Toolbox.exe run permdel.status      # 跑一个按钮（和界面同一条路径）
& bin\Mxx1Toolbox.exe status                  # key=value 状态
```

## 界面硬规则（违反就是"看着像 bug"那类问题）

| 规则 | 违反后的症状 |
| --- | --- |
| 网格列宽**运行时测量**（`ComputeCellWidth`：最长按钮名 + 36px 图标余量 + 8px 间距，钳 104…170） | 拍脑袋定 108px 时「关闭实时防护与篡改」渲染成「关闭实时防护与…」 |
| `Dock=Top` 的 `TableLayoutPanel` **末尾要加一个 100% 空列** | 多余宽度全被塞给最后一列 → 每行第 4 个按钮比同排宽 28px |
| `TableLayoutPanel` 的每一行都要显式 `RowStyles`（要填满就 `Percent 100`） | 行按内容 AutoSize → 底栏按钮 30px 挤在 24px 条里，下边缘被裁 5px |
| 底栏按钮 `AutoSize=false`，**高度按字体算**（`MeasureText("国").Height + 6` = 22px），底栏高 = 按钮高 + 4 | 写死 20px 时 8.25pt 的文字下半截被裁 —— 用户看到"右下角按钮没正常显示、被挡住" |
| 一律 `TableLayoutPanel` / 排版函数，**绝不手写坐标**；`Label` 绝不与按钮重叠 | 缩放/DPI 一变就错位；标签会吃掉鼠标点击，按钮"点了没反应" |
| 页签用一排 `Flat` 按钮，**不用 `TabControl`** | `TabControl` 深色主题下不可控（白底标签刺眼） |
| `MinimizeBox=false` + `MaximizeBox=false` | 标题栏多一个**灰掉的**最大化方框，点了没反应 |
| 界面文字不用 `✓ ⚠ →`（微软雅黑没字形）；图标走 PNG 或程序内绘制 | 渲染成空白 |
| 日志显示**最新在最上面**，不给正序开关 | 想看刚点的那次结果得滚到底 |
| DPI：清单里 `dpiAware=true`（System aware）+ `AutoScaleMode.Font` | 不要用 PerMonitorV2：它需要 app.config，而 exe.config 会破坏单文件 |

深色不需要自绘控件：`FlatStyle=Flat` + `FlatAppearance.BorderColor / MouseOverBackColor / MouseDownBackColor`
+ `BackColor` / `ForeColor` 逐主题赋值即可；标题栏 `DwmSetWindowAttribute(hwnd, 20 → 19, dark)`，
滚动条 `SetWindowTheme(hwnd, "DarkMode_Explorer")`。跟随系统读 `AppsUseLightTheme` + `UserPreferenceChanged`。

## 加一个按钮

1. 用户层：写 `%LOCALAPPDATA%\mxx1-toolbox\tools.json`（同 `id` 覆盖内置，升级不冲掉，**不用重编译**）；
2. 内置层：改 `tools\*.json` 后**必须重新编译**才会内嵌进 exe（`build.ps1` 编译后会自检 5 个资源名）；
3. 字段：`id / tab / segment / order / name / kind` + 对应类型的字段；
   `tab` 可写 id（`common`/`rightmenu`/`cleanup`/`system`/`mine`）或中文页签名；`danger: true` 变深红 + 二次确认；
   `placeholder: true` 表示只做按钮不接功能（点击 = 状态栏 + 日志 + 灰 0.6 秒）；
4. 类型：`builtin`（`module`+`action`）/ `exe`（`path`+`args`）/ `script`（`shell`+`inline` 或 `path`）/
   `open`（`target`，支持 `shell:startup`、`ms-settings:`、`https://`）；`macro` 在 P1；
5. 编码 **UTF-8 无 BOM**；`%ProgramFiles%` 这类占位符会展开；
6. 改完跑 `tests\Test-All.ps1`；界面有变化补 `tests\Test-Gui.ps1`。

## 「右键增强」怎么接现成 exe（零改动集成）

工具箱**不重新实现删除逻辑**，只当遥控器调 `PermanentDeleteSetup.exe`：

| 按钮 | 命令 |
| --- | --- |
| 安装到右键 | `install --quiet`（二次确认 → UAC） |
| 卸载右键菜单 | `uninstall --quiet`（隔壁会先 `reg export` 备份） |
| 查看状态 | `status` |
| 测试一下 | `verify` |
| 使用条款 | `disclaimer` |
| 引擎日志 | 本程序内窗口读 `%LOCALAPPDATA%\PermanentDelete\delete.log`（**别用 notepad**） |
| 版本与更新 | `checkupdate` |
| 关于作者 | 本程序关于窗口 |

- 找 exe 的顺序：`settings.PermanentDeleteExe` → 环境变量 `MXX1_PERMDEL_EXE` → 工具箱同目录 →
  向上三层找 `permanent-delete-menu\bin\PermanentDeleteSetup.exe` → `%LOCALAPPDATA%\PermanentDelete\`。
  **代码里不许出现本机绝对路径**（编码体检会拦）。
- 它是 **GUI 子系统**程序：直接 `&` 调用拿不到输出，要用 `ProcessStartInfo` 重定向，并且
  **边跑边读**（`ReadToEndAsync`）；先 `WaitForExit` 再 `ReadToEnd` 在输出 >4KB 时会父子互等。
- 别把删除逻辑抄进工具箱：206 项测试、条款确认门、日志倒序都在隔壁仓库，抄一份必然分叉。

## 编码红线（踩过两次，能静默毁功能）

- `.cs` / `.ps1` 必须 **UTF-8 带 BOM**（csc 与 PS 5.1 都按 ANSI 解码无 BOM 的文件）；
  `.md` / `.yml` / `.json` **无 BOM**。`build.ps1` 会给 `src\*.cs` / `tests\*.ps1` / `tools\*.ps1` 自动补 BOM，
  并去掉 `tools\*.json` 的 BOM；提交前跑 `tools\Test-Encoding.ps1`（`-Fix` 可修）。
- **没有 BOM 的临时脚本 + 中文注释 = PS 5.1 按 GBK 读，注释可能吞掉换行**（本仓库踩过：
  补丁脚本解析报一堆莫名其妙的错）。写一次性脚本要么纯 ASCII 注释，要么用 `pwsh`（7）跑。

## 测试怎么用

- `tests\Test-Gui.ps1` 用 Win32 探针，不看截图：`EnumChildWindows` + `GetWindowRect` 判重叠
  （先排除"完整包住别人"的容器）、`GetWindowLong(GWL_STYLE)` 判标题栏、
  `PostMessage(BM_CLICK)` 真点按钮、`WM_GETTEXT` 跨进程读文字。
  按钮清单**从 `Mxx1Toolbox.exe list` 读**，两边必须一致（别在测试里写死名单）。
- 四组容易写错的断言：
  1. 占位按钮点完会灰 600ms，**要等它恢复**再断言"按钮可用"；
  2. WinForms `Label` 是有窗口句柄的（`STATIC` 类），所以"标签压按钮"能被枚举出来；
  3. 读 `status` 的 `key=value` 时**别让行尾 `\r` 混进值里**（`([^\r\n]*)` + `Trim()`），
     否则 `Test-Path` 会报"路径含非法字符"；
  4. "按钮/文字被裁"这类问题**用 `GetParent` + 矩形包含**来判（D01b）：
     子控件的矩形必须完全落在父容器里，别靠肉眼看截图（缩略图会骗人）。
- 界面回归会临时改 `settings.ini` 里的主题，**跑完必须按原样复原**。

## 待办 / 别自己替他决定

1. **Git 仓库还没建**：本地 `git init` + 首笔提交可以做；**GitHub 远程仓库名（建议 `mxx1-toolbox`）和推送都要先问用户**。
2. 图标已经生成好了；要改样式就动 `tools\Make-Icons.ps1` 里的关键词映射 / 配色，然后重跑它 + `build.ps1`。
3. P1 的范围（先接哪个页签的真功能）要问用户，别自己挑。
4. skill 三处同步：`D:\萌新工具开发\.dsh\skills\mxx1-toolbox\SKILL.md`（已建）、
   仓库内 `toolbox\skill\mxx1-toolbox\SKILL.md`（已建）、
   `%USERPROFILE%\.dsh\skills\mxx1-toolbox\SKILL.md`（**尚未建**）。
