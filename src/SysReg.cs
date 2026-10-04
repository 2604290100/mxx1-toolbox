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

        /// <summary>悬停说明里附的那一行（和隐私开关一样，写成一句人话，不甩 id 出去）。</summary>
        public static string Describe(string id, bool on)
        {
            Tweak t = FindTweak(id);
            if (t == null) { return "系统设置里没有这个开关：" + id; }
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

        /// <summary>把一个系统设置开关设成 on（见 Tweak.OnLabel）或 off。</summary>
        public static string Set(string id, bool on, out bool ok)
        {
            Tweak t = FindTweak(id);
            if (t == null) { ok = false; return "不认识这个系统设置开关：" + id; }
            string mismatch = BuildMismatch(t);
            if (mismatch.Length > 0)
            {
                ok = true;   // 不是失败：这台机器上本来就没有这回事
                return "【" + t.Item.Name + "】" + Wording(id, on) + Environment.NewLine
                    + Environment.NewLine + mismatch;
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
                sb.Append("  反悔就点「还原系统设置改动」（按改动前的原值写回去）。");
            }
            else
            {
                sb.Append("  有地方没写成功 —— 上面带 × 的就是（多半是缺管理员权限）。");
            }
            ok = report.Ok;
            return sb.ToString();
        }

        /// <summary>只读：每个开关现在是什么状态 + 记了哪些原值。</summary>
        public static string Status()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("系统设置当前状态（只读，没改任何东西）");
            sb.AppendLine();
            foreach (Tweak t in Table)
            {
                sb.Append("  ").Append(RegEngine.PadCjk(t.Item.Name, 24)).Append(Engine.StateText(t.Item));
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
