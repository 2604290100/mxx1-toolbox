# 萌新工具箱 · 踩坑清单（32 条）

> 这是 skill 的**按需**文件：改测试 / 改界面探针 / 改用户可见文案之前读一次。
> 每一条都是真踩过的，写着"根因 + 判据 + 哪个回归盯着"。序号在 `SKILL.md` 与提交信息里会被引用
> （比如"坑 29"= 进程归属那条），所以**别重排、别删号**。
> 最近更新：2026-10-06。

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
      ⚠️ v1.5.4 又踩一次：改 `rightmenu.auto.on` 的 hint 补了"提示卡 / 看归属"那两句，
      变成 **127 字**，本地没跑命令行回归、**CI 上 H05 当场红**。
      **改完 hint 就量一下**（`tip <id>` 每行都 ≤110 字，`tip` 是 winexe，要
      `Start-Process -Wait -RedirectStandardOutput`）。
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
  29. **"进程名黑名单"拦不住系统进程 —— 看"归属"才拦得住**（2026-10-06 **CI 抓到的真事故**，
      不是自己发现的）：v1.5.4 推上去之后 GitHub runner 上 **M34 红了**，而且是真出事了 ——
      `[FAIL] M34 … (auto=killed killed=2 占用者 3→1)`：**「一键解除占用」在别的机器上真的结束了
      系统服务**（那台机器上我们正好是管理员，"结束失败"这层保险也没兜住）。
      根因：底线原来是一张**进程名黑名单**（`CriticalNames`：svchost / lsass / wininit…），
      名字来自 `Process.GetProcessById(pid).ProcessName` —— **权限不够时读不出来**，
      代码退回 `RmGetProcessInfo` 报的**友好名**（"Windows Event Log" 这种服务名），
      黑名单一条都对不上，于是它就动手了。
      **教训：名字是别人给的、随时可能读不到；归属是系统给的、读不到就拒绝。**
      修法（`FileLock.AutoUnlockTarget` 现在是「归属 + 来源 + 名字」三层，任何一层不确定就拒绝）：
      ① `NotMine` —— 进程令牌里的用户 SID 和当前进程不一样（SYSTEM / 别的账户 / TrustedInstaller）；
      ② `IsService` —— `RM_APP_INFO.ApplicationType == RmService`；
      ③ `NameUnread` —— `ExeNameOf(pid)` 空（手里只有友好名，名单无效）。
      **读不到就当成"不是自己的"**（fail closed）。窗口那条路一个字没改。
      本机实测：系统事件日志 `killed=0 targets=`（一个都没挑中）；自己开的 powershell 独占的文件
      `killed=2 targets=powershell.exe`（照结束）。回归 **M34**（失败信息带 `targets=`）
      + **M39**（源码级看门狗：盯住那三条拒绝还在 —— 真造"系统服务占着文件"的现场要管理员 +
      计划任务，造不出来只能 Skip，那等于没测）。见 `docs\DESIGN.md` §12.57。
      连带一个调试小习惯：**"挑中了谁"要能看见**（`targets=`）—— 出事时第一句要问的就是它。
  30. **给 `LinkLabel` 发 PostMessage 不会触发 `LinkClicked`，只有真鼠标才会**：用户 2026-10-06 要
      「关于里面的网址点一下能打开」，回归 D07d 一开始写的是发 `WM_MOUSEMOVE` +
      `WM_LBUTTONDOWN` + `WM_LBUTTONUP`（窗口已经 `SetForegroundWindow`、坐标就是控件正中间）——
      **一条日志都不写**；换成 `SetCursorPos` + `mouse_event`（真鼠标）**立刻**就写了
      「打开链接 https://mxx1.cn」。所以"能不能点"这种事只能真点（`TBGui.RealClick`）。
      连带两条：① 真点之前先用 `WindowFromPoint` 确认鼠标底下**就是那个控件**
      （`TBGui.HandleAt`，别用 `WindowAt` —— 那个会往上找顶层窗口），不是就跳过，别误点用户别的窗口；
      ② 真点之前把鼠标位置存下来、跑完放回去（和 B10/B11 一个规矩）；
      ③ 链接类控件跨进程看**类名没用**（LinkLabel 底下还是 `STATIC`），只能靠"点一下有没有反应"验。
  31. **`$ErrorActionPreference='Stop'` 下，原生命令写到 stderr 的每一行 = 终止性错误**（2026-10-06
      在提交前闸门里踩到，而且是**静默**的）：`tools\Test-Quick.ps1` 开头设了 `Stop`，里面对 git 的
      调用写了 `2>$null` —— 以为 stderr 就被丢掉了，其实 **PowerShell 5.1 先把 stderr 的每一行变成
      ErrorRecord**，`Stop` 之下直接抛：命令中断、`$LASTEXITCODE` 变成 `-1`。而 git 只要工作区里有
      CRLF 差异就会写 `warning: … CRLF will be replaced by LF …` —— 于是**改动集永远读成空**，
      闸门"静默退回全套跑"（表面"宁多勿少"很安全，实际把 `tests\test-map.json` 整个架空，报出来的
      原因还是误导的："自动读不出改动"，听着像工作区是干净的）。修法：调用原生命令那一段**临时**把
      EAP 放成 `Continue`（函数作用域就够，不用改全局），再显式看 `$LASTEXITCODE`。自检在
      `tools\Test-InlineSyntax.ps1` 末尾：造一个假 git（先往 stderr 写一行 warning、再往 stdout 报
      一个文件名），读不出那个文件名就 FAIL；**灵敏度也验过**（摘掉防线立刻 `COUNT=0`）。
      同一类的第二个坑：`[void](函数 …)` 会把**函数里那个子进程的 stdout 一起吞掉** ——
      闸门曾经跑两分钟屏幕上一片安静、失败时只剩一句"闸门没过"，没有任何细节（改成 `| Write-Host`）。
  32. **PS 5.1 里 SwitchParameter 不能直接转整数**：`[int]$NoBuild` 抛
      `Cannot convert the False value of type SwitchParameter to type Int32`（它能隐式当布尔用，
      但不能转 int）—— 要写 `$NoBuild.IsPresent`。2026-10-06 在闸门的指纹函数里踩到。
      **连带一个排查教训**：那次报错行被我自己的输出过滤器（`| Select-String -Pattern '要跑的|用时'`）
      吃掉了，屏幕上看只剩"跑了 1 秒、什么都没输出"，看着像程序悄悄退出 ——
      **排查时先把原始输出落盘（`Out-File`）再过滤**，别拿过滤后的结果当全部信息。
  33. **界面自动化：UIA 在这台机器上把 WinForms 按钮报成 `ControlType.Pane`，而且报的矩形是错的**
      （2026-10-06 写「文件哈希校验」的界面验证时踩到）：`AutomationElement.Current.BoundingRectangle`
      给的是 `1488,536`，而那个按钮真实位置在它**下面 180px** —— 照它点会点到分段标题上，
      表现出来是"点了没反应"。另外 `InvokePattern` 对报成 Pane 的元素直接 `Unsupported Pattern`。
      **稳妥做法回到 `tests\Test-Gui.ps1` 那一套**：`EnumChildWindows` + `WM_GETTEXT` 读文字 +
      `GetWindowRect` 拿真实屏幕坐标 + `PostMessage(BM_CLICK)` 点按钮 + 真鼠标（`SetCursorPos` +
      `mouse_event`）点右键。两个连带事实：① **`BM_CLICK` 只发 `BN_CLICKED`、不动焦点** ——
      想验快捷键（F1 这种走 `ProcessCmdKey` 的），必须先用**真鼠标**点一下那个按钮，
      否则 `ActiveControl` 根本不是它，按 F1 什么都不会发生；② **读别的进程里 Edit 的文字要用
      `WM_GETTEXT`（`SendMessageTimeout` + `SMTO_ABORTIFHUNG`）**，`GetWindowText` 对"没有标题的控件"
      基本返回空（`[TBGui]::Text` 本来就这么写的）。
