---
name: mxx1-toolbox
description: Use when working on "萌新工具箱 / mxx1 Toolbox" — the Windows button-wall launcher whose main window is a multi-row, multi-column grid of small buttons that each start another program, script, or feature. Covers its interface rules (four column compact buttons, top tabs, separator segments, light/dark theme, computed column width, grey "not wired up yet" buttons), the tools\*.json button registry, bin-tools\ auto-loaded tool buttons (tool.json), how buttons launch things (exe / script / open / builtin / macro), how the 26 系统工具 buttons open Windows components, how the 常用设置 registry switches record the original value so every change can be undone, how a run reports success or failure, the first-run consent gate (免责声明与服务条款 fingerprint), the read-only update check, Windows 7/10/11 compatibility, plus build.ps1, screenshot tool and the GUI/CLI test suites.
---

# 萌新工具箱（mxx1 Toolbox）

**主界面 = 多行多列的小按钮墙**，点一下按钮就启动一个已经做好的程序 / 脚本 / 功能。
加按钮只是往 `tools\*.json` 丢配置，**不需要重新编译主程序**。

- 程序名 **萌新工具箱**，标题栏 `萌新工具箱 v1.5.5`；署名 `mxx1` / `mxx1.cn`；GPL-3.0-or-later
- 工程目录 **`D:\萌新工具开发\toolbox\`**，与隔壁 `permanent-delete-menu` **互不修改**（只调它的 exe）
- 支持范围 **Windows 7 SP1 / 10 / 11**（用户 2026-10-05 收窄的：**只考虑这三版**）；见「兼容性」一节
- 外观参考：`C:\Users\Administrator\Pictures\Snipaste_2026-10-04_10-29-34.png`（那种紧凑按钮墙）

> **接手 / 新会话先做两件事**：读 `docs\DESIGN.md`（外观与行为的**唯一正本**）和本文件。
> 设计一改先改 `DESIGN.md`，再同步本 skill —— 两份分叉就会出现"两套行为"。

## 当前状态（2026-10-09 晚补九，**未发布**）

> **⚠️ 有一批改动还没提交 / 没发版**：2026-10-09 用户要的「**右键菜单管理**」
> （原话：「工具箱做一个 右键菜单管理按钮 弹出一个窗口显示右键菜单的列表【要考虑多种情况下的
> 右键的文件列表、文件夹列表、桌面列表的右键展示】，功能暂定 禁用、恢复、删除。你整理一个文字
> 效果图发我看一下，使用最简单的方案」）。落地：`src\CtxMenu.cs` + `src\CtxMenuForm.cs` + 按钮
> 「右键菜单管理」（右键增强页「状态与修补」段第 4 个，**16 → 17 个按钮**，总按钮 121 → **122**）+
> 只读出口 `rightmenu list`。**禁用 = 往那个键写 `LegacyDisable` 空值**（量出来的，改名没用）、
> **删除先备份 .reg**、**扩展项只显示**、**要管理员的项提权重开 + 再点一次**。
> 正本 `docs\DESIGN.md` **§12.66**；`CHANGELOG.md` 的 `## [未发布]` 有完整条目。
> **推送 / 发版照旧要用户点头**，版本号还没动（下次发版大概 1.5.6）。
>
> **⚠️ 2026-10-10 又补了一轮：被 Windows 保护的那类项也能禁用了。** 用户问「**为什么管理员权限启动
> 也无法禁止？**」→ 量出来是那些键（`HKLM\...\Drive\shell\cmd` 等）**所有者是 TrustedInstaller、
> 管理员只有 ReadKey**，提权也没用。现在：界面标「（Windows 保护）」+ 勾选框灰掉 + 底栏提示 +
> 新按钮「取得所有权」（重确认后改所有者 + 追加完全控制 + 写隐藏开关，原样 SDDL 存成 .sddl），
> 动作记录里挂「还原权限」把它写回去。新文件 `src\RegAcl.cs`；探针 `tools\Show-MenuOrder.ps1` 加了
> `-Extended`。⚠️ 还修了个真 bug：一个只读的键会让 `OpenSubKey("command")` 抛异常 → 整个窗口崩掉。
> 正本 `docs\DESIGN.md` §12.66 ⑧、坑 48；回归 界面 C19u–C19z2 + 命令行 M50b。
>
> **⚠️ 2026-10-09 晚十：界面按用户挑的方案重做过一轮**（他原话「**右键菜单管理 的界面是不是可以优化
> 一下，给我几个方案看看。最好就是和主窗口【萌新工具箱】界面统一风格**」）——**宽行卡片**（段标题 +
> 一行行卡片，不再用 ListView）+ **勾选多选 + 底栏动作** + **禁用/恢复不弹确认、删除仍然弹** +
> **动作记录每条能撤销**；页签带条数、过滤器多了「未禁用」、搜索框有灰字提示。
> ⚠️ 两条"照主窗口"的高度规矩（都是拍实图看出来的）：**页签行必须写死 `Absolute(30)`**
> （原来写 `AutoSize` → 被撑到 **94px 高**）、**小按钮高度按字体算** `max(24, 量「国」+8)`
> （原来写死 20 → 字被上下切）。正本 `docs\DESIGN.md` §12.66 ⑦、截图 `docs\ctxmenu-shot.png`。
> 回归：界面 **C17–C19t**（换了一轮判据）+ 命令行 M49–M54。

- 版本 **1.5.5**（`src\AssemblyInfo.cs` 是唯一来源），已打 tag `v1.5.5` 并发 Release：
  <https://github.com/2604290100/mxx1-toolbox/releases/tag/v1.5.5>（`Mxx1Toolbox.exe` 842,240 字节 +
  `Mxx1Toolbox-package.zip` 1,308,446 字节，说明里带 SHA256）。CI 绿的。
  ⚠️ **zip 发版后重传过一版**（第一版 1,304,367 字节；CI 那次假红修好之后重打重传 —— 里面装着 `tests\`，
  **exe 一个字节没变**，所以 exe 那个 SHA256 前后一致）。
- 测试 **452 项** = 命令行 **286**（285 通过 + 1 项环境不满足跳过）+ 界面 **166**（两套都完整跑过、
  0 失败、**没有没跑的组**）；编码体检 204 个文件。本地实测：命令行约 2 分钟（挑组 `-Only T` 只要 2.9 秒）、
  界面约 2.5 分钟。
- ⚠️ **夹具根已经规范化过**：两个套件都用 `$tmpRoot = [System.IO.Path]::GetFullPath($env:TEMP)`，
  套件里**不许**再写"`Join-Path` 直接接 `$env:TEMP`"（CI 的 `%TEMP%` 是 8.3 短名，程序报长名 →
  四条断言假红，A 组 **A03e** 是盯着这件事的看门狗）。**改版本号要连 `Test-Cli.ps1` 的
  A03 / S11 一起改**。两条都在 `PITFALLS.md` **坑 40**、正本 `docs\DESIGN.md` **§12.65**。
- **测试可以挑组跑了**（`-Only M,N` / `-Skip P`）+ 提交前闸门 `tools\Test-Quick.ps1` +
  映射表 `tests\test-map.json`；分层与流程约定见 `docs\DESIGN.md` **§15**。
- 这一轮（2026-10-06）改的东西：① 「一键解除占用」不弹窗口那条路（鼠标旁边一张提示卡，
  `src\AutoUnlock.cs` + `src\Balloon.cs`）；② 解锁窗口扫描不再卡界面 + 二分定位占用者；
  ③ 关于窗口的「官网 / 仓库」能点开；④ **CI 抓到的安全修复**：底线从"看进程名"改成"看归属"
  （见坑 29）；⑤ 挑组执行 + 闸门 + 映射表 + 本 skill 拆成三份。
- **2026-10-06 晚二（这一轮）**：用户给了一份《UX 体验强化版说明文案》（10 个新工具各三段），要「做一个最简单的功能看一下开发速度」—— 落地了第一个：**「文件哈希校验 MD5/SHA256」**（系统工具页，`hash-check`）。同时把「说明文案」做成两层机制：`hint` = 悬停那一句、**新增 `about` = 右键「功能说明…」窗口里的整段详情**（窗口与 CLI `tip <id> --full` 打的是同一份 `MainForm.HelpText`；清单规矩交给 L0：写了 `about` 就必须有 `hint`、`hint` ≤ 120 字）。用户拍板「**面板简介那一层不做**」。顺手补了 F1（选中按钮按 F1 = 功能说明）。计数：命令行 **234** + 界面 **164** = **398 项**；设计正本 `docs\DESIGN.md` **§12.59**。
- **2026-10-06 晚四**：用户用了一次「一键解除占用」之后改口 —— 原话
  「**哪个弹框弹出以后不要跟随鼠标和 3 秒自动消失**」。提示卡于是**反转两条规矩**：
  **位置只算一次（不再跟着鼠标跑）**+**不给 `--notify` 就一直留着（点一下才关，卡片右下角写着
  「点一下关闭」）**；`--notify=<毫秒>` 照旧；新卡起来前先按固定标题把上一张关掉。
  **顺手修了两个不是这次功能带出来的 bug**：
  ① `FindWindowW` 的 `DllImport` 少了 `EntryPoint`/`ExactSpelling`（异常被 catch 吞掉 → "上一张卡关不掉"）；
  ② **P 组（发布包）挑组跑时会崩**（它用的 `$toolDir` 定义在 F 组里，`$null` 直接抛终止性错误，
  把整个命令行套件带走、T 组一条都没跑）—— 修法与教训见 `docs\DESIGN.md` **§15.7**。
- **2026-10-06 晚五**：三件事一起做完（用户原话：「**复制文件路径 / 在此处打开终端
  这2个功能做一下**」+「**一键解除占用改成 5 秒自动关闭，保留点击关闭**」）。
  ① **提示卡再改口一次**：晚四的"一直留着"撤掉，默认变成 **5 秒自动关闭**（`Program.DefaultNotifyMs`），
  **"位置钉住"和"点一下提前关"留着** —— 三轮口径一次记全在 `docs\DESIGN.md` **§12.60** 与
  `src\Balloon.cs` 的类说明（**别再翻回去**）；界面回归 N21e/N21f 跟着改。
  ② **「复制文件路径」**（`src\CopyPath.cs`，CLI `copypath`，右键菜单第四项）：难点是"多选到底会被
  调用几次"，做法是**单个路径先记进批次文件、轮询到安静才由最后一个进程把整批复制出去** →
  两种调用模型都对。⚠️ 第一版"死等 400ms"实测**被切成两批、丢文件**（三个并发进程）——
  正本 `docs\DESIGN.md` **§12.61**。
  ③ **「在此处打开终端」**（`src\Terminal.cs`，CLI `terminal`，右键菜单第五项，装 3 个位置）：
  wt → powershell → cmd，**如实报用的是哪个**；"真的落在那个目录里"用**自己那条句柄表命令**
  （`rightmenu handles <目录>`）验的 —— 正本 **§12.62**。
  计数：命令行 **259** + 界面 **164** = **423 项**；右键增强从 10 个按钮 → **14 个**（5 对装 / 撤）。
- **2026-10-06 晚六（最新一轮）**：用户**同一晚改了四遍口径**，最后一遍才是最终稿（别再翻回去）。
  三句话起头：「**【在此处打开终端】要求多选 一个是cmd 另外一个是powershell**」、
  「**【复制文件路径】要求多选 一个是相对路径 另外一个是绝对路径。复制出来的路径两边都不可以带有引号**」、
  「**右键增强哪里这 【状态与修补】这个说明栏和里面的按钮应该放底部才对**」（问了一句放哪儿，
  答「**放到倒数第一**」）；随后「**是要你做成子菜单**」、「**相对路径不要了，或者新增按钮复制文件名
  就好了**」；**最后**发来一张他画的菜单效果图 +「**我要这个效果，然后记得加上安装和卸载按钮**」，
  再问他两句，他选「**终端保持子菜单一条**」+「**复制各配一对，共 16 个按钮**」。落地：
  ① **复制 = 两条平铺的一级项**：`copyname` `Mxx1CopyName`（`copypath --name "%1"`，只取名字）+
  `copypath` `Mxx1CopyPath`（`copypath "%1"`，完整路径），**各一对装 / 撤按钮**（界面 14 → **16 个 /
  6 对**）；两条命令**都不带 `--quote`**（用户不要引号；它只留作命令行开关）。
  ② **终端 = 一个子菜单父项** `Mxx1Terminal` + **两棵子项树**（`Mxx1Toolbox.Terminal` = `%1` 那棵 /
  `Mxx1Toolbox.Terminal.bg` = `%V` 那棵），每棵 2 行（cmd / PowerShell 各钉死一个）——
  ⚠️ **子项命令里的占位符是跟着父项所在的右键位置替换的**，`%1` 和 `%V` **不能共用一棵树**。
  ③ `copypath` 新增 **`--name`**；批次文件的模式头变成三种（`#abs` / `#name` / `#rel <基准>`），
  **模式对不上就不并批**（否则两条菜单项互相串味）。`--relative` 只留作命令行开关，菜单不用了。
  ④ **上一版那两个同名键就地改写**（`Mxx1CopyPath` 去掉 `--quote`、`Mxx1Terminal` 变成子菜单父项，
  启动修补 `SyncIfInstalled` 会做）；**新加的 `Mxx1CopyName` 不会自己冒出来**（新功能要用户自己点一次）。
  晚六前两稿那四个 verb（`Mxx1CopyPathRel`/`Abs`、`Mxx1TerminalCmd`/`Ps`）装 / 撤 / 启动修补时顺手清。
  ⑤ **「状态与修补」段排到最底下**（段号 8），界面回归 **C01e** 钉整页 8 段的顺序、**C01f** 钉 16 个
  按钮的名单。⚠️ C01e 第一次写就红了：**置顶按钮排在本页最前**（`permdel.gui` 带 `pinned: true`），
  所以屏幕上的第一段永远是「隔壁工具（永久删除）」—— **段号决定不了置顶项的位置**（坑 36）。
  ⑥ 顺手修了「只有盘符的路径」（`D:`）—— `"D:\"` 在命令行里那个反斜杠会把引号吃掉（坑 37）。
  ⚠️ 还抓到一个**测试自己的假绿**：原来的 M45 造的夹具键名跟代码真用的那个对不上，"别人的同名键
  不许动"那条断言其实是空的（见 `PITFALLS.md` 第 38 条）。
  正本 `docs\DESIGN.md` **§12.63**；计数：命令行 **280**（279 通过 + 1 跳过）+ 界面 **166** = **446 项**（完整两套都跑过：0 失败）。
- **2026-10-06 晚七（最新一轮，已随 v1.5.5 发布）：右键菜单的先后顺序**。用户原话「**右键增强后的右键菜单顺序文字效果图
  发我看下，我需要调整一下顺序**」→ 拍板顺序 **复制文件名 → 复制文件路径 → 解除文件占用 →
  一键解除占用 → 常用功能 ▸ → 在此处打开终端 ▸**（两个子菜单收在最后）；同一轮他还选了两件：
  「**把「常用功能」也装上并排进顺序**」、「**换名做进「启动修补」，自动换顺序（不用我点装上）**」。
  ① **先量清楚"顺序是谁定的"**：Windows 把**同一个右键位置**里的静态项按 **verb 键名的字母序**排，
  跟代码里写的顺序无关（用 shell 自己的 `IContextMenu::QueryContextMenu` 只读探针量的，四个位置 4/4
  一致：那时排出来的是「一键解除占用 / 复制文件名 / 复制文件路径 / 在此处打开终端 / 解除文件占用」——
  `Mxx1Unlock` 的 `U` 排最后，所以「解除文件占用」掉到最底下）。另做了一次**对照实验**（临时文件类型
  + 四条假键，跑完即删）证明**键名决定顺序、标题完全不参与**（正本 **§12.64**）。
  ② **改法 = 键名带序号** `Mxx1Toolbox.<n>.<名字>`（1 CopyName / 2 CopyPath / 3 Unlock /
  4 AutoUnlock / 5 Common / 6 Terminal）—— **以后改顺序只改这个数字**；界面那一页的段序**故意没跟着动**。
  ③ **老用户自动换名**（`MigrateLegacyNames`，走 `SyncIfInstalled` 和任何一次「装上…」）：
  **先确认新键名写好、而且是我们写的，才删旧键**；不是我们的同名键不动；不该在这个位置的旧键直接清；
  旧记录一并摘掉。`IsInstalled` **新旧名字都问**（只认新名字 → 老用户被当成"没装"、不参与修补）。
  ④ `rightmenu items` 新增 **`verbs=` / `legacy=`**（顺序唯一的机器可读出口）、`titles=` 按菜单顺序报；
  `rightmenu status` 会说「上一版的键名 还有 N 处在（菜单顺序还是老的）」。
  ⑤ 新增**只读探针 `tools\Show-MenuOrder.ps1`**（把真实菜单按顺序打出来，工具箱六项打 ★，一个字节不写）。
  ⑥ **「常用功能」要用户自己点一次装上**（设计上不替用户往菜单里加新项；键名第 5 位已留好）。
  回归：**M46/M47/M47b**（顺序钉住）、**M44/M44b**（旧键名迁移 + 别人的旧键名不动）、**M48**（启动修补换名）、
  **M22c** 用户真实菜单值快照改成新旧名字一起拍。正本 `docs\DESIGN.md` **§12.64**。
- **2026-10-06 晚**：主窗口标题栏加了**最小化**（用户："给工具箱右上角添加一个最小化，目前很影响体验，
  只有关闭的情况下"）。只开最小化、不开最大化，所以标题栏会有一个**灰掉的最大化方框**（系统标准画法，
  用户拍板留着）—— 取舍与实测见 `docs\DESIGN.md` **§12.58**；界面回归补了 A03/A03b/A03c。
- **条款正文这一轮改了三次**（`docs\DISCLAIMER.md`）→ 同意指纹变了 → **所有人（包括用户自己）
  下次打开界面会再看到一次《使用条款确认》**（想免打扰：`Mxx1Toolbox.exe consent --accept`）。
- 详细历史（每一轮的来龙去脉、当时的原话与实测数字）在 **`HISTORY.md`**；
  **38 条**踩坑清单在 **`PITFALLS.md`** —— 这两个文件按需读，别再往这一页里堆。

## 结构

```
D:\萌新工具开发\toolbox\
  build.ps1                        一键编译（系统自带 csc.exe，不需要 .NET SDK）
  bin\Mxx1Toolbox.exe              交付物：单文件 GUI+CLI（不入仓）
  src\Program.cs                   CLI 入口（list / run [--dry|--confirm] / draft / status / tip / privacy /
                                   sysreg / rightmenu / ui / pin / export / import / checkupdate / help）
  src\MainForm.cs                  主窗口：页签 + 四列网格 + 底栏 + 搜索 + 日志面板 + 键盘；
                                   **`MainForm.OpenUrl` 是全工具唯一"打开网址"的入口**
                                   （关于窗口那两行链接、更新检查的「打开发布页」都走它；
                                   `MXX1_NO_OPEN=1` 时只写日志不真打开 —— 回归 D07d 靠它断言）
  src\ToolButton.cs                紧凑按钮（Flat + 主题配色 + 16×15 图标画布 + 灰色占位 + Flash/SetBusy）
  src\IconFactory.cs               图标：有 PNG 用 PNG，没有就实时画；一律 Normalize 成 16×15；GetMuted 出灰版
  src\ToolItem.cs / ToolRegistry.cs 按钮模型 + 读内嵌 tools\*.json + 用户层 tools.json（只认 tools 数组）；`hint` = 悬停那一句、**`about` = 「功能说明」窗口的整段详情**（新增字段）
  src\Launcher.cs                  按 kind 启动；SystemTargets 表（26 个系统工具）；找隔壁 exe / bin-tools；
                                   `LaunchElevatedCopy`（自己提权再起一遍 = 静默、不弹黑窗口）；UTF-8 输出
  src\RegEngine.cs                 "记原值 → 写入 → 读回核对 → 一键还原"的唯一实现（Privacy 与 SysReg 共用）
  src\SysReg.cs                    6 组系统设置开关（12 个按钮）+ 查看/还原改动 + selftest（TSV 记录）
  src\RightMenu.cs                 「右键增强」后端：在 HKCU\Software\Classes 下装 / 卸 verb 与级联子菜单、
                                   写前记原值（rightmenu-installed.tsv）、按位置给占位符（%1 / %V）、
                                   启动时顺手修补自己装过的键、读 Context Menu Manager Plus 的态度
  src\CtxMenu.cs                   **2026-10-09**：「右键菜单管理」后端 —— 按**五个位置**（文件 / 文件夹 /
                                   文件夹空白处 / 桌面 / 磁盘）列出右键菜单里**真实存在的项**（含别的软件装的、
                                   含 DLL 扩展项），并且能 **禁用 / 恢复 / 删除**。
                                   ⭐ **禁用 = 往那个键里写一个 `LegacyDisable` 空值**（键结构和标题一个字不动，
                                   恢复 = 删掉这个值）—— 这是 2026-10-09 用只读探针**量**出来的：
                                   「整键改名」那条路**没用**（菜单里照样显示，键名被当成标题兜底）。
                                   五种写法 + 五个位置的实测表在 `docs\DESIGN.md` §12.66 ①。
                                   ⚠️ `shellex` 是 `shell` 的**兄弟**（`<类键>\shellex\ContextMenuHandlers`），
                                   不是子键 —— 拼成 `<类键>\shell\shellex\...` 就永远列不出扩展项。
                                   删除前必备份成 .reg（备份没成功就绝不删）；测试走
                                   `MXX1_CTXMENU_ROOT` / `MXX1_CTXMENU_ROOT_MACHINE` 两个隔离根
  src\RegAcl.cs                    **2026-10-10**：注册表权限那一层 —— `CanWrite`（**只读判断**：试着以可写
                                   方式打开，用来标注"改不动的项"）、`ReadSddl` / `TakeOwn` / `RestoreSddl`
                                   （取得所有权 + 还原权限：两个特权 SeTakeOwnershipPrivilege /
                                   SeRestorePrivilege 都**要先在进程里打开**，管理员默认持有但默认关着）。
                                   ⚠️ **存不下原样 SDDL 就绝不动手**；只加 ACE、不删原有 ACE。
                                   为什么需要它：那些键所有者是 TrustedInstaller、管理员只有 ReadKey。
  src\CtxMenuForm.cs               同上的窗口（**2026-10-09 界面重做过**）：5 个位置页签（**带条数**，高 30 写死）
                                   + 搜索框（灰字提示）+ 五个快选 + 段标题 + 一行行卡片（勾选多选）
                                   + 底栏（禁用 / 恢复 / 删除（先备份）/ 刷新 / 关闭，数字跟着勾选变）
                                   + 可收起的「动作记录」（每条后面能撤销）。原来的那行：5 个位置页签 + 列表 + 「禁用 / 恢复 / 删除（先备份）/
                                   刷新 / 打开备份文件夹 / 关闭」+ 只读报告框（写清动了哪个键）。
  src\CtxRow.cs                    那一行的控件：勾选框 + 图标 + 名称 +（灰字）点下去会跑什么 + 右侧状态。
                                   ⚠️ **名称 / 状态必须是真 Label、勾选框必须是真 CheckBox**：界面回归跨进程
                                   读这些字（WM_GETTEXT），画上去的字读不到 —— 那样"窗口里列了什么"就只能信 CLI。
                                   ⚠️ 点整行 = 勾上/取消（三个 Label 也要挂同一个处理器，否则"点字没反应"）。
                                   **要管理员的项走"提权重开窗口 + 自动选中原来那一行 + 再点一次"**：
                                   自动做完就需要一个"带动作"的命令行参数，那会破掉
                                   「命令行只有只读入口」这条底线（L07 / M52 钉着）。
  src\MenuIcons.cs                 菜单图标：把内嵌的按钮 PNG 现场转成多尺寸 .ico（注册表的 Icon
                                   只认 exe/dll 图标资源或 .ico，**指 .png 是无效的**）
  src\FileLock.cs                  「谁占着这个文件」：Restart Manager（rstrtmgr.dll）P/Invoke +
                                   文件夹往下扫 4 层 / 整批失败二分劈开重查 / 命中后定位到具体文件 +
                                   SelfCheck（自己独占打开一次，给"真没人在用 / 有人占着但报不出名字 /
                                   其实是权限"三种确定结论）+ 系统关键进程禁止结束
  src\AutoUnlock.cs                **v1.5.4**：「一键解除占用」——不弹窗口那条路（`rightmenu unlock --auto`）。
                                   查（复用 FileLock.Scan）→ 直接结束占着它的程序（FileLock.Kill）→ 写日志 + 提示卡。
                                   能结束谁只有一处规则：`FileLock.AutoUnlockTarget`（见「底线」那节）
  src\Balloon.cs                   **v1.5.4**：鼠标旁边的提示卡（`Balloon.Show` + `NoticeForm`，**自己画的卡片**）。
                                   原来那一版是系统托盘气泡（NotifyIcon.ShowBalloonTip），用户 2026-10-06 报
                                   「没有正常弹出 + 位置要跟随鼠标」→ 换成自己画（见坑 28 / DESIGN §12.56）。
                                   **这张卡用户一晚上改口两次**（三轮口径在 DESIGN §12.60 里，别再翻回去）：
                                   ① v1.5.4 跟着鼠标走 + 6 秒自消；② 晚四 位置钉住 + 一直留着（点一下才关）；
                                   ③ **晚五（现行）位置钉住 + 默认 5 秒自动关闭 + 点一下提前关**
                                   （默认值在 `Program.DefaultNotifyMs`，「点一下关闭」那行灰字留着）。
                                   新卡先关掉上一张（固定窗口标题按 `FindWindowW` 找）；三张卡
                                   （解占用 / 复制文件路径 / 在此处打开终端）**共用这一段实现**。
                                   `--quiet` / `--notify=0` / `MXX1_NO_NOTIFY=1` 关掉它（测试一律关，只有 N21 那组
                                   故意开着量位置）。改动的来龙去脉与实测见 **DESIGN §12.60**
  src\CopyPath.cs                  「复制文件名 / 复制文件路径」（CLI `copypath [--name] [--relative] [--base=<目录>] [--quote]`；
                                   右键菜单里是**两条平铺的一级项**：`Mxx1CopyName`（`copypath --name "%1"`，只取名字）
                                   / `Mxx1CopyPath`（`copypath "%1"`，完整路径），**都不带 `--quote`**，
                                   **各一对装 / 撤按钮** —— 用户 2026-10-06 晚六最终稿的效果图就是这样）。
                                   难点是"多选会被调用几次"（Player 模型没在真实资源管理器里实测过）→
                                   **单个路径先记进批次文件、轮询到安静才由最后一个进程复制整批**，
                                   两种调用模型都对。⚠️ 别改回"死等固定毫秒"（实测会把一批切成两批、丢文件）。
                                   ⚠️ 批次文件的**模式头**分三种（`#abs` / `#name` / `#rel <基准>`）：
                                   模式对不上就不并批（两条菜单项是两个模式，串味 = 随机坏掉）。
                                   `--relative` 只留作命令行开关（菜单不用）：基准 = `--base` → **当前工作目录**
                                   → 目标所在目录；**跨盘符退回完整路径**；相对路径是**手算**的
                                   （.NET Framework 4.x 没有 `Path.GetRelativePath`）。
                                   设计正本 **DESIGN §12.61 / §12.63**；回归 = 命令行 O 组
  src\Terminal.cs                  「在此处打开终端」（CLI `terminal [--wt|--ps|--cmd] [--dry]`；
                                   右键菜单里是**一个子菜单父项** `Mxx1Terminal` + **两棵子项树**
                                   （`Mxx1Toolbox.Terminal` / `.Terminal.bg`），每棵 2 行：cmd / PowerShell 各钉死一个）。
                                   ⚠️ **子项命令里的占位符跟着父项所在的右键位置替换**，`%1` 与 `%V`
                                   **不能共用一棵树**（见 `RightMenu.TreeKeyOf`）。
                                   命令行不给开关时照旧 wt → powershell → cmd，**如实报用的是哪个**；
                                   `--dry` 只打印命令；不提权；目录不存在就退出码 2。
                                   设计正本 **DESIGN §12.62 / §12.63**；回归 = 命令行 Q 组
  src\UnlockForm.cs                「解除文件占用」的结果窗口（`rightmenu unlock` 起来的独立进程，不开主界面；
                                   **高度按内容自适应**，见"界面硬规则"里那条）
  src\AppIcon.cs                   窗口图标（按 DPI 取 exe 资源里的那一档）+ `Mxx1Form` 基类（11 个窗口都从它派生）
  src\HashTool.cs                  **本轮新增**：「文件哈希校验」——计算（MD5+SHA256 一趟读完、FileShare.ReadWrite 只读不独占、
                                   不猜算法）+ 窗口 HashForm（后台线程 + 进度、拖文件进来、复制 / 对照值 / 对比）。
                                   CLI `hash <文件> [--expect=]` 用的是同一份 HashTool
  src\HelpForm.cs                  **本轮新增**：「功能说明」窗口（右键 →「功能说明…」/ 选中按钮按 F1）。
                                   正文 = MainForm.HelpText（hint + about），窗口与 CLI `tip <id> --full` 同一份
  src\ConfirmForm.cs               自家的确认窗口「请确认」（不再用 MessageBox 甩命令，见 §"运行反馈"）。
                                   **所有确认框共用的一个窗口**（主窗口危险按钮 / 右键菜单管理的删除与
                                   取得所有权 / 解除占用 / 提权子进程）→ 改它要跑 界面 F,C,N。
                                   ⚠️ **2026-10-11 修过"按钮被挤出窗口"**：正文进 `AutoScroll` 面板占
                                   `Percent` 行、按钮行单独一行 `Absolute`（量出来的高）、`OnLoad` 里
                                   `FitToContent()` 按内容定高度 —— `TableLayoutPanel` 装不下时**不压行高、
                                   把最后一行推到容器外面**（坑 50）
  src\UserTools.cs                 用户层 tools.json 的读写（最小 JSON writer，写入前备份 .bak）
  src\ToolFolders.cs               **v1.5.3**：扫 bin-tools\ 的工具文件夹，自动长出按钮（只读，
                                   撞 id 就让位；`ToolItem.AutoLayer` 标记它不可编辑）
  src\Consent.cs / ConsentForm.cs   **v1.5.3**：首次运行条款确认门（记条款正文指纹，不是记 true）
  src\DisclaimerForm.cs            **v1.5.3**：窗口显示 docs\DISCLAIMER.md（编译时内嵌，唯一正本）
  src\UpdateCheck.cs               **v1.5.3**：只读更新检查（GitHub releases → tags；不下载不替换）
  src\NewToolForm.cs               图形化「新建按钮 / 编辑按钮」窗口（4 种类型）
  src\LinksForm.cs                 「常用链接」窗口（官网/仓库/几个 ms-settings 入口）
  src\Json.cs                      自带的小 JSON 解析器（不依赖 Newtonsoft / System.Web）
  src\Theme.cs / Native.cs         浅深主题配色（含灰色占位三色）+ DWM 深色标题栏 / 滚动条
  src\Settings.cs / Logger.cs / AppPaths.cs
  src\AboutForm.cs                 署名、站点、仓库、许可证常量的唯一来源；**关于窗口里官网 / 仓库那两行
                                   是 LinkLabel**（点了走 `MainForm.OpenUrl`，v1.5.4 用户要的：
                                   「网址要点击后可以访问，主页改成官网」）
  src\LogForm.cs / OutputForm.cs   程序内日志窗口（最新在最上）/ 命令输出窗口
  src\SettingsForm.cs              设置窗口
  tools\*.json                     121 个按钮的内置定义（编译时内嵌，资源名 tools.<文件名>）；7 个文件 =
                                 common / system / privacy / apps / cleanup / rightmenu / mine
  tools\Test-Encoding.ps1          编码红线体检（-Fix 修 BOM）
  tools\Test-Quick.ps1             **提交前闸门**：读 git 改动 → 查 tests\test-map.json → 跑
                                   L0（编码 + 内联）+ 编译 + `Test-Cli.ps1 -Only <受影响的组>`；
                                   `-List` 打映射表，`-Gui` 连界面组一起跑（要你没开着工具箱）。
                                   ⚠️ 读 git 那段**必须**临时把 `$ErrorActionPreference` 放成
                                   `Continue`（`Stop` 下 git 写 stderr 的 CRLF warning 会变成终止性错误，
                                   改动集读成空 → 静默退回全套跑，映射表被架空）；见 DESIGN §15.3
  tools\Sync-Skill.ps1             skill 三份副本同步成一个字节（正本=仓库里那份；-Check 只核对）
  tests\test-map.json              **改哪块 → 跑哪些组**的映射表（给 Test-Quick.ps1 用；也当文档看）
  tools\Make-Screenshots.ps1       拍 docs\gui-shot.png / dark-shot.png / system-shot.png（PrintWindow）
  tools\Make-Icons.ps1             批量画图标（先从 exe 的 list 读清单，所以**先 build 再跑它**）
  tools\Make-AppIcon.ps1           画 assets\app.ico（程序自己的图标，八尺寸；改完要重新 build）
  tools\Make-Package.ps1           打发布包 zip（build.ps1 -Package 调它；**回读 zip 逐个核对**，
                                   少一个文件就失败 —— 见"打包"那条坑）
  assets\icons\*.png               121 个按钮图标（编译时内嵌成 icons.<id>.png）
  assets\app.ico                   程序图标（`/win32icon` 用的就是它）
  tests\Test-All.ps1               一条命令跑完全部（也支持 `-Only` / `-Skip` 透传给两个套件）
  tools\Test-InlineSyntax.ps1      内联脚本语法 + 清单 JSON + **每个 .ps1 的语法**体检（34 个内联 / 7 个清单）
                                   + 闸门"读 git 改动"那段的**真跑自检**（假 git 往 stderr 写一行 warning，
                                   必须仍读得出文件名；见 DESIGN §15.3）
  tests\Test-Cli.ps1               命令行回归 280 项（A03b–A03d/D01 盯兼容、A14–A17 盯 exe 图标、
                                   L 组 12 项盯 sysreg、J09–J11 盯卸载窗口的列表、M 组盯右键增强（M40/M41 复制文件路径 / M41b/M41c 复制文件名 / M42–M43c 终端子菜单 / M44 升级清理 / M45+M45b 别人的键 + 补装 / M21b 各装各撤）、
                                   R 组 14 项盯 bin-tools 自动按钮、P 组 10 项盯发布包内容、
                                   S 组 26 项盯条款门 + 更新检查；
                                   环境不满足的项走 Skip()，打印 [SKIP] 不算失败）
  tests\Test-Gui.ps1               界面回归 166 项（要交互式桌面，无桌面返回 3 = 跳过；A04b 盯窗口图标、
                                   A03 盯"主窗口有最小化方框、没有最大化方框"、A03b/A03c 真发一次
                                   最小化再还原（位置尺寸一字不差）、
                                   H 组盯固定尺寸、E04b 盯窗口位置、A09 盯 bin-tools 说明、
                                   N 组 30 项盯解除占用小窗口（含 N14/N15/N17/N18 的高度自适应、
                                   N19/N19b「一键解除占用（--quiet）一个窗口都不弹」、N20/N20b「扫描期间窗口一直活着」、
                                   N21/N21d「提示卡就在鼠标旁边、卡片上有字」、**N21b 卡片不跟着鼠标跑**、
                                   N21c 给了 `--notify` 还是到点自己消失、
                                   **N21e/N21f/N21g「不给 --notify 时 1 秒还在、5 秒自己关掉 + 点一下立刻关 + 新卡顶掉上一张」**）、
                                   I 组 27 项把条款确认窗口真开起来点一遍，含 I10b/I10c；C01e 钉右键增强页 8 段段序、C01f 钉那 17 个按钮的名单、C17–C19o 钉「右键菜单管理」窗口）
  docs\DESIGN.md                   设计正本（含"踩过的坑"清单 + §13.7 自动按钮 + §16 兼容性 + §17 条款门）
  docs\DISCLAIMER.md               免责声明与服务条款正本（**编译时内嵌进 exe**，窗口显示的就是它）
  assets\app.manifest              清单：asInvoker + supportedOS（Win7/8/8.1/10）+ System DPI + longPathAware
  docs\gui-shot.png / dark-shot.png / system-shot.png  界面截图
```

## 命令

```powershell
powershell -File build.ps1                    # 编译 → bin\Mxx1Toolbox.exe
powershell -File build.ps1 -Package            # 额外打 zip（调 tools\Make-Package.ps1，整棵 bin-tools\ 进包）
powershell -File tools\Make-Package.ps1       # 只打包（不用重编）：-StageDir / -ZipPath 可指到临时目录
powershell -File tools\Test-Encoding.ps1      # 编码体检（改完文件必跑；-Fix 修 BOM）
powershell -ExecutionPolicy Bypass -File tests\Test-All.ps1   # 全套（无桌面加 -SkipGui）
powershell -ExecutionPolicy Bypass -File tests\Test-Cli.ps1   # 命令行回归
powershell -ExecutionPolicy Bypass -File tests\Test-Gui.ps1   # 界面回归（要交互式桌面）
powershell -File tools\Make-Screenshots.ps1   # 重拍文档截图（浅色 / 深色 / 系统工具页签）
powershell -File tools\Make-Icons.ps1         # 重生成 PNG 按钮图标（先 build 再跑，改完还要再 build）
powershell -File tools\Make-AppIcon.ps1       # 重生成 assets\app.ico（改完还要再 build）

& bin\Mxx1Toolbox.exe list [--tab system]     # 列按钮（tab 分隔：id / 页签 / 名称 / 类型）
& bin\Mxx1Toolbox.exe run devmgmt --dry       # 只解析按钮指向哪里，不真的启动（灰按钮回 kind=none）
& bin\Mxx1Toolbox.exe run permdel.gui         # 跑一个按钮（和界面同一条路径）
& bin\Mxx1Toolbox.exe status                  # key=value（含 systemTargets / systemMissing / autoButtons /
                                             #   windows / settingsApp / consent / updateCheck / admin / pinned）
& bin\Mxx1Toolbox.exe checkupdate             # 只读版本号，不下载不替换（0 = 查过了，1 = 关掉了 / 查不成）
& bin\Mxx1Toolbox.exe disclaimer              # 打印条款正文（和窗口显示的一致）
& bin\Mxx1Toolbox.exe consent [--accept|--reset]   # 看 / 记下 / 清掉首次运行的条款确认状态
& bin\Mxx1Toolbox.exe tip [id]                # 打印按钮的悬停说明（界面交给 ToolTip 的就是这一串）
& bin\Mxx1Toolbox.exe tip [id] --full         # 打印「功能说明」窗口里的整段正文（同一份 MainForm.HelpText）
& bin\Mxx1Toolbox.exe hash <文件> [--expect=<校验值>]   # 算 MD5/SHA256（只读、不联网）；给 --expect 就顺便对一次：
                                              #   0 = 一致、1 = 不一致、2 = 用法错/读不了/校验值认不出
& bin\Mxx1Toolbox.exe privacy status          # 只读列隐私开关状态；selftest 自检「原值→写入→还原」
& bin\Mxx1Toolbox.exe sysreg  status          # 只读列 6 组系统设置开关的现状；items / selftest 同上
& bin\Mxx1Toolbox.exe rightmenu status        # 只读列右键菜单里装了什么 / 子菜单几项（**写入口只在界面里点**）
& bin\Mxx1Toolbox.exe rightmenu list          # 只读列五个位置里**真实存在**的菜单项（含别人的软件装的 / DLL 扩展项）
& bin\Mxx1Toolbox.exe rightmenu unlock --query-only <路径>   # 只查谁占着这个文件，不弹窗也不结束进程
& bin\Mxx1Toolbox.exe rightmenu unlock --auto <路径>         # **一键解除占用**：不弹窗，直接结束占着它的程序
                                                            #   加 --notify=<毫秒> 定提示卡显示多久（--quiet 连它也不要）
                                                          #   （右键菜单里那一项用的就是它；--quiet 不要提示卡）
                                                          # 输出：path/exists/scanned/truncated/hits/lockers/
                                                          # badfiles/file/pid…/verdict/verdictlocked/error
                                                          # （tests 靠这些键断言，v1.5.1 加了后半截）
& bin\Mxx1Toolbox.exe ui log | ui settings    # 打开界面并直接看日志 / 设置（右键子菜单里那两个固定入口）
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
- **这个目录空着是正常的**：第一次打开界面时 `AppPaths.EnsurePayloadDir()`（在 `Program.Main` 的 GUI
  分支里）会建好目录并放一份 **`说明.txt`**（只在缺文件时写、不覆盖用户改过的）。用户 2026-10-04
  问过「bin-tools 里面为什么是空的？」—— 开发机上不放东西照样能用，查找顺序最后会命中上一层相邻
  仓库的 `permanent-delete-menu\bin\`；放进去的文件优先级更高，所以隔壁重新编译后要换掉旧副本。
  回归：Test-Gui 的 A09 盯着这个文件必须在。
- 查找顺序：设置里指定的路径 → `bin-tools\<文件名>` → exe 同目录同名文件 → 用户目录 `bin-tools\` →
  向上三层找隔壁仓库 `permanent-delete-menu\bin\`（开发用）→ `%LOCALAPPDATA%\PermanentDelete\`。
- **相对路径按「工具箱目录 → `bin-tools\`」解析**（`AppPaths.Resolve`），所以清单里只写文件名就行；
  `icon` 字段和 `open` 的 target 走同一条路。
- 入口：设置窗口 + 关于窗口的「打开工具目录」（没有就自动建）；找不到工具时的提示里直接写出这个路径。
- 打包：`build.ps1 -Package` → **`tools\Make-Package.ps1`** 把 **exe 旁边这个 `bin-tools\` 整个**
  复制进发布包的 `bin-tools\`（用户自己放的工具文件夹 / 单个 exe 一起走；`cache` / `*.tmp` / `*.log` /
  `*.bak` / `说明.txt` 除外，`说明.txt` 由工具箱第一次打开界面时自己写一份最新的）。
  **哪台机器上打包，包里带的就是那台机器上的工具**；隔壁那个 `PermanentDeleteSetup.exe` 不在
  `bin-tools\` 里时才从隔壁仓库的 `bin\` 补一份（找不到就打印一行说明，不报错）。
  打完包会**回读 zip 逐个核对**，少一个文件就失败并列出少了哪些 —— 见下面"打包"那条坑。

### 放一个自己的工具进去（正确流程 + "谁会不会清掉它"）

2026-10-04 用户要把 Mem Reduct 便携版放进去时问的就是这两件事，答案固定：

1. **拷到 exe 旁边**：`D:\萌新工具开发\toolbox\bin\bin-tools\<工具名>\`，**整个文件夹拷**、别只拷 exe ——
   便携版一般还带语言文件 / 便携标记（Mem Reduct = `memreduct.lng` + `portable.dat`，缺了会变英文、
   或者改去写注册表）。按架构挑子目录（这台是 AMD64 → `memreduct\64\` 那三个文件）。
2. **按钮写相对路径**：`"kind": "exe"`, `"path": "<工具名>\\<exe>"`, `"workdir": "<工具名>"` ——
   `AppPaths.Resolve` 会按「工具箱目录 → `bin-tools\`」解析；**别写绝对路径**（工具箱一搬就断）。
   按钮放**用户层** `%LOCALAPPDATA%\mxx1-toolbox\tools.json`（不用重编译、仓库里也不会有它；
   想让所有人都有才写进 `tools\*.json` + 重编译）。
3. **图标**：用户层按钮不生成 PNG（`Make-Icons.ps1` 只画内置按钮），默认是"实时画的占位图标"。
   想要真图标就把 exe 的图标导出来放进同一个文件夹：`ExtractIconEx` 的 **small（16×16）** 那一张最清楚
   （`Normalize()` 只把它放进 16×16 画布、不缩放），按钮里写 `"icon": "<工具名>\\<图标>.png"`。
4. **验证**（不用点按钮）：`bin\Mxx1Toolbox.exe run <id> --dry` → `target=` 必须指到
   `...\bin-tools\<工具名>\...` 且 `exists=yes`；`status` 的 `buttons=` 会 +1。
5. **谁会不会清掉它**：`build.ps1`、`build.ps1 -Package`（只往**发布包**里拷隔壁安装器）、
   `Make-Icons.ps1` / `Make-AppIcon.ps1`（只写 `assets\`）、启动时的 `EnsurePayloadDir()`（只在缺
   `说明.txt` 时写它）**都不碰**；`tests\Test-Cli.ps1` 的 F02/F03 只碰它自己拷的那份
   `PermanentDeleteSetup.exe`（本来就有就跳过拷贝，**也绝不在收尾删别人的文件**），
   只有 `bin-tools` 目录**不存在**时才会建了又删。
   ⚠️ 唯一真风险：**`bin/` 整个目录不入 Git**（`.gitignore` 里一行 `bin/`）—— 手删 `bin\`、
   换机器 / 重新 clone、把 exe 挪走（`bin-tools` 跟着 exe 走）都会没。所以**原始安装包 / 压缩包留着
   别删**，或者再放一份到 `%LOCALAPPDATA%\mxx1-toolbox\bin-tools\`（备用工具目录，查找顺序里排后面）。

### 工具文件夹自动长按钮（v1.5.3，`src\ToolFolders.cs`）

用户 2026-10-04 问「bin-tools 里面的工具是不是应该自动加载一个按钮？」→ **已实现**。规则（设计正本 §13.7）：

| 文件夹里有什么 | 结果 |
| --- | --- |
| `tool.json`（字段和 `tools\*.json` 一样） | 按它建按钮（`id` 默认 `auto.<文件夹名>`、`tab` 默认 `mine`、`segment` 默认 2） |
| 只有一个 exe | 就用它 |
| 好几个 exe，其中有一个和文件夹同名 | 用同名那个 |
| 好几个 exe 又对不上名字 / 一个 exe 都没有 | **不猜**：不生成按钮，日志里说明原因 |
| 里面的 `tool.json` 语法坏了 | **只跳过它自己**，别的按钮照常 |

- 扫的是 **exe 旁边的 `bin-tools\`**（+ `%LOCALAPPDATA%` 那份备用目录）的**一级子文件夹**，
  `64\` 这种再套一层也认；按 **F5** 重扫，不用重启。
- 自动按钮**只读**（`ToolItem.AutoLayer=true`，右键菜单里不能编辑 / 删除）、
  **绝不覆盖**已有 id（撞上就让位并写日志）。
- `status` 里有 `autoButtons=N` 与每个按钮一行 `autoButton=<页签>\t<id>\t<来源>`；
  测试（R 组）就是靠这两行、并且**只碰自己建的 `__mxx1-autotest-*` 文件夹**。

## 兼容性（Win7 / Win10 / Win11 —— 用户定的范围）

- **运行环境**：exe 是 .NET Framework 4.x 程序（系统自带 `csc` 编的）→ **Win10/11 自带，Win7 要 SP1 +
  自己装 4.x**，否则双击只有系统那句"需要 .NET Framework"（拦不住，写进文档说清）。
  exe 是 anycpu：32 位系统上按 32 位跑。
- **`assets\app.manifest` 必须写齐 `supportedOS`**（Win7/8/8.1/10）：漏了老系统那条，老系统会按兼容模式
  对待程序，而且 `GetVersionEx` 在 Win8.1 以上**撒谎**（一律 6.2）。
- **`ms-settings:` 只有 Win10 起有**：系统工具页 7 个走它的按钮在 Win7 上给一句"去控制面板哪一项"，
  `status` 里 `settingsApp=yes|no` 可查。判据**先看注册表** `HKCR\ms-settings`（版本号会被骗），
  读不到才退回版本号。
- **句柄表布局随指针宽度变**（x64 40 / x86 28 字节）：`src\HandleUnlock.cs` 按 `IntPtr.Size` 现算，
  别写死 x64 的 40/16/+8/+16/+30（在 32 位 Windows 上会扫出垃圾）。「文件」类型编号是自查的，不写死。
- **深色标题栏**只有 Win10 1809+ 认（DWM 19/20 号属性），Win7 上安静地不生效。
- **Win11 右键菜单折叠**：装上的项要先点「显示更多选项」。
- 测试里 `A03b` / `A03c` / `A03d` / `D01` **按这台机器是哪一版分叉断言**：
  Win7 上那 7 个必须算"没有"，Win10/11 上一个都不许缺。逐项表见 `docs\DESIGN.md` §16。

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
| **主窗口** `MinimizeBox=true` + `MaximizeBox=false`（其它窗口两个都 `false`） | 最小化要能用（2026-10-06 用户原话「给工具箱右上角添加一个最小化，目前很影响体验，只有关闭的情况下」）。**代价是标题栏多一个灰掉、点不动的最大化方框** —— 那是系统对"只给最小化"的窗口的标准画法（实测 `TITLEBARINFOEX` 报 `state=0x1`），用户看过对比后拍板留着。**别为了藏它把 `MaximizeBox` 改成 true**：按钮墙固定 4 列、列宽不随窗口变，最大化之后左上一小块 + 一大片空白，还会把记住的窗口尺寸写坏。见 `docs\DESIGN.md` §12.58；A03/A04/A03b/A03c 盯着 |
| 界面文字不用 `✓ ⚠ →`（微软雅黑没字形）；图标走 PNG 或程序内绘制 | 渲染成空白 |
| 日志显示**最新在最上面**，不给正序开关 | 想看刚点的那次结果得滚到底 |
| DPI：清单里 `dpiAware=true`（System aware）+ `AutoScaleMode.Font` | 不要用 PerMonitorV2：它需要 app.config，而 exe.config 会破坏单文件 |
| **非模态窗口（结果 / 日志 / 常用链接）必须手工居中**：`StartPosition = CenterParent` **只对 `ShowDialog()` 的模态窗口有效**，`Show()` 出来的窗口 Windows 会放在屏幕左上角 → 用 `WindowPlacement.ShowCentered(f, owner)`（对 owner 居中 + 夹进工作区 + 多开时错开 28px） | 用户报的「点击激活状态为什么会弹到左上角窗口」。判据：Test-Gui 的 E04b，比的是结果窗口与主窗口的**重叠比例**（要 ≥ 50%，居中时实测 99%） |
| **窗口高度按内容量出来，别按"行数"拍公式**：`UnlockForm.UpdateSize()` —— 文字块用 `TextRenderer.MeasureText(text, font, new Size(标签自己的 MaximumSize.Width, 0), WordBreak)`，列表用 `ListViewItem.Bounds`（Details 视图第一行的 `Top` = 列头高、`Height` = 行高），相加后夹进屏幕工作区；高度变了**按原中心点重新摆一次** | 原来写的是 `210 + 行数 × 20`（最多 470）——上面那几行文字（路径 / 状态 / 自查结论 / 常驻提示）全都会换行，它一行都没算：行数少时偏高，**"没查到占用"**时正文八九行被切掉一半（用户报「窗口高度没有做自适应高度」）。判据：Test-Gui 的 N14/N15/N17/N18（每个可见控件完整落在客户区 + 底部空白 ≤ 40px，用屏幕坐标判所以 DPI 无关） |
| **窗口图标 = 两部分**：exe 的资源（`assets\app.ico` + `build.ps1` 的 `/win32icon`）**和** `Form.Icon`（`src\AppIcon.cs`：`LoadImage(hInstance, "#32512", IMAGE_ICON, cx, cy)` 按 DPI 取那一档，`Form.Icon` 给大图、随后补一次 `ICON_SMALL`；窗口都从 `Mxx1Form` 派生） | 只做前面半截 = 资源管理器里有图标、**标题栏和任务栏还是 .NET 那个空白窗体图标**（用户报「编译好的 exe 没有图标」）。判据：Test-Cli A14–A17 + Test-Gui A04b/N16，都按像素认"蓝底 + 纯白方块"（.NET 默认图标一点纯白都没有） |
| **用户可见文案一律直白**：写"是什么 / 会怎样 / 怎么办"，不写"我发现了一条线索""这不是猜的"这种叙述；和别家软件的比较（比如火绒）只留在代码注释和文档里 | 用户原话「那些提示不要做得太像AI了，明明都是固定的功能，非要说什么线索」。改文案时**别把这些功能说明词删掉**：「它自己在运行」「窗口里开着它」「查到 N 个程序占着它」「独占打开」都是测试断言和用户理解情况用的锚点（M14b / N03 / N07 盯着） |

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

**那个卸载窗口里显示的是中文应用名**（用户 2026-10-04 问过「里面的窗口是不能显示中文是？」）：
`Get-AppxPackage` 的 `Name` 是包标识（`Microsoft.WindowsCalculator`），人能看懂的名字在「开始菜单」那一层 ——
脚本先读 `Shell.Application` 的 `shell:AppsFolder`、再读 `Get-StartApps`，用 AppID 里 `!` 前面的包族名
建映射，列表写成 `计算器　（Microsoft.WindowsCalculator）　·　11.2508.4.0`；`SignatureKind=System`
的 Windows 组件排在**最后**并标 `【系统组件】`（原来按包名首字母排，一屏 `Microsoft.AAD.BrokerPlugin`）。
两个坑记着：① **筛选框会导致 `SelectedIndex` 与包数组下标错位** → 必须同步维护过滤后的 `$shown`
（否则"搜关键字再卸载"会卸错应用）；② 设 `MXX1_PICKER_LIST_ONLY=1` 时脚本只打印列表不弹窗，
命令行回归靠它真跑这条（`J09`–`J11`）。
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

## 「右键增强」怎么点（17 个按钮 = 16 个右键菜单按钮（6 对装 / 撤 + 状态 / 重建 / 说明 + 右键菜单管理）+ 隔壁工具）

**用户 2026-10-04 定的方案**（设计正本 `docs\DESIGN.md` §14，晚六最终口径见 **§12.63**）：
工具箱自己往 Windows 右键菜单里装 **六样**东西 —— **只写 `HKCU\Software\Classes`**，不要管理员、
不装 shell 扩展 DLL、不起服务、不加开机启动
（微软文档写明在这个根下注册子动词不需要提升权限；实测也是真的不用）。四个位置：任意文件 /
文件夹 / 文件夹里的空白处（= 当前文件夹）/ 桌面空白处 —— **但后加的那几项只装适合自己的那几个位置**
（`RightMenu.LocationsOf`：两条复制要有选中的东西、打开终端要一个能代表目录的位置）。

| 装什么 | 键（都在 HKCU\Software\Classes 下；**键名里的序号 = 菜单里的先后**，见下） | 点了做什么 |
| --- | --- | --- |
| **复制文件名** | `Mxx1Toolbox.1.CopyName` ×（`*\shell` + `Directory\shell`）= **2 个键** | `copypath --name "%1"` → 只取名字（`报告.txt` / `2026`）进剪贴板，**不带引号** |
| **复制文件路径** | `Mxx1Toolbox.2.CopyPath` ×（同上）= **2 个键** | `copypath "%1"` → 完整路径进剪贴板，一行一个，**不带引号** |
| 解除文件占用 | `Mxx1Toolbox.3.Unlock` 等 4 个 verb | `"<exe>" rightmenu unlock "%1"` → 开一个小窗口列出谁占着它 |
| **一键解除占用**（v1.5.4） | `Mxx1Toolbox.4.AutoUnlock` 等 4 个 verb | `"<exe>" rightmenu unlock --auto "%1"` → **不弹窗口**，直接结束占着它的程序，鼠标旁边一张提示卡说结果 |
| 常用功能（级联子菜单） | `Mxx1Toolbox.5.Common` + 共用子项键 `Mxx1Toolbox.Common` | 子项 = 「常用」页的镜像，命令是 `"<exe>" run <id>` |
| **在此处打开终端（级联子菜单）** | `Mxx1Toolbox.6.Terminal` ×（文件夹 + 两个背景位置）= **3 个键** + 两棵子项树（`Mxx1Toolbox.Terminal` = `%1` / `.Terminal.bg` = `%V`） | 子项 2 行：`terminal --cmd "%1"` / `terminal --ps "%1"`（背景位置是 `%V`）→ 在那个目录里开指定终端 |

⚠️ **顺序 = 键名里的序号**（`Mxx1Toolbox.<n>.<名字>`）：Windows 按**同一个右键位置里 verb 键名的
字母序**排菜单，不看代码里的顺序 —— 这正是 2026-10-06 晚七这一轮改的东西（**§12.64**：
量法、对照实验、改顺序的正确做法、老用户换名 `MigrateLegacyNames`）。
上一版那批**不带序号**的键名（`Mxx1Unlock` / `Mxx1AutoUnlock` / `Mxx1Common` / `Mxx1CopyName` /
`Mxx1CopyPath` / `Mxx1Terminal`）已在 `LegacyVerbOf` 里登记，由启动修补自动换掉 —— 别把它们删了。
`rightmenu items` 的 `verbs=` 是顺序唯一的机器可读出口（M46/M47 钉着）；人眼核对用
`tools\Show-MenuOrder.ps1`（只读）。

**六对的装 / 撤都是成对的独立按钮**（`rightmenu.<item>.on` / `.off`），互不影响 ——
**两条复制各写自己的键、各一对按钮**（用户晚六原话「记得加上安装和卸载按钮」；M21b 盯着
"撤掉复制文件名之后复制文件路径还在"）。
`RightMenu.AllItems` 是唯一那份名单（现在是 **6 项**），`Install` / `Uninstall` / `Status` / `Help` /
图标核对 / 残留检测都遍历它 —— **加一项只要动 id、标题、图标、命令、位置表这几处，别在别处另写名单**。
晚六前两稿那四个 verb（`Mxx1CopyPathRel` / `Abs` / `Mxx1TerminalCmd` / `Ps`）没发布过，
但装 / 撤 / 启动修补都会顺手清掉（`CleanSuperseded`）；上一版的
`Mxx1CopyPath`（去掉 `--quote`）与 `Mxx1Terminal`（变成子菜单父项）是**同名就地改写**，
启动时 `SyncIfInstalled` 会做（见 §12.63）；**晚七又把它们换成带序号的键名**（§12.64，自动换）。
**段序**：六个功能段 → 隔壁工具 → **状态与修补放最底下**（共 8 段）；⚠️ 置顶的「永久删除工具」
永远排在页面最前（C01e 盯着整页顺序、C01f 盯着 17 个按钮的名单）。**页面段序 ≠ 右键菜单里的先后**：
后者由键名里的序号决定，改段号不会动菜单。

**第三项「一键解除占用」**（`src\AutoUnlock.cs`，用户 2026-10-05 点名要的"不弹窗版本"）：

- 用户原话：**「保留现有的功能的前提下加个不弹窗的一键解除，对应也要单独加 2 个按钮一个添加右键
  一个撤销右键」** → **窗口版一个字节都没改**，这是另外一条入口；装 / 撤用**单独那对按钮**
  （`rightmenu.auto.on` / `rightmenu.auto.off`），和「装上 / 撤掉解除占用」互不影响。
- 查占用 / 结束进程**复用同一份实现**（`FileLock.Scan` / `FileLock.Kill`），区别只有"要不要问"。
- **底线更窄，而且只有一处**（`FileLock.AutoUnlockTarget`，测试 M34/M39 盯着）：只结束
  **真占着文件的**（`lock`）和**它自己在运行的**（`run`）；"窗口里开着它"（`open`）不动。
  拒绝清单 = 老的那套（`Protected`：系统关键进程 / `pid ≤ 4` / `svchost` / `lsass`…、`explorer.exe`、
  工具箱自己）+ **2026-10-06 补的三条结构性规矩**（⚠️ 靠"进程名黑名单"是**拦不住**的，见坑 29）：
  **不是当前用户自己的进程**（`NotMine`：比进程令牌里的用户 SID）、**系统报的类型是"服务"**（`IsService`）、
  **连进程名都读不出来**（`NameUnread`，这时手里的名字是系统报的友好名，名单对它无效）。
  任何一条不确定就**不动**（fail closed）。窗口那条路（有确认框）**一个字没改**。
- **没同意过使用条款 = 一个进程都不碰**（这条路没有窗口可以弹确认框，规矩是"不同意就不动手"，
  只写一行日志）。回归 **M35** 就是盯这条。
- 结果用**鼠标旁边一张提示卡**说一句（`src\Balloon.cs` 的 `NoticeForm`）—— 不弹窗口，但也不能一声不吭
  （用户 2026-10-04 报过「点击确认以后也没有成功或者失败的反馈」）。
  ⚠️ 原来这一格是**系统托盘气泡**，用户 2026-10-06 报「没有正常弹出 + 位置要跟随鼠标」→ 换成自己画的
  卡片（不抢焦点 / 位置在鼠标旁边 / 点一下也关），见坑 28 与 `docs\DESIGN.md` §12.56。
  **2026-10-06 晚用户一晚上改口两次**：晚四「不要跟随鼠标和 3 秒自动消失」→ 位置钉住 + 一直留着；
  **晚五（现行）「改成 5 秒自动关闭，保留点击关闭」** → **位置仍然只算一次、默认 5 秒自己关、点一下提前关**
  （卡片上写着「点一下关闭」）—— 三轮口径见 §12.60，**别再翻回去**。同一段卡片实现也被
  「复制文件路径」「在此处打开终端」共用（§12.61 / §12.62）。
  卡片显示多久用 `--notify=<毫秒>` 定；测试一律 `--quiet` / `--notify=0` /
  `MXX1_NO_NOTIFY=1`：**别在别人桌面上弹东西**（只有 N21 那一组故意开着它量位置）。
- 命令行输出是给测试读的：`auto=killed|partial|none|locked|skipped-consent|error` / `killed=` /
  `failed=` / `targets=`（挑中了谁，逗号分隔，排查"为什么没动"就看它）/ `summary=`；
  退出码 0 = 正常跑完、2 = 没给路径、1 = 查询本身出错。
- **实测**（写测试之前端到端跑过）：锁一个文件 → `auto=killed killed=2 targets=powershell.exe`
  （连 conhost 子进程一起），再查 `lockers=0 candelete=yes`；系统事件日志那个永远被几个 `svchost`
  占着的文件 → `auto=locked killed=0 targets=`（一个都没挑中），占用者一个没少。

- **「解除文件占用」**（`src\FileLock.cs`）：用 **Windows 自带的 Restart Manager**（`rstrtmgr.dll`
  的 `RmStartSession` → `RmRegisterResources` → `RmGetList`）查"谁占着这个文件"，**不装 handle.exe、
  不要管理员**（实测非管理员也能查出别人的进程）。**三层做法**（v1.5.1 定型）：
  - **扫**：文件就查它；**文件夹按层（BFS）往下扫 4 层、最多 400 个文件**，跳过软链接 / 联接点，
    整次扫描最多 4 秒 —— **只扫第一层是个已被用户报过 bug 的坑**（第一层全是子文件夹时什么都查不到）；
  - **查**：整批先问一次（实测 60 个文件一批只要 **10ms**，和查 1 个一样）判"有没有"；
    整批失败就**二分劈开重查**（RM 全有或全无：一个它不认的路径会让整批回 0 结果，
    含非法字符的路径、`kernel32.dll` 这种已知 DLL 都会，实测还撞到 `ERROR_INVALID_HANDLE(6)`）；
    有命中再**定位到具体是哪个文件**：v1.5.4 起是**二分定位**（把这一批劈成两半各问一次，
    哪半有人占着就继续劈，问到单个文件为止）—— 400 个文件里占的是第 200 个也只要 **9 层约 18 次**
    查询（原来"挨着问前 60 个"既慢又定位不到：实测第 200 个直接报"没定位到"）；
    查询次数预算 `MaxAttributeQueries = 64`，超了就说"还有 N 个文件没定位"；
  - **自查**（`SelfCheck`，v1.5.1 新增）：不管查没查到，都自己试着**独占打开**一次那个路径 ——
    能打开 = 「我自己能独占打开它 —— 现在真的没有程序占着它」；被共享冲突拒绝（`0x80070020`）
    = 「确实有程序占着它，但报不出是哪个程序」（对方权限更高 / 别的用户）；权限被拒（`0x80070005`）
    = 「不是被占用，是权限或只读属性拦住了」；**文件夹**也这么试（`CreateFile` + `dwShareMode=0` +
    `FILE_FLAG_BACKUP_SEMANTICS`，被占用回 `err=32`）—— 这一条原来根本查不到。
  - 还有两个反直觉的坑：**绝不能把目录路径登记给 RM**（回 `ERROR_ACCESS_DENIED(5)` 而且污染整批）；
    `strAppName` 是**友好显示名**不是路径，`svchost` 里几个服务会返回**同一个 PID 好几行** → 按 PID 去重。

- **v1.5.2 补的另外两种情况**（用户报「右键文件夹说有程序占用着但找不到进程」「右键 sitemap.txt 说没找到」
  之后加的，都在 `FileLock.cs`，CLI 上分别是 `run=` / `open=` / `candelete=`）：
  - **「它自己在运行」**（`ProbeRunners`）：**正在运行的程序不持有文件句柄**（exe 是内存映射，
    加载器读完就关句柄）—— RM 和"独占打开试试"**都看不见它**，可它让文件删不掉、文件夹松不开。
    查法：Toolhelp 拿全进程 → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` +
    `QueryFullProcessImageName` 拿镜像路径 → 等于目标文件 / 在目标文件夹里（任意一层）就点名。
    **别用 `Process.MainModule`**（那要 `PROCESS_VM_READ`，别的用户/更高权限的进程会抛）；也别指望
    `lockers=0`：实测 RM **有时也能**把"正在运行的 exe 自己的镜像文件"报成占用。
  - **「窗口里开着它」**（`ProbeWindows`）：记事本这类程序**读完就关句柄**，本来就没锁
    （实测用户开着的 `sitemap.txt` 谁都锁不住）—— 所以"没查到"是**对的**，但用户会觉得工具坏了。
    查法：`EnumWindows` 取可见窗口标题 + 类名（文件名出现在标题里；文件夹则要求是资源管理器窗口
    `CabinetWClass` / `ExploreWClass` 且标题正好等于文件夹名，避免误报），默认**不勾**、说明写"没锁住文件"。
  - **「能不能删 / 能不能改名」**（`DeleteCheck`）：拿 `DELETE` 权限开一次（共享模式放开
    `read|write|delete`，目录带 `FILE_FLAG_BACKUP_SEMANTICS`）：开得成 = 系统允许删除；`err=32` =
    有程序拦着；`err=5` = 权限。**实测"被打开着" ≠ "删不掉"**：用户那个文件夹独占探测 `err=32`
    但 DELETE 权限 `ok`（能删能改名）；桌面反而真被拦。
    **正在运行的 exe 这招测不出来**（镜像文件照样能拿 DELETE 权限开），所以那种一律由
    `FinishDeleteNote` 用"它自己在运行"覆盖掉结论。
  - **去重键必须是 PID + 来源**（`OfSource` / `UnlockForm.HasRow`）：同一个进程可能既是
    RM 报的"占着它"又是"它自己在运行"（你右键的正好是个在跑的 exe）—— 按 PID 一去重就会把
    "删不掉是因为它自己在运行"这条**最有用的**信息吞掉（实测踩过：`run=1` 却找不到 `run\tpid=` 行）。

- **结束进程要连子进程一起**（`FileLock.Kill`，v1.5.2 修；用户报「结束进程后窗口还在」）：
  安装包 / 启动器都是"父进程拉个子进程干活"（Inno Setup 还会把自己解到 `%TEMP%\is-*.tmp` 再跑），
  只杀父进程 = 锁解开了、窗口还留着（实测现场：`qingjian-server.exe` 的父进程已死、窗口还在）。
  修法：Toolhelp（`CreateToolhelp32Snapshot` + `Process32First/Next` 的 `th32ParentProcessID`）算出
  目标 + 所有后代 → **按层数深的先杀**（先子后父，顺手断掉"父进程被杀了又被守护子进程拉起来"）→
  子进程也要过一遍系统关键进程 / 工具箱自己的底线 → 确认框里写出"会连带结束哪几个子进程"。
  .NET Framework 4.8 的 `Process.Kill()` **没有整棵树的重载**，得自己按父子关系来。

- **底线（代码里写死 + M/N 组盯着）**：只结束用户勾选的进程；`explorer.exe` 默认不勾（结束它 = 桌面
  重启一次）；系统关键进程（System / csrss / winlogon / lsass / services…）**列出来但禁止勾选**；
  查不到就如实说查不到并列出可能原因，**不谎报「已解除」**。
  结果窗口是 `src\UnlockForm.cs`，**独立进程、不开主界面**（`Program.Main` 里 `rightmenu unlock` 走
  `Application.Run(new UnlockForm(paths))`）。
- **句柄级「强制解锁（不关程序）」**（`src\HandleUnlock.cs`，v1.5.2 新增）：用户问「**火绒的解除文件
  占用是怎么做的**」之后定的 —— 火绒靠**内核驱动 + SYSTEM 服务**遍历句柄表直接关掉对方的句柄，
  所以"全部解锁"而**不用关程序**。我们不用驱动、不提权，走**用户态那条路**：
  ① `NtQuerySystemInformation(SystemExtendedHandleInformation=64)` 一次拿全表
  （x64 每条 40 字节、表头 16 字节：`NumberOfHandles` + `Reserved`，别用老结构体的 24 字节版）；
  ② 只留"文件"类型句柄 —— **类型编号每个系统版本都不一样**（Win11=40 / Win10=37 / Win7=28），
  所以先开一个 `NUL` 句柄、回表里查它自己的 `ObjectTypeIndex`（**不写死版本号**）；
  ③ 先 `GetFileType` 过滤掉非磁盘文件（管道 / 设备）—— **管道句柄上查名字会阻塞**
  （社区文章原话；实测 `GetFinalPathNameByHandle` 把整个进程卡死过一次），这一步把会卡住的对象挡在门外；
  ④ 名字查询再套"**开线程 + 250ms 超时**"的保险，连着卡 5 条就收工并如实说"结果可能不全"；
  ⑤ `DuplicateHandle(..., DUPLICATE_SAME_ACCESS | DUPLICATE_CLOSE_SOURCE)` —— **这一句就是"解锁"**：
  把句柄复制过来的同时把**源进程里的那个关掉**；⑥ 关之前**再核对一遍路径**
  （句柄值会被系统回收再分配，绝不能拿旧值去关别人的别的东西）。
  实测：全表约 4000 条文件句柄、**700–950 ms** 扫完、零卡死；`FileShare.ReadWrite` 共享打开的文件
  （RM 看不见）和**目录句柄**都查得到。风险：句柄被从脚下抽走，那个程序可能报错 / 存不上盘 ——
  所以过确认框、默认不勾；**命令行故意只给只读入口**（`rightmenu handles <路径>`，`unlock --query-only`
  里也给 `child=` 预览），关句柄只能从界面点。系统进程 / 别的用户抽不动（要管理员）；
  **内核驱动自己持有的句柄谁都抽不掉**（火绒官方论坛原话「火绒剑无法摘除驱动句柄的」「暂不支持」）。
- **装 / 卸 / 状态**（`src\RightMenu.cs`）：写之前记原值（`rightmenu-installed.tsv`，和
  `sysreg-original.tsv` 同一套路）、写完读回核对、撤掉时**只删自己那几个 `Mxx1*` 键** ——
  同名键不是工具箱写的就跳过并在报告里说明（不覆盖、不删别人的东西）。
  **命令行只有只读入口**（`rightmenu status|items|list|help`、`rightmenu unlock --query-only <路径>`），
  写注册表只在界面里点（和 `sysreg` 同一条规矩）。
- **「右键菜单管理」**（`src\CtxMenu.cs` / `src\CtxMenuForm.cs`，2026-10-09 新增，按钮在「状态与修补」段）：
  按位置列出真实菜单项 + 禁用 / 恢复 / 删除。三条要记住的：
  ① **禁用写 `LegacyDisable` 空值**（不是改名 —— 改名实测没用）；② **删除先备份 .reg，备份没成功就绝不删**；
  ③ **扩展项这一版只显示不给动**（另一套机制：系统级黑名单 + 必须重启资源管理器 + 禁了是所有位置一起没）。
  只读出口 `rightmenu list [--zone=…]`；要管理员的项 = 提权重开窗口 + 用户**再点一次**（命令行里
  **不存在**"带动作"的参数，`rightmenu disable` 这种写法一律退出码 2）。正本 `docs\DESIGN.md` §12.66，
  回归 = 命令行 M49–M54 + 界面 C17–C19o。
- **菜单图标（v1.5.1，`src\MenuIcons.cs`）**：注册表的 `Icon` **只认"带图标资源的 exe/dll"或 `.ico`
  文件，指 `.png` 是无效的**；而工具箱那个 exe 自己也没有 `/win32icon`（`assets\app.ico` 不存在，
  `build.ps1` 那行等于没生效）—— 所以 v1.5.0 写 `Icon=<exe>` 的结果是**菜单里一片空白**（用户报的）。
  v1.5.2 收尾把 exe 自己的图标补上了（`tools\Make-AppIcon.ps1` → `assets\app.ico` → `/win32icon`，
  另外窗口那一份走 `src\AppIcon.cs`），但菜单图标**还是**用生成的 `.ico`（它才是按 DPI 精确的那一档）。
  现在装菜单时把内嵌的按钮 PNG 拼成 32 位 DIB 的 `.ico`（16/20/24/32 四个尺寸，文件名带源图指纹），
  写到 `%LOCALAPPDATA%\mxx1-toolbox\rightmenu-icons\`，**两个父项 + 子菜单每一项**都写 `Icon`
  并读回核对（指不到文件就不写这个值，不留空白图标位）；撤掉两项时把生成的 .ico 一起清掉。
- **图标"看起来没生效"的正确验法和两个坑**（v1.5.2 踩完记下来的）：
  - **别用 `new Icon(stream, w, h)` 验图标**：那个 GDI+ 重载对**任何** `.ico` 都会抛
    「Argument 'picture' must be a picture that can be used as a Icon」，拿它当判据会把好图标
    误判成坏的（我上一轮就这么误判过一次）。要验就用**系统自己的装载器**：
    `SHDefExtractIcon`（返回 `hr=0` 且 HICON 非空）或 `PrivateExtractIcons`（能按 16/20/24/32 取），
    再 `Icon.FromHandle(...).ToBitmap()` 数一下不透明像素（正常一张 16×16 按钮图约 **219 个**）。
  - **图标目录被删过一次 = 用户菜单一片空白**：测试项和用户真实那份菜单**曾经共用一个目录**
    （`rightmenu-icons`），测试卸载时 `MenuIcons.RemoveAll()` 把整个目录删了 → 用户菜单指向的 `.ico`
    全没了。现在**按注册表根分开**：测试根用 `rightmenu-icons-test`（`MenuIcons.Dir` 看
    `MXX1_RIGHTMENU_ROOT`）。**恢复办法就是让工具箱跑一次**：启动时 `SyncIfInstalled` 会补齐缺失的
    `.ico` 并重写键（实测用户 22:10 打开一次工具箱，图标就回来了；状态行还会念"菜单图标 8 个都在"）。
- **占位符按位置写（v1.5.1 修的）**：文件 / 文件夹是 `%1`，但**「文件夹里的空白处」和「桌面空白处」
  必须用 `%V`** —— 那两个位置资源管理器**不替换 `%1`**，会把字面量 `%1` 当路径传进来
  （`RightMenuLocation.Placeholder`；`SelfCheck` 见到"路径不存在"时会提示这一点）。
- **旧版装出来的键会自动修补**：`RightMenu.SyncIfInstalled` 除了重建子菜单，还把自己写的 verb
  修补到当前版本（占位符 + 图标），**主窗口启动时也调一次** —— 用户不用自己想到"要再点一次装上"。
  环境变量 `MXX1_NO_RIGHTMENU_SYNC=1` 可以关掉（界面回归测试用它：测试不该改用户真实的注册表）。
- **子菜单内容会自动跟着变**：点一次按钮（`PushRecent` 之后）、置顶 / 取消置顶、清空最近使用之后
  都会重建（只在"装了「常用功能」"时才动注册表；没装就空转）；另有「重建常用功能」手动兜底。
  清单里 `danger: true` 的按钮进菜单时命令带 `--confirm`，点了先弹自家确认框（`run <id> --confirm`）。
  挑不出东西的按钮（灰色占位 / 界面动作 / 隐藏 / 「右键增强」自己这一页）不进菜单。
- **测试隔离**：环境变量 `MXX1_RIGHTMENU_ROOT` 把根挪到 `HKCU\Software\mxx1-toolbox\rightmenu-test`。
  **回归一律用它，绝不把测试项真装到用户的右键菜单上**（M22 断言用户真实的
  `HKCU\Software\Classes\*\shell` **测试前后一个键都没变** —— 注意判据是"没变"，不是"里面不许有
  我们的键"：用户自己点过「装上…」是正常状态，v1.5.0 那条断言把他自己的安装当成了失败，M22b 现在
  专门认这件事；M23 断言测试根和原值记录都收拾干净了）。
- **「最近使用」上限 30**（`UserTools.RecentLimit`，v1.5.0 从 12 改上来）：工具箱「常用」页签那条
  「最近使用 · N 个（最多 30 个）」跟着变，右键「常用功能」子菜单里的「最近用过」也按这个数。
- **右键菜单管理器（Context Menu Manager Plus）**：用户提醒过「我电脑装了 Context Menu Manager Plus，
  你给的叫我审核」。实测（本机 `D:\Context Menu Manager Plus` v1.6.8，带服务 +
  shell 扩展 `{004B0726-…}`，数据在 `C:\ProgramData\ContextMenuMgr\`）：它靠
  `backend-protection-settings.json` 里的 **`lockNewContextMenuItems`** 决定新装进去的项要不要走
  "待审核"（开着就必须去它那里放行才出现在右键里）；它自己的 `context-menu-state.json` 逐项记着
  `isPendingApproval` / `desiredEnabled` / `onlyWithShift`。所以「右键菜单状态」会主动念出
  「装了什么版本的菜单管理器 / 它的锁定开关是开是关 / 我们这两项被它标成待审核没有」
  （`RightMenu.CcmpNote()`，路径用系统文件夹拼，**代码里不写本机绝对路径**）。
- **还需要人眼确认的事情**（自动化回归验不了，别假装测过）：① 右键菜单里真的出现了这两项
  （Win11 要先点「显示更多选项」；被菜单管理器藏了要去放行）；② 多选几个文件时命令被调用几次
  （现在写的是 `MultiSelectModel=Player`）；③ **图标看着行不行**（v1.5.1 才做出来，是 16px 按钮图标
  放大到 DPI 需要的尺寸，125% / 150% 下糊不糊得用户自己看 —— 觉得糊就得让 `Make-Icons.ps1`
  另画一套大尺寸的菜单图标）。见 `docs\DESIGN.md` §14.10。

### 第 8 个按钮：隔壁「永久删除工具」（零改动集成）

**「永久删除工具」`permdel.gui`** = 不带参数启动隔壁 exe = 开它自己的窗口
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

## 说明文案的两层（未发布这一轮起，改文案之前先看这一条）

- **`hint`（悬停那一句）**：鼠标停在按钮上的 tooltip = 名称 + `hint` + 危险/管理员/灰色边界（`MainForm.TipFor`）。
  要短 —— **超过 120 字 L0 体检当场报 FAIL**（悬停是一行一句，长了在 tooltip 里就是一堵墙）。
- **`about`（详情整段）**：右键按钮 →「功能说明…」，或选中按钮按 **F1**。正文由 `MainForm.HelpText()`
  拼成三段：它是干什么的（= `hint`）/ 怎么用（= `about`）/ 点下去会执行什么（= `Launcher.DescribeCommand`）。
- **正文只有一个拼装处**：说明窗口（`src\HelpForm.cs`）和 `MXX1Toolbox.exe tip <id> --full` 用的是同一个函数
  —— 所以"窗口里到底写了什么"能被自动断言（T24–T26 / C03h），也**不许在 C# 里另抄一份文案**。
- **只写一半的说明比不写更糟**：写了 `about` 却没有 `hint` → L0 报 FAIL（用户会以为那就是全部）。
- 「说明」和「查看按钮定义」是两件事：前者给人看，后者给排查问题看（来源清单 / 路径 / 启动命令）。

## 编码红线（踩过两次，能静默毁功能）

- `.cs` / `.ps1` 必须 **UTF-8 带 BOM**（csc 与 PS 5.1 都按 ANSI 解码无 BOM 的文件）；
  `.md` / `.yml` / `.json` **无 BOM**。`build.ps1` 会给 `src\*.cs` / `tests\*.ps1` / `tools\*.ps1` 自动补 BOM，
  并去掉 `tools\*.json` 的 BOM；提交前跑 `tools\Test-Encoding.ps1`（`-Fix` 可修）。
- **补丁/编辑工具会静默吃掉 BOM，所以"改完先修再跑"是硬顺序**：2026-10-04 给 `tests\Test-Cli.ps1`
  追加 M 组之后直接跑回归，PowerShell 5.1 按 GBK 读中文 → 满屏 `Unexpected token '}'`，
  报错位置还全在别的函数上（看着像"我改错了"，其实是编码）。**改完任何 `.cs` / `.ps1`：
  先 `tools\Test-Encoding.ps1 -Fix` → 再 `build.ps1` → 最后跑测试。**
- **没有 BOM 的临时脚本 + 中文注释 = PS 5.1 按 GBK 读，注释可能吞掉换行**（本仓库踩过：
  补丁脚本解析报一堆莫名其妙的错）。写一次性脚本要么纯 ASCII 注释，要么用 `pwsh`（7）跑。
- **`$ErrorActionPreference='Stop'` 下别调原生命令**：PS 5.1 会把原生命令写到 stderr 的每一行当成
  **终止性错误**（`2>$null` 也拦不住）→ 命令中断、`$LASTEXITCODE` 变 `-1`。闸门就是栽在这上面
  （git 的 CRLF warning → 读不出改动 → 静默退回全套跑）。**见坑 31**，自检在
  `tools\Test-InlineSyntax.ps1` 末尾。
- **C# 里别用 `File` / `Shell` / `Url` 这种方法名**：它们会盖住 `System.IO.File`，
  于是类里每个 `File.Exists` 都编译不过（CS0119）。用 `FileTarget` / `ShellTarget` / `UrlTarget`。

## 测试怎么用

- **别每次都跑全套**（2026-10-06 用户拍板：「单独改一个功能或者添加一个功能不应该影响到其他功能，
  那其他功能就不用测试」）。分层跑法（正本 `docs\DESIGN.md` **§15**）：

  | 层 | 跑什么 | 耗时 | 什么时候 |
  | --- | --- | --- | --- |
  | L0 | 编码体检 + 内联体检 | 约 5 秒 | **每次改完都跑，不商量**（BOM 掉了 / 清单坏了只有这两关能拦） |
  | L1 | 映射到的命令行组（`-Only`） | 4 - 60 秒 | 提交前：`powershell -File tools\Test-Quick.ps1` |
  | L2 | 映射到的界面组 | 约 2 分钟 | 涉及界面 / 互操作，且**你没开着工具箱** |
  | L3 | 命令行全套 280 项 | 约 2 分钟 | 推上去之后 CI 跑（在你机器之外） |
  | L4 | 全套（命令行 280 + 界面 166） | 约 5 分钟 | **发版前一次** |

- **挑组**：两个套件都支持 `-Only M,N` / `-Skip P`（组标记就是源码里 `# ---- X 组：…` 那行的字母，
  前缀匹配）。**命令行挑组必须带上 A 组**（B/C/F/H/I/K/M/P/S 都读 A 跑出来的 `$status` 等公共量；
  `Test-Quick.ps1` 会自动加）。汇总里会打 `本次没跑的组: …` —— **那一行要写进提交信息与汇报**，
  **发版前要清空这份欠账**（v1.5.4 就是带着"界面回归没复跑"发出去的，那之后才定的这条规矩）。
- **别让它动不动跑全套**（2026-10-06 晚，用户问「耗时最长的步骤是什么，感觉很浪费时间」之后改的；
  正本 `docs\DESIGN.md` **§15.5**）。三条现在都生效了：
  - **闸门会跳过刚跑过的**：同一套输入（改动文件 + 组名单 + 开关）30 分钟内跑通过 → 套件跳过，
    只跑 L0 + 编译（约 6 秒）。要强制重跑：`-Force`。**别改完一个小东西就反复跑闸门** ——
    但反过来也别以为"跳过了就是没测"，它跳过 = 这套几分钟前刚跑通过。
  - **改了"套件自己"按 diff 行号精确到组**（2026-10-06 深夜改：用户问「加一个功能不就是应该只测
    一个功能的就行了？为什么要跑全套测试回答我」）：改动落在哪个 `# ---- X 组` 块里**就只跑哪一组**
    （命令行自动带前置 A）；**只有动了公共区**（文件头 / 探针辅助 / 挑组逻辑 / 夹具准备）或者
    读不出改了哪一块，才退回整套。实测：往 T 组块里加一行注释 = `A,T` 47 项 **4.5 秒**（改之前
    是整套 234 项 95 秒）；改映射表 / 闸门自己（L0 那一层）不强制任何套件（回归靠闸门两道自检：
    组名核对 + 覆盖率核对）。改 `src\MainForm.cs` 级的一次约 **26 秒**，不再是一分钟起步。
  - **默认口径（用户拍板）**：**功能做完先跑这个功能自己的组**（例：改哈希校验 = 命令行 `-Only T`
    2.9 秒 + 界面 `-Only C` 19 秒）；**全套 / 完整界面回归只在发版前、或者用户明确要求时跑一次** ——
    别再由着自己"顺手跑一遍全套"。
  - **只想验一小块时手写挑组**：`Test-Gui.ps1 -Only A`（只有十几条、几十秒），
    `Test-Cli.ps1 -Only A,E`（4 秒）—— 清"界面回归欠账"不必每次都拉 161 项全量。
- **必须先确认工具箱没开着**（`Get-Process Mxx1Toolbox` 空）：两个套件都会写 `settings.ini` /
  暂停用户 `tools.json`，正跑着的那个实例会把内存状态写回去 → 一整套假红。**不要杀用户的进程。**
- **一律 `powershell -ExecutionPolicy Bypass -File …`**（Windows PowerShell **5.1**，**不是 pwsh**）。
  两个坑都是 2026-10-04 用 pwsh 跑时踩的：① 套件里有 `Get-Content -Encoding Byte`
  （PS 7 改成了 `-AsByteStream`）→ 跑到 M20 当场抛 `'Byte' is not a supported encoding name`
  并**中断整个套件**（那一次用户的 `tools.json` 就留在暂停状态了，见 §12 坑 12）；
  ② `-ExecutionPolicy Bypass` 会设 `PSExecutionPolicyPreference` 环境变量、**子进程继承** ——
  M14f 那个"父进程拉个子进程"的现场靠它（这台机器策略是 Restricted，不走 Bypass 时子进程
  `-File child.ps1` 直接被拒 → `child=0` 假红）。
- **环境不满足的项写 `Skip '名字' '原因'`，不要写 `Check ... $false 'skipped'`**：后者把"没测到"
  记成"失败"。克隆仓库的人（没有隔壁 `permanent-delete-menu`、工具目录里已经放了 exe、没装 COM）
  本来就会遇到 C/F/G/R 组那几项 —— 现在它们打印 `[SKIP]`、计入跳过数，**退出码仍是 0**；
  同理 `A15`（没生成 `assets\app.ico`）也走 Skip。**但"真的断言失败"绝不许改成 Skip**。
- **跑界面回归之前，用户不能开着工具箱**：`Test-Gui` 会写 `settings.ini`（主题 / 页签 / 关闭确认），
  而正在运行的那个实例关闭时会把自己的内存状态写回去 → 一整套假红（2026-10-04 真踩过：
  6 项红 + 条款确认窗口在测试中途冒出来）。先确认 `Get-Process Mxx1Toolbox` 是空的，**不要杀用户的进程**。
- `tests\Test-Gui.ps1` 用 Win32 探针，不看截图：`EnumChildWindows` + `GetWindowRect` 判重叠
  （先排除"完整包住别人"的容器）、`GetWindowLong(GWL_STYLE)` 判标题栏、
  `PostMessage(BM_CLICK)` 真点按钮、`WM_GETTEXT` 跨进程读文字。
  按钮清单**从 `Mxx1Toolbox.exe list` 读**，两边必须一致（别在测试里写死名单）。
- `tests\Test-Cli.ps1` 里 `run <id> --dry` 会把 12 个系统工具全解析一遍（不许真的开 12 个窗口），
  并且直接问隔壁 `PermanentDeleteSetup.exe status` 拿 `installed=` —— "找得到 + 真能跑"都证明一次。
> **30 条坑已挪到 `PITFALLS.md`**（`skill\mxx1-toolbox\PITFALLS.md`）。
> 里面是"容易写错的断言 / 哪些做法踩过"那 30 条，逐条写清楚了根因与判据 ——
> 改测试、改界面探针、改文案之前**按需读一次**，别在没读的情况下照着直觉写。

- **别在 PowerShell 里按像素调函数**：一个 `Get-Pixel` 每像素调一次，几万次调用要几分钟，
  看起来像卡死（踩过一次）。要么 `LockBits` 取一次 `byte[]` 再纯数组循环（`Get-InkRows` 的写法），
  要么用 csc 编个临时小工具（`local\InkDiag.cs` 那种）。
- **改完任何 `.ps1` / `.cs` 都回头看一眼 BOM**：`edit` 类工具会静默吃掉 BOM，而 PowerShell 5.1
  读无 BOM 的 `.ps1` 按 GBK 解 → 中文注释变成语法错误，报错位置还完全不相干。
  改完跑 `tools\Test-Encoding.ps1 -Fix`，再 `build.ps1`。
- **重编时如果 `bin\Mxx1Toolbox.exe` 被占用，不要杀进程**：`build.ps1` 会自动把旧 exe 改名成
  `Mxx1Toolbox.exe.old-<时分秒>` 再编（用户可能正开着界面在用）。

## 待办 / 别自己替他决定

1. **Git**：本地仓库 + **GitHub 远程 <https://github.com/2604290100/mxx1-toolbox> 都已建**
   （2026-10-05 用户拍板"建一个"之后建的）。**推送前依然要先问用户**
   （原话："推送的时候不要每次都推送，太卡了要问过我才行"）。
2. **外部工具目录 `bin-tools\`**：① 工具目录、③ 扫 `tool.json` 自动长按钮 **都已实现**（见上面那节与
   `docs\DESIGN.md` §13.7）。还没做的只有 ② 「把 `bin-tools\` 里的 exe 内嵌进 exe、首次点击释放到固定目录」
   —— **等用户说"我只想拷一个 exe"再做，别自己开工。**
3. ✅ **v1.5.5 已经发布**（2026-10-09）：版本号 `1.5.5`（`src\AssemblyInfo.cs`，标题栏跟着变 ——
   唯一来源），`CHANGELOG.md` 里是 `[1.5.5] - 2026-10-09`；已打 tag **`v1.5.5`** 并发 Release
   （附 `Mxx1Toolbox.exe` 842,240 字节 + `Mxx1Toolbox-package.zip` 1,308,446 字节 + 说明里的 SHA256；
   zip 发版后因为"CI 那次假红"重传过一版、exe 没变），CI 绿的。
   ⚠️ **这一版没改条款正文**（`docs\DISCLAIMER.md` 一个字没动）→ 同意指纹没变，
   用户这次打开**不会**再被要求确认。
   上一版（v1.5.4，2026-10-06）指向 `e5c1bdb`。
   **下一次要发版还是先问用户**（原话："推送的时候不要每次都推送，太卡了要问过我才行"、"发布工具"
   —— Release 的说明文字要他点头）。
   流程照旧：`build.ps1 -Package` → 算 SHA256 → `git tag -a vX.Y.Z` + push →
   `gh release create vX.Y.Z bin\Mxx1Toolbox.exe bin\Mxx1Toolbox-package.zip --title … --notes-file …`。
   ⚠️ 改版本号要连测试一起改：`tests\Test-Cli.ps1` 的 **A03**（`status` 报的版本号）与
   **S11**（`checkupdate` 关掉时也报版本号）两处是硬编码断言，漏改就红。
4. 条款正文（`docs\DISCLAIMER.md`）**改一个字就会让所有人下次打开重新确认一次**（记的是正文指纹）。
   **v1.5.4 这一轮改过三次**（① 补第三项与「一键解除占用」的行为 / 底线 / 风险；
   ② 把"右下角气泡"改成"鼠标旁边的小提示卡"；③ 底线里加上"不是你自己账户的程序也一律不动"
   —— 就是坑 29 那个 CI 事故的修法），所以用户下次打开会再看到一次《使用条款确认》
   —— **已经跟他说明过**；想免打扰：`bin\Mxx1Toolbox.exe consent --accept`。以后再改正文同样要先说一声。
5. P1 的范围（先接哪个页签的真功能）要问用户，别自己挑。
6. 图标要改样式就动 `tools\Make-Icons.ps1` 的关键词映射 / 配色，然后按
   `build.ps1 → Make-Icons.ps1 → 删孤儿 → build.ps1` 的顺序跑。
7. skill 三处同步（现在是**三份文件 × 三处 = 九份**：`SKILL.md` / `PITFALLS.md` / `HISTORY.md`）：
   `D:\萌新工具开发\.dsh\skills\mxx1-toolbox\`、仓库内 `toolbox\skill\mxx1-toolbox\`、
   `%USERPROFILE%\.dsh\skills\mxx1-toolbox\`。**别手工拷**，跑
   `powershell -File tools\Sync-Skill.ps1`（`-Check` 只核对，会逐个比 SHA256）。
   正本是**仓库里那份**；工作区级与用户级那两份是给 DSH 加载用的。
8. **流程约定（2026-10-06 用户拍板，正本 `docs\DESIGN.md` §15.4）**：
   ① 本地随便提交，**push / 发版要用户点头**（原话"本地推送可以线上推送得经过我点头"）；
   ② 每次向用户汇报**带一行「未推送提交」**（`git log origin/main..HEAD --oneline` 的真实输出，
   别手写状态）；③ 提交信息写四段：改了什么 / 为什么 / 我验了什么 / 我没验什么；
   ④ 提交前跑 `tools\Test-Quick.ps1`，汇报里指名跳过了哪些组；⑤ **发版前清空"没跑的测试"欠账**。
9. ⚠️ **界面回归的欠账**（2026-10-06 晚）：v1.5.4 发版前没复跑，之后补跑了两次 ——
   一次 150 通过 + 1 失败（N21b，环境：鼠标挪不动，现在会走 `[SKIP]`），
   一次 149 通过 + 2 失败（B10/B11，环境：用户当时开着自己的工具箱，现在会走 `[SKIP]`）。
   **这两条已经改成"环境不满足就 Skip"**，并且 2026-10-06 晚在"工具箱关着"的条件下**跑过完整界面回归**
   （**161 通过 / 0 失败**，含哈希工具那 8 条）—— 这一笔欠账算清了。
   那一晚后来又跑过两次完整界面回归，都是 **164 通过 / 0 失败**（最近一次含提示卡那 7 条，
   `N21b` 那次真的挪动了鼠标、`卡片挪=0,0`）—— 所以到目前为止**没有未清的界面欠账**。
   命令行的欠账看闸门尾部的「本次没跑的组」那一行。
   下次方便时（用户关掉工具箱）想复跑：`powershell -File tests\Test-Gui.ps1`。