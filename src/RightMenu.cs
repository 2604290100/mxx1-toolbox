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
        public const string UnlockVerb = "Mxx1Unlock";
        public const string CommonVerb = "Mxx1Common";
        public const string SharedKey = "Mxx1Toolbox.Common";
        public const string UnlockTitle = "解除文件占用";
        public const string CommonTitle = "常用功能";
        public const string ItemUnlock = "unlock";
        public const string ItemCommon = "common";
        public const string BackupName = "rightmenu-installed.tsv";

        /// <summary>命令里给"右键选中的那个路径"的占位：Explorer 会把它换成真实路径。</summary>
        public const string SelectedPlaceholder = "%1";

        /// <summary>背景位置（文件夹里的空白处 / 桌面空白处）要用 %V：那里 Explorer 不替换 %1。</summary>
        public const string BackgroundPlaceholder = "%V";

        /// <summary>两项菜单项各自的图标源（都是工具箱自己的按钮图标，装的时候转成 .ico）。</summary>
        public const string UnlockIconId = "rightmenu.unlock.on";
        public const string CommonIconId = "rightmenu.common.on";

        private static readonly RightMenuLocation[] Table = new RightMenuLocation[]
        {
            new RightMenuLocation("files",    "任意文件",           "*\\shell",                   SelectedPlaceholder),
            new RightMenuLocation("folder",   "文件夹",             "Directory\\shell",           SelectedPlaceholder),
            new RightMenuLocation("folderbg", "文件夹里的空白处",   "Directory\\Background\\shell", BackgroundPlaceholder),
            new RightMenuLocation("desktop",  "桌面空白处",         "DesktopBackground\\Shell",   BackgroundPlaceholder),
        };

        private static readonly string[] CriticalProcesses = new string[]
        {
            "system", "registry", "memory compression", "idle", "smss", "csrss", "wininit",
            "winlogon", "services", "lsass", "fontdrvhost", "dwm", "svchost", "audiodg",
        };

        public static RightMenuLocation[] Locations { get { return Table; } }

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

        /// <summary>装上（或修复）右键项。unlock / common 至少选一个。已装过 = 覆盖成当前版本
        /// （exe 路径可能搬过家），所以这个按钮同时也是"修复"。</summary>
        public static string Install(bool unlock, bool common, out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("把工具箱装进 Windows 右键菜单");
            if (IsTestRoot) { sb.AppendLine("（测试根：" + RootLabel + " —— 没有碰真实的右键菜单）"); }
            sb.AppendLine();

            int done = 0;
            int skipped = 0;
            foreach (RightMenuLocation loc in Table)
            {
                if (unlock)
                {
                    bool wrote;
                    string error = InstallUnlockVerb(loc, out wrote);
                    if (wrote) { done++; sb.Append("  √ ").Append(UnlockTitle).Append(" · ").Append(loc.Label).AppendLine(); }
                    else if (error.Length > 0) { skipped++; sb.Append("  × ").Append(loc.Label).Append("  ").Append(error).AppendLine(); }
                }
                if (common)
                {
                    bool wrote;
                    string error = InstallCommonVerb(loc, out wrote);
                    if (wrote) { done++; sb.Append("  √ ").Append(CommonTitle).Append(" · ").Append(loc.Label).AppendLine(); }
                    else if (error.Length > 0) { skipped++; sb.Append("  × ").Append(loc.Label).Append("  ").Append(error).AppendLine(); }
                }
            }

            if (common)
            {
                string error;
                string report = WriteSharedTree(out error);
                sb.AppendLine();
                if (error.Length > 0)
                {
                    skipped++;
                    sb.Append("  × ").Append(CommonTitle).Append(" 的子项没写成：").Append(error).AppendLine();
                }
                else
                {
                    sb.Append(report).AppendLine();
                }
            }

            sb.AppendLine();
            if (skipped == 0)
            {
                sb.Append("  ").Append(done.ToString(CultureInfo.InvariantCulture))
                  .Append(" 处都写进去并读回核对过了。").AppendLine();
                sb.Append("  菜单图标：从工具箱自带的按钮图标生成 .ico 写在 ").Append(MenuIcons.Dir)
                  .AppendLine("，");
                sb.AppendLine("  右键里那两项（以及「常用功能」子菜单的每一项）都会显示图标 —— 注册表的 Icon");
                sb.AppendLine("  只能指向 exe/dll 或 .ico，指 .png 是没用的，所以这里要先转一道。");
                string names = unlock && common
                    ? ("「" + UnlockTitle + "」和「" + CommonTitle + "」")
                    : (unlock ? ("「" + UnlockTitle + "」") : ("「" + CommonTitle + "」"));
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
                + (unlock ? "解除占用" : "") + (unlock && common ? " + " : "") + (common ? "常用功能" : "") + "）");
            return sb.ToString();
        }

        /// <summary>撤掉：按记录删，只删工具箱自己写的键。</summary>
        public static string Uninstall(bool unlock, bool common, out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("把工具箱从 Windows 右键菜单里撤掉（只删工具箱自己写的键）");
            if (IsTestRoot) { sb.AppendLine("（测试根：" + RootLabel + " —— 没有碰真实的右键菜单）"); }
            sb.AppendLine();

            List<RightMenuRecord> records = LoadRecords();
            int removed = 0;
            int kept = 0;

            foreach (RightMenuLocation loc in Table)
            {
                if (unlock) { RemoveVerb(sb, records, ItemUnlock, loc.Key + "\\" + UnlockVerb, loc.Label + " · " + UnlockTitle, ref removed, ref kept); }
                if (common) { RemoveVerb(sb, records, ItemCommon, loc.Key + "\\" + CommonVerb, loc.Label + " · " + CommonTitle, ref removed, ref kept); }
            }
            if (common)
            {
                string full = RootKey + "\\" + SharedKey;
                if (KeyExists(full))
                {
                    string error = DeleteTree(full);
                    if (error.Length == 0) { removed++; sb.Append("  √ ").Append(CommonTitle).Append(" 的子项（").Append(full).Append("）").AppendLine(); }
                    else { kept++; sb.Append("  × ").Append(full).Append("  ").Append(error).AppendLine(); }
                }
            }

            List<RightMenuRecord> keep = new List<RightMenuRecord>();
            foreach (RightMenuRecord r in records)
            {
                bool drop = (r.Item == ItemUnlock && unlock) || (r.Item == ItemCommon && common);
                if (!drop) { keep.Add(r); }
            }
            string saveError = SaveRecords(keep);

            sb.AppendLine();
            if (kept == 0)
            {
                sb.Append("  删掉 ").Append(removed.ToString(CultureInfo.InvariantCulture)).Append(" 处。").AppendLine();
                sb.Append("  右键菜单里那两项下一次弹菜单就没有了（不用重启电脑；菜单不刷新见「右键增强说明」）。");
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
            // 两项都撤掉了：生成出来的 .ico 也没用了，删掉（留着一个空目录也只是碍眼）。
            if (!IsInstalled(ItemUnlock) && !IsInstalled(ItemCommon))
            {
                MenuIcons.RemoveAll();
                sb.AppendLine();
                sb.Append("  顺带清掉了菜单图标的临时文件（").Append(MenuIcons.Dir).Append("）");
            }
            ok = (kept == 0);
            Logger.Write("右键增强", (ok ? "完成 · " : "部分失败 · ") + "撤右键菜单（"
                + ((unlock ? "解除占用" : "") + (unlock && common ? " + " : "") + (common ? "常用功能" : "")) + "）");
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
            string report = WriteSharedTree(out error);
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
        /// exe，菜单里是空白）。只重写 Mxx1* 这几个自己写的键，别人的键一个都不碰。</summary>
        public static void SyncIfInstalled()
        {
            try
            {
                bool unlock = IsInstalled(ItemUnlock);
                bool common = IsInstalled(ItemCommon);
                if (!unlock && !common) { return; }
                if (SyncDisabled) { return; }

                if (unlock)
                {
                    foreach (RightMenuLocation loc in Table)
                    {
                        bool wrote;
                        InstallUnlockVerb(loc, out wrote);   // 内部有 ForeignReason 把关：别人的键不动
                    }
                }
                if (common)
                {
                    foreach (RightMenuLocation loc in Table)
                    {
                        bool wrote;
                        InstallCommonVerb(loc, out wrote);
                    }
                    string error;
                    WriteSharedTree(out error);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ 写键

        private static string InstallUnlockVerb(RightMenuLocation loc, out bool wrote)
        {
            wrote = false;
            string relative = loc.Key + "\\" + UnlockVerb;
            string full = RootKey + "\\" + relative;
            string foreign = ForeignReason(full, relative, UnlockTitle);
            if (foreign.Length > 0) { return foreign; }
            bool existed = KeyExists(full);

            string error;
            if (!WriteValue(full, "", "", out error)) { return error; }
            if (!WriteValue(full, "MUIVerb", UnlockTitle, out error)) { return error; }
            WriteIconValue(full, UnlockIconId);
            if (!WriteValue(full, "MultiSelectModel", "Player", out error)) { return error; }
            string command = QuoteExe() + " rightmenu unlock \"" + loc.Placeholder + "\"";
            if (!WriteValue(full + "\\command", "", command, out error)) { return error; }

            string back;
            if (!VerifyValue(full, "MUIVerb", UnlockTitle, out back))
            {
                return "写完读回来不是" + UnlockTitle + "（读到：" + back + "）";
            }
            string backCmd;
            if (!VerifyValue(full + "\\command", "", command, out backCmd))
            {
                return "命令写完读回来不对（读到：" + backCmd + "）";
            }
            Remember(ItemUnlock, full, existed);
            wrote = true;
            return "";
        }

        private static string InstallCommonVerb(RightMenuLocation loc, out bool wrote)
        {
            wrote = false;
            string relative = loc.Key + "\\" + CommonVerb;
            string full = RootKey + "\\" + relative;
            string foreign = ForeignReason(full, relative, CommonTitle);
            if (foreign.Length > 0) { return foreign; }
            bool existed = KeyExists(full);

            string error;
            if (!WriteValue(full, "", "", out error)) { return error; }
            if (!WriteValue(full, "MUIVerb", CommonTitle, out error)) { return error; }
            WriteIconValue(full, CommonIconId);
            if (!WriteValue(full, "MultiSelectModel", "Player", out error)) { return error; }
            // 子项只写一份（HKCU\Software\Classes\Mxx1Toolbox.Common\shell\NN），四个位置都指过去：
            // 重建菜单只要写一个地方。
            if (!WriteValue(full, "ExtendedSubCommandsKey", SharedKey, out error)) { return error; }

            string back;
            if (!VerifyValue(full, "MUIVerb", CommonTitle, out back))
            {
                return "写完读回来不是" + CommonTitle + "（读到：" + back + "）";
            }
            Remember(ItemCommon, full, existed);
            wrote = true;
            return "";
        }

        /// <summary>把共用的子项键整棵重写：先删再建，保证没有上一次留下的孤儿项。</summary>
        private static string WriteSharedTree(out string error)
        {
            error = "";
            string full = RootKey + "\\" + SharedKey;
            if (KeyExists(full))
            {
                error = DeleteTree(full);
                if (error.Length > 0) { return ""; }
            }
            int pinned;
            int recent;
            List<RightMenuEntry> items = BuildEntries(out pinned, out recent);
            int n = 0;
            int icons = 0;
            foreach (RightMenuEntry e in items)
            {
                string key = full + "\\shell\\" + n.ToString("00", CultureInfo.InvariantCulture);
                if (!WriteValue(key, "MUIVerb", e.Name, out error)) { return ""; }
                if (WriteIconValue(key, e.IconId)) { icons++; }
                if (e.SeparatorBefore) { WriteDword(key, "CommandFlags", 0x20); }
                if (!WriteValue(key + "\\command", "", e.Command, out error)) { return ""; }
                n++;
            }
            if (n == 0)
            {
                error = "没算出任何子项（置顶和最近使用都是空的？）";
                return "";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("  √ ").Append(CommonTitle).Append(" 子菜单写了 ").Append(n.ToString(CultureInfo.InvariantCulture))
              .Append(" 项（置顶 ").Append(pinned.ToString(CultureInfo.InvariantCulture))
              .Append(" + 最近用过 ").Append(recent.ToString(CultureInfo.InvariantCulture))
              .Append(" + 固定 3 项，带图标的 ").Append(icons.ToString(CultureInfo.InvariantCulture)).Append(" 项）");
            return sb.ToString();
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
            sb.AppendLine();

            foreach (string item in new string[] { ItemUnlock, ItemCommon })
            {
                string title = (item == ItemUnlock) ? UnlockTitle : CommonTitle;
                sb.Append("  ").Append(RegEngine.PadCjk(title, 16));
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
                else
                {
                    sb.AppendLine();
                }
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
                foreach (string verb in new string[] { UnlockVerb, CommonVerb })
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
            sb.AppendLine("  工具箱能往你的右键菜单里装两样东西，两样都只写当前用户（HKCU\\Software\\Classes），");
            sb.AppendLine("  不要管理员权限、不装 shell 扩展 DLL、不起服务、不加开机启动：");
            sb.AppendLine();
            sb.AppendLine("  1. " + UnlockTitle + " —— 右键一个文件 / 文件夹，看到是谁占着它，勾一下就能把");
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
            sb.AppendLine("  2. " + CommonTitle + " —— 右键里多一个子菜单，里面是你工具箱「常用」页的东西：");
            sb.AppendLine("     置顶的按钮 + 最近用过的按钮（最多 " + UserTools.RecentLimit.ToString(CultureInfo.InvariantCulture)
                + " 个）+ 打开工具箱 / 运行日志 / 设置。");
            sb.AppendLine();
            sb.AppendLine("装在哪些位置");
            sb.AppendLine();
            sb.AppendLine("  任意文件 / 文件夹 / 文件夹里的空白处（= 当前这个文件夹）/ 桌面空白处。");
            sb.AppendLine();
            sb.AppendLine("怎么卸干净");
            sb.AppendLine();
            sb.AppendLine("  点「撤掉解除占用」「撤掉常用功能」即可 —— 只删工具箱自己写的 Mxx1* 键，");
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
                    foreach (string verb in new string[] { UnlockVerb, CommonVerb })
                    {
                        string id = loc.Key + "|" + verb;
                        object raw;
                        if (!root.TryGetValue(id, out raw)) { continue; }
                        Dictionary<string, object> item = Json.AsObject(raw);
                        if (item == null) { continue; }
                        if (!Json.GetBool(item, flag, false)) { continue; }
                        list.Add(((verb == UnlockVerb) ? UnlockTitle : CommonTitle) + "（" + loc.Label + "）");
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
            string suffix = (item == ItemUnlock) ? UnlockVerb : CommonVerb;
            foreach (RightMenuLocation loc in Table)
            {
                if (KeyExists(RootKey + "\\" + loc.Key + "\\" + suffix)) { return true; }
            }
            return false;
        }

        private static List<string> InstalledLocations(string item)
        {
            List<string> list = new List<string>();
            string suffix = (item == ItemUnlock) ? UnlockVerb : CommonVerb;
            foreach (RightMenuLocation loc in Table)
            {
                if (KeyExists(RootKey + "\\" + loc.Key + "\\" + suffix)) { list.Add(loc.Label); }
            }
            return list;
        }

        private static List<string> KnownRelativeKeys()
        {
            List<string> list = new List<string>();
            foreach (RightMenuLocation loc in Table)
            {
                list.Add(loc.Key + "\\" + UnlockVerb);
                list.Add(loc.Key + "\\" + CommonVerb);
            }
            list.Add(SharedKey);
            return list;
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

        /// <summary>这个键是我们的吗：键名就叫 SharedKey，或者 MUIVerb 写着我们那两句话之一。</summary>
        private static bool IsOurs(string fullKey, string relativeKey)
        {
            if (string.Equals(relativeKey, SharedKey, StringComparison.OrdinalIgnoreCase)) { return true; }
            string text = ReadText(fullKey, "MUIVerb");
            return string.Equals(text, UnlockTitle, StringComparison.Ordinal)
                || string.Equals(text, CommonTitle, StringComparison.Ordinal);
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
            string ico = MenuIcons.IcoFor(iconId);
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
