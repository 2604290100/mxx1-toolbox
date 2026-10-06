---
name: mxx1-toolbox
description: Use when working on "萌新工具箱 / mxx1 Toolbox" — the Windows button-wall launcher whose main window is a multi-row, multi-column grid of small buttons that each start another program, script, or feature. Covers its interface rules (four column compact buttons, top tabs, separator segments, light/dark theme, computed column width, grey "not wired up yet" buttons), the tools\*.json button registry, bin-tools\ auto-loaded tool buttons (tool.json), how buttons launch things (exe / script / open / builtin / macro), how the 26 系统工具 buttons open Windows components, how the 常用设置 registry switches record the original value so every change can be undone, how a run reports success or failure, the first-run consent gate (免责声明与服务条款 fingerprint), the read-only update check, Windows 7/10/11 compatibility, plus build.ps1, screenshot tool and the GUI/CLI test suites.
---

# 萌新工具箱（mxx1 Toolbox）

**主界面 = 多行多列的小按钮墙**，点一下按钮就启动一个已经做好的程序 / 脚本 / 功能。
加按钮只是往 `tools\*.json` 丢配置，**不需要重新编译主程序**。

- 程序名 **萌新工具箱**，标题栏 `萌新工具箱 v1.5.4`；署名 `mxx1` / `mxx1.cn`；GPL-3.0-or-later
- 工程目录 **`D:\萌新工具开发\toolbox\`**，与隔壁 `permanent-delete-menu` **互不修改**（只调它的 exe）
- 支持范围 **Windows 7 SP1 / 10 / 11**（用户 2026-10-05 收窄的：**只考虑这三版**）；见「兼容性」一节
- 外观参考：`C:\Users\Administrator\Pictures\Snipaste_2026-10-04_10-29-34.png`（那种紧凑按钮墙）

> **接手 / 新会话先做两件事**：读 `docs\DESIGN.md`（外观与行为的**唯一正本**）和本文件。
> 设计一改先改 `DESIGN.md`，再同步本 skill —— 两份分叉就会出现"两套行为"。

## 当前状态（2026-10-06，v1.5.4）

- ⚠️ **2026-10-06 补一轮（用户拿到 v1.5.4 之前的构建之后报的）**：原话
  **「气泡没有正常弹出，而且弹出的位置要跟随鼠标」**。根因：提示走的是**系统托盘气泡**
  （`NotifyIcon.ShowBalloonTip`）—— 能不能看见由**用户的系统通知设置**说了算（这台是精简版
  Windows，通知平台被裁过；Win10 / Win11 关掉「通知」或开着专注助手同样看不到），
  而且位置由系统钉在**右下角**、离鼠标很远。现在改成**自己画的一张卡片**
  （`src\Balloon.cs` 的 `NoticeForm`）：位置在**鼠标旁边**（右下 18/22，贴边翻到另一侧并夹进
  那块屏幕的工作区）、**鼠标动它跟着动**（60ms 一拍、挪够 8 像素才动）、默认 6 秒自己消失、
  点一下就关、**不抢焦点**（`ShowWithoutActivation` + `WS_EX_NOACTIVATE`）、不占任务栏。
  新增 `--notify=<毫秒>` 定显示多久。见 `docs\DESIGN.md` **§12.56**；
  回归 **N21 / N21b / N21c / N21d**（末尾那条"卡片上真的有字"是抓 `PrintWindow` 像素数的墨迹
  —— **"有窗口"不等于"看得见"**，用户报的就是"看不到"）。见坑 28。
- ✅ **2026-10-05 那一轮（已随 v1.5.4 一起发）**：用户问
  **「工具箱里面的解除文件占用功能还有没有优化的空间？或者出一个不弹出窗口的版本」**，
  拍板时说的是 **「保留现有的功能的前提下加个不弹窗的一键解除，对应也要单独加 2 个按钮
  一个添加右键一个撤销右键」**，按钮名由用户点名：**装上一键解除占用 / 撤掉一键解除占用**。
  这一轮 = 两条治本 + 第三个右键项：
  ① **小窗口在查的时候是死的**（实测右键一个 400 个文件的文件夹：窗口 184ms 出现、
  612ms 起完全没响应，一直到 7093ms）→ 扫描挪到**后台线程** + 状态行念秒数 + 查的时候按钮禁用。
  见 `docs\DESIGN.md` §12.54；回归 **N20**（最长没人应 < 500ms，修好后实测 **0ms**）。
  ② **「是哪个文件被占着」在大文件夹里报不出来**（原来只查**前 60 个**文件，实测 400 个文件里
  占的是第 200 个 → 只会说"没定位到"）→ 改成**二分定位**（9 层约 18 次查询就指名，还比原来快）。
  见 §12.55。
  ③ **新入口「一键解除占用」**（`rightmenu unlock --auto`，`src\AutoUnlock.cs` + `src\Balloon.cs`）：
  **不弹窗口**，后台查到谁占着它就**直接结束那些程序**，鼠标旁边一张提示卡说结果；
  装 / 撤是**单独一对按钮**（`rightmenu.auto.on` / `rightmenu.auto.off`，页签 8 → **10 个按钮**）。
  底线比窗口那条更窄（系统关键进程 / explorer / 工具箱自己一律不动；没同意条款一个进程都不碰）。
  见 §14.14；回归 **M30–M38** + 界面 **N19/N19b/N21\***。
  顺手还修了 `Make-Icons.ps1` 会给 `bin-tools\` 自动按钮画图标并提交进仓库的问题（见坑 27）。
- ✅ **测试 356 项全绿**（本机 355 通过 + 1 跳过）：命令行回归 **206**（A03b–A03d/D01 盯兼容、R 组 14 项盯 **bin-tools 自动按钮**、
  P 组 10 项盯**发布包内容**、S 组 26 项盯**条款确认门 + 更新检查**、**M 组 38 项盯「右键增强」**、A14–A17 盯 **exe 自己的图标**）
  + 界面回归 **150**（I 组 27 项把**条款确认窗口**真开起来点一遍（含 I10b/I10c）、**N 组 27 项**盯「解除文件占用」小窗口 +
  「一键解除占用」不弹窗口 + 鼠标旁边的提示卡）
  （外加编码体检 186 个文件、内联脚本与清单体检 34 个脚本 / 7 个清单 + 每个 `.ps1` 的语法）。
  产物 `bin\Mxx1Toolbox.exe`（786,432 字节单文件 / 约 768 KB），
  七个 `tools.*.json` + 114 个 `icons.*.png` + **`Disclaimer.md`（改过两次：第三项与一键解除那几段 +
  「鼠标旁边的小提示卡」那两处）** + `assets\app.ico` 那份程序图标 已内嵌。
  ⚠️ **条款正文又改过了**（`docs\DISCLAIMER.md`，v1.5.4 两次）→ 指纹变了 → **所有人（包括用户自己）下次打开界面
  会重新看到一次《使用条款确认》**，这是设计如此（想免打扰：`Mxx1Toolbox.exe consent --accept`）。
  **跑法固定：`powershell -ExecutionPolicy Bypass -File tests\Test-All.ps1`**（必须 Windows PowerShell
  5.1 —— 套件里有 `-Encoding Byte`，pwsh 7 改叫 `-AsByteStream`，跑到 M20 会当场中断；`Bypass`
  还会让子进程继承执行策略，M14f 那个"父进程拉子进程"的现场靠它。见 `docs\DESIGN.md` §15）。
  **环境不满足的项走 `Skip()`**（打印 `[SKIP]`，不算失败）：原来的写法是 `Check ... $false 'skipped'`，
  那会把"没测到"记成"失败" —— 克隆仓库的人在 C/F/G/R 组会一片假红。
- ⚠️ **发布当天（2026-10-05）用户拿到 Release 后又报了两个真 bug，都已修并重传了资产**：
  ① **打包漏文件**（「bin-tools 里面只有 PermanentDeleteSetup.exe 进压缩包了，memreduct 没有进」）——
  打包原来写在 `build.ps1` 里，`bin-tools\` 只硬编码拷隔壁那一个安装器；同一处 `Copy-Item` 带通配符拷目录
  **不带 `-Recurse` 只建空目录**，于是 `assets\icons\` 一百多张图标在包里是**空的**。
  现在独立成 **`tools\Make-Package.ps1`**（逐条列文件 + 拷整棵树 + **回读 zip 逐个核对**，少一个就失败），
  由 `build.ps1 -Package` 调用，命令行回归 **P 组**盯着它。见 `docs\DESIGN.md` §12.52。
  ② **同意记录被"关闭窗口"抹掉**（「使用条款确认 每次打开都弹」）—— 主窗口那份 `Settings` 是
  **构造函数**里加载的（比 `OnLoad` 的确认门早），关窗口时 `Save()` 拿旧快照把刚写下的指纹覆盖成空。
  修法：`Settings.Save()` 里同意记录**只认磁盘那份**（`ConsentTouched` 标记由 `Consent.Accept/Reset` 置上），
  确认通过后顺手刷新快照。见 `docs\DESIGN.md` §12.53，回归 I10b/I10c。
- ✅ **v1.5.3（2026-10-05）四件事**：① **`bin-tools\<工具>\` 自动长按钮**（用户问
  「bin-tools 里面的工具是不是应该自动加载一个按钮？」）；② **完整的更新检查 + 免责声明与服务条款 +
  首次运行确认门**（用户点名照隔壁 `permanent-delete-menu` 那套做）；
  ③ **Win7 / Win10 / Win11 兼容**（用户：「兼容只需要考虑 win7 win10 win11 就行了」）；
  ④ **建 GitHub 仓库并推送**（用户：「你做好以后就上传仓库吧，没有建仓库那就建一个」）。
  细节见下面各节，设计正本 `docs\DESIGN.md` §13.7 / §16 / §17。
- ⚠️ **发布当天抓到的真 bug：更新检查的接口地址被写成了网页地址**（`2604290100/mxx1-toolbox`）。
  原来 `UpdateCheck.ReleasesApi/TagsApi` 是拿 `AboutForm.RepoUrl` 拼的 →
  `https://github.com/<账号>/<仓库>/releases/latest` 是 **HTML 页面**，请求里带着
  `Accept: application/vnd.github+json` 时 GitHub 回 **406**，用户那边永远「检查失败：http-406」。
  现在从仓库地址现推接口根（`MakeApiBase()` → `https://api.github.com/repos/<账号>/<仓库>`）。
  **为什么两套测试都没抓到**：S 组全程用 `MXX1_UPDATE_URL` 指到本机假接口，绕开了默认值 ——
  补了 **S01b**（只看 `checkupdate` 打印的 `api=` 那一行，**不联网也能跑**）。
  **教训：凡是"默认值只在真实环境生效"的东西，必须有一条不依赖外部服务的断言盯着它。**
- ⚠️ **v1.5.2 收尾修的三件事**（用户当天第三轮反馈）：① **编出来的 exe 没有图标** ——
  `build.ps1` 里 `/win32icon:assets\app.ico` 要的文件**从来不存在**（那行等于没写），
  现在有了 `assets\app.ico`（`tools\Make-AppIcon.ps1` 生成，八尺寸）+ `src\AppIcon.cs`
  （WinForms 窗口的标题栏 / 任务栏图标是**另一回事**，不设 `Form.Icon` 就是它自带的空白窗体图标）；
  ② **「解除文件占用」小窗口高度不跟着内容变**（原来是 `210 + 行数 × 20`，上面几行文字换行没算，
  正文长时被切）→ 现在逐块量出来相加；③ **界面文案不许写成"推理"**（用户原话
  「那些提示不要做得太像AI了，明明都是固定的功能，非要说什么线索」）—— 详见"界面硬规则"里那条"用户可见文案一律直白"。
- ✅ **v1.5.1 修的是用户当天报的两个问题**（装完 v1.5.0 之后）：
  ① **「解除文件占用」右键文件夹扫不到占用** —— 原来只枚举文件夹**第一层**的文件，第一层全是
  子文件夹时直接放弃，而"占用它的是子文件夹里的 Word / PDF"正是最常用的场景；
  ② **右键菜单项没有图标** —— 注册表 `Icon` 指的 exe 从来没有 `/win32icon`（`assets\app.ico`
  不存在），而 `Icon` 又不认 `.png`。两条的根因 / 修法 / 实测写在 `docs\DESIGN.md` §14.11。
- ✅ **八个页签、112 个内置按钮，全部是真功能，灰色占位一个不剩**：`常用`（置顶 + 最近使用**最多 30 个**，
  算出来的）/ `常用设置` 33 / `系统工具` 26 / `隐私设置` 29（11 组成对开关 + 4 个权限入口 +
  状态/优化/还原）/ `应用管理` 5 / `清理优化` 8 / `右键增强` 8 / `我的工具` 3（新建 / 导出 / 导入，真）。
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
- ✅ **「右键增强」8 个按钮（v1.5.0 / v1.5.1）**：前 7 个是工具箱自己在 `HKCU\Software\Classes` 下装的两样东西
  （「解除文件占用」verb + 「常用功能」级联子菜单）—— 装 / 撤 / 状态 / 重建 / 说明；第 8 个还是
  「永久删除工具」（不带参数启动隔壁 `PermanentDeleteSetup.exe`，开它自己的窗口）。详见下面那一节。
  v1.5.1 把两件事补上了：**右键文件夹会往下扫 4 层**（并指名是哪个文件被占着）、
  **菜单项的图标**（装的时候把内嵌 PNG 转成 `.ico`，两个父项 + 子菜单每一项都有）。
- ✅ **GitHub 仓库已建并发布**：<https://github.com/2604290100/mxx1-toolbox>（账号 `2604290100`，
  2026-10-05 由用户拍板"建一个"之后建的；用户还选了"顺手打 tag 发 Release"）。
  已打 tag **`v1.5.3`**（指向最终提交）并发 Release，附 `Mxx1Toolbox.exe` 与 `Mxx1Toolbox-package.zip`
  （发布说明里带 SHA256）；CI 是绿的（编码体检 + 内联体检 + 编译 + 命令行回归）。
  **推送前依然要先问用户**（原话："推送的时候不要每次都推送，太卡了要问过我才行"）。
  ⚠️ **同一天用户拿到包之后报了两个 bug，已修并"原样重传"了 `v1.5.3` 的两个资产**
  （用户 2026-10-05 的选择：不另开版本号，直接换掉；发布说明里的 SHA256 也换成了新的）。
  流程：`build.ps1 -Package`（编 + 打 → `bin\Mxx1Toolbox-package.zip`）→
  `gh release upload v1.5.3 bin\Mxx1Toolbox.exe bin\Mxx1Toolbox-package.zip --clobber` →
  用 `Get-FileHash` 算新的 SHA256 更新说明（`gh release edit v1.5.3 --notes-file …`）。
  **注意**：这样 tag 指向的那个提交不再等于资产的内容（zip 里的 `build.ps1` 是新的）——
  用户明确选了这个做法；下次要避免这种错位就改版本号发新 tag。
- ✅ 按钮图标：112 个 16×16 PNG 由 `tools\Make-Icons.ps1` 生成并内嵌（`icons.<id>.png`；
  用户自建按钮的图标**不**生成，免得把别人机器上的东西提交进仓库），
  全部经 `IconFactory.Normalize()` 归一化成 16×15 画布（见"界面硬规则"里那条）。
  优先级：清单里的 `icon` > `assets\icons\<id>.png` > 内嵌 > 程序内实时画的占位图标。
- ✅ **程序自己的图标**（v1.5.2 收尾）：`assets\app.ico` = `tools\Make-AppIcon.ps1` 生成的
  圆角蓝底 + 2×2 白色方块（"一墙按钮"），**16/20/24/32/48/64/128/256 八个尺寸各画一遍**、
  32 位 DIB 拼 ICO（不写 PNG 帧），374 KB；`build.ps1` 交给 csc 的 `/win32icon`，
  缺文件时会 `Write-Warning` 喊一声。**窗口那一份另外算**：`src\AppIcon.cs` 里的
  `LoadImage(hInstance, "#32512", IMAGE_ICON, cx, cy)` 按窗口要的尺寸取（标题栏 ICON_SMALL 16/20/24、
  任务栏 ICON_BIG 32），全部 9 个窗口都从 `Mxx1Form` 派生（在 `AppIcon.cs` 里），句柄建好 + Shown 各装一次。
- ✅ **外部工具目录 `bin-tools\` 已实现**（用户定的名字）：查找顺序里加一档、`build.ps1 -Package` 自动拷入、
  设置 / 关于窗口有「打开工具目录」、`kind: exe` 的相对路径按「工具箱目录 → `bin-tools\`」解析。
- ✅ **工具文件夹自动长按钮（v1.5.3，`src\ToolFolders.cs`）**：见下面「外部工具目录 `bin-tools\`」那节。
  ⬜ 还没做的只剩「把 `bin-tools\` 里的 exe 内嵌进 exe 当兜底」（`docs\DESIGN.md` §13 方案 ②）。
- ⬜ P1：剩下的都做完了，只有「Win10 / Win11 资源管理器」两个继续灰着（含义待用户定）、按钮排序 / 隐藏 / 固定到常用、多步 `macro`。

## 结构

```
D:\萌新工具开发\toolbox\
  build.ps1                        一键编译（系统自带 csc.exe，不需要 .NET SDK）
  bin\Mxx1Toolbox.exe              交付物：单文件 GUI+CLI（不入仓）
  src\Program.cs                   CLI 入口（list / run [--dry|--confirm] / draft / status / tip / privacy /
                                   sysreg / rightmenu / ui / pin / export / import / checkupdate / help）
  src\MainForm.cs                  主窗口：页签 + 四列网格 + 底栏 + 搜索 + 日志面板 + 键盘
  src\ToolButton.cs                紧凑按钮（Flat + 主题配色 + 16×15 图标画布 + 灰色占位 + Flash/SetBusy）
  src\IconFactory.cs               图标：有 PNG 用 PNG，没有就实时画；一律 Normalize 成 16×15；GetMuted 出灰版
  src\ToolItem.cs / ToolRegistry.cs 按钮模型 + 读内嵌 tools\*.json + 用户层 tools.json（只认 tools 数组）
  src\Launcher.cs                  按 kind 启动；SystemTargets 表（26 个系统工具）；找隔壁 exe / bin-tools；
                                   `LaunchElevatedCopy`（自己提权再起一遍 = 静默、不弹黑窗口）；UTF-8 输出
  src\RegEngine.cs                 "记原值 → 写入 → 读回核对 → 一键还原"的唯一实现（Privacy 与 SysReg 共用）
  src\SysReg.cs                    6 组系统设置开关（12 个按钮）+ 查看/还原改动 + selftest（TSV 记录）
  src\RightMenu.cs                 「右键增强」后端：在 HKCU\Software\Classes 下装 / 卸 verb 与级联子菜单、
                                   写前记原值（rightmenu-installed.tsv）、按位置给占位符（%1 / %V）、
                                   启动时顺手修补自己装过的键、读 Context Menu Manager Plus 的态度
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
                                   不抢焦点、跟着鼠标、几秒自消、点一下就关；`--quiet` / `--notify=0` /
                                   `MXX1_NO_NOTIFY=1` 关掉它（测试一律关，只有 N21 故意开着量位置）
  src\UnlockForm.cs                「解除文件占用」的结果窗口（`rightmenu unlock` 起来的独立进程，不开主界面；
                                   **高度按内容自适应**，见"界面硬规则"里那条）
  src\AppIcon.cs                   窗口图标（按 DPI 取 exe 资源里的那一档）+ `Mxx1Form` 基类（9 个窗口都从它派生）
  src\ConfirmForm.cs               自家的确认窗口「请确认」（不再用 MessageBox 甩命令，见 §"运行反馈"）
  src\UserTools.cs                 用户层 tools.json 的读写（最小 JSON writer，写入前备份 .bak）
  src\ToolFolders.cs               **v1.5.3**：扫 bin-tools\ 的工具文件夹，自动长出按钮（只读，
                                   撞 id 就让位；`ToolItem.AutoLayer` 标记它不可编辑）
  src\Consent.cs / ConsentForm.cs   **v1.5.3**：首次运行条款确认门（记条款正文指纹，不是记 true）
  src\DisclaimerForm.cs            **v1.5.3**：窗口显示 docs\DISCLAIMER.md（编译时内嵌，唯一正本）
  src\UpdateCheck.cs               **v1.5.3**：只读更新检查（GitHub releases → tags；不下载不替换）
  src\NewToolForm.cs               图形化「新建按钮 / 编辑按钮」窗口（4 种类型）
  src\LinksForm.cs                 「常用链接」窗口（项目主页/仓库/几个 ms-settings 入口）
  src\Json.cs                      自带的小 JSON 解析器（不依赖 Newtonsoft / System.Web）
  src\Theme.cs / Native.cs         浅深主题配色（含灰色占位三色）+ DWM 深色标题栏 / 滚动条
  src\Settings.cs / Logger.cs / AppPaths.cs
  src\AboutForm.cs                 署名、站点、仓库、许可证常量的唯一来源
  src\LogForm.cs / OutputForm.cs   程序内日志窗口（最新在最上）/ 命令输出窗口
  src\SettingsForm.cs              设置窗口
  tools\*.json                     112 个按钮的内置定义（编译时内嵌，资源名 tools.<文件名>）；7 个文件 =
                                 common / system / privacy / apps / cleanup / rightmenu / mine
  tools\Test-Encoding.ps1          编码红线体检（-Fix 修 BOM）
  tools\Make-Screenshots.ps1       拍 docs\gui-shot.png / dark-shot.png / system-shot.png（PrintWindow）
  tools\Make-Icons.ps1             批量画图标（先从 exe 的 list 读清单，所以**先 build 再跑它**）
  tools\Make-AppIcon.ps1           画 assets\app.ico（程序自己的图标，八尺寸；改完要重新 build）
  tools\Make-Package.ps1           打发布包 zip（build.ps1 -Package 调它；**回读 zip 逐个核对**，
                                   少一个文件就失败 —— 见"打包"那条坑）
  assets\icons\*.png               112 个按钮图标（编译时内嵌成 icons.<id>.png）
  assets\app.ico                   程序图标（`/win32icon` 用的就是它）
  tests\Test-All.ps1               一条命令跑完全部
  tools\Test-InlineSyntax.ps1      内联脚本语法 + 清单 JSON + **每个 .ps1 的语法**体检（34 个内联 / 7 个清单）
  tests\Test-Cli.ps1               命令行回归 206 项（A03b–A03d/D01 盯兼容、A14–A17 盯 exe 图标、
                                   L 组 12 项盯 sysreg、J09–J11 盯卸载窗口的列表、M 组盯右键增强、
                                   R 组 14 项盯 bin-tools 自动按钮、P 组 10 项盯发布包内容、
                                   S 组 26 项盯条款门 + 更新检查；
                                   环境不满足的项走 Skip()，打印 [SKIP] 不算失败）
  tests\Test-Gui.ps1               界面回归 150 项（要交互式桌面，无桌面返回 3 = 跳过；A04b 盯窗口图标、
                                   H 组盯固定尺寸、E04b 盯窗口位置、A09 盯 bin-tools 说明、
                                   N 组 27 项盯解除占用小窗口（含 N14/N15/N17/N18 的高度自适应、
                                   N19/N19b「一键解除占用（--quiet）一个窗口都不弹」、N20/N20b「扫描期间窗口一直活着」、
                                   N21/N21b/N21c/N21d「提示卡就在鼠标旁边、跟着鼠标动、到点自己消失、卡片上真有字」）、
                                   I 组 27 项把条款确认窗口真开起来点一遍，含 I10b/I10c）
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
& bin\Mxx1Toolbox.exe privacy status          # 只读列隐私开关状态；selftest 自检「原值→写入→还原」
& bin\Mxx1Toolbox.exe sysreg  status          # 只读列 6 组系统设置开关的现状；items / selftest 同上
& bin\Mxx1Toolbox.exe rightmenu status        # 只读列右键菜单里装了什么 / 子菜单几项（**写入口只在界面里点**）
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
| `MinimizeBox=false` + `MaximizeBox=false` | 标题栏多一个**灰掉的**最大化方框，点了没反应 |
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

## 「右键增强」怎么点（10 个按钮 = 9 个右键菜单按钮 + 隔壁工具）

**用户 2026-10-04 定的方案**（设计正本 `docs\DESIGN.md` §14）：工具箱自己往 Windows 右键菜单里装三样
东西 —— **只写 `HKCU\Software\Classes`**，不要管理员、不装 shell 扩展 DLL、不起服务、不加开机启动
（微软文档写明在这个根下注册子动词不需要提升权限；实测也是真的不用）。四个位置：任意文件 /
文件夹 / 文件夹里的空白处（= 当前文件夹）/ 桌面空白处。

| 装什么 | 键（都在 HKCU\Software\Classes 下） | 点了做什么 |
| --- | --- | --- |
| 解除文件占用 | `*\shell\Mxx1Unlock` 等 4 个 verb | `"<exe>" rightmenu unlock "%1"` → 开一个小窗口列出谁占着它 |
| **一键解除占用**（v1.5.4） | `*\shell\Mxx1AutoUnlock` 等 4 个 verb | `"<exe>" rightmenu unlock --auto "%1"` → **不弹窗口**，直接结束占着它的程序，鼠标旁边一张提示卡说结果 |
| 常用功能（级联子菜单） | `*\shell\Mxx1Common` + 共用子项键 `Mxx1Toolbox.Common` | 子项 = 「常用」页的镜像，命令是 `"<exe>" run <id>` |

**第三项「一键解除占用」**（`src\AutoUnlock.cs`，用户 2026-10-05 点名要的"不弹窗版本"）：

- 用户原话：**「保留现有的功能的前提下加个不弹窗的一键解除，对应也要单独加 2 个按钮一个添加右键
  一个撤销右键」** → **窗口版一个字节都没改**，这是另外一条入口；装 / 撤用**单独那对按钮**
  （`rightmenu.auto.on` / `rightmenu.auto.off`），和「装上 / 撤掉解除占用」互不影响。
- 查占用 / 结束进程**复用同一份实现**（`FileLock.Scan` / `FileLock.Kill`），区别只有"要不要问"。
- **底线更窄，而且只有一处**（`FileLock.AutoUnlockTarget`，测试 M34 盯着）：只结束
  **真占着文件的**（`lock`）和**它自己在运行的**（`run`）；系统关键进程 / `pid ≤ 4` / `svchost` /
  `lsass`…、`explorer.exe`、工具箱自己 —— **一律不动**；"窗口里开着它"（`open`）不动。
- **没同意过使用条款 = 一个进程都不碰**（这条路没有窗口可以弹确认框，规矩是"不同意就不动手"，
  只写一行日志）。回归 **M35** 就是盯这条。
- 结果用**鼠标旁边一张提示卡**说一句（`src\Balloon.cs` 的 `NoticeForm`）—— 不弹窗口，但也不能一声不吭
  （用户 2026-10-04 报过「点击确认以后也没有成功或者失败的反馈」）。
  ⚠️ 原来这一格是**系统托盘气泡**，用户 2026-10-06 报「没有正常弹出 + 位置要跟随鼠标」→ 换成自己画的
  卡片（不抢焦点 / 跟着鼠标 / 几秒自消 / 点一下也关），见坑 28 与 `docs\DESIGN.md` §12.56。
  卡片显示多久用 `--notify=<毫秒>` 定（默认 6 秒）；测试一律 `--quiet` / `--notify=0` /
  `MXX1_NO_NOTIFY=1`：**别在别人桌面上弹东西**（只有 N21 那四条故意开着它量位置）。
- 命令行输出是给测试读的：`auto=killed|partial|none|locked|skipped-consent|error` / `killed=` /
  `failed=` / `summary=`；退出码 0 = 正常跑完、2 = 没给路径、1 = 查询本身出错。
- **实测**（写测试之前端到端跑过）：锁一个文件 → `auto=killed killed=2`（连 conhost 子进程一起），
  再查 `lockers=0 candelete=yes`；系统事件日志那个永远被 4 个 `svchost` 占着的文件 →
  `auto=locked killed=0`，占用者一个没少。

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
  **命令行只有只读入口**（`rightmenu status|items|help`、`rightmenu unlock --query-only <路径>`），
  写注册表只在界面里点（和 `sysreg` 同一条规矩）。
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
- **C# 里别用 `File` / `Shell` / `Url` 这种方法名**：它们会盖住 `System.IO.File`，
  于是类里每个 `File.Exists` 都编译不过（CS0119）。用 `FileTarget` / `ShellTarget` / `UrlTarget`。

## 测试怎么用

- **一律 `powershell -ExecutionPolicy Bypass -File tests\Test-All.ps1`**（Windows PowerShell **5.1**，
  **不是 pwsh**）。两个坑都是 2026-10-04 用 pwsh 跑时踩的：① 套件里有 `Get-Content -Encoding Byte`
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
      **两道防线缺一不可**（2026-10-04 又踩了一次）：① **两个套件都要挂脚本级 `trap`**
      （Test-Gui 早就有，Test-Cli 原来是裸的 —— 它一样会暂停用户文件，脚本级错误一来收尾就没了）；
      ② **"把暂停文件放回去"这件事不许依赖 JSON 解析成功**：用户 `tools.json` 的 `_comment` 里
      有不合法转义（`\*`，JSON 里 `\` 后面只允许 `" \ / b f n r t u`）时 `ConvertFrom-Json` 直接抛错，
      三道闸整段被跳过 → 连搬回文件都做不成。现在解析前先跑 `Repair-JsonEscapes`（把这类转义补成
      合法的，只影响解析、不动用户文件），`_comment` 里写的也是合法的 `tools\\*.json`。
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
  17. **悬停检查（B10/B11）是真动系统鼠标**：`SetCursorPos` + 读 `ToolTip` 窗口文字。用户自己开着另一个
      工具箱实例时，两个窗口叠在一起、鼠标被上面那个接走 → 假红"没找到说明"（2026-10-04 真踩过）。
      所以测试实例写 `settings.ini` 前会先枚举屏幕上的工具箱窗口，挑一个不相交的角落放自己；悬停重试 3 次；
      失败信息里带 `[TBGui]::WindowAt(x,y)` 与 `Foreground()`，一眼能看出是不是环境问题。
  18. **按钮增减会连累一批"写死数量"的老断言**：v1.5.0 加 7 个按钮，Test-Cli 里 A04/A07/A09/A10/A11/H02
      六项全红（都是 105 / 右键 1 这种硬编码），Test-Gui 那边因为名字是 `list` 读出来的反而没事。
      v1.5.4 又加 2 个（一键解除那一对）→ 同样这六项 + M03/M05/M07/M08 一起红（112 → 114、右键 8 → 10）。
      **加按钮时按顺序搜：`112`（现在是 114）、`右键增强 8`、`'rightmenu' = 8`、`ok=8/8`、`菜单图标 8 个`**
      —— 页签分布那串数字（0/33/3/26/8/29/5/10）也在两处。
  19. **悬停说明（`hint`）别超过 110 字**：`ToolTip` 不换行，太长会顶出屏幕（H05 盯着）。
      长说明写进「右键增强说明」那种窗口里，`hint` 只留一句话。
  20. **`& bin\Mxx1Toolbox.exe …` 在命令行里读输出会读串**：它是 `/target:winexe`，PowerShell
      **不等待** GUI 子系统程序 —— `$LASTEXITCODE` 是空的、`$o = & $exe …` 是 `$null`、
      几次调用的输出还会挤在一起冒出来（2026-10-04 排查"RM 查不查得到占用"时被骗过一次：
      把上一次调用的输出当成了这一次的结论）。**一律 `Start-Process -Wait
      -RedirectStandardOutput`**（`Invoke-Exe` 就是这么做）。
  21. **GDI+ 的 `Image.FromStream` 要那个流活到图片用完**：`using (ms) { img = Image.FromStream(ms) }`
      之后再画会抛"参数无效"（惰性解码）。`.ico` 也别指望 PNG-in-ICO，自己拼 32 位 DIB
      （`BITMAPINFOHEADER` + 自下而上的 BGRA + 全 0 的 AND 掩码）。
  22. **编码体检会拦"绝对路径"**：测试里造"旧版菜单"的假命令时写了 `C:\old\Mxx1Toolbox.exe`，
      `Test-Encoding.ps1` 立刻报 `FAIL abs path`。用 `'"' + $Exe + '" …'` 拼，别写字面量。
  23. **打包（`Copy-Item` 拷目录必须带 `-Recurse`）**：2026-10-05 用户报「bin-tools 里面只有
      PermanentDeleteSetup.exe 进压缩包了，memreduct 没有进」—— 而翻 zip 时还发现 `assets\icons\`
      在包里是个**空目录**（112 张按钮图标一张没进；`Copy-Item (Join-Path $root 'assets\*') $dst`
      不带 `-Recurse` 时，目录**只建目录、不拷文件**，还不报错）。现在打包在
      **`tools\Make-Package.ps1`**：逐条列文件 + 拷整棵树 + **回读 zip 逐个核对**（少一个就失败）。
      **教训：批处理式的"打包 / 拷贝"一定要有一句"打完自己读回来核对"**，否则错误只能等用户翻包。
      回归：Test-Cli 的 **P 组**（P03 盯图标、P04/P05 盯工具目录、P08 盯失败路径、P09 盯"没碰真包"）。
  24. **别用旧快照覆盖"另有一条权威写入路径"的字段**：2026-10-05 用户报「使用条款确认 每次打开都弹」。
      主窗口的 `_settings` 是**构造函数**里 `Load()` 的，确认门在 `OnLoad` —— 快照比同意早，
      关窗口 `Save()` 就把刚写下的指纹盖成空。修法：`Settings.Save()` 里同意记录**只认磁盘那份**
      （`ConsentTouched` 由 `Consent.Accept/Reset` 置上），主窗口确认通过后顺手刷新快照。
      同类字段以后还会遇到（注册表原值、安装状态…）：**要么别放进这个对象，要么保存前合并**。
      回归：Test-Gui **I10b**（关窗口后记录还在）/ **I10c**（重开不再弹）——
      原来的 I09 是窗口**还开着**时查的，所以这个 bug 从测试里溜过去了。
  25. **"这一步要几秒"就不能放在界面线程上**：解锁窗口原来在 `Shown` 里同步调 `FileLock.Scan()`
      （实测右键一个 400 个文件的文件夹：窗口 184ms 出现、**612ms 起完全没响应，一直到 7093ms**，
      拖不动、关不掉、任务栏写"无响应"）。而那个扫描**最坏十几秒**（时间 90% 花在系统的
      `RmGetList` 上，工具自己的代码只占 0.8 秒），**慢不慢还取决于系统对那批路径的心情**
      （同样的文件换个文件夹 6.1 秒 → 0.23 秒），代码里根本预判不了 → 只能挪到后台线程。
      判据也别用"窗口还在不在"（窗口一直在，只是不回消息）：用
      `SendMessageTimeout(WM_NULL, SMTO_ABORTIFHUNG)` 量"最长一次没人应的时长"，回归 **N20**
      （修好后实测 **0ms**）。**这招拆小批次救不了**（400 个一批 5620ms vs 50 个一批查 8 次 5461ms）。
      连带两个小教训：**扫描期间那三个按钮要禁用**（查完恢复）—— 于是测试点按钮前必须等它 `Enabled`，
      否则点了禁用按钮会什么都不发生，然后假红成"没弹出确认框"（N09 真踩过）；
      状态行上的秒数由界面线程的 `Timer` 改，**它还在动就说明界面没被堵住**。
  26. **"挨着问前 N 个"不等于"定位到了"**：原来"是哪个文件被占着"是拿文件夹里**前 60 个**文件
      挨着问一遍 —— 400 个文件里被占的是第 200 个时，只会得到一句"查到了占用的程序，但没定位到
      是文件夹里哪个文件"（而这是 v1.5.1 专门做出来的东西）。改成**二分定位**：整批已经知道
      "有人占着"，就劈成两半各问一次，哪半有人占着继续劈 —— 靠的是 RM **全有或全无**的性质。
      400 个文件里 1 个被占：**9 层约 18 次查询**（原来 60 次，还找不到），**又快又准**。
      查询次数要留预算（`MaxAttributeQueries = 64`；System32 那种被占很多的文件夹会撑大二分树）。
  27. **`Make-Icons.ps1` 会给 `bin-tools\` 里的工具画图标并提交进仓库**：实测跑一次多出
      `assets\icons\mine.memreduct.png`（那是我这台机器 `bin-tools\memreduct\` 自动长出来的按钮，
      id 还是从文件夹名推的）。现在它连**自动按钮**一起跳过（读 `status` 的 `autoButton=` 行）——
      注意 `Mxx1Toolbox.exe` 是 winexe，**必须 `Start-Process -Wait -RedirectStandardOutput`** 才读得到
      输出（就是坑 20），`& $exe status` 拿到的是空数组，跳过的名单会静默变成空的。
  28. **"让系统替你弹提示"= 把"能不能看见"交给了用户的系统设置**：用户 2026-10-06 报
      **「气泡没有正常弹出，而且弹出的位置要跟随鼠标」** —— 原来那条路用的是
      `NotifyIcon.ShowBalloonTip`，它 ① 能不能看见由用户的**通知设置 / 专注助手**说了算
      （这台还是精简版 Windows，通知平台被裁过），② 位置由系统钉在**右下角**，离鼠标很远。
      现在改成**自己画的卡片**（`src\Balloon.cs` 的 `NoticeForm`）：位置在鼠标旁边（右下 18/22，
      贴边翻到另一侧并夹进那块屏幕的工作区）、鼠标动它跟着动、几秒自消、点一下就关，
      而且要 `ShowWithoutActivation` + `WS_EX_NOACTIVATE`（**不许抢焦点**：正在打字时字照样
      打进原来那个窗口）+ `WS_EX_TOOLWINDOW`（不进 Alt+Tab）。
      位置实测（`GetCursorPos` + `GetWindowRect` 对着量）：鼠标 700,300 → 卡片 718,322（dx=18,dy=22）；
      鼠标 1880,1020 → 卡片 1603,940（右下放不下，翻到左上）。
      连带两条：① **"有窗口"不等于"看得见"** —— 回归 **N21d** 抓 `PrintWindow` 的像素数墨迹才算验过；
      ② N21 要**真动鼠标**（`SetCursorPos`），所以先存原来的位置、跑完放回去（和 B10/B11 一个规矩），
      而且采样几次取最贴的一次（卡片跟鼠标有 ≤60ms 的滞后，用户也可能正在动鼠标）。
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
3. ⚠️ **v1.5.4 已经改好版本号，等用户点头才推 / 发**：版本号是 `1.5.4`
   （`src\AssemblyInfo.cs`，标题栏跟着变 —— 唯一来源），`CHANGELOG.md` 里是 `[1.5.4] - 2026-10-06`。
   **推送 / 打 tag / 发 Release 都要先问用户**（原话："推送的时候不要每次都推送，太卡了要问过我才行"，
   以及"发布工具"—— Release 的说明文字与截图要他点头）。要发就：
   `build.ps1 -Package` → 打 tag `v1.5.4` → 发 Release（附 `Mxx1Toolbox.exe` + zip + SHA256）。
4. 条款正文（`docs\DISCLAIMER.md`）**改一个字就会让所有人下次打开重新确认一次**（记的是正文指纹）。
   **v1.5.4 这一轮改过两次**（① 补第三项与「一键解除占用」的行为 / 底线 / 风险；
   ② 把"右下角气泡"改成"鼠标旁边的小提示卡"），所以用户下次打开会再看到一次《使用条款确认》
   —— **已经跟他说明过**；想免打扰：`bin\Mxx1Toolbox.exe consent --accept`。以后再改正文同样要先说一声。
5. P1 的范围（先接哪个页签的真功能）要问用户，别自己挑。
6. 图标要改样式就动 `tools\Make-Icons.ps1` 的关键词映射 / 配色，然后按
   `build.ps1 → Make-Icons.ps1 → 删孤儿 → build.ps1` 的顺序跑。
7. skill 三处同步：`D:\萌新工具开发\.dsh\skills\mxx1-toolbox\SKILL.md`、
   仓库内 `toolbox\skill\mxx1-toolbox\SKILL.md`、`%USERPROFILE%\.dsh\skills\mxx1-toolbox\SKILL.md`
   （**三份必须字节一致**，用 SHA256 核对）。
