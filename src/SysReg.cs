// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>一个「系统设置开关」：一对按钮（两个方向）背后的注册表改动，外加两样元数据 ——
    /// 两个方向各自叫什么（写进报告里的话），以及在哪些 Windows 版本上根本不成立。</summary>
    internal sealed class Tweak
    {
        public RegItemSpec Item;

        /// <summary>「开」这个方向在界面上叫什么（比如「任务栏从不合并」）。</summary>
        public string OnLabel = "";

        /// <summary>「关」这个方向叫什么（比如「任务栏始终合并」）。</summary>
        public string OffLabel = "";

        /// <summary>只有 build &gt;= MinBuild 才成立（0 = 不限）。用来挡 Win11 专属的开关。</summary>
        public int MinBuild = 0;

        /// <summary>只有 build &lt; MaxBuild 才成立（0 = 不限）。</summary>
        public int MaxBuild = 0;

        /// <summary>版本不成立时给出的整句说明（不是报错，是"这台机器上没有这回事"）。</summary>
        public string BuildNote = "";

        /// <summary>版本对不上但写了也安全时，先照写再补一句提醒。</summary>
        public string AfterNote = "";

        /// <summary>写完之后**自动重启一次资源管理器**（这个改动不重启就看不到）。
        /// 用户 2026-10-20 的要求：「需要重启资源管理器的功能，要自动调用」——
        /// 走 ExplorerRestart（命令的唯一来源是 common.json 里那颗「重启资源管理器」）。</summary>
        public bool RestartExplorer;

        public string Name { get { return Item.Name; } }
        public string Id { get { return Item.Id; } }
    }

    /// <summary>「常用设置」里那批直接改注册表的按钮的后端（任务栏合并方式 / 开始菜单对齐 /
    /// 驱动自动安装 / 内核隔离 / 资源管理器与右键菜单的 CLSID 覆盖）。
    ///
    /// 以前这些是 12 段内联 PowerShell，各自 Set-ItemProperty 完就算完 —— 没有原值记录、没有
    /// 还原入口，跟「隐私设置」那 29 个开关两套待遇。现在它们和隐私开关共用同一套引擎
    /// （`RegEngine`）：写之前先记原值、写完读回核对、随时「还原系统设置改动」。
    ///
    /// 版本判断（原来是脚本里读 CurrentBuildNumber）搬到这里：Win11 上「任务栏从不合并」
    /// 微软已经取消，写注册表也不生效；Win10 上「经典资源管理器」本来就是这个样子 ——
    /// 这两种情况都只给一句说明、一个字节都不写。</summary>
    internal static class SysReg
    {
        private const string Scratch = "SOFTWARE\\mxx1-toolbox\\sysreg-selftest";

        /// <summary>「桌面图标黑块」那一项的 id（按钮的 options 就是它）。</summary>
        public const string IconBlockId = "shell-icon-block";

        private const string ShellIconsKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons";

        /// <summary>「Shell Icons\29」那两处值（快捷方式左下角那个叠加图标）。`onValue` 是"隐藏小箭头"
        /// 时写进去的东西 —— 现在固定是工具箱自己生成的那张空白图标（BlankIcon.Path），
        /// **不再是** `shell32.dll,-50`：那个在部分 Win10 上会被画成黑方块（坑 101）。
        /// ⚠️ 测试隔离：`MXX1_SHELLICONS_ROOT` 一设，两处合并成 **HKCU 下面这一个键** ——
        /// 界面回归就能真点「修复 / 还原」走完整条路，而用户真实的 `Shell Icons` 一个字节都不碰，
        /// 也不需要管理员（见 tests\Test-Gui.ps1 里那两个环境变量）。</summary>
        private static RegValueSpec[] ShellIcon29Values(string onValue)
        {
            string env = (Environment.GetEnvironmentVariable("MXX1_SHELLICONS_ROOT") ?? "").Trim().TrimEnd('\\');
            if (env.Length > 0)
            {
                // 两处合成一处：值名还是 29，写入 / 读回 / 还原整条路一样走。
                return new RegValueSpec[]
                {
                    Str("HKCU", env, "29", onValue, null)
                };
            }
            return new RegValueSpec[]
            {
                Str("HKCU", ShellIconsKey, "29", onValue, null),
                Str("HKLM", ShellIconsKey, "29", onValue, null)
            };
        }

        private static RegValueSpec Dword(string hive, string key, string name, string on, string off)
        {
            RegValueSpec v = new RegValueSpec();
            v.Hive = hive; v.Key = key; v.Name = name; v.Dword = true;
            v.OnValue = on; v.OffValue = off;
            return v;
        }

        private static RegValueSpec Str(string hive, string key, string name, string on, string off)
        {
            RegValueSpec v = new RegValueSpec();
            v.Hive = hive; v.Key = key; v.Name = name; v.Dword = false;
            v.OnValue = on; v.OffValue = off;
            return v;
        }

        /// <summary>整个键的开关：开 = 建键（连同子值），关 = 删掉整棵树。</summary>
        private static RegValueSpec KeyTree(string hive, string key, string onValue, RegSubSpec[] subs)
        {
            RegValueSpec v = new RegValueSpec();
            v.Hive = hive; v.Key = key; v.Name = ""; v.Dword = false;
            v.OnValue = onValue; v.OffValue = null;
            v.OffDeletesKey = true; v.Subs = subs;
            return v;
        }

        private static readonly string ExplorerDll = "%SystemRoot%\\System32\\Windows.UI.FileExplorer.dll_";
        private const string ClsidItemsView = "{2aa9162e-c906-4dd9-ad0b-3d24a8eef5a0}";
        private const string ClsidXamlIsland = "{6480100b-5a83-4d1e-9f69-8ae5a88e9a33}";
        private const string ClsidOldContextMenu = "{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

        private static Tweak[] Build()
        {
            List<Tweak> list = new List<Tweak>();

            // ① 任务栏按钮合并方式：Win11 已经取消这个开关，所以只在 Win10 上成立。
            Tweak combine = new Tweak();
            combine.OnLabel = "从不合并";
            combine.OffLabel = "始终合并";
            combine.MaxBuild = 22000;
            combine.BuildNote = "这台是 Windows 11：微软已经取消「任务栏从不合并」，改注册表也不生效（只能靠第三方工具）。注册表没有动。";
            combine.Item = new RegItemSpec();
            combine.Item.Id = "taskbar-combine";
            combine.Item.Name = "任务栏按钮合并方式";
            combine.Item.What = "任务栏上一个窗口一个按钮（从不合并），还是同一个程序合成一个按钮（始终合并）";
            combine.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarGlomLevel", "2", "0")
            };
            list.Add(combine);

            // ② 开始菜单靠左 / 居中：Win11 才有的开关，Win10 上写了也不报错，只是看不到变化。
            Tweak align = new Tweak();
            align.OnLabel = "居左";
            align.OffLabel = "居中";
            align.AfterNote = "「开始菜单居左」是 Windows 11 才有的开关；这台机器上值已经写好，换到 Win11 就生效。";
            align.Item = new RegItemSpec();
            align.Item.Id = "startmenu-align";
            align.Item.Name = "开始菜单对齐方式";
            align.Item.What = "Win11 的开始菜单是靠左还是居中（TaskbarAl，0 = 靠左，1 = 居中）";
            align.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarAl", "0", "1")
            };
            list.Add(align);

            // ③ 驱动自动安装（HKLM，要管理员）
            Tweak driver = new Tweak();
            driver.OnLabel = "开启";
            driver.OffLabel = "关闭";
            driver.Item = new RegItemSpec();
            driver.Item.Id = "driver-install";
            driver.Item.Name = "驱动自动安装";
            driver.Item.What = "插上新设备时要不要让 Windows 更新自动去找驱动（SearchOrderConfig，0 = 不自动装，1 = 允许；和原来的脚本写的是同一对值）";
            driver.Item.Values = new RegValueSpec[]
            {
                Dword("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DriverSearching", "SearchOrderConfig", "1", "0")
            };
            list.Add(driver);

            // ④ 内核隔离 / 内存完整性（HKLM，要管理员）
            Tweak hvic = new Tweak();
            hvic.OnLabel = "开启";
            hvic.OffLabel = "关闭";
            hvic.Item = new RegItemSpec();
            hvic.Item.Id = "core-isolation";
            hvic.Item.Name = "内核隔离（内存完整性）";
            hvic.Item.What = "Windows 的「内存完整性」虚拟化保护；有些老驱动跟它冲突才需要关（关掉会降低针对内核攻击的防护）";
            hvic.Item.Values = new RegValueSpec[]
            {
                Dword("HKLM", "SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity", "Enabled", "1", "0")
            };
            list.Add(hvic);

            // ⑤ Win11 上把资源管理器换成 Win10 经典样式（三个 CLSID 覆盖键）
            Tweak classic = new Tweak();
            classic.OnLabel = "Win10 经典样式";
            classic.OffLabel = "Win11 原生样式";
            classic.MinBuild = 22000;
            classic.BuildNote = "这台是 Windows 10：资源管理器本来就是经典界面（顶部完整菜单栏 + 大功能按钮区），没有需要切换的东西。这两个按钮只在 Win11 上有区别，注册表一个字节都没动。";
            classic.Item = new RegItemSpec();
            classic.Item.Id = "explorer-classic";
            classic.Item.Name = "资源管理器样式";
            classic.Item.What = "Win11 的资源管理器换回 Win10 经典界面（顶部完整菜单栏 + 大功能按钮区），靠三个 CLSID 覆盖键实现；「Win11 原生样式」把这些覆盖删掉";
            classic.Item.Values = new RegValueSpec[]
            {
                KeyTree("HKCU", "Software\\Classes\\CLSID\\" + ClsidItemsView, "CLSID_ItemsViewAdapter",
                    new RegSubSpec[] { new RegSubSpec("InProcServer32", "", ExplorerDll), new RegSubSpec("InProcServer32", "ThreadingModel", "Apartment") }),
                KeyTree("HKCU", "Software\\Classes\\CLSID\\" + ClsidXamlIsland, "File Explorer Xaml Island View Adapter",
                    new RegSubSpec[] { new RegSubSpec("InProcServer32", "", ExplorerDll), new RegSubSpec("InProcServer32", "ThreadingModel", "Apartment") }),
                KeyTree("HKCU", "Software\\Classes\\CLSID\\" + ClsidOldContextMenu + "\\InprocServer32", "",
                    null)
            };
            list.Add(classic);

            // ⑥ 右键菜单：只换右键菜单，不动资源管理器本体
            Tweak ctx = new Tweak();
            ctx.OnLabel = "Win10 经典菜单";
            ctx.OffLabel = "Win11 新版菜单";
            ctx.AfterNote = "这台机器上本来就是这个经典右键菜单，看不到变化；换到 Win11 就生效。";
            ctx.Item = new RegItemSpec();
            ctx.Item.Id = "ctxmenu-classic";
            ctx.Item.Name = "右键菜单样式";
            ctx.Item.What = "Win11 的折叠式右键菜单换回 Win10 的完整菜单（一个 CLSID 覆盖键），「Win11 新版菜单」把它删掉";
            ctx.Item.Values = new RegValueSpec[]
            {
                KeyTree("HKCU", "Software\\Classes\\CLSID\\" + ClsidOldContextMenu + "\\InprocServer32", "", null)
            };
            list.Add(ctx);

            // ⑦ 显示文件扩展名（B 组：都是 HKCU、不用管理员，写之前照旧记原值）
            Tweak ext = new Tweak();
            ext.OnLabel = "显示扩展名";
            ext.OffLabel = "隐藏扩展名";
            ext.Item = new RegItemSpec();
            ext.Item.Id = "file-ext";
            ext.Item.Name = "文件扩展名显示";
            ext.Item.What = "资源管理器里文件名后面要不要显示 .txt / .exe 这类扩展名（HideFileExt，0 = 显示，1 = 隐藏）";
            ext.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "HideFileExt", "0", "1")
            };
            list.Add(ext);

            // ⑧ 显示隐藏文件（Hidden：1 = 显示，2 = 不显示；系统文件另由 ShowSuperHidden 控制）
            Tweak hidden = new Tweak();
            hidden.OnLabel = "显示隐藏文件";
            hidden.OffLabel = "不显示隐藏文件";
            hidden.Item = new RegItemSpec();
            hidden.Item.Id = "hidden-files";
            hidden.Item.Name = "隐藏文件显示";
            hidden.Item.What = "要不要把隐藏属性的文件也列出来（Hidden，1 = 显示，2 = 不显示）；带「系统」属性的文件仍由文件夹选项里那个受保护的勾单独控制";
            hidden.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "Hidden", "1", "2")
            };
            list.Add(hidden);

            // ⑨ 快捷方式小箭头：Shell Icons\29 覆盖成**工具箱自己生成的一张空白图标**
            //    （BlankIcon：1bpp + AND 掩码全 0xFF，靠掩码说"一个像素都不画"）。
            //    关 = 把这个值删掉（OffValue = null → 引擎删值，不是写空字符串）。
            //
            //    ⚠️ 2026-10-20 实测修正：**只写 HKCU 在这台 Win10 上完全没用** ——
            //    拿一个真 .lnk 调 SHGetFileInfo(SHGFI_LINKOVERLAY)，HKCU 有值 / 没值，
            //    叠加图标都是同样的 60 个像素；改成写 HKLM 的那一份，60 → **0**，删掉又回到 60。
            //    所以两个位置一起写：认 HKCU 的系统照旧，认 HKLM 的（这台）也真的会变。
            //    代价就是这一对从此要管理员（HKLM 那一条），「恢复」把两处都删掉。
            //
            //    ⚠️ 2026-10-26 修正（用户报「点了去掉小箭头会变成图标黑块」）：原来这里写的是
            //    `%SystemRoot%\System32\shell32.dll,-50` —— Windows 自带的那个"全透明空图标"。
            //    它的像素确实全是透明的，但 Shell 画叠加图标时会把它画成一块黑（坑 101）。
            //    现在写的是 BlankIcon.Path 那张自己生成的图（路径是绝对路径、没有 ,索引）。
            //    写之前 Set() 会先确保那张图真在硬盘上，生成不出来就一个字节都不写。
            //
            //    ⚠️ 还是要重启资源管理器才看得到变化。
            Tweak arrow = new Tweak();
            arrow.OnLabel = "去掉小箭头";
            arrow.OffLabel = "恢复小箭头";
            arrow.RestartExplorer = true;   // 不重启资源管理器看不到变化 → 写完之后自动重启一次
            arrow.AfterNote = "这个值要重启资源管理器（或注销一次）才看得到变化 —— 重启这一步工具箱替你做了（见下一行）。";
            arrow.Item = new RegItemSpec();
            arrow.Item.Id = "shortcut-arrow";
            arrow.Item.Name = "快捷方式小箭头";
            arrow.Item.What = "快捷方式图标左下角那个小箭头（把 Shell Icons\\29 写成工具箱自己生成的那张全透明空白图标 "
                + "blank-arrow.ico；「恢复小箭头」把这个值删掉。HKCU 和 HKLM 两处一起写，改完要重启资源管理器）";
            arrow.Item.Values = ShellIcon29Values(BlankIcon.Path);
            list.Add(arrow);

            // ⑩ 任务栏时钟显示秒：Windows 10 起才有这个值，Win7/8 上不成立（只给说明、不写注册表）
            Tweak clock = new Tweak();
            clock.OnLabel = "时钟显示秒";
            clock.OffLabel = "时钟不显示秒";
            clock.MinBuild = 10240;
            clock.BuildNote = "「任务栏时钟显示秒」是 Windows 10 起才有的开关；这台是更早的系统（Win7/8），写进去也不会有效果，注册表没有动。";
            clock.RestartExplorer = true;   // 任务栏时钟要重启资源管理器才刷新
            clock.AfterNote = "任务栏时钟要重启资源管理器才刷新 —— 重启这一步工具箱替你做了（见下一行）。";
            clock.Item = new RegItemSpec();
            clock.Item.Id = "clock-seconds";
            clock.Item.Name = "任务栏时钟秒";
            clock.Item.What = "任务栏右下角时钟里要不要显示秒数（ShowSecondsInSystemClock，1 = 显示，0 = 不显示）";
            clock.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "ShowSecondsInSystemClock", "1", "0")
            };
            list.Add(clock);

            // ⑪ 任务栏小组件按钮（Win11 起才有这个按钮；这台不是 Win11 也照写，换系统后生效）
            //    走 AfterNote 而不是 BuildNote：这个值在 Win10 上写了也没有副作用，
            //    写进去之后换到 Win11 就直接是用户想要的样子。
            Tweak widgets = new Tweak();
            widgets.OnLabel = "关掉小组件按钮";
            widgets.OffLabel = "显示小组件按钮";
            widgets.AfterNote = "「任务栏小组件」是 Windows 11 才有的按钮（Win10 的任务栏上没有它）：值已经写好，换到 Win11 登录一次就看得出来；任务栏要重启资源管理器才刷新，这一步工具箱替你做了（见下一行）。";
            widgets.RestartExplorer = true;   // 任务栏上的按钮是 explorer 画的，不重启看不到变化
            widgets.Item = new RegItemSpec();
            widgets.Item.Id = "taskbar-widgets";
            widgets.Item.Name = "任务栏小组件按钮";
            widgets.Item.What = "Win11 任务栏左边那个天气 / 小组件按钮要不要显示（TaskbarDa，0 = 关掉，1 = 显示）";
            widgets.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarDa", "0", "1")
            };
            list.Add(widgets);

            // ⑫ Copilot 按钮（Win11 22H2 / build 22621 起；同样是照写 + 一句说明）
            Tweak copilot = new Tweak();
            copilot.OnLabel = "隐藏 Copilot 按钮";
            copilot.OffLabel = "显示 Copilot 按钮";
            copilot.AfterNote = "「Copilot 按钮」是 Windows 11 22H2 起才有的东西：这台机器上没有这个按钮，值已经写好，换到 Win11 就生效；任务栏要重启才刷新，这一步工具箱替你做了（见下一行）。";
            copilot.RestartExplorer = true;   // 同上：任务栏上的按钮要重启才刷新
            copilot.Item = new RegItemSpec();
            copilot.Item.Id = "copilot-button";
            copilot.Item.Name = "Copilot 按钮";
            copilot.Item.What = "Win11 任务栏上的 Copilot 图标要不要显示（ShowCopilotButton，0 = 隐藏，1 = 显示）";
            copilot.Item.Values = new RegValueSpec[]
            {
                Dword("HKCU", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "ShowCopilotButton", "0", "1")
            };
            list.Add(copilot);

            // ⑬ 菜单弹出延迟：MenuShowDelay 是**字符串**值（REG_SZ），系统默认 400 毫秒。
            //    「更快」写 20（几乎一按就出），「默认」写 400；原来的值照旧先记进 tsv，随时能还原。
            Tweak menu = new Tweak();
            menu.OnLabel = "菜单弹出更快";
            menu.OffLabel = "恢复默认延迟";
            menu.AfterNote = "新的等待时间下一次弹菜单就生效（个别老程序要重新打开一次）。";
            menu.Item = new RegItemSpec();
            menu.Item.Id = "menu-delay";
            menu.Item.Name = "菜单弹出延迟";
            menu.Item.What = "右键菜单 / 开始菜单弹出前等多久（MenuShowDelay，单位毫秒；「更快」写 20，「默认」写 400）";
            menu.Item.Values = new RegValueSpec[]
            {
                Str("HKCU", "Control Panel\\Desktop", "MenuShowDelay", "20", "400")
            };
            list.Add(menu);

            // ⑭ 快速启动：HKLM，自动算成"要管理员"（下面的循环会把它标上）。
            //    ⚠️ 它要靠休眠文件：休眠关了（powercfg /h off）这个值写了也不生效 —— 说明里点明。
            Tweak fast = new Tweak();
            fast.OnLabel = "开启快速启动";
            fast.OffLabel = "关闭快速启动";
            fast.AfterNote = "快速启动要靠休眠文件：如果休眠被关掉（powercfg /h off），这个值写了也不生效 —— 这一页的「禁用/启用休眠」那颗按钮会把两者一起切。";
            fast.Item = new RegItemSpec();
            fast.Item.Id = "fast-startup";
            fast.Item.Name = "快速启动";
            fast.Item.What = "关机时把系统状态存进休眠文件、下次开机直接读回来（HiberbootEnabled，1 = 开，0 = 关）；只影响「关机 → 开机」，重启永远是完整启动";
            fast.Item.Values = new RegValueSpec[]
            {
                Dword("HKLM", "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power", "HiberbootEnabled", "1", "0")
            };
            list.Add(fast);

            // ⑮ 未激活水印（用户 2026-10-20 点名要的：「右下角 win 未激活的时候会显示出来，做个可以开关隐藏的功能」）。
            //    右下角那行「激活 Windows / 转到设置以激活 Windows」是资源管理器画的；
            //    写 SoftwareProtectionPlatform\NoAcrWatermark = 1 之后它就不画了，「恢复」= 把这个值删掉（回到系统默认）。
            //    ⚠️ 这条**本机验不了**：这台是已激活的 Windows 10 Pro（LicenseStatus=1），水印本来就不出现
            //       —— 所以说明里必须写清"已经激活的系统上点了看不出区别"，别让用户以为按钮没生效。
            //    ⚠️ 微软没有为这个值出过官方文档（社区里通行的做法）；2026-10-20 本机又核了一遍：
            //       explorer.exe / shell32.dll / sppsvc.exe / sppc.dll 等 130 个激活相关模块里
            //       **都找不到 "NoAcrWatermark" 这个字符串** → 不保证在每个版本上都灵。
            //       所以「恢复」必须做成"删值"（不是写 0），并且说明里要如实写"不保证有效、删掉就回原样"。
            Tweak mark = new Tweak();
            mark.OnLabel = "隐藏未激活水印";
            mark.OffLabel = "恢复未激活水印";
            mark.RestartExplorer = true;   // 水印是 explorer 画的，不重启看不到变化
            mark.AfterNote = "只隐藏右下角那行提示，不会激活 Windows（未激活时的功能限制照旧）。已经激活的系统上本来就没有这行字，点了看不出任何区别。⚠️ 这个值名在系统的激活组件里查不到引用（本机核过 explorer / shell32 / sppsvc / sppc 等 130 个模块），所以不保证在每个 Windows 版本上都灵；不灵就点「恢复未激活水印」把这个值删掉，立刻回到原样。";
            mark.Item = new RegItemSpec();
            mark.Item.Id = "activation-watermark";
            mark.Item.Name = "未激活水印";
            mark.Item.What = "右下角那行「激活 Windows / 转到设置以激活 Windows」要不要显示（NoAcrWatermark，1 = 不显示；「恢复」= 删掉这个值，回到系统默认）";
            mark.Item.Values = new RegValueSpec[]
            {
                Dword("HKLM", "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SoftwareProtectionPlatform", "NoAcrWatermark", "1", null)
            };
            list.Add(mark);

            // ⑯ 桌面图标黑块（2026-10-24 用户报的：「桌面快捷方式变成黑方块」+ 一张截图）。
            //    真因（本机量出来的）：Shell Icons\29 被写成了 `shell32.dll,-50` —— 这正是上面
            //    「去掉小箭头」写进去的那个「全透明空图标」；这台 Win10 上它没画成透明，反而
            //    渲染成一块黑。删掉 29（HKLM + HKCU）就回到系统默认的小箭头（2026-10-20 实测：
            //    这台机器真正生效的是 HKLM 那一份，所以两处都删）。
            //
            //    这一对**故意不复用 shortcut-arrow**：两个 id 各记各的原值 ——
            //    「还原修复前图标」要退回的是**点修复之前**的样子（可能正是那个 -50，
            //    也可能是美化工具写的别的值，甚至干脆没有），不是「去掉小箭头」之前的出厂值。
            //    所以：修复 = off 方向（删值，先记原值）；还原 = RestoreFor(这一项)。
            //    ⚠️ 只碰编号 29；77 / 179 这类别的编号只在报告里念一句，一个字节都不动。
            //    ⚠️ 这一项的 on 方向由 RestoreIconBlock 走"按记录还原"，不会走到 Engine.Apply ——
            //       OnValue 只是照 ShellIcon29Values 的要求占个位（填的是同一张空白图标），
            //       万一哪天真被 Apply 了，写进去的也是一张透明图，不至于把桌面弄花。
            Tweak blk = new Tweak();
            blk.OnLabel = "还原修复前图标";
            blk.OffLabel = "修复桌面图标黑块";
            blk.RestartExplorer = true;   // 桌面图标是 explorer 画的，不重启看不到变化
            blk.Item = new RegItemSpec();
            blk.Item.Id = IconBlockId;
            blk.Item.Name = "桌面图标黑块";
            blk.Item.What = "桌面 / 快捷方式图标左下角那块黑方块（Shell Icons\\29 被写成了一个在这台机器上会画成黑块的图标；"
                + "「修复桌面图标黑块」= 把 HKCU 与 HKLM 两处的 29 都删掉、回到系统默认的小箭头；"
                + "「还原修复前图标」= 按修复前记下的原值写回去）";
            blk.Item.Values = ShellIcon29Values(BlankIcon.Path);
            list.Add(blk);

            foreach (Tweak t in list)
            {
                foreach (RegValueSpec v in t.Item.Values)
                {
                    if (string.Equals(v.Hive, "HKLM", StringComparison.OrdinalIgnoreCase)) { t.Item.Admin = true; }
                }
            }
            return list.ToArray();
        }

        private static readonly Tweak[] Table = Build();
        private static readonly RegEngine Engine = new RegEngine(Items(), AppPaths.SysRegBackupFile, "系统设置");

        private static RegItemSpec[] Items()
        {
            List<RegItemSpec> items = new List<RegItemSpec>();
            foreach (Tweak t in Table) { items.Add(t.Item); }
            return items.ToArray();
        }

        public static Tweak[] All { get { return Table; } }
        public static string BackupPath { get { return AppPaths.SysRegBackupFile; } }

        public static Tweak FindTweak(string id)
        {
            foreach (Tweak t in Table)
            {
                if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { return t; }
            }
            return null;
        }

        /// <summary>写 HKLM 的值要管理员（按钮带 runAsAdmin，Launcher 会把自己提权重起）。</summary>
        public static bool NeedsAdmin(string id)
        {
            Tweak t = FindTweak(id);
            return (t != null) && t.Item.Admin;
        }

        /// <summary>两个方向各自在界面上叫什么，报告里用这句人话。</summary>
        public static string Wording(string id, bool on)
        {
            Tweak t = FindTweak(id);
            if (t == null) { return id; }
            return on ? t.OnLabel : t.OffLabel;
        }

        /// <summary>悬停说明里附的那一行（和隐私开关一样，写成一句人话，不甩 id 出去）。
        /// 「桌面图标黑块」那一对单开两句：它的 on 方向是"按记录还原"、off 方向是"删值"，
        /// 套通用的「设为『…』：桌面图标黑块」会读成"这个按钮是去修黑块的"
        /// —— 用户 2026-10-26 报的就是这句（点「还原修复前图标」看到的提示在讲「修复」）。</summary>
        public static string Describe(string id, bool on)
        {
            Tweak t = FindTweak(id);
            if (t == null) { return "系统设置里没有这个开关：" + id; }
            if (string.Equals(id, IconBlockId, StringComparison.OrdinalIgnoreCase))
            {
                return on
                    ? "按「修复桌面图标黑块」当时记下的原值，把 Shell Icons\\29 写回去（只还原这一项，别的开关的记录不动）"
                    : "把 Shell Icons\\29 那个自定义图标值删掉，回到系统默认的小箭头（写之前先记原值，随时能还原）";
            }
            return "设为「" + Wording(id, on) + "」：" + t.Item.Name + "（写注册表，可一键还原）";
        }

        /// <summary>当前系统的 build 号；读不到就返回 0（当"不知道"，不做版本拦截）。</summary>
        public static int CurrentBuild()
        {
            try
            {
                object o = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null);
                if (o == null) { return 0; }
                int n;
                if (int.TryParse(o.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return n; }
            }
            catch { }
            return 0;
        }

        private static string BuildMismatch(Tweak t)
        {
            int build = CurrentBuild();
            if (build <= 0) { return ""; }   // 读不到版本就别乱拦
            if (t.MinBuild > 0 && build < t.MinBuild) { return t.BuildNote; }
            if (t.MaxBuild > 0 && build >= t.MaxBuild) { return t.BuildNote; }
            return "";
        }

        // ---------------------------------------------------------------- 对外动作

        /// <summary>把一个系统设置开关设成 on（见 Tweak.OnLabel）或 off。
        /// ⚠️ 「桌面图标黑块」这一项是特例：off = 修复（删值 + 记原值），
        /// on = 按记录还原（**不是**写死一个 OnValue —— 别人的机器上原来可能是别的值、也可能没有）。</summary>
        public static string Set(string id, bool on, out bool ok)
        {
            if (string.Equals(id, IconBlockId, StringComparison.OrdinalIgnoreCase))
            {
                return on ? RestoreIconBlock(out ok) : FixIconBlock(out ok);
            }
            Tweak t = FindTweak(id);
            if (t == null) { ok = false; return "不认识这个系统设置开关：" + id; }
            string mismatch = BuildMismatch(t);
            if (mismatch.Length > 0)
            {
                ok = true;   // 不是失败：这台机器上本来就没有这回事
                return "【" + t.Item.Name + "】" + Wording(id, on) + Environment.NewLine
                    + Environment.NewLine + mismatch;
            }

            // 「去掉小箭头」要写的是工具箱自己生成的那张空白图标：先把文件弄到硬盘上。
            // 弄不出来就**一个字节都不写** —— 往注册表里塞一个指向不存在文件的路径，
            // 桌面只会更难看（用户报的下一件事就会是"图标变成白纸"）。
            if (on && UsesBlankIcon(t))
            {
                string iconPath;
                string iconError;
                if (!BlankIcon.Ensure(out iconPath, out iconError))
                {
                    ok = false;
                    StringBuilder fail = new StringBuilder();
                    fail.Append("【").Append(t.Item.Name).Append("】").Append(Wording(id, on)).AppendLine();
                    fail.AppendLine();
                    fail.Append("  这次一个字都没写：要先把那张「全透明空白图标」放到硬盘上，这一步没成功（")
                        .Append(iconError).AppendLine("）。");
                    fail.Append("  位置：").Append(iconPath).AppendLine();
                    fail.Append("  没有那张图就去写注册表，桌面图标只会变成一块黑方块 —— 所以这里宁愿不写。").AppendLine();
                    fail.Append("  你还可以：把那个文件夹建出来 / 给写入权限，然后重试；实在不行就别点这一颗。");
                    return fail.ToString();
                }
            }

            RegApplyReport report = Engine.Apply(t.Item, on);
            StringBuilder sb = new StringBuilder();
            sb.Append("【").Append(t.Item.Name).Append("】设为「").Append(Wording(id, on)).Append("」").AppendLine();
            sb.Append("  ").Append(t.Item.What).AppendLine();
            sb.AppendLine();
            foreach (RegOutcome o in report.Outcomes)
            {
                if (o.Ok)
                {
                    sb.Append("  √ ").Append(o.Label);
                    if (o.Now.Length > 0) { sb.Append(" = ").Append(o.Now); }
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("  × ").Append(o.Label).Append("  ").Append(o.Error.Length > 0 ? o.Error : "没成功").AppendLine();
                }
            }
            sb.AppendLine();
            if (report.BackupError.Length > 0)
            {
                sb.Append("  原值备份没写成：").Append(report.BackupError).AppendLine();
                sb.Append("  （「还原系统设置改动」会不完整，请手动记一下）").AppendLine();
            }
            if (report.Ok)
            {
                sb.Append("  ").Append(report.Written.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处都写进去并读回核对过了。").AppendLine();
                if (t.AfterNote.Length > 0) { sb.Append("  ").Append(t.AfterNote).AppendLine(); }
                if (t.RestartExplorer)
                {
                    // 不重启资源管理器就看不到变化的开关（小箭头 / 时钟秒）：当场自动重启一次。
                    // 用户 2026-10-20 的原话「需要重启资源管理器的功能，要自动调用」——
                    // 命令走 common.json 里那颗「重启资源管理器」（ExplorerRestart 是唯一入口）。
                    string restartMsg;
                    ExplorerRestart.Run(t.Item.Name, out restartMsg);
                    sb.Append("  ").Append(restartMsg).AppendLine();
                }
                sb.Append("  反悔就点「还原系统设置改动」（按改动前的原值写回去）。");
            }
            else
            {
                sb.Append("  有地方没写成功 —— 上面带 × 的就是（多半是缺管理员权限）。");
            }
            ok = report.Ok;
            return sb.ToString();
        }

        /// <summary>这一项「开」的方向要写的是不是 BlankIcon 那张空白图标（现在只有「去掉小箭头」）。
        /// 判断按值来、不写死 id：以后谁再用这张图，写之前自动也会先把它生成出来。</summary>
        private static bool UsesBlankIcon(Tweak t)
        {
            foreach (RegValueSpec v in t.Item.Values)
            {
                if (v.OnValue != null && BlankIcon.IsOurs(v.OnValue)) { return true; }
            }
            return false;
        }

        // ---------------------------------------------------------------- 桌面图标黑块

        private static string HiveLabel(string hive)
        {
            return string.Equals(hive, "HKLM", StringComparison.OrdinalIgnoreCase) ? "HKLM" : "HKCU";
        }

        /// <summary>「顺手看到」的那些编号：77 / 179 这类是第三方美化工具常写的位置。
        /// **只念出来，不碰**。测试隔离时读的是隔离根（不把用户真实的值念进测试报告）。</summary>
        private static string OtherShellIconValues()
        {
            StringBuilder sb = new StringBuilder();
            string env = (Environment.GetEnvironmentVariable("MXX1_SHELLICONS_ROOT") ?? "").Trim().TrimEnd('\\');
            string[] hives;
            string[] keys;
            if (env.Length > 0)
            {
                hives = new string[] { "HKCU" };
                keys = new string[] { env };
            }
            else
            {
                hives = new string[] { "HKLM", "HKCU" };
                keys = new string[] { ShellIconsKey, ShellIconsKey };
            }
            for (int i = 0; i < hives.Length; i++)
            {
                foreach (string name in new string[] { "77", "179" })
                {
                    RegValueSpec v = Str(hives[i], keys[i], name, "", null);
                    bool exists;
                    RegistryValueKind kind;
                    string text;
                    if (!Engine.TryReadRaw(v, out exists, out kind, out text) || !exists) { continue; }
                    sb.Append("    ").Append(HiveLabel(hives[i])).Append(' ').Append(name)
                      .Append(" = ").Append(text).AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>只读：把 `Shell Icons\29` 两处现在的样子念一遍 + 两种修法各自会做什么。
        /// 「修复图标黑块」那颗按钮点开的小窗口就是拿它当正文 —— 点开这个窗口**什么都不改**。</summary>
        public static string IconBlockReport()
        {
            Tweak t = FindTweak(IconBlockId);
            if (t == null) { return "找不到「桌面图标黑块」这一项：" + IconBlockId; }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("桌面图标左下角那块黑方块，来自「Shell Icons\\29」里那个自定义图标值。");
            sb.AppendLine();
            sb.AppendLine("  现在的样子：");
            bool any = false;
            string first = "";
            foreach (RegValueSpec v in t.Item.Values)
            {
                bool exists;
                RegistryValueKind kind;
                string text;
                Engine.TryReadRaw(v, out exists, out kind, out text);
                if (exists && first.Length == 0) { first = text; }
                any = any || exists;
                sb.Append("    ").Append(HiveLabel(v.Hive)).Append(" 29 = ")
                  .Append(exists ? text : "（没有这个值，系统默认的小箭头）").AppendLine();
            }
            sb.Append("    → ").AppendLine(IconBlockNote(any ? first : ""));
            sb.AppendLine();
            sb.AppendLine("  两种修法（两种都会先把原值记下来，随时能点「还原修复前图标」退回）：");
            sb.AppendLine("    ·「保持无箭头 · 只治黑块」：小箭头继续不显示，只把 29 换成工具箱自己生成的空白图标"
                + "（1 位色 + 掩码，一个像素都不画）。要管理员，写完自动重启一次资源管理器。");
            sb.AppendLine("    ·「恢复系统默认小箭头」：把 HKCU 与 HKLM 两处的 29 删掉，小箭头回到系统默认样子。"
                + "要管理员，写完自动重启一次资源管理器。");
            sb.AppendLine();
            string extras = OtherShellIconValues();
            if (extras.Length > 0)
            {
                sb.AppendLine("  顺手看到的（不是这个窗口写的，一个字节都不动）：");
                sb.Append(extras);
            }
            sb.AppendLine("  只动编号 29 这一个值（有的美化工具还会改 77 / 179，这里一概不碰）。");
            sb.Append("  要是两种修法都试过还有黑块，去「清理优化」页点「修复图标与缩略图缓存」。");
            return sb.ToString();
        }

        /// <summary>`29` 里那个值到底是什么东西 —— 一句人话（报告区那行 → 后面用它）。</summary>
        private static string IconBlockNote(string text)
        {
            if (text.Length == 0) { return "系统默认的小箭头（没有自定义图标值，本来不该有黑块）"; }
            if (BlankIcon.IsOurs(text))
            {
                return "工具箱自己生成的全透明空白图标 —— 小箭头是隐藏的；要是还有黑块，多半是图标缓存（见最后一行）";
            }
            if (text.IndexOf("shell32.dll,-50", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Windows 自带的那个「全透明空图标」—— 这台机器上 Shell 把它画成了黑方块；点上面任何一条都能修掉";
            }
            return "一个自定义图标值 —— 这块黑方块就是它画出来的；点上面任何一条都能修掉";
        }

        /// <summary>「修复桌面图标黑块」：用户 2026-10-26 要求「不能只是换回箭头，要兼容一下，
        /// 要 2 种方法：一个去图标后的修复方法、一个修改回箭头的方法」。
        ///   · keepHidden = true  → 小箭头**继续不显示**，只把 `29` 换成工具箱自己生成的那张空白图标
        ///     （以前那个 `shell32.dll,-50` 在这台机器上会被画成黑块，换掉它黑块就没了）；
        ///   · keepHidden = false → 把 `29` 删掉，小箭头回到系统默认的样子。
        /// 两种都先记原值、写完读回核对、自动重启一次资源管理器；
        /// 「还原修复前图标」按这一次记下的原值退回（两种修法共用同一份记录）。
        /// ⚠️ 只碰编号 29；77 / 179 这类别的编号只在报告里念一句，一个字节都不动。</summary>
        public static string FixIconBlock(bool keepHidden, out bool ok)
        {
            Tweak t = FindTweak(IconBlockId);
            if (t == null) { ok = false; return "找不到「桌面图标黑块」这一项：" + IconBlockId; }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(keepHidden
                ? "【修复桌面图标黑块】保持小箭头隐藏，只把那个会画成黑块的图标换成工具箱自己生成的空白图标"
                : "【修复桌面图标黑块】删掉 Shell Icons\\29 里那个自定义图标值，恢复系统默认的小箭头");
            sb.AppendLine();
            sb.AppendLine("  写之前的样子：");
            foreach (RegValueSpec v in t.Item.Values)
            {
                bool exists;
                RegistryValueKind kind;
                string text;
                Engine.TryReadRaw(v, out exists, out kind, out text);
                sb.Append("    ").Append(HiveLabel(v.Hive)).Append(" 29 = ")
                  .Append(exists ? text : "（本来就没有这个值，系统默认小箭头）").AppendLine();
                if (exists && BlankIcon.IsOurs(text))
                {
                    sb.AppendLine(keepHidden
                        ? "      ↑ 这是「去掉小箭头」写进去的那张全透明空白图标 —— 小箭头会继续不显示，只把这张图换成工具箱这次的空白图。"
                        : "      ↑ 这是「去掉小箭头」写进去的那张全透明空白图标 —— 点这个修复会把这个设置一起撤掉，小箭头会回来。");
                }
            }
            sb.AppendLine();

            // 「保持无箭头」这条要先确保那张空白图真在硬盘上：弄不出来就**一个字节都不写**
            // （往注册表里塞一个指向不存在文件的路径，桌面只会更难看），同「去掉小箭头」那条。
            if (keepHidden)
            {
                string iconPath;
                string iconError;
                if (!BlankIcon.Ensure(out iconPath, out iconError))
                {
                    ok = false;
                    sb.AppendLine("  这次一个字都没写：要先把工具箱那张「全透明空白图标」放到硬盘上，这一步没成功");
                    sb.Append("  （").Append(iconError).AppendLine("）。");
                    sb.Append("  位置：").AppendLine(iconPath);
                    sb.AppendLine("  没有那张图就往注册表里写，桌面图标只会更难看 —— 所以这里宁愿不写。");
                    sb.AppendLine("  你还可以：点「恢复系统默认小箭头」那条路（它不生成任何文件）；或者把上面那个文件夹建出来、再点一次。");
                    return sb.ToString();
                }
            }

            RegApplyReport report = Engine.Apply(t.Item, keepHidden);
            sb.AppendLine("  改完读回核对：");
            for (int i = 0; i < report.Outcomes.Count; i++)
            {
                RegOutcome o = report.Outcomes[i];
                string hive = (i < t.Item.Values.Length) ? HiveLabel(t.Item.Values[i].Hive) : "?";
                if (o.Ok)
                {
                    sb.Append("    √ ").Append(hive).Append(" 29 —— ");
                    if (keepHidden) { sb.Append("现在是 ").Append(o.Now); }
                    else
                    {
                        sb.Append(o.Now.IndexOf("没有这个值") >= 0
                            ? "已经没有这个值了（回到系统默认的小箭头）"
                            : ("现在是 " + o.Now));
                    }
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("    × ").Append(hive).Append(" 29 —— ")
                      .Append(o.Error.Length > 0 ? o.Error : "没成功").AppendLine();
                }
            }
            sb.AppendLine();

            if (report.BackupError.Length > 0)
            {
                sb.Append("  原值没记下来（").Append(report.BackupError).AppendLine("）—— 「还原修复前图标」会不完整，请手动记一下。");
            }
            if (report.Ok)
            {
                sb.Append("  ").Append(report.Written.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处都写进去了，读回核对过。").AppendLine();
                sb.AppendLine(keepHidden
                    ? "  小箭头照旧不显示，只是换了一张「靠掩码透明」的空白图 —— 黑块应该就没了。"
                    : "  小箭头回到系统默认样子，黑块也没了。");
                string extras = OtherShellIconValues();
                if (extras.Length > 0)
                {
                    sb.AppendLine("  顺手看到的（不是这个按钮写的，一个字节都没动）：");
                    sb.Append(extras);
                }
                sb.AppendLine("  只动编号 29 这一个值（有的美化工具还会改 77 / 179，这里一概不碰）。");
                sb.AppendLine("  要是重启之后还有黑块，去「清理优化」页点「修复图标与缩略图缓存」（它删的是 Windows 会自己重建的缓存文件）。");
                if (t.RestartExplorer)
                {
                    string restartMsg;
                    ExplorerRestart.Run(t.Item.Name, out restartMsg);
                    sb.Append("  ").Append(restartMsg).AppendLine();
                }
                sb.Append("  反悔就点「还原修复前图标」（按刚才记下的原值写回去）。");
            }
            else
            {
                sb.Append("  有地方没写成功 —— 上面带 × 的就是（多半是缺管理员权限）。");
            }
            ok = report.Ok;
            return sb.ToString();
        }

        /// <summary>老的那个入口（=「恢复系统默认小箭头」）：命令行 / 旧调用点还用得到。</summary>
        public static string FixIconBlock(out bool ok)
        {
            return FixIconBlock(false, out ok);
        }

        /// <summary>「还原修复前图标」：只把这一项记下的原值写回去（别的开关的记录不动）。
        /// 原值可能是那个 -50（黑块会回来）、也可能是美化工具写的别的值，还可能"原来没有" ——
        /// 所以只能照记录写，不能写死一个值。
        /// ⚠️ 2026-10-26：报头原来写的是「【桌面图标黑块】还原修复前的图标设置」+ 空记录时一句
        /// 「还没点过『修复桌面图标黑块』」—— 用户点了「还原修复前图标」看到这两句，以为程序
        /// 搞错了按钮（报的问题原话是"点击还原修复前图标提示修复桌面图标黑块"）。现在报头
        /// 直接用**按钮名**，空记录时先把"这次一个字没写"说清楚，再告诉他想干那件事该点哪颗。</summary>
        public static string RestoreIconBlock(out bool ok)
        {
            RegRestoreReport report = Engine.RestoreFor(IconBlockId);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("【还原修复前图标】按「修复桌面图标黑块」当时记下的原值，把 Shell Icons\\29 写回去");
            sb.AppendLine();
            if (report.Outcomes.Count == 0)
            {
                ok = true;
                sb.AppendLine("  这次没有写任何东西：翻原值记录没找到「桌面图标黑块」这一项的记录");
                sb.AppendLine("  （说明「修复桌面图标黑块」还没点过，或者上次还原完已经把记录清掉了）。");
                sb.AppendLine();
                sb.AppendLine("  如果你想要的是别的：");
                sb.AppendLine("    · 小箭头现在是隐藏的、想让它回来 → 点「恢复小箭头」");
                sb.AppendLine("    · 桌面图标上有黑块 → 点「修复桌面图标黑块」");
                sb.AppendLine("    · 只想看看现在是什么状态 → 点「查看设置改动」");
                sb.AppendLine();
                sb.Append("  原值记录在 ").Append(BackupPath).AppendLine();
                return sb.ToString();
            }
            foreach (RegOutcome o in report.Outcomes)
            {
                if (o.Ok)
                {
                    sb.Append("  √ ").Append(o.Label);
                    if (o.Note.Length > 0) { sb.Append("  ").Append(o.Note); }
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("  × ").Append(o.Label).Append("  还原失败：").Append(o.Error).AppendLine();
                }
            }
            sb.AppendLine();
            if (report.Failed == 0)
            {
                sb.Append("  ").Append(report.Done.ToString(CultureInfo.InvariantCulture)).Append(" 处都写回去了");
                if (report.Cleared) { sb.Append("；这一项的原值记录已经清空（别的开关的记录没动）"); }
                sb.AppendLine();
                sb.Append("  桌面图标要重启资源管理器才重画 —— 这一步工具箱替你做了（见下一行）。").AppendLine();
                string restartMsg;
                ExplorerRestart.Run("还原修复前图标", out restartMsg);
                sb.Append("  ").Append(restartMsg);
            }
            else
            {
                sb.Append("  有 ").Append(report.Failed.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处没还原成功（多半是缺管理员权限），原值记录先留着。");
            }
            ok = (report.Failed == 0);
            return sb.ToString();
        }

        /// <summary>「桌面图标黑块」在「查看设置改动」里念的那句话。
        /// 不能沿用通用的「开启状态」—— 用户读到「开启状态」会以为黑块是某个自己打开的功能，
        /// 实际含义只是"29 里有个自定义图标值"。</summary>
        private static string IconBlockStateText(RegItemSpec item)
        {
            int have = 0;
            string first = "";
            foreach (RegValueSpec v in item.Values)
            {
                bool exists;
                RegistryValueKind kind;
                string text;
                if (Engine.TryReadRaw(v, out exists, out kind, out text) && exists)
                {
                    have++;
                    if (first.Length == 0) { first = text; }
                }
            }
            if (have == 0) { return "系统默认的小箭头（没有自定义的 29 值）"; }
            if (BlankIcon.IsOurs(first))
            {
                return "小箭头是隐藏状态（29 = " + first + "，这是工具箱自己生成的全透明空白图标）";
            }
            if (first.IndexOf("shell32.dll,-50", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "有自定义图标值（" + first + "，「去掉小箭头」以前写进去的那张全透明空图标；"
                    + "在这台机器上它会被画成黑方块 → 点「修复桌面图标黑块」）";
            }
            return "有自定义图标值（" + first + "，这块黑方块就是它画出来的）";
        }

        /// <summary>只读：每个开关现在是什么状态 + 记了哪些原值。</summary>
        public static string Status()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("系统设置当前状态（只读，没改任何东西）");
            sb.AppendLine();
            foreach (Tweak t in Table)
            {
                string state = (t.Item.Id == IconBlockId) ? IconBlockStateText(t.Item) : Engine.StateText(t.Item);
                sb.Append("  ").Append(RegEngine.PadCjk(t.Item.Name, 24)).Append(state);
                if (t.Item.Admin) { sb.Append("　[要管理员]"); }
                sb.AppendLine();
                List<RegRecord> mine = Engine.RecordsFor(t.Id);
                if (mine.Count > 0)
                {
                    sb.Append("      改动过 ").Append(mine.Count.ToString(CultureInfo.InvariantCulture)).Append(" 处，原值：");
                    int shown = 0;
                    foreach (RegRecord r in mine)
                    {
                        if (shown > 0) { sb.Append("，"); }
                        sb.Append(r.Label).Append("=").Append(r.Existed ? r.Value : "（原来没有）");
                        shown++;
                        if (shown >= 3 && mine.Count > 3) { sb.Append(" 等 ").Append(mine.Count.ToString(CultureInfo.InvariantCulture)).Append(" 处"); break; }
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
            int n = Engine.RecordCount();
            if (n > 0)
            {
                sb.Append("  改动前的原值记了 ").Append(n.ToString(CultureInfo.InvariantCulture)).Append(" 条：").AppendLine();
                sb.Append("  ").Append(BackupPath).AppendLine();
                sb.Append("  （点「还原系统设置改动」可以把它们写回去）");
            }
            else
            {
                sb.Append("  还没动过任何系统设置（所以没有原值可还原）");
            }
            return sb.ToString();
        }

        /// <summary>把记录下来的原值全部写回去，然后清空记录。</summary>
        public static string Restore(out bool ok)
        {
            RegRestoreReport report = Engine.Restore();
            if (report.Outcomes.Count == 0)
            {
                ok = true;
                return "没有要还原的东西：还没动过这一页的系统设置。" + Environment.NewLine
                    + "（原值记录应该在 " + BackupPath + "）";
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("还原系统设置改动：按改动前的原值写回去");
            sb.AppendLine();
            foreach (RegOutcome o in report.Outcomes)
            {
                if (o.Ok)
                {
                    sb.Append("  √ ").Append(o.Label);
                    if (o.Note.Length > 0) { sb.Append("  ").Append(o.Note); }
                    sb.AppendLine();
                }
                else
                {
                    sb.Append("  × ").Append(o.Label).Append("  还原失败：").Append(o.Error).AppendLine();
                }
            }
            sb.AppendLine();
            if (report.Failed == 0)
            {
                sb.Append("  ").Append(report.Done.ToString(CultureInfo.InvariantCulture)).Append(" 处都还原了");
                if (report.Cleared) { sb.Append("；原值记录已经清空（下次再改会重新记当时的原值）"); }
                sb.AppendLine();
                sb.Append("  任务栏 / 开始菜单这类改动要重启资源管理器或重新登录才看得到（本程序「常用设置」里就有「重启资源管理器」）。");
            }
            else
            {
                sb.Append("  有 ").Append(report.Failed.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处没还原成功（多半是缺管理员权限），原值记录先留着。");
            }
            ok = (report.Failed == 0);
            return sb.ToString();
        }

        /// <summary>用工具箱自己名下的测试键把「记原值 → 写入 → 读回核对 → 还原」整条链路走一遍，
        /// 三种值都覆盖：DWORD、字符串、整棵键（CLSID 那种覆盖）。用户自己的原值记录会先挪走再放回。
        /// 全程只碰 HKCU\Software\mxx1-toolbox\sysreg-selftest。</summary>
        public static string SelfTest(out bool ok)
        {
            ok = false;
            StringBuilder sb = new StringBuilder();
            string savedBackup = null;
            bool hadBackup = File.Exists(BackupPath);
            if (hadBackup)
            {
                try { savedBackup = File.ReadAllText(BackupPath, Encoding.UTF8); File.Delete(BackupPath); }
                catch { savedBackup = null; }
            }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Scratch))
                {
                    if (k == null) { sb.AppendLine("建不出测试用的注册表键"); return sb.ToString(); }
                    k.SetValue("probe-dword", 7, RegistryValueKind.DWord);
                    k.SetValue("probe-string", "orig", RegistryValueKind.String);
                }
                sb.AppendLine("测试键 " + Scratch + "：probe-dword=7、probe-string=orig，准备走一遍「设成开」");
                sb.AppendLine("（整棵键那条路径用一个不存在的子键试，见下面 keytree）");
                sb.AppendLine();

                RegItemSpec fake = new RegItemSpec();
                fake.Id = "selftest";
                fake.Name = "自检项";
                fake.What = "只写工具箱自己的测试键";
                fake.Values = new RegValueSpec[]
                {
                    Dword("HKCU", Scratch, "probe-dword", "1", "0"),
                    Str("HKCU", Scratch, "probe-string", "on-text", "off-text"),
                    KeyTree("HKCU", Scratch + "\\keytree", "self-test",
                        new RegSubSpec[] { new RegSubSpec("InProcServer32", "", "C:\\selftest.dll"), new RegSubSpec("InProcServer32", "ThreadingModel", "Apartment") })
                };

                RegApplyReport applied = Engine.Apply(fake, true);
                int dwordNow;
                bool readDword = FakeRead(fake.Values[0], out dwordNow);
                string textNow;
                bool readText = FakeReadText(fake.Values[1], out textNow);
                bool keyNow = Engine.KeyExists("HKCU", Scratch + "\\keytree");
                if (!applied.Ok || !readDword || dwordNow != 1 || !readText || textNow != "on-text" || !keyNow)
                {
                    sb.AppendLine("写入或读回核对失败：dword=" + (readDword ? dwordNow.ToString(CultureInfo.InvariantCulture) : "读不到")
                        + "、string=" + (readText ? textNow : "读不到") + "、keytree=" + (keyNow ? "在" : "不在"));
                    return sb.ToString();
                }
                sb.AppendLine("写入 + 读回核对通过（probe-dword=1、probe-string=on-text、keytree 建出来了）");

                RegRestoreReport back = Engine.Restore();
                readDword = FakeRead(fake.Values[0], out dwordNow);
                readText = FakeReadText(fake.Values[1], out textNow);
                keyNow = Engine.KeyExists("HKCU", Scratch + "\\keytree");
                if (back.Failed != 0 || !readDword || dwordNow != 7 || !readText || textNow != "orig" || keyNow)
                {
                    sb.AppendLine("还原失败：dword=" + (readDword ? dwordNow.ToString(CultureInfo.InvariantCulture) : "读不到")
                        + "、string=" + (readText ? textNow : "读不到") + "、keytree=" + (keyNow ? "还在" : "已删"));
                    return sb.ToString();
                }
                sb.AppendLine("还原通过（dword 回到 7、string 回到 orig、整棵键删掉了）");

                // 再走一遍「原来没有这个值」的分支：删掉 DWORD 值 → 设成开 → 还原 → 应该又变回"没有"
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Scratch))
                {
                    if (k != null) { k.DeleteValue("probe-dword", false); }
                }
                Engine.Apply(fake, true);
                Engine.Restore();
                readDword = FakeRead(fake.Values[0], out dwordNow);
                if (readDword) { sb.AppendLine("「原来没有这个值」的分支不对：还原后值还在"); return sb.ToString(); }
                sb.AppendLine("「原来没有这个值」的分支也对（还原后那个值被删掉了）");

                ok = true;
                return sb.ToString();
            }
            catch (Exception ex)
            {
                sb.AppendLine("自检出错：" + ex.Message);
                return sb.ToString();
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(Scratch, false); } catch { }
                try
                {
                    if (hadBackup && savedBackup != null) { File.WriteAllText(BackupPath, savedBackup, new UTF8Encoding(false)); }
                    else if (File.Exists(BackupPath)) { File.Delete(BackupPath); }
                }
                catch { }
            }
        }

        // 自检里读回值用的小工具（引擎的读法是公开的，这里只是省得建一堆变量）
        private static bool FakeRead(RegValueSpec v, out int value)
        {
            return Engine.TryReadInt(v, out value);
        }

        private static bool FakeReadText(RegValueSpec v, out string text)
        {
            text = "";
            bool exists;
            RegistryValueKind kind;
            if (!Engine.TryReadRaw(v, out exists, out kind, out text)) { return false; }
            return exists;
        }
    }
}
