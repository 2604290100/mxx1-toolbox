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
    /// <summary>One line of the「常用功能」cascading submenu.</summary>
    internal sealed class RightMenuEntry
    {
        public string Id = "";
        public string Name = "";
        public string Command = "";
        public string IconId = "";          // 用哪个按钮的内嵌图标生成 .ico（空 = 这一项不写图标）
        /// <summary>按钮自己指定的图标文件（bin-tools 里的工具按钮就有：它的 PNG 在工具文件夹里，
        /// 不在 exe 的内嵌资源里）。非空且真在磁盘上时优先用它，否则退回按 id 找内嵌 PNG。</summary>
        public string IconPath = "";
        public bool SeparatorBefore = false;
    }

    /// <summary>Where a verb is installed. Key is relative to the root (HKCU\Software\Classes).</summary>
    internal sealed class RightMenuLocation
    {
        public string Id = "";
        public string Label = "";
        public string Key = "";
        /// <summary>命令里代表"右键的那个路径"的占位符。**"文件夹里的空白处"和"桌面空白处"必须用
        /// %V** —— 那两个位置资源管理器不会替换 %1，会把字面量 "%1" 原样传进来（用户 2026-10-04
        /// 报「没扫描到占用文件」的一部分原因就是这个）。</summary>
        public string Placeholder = "%1";

        public RightMenuLocation(string id, string label, string key, string placeholder)
        {
            Id = id; Label = label; Key = key; Placeholder = placeholder;
        }
    }

    /// <summary>装之前记下来的现场：这个键当时在不在。</summary>
    internal sealed class RightMenuRecord
    {
        public string Item = "";     // unlock | common
        public string Key = "";      // HKCU\Software\Classes\*\shell\Mxx1Unlock
        public bool Existed = false;
        public string When = "";
    }

    /// <summary>「右键增强」的后端：把两件事装进 Windows 右键菜单。
    ///
    /// 只写 **HKCU\Software\Classes**（微软文档里写明这样注册子动词不需要提升权限，
    /// 见 docs\DESIGN.md §14.1）：不装 shell 扩展 DLL、不起服务、不加开机启动，删掉键就干净。
    ///
    /// 三条底线：
    /// ① **只碰自己那几个 Mxx1* 键** —— 同名键如果内容不是工具箱写的（旧版本或别人手写），
    ///    不覆盖、不删除，只在报告里说明；
    /// ② **装之前先记现场**（rightmenu-installed.tsv）：「撤掉」按记录删，记录丢了也有按名字兜底的路径；
    /// ③ **写完读回核对**（和隐私 / 常用设置同一条规矩，读了不等于写进去了）。
    ///
    /// 测试要能把根挪走：环境变量 MXX1_RIGHTMENU_ROOT。回归测试一律用它，绝不把测试项真装到
    /// 用户的右键菜单上。</summary>
    internal static class RightMenu
    {
        // ---- 菜单项的 verb（注册表键名）------------------------------------------------------
        //
        // ⚠️ **键名决定菜单里的先后顺序**：资源管理器把同一个右键位置里的静态菜单项**按 verb 名
        // （= `...\shell\` 下面那个键名）的字母序**排，不看我们想让它排第几。这是 2026-10-06 晚七
        // 用 shell 自己的 `IContextMenu` 把真实菜单**按顺序打出来**量到的（只读探针
        // `tools\Show-MenuOrder.ps1`，四个位置 4/4 一致，见 DESIGN §12.64）：那时候名字是
        // Mxx1AutoUnlock / Mxx1CopyName / Mxx1CopyPath / Mxx1Terminal / Mxx1Unlock，排出来的
        // 顺序就是「一键解除占用 / 复制文件名 / 复制文件路径 / 在此处打开终端 / 解除文件占用」，
        // **「解除文件占用」因为 U 排最后掉到了最底下** —— 用户看了效果图要改顺序。
        //
        // 所以每个名字前面挂一个**序号**：`Mxx1Toolbox.<n>.<名字>`，字母序 = 我们要的顺序。
        // **改顺序 = 改这个数字**（老用户那边由「启动修补」自动换名，见 MigrateLegacyNames）。
        // 顺序（用户 2026-10-06 晚七拍板，两个子菜单收在最后）：
        //   1 复制文件名 · 2 复制文件路径 · 3 解除文件占用 · 4 一键解除占用 · 5 常用功能 · 6 在此处打开终端
        public const string VerbPrefix = "Mxx1Toolbox.";
        /// <summary>「复制文件名」—— 平铺的一级菜单项（`copypath --name "%1"`）。</summary>
        public const string CopyNameVerb = "Mxx1Toolbox.1.CopyName";
        /// <summary>「复制文件路径」—— 平铺的一级菜单项（`copypath "%1"`）。
        /// 晚六那版叫 `Mxx1CopyPath`（不带序号），老用户那边由迁移换名（见 LegacyCopyPathVerb）。</summary>
        public const string CopyPathVerb = "Mxx1Toolbox.2.CopyPath";
        public const string UnlockVerb = "Mxx1Toolbox.3.Unlock";
        public const string AutoVerb = "Mxx1Toolbox.4.AutoUnlock";
        public const string CommonVerb = "Mxx1Toolbox.5.Common";
        /// <summary>「在此处打开终端」的**父项** verb —— 它是**级联子菜单**（用户 2026-10-06 晚六：
        /// 「我是让你设定为可以多选而不是新增一个菜单，是要你做成子菜单」；问他「复制改成平铺了，
        /// 终端要不要也照做」时，他选「**保持子菜单一条**」）：菜单里只出现一项，
        /// 鼠标移上去弹出「cmd / PowerShell」。</summary>
        public const string TerminalVerb = "Mxx1Toolbox.6.Terminal";

        /// <summary>**这一版之前**那六个 verb 名（2026-10-06 晚六及更早，不带序号）。
        /// 老用户那边这些键由 MigrateLegacyNames 就地换名 —— **先写新的、新键写成读回核对过了
        /// 才删旧的**（顺序反了会让人短暂地没有这一项，甚至永久丢掉）。</summary>
        public const string LegacyCopyNameVerb = "Mxx1CopyName";
        public const string LegacyCopyPathVerb = "Mxx1CopyPath";
        public const string LegacyUnlockVerb = "Mxx1Unlock";
        public const string LegacyAutoVerb = "Mxx1AutoUnlock";
        public const string LegacyCommonVerb = "Mxx1Common";
        public const string LegacyTerminalVerb = "Mxx1Terminal";
        /// <summary>**晚六第一稿**（同一天，没发布）装过四个独立菜单项，用的是这四个 verb。
        /// 万一有人点过，装上 / 撤掉 / 启动修补时顺手清掉（见 CleanSuperseded）——
        /// 不然他会同时看到四个独立项和新做的子菜单。</summary>
        public const string SupersededCopyRelVerb = "Mxx1CopyPathRel";
        public const string SupersededCopyAbsVerb = "Mxx1CopyPathAbs";
        public const string SupersededTerminalCmdVerb = "Mxx1TerminalCmd";
        public const string SupersededTerminalPsVerb = "Mxx1TerminalPs";
        /// <summary>共用子项键的前缀（都在 HKCU\Software\Classes 下）：级联子菜单的内容写在这里，
        /// 父项只写一个 `ExtendedSubCommandsKey` 指过来。
        /// ⚠️ 晚七起这个前缀**同时也是 verb 名的前缀**（`Mxx1Toolbox.<n>.<名字>`，见上面那段），
        /// 所以 `IsOurs()` 拿它当"这是工具箱的键"的判据比原来更宽 —— 两边的键都在同一个根下面，
        /// 这个前缀就是"我们家的东西"的标记。</summary>
        public const string SharedKeyPrefix = "Mxx1Toolbox.";
        public const string SharedKey = "Mxx1Toolbox.Common";
        /// <summary>「复制文件路径」**曾经**在晚六第一稿里是一棵子项树（`Mxx1Toolbox.CopyPath`）。
        /// 那一稿没发布，但万一有人点过「装上」，撤掉 / 启动修补时要把这棵孤儿树清掉 ——
        /// 所以名字还留着，只是不再有人写它（列在 SupersededTrees 里）。</summary>
        public const string SupersededCopyTreeKey = "Mxx1Toolbox.CopyPath";
        /// <summary>终端的两个子项树：文件夹那一档用 `%1`，两个**空白处**用 `%V` ——
        /// 子项命令里的占位符跟父项一样按位置替换，所以这两种不能共用一棵树（见 TreeKeyOf）。</summary>
        public const string TerminalTreeKey = "Mxx1Toolbox.Terminal";
        public const string TerminalBgTreeKey = "Mxx1Toolbox.Terminal.bg";
        public const string UnlockTitle = "解除文件占用";
        public const string AutoTitle = "一键解除占用";
        public const string CommonTitle = "常用功能";
        public const string CopyNameTitle = "复制文件名";
        public const string CopyPathTitle = "复制文件路径";
        public const string TerminalTitle = "在此处打开终端";
        /// <summary>终端子菜单里那两行的名字。**「复制」已经没有子菜单了**（两条平铺的一级项，
        /// 名字就是 `CopyNameTitle` / `CopyPathTitle`）：用户先要「相对路径」、随后发现
        /// **折出来的就是文件名**（右键时的工作目录就是文件所在的那个文件夹），于是改口
        /// 「相对路径不要了，或者新增按钮复制文件名就好了」；再往后他要两条直接摆平
        /// （「复制文件名」+「复制文件路径」），所以复制那两行不再走子项树。</summary>
        public const string TerminalCmdName = "cmd";
        public const string TerminalPsName = "PowerShell";
        public const string ItemUnlock = "unlock";
        public const string ItemAuto = "auto";
        public const string ItemCommon = "common";
        public const string ItemCopyName = "copyname";
        public const string ItemCopyPath = "copypath";
        public const string ItemTerminal = "terminal";
        public const string BackupName = "rightmenu-installed.tsv";

        /// <summary>六个菜单项。**这里的顺序 = 菜单里的顺序**（= 键名里那个序号），所以状态报告、
        /// 说明、残留检测都按它排 —— 别在别处另写一份名单。
        /// 用户 2026-10-06 晚七拍板的顺序：复制文件名 / 复制文件路径 / 解除文件占用 /
        /// 一键解除占用 / 常用功能 / 在此处打开终端。
        ///
        /// 其中只有两项是**级联子菜单**（「常用功能」和「在此处打开终端」：父项写
        /// `ExtendedSubCommandsKey`，内容写在共用的子项树里）；另外四项（复制文件名 / 复制文件路径 /
        /// 解除文件占用 / 一键解除占用）都是直接挂命令的普通项。
        /// 界面上是 **6 对**装 / 撤按钮（一共 16 个按钮），一对只管一项 ——
        /// ⚠️ **界面那一段的顺序没跟着动**（用户只要求改右键菜单里的顺序，见 tools\rightmenu.json）。</summary>
        public static readonly string[] AllItems = new string[]
        {
            ItemCopyName, ItemCopyPath, ItemUnlock, ItemAuto, ItemCommon, ItemTerminal,
        };

        /// <summary>命令里给"右键选中的那个路径"的占位：Explorer 会把它换成真实路径。</summary>
        public const string SelectedPlaceholder = "%1";

        /// <summary>背景位置（文件夹里的空白处 / 桌面空白处）要用 %V：那里 Explorer 不替换 %1。</summary>
        public const string BackgroundPlaceholder = "%V";

        /// <summary>六项菜单项各自的图标源（都是工具箱自己的按钮图标，装的时候转成 .ico）。
        /// 名字必须是**真实存在的按钮 id**（`assets\icons\<id>.png` 是 Make-Icons.ps1 按 id 生成的；
        /// 2026-10-05 写成 rightmenu.unlock.auto 这种不存在的 id 时，图标会静默缺失 —— M20a 盯着）。
        /// 终端子菜单里那两行共用父项那张图（cmd / PowerShell 看标题就分得清）。</summary>
        public const string UnlockIconId = "rightmenu.unlock.on";
        public const string AutoIconId = "rightmenu.auto.on";
        public const string CommonIconId = "rightmenu.common.on";
        public const string CopyNameIconId = "rightmenu.copyname.on";
        public const string CopyPathIconId = "rightmenu.copy.on";
        public const string TerminalIconId = "rightmenu.terminal.on";

        // ------------------------------------------------------------------ item（unlock / auto / common / copyname / copypath / terminal）
        //
        // 2026-10-05 加第三项「一键解除占用」（用户要的：保留原来那个弹窗口的，另外单独给一个
        // 不弹窗、直接结束占用它的程序的一键版 + 单独一对装 / 撤按钮）。
        // 2026-10-06 晚五加第四、五项「复制文件路径」和「在此处打开终端」（用户原话：
        // 「复制文件路径 / 在此处打开终端 这2个功能做一下」）—— 同样各一对装 / 撤按钮。
        // 2026-10-06 晚六：先按用户要求把后两项各拆成两个菜单项、但界面还是 5 对按钮（点一次装两条），
        // 他看过效果图之后又改口 —— **「复制」不做子菜单、直接两条平铺**（「复制文件名」+
        // 「复制文件路径」，「我要这个效果，然后记得加上安装和卸载按钮」），
        // **「终端」保持子菜单一条**。所以现在是 **6 项 / 6 对按钮（16 个）**。
        // 六项共用同一张位置表、同一套"写前记原值 → 写完读回核对 → 只删自己那几个键"的规矩，
        // 所以下面这几个小映射函数是唯一的"项目 → 键名 / 标题 / 图标 / 命令 / 装哪几个位置"的出处。

        private static string VerbOf(string item)
        {
            if (item == ItemAuto) { return AutoVerb; }
            if (item == ItemCommon) { return CommonVerb; }
            if (item == ItemCopyName) { return CopyNameVerb; }
            if (item == ItemCopyPath) { return CopyPathVerb; }
            if (item == ItemTerminal) { return TerminalVerb; }
            return UnlockVerb;
        }

        /// <summary>这一项**上一版用的**键名（不带序号那批，2026-10-06 晚七以前）。只用来做迁移：
        /// 老用户升级上来时把旧键名换成新键名，菜单顺序才会跟着变。返回空 = 这一项没有旧名字。</summary>
        private static string LegacyVerbOf(string item)
        {
            if (item == ItemCopyName) { return LegacyCopyNameVerb; }
            if (item == ItemCopyPath) { return LegacyCopyPathVerb; }
            if (item == ItemUnlock) { return LegacyUnlockVerb; }
            if (item == ItemAuto) { return LegacyAutoVerb; }
            if (item == ItemCommon) { return LegacyCommonVerb; }
            if (item == ItemTerminal) { return LegacyTerminalVerb; }
            return "";
        }

        private static string TitleOf(string item)
        {
            if (item == ItemAuto) { return AutoTitle; }
            if (item == ItemCommon) { return CommonTitle; }
            if (item == ItemCopyName) { return CopyNameTitle; }
            if (item == ItemCopyPath) { return CopyPathTitle; }
            if (item == ItemTerminal) { return TerminalTitle; }
            return UnlockTitle;
        }

        private static string IconOf(string item)
        {
            if (item == ItemAuto) { return AutoIconId; }
            if (item == ItemCommon) { return CommonIconId; }
            if (item == ItemCopyName) { return CopyNameIconId; }
            if (item == ItemCopyPath) { return CopyPathIconId; }
            if (item == ItemTerminal) { return TerminalIconId; }
            return UnlockIconId;
        }

        /// <summary>这一项是不是**级联子菜单**（父项不写命令，只写 `ExtendedSubCommandsKey`）。
        /// 只剩「常用功能」和「在此处打开终端」两项 —— 「复制」那两条是普通项。</summary>
        private static bool IsSubmenu(string item)
        {
            return (item == ItemCommon) || (item == ItemTerminal);
        }

        /// <summary>直接挂在父项上的命令（不是子菜单的那四项）。unlock = 开那个结果窗口；
        /// auto = 不弹窗、直接解锁；copyname / copypath = 复制文件名 / 复制完整路径，
        /// **都不带 `--quote`**（用户要的"两边不许有引号"）。子菜单那两项的命令写在子项树里
        /// （见 FixedChildren）。</summary>
        private static string CommandOf(string item, string placeholder)
        {
            if (item == ItemAuto) { return QuoteExe() + " rightmenu unlock --auto \"" + placeholder + "\""; }
            if (item == ItemCopyName) { return QuoteExe() + " copypath --name \"" + placeholder + "\""; }
            if (item == ItemCopyPath) { return QuoteExe() + " copypath \"" + placeholder + "\""; }
            return QuoteExe() + " rightmenu unlock \"" + placeholder + "\"";
        }

        /// <summary>这一项装在哪几个位置。解锁那三项四个位置都有；**「复制文件名」和
        /// 「复制文件路径」只装在"有选中东西"的两个位置**（任意文件 / 文件夹）—— 背景位置没有
        /// 选中项，装上去点一下只会得到一句"路径不存在"；「在此处打开终端」反过来：
        /// **背景那两个位置才是重点**（"在这个文件夹里开终端"），加上右键文件夹那一个。</summary>
        private static RightMenuLocation[] LocationsOf(string item)
        {
            List<RightMenuLocation> list = new List<RightMenuLocation>();
            foreach (RightMenuLocation loc in Table)
            {
                bool take = true;
                if (item == ItemCopyName || item == ItemCopyPath) { take = (loc.Id == "files") || (loc.Id == "folder"); }
                else if (item == ItemTerminal) { take = (loc.Id == "folder") || (loc.Id == "folderbg") || (loc.Id == "desktop"); }
                if (take) { list.Add(loc); }
            }
            return list.ToArray();
        }

        private static readonly RightMenuLocation[] Table = new RightMenuLocation[]
        {
            new RightMenuLocation("files",    "任意文件",           "*\\shell",                   SelectedPlaceholder),
            new RightMenuLocation("folder",   "文件夹",             "Directory\\shell",           SelectedPlaceholder),
            new RightMenuLocation("folderbg", "文件夹里的空白处",   "Directory\\Background\\shell", BackgroundPlaceholder),
            new RightMenuLocation("desktop",  "桌面空白处",         "DesktopBackground\\Shell",   BackgroundPlaceholder),
        };

        /// <summary>这一项装不装在这个位置（和 LocationsOf 同一张表，单独问一个位置时用它，
        /// 换名那一段要按位置决定"新键应该在这儿吗"）。</summary>
        private static bool InstallsAt(string item, RightMenuLocation loc)
        {
            foreach (RightMenuLocation l in LocationsOf(item))
            {
                if (string.Equals(l.Id, loc.Id, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ 级联子菜单的内容
        //
        // 父项（`Mxx1Common` / `Mxx1Terminal`）只写一个 `ExtendedSubCommandsKey` 指到一棵
        // **共用子项树**（`HKCU\Software\Classes\Mxx1Toolbox.*`），子项本身也放在那棵树里 ——
        // 「常用功能」那棵树的内容是动态的（置顶 / 最近使用变了要整棵重写）；
        // 终端那两棵是**固定**的两行（cmd / PowerShell）。
        //
        // ⚠️ 「复制」**不再走这条路**：用户晚六最后把它改成两条平铺的一级菜单项（见 CommandOf）。

        /// <summary>这一项在这个位置上该指哪棵子树。空 = 这一项不是子菜单。
        ///
        /// ⚠️ 终端的两个**空白处**要单独一棵树：子项命令里的占位符是**跟着父项所在的右键位置**
        /// 替换的（`%1` = 选中的那个东西；空白处那里资源管理器不替换 `%1`），所以 `%1` 和 `%V`
        /// 两种命令不能共用一个 `command` 值。</summary>
        private static string TreeKeyOf(string item, RightMenuLocation loc)
        {
            if (item == ItemCommon) { return SharedKey; }
            if (item == ItemTerminal)
            {
                return (loc.Placeholder == BackgroundPlaceholder) ? TerminalBgTreeKey : TerminalTreeKey;
            }
            return "";
        }

        /// <summary>子菜单里那两行（名字 + 命令 + 图标）—— 现在只剩**终端**一项。
        /// 两行各把一个终端**钉死**（`--cmd` / `--ps`），不再走"wt → PowerShell → cmd"那个自动挑。
        /// （「复制」那两条晚六改成平铺的一级项了，命令在 CommandOf 里，**不带 `--quote`**。）</summary>
        private static List<RightMenuEntry> FixedChildren(string item, string placeholder)
        {
            List<RightMenuEntry> list = new List<RightMenuEntry>();
            if (item == ItemTerminal)
            {
                list.Add(Child(TerminalCmdName, QuoteExe() + " terminal --cmd \"" + placeholder + "\"", TerminalIconId));
                list.Add(Child(TerminalPsName, QuoteExe() + " terminal --ps \"" + placeholder + "\"", TerminalIconId));
            }
            return list;
        }

        private static RightMenuEntry Child(string name, string command, string iconId)
        {
            RightMenuEntry e = new RightMenuEntry();
            e.Name = name;
            e.Command = command;
            e.IconId = iconId;
            return e;
        }

        // ------------------------------------------------------------------ 晚六第一稿留下的四个独立项
        //
        // 同一天的稿子先在每个位置装了**四条独立菜单项**（`Mxx1CopyPathRel` 这种）。用户随后澄清
        // 「我是让你设定为可以多选而不是新增一个菜单，是要你做成子菜单」→ 改成子菜单。
        // 那份稿子没发布，但**万一有人点过「装上」**，装上 / 撤掉 / 启动修补时都要把这四条清掉，
        // 否则他会同时看到四条独立项和两棵子菜单。只删确认是我们的（MUIVerb 对得上那四句）。

        private static readonly string[] SupersededVerbs = new string[]
        {
            SupersededCopyRelVerb, SupersededCopyAbsVerb,
            SupersededTerminalCmdVerb, SupersededTerminalPsVerb,
        };

        private static readonly string[] SupersededTitles = new string[]
        {
            "复制相对路径", "复制绝对路径",
            "在此处打开终端（cmd）", "在此处打开终端（PowerShell）",
        };

        private static bool IsSupersededTitle(string text)
        {
            foreach (string t in SupersededTitles)
            {
                if (string.Equals(text, t, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        /// <summary>清掉晚六第一稿那四个独立项（如果装过）。`sb` 给 null 就闭嘴（启动时的自动修补
        /// 用它）、`records` 给 null 就不管记录。返回删掉的处数。</summary>
        private static int CleanSuperseded(StringBuilder sb, List<RightMenuRecord> records)
        {
            int removed = 0;
            foreach (RightMenuLocation loc in Table)
            {
                foreach (string verb in SupersededVerbs)
                {
                    string relative = loc.Key + "\\" + verb;
                    string full = RootKey + "\\" + relative;
                    if (!KeyExists(full)) { continue; }
                    string title = ReadText(full, "MUIVerb");
                    if (!IsSupersededTitle(title))
                    {
                        if (sb != null)
                        {
                            sb.Append("  × 这个位置的 ").Append(verb).Append(" 不是工具箱写的，没动它：").Append(full).AppendLine();
                        }
                        continue;
                    }
                    string error = DeleteTree(full);
                    if (error.Length > 0)
                    {
                        if (sb != null) { sb.Append("  × 第一稿那个独立项没清掉（").Append(full).Append("）：").Append(error).AppendLine(); }
                        continue;
                    }
                    removed++;
                    if (sb != null)
                    {
                        sb.Append("  √ 清掉了第一稿的独立项：").Append(loc.Label).Append(" · ").Append(title).AppendLine();
                    }
                }
            }
            if ((records != null) && (removed > 0))
            {
                List<string> gone = new List<string>();
                foreach (string verb in SupersededVerbs) { gone.Add(verb); }
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    string key = (records[i].Key == null) ? "" : records[i].Key;
                    bool hit = false;
                    foreach (string verb in gone) { if (key.EndsWith("\\" + verb, StringComparison.OrdinalIgnoreCase)) { hit = true; break; } }
                    if (hit) { records.RemoveAt(i); }
                }
            }
            if (removed > 0)
            {
                Logger.Write("右键增强", "清掉了第一稿的四个独立项（"
                    + removed.ToString(CultureInfo.InvariantCulture) + " 处）");
            }
            return removed;
        }

        /// <summary>把上一版那批**不带序号**的旧键名换成这一版带序号的（菜单顺序就是靠键名排的，
        /// 不换名顺序不会变 —— 见文件顶部那段说明和 DESIGN §12.64）。
        ///
        /// 规矩（**顺序不能反**）：先确认新键名那一项在这个位置已经装好、而且是工具箱写的，
        /// 才删旧键；反过来先删旧的，用户会短暂地（最坏是永久地）少一项。
        /// 新键没写成的那些位置（比如那里蹲着一个不是我们写的同名键）一律**不删**旧键，
        /// 留着让人还能用，并把原因念在报告里。
        ///
        /// `only` 给 null = 所有项（启动修补那条路用）；否则只处理名单里的项
        /// （用户点了哪一对「装上…」就只换那一项，别的项一个字节都不动）。
        /// `sb` 给 null 就闭嘴（启动时那条路）。返回换掉的处数。</summary>
        private static int MigrateLegacyNames(StringBuilder sb, List<RightMenuRecord> records, List<string> only)
        {
            int moved = 0;
            List<string> goneKeys = new List<string>();
            foreach (string item in AllItems)
            {
                if ((only != null) && !Contains(only, item)) { continue; }
                string legacy = LegacyVerbOf(item);
                if (legacy.Length == 0) { continue; }
                foreach (RightMenuLocation loc in Table)
                {
                    string relOld = loc.Key + "\\" + legacy;
                    string fullOld = RootKey + "\\" + relOld;
                    if (!KeyExists(fullOld)) { continue; }
                    if (!IsOurs(fullOld, relOld))
                    {
                        if (sb != null)
                        {
                            sb.Append("  × ").Append(fullOld).Append(" 不是工具箱写的，没动它").AppendLine();
                        }
                        continue;
                    }
                    // 这一项本来该装在这儿的话，新键必须先在那儿（写完读回核对过才算）
                    if (InstallsAt(item, loc))
                    {
                        string relNew = loc.Key + "\\" + VerbOf(item);
                        string fullNew = RootKey + "\\" + relNew;
                        if (!KeyExists(fullNew) || !IsOurs(fullNew, relNew)) { continue; }
                    }
                    string error = DeleteTree(fullOld);
                    if (error.Length > 0)
                    {
                        if (sb != null)
                        {
                            sb.Append("  × 旧键名没换掉（").Append(fullOld).Append("）：").Append(error).AppendLine();
                        }
                        continue;
                    }
                    goneKeys.Add(fullOld);
                    moved++;
                }
            }
            if (moved > 0)
            {
                // 旧键的记录也摘掉（不摘的话「撤掉…」会念一句"记录里有、注册表里没有"）
                if (records != null)
                {
                    for (int i = records.Count - 1; i >= 0; i--)
                    {
                        string key = (records[i].Key == null) ? "" : records[i].Key;
                        if (Contains(goneKeys, key)) { records.RemoveAt(i); }
                    }
                    SaveRecords(records);
                }
                Logger.Write("右键增强", "把上一版的旧键名换成带序号的（"
                    + moved.ToString(CultureInfo.InvariantCulture) + " 处）");
            }
            return moved;
        }

        private static readonly string[] CriticalProcesses = new string[]
        {
            "system", "registry", "memory compression", "idle", "smss", "csrss", "wininit",
            "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg",
        };

        public static RightMenuLocation[] Locations { get { return Table; } }

        /// <summary>菜单里从上到下的标题（顺序 = `AllItems` = 键名里的序号）。`rightmenu items`
        /// 打它，回归测试也读它。</summary>
        public static string[] MenuTitles
        {
            get
            {
                List<string> list = new List<string>();
                foreach (string item in AllItems) { list.Add(TitleOf(item)); }
                return list.ToArray();
            }
        }

        /// <summary>菜单里从上到下的 verb 名 —— **"菜单顺序"唯一能被机器读到的出口**：
        /// 顺序是 Windows 按键名排的，所以这里念出来的顺序就是菜单里真正的顺序（见 DESIGN §12.64）。</summary>
        public static string[] MenuVerbs
        {
            get
            {
                List<string> list = new List<string>();
                foreach (string item in AllItems) { list.Add(VerbOf(item)); }
                return list.ToArray();
            }
        }

        /// <summary>上一版那批不带序号的旧键名（换名迁移用；也打出来给回归测试核对）。</summary>
        public static string[] LegacyVerbs
        {
            get
            {
                List<string> list = new List<string>();
                foreach (string item in AllItems)
                {
                    string v = LegacyVerbOf(item);
                    if (v.Length > 0) { list.Add(v); }
                }
                return list.ToArray();
            }
        }

        public static string BackupFile
        {
            get { return Path.Combine(AppPaths.BaseDir, BackupName); }
        }

        /// <summary>装到哪个根下面。默认 HKCU\Software\Classes；测试用环境变量挪到
        /// HKCU\Software\mxx1-toolbox\rightmenu-test，这样回归测试永远不会碰到真实右键菜单。</summary>
        public static string RootKey
        {
            get
            {
                string custom = AppPaths.Expand(Environment.GetEnvironmentVariable("MXX1_RIGHTMENU_ROOT"));
                custom = (custom == null) ? "" : custom.Trim();
                if (custom.Length == 0) { return "Software\\Classes"; }
                custom = custom.TrimStart('\\');
                if (custom.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
                {
                    custom = custom.Substring(5);
                }
                else if (custom.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                {
                    custom = custom.Substring(18);
                }
                return custom;
            }
        }

        public static bool IsTestRoot
        {
            get { return !string.Equals(RootKey, "Software\\Classes", StringComparison.OrdinalIgnoreCase); }
        }

        public static string RootLabel { get { return "HKCU\\" + RootKey; } }

        // ------------------------------------------------------------------ 装 / 撤

        /// <summary>装上（或修复）右键项。传要装哪几项（`AllItems` 里的 id，至少一项）。
        /// 已装过 = 覆盖成当前版本（exe 路径可能搬过家），所以这个按钮同时也是"修复"。</summary>
        public static string Install(List<string> items, out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("把工具箱装进 Windows 右键菜单");
            if (IsTestRoot) { sb.AppendLine("（测试根：" + RootLabel + " —— 没有碰真实的右键菜单）"); }
            sb.AppendLine();

            int done = 0;
            int skipped = 0;
            foreach (string item in items)
            {
                if (IsSubmenu(item)) { continue; }       // 子菜单那两项另走一遍（见下）
                foreach (RightMenuLocation loc in LocationsOf(item))
                {
                    bool wrote;
                    string error = InstallSimpleVerb(loc, item, out wrote);
                    if (wrote) { done++; sb.Append("  √ ").Append(TitleOf(item)).Append(" · ").Append(loc.Label).AppendLine(); }
                    else if (error.Length > 0) { skipped++; sb.Append("  × ").Append(loc.Label).Append("  ").Append(error).AppendLine(); }
                }
            }

            // 级联子菜单：父项（每个位置一个键）+ 它指过去的那棵子项树
            bool treeGap = false;
            foreach (string item in items)
            {
                if (!IsSubmenu(item)) { continue; }
                foreach (RightMenuLocation loc in LocationsOf(item))
                {
                    bool wrote;
                    string error = InstallSubmenuVerb(loc, item, TreeKeyOf(item, loc), out wrote);
                    if (wrote) { done++; sb.Append("  √ ").Append(TitleOf(item)).Append(" · ").Append(loc.Label).AppendLine(); }
                    else if (error.Length > 0) { skipped++; sb.Append("  × ").Append(loc.Label).Append("  ").Append(error).AppendLine(); }
                }
                // 树写一次就够（终端那三个位置共用两棵：`%1` 一棵、`%V` 一棵）
                List<string> trees = new List<string>();
                foreach (RightMenuLocation loc in LocationsOf(item))
                {
                    string t = TreeKeyOf(item, loc);
                    if (t.Length > 0 && !Contains(trees, t)) { trees.Add(t); }
                }
                sb.AppendLine();
                foreach (string tree in trees)
                {
                    bool dynamic = (item == ItemCommon);
                    List<RightMenuEntry> rows;
                    if (dynamic)
                    {
                        int pinned;
                        int recent;
                        rows = BuildEntries(out pinned, out recent);
                    }
                    else
                    {
                        string ph = (tree == TerminalBgTreeKey) ? BackgroundPlaceholder : SelectedPlaceholder;
                        rows = FixedChildren(item, ph);
                    }
                    string treeError;
                    string report = WriteTree(tree, rows, out treeError);
                    if (treeError.Length > 0)
                    {
                        treeGap = true;                     // 树没写成就等于这一项点下去是空的 —— 算失败
                        sb.Append("  × ").Append(TitleOf(item)).Append(" 的子项没写成：").Append(treeError).AppendLine();
                    }
                    else
                    {
                        sb.Append("  √ ").Append(TitleOf(item)).Append(" 的子菜单：").Append(report).AppendLine();
                    }
                }
            }
            if (treeGap) { skipped++; }

            sb.AppendLine();
            // 晚七：把上一版那批**不带序号**的旧键名换成带序号的（菜单里的先后顺序就是按键名排的，
            // 见文件顶部说明）。必须排在上面两轮写入之后 —— 里面"先确认新键装好了才删旧键"。
            {
                List<RightMenuRecord> recs = LoadRecords();
                int moved = MigrateLegacyNames(sb, recs, items);
                if (moved > 0)
                {
                    sb.Append("  （顺手把上一版的旧键名换掉了 ").Append(moved.ToString(CultureInfo.InvariantCulture))
                      .Append(" 处 —— 键名里那个序号就是右键菜单里的先后顺序）").AppendLine();
                }
            }
            // 晚六第一稿那四个独立项（如果装过）：装上时顺手清掉，见 CleanSuperseded 那段说明。
            // 现在**这四条正好全都要清**：复制那两条第一稿用的是 `Mxx1CopyPathRel`/`Abs`、
            // 终端那两条用的是 `Mxx1TerminalCmd`/`Ps`，跟这一版的名字都不一样。
            if (Contains(items, ItemCopyName) || Contains(items, ItemCopyPath) || Contains(items, ItemTerminal))
            {
                List<RightMenuRecord> recs = LoadRecords();
                int gone = CleanSuperseded(sb, recs);
                if (gone > 0)
                {
                    SaveRecords(recs);
                    sb.Append("  （这一版不要第一稿那种独立项写法了，所以上面顺手清掉了那 ").Append(gone.ToString(CultureInfo.InvariantCulture))
                      .Append(" 个）").AppendLine();
                }
            }
            if (skipped == 0)
            {
                sb.Append("  ").Append(done.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处都写进去并读回核对过了。").AppendLine();
                sb.Append("  菜单图标：从工具箱自带的按钮图标生成 .ico 写在 ").Append(MenuIcons.Dir)
                  .AppendLine("，");
                sb.AppendLine("  右键里那几项（以及「常用功能」子菜单的每一项）都会显示图标 —— 注册表的 Icon");
                sb.AppendLine("  只能指向 exe/dll 或 .ico，指 .png 是没用的，所以这里要先转一道。");
                string names = NamesOf(items);
                sb.AppendLine("  怎么用：在资源管理器里右键一个文件 / 文件夹（或文件夹里的空白处）就能看到"
                    + names + "。").AppendLine();
                sb.Append("  反悔就点「撤掉…」或者「装…」旁边那两个按钮，它们只删工具箱自己写的键。");
            }
            else
            {
                sb.Append("  有 ").Append(skipped.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处没装成 —— 上面带 × 的就是（多半是有同名键但不是工具箱写的）。").AppendLine();
                sb.Append("  没动过的键一个字节都没改。");
            }
            ok = (skipped == 0 && done > 0);
            Logger.Write("右键增强", (ok ? "完成 · " : "部分失败 · ") + "装右键菜单（"
                + NamesOf(items) + "）");
            return sb.ToString();
        }

        /// <summary>报告里那串名字（「解除文件占用」、「一键解除占用」和「复制文件路径」…）。</summary>
        private static string NamesOf(List<string> items)
        {
            List<string> parts = new List<string>();
            foreach (string item in items)
            {
                parts.Add("「" + TitleOf(item) + "」");
            }
            if (parts.Count == 0) { return "什么都没选"; }
            if (parts.Count == 1) { return parts[0]; }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) { sb.Append((i == parts.Count - 1) ? "和" : "、"); }
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>撤掉：按记录删，只删工具箱自己写的键。</summary>
        public static string Uninstall(List<string> items, out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("把工具箱从 Windows 右键菜单里撤掉（只删工具箱自己写的键）");
            if (IsTestRoot) { sb.AppendLine("（测试根：" + RootLabel + " —— 没有碰真实的右键菜单）"); }
            sb.AppendLine();

            List<RightMenuRecord> records = LoadRecords();
            int removed = 0;
            int kept = 0;

            foreach (string item in items)
            {
                foreach (RightMenuLocation loc in LocationsOf(item))
                {
                    RemoveVerb(sb, records, item, loc.Key + "\\" + VerbOf(item),
                        loc.Label + " · " + TitleOf(item), ref removed, ref kept);
                }
            }
            // 晚六第一稿那四个独立项也要一起撤（不然用户点了「撤掉」，菜单里还留着它们）；
            // 顺便把它们的记录从 records 里摘掉，下面的 SaveRecords 就会把它写没了。
            if (Contains(items, ItemCopyName) || Contains(items, ItemCopyPath) || Contains(items, ItemTerminal))
            {
                removed += CleanSuperseded(sb, records);
            }
            // 子菜单那几项：父项删掉之后，它指过去的那棵树也要删（不然注册表里留一棵孤儿）。
            // 「复制」已经不是子菜单了，但晚六第一稿给它建过一棵树（SupersededCopyTreeKey）——
            // 撤掉复制那两条时顺手清掉。
            foreach (string item in items)
            {
                List<string> trees = new List<string>();
                if (item == ItemCommon) { trees.Add(SharedKey); }
                else if (item == ItemTerminal) { trees.Add(TerminalTreeKey); trees.Add(TerminalBgTreeKey); }
                else if (item == ItemCopyName || item == ItemCopyPath) { trees.Add(SupersededCopyTreeKey); }
                foreach (string tree in trees)
                {
                    string full = RootKey + "\\" + tree;
                    if (!KeyExists(full)) { continue; }
                    string error = DeleteTree(full);
                    if (error.Length == 0)
                    {
                        removed++;
                        sb.Append("  √ ").Append(TitleOf(item)).Append(" 的子项树（").Append(full).Append("）").AppendLine();
                    }
                    else
                    {
                        kept++;
                        sb.Append("  × ").Append(full).Append("  ").Append(error).AppendLine();
                    }
                }
            }

            List<RightMenuRecord> keep = new List<RightMenuRecord>();
            foreach (RightMenuRecord r in records)
            {
                if (!Contains(items, r.Item)) { keep.Add(r); }
            }
            string saveError = SaveRecords(keep);

            sb.AppendLine();
            if (kept == 0)
            {
                sb.Append("  删掉 ").Append(removed.ToString(CultureInfo.InvariantCulture)).Append(" 处。").AppendLine();
                sb.Append("  右键菜单里撤掉的那几项下一次弹菜单就没有了（不用重启电脑；菜单不刷新见「右键增强说明」）。");
            }
            else
            {
                sb.Append("  删掉 ").Append(removed.ToString(CultureInfo.InvariantCulture)).Append(" 处，")
                  .Append(kept.ToString(CultureInfo.InvariantCulture)).Append(" 处没敢动（上面带 × 的）。").AppendLine();
                sb.Append("  没敢动的那些不是工具箱写的（或者被别的程序占着），留给你自己处理。");
            }
            if (saveError.Length > 0)
            {
                sb.AppendLine();
                sb.Append("  记录文件没写回去：").Append(saveError).AppendLine();
                sb.Append("  （" + BackupName + " 里的记录可能不准了，下次装会重新记）");
            }
            // 五项都撤掉了：生成出来的 .ico 也没用了，删掉（留着一个空目录也只是碍眼）。
            bool anyLeft = false;
            foreach (string item in AllItems)
            {
                if (IsInstalled(item)) { anyLeft = true; break; }
            }
            if (!anyLeft)
            {
                MenuIcons.RemoveAll();
                sb.AppendLine();
                sb.Append("  顺带清掉了菜单图标的临时文件（").Append(MenuIcons.Dir).Append("）");
            }
            ok = (kept == 0);
            Logger.Write("右键增强", (ok ? "完成 · " : "部分失败 · ") + "撤右键菜单（"
                + NamesOf(items) + "）");
            return sb.ToString();
        }

        private static void RemoveVerb(StringBuilder sb, List<RightMenuRecord> records, string item,
            string relativeKey, string label, ref int removed, ref int kept)
        {
            string full = RootKey + "\\" + relativeKey;
            bool recorded = false;
            foreach (RightMenuRecord r in records)
            {
                if (r.Item == item && string.Equals(r.Key, full, StringComparison.OrdinalIgnoreCase)) { recorded = true; break; }
            }
            if (!KeyExists(full))
            {
                if (recorded) { sb.Append("  · ").Append(label).Append("  没装（记录里有，注册表里没有）").AppendLine(); }
                return;
            }
            if (!IsOurs(full, relativeKey))
            {
                kept++;
                sb.Append("  × ").Append(label).Append("  ").Append(full)
                  .Append(" 不是工具箱写的，没动它").AppendLine();
                return;
            }
            string error = DeleteTree(full);
            if (error.Length == 0) { removed++; sb.Append("  √ ").Append(label).AppendLine(); }
            else { kept++; sb.Append("  × ").Append(label).Append("  ").Append(error).AppendLine(); }
        }

        /// <summary>重建「常用功能」的子项（置顶 + 最近使用变了之后自动调用；界面也有手动按钮）。
        /// 没装过「常用功能」就什么都不做 —— 不许因为用户点了一个按钮就悄悄改注册表。</summary>
        public static string Rebuild(out bool ok)
        {
            ok = true;
            StringBuilder sb = new StringBuilder();
            if (!IsInstalled(ItemCommon))
            {
                sb.Append("「常用功能」还没装进右键菜单，所以没什么可重建的。").AppendLine();
                sb.Append("先点「装上常用功能」。");
                return sb.ToString();
            }
            string error;
            string report = WriteTree(SharedKey, CommonRows(), out error);
            sb.Append("重建「常用功能」子菜单").AppendLine();
            sb.AppendLine();
            if (error.Length > 0)
            {
                ok = false;
                sb.Append("  × 没写成：").Append(error).AppendLine();
                sb.Append("  （注册表被别的程序锁着？过一会儿再试一次）");
                return sb.ToString();
            }
            sb.Append(report).AppendLine();
            sb.AppendLine();
            sb.Append("  子菜单内容 = 工具箱「常用」页的镜像：置顶的按钮 + 最近用过的按钮");
            sb.Append("（最多 ").Append(UserTools.RecentLimit.ToString(CultureInfo.InvariantCulture)).Append(" 个）。").AppendLine();
            sb.Append("  在工具箱里把按钮置顶 / 点它一次，下次弹出右键菜单就会跟着变（这里是手动兜底）。");
            Logger.Write("右键增强", "重建常用功能子菜单");
            return sb.ToString();
        }

        /// <summary>关掉"启动时顺手修补右键菜单"（界面回归测试用：测试不该碰用户真实的菜单）。</summary>
        public static bool SyncDisabled
        {
            get
            {
                string v = AppPaths.Expand(Environment.GetEnvironmentVariable("MXX1_NO_RIGHTMENU_SYNC"));
                return v != null && v.Trim() == "1";
            }
        }

        /// <summary>安静地重建一次（界面上点过按钮 / 改过置顶之后调用）。没装就什么都不做，
        /// 出错也不弹东西 —— 这条路上不该因为注册表问题打断用户。
        ///
        /// 2026-10-04 起这里顺带**修补自己装过的键**：命令里的占位符（旧版「文件夹里的空白处」
        /// 和「桌面空白处」写的是 %1，资源管理器在那种位置不替换 %1）和图标（旧版指向没有图标资源的
        /// exe，菜单里是空白）。只重写 Mxx1* 这几个自己写的键，别人的键一个都不碰。
        ///
        /// 2026-10-06 晚六起：「终端」变成了**级联子菜单**（父项 + 子项树），老用户那个同名键
        /// `Mxx1Terminal` 会被**就地改写成子菜单父项**并补上子项树 —— 不用他自己想到"要再点一次装上"。
        /// 「复制」那两条是平铺项：`Mxx1CopyPath` 同名（就地改写成"不带 --quote"的新命令），
        /// 而**新加的 `Mxx1CopyName` 不会自己冒出来** —— 那一项要用户自己点一次「装上复制文件名」。</summary>
        public static void SyncIfInstalled()
        {
            try
            {
                List<string> installed = new List<string>();
                foreach (string item in AllItems)
                {
                    if (IsInstalled(item)) { installed.Add(item); }
                }
                if (installed.Count == 0) { return; }
                if (SyncDisabled) { return; }

                foreach (string item in installed)
                {
                    if (IsSubmenu(item)) { continue; }
                    foreach (RightMenuLocation loc in LocationsOf(item))
                    {
                        bool wrote;
                        InstallSimpleVerb(loc, item, out wrote);   // 内部有 ForeignReason 把关：别人的键不动
                    }
                }
                foreach (string item in installed)
                {
                    if (!IsSubmenu(item)) { continue; }
                    foreach (RightMenuLocation loc in LocationsOf(item))
                    {
                        bool wrote;
                        InstallSubmenuVerb(loc, item, TreeKeyOf(item, loc), out wrote);
                    }
                    string error;
                    if (item == ItemCommon)
                    {
                        WriteTree(SharedKey, CommonRows(), out error);
                    }
                    else if (item == ItemTerminal)
                    {
                        WriteTree(TerminalTreeKey, FixedChildren(ItemTerminal, SelectedPlaceholder), out error);
                        WriteTree(TerminalBgTreeKey, FixedChildren(ItemTerminal, BackgroundPlaceholder), out error);
                    }
                }
                // 换名（晚七）：上一版的键名不带序号，而**菜单顺序就是按键名排的** ——
                // 老用户升级上来光修命令是不够的，得把键名一起换掉顺序才会变。上面两轮已经把
                // 新键名的键写好了，所以换名排在这儿（里面"先确认新键装好了才删旧键"）。
                {
                    List<RightMenuRecord> recs = LoadRecords();
                    MigrateLegacyNames(null, recs, installed);
                }
                // 新写法都写好了，现在才清第一稿那四个独立项（顺序不能反）
                CleanSuperseded(null, null);
            }
            catch { }
        }

        // ------------------------------------------------------------------ 写键

        /// <summary>装一个"普通 verb"（「解除文件占用」/「一键解除占用」—— 点下去直接跑一条命令的那种）：
        /// 写 MUIVerb + 图标 + MultiSelectModel + command，写完读回核对。
        ///
        /// 两项共用这一份实现：区别只在标题、图标和命令（一键解除那条多一个 --auto）。
        /// MultiSelectModel=Player = 一次选中多个文件时**只起一个进程**、所有路径一起传进来
        /// （Document 那种是每个文件起一个 = 选中 10 个弹 10 个窗口，不能要）。</summary>
        private static string InstallSimpleVerb(RightMenuLocation loc, string item, out bool wrote)
        {
            wrote = false;
            string title = TitleOf(item);
            string relative = loc.Key + "\\" + VerbOf(item);
            string full = RootKey + "\\" + relative;
            string foreign = ForeignReason(full, relative, title);
            if (foreign.Length > 0) { return foreign; }
            bool existed = KeyExists(full);

            string error;
            if (!WriteValue(full, "", "", out error)) { return error; }
            if (!WriteValue(full, "MUIVerb", title, out error)) { return error; }
            WriteIconValue(full, IconOf(item));
            if (!WriteValue(full, "MultiSelectModel", "Player", out error)) { return error; }
            string command = CommandOf(item, loc.Placeholder);
            if (!WriteValue(full + "\\command", "", command, out error)) { return error; }

            string back;
            if (!VerifyValue(full, "MUIVerb", title, out back))
            {
                return "写完读回来不是" + title + "（读到：" + back + "）";
            }
            string backCmd;
            if (!VerifyValue(full + "\\command", "", command, out backCmd))
            {
                return "命令写完读回来不对（读到：" + backCmd + "）";
            }
            Remember(item, full, existed);
            wrote = true;
            return "";
        }

        /// <summary>装一个**级联子菜单的父项**（「常用功能」/「复制文件路径」/「在此处打开终端」）：
        /// 写 MUIVerb + 图标 + MultiSelectModel + `ExtendedSubCommandsKey`（指到共用的子项树），
        /// 写完读回核对。父项**不写命令** —— 命令在子项树里。
        ///
        /// 这三项共用这一份实现，区别只在标题、图标和指哪棵树（`treeKey`）。</summary>
        private static string InstallSubmenuVerb(RightMenuLocation loc, string item, string treeKey, out bool wrote)
        {
            wrote = false;
            string title = TitleOf(item);
            string relative = loc.Key + "\\" + VerbOf(item);
            string full = RootKey + "\\" + relative;
            string foreign = ForeignReason(full, relative, title);
            if (foreign.Length > 0) { return foreign; }
            bool existed = KeyExists(full);

            string error;
            if (!WriteValue(full, "", "", out error)) { return error; }
            if (!WriteValue(full, "MUIVerb", title, out error)) { return error; }
            WriteIconValue(full, IconOf(item));
            if (!WriteValue(full, "MultiSelectModel", "Player", out error)) { return error; }
            // 子项只写一份（HKCU\Software\Classes\Mxx1Toolbox.*\shell\NN），这个位置指过去就行：
            // 重建子菜单只要写一个地方。
            if (!WriteValue(full, "ExtendedSubCommandsKey", treeKey, out error)) { return error; }

            string back;
            if (!VerifyValue(full, "MUIVerb", title, out back))
            {
                return "写完读回来不是" + title + "（读到：" + back + "）";
            }
            string backTree;
            if (!VerifyValue(full, "ExtendedSubCommandsKey", treeKey, out backTree))
            {
                return "子菜单指针读完不对（读到：" + backTree + "）";
            }
            Remember(item, full, existed);
            wrote = true;
            return "";
        }

        /// <summary>把一棵共用的子项树整棵重写：先删再建，保证没有上一次留下的孤儿项
        /// （「常用功能」那棵的内容会随置顶 / 最近使用变，所以每次都是整棵重写）。</summary>
        private static string WriteTree(string treeKey, List<RightMenuEntry> rows, out string error)
        {
            error = "";
            string full = RootKey + "\\" + treeKey;
            if (KeyExists(full))
            {
                error = DeleteTree(full);
                if (error.Length > 0) { return ""; }
            }
            int n = 0;
            int icons = 0;
            foreach (RightMenuEntry e in rows)
            {
                string key = full + "\\shell\\" + n.ToString("00", CultureInfo.InvariantCulture);
                if (!WriteValue(key, "MUIVerb", e.Name, out error)) { return ""; }
                if (WriteIconValue(key, e.IconId, e.IconPath)) { icons++; }
                if (e.SeparatorBefore) { WriteDword(key, "CommandFlags", 0x20); }
                if (!WriteValue(key + "\\command", "", e.Command, out error)) { return ""; }
                n++;
            }
            if (n == 0)
            {
                error = "没算出任何子项（置顶和最近使用都是空的？）";
                return "";
            }
            // 读回核对：子项数量对不上就当没写成（宁可报错，也别留一棵半截的树）
            int back = 0;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(full + "\\shell", false))
                {
                    if (k != null) { back = k.SubKeyCount; }
                }
            }
            catch { }
            if (back != n)
            {
                error = "写完读回来只有 " + back.ToString(CultureInfo.InvariantCulture) + " 项（应该 "
                    + n.ToString(CultureInfo.InvariantCulture) + " 项）";
                return "";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(" 项（")
              .Append(treeKey).Append("，带图标的 ").Append(icons.ToString(CultureInfo.InvariantCulture)).Append(" 项）");
            return sb.ToString();
        }

        /// <summary>「常用功能」子菜单的内容（置顶 + 最近用过 + 固定 3 项）。</summary>
        private static List<RightMenuEntry> CommonRows()
        {
            int pinned;
            int recent;
            return BuildEntries(out pinned, out recent);
        }

        /// <summary>算出子菜单里要放哪几行。置顶在前，最近用过在后（自动段之间有分隔线），
        /// 最后是固定三项。挑不出东西的那些按钮（灰色占位 / 界面动作 / 隐藏 / 「右键增强」自己这一页）
        /// 一律不进菜单 —— 点不动的按钮放进右键菜单比放进界面更容易让人以为是坏了。</summary>
        public static List<RightMenuEntry> BuildEntries(out int pinnedCount, out int recentCount)
        {
            List<RightMenuEntry> list = new List<RightMenuEntry>();
            List<string> warnings = new List<string>();
            List<ToolItem> all = ToolRegistry.LoadAll(warnings);
            List<string> pinnedIds = UserTools.LoadPinned();
            List<string> recentIds = UserTools.LoadRecent();

            List<ToolItem> pinnedTools = new List<ToolItem>();
            List<ToolItem> recentTools = new List<ToolItem>();
            List<string> seen = new List<string>();
            foreach (string id in pinnedIds)
            {
                ToolItem t = Find(all, id);
                if (t == null || !MenuWorthy(t)) { continue; }
                if (Contains(seen, t.Id)) { continue; }
                seen.Add(t.Id);
                pinnedTools.Add(t);
            }
            foreach (string id in recentIds)
            {
                ToolItem t = Find(all, id);
                if (t == null || !MenuWorthy(t)) { continue; }
                if (Contains(seen, t.Id)) { continue; }
                seen.Add(t.Id);
                recentTools.Add(t);
            }
            pinnedCount = pinnedTools.Count;
            recentCount = recentTools.Count;

            foreach (ToolItem t in pinnedTools) { list.Add(EntryFor(t)); }
            for (int i = 0; i < recentTools.Count; i++)
            {
                RightMenuEntry e = EntryFor(recentTools[i]);
                // 置顶那一段和「最近用过」之间画一条分隔线（ECF_SEPARATORBEFORE）。
                if (i == 0 && list.Count > 0) { e.SeparatorBefore = true; }
                list.Add(e);
            }

            List<RightMenuEntry> fixedItems = new List<RightMenuEntry>();
            RightMenuEntry open = new RightMenuEntry();
            open.Id = "app.open";
            open.IconId = CommonIconId;
            open.Name = "打开工具箱";
            open.SeparatorBefore = true;
            open.Command = QuoteExe();
            fixedItems.Add(open);
            RightMenuEntry log = new RightMenuEntry();
            log.Id = "app.log";
            log.IconId = "export-logs";
            log.Name = "运行日志";
            log.Command = QuoteExe() + " ui log";
            fixedItems.Add(log);
            RightMenuEntry settings = new RightMenuEntry();
            settings.Id = "app.settings";
            settings.IconId = "control-panel";
            settings.Name = "设置";
            settings.Command = QuoteExe() + " ui settings";
            fixedItems.Add(settings);
            list.AddRange(fixedItems);
            return list;
        }

        private static RightMenuEntry EntryFor(ToolItem t)
        {
            RightMenuEntry e = new RightMenuEntry();
            e.Id = t.Id;
            e.Name = t.Name;
            e.IconId = t.Id;
            e.IconPath = t.IconPath;
            // `--show` 是给"结果就是一段文字"的按钮用的（激活状态 / 查看设置改动 / 导出系统日志…）：
            // 工具箱这个 exe 是 winexe、**没有控制台**，不弹窗口的话用户点了等于"没有效果"
            // （2026-10-04 用户报的就是这两个按钮）。有文字结果或失败时才弹，所以对
            // "本来就会自己开窗口的程序"（性能监视器 / 服务 / 设备管理器）没有副作用。
            e.Command = QuoteExe() + " run " + t.Id + (t.Danger ? " --confirm" : "") + " --show";
            return e;
        }

        private static bool MenuWorthy(ToolItem t)
        {
            if (t == null || t.Hidden || t.Placeholder) { return false; }
            if (string.Equals(t.Tab, Tabs.RightMenu, StringComparison.OrdinalIgnoreCase)) { return false; }
            if (t.Kind == "builtin" && t.Module == Launcher.ModuleApp) { return false; }
            return true;
        }

        private static ToolItem Find(List<ToolItem> all, string id)
        {
            foreach (ToolItem t in all)
            {
                if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) { return t; }
            }
            return null;
        }

        private static bool Contains(List<string> list, string id)
        {
            foreach (string s in list)
            {
                if (string.Equals(s, id, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------ 状态 / 说明

        /// <summary>只读：装没装、装在哪几个位置、子菜单现在几项、exe 路径还有效吗。</summary>
        public static string Status()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("右键增强当前状态（只读，没改任何东西）");
            if (IsTestRoot) { sb.AppendLine("（测试根：" + RootLabel + "）"); }
            sb.AppendLine("（下面六行的**先后就是右键菜单里的先后** —— 顺序是按注册表键名里的序号排的，见「右键增强说明」）");
            sb.AppendLine();

            foreach (string item in AllItems)
            {
                // 标题里有「在此处打开终端（PowerShell）」这种比较长的，列宽给到 24
                // （别改回 16：那会让状态报告右半边参差不齐）
                sb.Append("  ").Append(RegEngine.PadCjk(TitleOf(item), 24));
                List<string> where = InstalledLocations(item);
                if (where.Count == 0) { sb.Append("没装"); }
                else
                {
                    sb.Append("已装 · ").Append(where.Count.ToString(CultureInfo.InvariantCulture))
                      .Append(" 个位置（").Append(string.Join(" / ", where.ToArray())).Append("）");
                }
                if (item == ItemUnlock)
                {
                    sb.Append("　要管理员才看得到别人的进程").AppendLine();
                }
                else if (item == ItemAuto)
                {
                    sb.Append("　点了不弹窗口：查到占用就直接结束那些程序").AppendLine();
                }
                else if (item == ItemCopyName)
                {
                    sb.Append("　只取名字，不带路径；多选一行一个，两边都不带引号").AppendLine();
                }
                else if (item == ItemCopyPath)
                {
                    sb.Append("　完整路径（像 D:\\资料\\报告.txt）；多选一行一个，两边都不带引号").AppendLine();
                }
                else if (item == ItemTerminal)
                {
                    sb.Append("　子菜单（2 项）：").Append(TerminalCmdName).Append(" / ").Append(TerminalPsName)
                      .Append("，各开各的，不再替你挑").AppendLine();
                }
                else
                {
                    sb.AppendLine();
                }
            }

            // 上一版那批**不带序号**的旧键名（晚七换了名）：还在的话顺序就还是老的，
            // 如实念出来 —— 打开一次工具箱（启动修补）或者点一次「装上…」就会换掉。
            List<string> oldNames = new List<string>();
            foreach (string item in AllItems)
            {
                string legacy = LegacyVerbOf(item);
                if (legacy.Length == 0) { continue; }
                foreach (RightMenuLocation loc in Table)
                {
                    string rel = loc.Key + "\\" + legacy;
                    string full = RootKey + "\\" + rel;
                    if (KeyExists(full) && IsOurs(full, rel)) { oldNames.Add(full); }
                }
            }
            if (oldNames.Count > 0)
            {
                sb.Append("  ").Append(RegEngine.PadCjk("上一版的键名", 24));
                sb.Append("还有 ").Append(oldNames.Count.ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" 处在（菜单顺序还是老的）—— 打开一次工具箱就会自动换成新键名，换完顺序才是上面这个");
            }

            // 晚六第一稿那四个独立项（万一装过）：如实念出来，并说明点一次装上就会换掉
            int oldStyle = 0;
            foreach (RightMenuLocation loc in Table)
            {
                foreach (string verb in SupersededVerbs)
                {
                    string full = RootKey + "\\" + loc.Key + "\\" + verb;
                    if (KeyExists(full) && IsSupersededTitle(ReadText(full, "MUIVerb"))) { oldStyle++; }
                }
            }
            if (oldStyle > 0)
            {
                sb.Append("  ").Append(RegEngine.PadCjk("第一稿的独立项", 24));
                sb.Append("还有 ").Append(oldStyle.ToString(CultureInfo.InvariantCulture))
                  .AppendLine(" 个在菜单里（点一次「装上…」就清掉）");
            }

            // 子菜单内容
            int pinned;
            int recent;
            List<RightMenuEntry> items = BuildEntries(out pinned, out recent);
            sb.Append("  ").Append(RegEngine.PadCjk(CommonTitle + " 子菜单", 16));
            if (IsInstalled(ItemCommon))
            {
                sb.Append("现在会有 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项（置顶 ")
                  .Append(pinned.ToString(CultureInfo.InvariantCulture)).Append(" + 最近用过 ")
                  .Append(recent.ToString(CultureInfo.InvariantCulture)).Append(" + 固定 3 项）").AppendLine();
            }
            else
            {
                sb.Append("没装，装了会有 ").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项").AppendLine();
            }
            sb.Append("      最近使用最多留 ").Append(UserTools.RecentLimit.ToString(CultureInfo.InvariantCulture))
              .Append(" 个（").Append(UserTools.RecentFile).Append("）").AppendLine();

            // exe 路径
            string exe = AppPaths.ExePath;
            sb.Append("  ").Append(RegEngine.PadCjk("菜单里的 exe", 16)).Append(exe);
            sb.Append(File.Exists(exe) ? "（在）" : "（不在了！菜单点了会报错，重装一次就好）").AppendLine();

            // 菜单图标：Icon 指的是生成出来的 .ico 文件。那批文件被清理软件 / 测试的卸载删掉之后，
            // 菜单项就变成空白图标（用户 2026-10-04 报过一次），所以这里念一句。
            int iconOk = 0;
            int iconBad = 0;
            foreach (RightMenuLocation loc in Table)
            {
                foreach (string verb in AllVerbs())
                {
                    string key = RootKey + "\\" + loc.Key + "\\" + verb;
                    if (!KeyExists(key)) { continue; }
                    string icon = ReadText(key, "Icon");
                    bool ok = false;
                    try { ok = (icon.Length > 0) && File.Exists(icon); }
                    catch { }
                    if (ok) { iconOk++; } else { iconBad++; }
                }
            }
            sb.Append("  ").Append(RegEngine.PadCjk("菜单图标", 16));
            if (iconOk + iconBad == 0) { sb.AppendLine("还没装，没什么可看的"); }
            else if (iconBad == 0)
            {
                sb.Append(iconOk.ToString(CultureInfo.InvariantCulture)).Append(" 个都在（")
                  .Append(MenuIcons.Dir).Append("）").AppendLine();
            }
            else
            {
                sb.Append("有 ").Append(iconBad.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个不见了（文件被删了？）—— 点一次「装上…」会重新生成").AppendLine();
            }

            sb.Append(CcmpNote());

            // 记录 / 残留
            List<RightMenuRecord> records = LoadRecords();
            sb.AppendLine();
            if (records.Count > 0)
            {
                sb.Append("  装的时候记了 ").Append(records.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条现场：").Append(BackupFile).AppendLine();
            }
            else
            {
                sb.Append("  没有记录文件（还没装过，或者记录被删了）").AppendLine();
            }
            List<string> leftovers = Leftovers(records);
            if (leftovers.Count > 0)
            {
                sb.Append("  注意：检测到没记在案的工具箱键（").Append(leftovers.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个）—— 点「撤掉」可以清掉：").AppendLine();
                foreach (string s in leftovers) { sb.Append("      ").Append(s).AppendLine(); }
            }
            return sb.ToString();
        }

        private static List<string> Leftovers(List<RightMenuRecord> records)
        {
            List<string> list = new List<string>();
            foreach (string rel in KnownRelativeKeys())
            {
                string full = RootKey + "\\" + rel;
                if (!KeyExists(full) || !IsOurs(full, rel)) { continue; }
                bool known = false;
                foreach (RightMenuRecord r in records)
                {
                    if (string.Equals(r.Key, full, StringComparison.OrdinalIgnoreCase)) { known = true; break; }
                }
                if (!known) { list.Add(full); }
            }
            return list;
        }

        /// <summary>「右键增强说明」按钮的内容：原理、怎么卸干净、菜单没出现怎么办。</summary>
        public static string Help()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("右键增强是什么");
            sb.AppendLine();
            sb.AppendLine("  工具箱能往你的右键菜单里装六样东西，六样都只写当前用户（HKCU\\Software\\Classes），");
            sb.AppendLine("  不要管理员权限、不装 shell 扩展 DLL、不起服务、不加开机启动。");
            sb.AppendLine("  六样在菜单里都是**一级项**，看到就能点；只有「" + CommonTitle + "」和「" + TerminalTitle + "」");
            sb.AppendLine("  是**级联子菜单**：鼠标移上去会弹出几行让你选（Windows 那套 `ExtendedSubCommandsKey` 机制，");
            sb.AppendLine("  子项写在 HKCU\\Software\\Classes\\Mxx1Toolbox.* 下，父项只存一个指针）：");
            sb.AppendLine();
            sb.AppendLine("  1. " + CopyNameTitle + "、2. " + CopyPathTitle + " —— 右键一个文件 / 文件夹，");
            sb.AppendLine("     把**名字**（文件名 / 文件夹名）或**完整路径**复制进剪贴板。一次选中好几个就一行一个。");
            sb.AppendLine("     · 「" + CopyNameTitle + "」= 只取名字：文件 `报告.txt`、文件夹 `2026`，不带路径。");
            sb.AppendLine("       用户 2026-10-06：原来那版给的是「相对路径」，可右键时的工作目录**就是**文件所在的");
            sb.AppendLine("       那个文件夹，折出来正好就是文件名 —— 所以照他要的直接给名字。");
            sb.AppendLine("     · 「" + CopyPathTitle + "」= 完整路径（像 D:\\资料\\报告.txt 这样）。");
            sb.AppendLine("     两条**都不带引号**（用户定的：带引号粘到别处还得自己动手删）。");
            sb.AppendLine("     两条都**只读** —— 不移动、不改名、不删除任何文件。");
            sb.AppendLine("  3. " + UnlockTitle + " —— 右键一个文件 / 文件夹，看到是谁占着它，勾一下就能把");
            sb.AppendLine("     那个程序结束掉（用的是 Windows 自带的 Restart Manager，不装 handle.exe）。");
            sb.AppendLine("     右键**文件夹**时会往下扫 " + FileLock.MaxScanDepth.ToString(CultureInfo.InvariantCulture)
                + " 层、最多 " + FileLock.MaxScanFiles.ToString(CultureInfo.InvariantCulture)
                + " 个文件（占用的多半是子文件夹里");
            sb.AppendLine("     那个 Office / PDF / 播放器），并且会告出到底是哪一个文件被占着。");
            sb.AppendLine("     除了「谁占着」，还会列出另外两种情况：「它自己在运行」（正在运行的程序不持有文件句柄，");
            sb.AppendLine("     句柄类接口查不到它，可它让文件删不掉、文件夹松不开）和「窗口里开着它」");
            sb.AppendLine("     （记事本这类程序读完就关句柄，本来就没锁）。");
            sb.AppendLine("     点「结束选中的进程」会连它启动的子进程一起结束（安装包 / 启动器都是父进程拉个");
            sb.AppendLine("     子进程干活，只结束父进程的话文件锁解开了、窗口还留着）。");
            sb.AppendLine("     还有个「强制解锁（不关程序）」按钮：遍历全系统句柄表（用户态能做的那条路），");
            sb.AppendLine("     把对方手里那个句柄直接关掉，**进程不动**（和火绒的「解锁占用」是一个思路）。");
            sb.AppendLine("     风险写在确认框里：句柄被突然关掉，那个程序可能报错 / 存不上盘；");
            sb.AppendLine("     系统进程和内核驱动的句柄关不掉（谁做的都一样，得靠内核驱动）。");
            sb.AppendLine("  4. " + AutoTitle + " —— 不弹窗口的一键版：右键一下，它在后台查谁占着它，");
            sb.AppendLine("     查到就直接把那些程序结束掉，然后鼠标旁边一张小卡片说结果（5 秒自动关闭）。");
            sb.AppendLine("     适合「我只想让这个文件能删掉、别问我」的场合。代价是没有确认框：");
            sb.AppendLine("     那些程序里没保存的东西会丢。系统关键进程、explorer.exe、工具箱自己，它一律不动。");
            sb.AppendLine("     拿不准的时候用上面那个「" + UnlockTitle + "」（它会先列出来让你勾）。");
            sb.AppendLine("  5. " + CommonTitle + " —— 右键里多一个子菜单，里面是你工具箱「常用」页的东西：");
            sb.AppendLine("     置顶的按钮 + 最近用过的按钮（最多 " + UserTools.RecentLimit.ToString(CultureInfo.InvariantCulture)
                + " 个）+ 打开工具箱 / 运行日志 / 设置。");
            sb.AppendLine("  6. " + TerminalTitle + "（**子菜单**两行：「" + TerminalCmdName + "」「" + TerminalPsName + "」）");
            sb.AppendLine("     —— 右键一个文件夹（或文件夹里的空白处、桌面空白处），终端直接在");
            sb.AppendLine("     那个目录里打开，省掉「开终端再 cd 半天」。两行各钉死一个终端：");
            sb.AppendLine("     要 cmd 就点 cmd，要 PowerShell 就点 PowerShell（不再替你挑）。");
            sb.AppendLine("     **不申请管理员权限** —— 就是一个普通用户的终端窗口。");
            sb.AppendLine();
            sb.AppendLine("菜单里的先后顺序是怎么定的");
            sb.AppendLine();
            sb.AppendLine("  资源管理器把同一个右键位置里的项**按注册表键名的字母序**排，不看我们想让它排第几");
            sb.AppendLine("  （微软的文档只给了一个 `Position=Top|Bottom`，只能把一项顶到最上或最下）。");
            sb.AppendLine("  所以六项的键名前面都带一个序号，序号就是菜单里的位置：");
            foreach (string hmItem in AllItems)
            {
                sb.Append("    ").Append(VerbOf(hmItem)).Append("  →  ").AppendLine(TitleOf(hmItem));
            }
            sb.AppendLine("  **改顺序 = 改这几个名字里的数字**（老用户那边的旧键名由启动时的自动修补换掉）。");
            sb.AppendLine();
            sb.AppendLine("装在哪些位置");
            sb.AppendLine();
            sb.AppendLine("  任意文件 / 文件夹 / 文件夹里的空白处（= 当前这个文件夹）/ 桌面空白处。");
            sb.AppendLine("  其中「" + CopyNameTitle + "」「" + CopyPathTitle + "」只装在**有选中东西**的两个位置");
            sb.AppendLine("  （任意文件 / 文件夹）；");
            sb.AppendLine("  「" + TerminalTitle + "」只装在**能代表一个目录**的三个位置（文件夹 / 文件夹里的空白处 / 桌面空白处）。");
            sb.AppendLine();
            sb.AppendLine("怎么卸干净");
            sb.AppendLine();
            sb.AppendLine("  点「撤掉…」那一排按钮即可（解除占用 / 一键解除占用 / 常用功能 / 复制文件名 / 复制文件路径 /");
            sb.AppendLine("  在此处打开终端 —— 六项各一对按钮，撤哪一项就只删哪一项的键）");
            sb.AppendLine("  —— 只删工具箱自己写的 Mxx1* 键（子菜单那一项连它指的那棵子项树一起删；");
            sb.AppendLine("  要是装过同一晚早一点的「四个独立项」那一稿，也会顺手清掉），");
            sb.AppendLine("  别的键（包括隔壁「永久删除」那套）一个都不碰。装之前会记现场，写在");
            sb.AppendLine("  " + BackupFile);
            sb.AppendLine("  「右键菜单状态」会念给你听：装了几个位置、子菜单现在几项、有没有残留。");
            sb.AppendLine();
            sb.AppendLine("右键里看不到？按顺序查");
            sb.AppendLine();
            sb.AppendLine("  1. 是不是没点「装上…」——「右键菜单状态」里写着「没装」就是没装；");
            sb.AppendLine("  2. Win11 的菜单被折叠了：先点「显示更多选项」，或者用「常用设置」里的");
            sb.AppendLine("     「Win10 右键菜单」把它切回经典样式；");
            sb.AppendLine("  3. 装了右键菜单管理器（比如这台机器上的 Context Menu Manager Plus）：菜单里显示什么");
            sb.AppendLine("     由它说了算 —— 它有个「锁定新右键菜单项」开关，开着的时候新装的项会进它的");
            sb.AppendLine("     「待审核」列表，得去它那里放行一次才会出现在右键里；也可能被它标成");
            sb.AppendLine("     「仅 Shift 显示」或直接隐藏。「右键菜单状态」会念给你听它现在是什么态度。");
            sb.AppendLine("  4. 资源管理器缓存：改完注册表一般立刻生效，实在不出现就注销一次（不用重启电脑）；");
            sb.AppendLine("  5. 菜单里的图标是从工具箱自带的按钮图标现生成的 .ico（放在");
            sb.AppendLine("     " + MenuIcons.Dir + "）—— 注册表的 Icon 只能指");
            sb.AppendLine("     exe/dll 或 .ico，指 .png 没用；图标没了就点一次「装上…」（或「重建常用功能」）重生成。");
            sb.AppendLine();
            sb.AppendLine("底线（代码里写死的）");
            sb.AppendLine();
            sb.AppendLine("  · 只写 Mxx1* 这几个自己写的键；同名键不是工具箱写的就不覆盖、不删除；");
            sb.AppendLine("  · 结束进程只结束你勾选的（连带它们启动的子进程）；explorer.exe 默认不勾（结束它 = 桌面重启一次）；");
            sb.AppendLine("  · 「" + AutoTitle + "」不问就动手，所以它自己那条线更窄：系统关键进程 / explorer.exe /");
            sb.AppendLine("    工具箱自己一律不动，没同意过使用条款也一律不动（只写日志，不碰任何进程）；");
            sb.AppendLine("  · 系统关键进程（System / csrss / winlogon / lsass …）列出来但禁止勾选；");
            sb.AppendLine("  · 不做句柄级强杀（那种内核动作有蓝屏风险，不做）；");
            sb.AppendLine("  · 查不到占用它的程序就如实说查不到，不谎报「已解除」。");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 右键菜单管理器（Context Menu Manager Plus）

        /// <summary>这台机器上的右键菜单管理器（如果装了）的数据目录。路径由系统文件夹拼出来，
        /// 代码里不写本机绝对路径。</summary>
        private static string CcmpDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ContextMenuMgr");
            }
        }

        /// <summary>装了右键菜单管理器的话，这里说清楚"新装的项要不要去它那里放行"。
        ///
        /// 为什么要写这一段：用户 2026-10-04 专门提醒「我电脑装了 Context Menu Manager Plus，
        /// 你给的叫我审核」。它是带服务 + shell 扩展的第三方菜单管理器，菜单里显示什么由它说了算：
        /// ① 它有个「锁定新右键菜单项」开关（backend-protection-settings.json 的
        ///    lockNewContextMenuItems）—— 开着的时候新装进去的项会进它的**待审核列表**，
        ///    不去它那里放行就不会出现在右键里；这台机器上现在是关的。
        /// ② 它自己的状态文件（context-menu-state.json）会记下每一项是启用 / 隐藏 / 待审核，
        ///    所以我们可以直接念给它听，而不是让用户对着"装了但右键里没有"发懵。</summary>
        private static string CcmpNote()
        {
            string version = CcmpVersion();
            bool dirHere = false;
            try { dirHere = Directory.Exists(CcmpDir); } catch { }
            if (version.Length == 0 && !dirHere) { return ""; }

            StringBuilder sb = new StringBuilder();
            sb.Append("  ").Append(RegEngine.PadCjk("右键菜单管理器", 16));
            sb.Append("Context Menu Manager Plus");
            if (version.Length > 0) { sb.Append(" ").Append(version); }
            sb.AppendLine();

            bool hasSettings;
            bool locked = ReadCcmpLock(out hasSettings);
            if (!hasSettings)
            {
                sb.Append("      读不到它的设置文件（").Append(CcmpDir)
                  .Append("）—— 装完之后如果右键里看不到这两项，去它那里确认一下没被隐藏").AppendLine();
            }
            else if (locked)
            {
                sb.AppendLine("      它的「锁定新右键菜单项」开着：刚装进去的项会进它的待审核列表，");
                sb.AppendLine("      要在它那里放行一次才会出现在右键里（这是它的保护功能，不是工具箱装失败了）。");
            }
            else
            {
                sb.AppendLine("      它的「锁定新右键菜单项」是关的，新装的项直接生效，不用去它那里点。");
            }

            List<string> pending = CcmpFlagged("isPendingApproval");
            if (pending.Count > 0)
            {
                sb.Append("      它现在把").Append(pending.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 项标成「待审核」：").Append(string.Join("、", pending.ToArray()))
                  .AppendLine();
            }
            List<string> hidden = CcmpFlagged("isHiddenByManager");
            if (hidden.Count > 0)
            {
                sb.Append("      它把这").Append(hidden.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(" 项藏起来了：").Append(string.Join("、", hidden.ToArray())).AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>它在自己的状态文件里把我们这两个项标成什么了（待审核 / 隐藏）。读不到就返回空。</summary>
        private static List<string> CcmpFlagged(string flag)
        {
            List<string> list = new List<string>();
            try
            {
                string file = Path.Combine(CcmpDir, "context-menu-state.json");
                if (!File.Exists(file)) { return list; }
                Dictionary<string, object> root = Json.AsObject(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                if (root == null) { return list; }
                foreach (RightMenuLocation loc in Table)
                {
                    foreach (string verb in AllVerbs())
                    {
                        string id = loc.Key + "|" + verb;
                        object raw;
                        if (!root.TryGetValue(id, out raw)) { continue; }
                        Dictionary<string, object> item = Json.AsObject(raw);
                        if (item == null) { continue; }
                        if (!Json.GetBool(item, flag, false)) { continue; }
                        list.Add(TitleOfVerb(verb) + "（" + loc.Label + "）");
                    }
                }
            }
            catch { }
            return list;
        }

        private static bool ReadCcmpLock(out bool exists)
        {
            exists = false;
            try
            {
                string file = Path.Combine(CcmpDir, "backend-protection-settings.json");
                if (!File.Exists(file)) { return false; }
                exists = true;
                Dictionary<string, object> o = Json.AsObject(Json.Parse(File.ReadAllText(file, Encoding.UTF8)));
                return Json.GetBool(o, "lockNewContextMenuItems", false);
            }
            catch { return false; }
        }

        /// <summary>从卸载登记里读它的版本号（读不到就空着，不影响别的）。</summary>
        private static string CcmpVersion()
        {
            string[] roots = new string[]
            {
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
                "SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall",
            };
            foreach (string root in roots)
            {
                foreach (RegistryKey hive in new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(root, false))
                        {
                            if (k == null) { continue; }
                            string[] subs = k.GetSubKeyNames();
                            foreach (string sub in subs)
                            {
                                using (RegistryKey item = k.OpenSubKey(sub, false))
                                {
                                    if (item == null) { continue; }
                                    object name = item.GetValue("DisplayName", null);
                                    if (name == null) { continue; }
                                    if (name.ToString().IndexOf("Context Menu Manager", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                                    object v = item.GetValue("DisplayVersion", null);
                                    return (v == null) ? "" : v.ToString();
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            return "";
        }

        // ------------------------------------------------------------------ 记录

        public static List<RightMenuRecord> LoadRecords()
        {
            List<RightMenuRecord> list = new List<RightMenuRecord>();
            try
            {
                if (!File.Exists(BackupFile)) { return list; }
                foreach (string line in File.ReadAllLines(BackupFile, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("#")) { continue; }
                    string[] f = line.Split('\t');
                    if (f.Length < 4) { continue; }
                    RightMenuRecord r = new RightMenuRecord();
                    r.When = f[0];
                    r.Item = f[1];
                    r.Key = f[2];
                    r.Existed = string.Equals(f[3], "yes", StringComparison.OrdinalIgnoreCase);
                    if (r.Item.Length > 0 && r.Key.Length > 0) { list.Add(r); }
                }
            }
            catch { }
            return list;
        }

        /// <summary>只记第一次（重复装不会把已经装过的状态当成"原来不存在"）。</summary>
        private static void Remember(string item, string fullKey, bool existed)
        {
            try
            {
                List<RightMenuRecord> list = LoadRecords();
                foreach (RightMenuRecord r in list)
                {
                    if (string.Equals(r.Key, fullKey, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Item, item, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                RightMenuRecord n = new RightMenuRecord();
                n.When = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                n.Item = item;
                n.Key = fullKey;
                n.Existed = existed;
                list.Add(n);
                SaveRecords(list);
            }
            catch { }
        }

        private static string SaveRecords(List<RightMenuRecord> list)
        {
            try
            {
                AppPaths.EnsureBase();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 萌新工具箱 · 「右键增强」装进右键菜单的键（「撤掉…」按这份记录删；别手改）");
                sb.AppendLine("# 时间\t项目\t键\t装之前存在吗");
                foreach (RightMenuRecord r in list)
                {
                    sb.Append(r.When).Append('\t').Append(r.Item).Append('\t').Append(r.Key).Append('\t')
                      .Append(r.Existed ? "yes" : "no").AppendLine();
                }
                File.WriteAllText(BackupFile, sb.ToString(), new UTF8Encoding(false));
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static bool IsInstalled(string item)
        {
            foreach (RightMenuRecord r in LoadRecords())
            {
                if (string.Equals(r.Item, item, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            // 记录被删了也别谎报"没装"：按已知键名再问一遍注册表。
            // **新名字和上一版那个旧名字都要问** —— 只认新名字的话，老用户（键名还没换过来）
            // 会被当成"没装"进而不参与启动修补，顺序永远换不过来。
            foreach (string suffix in new string[] { VerbOf(item), LegacyVerbOf(item) })
            {
                if (suffix.Length == 0) { continue; }
                foreach (RightMenuLocation loc in Table)
                {
                    if (KeyExists(RootKey + "\\" + loc.Key + "\\" + suffix)) { return true; }
                }
            }
            return false;
        }

        /// <summary>这一项现在装在哪些位置（**新键名和上一版的旧键名都算** —— 老用户那边键名还没换过来
        /// 的时候，这里说"没装"就是谎报；换没换过来由状态里"上一版的键名"那一行单独说）。</summary>
        private static List<string> InstalledLocations(string item)
        {
            List<string> list = new List<string>();
            string legacy = LegacyVerbOf(item);
            foreach (RightMenuLocation loc in Table)
            {
                string cur = RootKey + "\\" + loc.Key + "\\" + VerbOf(item);
                string old = (legacy.Length == 0) ? "" : (RootKey + "\\" + loc.Key + "\\" + legacy);
                if (KeyExists(cur) || ((old.Length > 0) && KeyExists(old))) { list.Add(loc.Label); }
            }
            return list;
        }

        private static List<string> KnownRelativeKeys()
        {
            List<string> list = new List<string>();
            foreach (RightMenuLocation loc in Table)
            {
                foreach (string verb in AllVerbs())
                {
                    list.Add(loc.Key + "\\" + verb);
                }
            }
            list.Add(SharedKey);
            list.Add(SupersededCopyTreeKey);
            list.Add(TerminalTreeKey);
            list.Add(TerminalBgTreeKey);
            return list;
        }

        /// <summary>这一版六个 verb 名 + **上一版那六个旧键名**（晚七换了名，老用户那边可能还在）
        /// + 晚六第一稿那四个独立项 —— 图标核对 / 残留检测 / 菜单管理器那几处都遍历它。
        /// 旧名字也要认：不认就查不到、也撤不干净。</summary>
        private static string[] AllVerbs()
        {
            return new string[] { UnlockVerb, AutoVerb, CommonVerb, CopyNameVerb, CopyPathVerb, TerminalVerb,
                LegacyUnlockVerb, LegacyAutoVerb, LegacyCommonVerb, LegacyCopyNameVerb, LegacyCopyPathVerb, LegacyTerminalVerb,
                SupersededCopyRelVerb, SupersededCopyAbsVerb,
                SupersededTerminalCmdVerb, SupersededTerminalPsVerb };
        }

        /// <summary>verb 名 → 给人看的标题（菜单管理器那个状态文件里是按 verb 记的）。</summary>
        private static string TitleOfVerb(string verb)
        {
            if (verb == AutoVerb || verb == LegacyAutoVerb) { return AutoTitle; }
            if (verb == CommonVerb || verb == LegacyCommonVerb) { return CommonTitle; }
            if (verb == CopyNameVerb || verb == LegacyCopyNameVerb) { return CopyNameTitle; }
            if (verb == CopyPathVerb || verb == LegacyCopyPathVerb) { return CopyPathTitle; }
            if (verb == TerminalVerb || verb == LegacyTerminalVerb) { return TerminalTitle; }
            if (verb == SupersededCopyRelVerb) { return SupersededTitles[0]; }
            if (verb == SupersededCopyAbsVerb) { return SupersededTitles[1]; }
            if (verb == SupersededTerminalCmdVerb) { return SupersededTitles[2]; }
            if (verb == SupersededTerminalPsVerb) { return SupersededTitles[3]; }
            return UnlockTitle;
        }

        // ------------------------------------------------------------------ 注册表小工具

        private static bool KeyExists(string fullKey)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(fullKey, false))
                {
                    return k != null;
                }
            }
            catch { return false; }
        }

        /// <summary>这个键是我们的吗：键名是我们那几棵子项树之一，或者 MUIVerb 写着我们那几句话之一
        /// （包含晚六第一稿那四句「复制相对路径」… —— 清那一稿的残留时要认得出）。</summary>
        private static bool IsOurs(string fullKey, string relativeKey)
        {
            if (relativeKey.StartsWith(SharedKeyPrefix, StringComparison.OrdinalIgnoreCase)) { return true; }
            string text = ReadText(fullKey, "MUIVerb");
            foreach (string item in AllItems)
            {
                if (string.Equals(text, TitleOf(item), StringComparison.Ordinal)) { return true; }
            }
            return IsSupersededTitle(text);
        }

        /// <summary>不是我们的就返回一句人话（用来跳过，绝不覆盖别人写的东西）。</summary>
        private static string ForeignReason(string fullKey, string relativeKey, string title)
        {
            if (!KeyExists(fullKey)) { return ""; }
            if (IsOurs(fullKey, relativeKey)) { return ""; }
            return fullKey + " 已经存在而且不是工具箱写的（MUIVerb=" + ReadText(fullKey, "MUIVerb") + "），没动它";
        }

        private static string ReadText(string fullKey, string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(fullKey, false))
                {
                    if (k == null) { return ""; }
                    object o = k.GetValue(name, null);
                    return (o == null) ? "" : o.ToString();
                }
            }
            catch { return ""; }
        }

        private static bool WriteValue(string fullKey, string name, string value, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(fullKey))
                {
                    if (k == null) { error = "建不了键：" + fullKey; return false; }
                    k.SetValue(name, value, RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = fullKey + " 写不进去：" + ex.Message;
                return false;
            }
        }

        private static void WriteDword(string fullKey, string name, int value)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(fullKey))
                {
                    if (k != null) { k.SetValue(name, value, RegistryValueKind.DWord); }
                }
            }
            catch { }
        }

        /// <summary>给菜单项写图标。注册表的 Icon **只能指向带图标资源的 exe/dll 或 .ico 文件**，
        /// 所以这里先用 MenuIcons 把内嵌的按钮 PNG 转成 .ico，指不到就不写这个值（别留一个空白图标位）。
        /// 写入成功返回 true。</summary>
        private static bool WriteIconValue(string fullKey, string iconId)
        {
            return WriteIconValue(fullKey, iconId, "");
        }

        private static bool WriteIconValue(string fullKey, string iconId, string iconPath)
        {
            string ico = MenuIcons.IcoFor(iconId, iconPath);
            if (ico.Length == 0)
            {
                DeleteValue(fullKey, "Icon");
                return false;
            }
            string error;
            if (!WriteValue(fullKey, "Icon", ico, out error)) { return false; }
            string back;
            return VerifyValue(fullKey, "Icon", ico, out back);
        }

        private static void DeleteValue(string fullKey, string name)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(fullKey, true))
                {
                    if (k != null) { k.DeleteValue(name, false); }
                }
            }
            catch { }
        }

        private static bool VerifyValue(string fullKey, string name, string expect, out string actual)
        {
            actual = ReadText(fullKey, name);
            return string.Equals(actual, expect, StringComparison.Ordinal);
        }

        private static string DeleteTree(string fullKey)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(fullKey, false);
                if (KeyExists(fullKey)) { return "删了之后还在：" + fullKey; }
                return "";
            }
            catch (Exception ex) { return "删不掉：" + ex.Message; }
        }

        private static string QuoteExe()
        {
            return "\"" + AppPaths.ExePath + "\"";
        }
    }
}
