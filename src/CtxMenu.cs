// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Mxx1Toolbox
{
    /// <summary>「右键菜单管理」里的一页 = 一个右键位置（用户能理解的粒度，
    /// **不是**注册表键的粒度：一页背后可能是两三个注册表位置合并出来的）。</summary>
    internal sealed class CtxZone
    {
        public string Id = "";
        public string Label = "";
        /// <summary>这一页是什么（窗口顶部那句人话）。</summary>
        public string Note = "";
        /// <summary>这一页背后是哪些**类键**（相对 Classes 根）。两组东西分别挂在它下面：
        /// 静态菜单项在 `&lt;类键&gt;\shell`，DLL 扩展在 `&lt;类键&gt;\shellex\ContextMenuHandlers`。
        /// ⚠️ `shellex` 是 `shell` 的**兄弟**、不是子键 —— 一开始按 `\shell\shellex\...` 拼，
        /// 那个键根本不存在，于是永远列不出扩展项；别改回去。
        /// ⚠️「文件夹」那一页是**三个类键合并**（Directory / Folder / AllFilesystemObjects）——
        /// 只读 Directory 会漏掉一批真实存在的项，用户会以为工具坏了（"怎么我右键里有的它不显示"）。</summary>
        public string[] Classes = new string[0];

        public CtxZone(string id, string label, string note, string[] classes)
        {
            Id = id; Label = label; Note = note; Classes = classes;
        }
    }

    /// <summary>列表里的一行：右键菜单里的一个项。</summary>
    internal sealed class CtxEntry
    {
        public string ZoneId = "";
        public string ZoneLabel = "";
        /// <summary>相对 Classes 根的键路径：静态项是 `*\shell`，
        /// 扩展项是 `*\shellex\ContextMenuHandlers`。</summary>
        public string Sub = "";
        /// <summary>菜单项的键名（verb 名）；扩展项时是登记名（通常是 CLSID）。</summary>
        public string Verb = "";
        /// <summary>菜单上显示的字。</summary>
        public string Title = "";
        /// <summary>`command` 子键里那行命令（扩展项为空）。</summary>
        public string Command = "";
        /// <summary>这一项在哪些根下面（用户区 / 系统区）。写的时候**两份都要写**：
        /// 只写用户区那份、系统区那份还在的话，菜单里那一项不会消失。</summary>
        public bool InUser = false;
        public bool InMachine = false;
        /// <summary>扩展项（`shellex\ContextMenuHandlers`，真正干活的是 DLL）。
        /// ⚠️ 它**不走「禁用 / 恢复 / 删除」那三个动作**（那两个是改登记键的）—— 它走
        /// **「停用扩展 / 恢复扩展」**：往系统黑名单里写/删一条（值名 = CLSID），
        /// 登记键一个字都不动，而且要**重启资源管理器**才生效。见 §12.66 与 §12.68。</summary>
        public bool Shellex = false;
        /// <summary>已经带着隐藏开关（我们写的，或者别人写的）。</summary>
        public bool Disabled = false;
        /// <summary>这一项是不是「Windows 保护的」（所有者是 TrustedInstaller、管理员只有读权限）——
        /// 判定办法就是**试着以可写方式打开那个键**（只读判断、一个字节都不写）。
        /// 2026-10-10 用户问「为什么管理员权限启动也无法禁止？」之后加的：这些项要「取得所有权」才动得了，
        /// 而界面必须**在点之前**就看得出来（以前是点了才知道没反应）。
        /// 实测那几个键：`HKLM\SOFTWARE\Classes\Drive\shell\cmd` 之类，所有者 `NT SERVICE\TrustedInstaller`、
        /// `BUILTIN\Administrators` 只有 `ReadKey`。</summary>
        public bool Protected = false;
        /// <summary>键里有 `Extended` = 只在按住 Shift 右键时才出现。</summary>
        public bool ShiftOnly = false;
        /// <summary>级联子菜单的父项（`SubCommands` / `ExtendedSubCommandsKey`）。</summary>
        public bool Submenu = false;
        /// <summary>系统自带那一档（键名以 `Windows.` 开头），删除时确认框里要多说一句。</summary>
        public bool Systemish = false;
        /// <summary>工具箱自己装的（键名以 `Mxx1Toolbox.` 开头）。</summary>
        public bool Ours = false;
        /// <summary>补充说明（扩展项的 DLL 路径 / 名字等）。</summary>
        public string Note = "";
        /// <summary>注册表 `Icon` 值（`&lt;某个 exe 的路径&gt;,0` 这种；扩展项时是那个 DLL 的路径）——
        /// 列表里每行显示"这一项自己的图标"就靠它，抽不出来再退回按段给的内嵌图标。</summary>
        public string IconValue = "";

        // ---- 扩展项（shellex）专有的四样（2026-10-11 加的：那之前扩展项既不看黑名单也不给动）----
        /// <summary>扩展项真正的 CLSID（原样，用来显示和「复制 CLSID」）。</summary>
        public string Clsid = "";
        /// <summary>规范化后的 CLSID（`{小写-guid}`）——**黑名单是按 GUID 认的**，
        /// 有的软件写 `{...}`、有的不带花括号、有的还多打一个字符（实测遇到过一个尾部多一个横杠的畸形键名）。
        /// 规范不出来就是空串：那种项**不参与黑名单匹配**（宁可不认，也不去猜）。</summary>
        public string ClsidNorm = "";
        /// <summary>扩展的 DLL 完整路径（`HKCR\CLSID\{…}\InprocServer32` 里那个；抽不出来就是空串）——
        /// 底栏「打开 DLL 目录」用它。</summary>
        public string DllPath = "";
        /// <summary>系统黑名单里有没有它（用户区 / 系统区各一份，用户区那份不用管理员就能写）。</summary>
        public bool BlockedUser = false;
        public bool BlockedMachine = false;
        /// <summary>黑名单里写的那个**人看的名字**（值数据；资源管理器不看它，别的工具写给用户看的）——
        /// 比如 `打开方式(H)` 这种。抽不到就是空串。</summary>
        public string BlockedName = "";
        /// <summary>黑名单里有它、可**机器上已经找不到对应的扩展**了（卸载残留）。
        /// 这种行不占位置的条数（它跟位置无关，只在「文件」那一页的最后一段列出来）。</summary>
        public bool Zombie = false;

        /// <summary>这一项在系统黑名单里吗（用户区或系统区任一处有就算）。</summary>
        public bool Blocked { get { return BlockedUser || BlockedMachine; } }

        /// <summary>用户区那份的完整键路径（要写先写它）。</summary>
        public string UserKey { get { return CtxMenu.UserRoot + "\\" + Sub + "\\" + Verb; } }
        public string MachineKey { get { return CtxMenu.MachineRoot + "\\" + Sub + "\\" + Verb; } }

        /// <summary>这一项在注册表里的完整键路径 —— **动作记录里写的就是它，撤销也照它打开**。
        /// ⚠️ 扩展项的"键"**不是**它那个登记键，而是**系统黑名单那个键 + 值名（CLSID）**：
        /// 停用/恢复改的只有黑名单，登记键一个字都没动（所以撤销时把"路径最后那一段"当 CLSID 用，
        /// 见 `Undo`）。「黑名单残留」那种连登记键都没有，同理。</summary>
        public string FullKey
        {
            get
            {
                if (Shellex)
                {
                    string root = Zombie
                        ? (BlockedUser ? CtxMenu.BlockedUserRoot : CtxMenu.BlockedMachineRoot)
                        : CtxMenu.BlockedUserRoot;
                    string id = Zombie ? Verb : (ClsidNorm.Length > 0 ? ClsidNorm : Verb);
                    return root + "\\" + id;
                }
                return (InMachine && !InUser) ? MachineKey : UserKey;
            }
        }

        /// <summary>列表「状态」那一列。
        /// ⚠️ 扩展项这一档 2026-10-11 起**分两种情况**了：系统黑名单里有它 = 已经停用（它现在其实
        /// 不在右键菜单里），没有才是"在用"。以前一律写「扩展项」，等于把已经停用的项当成在用的 ——
        /// 用户在这台机器上量到：列出来的 19 个 CLSID 里有 **6 个早就在黑名单里**了。</summary>
        public string StateLabel
        {
            get
            {
                if (Zombie) { return "黑名单残留"; }
                if (Shellex)
                {
                    if (BlockedUser && BlockedMachine) { return "已停用（两处都写了）"; }
                    if (BlockedUser) { return "已停用"; }
                    if (BlockedMachine) { return "已停用（系统区）"; }
                    return "扩展项";
                }
                return Disabled ? "已禁用" : "在用";
            }
        }

        /// <summary>列表「位置」那一列 —— 回答"改它要不要管理员"。</summary>
        public string WhereLabel
        {
            get
            {
                if (InMachine && InUser) { return "用户区 + 系统区"; }
                if (InMachine) { return "系统区（要管理员）"; }
                return "用户区";
            }
        }

        /// <summary>这一项能不能被这三个动作处理（扩展项不行 —— 它走「停用扩展」那条黑名单路）。</summary>
        public bool Actionable { get { return !Shellex; } }

        /// <summary>这一行**能不能勾**（批量动作按它算，界面上的勾选框也跟着它灰）。
        /// 静态项一直能勾；扩展项 2026-10-11 起也能勾了，但三种情形是灰的：
        /// ① 黑名单**系统区**那份有它 —— 那是别的工具/安装程序写的，本工具箱只写用户区（勾了也动不了）；
        /// ② CLSID 认不出来（不规范的那种）—— 没有一个能写进黑名单的 GUID；
        /// ③ 那个登记键读不动（`KeyLocked`，只看得到登记名）。
        /// 「黑名单残留」那一段相反：**用户区**的残留能勾（点「恢复」= 把它从黑名单里清掉），系统区的不能。</summary>
        public bool Checkable
        {
            get
            {
                if (Zombie) { return BlockedUser; }
                if (Shellex) { return (ClsidNorm.Length > 0) && (!BlockedMachine) && (!KeyLocked); }
                return true;
            }
        }

        /// <summary>那个登记键**读不动**（权限被保护）—— 只看得到登记名。
        /// ⚠️ 这和 `Protected` **不是一回事**：`Protected` 说的是"要取得所有权才写得动这个键"，
        /// 而扩展项根本不用写那个键（停用 = 写黑名单），所以登记键读不动 ≠ 不能停用它。</summary>
        public bool KeyLocked = false;

        /// <summary>改它要不要管理员（系统区里有一份、而当前不是管理员）。
        /// ⚠️ 扩展项恒为 false：黑名单只写**用户区**那一份（不用管理员、只影响当前用户、随时能删），
        /// 系统区那份一个字都不动 —— 不然会为一个根本不需要提权的动作弹 UAC。</summary>
        public bool NeedsAdmin { get { return !Shellex && InMachine && !CtxMenu.IsAdmin(); } }

        /// <summary>列表里的段（对着用户的眼睛分，不是对着注册表分）：
        /// 工具箱自己装的 / 程序装的 / 系统自带的 / DLL 扩展项 / 黑名单残留。</summary>
        public string GroupId
        {
            get
            {
                if (Zombie) { return CtxMenu.GroupZombie; }
                if (Shellex) { return CtxMenu.GroupShellex; }
                if (Ours) { return CtxMenu.GroupOurs; }
                return Systemish ? CtxMenu.GroupSystem : CtxMenu.GroupApps;
            }
        }
    }

    /// <summary>「动作记录」里的一条（界面底部那个面板读它）。
    ///
    /// 它不只是"看历史"：**撤销就是照这条记录把那个动作反着做一遍** ——
    /// 禁用 → 删掉隐藏开关；恢复 → 再写一个；删除 → 把那份 `.reg` 导回去。
    /// 所以记录里必须有**完整键路径**（带 hive，撤销时直接打开）和**备份文件名**。
    /// ⚠️ 更早那版记录只有 6 列（只记键名、没有备份），照样能读，但标成 `Legacy`，
    /// 界面上显示成"只能看、不能撤销" —— 绝不假装能撤。</summary>
    internal sealed class CtxAction
    {
        public string When = "";
        public string Action = "";
        public string ZoneId = "";
        public string ZoneLabel = "";
        /// <summary>完整键路径（HKEY_CURRENT_USER\Software\Classes\*\shell\Xxx）。</summary>
        public string Key = "";
        /// <summary>删除时那份 .reg 的完整路径（「还原来」要导入它）。</summary>
        public string Backup = "";
        public string Title = "";
        /// <summary>这一步的结果：成功 / 失败：原因 / 跳过：原因（2026-10-09 加的 —— 用户提的
        /// 「成功和失败为什么写到工具箱日历（日志）去了？就直接写到动作记录里面不行？两个窗口操作太麻烦了」）。
        /// 空 = 更早那版记录（那时候不记结果）。</summary>
        public string Result = "";
        /// <summary>旧格式的明细（只有旧记录才有，用来在界面上照原样显示）。</summary>
        public string Detail = "";
        /// <summary>旧格式记录（撤不了）。</summary>
        public bool Legacy = false;

        /// <summary>这一步是不是失败了（界面上整行画成危险色，用户一眼看到"哪条没成"）。</summary>
        public bool Failed
        {
            get { return Result.StartsWith("失败", StringComparison.Ordinal); }
        }

        /// <summary>这一条现在能不能撤销（删除要有备份文件；旧格式一律不行）。</summary>
        public bool CanUndo
        {
            get
            {
                if (Legacy || Key.Length == 0) { return false; }
                if (Action == "取得所有权并禁用")
                {
                    if (Backup.Length == 0) { return false; }
                    try { return File.Exists(Backup); } catch { return false; }
                }
                if (Action != "删除") { return true; }
                try { return (Backup.Length > 0) && File.Exists(Backup); }
                catch { return false; }
            }
        }

        /// <summary>撤销按钮上写什么（把"反着做一遍"说成人话）。</summary>
        public string UndoLabel
        {
            get
            {
                if (Action == "取得所有权并禁用") { return "还原权限"; }
                if (Action == "删除") { return "还原来"; }
                if (Action == "恢复") { return "再禁一次"; }
                return "恢复";
            }
        }

        /// <summary>键路径的短写法（`HKEY_CURRENT_USER\Software\Classes\…` → `HKCU\Software\Classes\…`）——
        /// 界面上那行要能一眼看出"动了哪个键"，完整 hive 名太占地方。
        ///
        /// ⚠️ 偏移量**一律用 `"前缀".Length` 算出来**，别写数字：这里 2026-10-10 就踩过一次 ——
        /// `HKEY_LOCAL_MACHINE\` 是 **19** 个字符，代码里写的是 `Substring(20)`，于是
        /// 用户自己那条真实记录显示成 `HKLM\oftware\Classes\Drive\shell\cmd`（**把 S 吃掉了**，
        /// 文件里存的是对的、只有界面上那行是错的）。`HKEY_CURRENT_USER\` 恰好 18，所以 HKCU
        /// 那条一直是好的 —— 这种"只有一半错"的 bug 靠肉眼很难发现。</summary>
        public string ShortKey
        {
            get
            {
                string s = Key;
                if (s.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                {
                    s = "HKCU\\" + s.Substring("HKEY_CURRENT_USER\\".Length);
                }
                else if (s.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
                {
                    s = "HKLM\\" + s.Substring("HKEY_LOCAL_MACHINE\\".Length);
                }
                return s;
            }
        }

        /// <summary>面板上那一行的正文。</summary>
        public string Line
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(When).Append("  ").Append(Action).Append("「").Append(Title).Append("」");
                if (Result.Length > 0) { sb.Append(" · ").Append(Result); }
                if (ShortKey.Length > 0) { sb.Append(" · ").Append(ShortKey); }
                else if (ZoneLabel.Length > 0) { sb.Append(" · ").Append(ZoneLabel); }
                if (Action == "删除" && Backup.Length > 0)
                {
                    try { sb.Append(" · 备份 ").Append(Path.GetFileName(Backup)); }
                    catch { }
                }
                if (Legacy) { sb.Append(" ·（旧记录，撤不了）"); }
                return sb.ToString();
            }
        }
    }

    /// <summary>「右键菜单管理」的后端：把 Windows 右键菜单里真实存在的项列出来，并且能禁用 / 恢复 / 删除。
    ///
    /// ## "禁用"到底写什么 —— 2026-10-09 实测定的，别改成别的写法
    ///
    /// 量法：临时造一条真菜单项（标题 `MXX1PROBE-VISIBLE`），用**只读探针**
    /// `tools\Show-MenuOrder.ps1`（走 shell 自己的 `IContextMenu::QueryContextMenu`，就是资源管理器
    /// 搭菜单用的那套接口）逐种写法各问一次"菜单里还有没有它"：
    ///
    /// | 写法                          | 结果                    |
    /// | ----------------------------- | ----------------------- |
    /// | 只有标题 + command（基线）     | 出现                    |
    /// | + `LegacyDisable` 空值         | **消失** ← 用这个       |
    /// | + `ProgrammaticAccessOnly` 空值| **消失**（一样灵，恢复时一并清） |
    /// | 删掉 `command` 子键            | 还在出现                |
    /// | `command` 改名                 | 还在出现                |
    /// | 整个 verb 键改名带后缀         | **还在出现**（键名被当成标题兜底） |
    ///
    /// 两个结论：① **禁用 = 往那个键里写一个 `LegacyDisable` 空值**，键和标题结构一个字都不用动，
    /// 恢复 = 把这个值删掉；② **"改键名"那条路是错的** —— 整键改名之后菜单里照样显示（拿键名当
    /// 标题），一开始正是打算这么做的，量过才知道不行。五个位置（文件 / 文件夹 / 文件夹空白处 /
    /// 桌面 / 磁盘）5/5 都灵，而且**全程没有重启资源管理器** —— 静态项是每次右键现场拼的，
    /// 改完再点一次右键就是新的。正本 docs\DESIGN.md §12.66。
    ///
    /// ## 三条底线
    /// ① **只动用户选中的那一个键**（禁用只加一个值；删除前必备份成 .reg，备份没成功就不删）；
    /// ② **扩展项（DLL）走另一套开关**（2026-10-11 补的，见 §12.68）：停用/恢复 = 往**用户区的**
    ///    系统黑名单里写/删一条（值名 = CLSID），登记键一个字都不动，而且**必须重启资源管理器**
    ///    才生效；黑名单是**按 CLSID 认的、全局的**，所以停用它 = 所有右键位置一起没。
    ///    ⚠️ 系统区那份黑名单（`HKLM`，这台机器上那 16 条就是 WinRAR / WPS / 火绒那些装进去的）
    ///    只读、一个字都不动 —— 那是别人的地盘，而且会影响所有用户；
    /// ③ **测试要把根挪走**：环境变量 `MXX1_CTXMENU_ROOT` / `_MACHINE`（菜单项那两个根）
    ///    与 `MXX1_CTXMENU_BLOCKED` / `_BLOCKED_MACHINE`（黑名单那两个根）。
    ///    回归测试一律用它，绝不碰用户真实的右键菜单、也绝不碰真实黑名单。
    ///
    /// 命令行只有**只读**出口（`rightmenu list`）；写只在界面里点（和 `sysreg` / `rightmenu` 同一条规矩）。
    /// 需要管理员时由界面把自己以管理员身份重开一次（`rightmenu manage --focus=…`），
    /// **命令行里不存在"带动作"的参数**，所以这条底线没破。</summary>
    internal static class CtxMenu
    {
        /// <summary>隐藏开关的值名（实测灵，见类说明）。</summary>
        public const string HideValue = "LegacyDisable";
        /// <summary>另一个等效的隐藏开关：恢复时一并清掉（别人用这个写过的也算"已禁用"）。</summary>
        public const string HideValue2 = "ProgrammaticAccessOnly";
        /// <summary>工具箱自己的右键项前缀（和 RightMenu 用的是同一个）。</summary>
        public const string OursPrefix = "Mxx1Toolbox.";

        // ---- 列表的四个段（2026-10-09 界面重做时定的：按"这是谁装的"分，
        //      而不是按注册表位置分 —— 用户要挑的就是"别人塞进来的那些"）--------------------
        public const string GroupOurs = "ours";
        public const string GroupApps = "apps";
        public const string GroupSystem = "system";
        public const string GroupShellex = "shellex";
        /// <summary>最后那一段：**系统黑名单里有、可机器上已经没有这个扩展了**的那些 CLSID。
        /// ⚠️ 它跟"在哪一页"无关（黑名单是按 GUID 认的、全局），所以只在**「文件」那一页**列出来 ——
        /// 五个位置各列一遍的话，每个位置的条数都会虚高，用户会以为"菜单里真有这么多"。</summary>
        public const string GroupZombie = "blockedorphan";

        /// <summary>段的顺序（从上到下）。</summary>
        public static readonly string[] GroupIds = new string[]
        {
            GroupOurs, GroupApps, GroupSystem, GroupShellex, GroupZombie
        };

        public static string GroupLabel(string id)
        {
            if (id == GroupOurs) { return "工具箱自己装的"; }
            if (id == GroupSystem) { return "系统自带"; }
            if (id == GroupShellex) { return "DLL 扩展项"; }
            if (id == GroupZombie) { return "黑名单里的残留条目"; }
            return "程序装的";
        }

        /// <summary>段标题后面那半句（说清这一段能不能动、为什么）。</summary>
        public static string GroupNote(string id)
        {
            if (id == GroupOurs) { return "想正规撤掉，回「右键增强」页点对应的「撤掉…」"; }
            if (id == GroupSystem) { return "Windows 自己的，建议用「禁用」而不是删除"; }
            if (id == GroupShellex)
            {
                return "状态是从系统黑名单读的：写着「已停用」的其实已经不在菜单里了；"
                    + "勾上能停用 / 恢复，但要重启资源管理器才生效（而且一停是所有位置一起没）";
            }
            if (id == GroupZombie)
            {
                return "这些 CLSID 还在系统黑名单里，可机器上已经找不到对应的扩展了（多半是卸载残留）——"
                    + "跟位置无关，统一放在「文件」这一页";
            }
            return "这些你可以禁用 / 删除";
        }

        /// <summary>段默认收起吗（系统自带的项最长、但最不该动，默认收起来；黑名单残留同理）。</summary>
        public static bool GroupCollapsedByDefault(string id)
        {
            return id == GroupSystem || id == GroupZombie;
        }

        /// <summary>上一版那批不带序号的旧键名（换名迁移之后就没了，但老用户那边可能还在）。
        /// 名单**直接引用 `RightMenu` 里的常量**，不在这里另抄一份 —— 抄一份就有第二处要维护。
        /// ⚠️ 判"这是我们家的键"**不能用 `Mxx1*` 前缀**：那样任何以 Mxx1 开头的别人的键都会被
        /// 认成我们的（测试里那批 `Mxx1Fixture*` 夹具当场就被认成了"工具箱自己装的"，
        /// 于是删除确认框里会多出一句牛头不对马嘴的提醒）。</summary>
        private static readonly string[] LegacyOursNames = new string[]
        {
            RightMenu.LegacyCopyNameVerb, RightMenu.LegacyCopyPathVerb, RightMenu.LegacyUnlockVerb,
            RightMenu.LegacyAutoVerb, RightMenu.LegacyCommonVerb, RightMenu.LegacyTerminalVerb,
            RightMenu.SupersededCopyRelVerb, RightMenu.SupersededCopyAbsVerb,
            RightMenu.SupersededTerminalCmdVerb, RightMenu.SupersededTerminalPsVerb,
        };

        private static bool IsOursName(string verb)
        {
            if (verb.StartsWith(OursPrefix, StringComparison.OrdinalIgnoreCase)) { return true; }
            foreach (string n in LegacyOursNames)
            {
                if (string.Equals(verb, n, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        private static readonly CtxZone[] Table = new CtxZone[]
        {
            new CtxZone("files", "文件", "在「文件」上点右键会看到的项",
                new string[] { "*" }),
            new CtxZone("folder", "文件夹", "在「文件夹」上点右键会看到的项",
                new string[] { "Directory", "Folder", "AllFilesystemObjects" }),
            new CtxZone("folderbg", "文件夹空白处", "在文件夹里的空白处点右键会看到的项",
                new string[] { "Directory\\Background" }),
            new CtxZone("desktop", "桌面", "在桌面空白处点右键会看到的项",
                new string[] { "DesktopBackground" }),
            new CtxZone("drive", "磁盘", "在磁盘分区上点右键会看到的项",
                new string[] { "Drive" }),
        };

        public static CtxZone[] Zones { get { return Table; } }

        public static CtxZone ZoneOf(string id)
        {
            foreach (CtxZone z in Table)
            {
                if (string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase)) { return z; }
            }
            return Table[0];
        }

        // ------------------------------------------------------------------ 根

        /// <summary>用户区（不用管理员）。默认 `HKEY_CURRENT_USER\Software\Classes`。整根字符串
        /// （带 hive 名）—— `reg.exe export` 也要用它，所以不留"相对"形态。</summary>
        public static string UserRoot
        {
            get { return Normalize(Environment.GetEnvironmentVariable("MXX1_CTXMENU_ROOT"), HiveCurrentUser); }
        }

        /// <summary>系统区（改它要管理员）。默认 `HKEY_LOCAL_MACHINE\Software\Classes`。</summary>
        public static string MachineRoot
        {
            get { return Normalize(Environment.GetEnvironmentVariable("MXX1_CTXMENU_ROOT_MACHINE"), HiveLocalMachine); }
        }

        private const string HiveCurrentUser = "HKEY_CURRENT_USER";
        private const string HiveLocalMachine = "HKEY_LOCAL_MACHINE";
        private const string ClassesSub = "Software\\Classes";

        // ---- 系统黑名单（DLL 扩展项真正的开关）------------------------------------------------------
        // 扩展项不是"注册表里一个键 = 一项"那种做法，它是 COM 的 shell 扩展：**停用它 = 把它的 CLSID
        // 写进一份黑名单**，资源管理器启动时看到 CLSID 在黑名单里就跳过它。有两个地方要记住：
        //   ① 值名 = CLSID（带花括号），值数据 = 一个**人看的名字**（资源管理器根本不看这个数据，
        //      它是别的工具写给用户看的 —— 实测这台机器上 16 条值数据写的就是「打开方式(H)」这种）；
        //   ② **必须重启资源管理器才生效**（DLL 是在 explorer 启动时加载进去的），和静态项
        //      「改完再点一次右键就是新的」完全不是一回事。
        // 用户区那份（HKCU）不用管理员，系统区那份（HKLM）是别的工具/安装程序写的 —— 本工具箱
        // **只写用户区那份**（ID 隔离、可逆、不碰别人的东西），系统区那几条只读出来告诉用户。
        /// <summary>黑名单的相对键路径（挂在 `HKCU` 或 `HKLM` 下面）。</summary>
        public const string BlockedSub = "Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked";

        /// <summary>用户区黑名单的整根（带 hive）。环境变量 `MXX1_CTXMENU_BLOCKED` 可以整根挪走
        /// —— ⚠️ **回归测试必须挪走**：不挪的话测试会把用户真实那份黑名单当成自己的夹具，
        /// 于是在这台机器上会凭空多出十几条「黑名单残留」（那 16 条都在 HKLM 里）。</summary>
        public static string BlockedUserRoot
        {
            get { return Normalize(Environment.GetEnvironmentVariable("MXX1_CTXMENU_BLOCKED"), HiveCurrentUser, BlockedSub); }
        }

        /// <summary>系统区黑名单的整根（默认 `HKLM\...\Blocked`）。同上，测试用
        /// `MXX1_CTXMENU_BLOCKED_MACHINE` 挪走（挪到 HKCU 下面就行，只读它的时候不需要管理员）。</summary>
        public static string BlockedMachineRoot
        {
            get { return Normalize(Environment.GetEnvironmentVariable("MXX1_CTXMENU_BLOCKED_MACHINE"), HiveLocalMachine, BlockedSub); }
        }

        public static bool IsTestRoot
        {
            get
            {
                return !string.Equals(UserRoot, HiveCurrentUser + "\\" + ClassesSub, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(MachineRoot, HiveLocalMachine + "\\" + ClassesSub, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>把环境变量给的根名整成"hive + 子键"的完整形式（`HKCU\Xxx` / `Software\Xxx` 都收）。</summary>
        private static string Normalize(string custom, string defaultHive)
        {
            return Normalize(custom, defaultHive, ClassesSub);
        }

        /// <summary>同上，但默认子键可以换（黑名单那两个根用的不是 `Software\Classes`）。</summary>
        private static string Normalize(string custom, string defaultHive, string defaultSub)
        {
            string s = AppPaths.Expand(custom);
            s = (s == null) ? "" : s.Trim().TrimStart('\\');
            if (s.Length == 0)
            {
                return defaultHive + "\\" + defaultSub;
            }
            if (s.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                return HiveCurrentUser + "\\" + s.Substring("HKCU\\".Length);
            }
            if (s.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            {
                return HiveCurrentUser + "\\" + s.Substring("HKEY_CURRENT_USER\\".Length);
            }
            if (s.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                return HiveLocalMachine + "\\" + s.Substring("HKLM\\".Length);
            }
            if (s.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            {
                // ⚠️ 这里原来写的是 `Substring(20)`，而 `HKEY_LOCAL_MACHINE\` 只有 19 个字符 →
                // 用完整 hive 名设系统区根（`MXX1_CTXMENU_ROOT_MACHINE=HKEY_LOCAL_MACHINE\…`）会被
                // 拼成 `HKEY_LOCAL_MACHINE\oftware\…`，然后去读一个不存在的键（M55 盯着这条）。
                return HiveLocalMachine + "\\" + s.Substring("HKEY_LOCAL_MACHINE\\".Length);
            }
            return defaultHive + "\\" + s;
        }

        private static void SplitFull(string full, out string hive, out string sub)
        {
            string s = (full == null) ? "" : full.Trim();
            if (s.StartsWith(HiveLocalMachine + "\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = HiveLocalMachine; sub = s.Substring(HiveLocalMachine.Length + 1);
            }
            else if (s.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = HiveLocalMachine; sub = s.Substring("HKLM\\".Length);
            }
            else if (s.StartsWith(HiveCurrentUser + "\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = HiveCurrentUser; sub = s.Substring(HiveCurrentUser.Length + 1);
            }
            else if (s.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            {
                hive = HiveCurrentUser; sub = s.Substring("HKCU\\".Length);
            }
            else
            {
                hive = HiveCurrentUser; sub = s.TrimStart('\\');
            }
        }

        /// <summary>打开一个完整键路径。`writable` 为真时**只打开已存在的键**，绝不新建 ——
        /// `CreateSubKey` 会在键已经不在的情况下凭空造一个空键出来，那就成了"给一个不存在的菜单项
        /// 写隐藏开关"，只会留垃圾。</summary>
        private static RegistryKey OpenFull(string full, bool writable)
        {
            string hive, sub;
            SplitFull(full, out hive, out sub);
            RegistryKey baseKey = string.Equals(hive, HiveLocalMachine, StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine : Registry.CurrentUser;
            if (writable)
            {
                using (RegistryKey probe = baseKey.OpenSubKey(sub, false))
                {
                    if (probe == null) { return null; }
                }
                return baseKey.OpenSubKey(sub, true);
            }
            return baseKey.OpenSubKey(sub, false);
        }

        // ------------------------------------------------------------------ 系统黑名单

        /// <summary>黑名单里的一条（同一个 CLSID 可能在用户区和系统区各写了一份）。</summary>
        internal sealed class BlockedHit
        {
            public bool InUser = false;
            public bool InMachine = false;
            public string UserName = "";
            public string MachineName = "";
            /// <summary>值数据里那个"人看的名字"（用户区优先）；两条都是空的就返回空串。</summary>
            public string Name { get { return (UserName.Length > 0) ? UserName : MachineName; } }
        }

        /// <summary>把黑名单里的值名整成 `{xxxxxxxx-xxxx-…}` 全小写形态。
        /// **黑名单是按 GUID 认的**，可写它的工具五花八门：带花括号的 / 不带的 / 大写小写混着的
        /// （实测这台机器 16 条值里就是大小写混着写的）。规范不出来（长度不对 —— 实测遇到过一个
        /// 尾部多打一个横杠的畸形注册）就返回空串：那种项**不参与黑名单匹配**，宁可不认也不猜。</summary>
        public static string NormalizeClsid(string s)
        {
            if (s == null) { return ""; }
            string t = s.Trim().Trim('{', '}').Replace(" ", "");
            if (t.Length != 36) { return ""; }
            return "{" + t.ToLowerInvariant() + "}";
        }

        /// <summary>读系统黑名单（用户区 + 系统区各读一份，合并成 CLSID → 命中信息）。
        /// 只读；一个字节都不写。</summary>
        public static Dictionary<string, BlockedHit> ReadBlocked()
        {
            Dictionary<string, BlockedHit> map = new Dictionary<string, BlockedHit>(StringComparer.OrdinalIgnoreCase);
            AddBlocked(map, BlockedUserRoot, true);
            AddBlocked(map, BlockedMachineRoot, false);
            return map;
        }

        private static void AddBlocked(Dictionary<string, BlockedHit> map, string root, bool user)
        {
            try
            {
                using (RegistryKey k = OpenFull(root, false))
                {
                    if (k == null) { return; }
                    foreach (string name in k.GetValueNames())
                    {
                        string id = NormalizeClsid(name);
                        if (id.Length == 0) { continue; }
                        BlockedHit h;
                        if (!map.TryGetValue(id, out h)) { h = new BlockedHit(); map[id] = h; }
                        string val = Str(k.GetValue(name, null));
                        if (user)
                        {
                            h.InUser = true;
                            if (val.Length > 0) { h.UserName = val; }
                        }
                        else
                        {
                            h.InMachine = true;
                            if (val.Length > 0) { h.MachineName = val; }
                        }
                    }
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ 列表

        /// <summary>列出某个位置里真实存在的项（zoneId 为空 = 全部位置）。
        /// 一个 verb 名在用户区和系统区各有一份时**只列一行**（菜单里也只有一项），
        /// 但 `InUser` / `InMachine` 两个标记都会亮 —— 写的时候两份都要写。</summary>
        public static List<CtxEntry> List(string zoneId)
        {
            Dictionary<string, BlockedHit> blocked = ReadBlocked();
            List<CtxEntry> list = new List<CtxEntry>();
            foreach (CtxZone z in Table)
            {
                if (zoneId != null && zoneId.Length > 0
                    && !string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase)) { continue; }
                Dictionary<string, CtxEntry> seen = new Dictionary<string, CtxEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (string cls in z.Classes) { AddVerbs(list, seen, z, cls); }
            }
            // 扩展项排在后面：它们只能看不能动，混在中间反而挡路
            foreach (CtxZone z in Table)
            {
                if (zoneId != null && zoneId.Length > 0
                    && !string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase)) { continue; }
                Dictionary<string, CtxEntry> seen = new Dictionary<string, CtxEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (string cls in z.Classes) { AddShellEx(list, seen, z, cls, blocked); }
            }
            // 「黑名单残留」那一段只挂在「文件」这一页（理由见 GroupZombie 的注释）
            CtxZone files = Table[0];
            bool wantZombies = (zoneId == null) || (zoneId.Length == 0)
                || string.Equals(zoneId, files.Id, StringComparison.OrdinalIgnoreCase);
            if (wantZombies && blocked.Count > 0)
            {
                // ⚠️ "这台机器上还有没有这个扩展"必须扫**五个位置**：上面两轮循环在"只看一页"时
                // 只扫了这一页 —— 拿它当判据会把别的位置独有的扩展误判成卸载残留。
                Dictionary<string, bool> registered = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                CollectRegisteredClsids(registered);
                AddZombies(list, blocked, registered, files);
            }
            return list;
        }

        /// <summary>把**五个位置**里登记过的扩展 CLSID 全收一遍（判"黑名单残留"用，只读）。</summary>
        private static void CollectRegisteredClsids(Dictionary<string, bool> registered)
        {
            foreach (CtxZone z in Table)
            {
                foreach (string cls in z.Classes)
                {
                    string rel = cls + "\\shellex\\ContextMenuHandlers";
                    foreach (string root in new string[] { UserRoot, MachineRoot })
                    {
                        using (RegistryKey k = OpenFull(root + "\\" + rel, false))
                        {
                            if (k == null) { continue; }
                            string[] names;
                            try { names = k.GetSubKeyNames(); }
                            catch { continue; }
                            foreach (string name in names)
                            {
                                if (name == null || name.Length == 0) { continue; }
                                try
                                {
                                    using (RegistryKey h = k.OpenSubKey(name, false))
                                    {
                                        if (h == null) { continue; }
                                        string id = NormalizeClsid(HandlerClsid(name, Str(h.GetValue(null, null))));
                                        if (id.Length > 0) { registered[id] = true; }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>黑名单里有、可机器上已经找不到对应扩展的 CLSID（卸载残留）→ 列成「黑名单残留」那一段。
        /// ⚠️ 系统区那份是别的工具写的：本工具箱只写用户区，所以系统区那几条**只显示、不给动**
        /// （界面上的判据就是 `BlockedUser`）。</summary>
        private static void AddZombies(List<CtxEntry> list, Dictionary<string, BlockedHit> blocked,
            Dictionary<string, bool> registered, CtxZone z)
        {
            List<string> ids = new List<string>(blocked.Keys);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                if (registered.ContainsKey(id)) { continue; }
                BlockedHit h = blocked[id];
                CtxEntry e = new CtxEntry();
                e.ZoneId = z.Id; e.ZoneLabel = z.Label;
                e.Sub = ""; e.Verb = id;
                e.Shellex = true;
                e.Zombie = true;
                e.Clsid = id; e.ClsidNorm = id;
                e.BlockedUser = h.InUser; e.BlockedMachine = h.InMachine; e.BlockedName = h.Name;
                e.InUser = h.InUser; e.InMachine = h.InMachine;
                e.Title = (h.Name.Length > 0) ? h.Name : id;
                e.Note = "黑名单里有这条 CLSID，可机器上已经找不到它对应的扩展了（多半是卸载残留）"
                    + (h.InUser ? " · 用户区那份能清掉" : "")
                    + (h.InMachine ? " · 系统区那份是别的工具写的，本工具箱不动" : "");
                list.Add(e);
            }
        }

        private static void AddVerbs(List<CtxEntry> list, Dictionary<string, CtxEntry> seen, CtxZone z, string cls)
        {
            string sub = cls + "\\shell";
            foreach (string root in new string[] { UserRoot, MachineRoot })
            {
                bool isUser = string.Equals(root, UserRoot, StringComparison.OrdinalIgnoreCase);
                using (RegistryKey k = OpenFull(root + "\\" + sub, false))
                {
                    if (k == null) { continue; }
                    string[] names;
                    try { names = k.GetSubKeyNames(); }
                    catch { continue; }
                    foreach (string verb in names)
                    {
                        if (verb == null || verb.Length == 0) { continue; }
                        CtxEntry found;
                        bool dup = seen.TryGetValue(verb, out found) && found != null;
                        if (dup)
                        {
                            // 两个根下同名：菜单里还是一项，但两份都得改
                            if (isUser) { found.InUser = true; } else { found.InMachine = true; }
                            continue;
                        }
                        // ⚠️ **读一项失败不能把整个列表带走**：2026-10-10 实测过一次 —— 有个键的权限
                        // 只给读（我们造的"被保护"夹具），`v.OpenSubKey("command", false)` 直接抛
                        // SecurityException，那一下把**整个管理窗口**崩掉了（进程退出码 0xE0434352）。
                        // 用户机器上只要有一个 ACL 古怪的项，窗口就会开不出来 —— 所以逐项兜住、跳过它。
                        try
                        {
                            using (RegistryKey v = k.OpenSubKey(verb, false))
                            {
                                if (v == null) { continue; }
                                CtxEntry e = new CtxEntry();
                                e.ZoneId = z.Id; e.ZoneLabel = z.Label;
                                e.Sub = sub; e.Verb = verb;
                                e.InUser = isUser; e.InMachine = !isUser;
                                FillVerb(e, v);
                                e.Protected = !AllRootsWritable(e);
                                seen[verb] = e;
                                list.Add(e);
                            }
                        }
                        catch
                        {
                            // 这一项读不动（权限被保护）：**照样列出来**，只是只知道键名。
                            // ⚠️ 别在这里 `continue` 跳过 —— 那会让这一项**从列表里消失**，
                            // 用户反而看不到它，也就没机会点「取得所有权」（2026-10-10 实测踩过：
                            // 跳过之后界面上少一条、计数也对不上）。
                            CtxEntry broken = new CtxEntry();
                            broken.ZoneId = z.Id; broken.ZoneLabel = z.Label;
                            broken.Sub = sub; broken.Verb = verb;
                            broken.InUser = isUser; broken.InMachine = !isUser;
                            broken.Title = verb;
                            broken.Protected = true;
                            // 尽力把标题读出来：读不动多半卡在 `command` 那一层，标题往往读得到
                            // （实测：只给 ReadKey 的键，默认值读得到，`OpenSubKey("command")` 才被拒）。
                            try
                            {
                                using (RegistryKey v2 = k.OpenSubKey(verb, false))
                                {
                                    if (v2 != null)
                                    {
                                        string t2 = Str(v2.GetValue(null, null));
                                        if (t2.Length == 0) { t2 = Str(v2.GetValue("MUIVerb", null)); }
                                        t2 = ResolveIndirect(t2);
                                        if (t2.Length > 0) { broken.Title = t2; }
                                    }
                                }
                            }
                            catch { }
                            broken.Note = "这一项读不动（权限被保护，连管理员也写不动）——"
                                + "只看得到键名。想动它：选中这一行点底栏的「取得所有权」。";
                            seen[verb] = broken;
                            list.Add(broken);
                        }
                    }
                }
            }
        }

        private static void FillVerb(CtxEntry e, RegistryKey v)
        {
            string title = Str(v.GetValue(null, null));
            if (title.Length == 0) { title = Str(v.GetValue("MUIVerb", null)); }
            title = ResolveIndirect(title);
            e.Disabled = (v.GetValue(HideValue, null) != null) || (v.GetValue(HideValue2, null) != null);
            e.ShiftOnly = (v.GetValue("Extended", null) != null);
            e.Submenu = (v.GetValue("SubCommands", null) != null)
                     || (v.GetValue("ExtendedSubCommandsKey", null) != null);
            e.Ours = IsOursName(e.Verb);
            e.Systemish = e.Verb.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase);
            e.IconValue = Str(v.GetValue("Icon", null));

            using (RegistryKey cmd = v.OpenSubKey("command", false))
            {
                if (cmd != null) { e.Command = Str(cmd.GetValue(null, null)); }
            }
            if (e.Command.Length == 0 && v.GetValue("DelegateExecute", null) != null)
            {
                e.Command = "（由程序自己处理：DelegateExecute）";
            }

            if (title.Length == 0)
            {
                // 没写标题的键：资源管理器会把键名当标题显示（实测过，见类说明里"整键改名"那一行）
                title = e.Verb;
            }
            StringBuilder sb = new StringBuilder(title);
            if (e.Submenu) { sb.Append("（子菜单）"); }
            if (e.ShiftOnly) { sb.Append("（按住 Shift 才显示）"); }
            if (e.Systemish) { sb.Append("（系统自带）"); }
            else if (e.Ours) { sb.Append("（工具箱）"); }
            e.Title = sb.ToString();

            if (e.Command.Length == 0 && !e.Submenu)
            {
                // Windows 11 起还有新一类：键里只有 `ExplorerCommandHandler`（一个 CLSID），没有 command
                // 子键，菜单上显示的字和图标由那个程序自己画（百度网盘那条 `YunShellExplorerCommand`
                // 就是这样，注册表里那个 `baidunetdisk` 只是键自己的标题）。
                // 这里**不猜**它到底叫什么，只把这件事说清楚 —— 用户看到"名字怪怪的"时知道为什么。
                string ech = Str(v.GetValue("ExplorerCommandHandler", null));
                e.Note = (ech.Length > 0)
                    ? ("Windows 11 的「程序自绘」型项（ExplorerCommandHandler " + ech
                       + "）：菜单上显示的字和图标由那个程序自己决定。")
                    : "这个键下面没有 command 子键（点了不会有动作，或者它用的是别的机制）";
            }
        }

        private static void AddShellEx(List<CtxEntry> list, Dictionary<string, CtxEntry> seen, CtxZone z, string cls,
            Dictionary<string, BlockedHit> blocked)
        {
            string rel = cls + "\\shellex\\ContextMenuHandlers";
            foreach (string root in new string[] { UserRoot, MachineRoot })
            {
                bool isUser = string.Equals(root, UserRoot, StringComparison.OrdinalIgnoreCase);
                using (RegistryKey k = OpenFull(root + "\\" + rel, false))
                {
                    if (k == null) { continue; }
                    string[] names;
                    try { names = k.GetSubKeyNames(); }
                    catch { continue; }
                    foreach (string name in names)
                    {
                        if (name == null || name.Length == 0) { continue; }
                        CtxEntry found;
                        if (seen.TryGetValue(name, out found) && found != null)
                        {
                            if (isUser) { found.InUser = true; } else { found.InMachine = true; }
                            continue;
                        }
                        try
                        {
                        using (RegistryKey h = k.OpenSubKey(name, false))
                        {
                            if (h == null) { continue; }
                            string value = Str(h.GetValue(null, null));
                            CtxEntry e = new CtxEntry();
                            e.ZoneId = z.Id; e.ZoneLabel = z.Label;
                            e.Sub = rel; e.Verb = name;
                            e.InUser = isUser; e.InMachine = !isUser;
                            e.Shellex = true;
                            string realClsid = HandlerClsid(name, value);
                            e.Systemish = HandlerDllIsSystem(realClsid);
                            e.IconValue = HandlerDllPath(realClsid);
                            e.Clsid = realClsid;
                            e.ClsidNorm = NormalizeClsid(realClsid);
                            e.DllPath = HandlerDllPath(realClsid);
                            string title = HandlerTitle(name, value);
                            // 黑名单里那个"人看的名字"只在**我们自己解不出名字**时才拿来当标题
                            // （解得出就用注册表里那个 —— 那个才是这项扩展真正的名字）
                            BlockedHit bh;
                            if (e.ClsidNorm.Length > 0 && blocked.TryGetValue(e.ClsidNorm, out bh) && bh != null)
                            {
                                e.BlockedUser = bh.InUser;
                                e.BlockedMachine = bh.InMachine;
                                e.BlockedName = bh.Name;
                                if (title.StartsWith("{", StringComparison.Ordinal) && e.BlockedName.Length > 0)
                                {
                                    title = e.BlockedName;
                                }
                            }
                            e.Title = title + (e.Systemish ? "（扩展项，系统自带）" : "（扩展项）");
                            // 副标题只留"这一项是什么"：状态（在用 / 已停用）在右边的状态列里，
                            // 黑名单里那个名字在选中它时底栏那句里说（副标题太长会被省略号吃掉）。
                            e.Note = "扩展项（CLSID " + (realClsid.Length > 0 ? realClsid : "没写")
                                + "，" + HandlerDll(realClsid) + "）";
                            seen[name] = e;
                            list.Add(e);
                        }
                        }
                        catch
                        {
                            CtxEntry broken = new CtxEntry();
                            broken.ZoneId = z.Id; broken.ZoneLabel = z.Label;
                            broken.Sub = rel; broken.Verb = name;
                            broken.InUser = isUser; broken.InMachine = !isUser;
                            broken.Shellex = true;
                            broken.Clsid = name;                    // 尽力：键名常常就是 CLSID
                            broken.ClsidNorm = NormalizeClsid(name);
                            broken.KeyLocked = true;
                            broken.Title = name + "（扩展项）";
                            broken.Note = "这一项读不动（那个登记键的权限被保护）—— 只看得到登记名。"
                                + "不过**停用它不用写那个键**：停用是往系统黑名单里写一条，照样能做。";
                            BlockedHit bh2;
                            if (broken.ClsidNorm.Length > 0 && blocked.TryGetValue(broken.ClsidNorm, out bh2) && bh2 != null)
                            {
                                broken.BlockedUser = bh2.InUser;
                                broken.BlockedMachine = bh2.InMachine;
                                broken.BlockedName = bh2.Name;
                            }
                            seen[name] = broken;
                            list.Add(broken);
                        }
                    }
                }
            }
        }

        /// <summary>扩展项的显示名。这里**有两种登记方式**，2026-10-09 在这台机器上两种都遇到了：
        /// ① 子键的默认值是 CLSID（`{23170F69-…}` → 要去 `HKCR\CLSID` 问它的名字，比如
        ///    「7-Zip Shell Extension」）；
        /// ② 子键的默认值**直接就是显示名**（`{90AA3A4E-…}` 的值是 `Taskband Pin`，键名才是 CLSID）
        ///    —— 第一版把这一种当成 CLSID 去查，查不到就退回去显示键名，于是列表里出现两行光秃秃的
        ///    `{90AA3A4E-…}`。判据是"这个值长得像不像 CLSID"，不是"键名长得像不像"。</summary>
        private static string HandlerTitle(string name, string value)
        {
            if (value.StartsWith("{", StringComparison.Ordinal))
            {
                string friendly = ClsidName(value);
                return (friendly.Length > 0) ? friendly : name;
            }
            if (value.Length > 0) { return value; }
            if (name.StartsWith("{", StringComparison.Ordinal))
            {
                string friendly = ClsidName(name);
                if (friendly.Length > 0) { return friendly; }
            }
            return name;
        }

        /// <summary>这一项真正的 CLSID：值像 CLSID 就用值，否则看键名像不像（两种登记方式都覆盖）。</summary>
        private static string HandlerClsid(string name, string value)
        {
            if (value.StartsWith("{", StringComparison.Ordinal)) { return value; }
            if (name.StartsWith("{", StringComparison.Ordinal)) { return name; }
            return "";
        }

        private static string ClsidName(string clsid)
        {
            if (clsid == null || clsid.Length == 0) { return ""; }
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid, false))
                {
                    if (k == null) { return ""; }
                    return ResolveIndirect(Str(k.GetValue(null, null)));
                }
            }
            catch { return ""; }
        }

        private static string HandlerDll(string clsid)
        {
            string p = HandlerDllPath(clsid);
            if (p.Length == 0) { return "不知道是哪个 DLL"; }
            try { return Path.GetFileName(p); }
            catch { return p; }
        }

        /// <summary>这个扩展的 DLL 是不是 Windows 自己的（在系统目录里）—— 是的话界面上标"系统自带"，
        /// 删除确认框里也会多一句（`复制文件路径` / `发送到` / `新建` 这些都是系统自带的扩展）。</summary>
        private static bool HandlerDllIsSystem(string clsid)
        {
            string p = HandlerDllPath(clsid);
            if (p.Length == 0) { return false; }
            string win = "";
            try { win = Environment.GetFolderPath(Environment.SpecialFolder.Windows); } catch { }
            if (win.Length == 0) { return false; }
            return p.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string HandlerDllPath(string clsid)
        {
            if (clsid == null || clsid.Length == 0) { return ""; }
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + clsid + "\\InprocServer32", false))
                {
                    if (k == null) { return ""; }
                    return AppPaths.Expand(Str(k.GetValue(null, null)));
                }
            }
            catch { return ""; }
        }

        /// <summary>这一项在**每一个**有它的根下都能写吗（不能写 = 被系统保护，要取得所有权）。</summary>
        private static bool AllRootsWritable(CtxEntry e)
        {
            foreach (string key in RootsOf(e))
            {
                if (!RegAcl.CanWrite(key)) { return false; }
            }
            return true;
        }

        private static string Str(object o)
        {
            return (o == null) ? "" : o.ToString();
        }

        /// <summary>`@shell32.dll,-8506` 这种间接字符串（系统自带的项基本都这么写）——
        /// 不解析的话列表里就是一片 `@dll,-数字`，等于没法看。
        /// ⚠️ 这个 API 的名字**没有 W 后缀**，所以必须写 `EntryPoint` + `ExactSpelling`：
        /// 只写 CharSet.Unicode 的话 CLR 会去找 `SHLoadIndirectStringW`（不存在），
        /// 异常还容易被 catch 吞掉 —— 和 2026-10-04 那个 `FindWindowW` 的坑是同一个（PITFALLS 坑 28）。</summary>
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "SHLoadIndirectString", ExactSpelling = true, PreserveSig = false)]
        private static extern void SHLoadIndirectString(string source, StringBuilder outBuf, int cchOutBuf, IntPtr reserved);

        private static string ResolveIndirect(string text)
        {
            if (text == null || text.Length == 0) { return ""; }
            if (text[0] != '@') { return text; }
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                SHLoadIndirectString(text, sb, sb.Capacity, IntPtr.Zero);
                string s = sb.ToString();
                return (s.Length > 0) ? s : text;
            }
            catch { return text; }
        }

        // ------------------------------------------------------------------ 统计

        /// <summary>一页的小结数字（窗口顶部与命令行都用它，只算一遍）。</summary>
        public static string Summary(List<CtxEntry> list)
        {
            int active = 0, disabled = 0, shellex = 0, machine = 0, zombie = 0, blockedExt = 0;
            foreach (CtxEntry e in list)
            {
                if (e.Zombie) { zombie++; }
                else if (e.Shellex) { shellex++; }
                else if (e.Disabled) { disabled++; }
                else { active++; }
                if (e.Shellex && !e.Zombie && e.Blocked) { blockedExt++; }
                if (e.InMachine) { machine++; }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("共 ").Append(list.Count.ToString(CultureInfo.InvariantCulture)).Append(" 条");
            sb.Append("（在用 ").Append(active.ToString(CultureInfo.InvariantCulture));
            sb.Append(" / 已禁用 ").Append(disabled.ToString(CultureInfo.InvariantCulture));
            sb.Append(" / 扩展项 ").Append(shellex.ToString(CultureInfo.InvariantCulture));
            if (zombie > 0) { sb.Append(" / 黑名单残留 ").Append(zombie.ToString(CultureInfo.InvariantCulture)); }
            int prot = 0;
            foreach (CtxEntry e in list) { if (e.Protected) { prot++; } }
            if (machine > 0)
            {
                sb.Append("；其中 ").Append(machine.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条在系统区，改它要管理员");
            }
            if (blockedExt > 0)
            {
                sb.Append("；其中 ").Append(blockedExt.ToString(CultureInfo.InvariantCulture))
                  .Append(" 个扩展项已被系统停用（其实已经不在菜单里了）");
            }
            if (zombie > 0)
            {
                sb.Append("；黑名单里还有 ").Append(zombie.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条已经没有对应扩展了（卸载残留，只在「文件」那一页列出来）");
            }
            if (prot > 0)
            {
                sb.Append("；").Append(prot.ToString(CultureInfo.InvariantCulture))
                  .Append(" 条被 Windows 保护（连管理员也写不动，要「取得所有权并禁用」）");
            }
            sb.Append("）");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 三个动作

        /// <summary>禁用：往这一项在**每一个**根下面的键里写一个隐藏开关（实测有效的那种写法）。
        /// 键结构和标题一个字都不动，所以"恢复"永远能把它原样弄回来。</summary>
        public static string Disable(CtxEntry e, out bool ok)
        {
            ok = false;
            StringBuilder sb = Head("禁用", e);
            if (!e.Actionable)
            {
                sb.AppendLine("结果：没有处理 —— 这是 DLL 扩展项，不走这三个动作（它走「停用扩展 / 恢复扩展」"
                    + "那条路：写系统黑名单，而且要重启资源管理器才生效，见段标题那行说明）。");
                return sb.ToString();
            }

            int done = 0, failed = 0, missed = 0;
            string why = "";        // 失败 / 跳过的原因（要写进动作记录那一行，用户不必再去翻日志）
            foreach (string key in RootsOf(e))
            {
                try
                {
                    using (RegistryKey k = OpenFull(key, true))
                    {
                        if (k == null) { missed++; why = "键已经不在了"; sb.AppendLine("· " + key + " —— 已经不在了，跳过"); continue; }
                        bool had = (k.GetValue(HideValue, null) != null) || (k.GetValue(HideValue2, null) != null);
                        k.SetValue(HideValue, "", RegistryValueKind.String);
                        object back = k.GetValue(HideValue, null);
                        if (back == null)
                        {
                            failed++; why = "写不进去（读回是空的）";
                            sb.AppendLine("· " + key + " —— 写不进去（读回是空的）"); continue;
                        }
                        done++;
                        sb.AppendLine("· " + key + " —— 已写入 " + HideValue
                            + (had ? "（这个键本来就有隐藏开关）" : ""));
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    why = ex.Message + AdminHint(e);
                    sb.AppendLine("· " + key + " —— 失败：" + ex.Message + AdminHint(e));
                }
            }
            ok = (failed == 0 && done > 0);
            sb.AppendLine();
            if (ok)
            {
                sb.AppendLine("结果：已禁用 —— 键和标题原样留着，右键里不再出现。"
                    + "（不用重启资源管理器：静态菜单项是每次右键现场拼的，实测过）");
                sb.AppendLine("想让它回来：选中这一行点「恢复」。");
                Record("禁用", e, done.ToString(CultureInfo.InvariantCulture) + " 个键", "", "成功");
            }
            else if (missed > 0 && done == 0)
            {
                sb.AppendLine("结果：这个键已经不存在了（可能刚被别的东西删掉 / 撤掉）。点「刷新」重新读一遍。");
                Record("禁用", e, "", "", "跳过：键已经不在了");
            }
            else
            {
                sb.AppendLine("结果：没成功。"
                    + (e.NeedsAdmin ? "这一项在系统区，需要管理员权限（点一下会弹 UAC，重开这个窗口再来一次）。" : ""));
                Record("禁用", e, "", "", "失败：" + FailWhy(why, e));
            }
            return sb.ToString();
        }

        /// <summary>恢复：把隐藏开关删掉（我们写的、别人写的都删 —— 用户点"恢复"要的就是"让它回来"，
        /// 删掉那个值就回来了，键本身从来没被改过）。</summary>
        public static string Restore(CtxEntry e, out bool ok)
        {
            ok = false;
            StringBuilder sb = Head("恢复", e);
            if (!e.Actionable)
            {
                sb.AppendLine("结果：没有处理 —— 这是 DLL 扩展项，不走这三个动作（它走「停用扩展 / 恢复扩展」"
                    + "那条路：写系统黑名单，而且要重启资源管理器才生效，见段标题那行说明）。");
                return sb.ToString();
            }

            int done = 0, failed = 0, nothing = 0;
            string why = "";
            foreach (string key in RootsOf(e))
            {
                try
                {
                    using (RegistryKey k = OpenFull(key, true))
                    {
                        if (k == null) { sb.AppendLine("· " + key + " —— 已经不在了，跳过"); continue; }
                        bool had1 = (k.GetValue(HideValue, null) != null);
                        bool had2 = (k.GetValue(HideValue2, null) != null);
                        if (!had1 && !had2)
                        {
                            nothing++;
                            sb.AppendLine("· " + key + " —— 本来就没有隐藏开关，没动它");
                            continue;
                        }
                        if (had1) { k.DeleteValue(HideValue, false); }
                        if (had2) { k.DeleteValue(HideValue2, false); }
                        bool gone = (k.GetValue(HideValue, null) == null) && (k.GetValue(HideValue2, null) == null);
                        if (!gone)
                        {
                            failed++; why = "删不掉（读回还在）";
                            sb.AppendLine("· " + key + " —— 删不掉（读回还在）");
                            continue;
                        }
                        done++;
                        sb.AppendLine("· " + key + " —— 已删掉 " + (had1 ? HideValue : HideValue2)
                            + (had2 && had1 ? " 和 " + HideValue2 : ""));
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    why = ex.Message + AdminHint(e);
                    sb.AppendLine("· " + key + " —— 失败：" + ex.Message + AdminHint(e));
                }
            }
            ok = (failed == 0 && done > 0);
            sb.AppendLine();
            if (ok)
            {
                sb.AppendLine("结果：已恢复 —— 重新点一次右键就能看到它（不用重启资源管理器）。");
                Record("恢复", e, done.ToString(CultureInfo.InvariantCulture) + " 个键", "", "成功");
            }
            else if (nothing > 0 && done == 0)
            {
                sb.AppendLine("结果：这一项本来就没被禁用，不需要恢复（你看到的状态可能是刚才改过、点「刷新」看新的）。");
                Record("恢复", e, "", "", "跳过：本来就没被禁用");
            }
            else
            {
                sb.AppendLine("结果：没成功。"
                    + (e.NeedsAdmin ? "这一项在系统区，需要管理员权限（点一下会弹 UAC，重开这个窗口再来一次）。" : ""));
                Record("恢复", e, "", "", "失败：" + FailWhy(why, e));
            }
            return sb.ToString();
        }

        /// <summary>「取得所有权并禁用」：给 Windows 保护的那些项用的（`src\RegAcl.cs`）。
        ///
        /// 三步，**顺序不能换**：① 先把原样的安全描述符（SDDL，含所有者和 DACL）存成一份文件 ——
        /// **存不下就绝不动手**（宁可不做，也不留一个"权限被改过、还不知道原来什么样"的系统键）；
        /// ② 取得所有权（所有者改 Administrators + 追加一条完全控制，原有 ACE 一条不删）；
        /// ③ 写隐藏开关（这时才写得动）。那份 SDDL 文件记在动作记录的"备份文件"那一列，
        /// 界面上「还原权限」就是把它写回去。
        /// ⚠️ 这是**改系统安全描述符**，比写一个值重得多，所以界面上过一次**重确认**才走到这里。</summary>
        public static string TakeOwnAndDisable(CtxEntry e, out bool ok)
        {
            ok = false;
            StringBuilder sb = Head("取得所有权并禁用", e);
            if (!e.Actionable)
            {
                sb.AppendLine("结果：没有处理 —— 这是 DLL 扩展项，不走「取得所有权」这条路"
                    + "（扩展项不用写那个登记键：停用/恢复是写系统黑名单，见「停用扩展」）。");
                return sb.ToString();
            }
            if (!IsAdmin())
            {
                sb.AppendLine("结果：没做 —— 这一步**必须是以管理员身份**（要打开「取得所有权」特权）。"
                    + "点一下会弹 UAC，重开这个窗口之后再来一次。");
                return sb.ToString();
            }
            if (!e.Protected)
            {
                sb.AppendLine("结果：没做 —— 这一项本来就能写，直接点「禁用」就行（不用抢所有权）。");
                return sb.ToString();
            }

            int done = 0, failed = 0;
            string sddlFile = "";
            string why = "";
            foreach (string key in RootsOf(e))
            {
                try
                {
                    if (!RegAcl.CanWrite(key))
                    {
                        string sddl = RegAcl.ReadSddl(key);
                        if (sddl.Length == 0)
                        {
                            failed++; why = "存不下原样的权限（SDDL），宁可不做";
                            sb.AppendLine("· " + key + " —— 存不下原样权限，跳过（没有它就不许改权限）");
                            continue;
                        }
                        Directory.CreateDirectory(BackupDir);
                        string file = Path.Combine(BackupDir,
                            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                            + "_acl_" + SafeName(key) + ".sddl");
                        File.WriteAllText(file, sddl, new UTF8Encoding(false));
                        sb.AppendLine("· 原样权限已存：" + file);

                        bool took;
                        string report = RegAcl.TakeOwn(key, out took);
                        sb.AppendLine(report.TrimEnd());
                        if (!took) { failed++; why = "取得所有权失败"; continue; }
                        sddlFile = file;
                    }
                    using (RegistryKey k = OpenFull(key, true))
                    {
                        if (k == null)
                        {
                            failed++; why = "键已经不在了";
                            sb.AppendLine("· " + key + " —— 已经不在了");
                            continue;
                        }
                        k.SetValue(HideValue, "", RegistryValueKind.String);
                        if (k.GetValue(HideValue, null) == null)
                        {
                            failed++; why = "写不进去（读回是空的）";
                            sb.AppendLine("· " + key + " —— 写不进去（读回是空的）");
                            continue;
                        }
                    }
                    done++;
                    sb.AppendLine("· " + key + " —— 已写入 " + HideValue);
                }
                catch (Exception ex)
                {
                    failed++; why = ex.Message;
                    sb.AppendLine("· " + key + " —— 失败：" + ex.Message);
                }
            }
            ok = (failed == 0 && done > 0);
            sb.AppendLine();
            if (ok)
            {
                sb.AppendLine("结果：已取得所有权并禁用 —— 那一项从右键里消失了，原样的权限存在上面那个文件里。");
                sb.AppendLine("想连权限一起放回去：动作记录里点「还原权限」。");
                Record("取得所有权并禁用", e, done.ToString(CultureInfo.InvariantCulture) + " 个键", sddlFile, "成功");
            }
            else
            {
                sb.AppendLine("结果：没成功。");
                Record("取得所有权并禁用", e, "", sddlFile, "失败：" + FailWhy(why, e));
            }
            return sb.ToString();
        }

        /// <summary>删除：**先备份成 .reg，备份没成功就不删**。
        /// 删除不是"隐藏"，键真的没了 —— 所以备份是这一步的硬前提（用户双击那个 .reg 就能还原）。</summary>
        public static string Delete(CtxEntry e, out string backupFile, out bool ok)
        {
            ok = false;
            backupFile = "";
            StringBuilder sb = Head("删除", e);
            if (!e.Actionable)
            {
                sb.AppendLine("结果：没有处理 —— 这是 DLL 扩展项，不走这三个动作（它走「停用扩展 / 恢复扩展」"
                    + "那条路：写系统黑名单，而且要重启资源管理器才生效，见段标题那行说明）。");
                return sb.ToString();
            }

            List<string> backups = new List<string>();
            int done = 0, failed = 0;
            string why = "";
            foreach (string key in RootsOf(e))
            {
                // ① 备份（先做，做不成就绝不往下走）
                string file;
                string err = Export(key, out file);
                if (err.Length > 0)
                {
                    failed++; why = "备份失败（" + err + "）";
                    sb.AppendLine("· " + key + " —— 备份失败，所以没有删：" + err);
                    continue;
                }
                backups.Add(file);
                sb.AppendLine("· 已备份：" + file);

                // ② 删（用 .NET 的删键，不拼 reg.exe 的命令行 —— 键名里有空格 / 斜杠时不会出错）
                try
                {
                    string parent, leaf;
                    SplitLeaf(key, out parent, out leaf);
                    using (RegistryKey p = OpenFull(parent, true))
                    {
                        if (p == null) { failed++; sb.AppendLine("   └ 父键不在了，跳过"); continue; }
                        p.DeleteSubKeyTree(leaf, false);
                    }
                    using (RegistryKey check = OpenFull(parent, false))
                    {
                        bool still = false;
                        if (check != null)
                        {
                            try { still = (check.OpenSubKey(leaf, false) != null); }
                            catch { still = true; }
                        }
                        if (still) { failed++; why = "删不掉（读回还在）"; sb.AppendLine("   └ 删不掉（读回还在）"); continue; }
                    }
                    done++;
                    sb.AppendLine("   └ 已删除：" + key);
                }
                catch (Exception ex)
                {
                    failed++; why = ex.Message + AdminHint(e);
                    sb.AppendLine("   └ 失败：" + ex.Message + AdminHint(e));
                }
            }
            if (backups.Count > 0) { backupFile = backups[0]; }
            sb.AppendLine();
            if (failed == 0 && done > 0)
            {
                sb.AppendLine("结果：已删除。后悔了就双击上面那个 .reg 文件，菜单项就回来了"
                    + "（也可以在窗口里点「打开备份文件夹」）。");
                Record("删除", e, done.ToString(CultureInfo.InvariantCulture) + " 个键", backupFile, "成功");
                ok = true;
            }
            else
            {
                sb.AppendLine("结果：没全部成功。"
                    + (e.NeedsAdmin ? "这一项在系统区，需要管理员权限（点一下会弹 UAC，重开这个窗口再来一次）。" : ""));
                Record("删除", e, "", backupFile, "失败：" + FailWhy(why, e));
            }
            return sb.ToString();
        }

        private static StringBuilder Head(string action, CtxEntry e)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(action).Append("右键菜单里的这一项：").AppendLine(e.Title);
            sb.Append("位置：").Append(e.ZoneLabel).Append("（").Append(e.Sub).Append("）").AppendLine();
            sb.Append("键：").AppendLine(e.Verb);
            if (e.Disabled) { sb.AppendLine("现在状态：已禁用（带着隐藏开关）"); }
            if (e.Command.Length > 0) { sb.Append("命令：").AppendLine(e.Command); }
            sb.AppendLine();
            return sb;
        }

        /// <summary>失败原因给用户看的那一句（记进动作记录那一行）。系统区的项还写不进去时，
        /// 补一句"要管理员"，免得用户只看到一句"拒绝访问"不知道该干嘛。</summary>
        private static string FailWhy(string why, CtxEntry e)
        {
            string s = (why == null || why.Length == 0) ? "没成功" : why;
            if (e.NeedsAdmin) { s += "（这一项在系统区，需要管理员权限：点一下会弹 UAC，重开这个窗口再来一次）"; }
            return s;
        }

        private static string AdminHint(CtxEntry e)
        {
            return e.NeedsAdmin ? "（这一项在系统区，需要管理员权限）" : "";
        }

        /// <summary>这一项在哪些根下面有键（用户区在前）。</summary>
        private static List<string> RootsOf(CtxEntry e)
        {
            List<string> list = new List<string>();
            if (e.InUser || !e.InMachine) { list.Add(e.UserKey); }
            if (e.InMachine) { list.Add(e.MachineKey); }
            return list;
        }

        private static void SplitLeaf(string full, out string parent, out string leaf)
        {
            int i = full.LastIndexOf('\\');
            if (i <= 0) { parent = full; leaf = ""; return; }
            parent = full.Substring(0, i);
            leaf = full.Substring(i + 1);
        }

        // ------------------------------------------------------------------ 备份

        /// <summary>备份目录（`%LOCALAPPDATA%\mxx1-toolbox\ctxmenu-backup`）。
        /// 放在 AppPaths 之外是**故意的**：AppPaths.cs 不在测试映射表里，动它会让闸门退回跑全套；
        /// 这里只借用它的 BaseDir，路径仍然只有一处。</summary>
        public static string BackupDir { get { return Path.Combine(AppPaths.BaseDir, "ctxmenu-backup"); } }

        /// <summary>动作流水（`ctxmenu-actions.tsv`）：窗口底部那行"上一次…"和事后排查都读它。</summary>
        public static string ActionLogFile { get { return Path.Combine(AppPaths.BaseDir, "ctxmenu-actions.tsv"); } }

        /// <summary>`reg.exe export` 一份。成功返回空串，失败返回原因（**失败就绝不删**）。
        /// ⚠️ 边跑边读输出：先 WaitForExit 再 ReadToEnd 在输出超过管道缓冲时会父子互等（本仓库踩过）。</summary>
        private static string Export(string key, out string file)
        {
            file = "";
            try
            {
                Directory.CreateDirectory(BackupDir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                file = Path.Combine(BackupDir, stamp + "_" + SafeName(key) + ".reg");
                if (File.Exists(file))
                {
                    file = Path.Combine(BackupDir, stamp + "-" + Environment.TickCount.ToString(CultureInfo.InvariantCulture)
                        + "_" + SafeName(key) + ".reg");
                }
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(
                    "reg.exe", "export \"" + key + "\" \"" + file + "\" /y");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = new System.Diagnostics.Process())
                {
                    p.StartInfo = psi;
                    StringBuilder err = new StringBuilder();
                    p.ErrorDataReceived += delegate(object s, System.Diagnostics.DataReceivedEventArgs a)
                    {
                        if (a.Data != null) { lock (err) { err.AppendLine(a.Data); } }
                    };
                    p.Start();
                    p.BeginErrorReadLine();
                    System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                    bool finished = p.WaitForExit(15000);
                    if (!finished) { try { p.Kill(); } catch { } return "导出超时（15 秒）"; }
                    outTask.Wait(2000);
                    if (p.ExitCode != 0)
                    {
                        string e2 = err.ToString().Trim();
                        return "reg.exe 退出码 " + p.ExitCode.ToString(CultureInfo.InvariantCulture)
                            + (e2.Length > 0 ? "：" + e2 : "");
                    }
                }
                // 读回核对：文件真的在、而且不是空的（空文件 = 拿到了一份没用的"备份"）
                if (!File.Exists(file)) { return "导出之后没找到那个文件"; }
                long len = new FileInfo(file).Length;
                if (len <= 0) { return "导出的文件是空的"; }
                return "";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>把键路径变成能当文件名的一段（Windows 文件名里不许出现 \\ / : * ? " &lt; &gt; |）。</summary>
        private static string SafeName(string key)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-') { sb.Append(c); }
                else { sb.Append('_'); }
            }
            string s = sb.ToString();
            if (s.Length > 80) { s = s.Substring(s.Length - 80); }
            return s;
        }

        /// <summary>记一笔动作。**这份记录不只是"看历史"**：界面上的「动作记录」面板每条后面挂一个
        /// 按钮（禁用→「恢复」/ 恢复→「再禁一次」/ 删除→「还原来」），撤销就是照这条记录反着做一遍。
        /// 所以从 2026-10-09 界面重做起，这里必须记**完整键路径**（带 hive，撤销时要能直接打开）
        /// 和**备份文件名**（删除的还原来要 `reg import` 它）。
        /// ⚠️ 旧格式（那时候只有 6 列、只记键名）照样能读，但标成"撤不了" —— 绝不假装能撤。</summary>
        private static void Record(string action, CtxEntry e, string detail, string backup, string result)
        {
            // 键路径一律走 `e.FullKey`：扩展项/残留条目的"键"不是菜单项那个键（见 CtxEntry.FullKey）
            string key = e.FullKey;
            RecordRaw(action, e.ZoneId, e.ZoneLabel, key, backup, e.Title, result);
            Logger.Write("右键菜单管理", action + " · " + e.Title + " · " + e.ZoneLabel + " · " + e.Verb
                + (detail.Length > 0 ? " · " + detail : ""));
        }

        /// <summary>记一笔动作（"键"直接给字符串的版本 —— 撤销那条路要照记录重建，手里没有 `CtxEntry`）。</summary>
        private static void RecordRaw(string action, string zoneId, string zoneLabel, string key, string backup, string title, string result)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "\t" + action
                + "\t" + zoneId + "\t" + zoneLabel
                + "\t" + key
                + "\t" + (backup == null ? "" : backup)
                + "\t" + title
                + "\t" + (result == null ? "" : result);
            try
            {
                AppPaths.EnsureBase();
                if (!File.Exists(ActionLogFile))
                {
                    File.WriteAllText(ActionLogFile,
                        "# 萌新工具箱 · 右键菜单管理做过什么（界面上「动作记录」面板读它；撤销也靠它，别手改）"
                        + Environment.NewLine
                        + "# 时间\t动作\t位置id\t位置\t完整键路径\t备份文件\t菜单上显示的字\t结果（成功 / 失败：原因）" + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                File.AppendAllText(ActionLogFile, line + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>读动作记录（**最新的在最前面** —— 和工具箱"日志一律最新在最上"同一条规矩）。</summary>
        public static List<CtxAction> LoadActions(int max)
        {
            List<CtxAction> list = new List<CtxAction>();
            try
            {
                if (!File.Exists(ActionLogFile)) { return list; }
                string[] lines = File.ReadAllLines(ActionLogFile, Encoding.UTF8);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string s = lines[i];
                    if (s.Length == 0 || s.StartsWith("#")) { continue; }
                    string[] f = s.Split('\t');
                    CtxAction a = new CtxAction();
                    a.When = (f.Length > 0) ? f[0] : "";
                    a.Action = (f.Length > 1) ? f[1] : "";
                    a.ZoneId = (f.Length > 2) ? f[2] : "";
                    a.ZoneLabel = (f.Length > 3) ? f[3] : "";
                    if (f.Length >= 8)
                    {
                        a.Key = f[4];
                        a.Backup = f[5];
                        a.Title = f[6];
                        a.Result = f[7];
                    }
                    else if (f.Length >= 7)
                    {
                        a.Key = f[4];
                        a.Backup = f[5];
                        a.Title = f[6];
                    }
                    else if (f.Length >= 6)
                    {
                        // 旧格式：第 5 列是"明细"，第 6 列才是标题；没有键路径也没有备份 → 撤不了
                        a.Legacy = true;
                        a.Detail = f[4];
                        a.Title = f[5];
                    }
                    else { continue; }
                    if (a.Action.Length == 0 || a.Title.Length == 0) { continue; }
                    list.Add(a);
                    if (max > 0 && list.Count >= max) { break; }
                }
            }
            catch { }
            return list;
        }

        /// <summary>撤销一条记录 = 把那个动作反着做一遍：
        /// 禁用 → 把隐藏开关删掉；恢复 → 再写一个隐藏开关；删除 → 把那份 .reg 导回去。
        /// 每一步都**读回核对**，并且撤销本身也记一笔（所以"撤销了撤销"也说得清）。</summary>
        public static string Undo(CtxAction a, out bool ok)
        {
            ok = false;
            StringBuilder sb = new StringBuilder();
            sb.Append("撤销：").Append(a.When).Append(" 的「").Append(a.Action).Append("「").Append(a.Title).Append("」").AppendLine();
            if (a.Legacy || a.Key.Length == 0)
            {
                sb.AppendLine("结果：没做 —— 这条记录是**旧格式**的（那时候只记了键名，没记完整键路径），"
                    + "所以撤不了。新的动作都有「恢复 / 还原来」。");
                return sb.ToString();
            }
            sb.Append("键：").AppendLine(a.Key);

            if (a.Action == "取得所有权并禁用")
            {
                // ① 先把隐藏开关删掉（让那一项回到菜单里）；② 再把安全描述符写回原样
                try
                {
                    using (RegistryKey k = OpenFull(a.Key, true))
                    {
                        if (k != null)
                        {
                            k.DeleteValue(HideValue, false);
                            k.DeleteValue(HideValue2, false);
                        }
                    }
                }
                catch (Exception ex) { sb.AppendLine("· 删隐藏开关时：" + ex.Message); }
                string sddl = "";
                try { if (File.Exists(a.Backup)) { sddl = File.ReadAllText(a.Backup, Encoding.UTF8).Trim(); } }
                catch { }
                bool okAcl;
                string rep = RegAcl.RestoreSddl(a.Key, sddl, out okAcl);
                sb.AppendLine(rep.TrimEnd());
                ok = okAcl;
                if (ok) { RecordRaw("撤销·还原权限", a.ZoneId, a.ZoneLabel, a.Key, a.Backup, a.Title, "成功"); }
                return sb.ToString();
            }

            if (a.Action == "停用扩展" || a.Action == "恢复扩展")
            {
                // 撤销扩展项那一步 = 反着做一次（改的只有**用户区**黑名单里那一条值）。
                // ⚠️ 记录里的"键"是 `<黑名单键>\<CLSID>`：**最后那一段是值名**，不是子键
                // （所以这里不能走 `OpenFull` —— 那样会去开一个叫 CLSID 的键，永远开不到）。
                int cut = a.Key.LastIndexOf('\\');
                if (cut <= 0)
                {
                    sb.AppendLine("结果：没做 —— 记录里那个路径看不出 CLSID（" + a.Key + "）。");
                    return sb.ToString();
                }
                string clsid = a.Key.Substring(cut + 1);
                bool wantBlocked = (a.Action == "恢复扩展");     // 「恢复」的撤销 = 再停用一次
                try
                {
                    using (RegistryKey k = OpenBlocked(wantBlocked))
                    {
                        if (k == null)
                        {
                            sb.AppendLine("结果：没做 —— 那个黑名单键不存在（大概是你刚把它手工删了）。");
                            return sb.ToString();
                        }
                        if (wantBlocked)
                        {
                            k.SetValue(clsid, MenuNameOf(a.Title), RegistryValueKind.String);
                            if (k.GetValue(clsid, null) == null)
                            {
                                sb.AppendLine("结果：没成功（写回之后读回是空的）");
                                return sb.ToString();
                            }
                            sb.AppendLine("结果：已再停用一次（黑名单里又有 " + clsid + " 了）—— 重启资源管理器后生效。");
                            RecordRaw("撤销·再停用扩展", a.ZoneId, a.ZoneLabel, a.Key, "", a.Title, "成功");
                        }
                        else
                        {
                            k.DeleteValue(clsid, false);
                            if (k.GetValue(clsid, null) != null)
                            {
                                sb.AppendLine("结果：没成功（删完读回它还在）");
                                return sb.ToString();
                            }
                            sb.AppendLine("结果：已恢复（黑名单里的 " + clsid + " 删掉了）—— 重启资源管理器后生效。");
                            RecordRaw("撤销·撤销停用扩展", a.ZoneId, a.ZoneLabel, a.Key, "", a.Title, "成功");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("结果：没成功 —— " + ex.Message);
                    return sb.ToString();
                }
                ok = true;
                return sb.ToString();
            }

            if (a.Action == "删除")
            {
                if (a.Backup.Length == 0 || !File.Exists(a.Backup))
                {
                    sb.AppendLine("结果：没做 —— 找不到那份备份文件（" + (a.Backup.Length > 0 ? a.Backup : "记录里没写") + "）。");
                    return sb.ToString();
                }
                string err = RunReg("import \"" + a.Backup + "\"");
                if (err.Length > 0) { sb.AppendLine("结果：导入失败 —— " + err); return sb.ToString(); }
                using (RegistryKey back = OpenFull(a.Key, false))
                {
                    if (back == null) { sb.AppendLine("结果：导入了，但读回没找到那个键（备份文件可能不完整）"); return sb.ToString(); }
                }
                sb.AppendLine("结果：已还原来（把备份 " + Path.GetFileName(a.Backup) + " 导回去了，键读回在）。");
                ok = true;
                RecordRaw("撤销·还原来", a.ZoneId, a.ZoneLabel, a.Key, a.Backup, a.Title, "成功");
                return sb.ToString();
            }

            bool wantDisabled = (a.Action != "禁用");   // 「恢复」的撤销 = 再禁一次
            try
            {
                using (RegistryKey k = OpenFull(a.Key, true))
                {
                    if (k == null) { sb.AppendLine("结果：没做 —— 那个键已经不存在了（可能被删掉了）"); return sb.ToString(); }
                    if (wantDisabled)
                    {
                        k.SetValue(HideValue, "", RegistryValueKind.String);
                        if (k.GetValue(HideValue, null) == null) { sb.AppendLine("结果：没成功（读回是空的）"); return sb.ToString(); }
                        sb.AppendLine("结果：已再禁一次（那一项又藏起来了）。");
                        RecordRaw("撤销·再禁一次", a.ZoneId, a.ZoneLabel, a.Key, "", a.Title, "成功");
                    }
                    else
                    {
                        k.DeleteValue(HideValue, false);
                        k.DeleteValue(HideValue2, false);
                        if ((k.GetValue(HideValue, null) != null) || (k.GetValue(HideValue2, null) != null))
                        {
                            sb.AppendLine("结果：没成功（读回还在）");
                            return sb.ToString();
                        }
                        sb.AppendLine("结果：已恢复（那一项回到菜单里了）。");
                        RecordRaw("撤销·恢复", a.ZoneId, a.ZoneLabel, a.Key, "", a.Title, "成功");
                    }
                    ok = true;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果：失败 —— " + ex.Message);
            }
            return sb.ToString();
        }

        /// <summary>`reg.exe` 跑一句（备份导入用）。成功返回空串，失败返回原因。
        /// ⚠️ 边跑边读：先 WaitForExit 再 ReadToEnd 在输出超过管道缓冲时会父子互等。</summary>
        private static string RunReg(string arguments)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("reg.exe", arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = new System.Diagnostics.Process())
                {
                    p.StartInfo = psi;
                    StringBuilder err = new StringBuilder();
                    p.ErrorDataReceived += delegate(object s, System.Diagnostics.DataReceivedEventArgs a)
                    {
                        if (a.Data != null) { lock (err) { err.AppendLine(a.Data); } }
                    };
                    p.Start();
                    p.BeginErrorReadLine();
                    System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return "reg.exe 超时（15 秒）"; }
                    outTask.Wait(2000);
                    if (p.ExitCode != 0)
                    {
                        string e2 = err.ToString().Trim();
                        return "reg.exe 退出码 " + p.ExitCode.ToString(CultureInfo.InvariantCulture)
                            + (e2.Length > 0 ? "：" + e2 : "");
                    }
                }
                return "";
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>窗口底部那行"上一次…"（没有记录就返回空串）。</summary>
        public static string LastAction()
        {
            List<CtxAction> list = LoadActions(1);
            if (list.Count == 0) { return ""; }
            CtxAction a = list[0];
            return "上一次：" + a.Action + "「" + a.Title + "」 · " + a.When;
        }

        // ------------------------------------------------------------------ 停用 / 恢复 DLL 扩展

        /// <summary>打开**用户区**那份黑名单键。`create` 时才新建 —— 黑名单那个键**本来可能不存在**
        /// （实测用户这台机器的 `HKCU` 下就没有，16 条全在 `HKLM` 里，是别的工具写的），
        /// 所以"要写"的时候必须能建。这是相对 `OpenFull` 那条"只打开已存在的键"的**有意例外**：
        /// 建这个键本身就是这个功能的正常动作，不像"给一个不存在的菜单项写隐藏开关"那样造垃圾。
        /// ⚠️ `create=false` 时**必须以可写方式打开**（先探一下存在、再 `OpenSubKey(sub, true)`）：
        /// `OpenSubKey(sub, false)` 拿到的是**只读**句柄，拿它去 `DeleteValue` 会抛
        /// 「无法写入到注册表项」—— 2026-10-11 实测踩过：整个「恢复扩展」都失败，界面上只有一句
        /// 「没成功」，原因只写在工具日志里（探针一看就明白了：`结果：没成功 —— 无法写入到注册表项。`）。</summary>
        private static RegistryKey OpenBlocked(bool create)
        {
            string hive, sub;
            SplitFull(BlockedUserRoot, out hive, out sub);
            RegistryKey baseKey = string.Equals(hive, HiveLocalMachine, StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine : Registry.CurrentUser;
            if (create) { return baseKey.CreateSubKey(sub); }
            using (RegistryKey probe = baseKey.OpenSubKey(sub, false))
            {
                if (probe == null) { return null; }
            }
            return baseKey.OpenSubKey(sub, true);
        }

        /// <summary>写进黑名单的"人看的名字"：黑名单里原来有名字就用它；没有就把界面上加的括号尾巴
        /// （`（扩展项）` / `（扩展项，系统自带）`）去掉（值数据只是给人看的，别塞界面用词）。</summary>
        private static string MenuName(CtxEntry e)
        {
            if (e.BlockedName.Length > 0) { return e.BlockedName; }
            return MenuNameOf(e.Title);
        }

        /// <summary>同上，只是手里只有那串标题（撤销那条路是从动作记录里读回来的）。</summary>
        private static string MenuNameOf(string title)
        {
            string t = (title == null) ? "" : title;
            const string ext = "（扩展项）";
            const string extSys = "（扩展项，系统自带）";
            if (t.EndsWith(extSys, StringComparison.Ordinal)) { return t.Substring(0, t.Length - extSys.Length); }
            if (t.EndsWith(ext, StringComparison.Ordinal)) { return t.Substring(0, t.Length - ext.Length); }
            return t;
        }

        /// <summary>停用一个 DLL 扩展 = 把它的 CLSID 写进**用户区**那份系统黑名单
        /// （值名 = CLSID，值数据 = 菜单上那个名字）。
        ///
        /// 四条边界（2026-10-11 定的，别改）：
        /// ① **只写用户区**（`HKCU`）：不用管理员、只影响当前用户、随时能删干净；系统区那份（`HKLM`）
        ///    是别的工具或安装程序写的，本工具箱**一个字都不动**（别人的地盘，而且影响所有用户）；
        /// ② **不碰登记键**：`shellex\ContextMenuHandlers\…` 一个字都不改 —— 所以"恢复"永远是干净的
        ///    （把黑名单里那条删掉就行），不像删键那样要备份；
        /// ③ ⚠️ **必须重启资源管理器才生效**：DLL 是 explorer 启动时加载进去的。写完立刻读回核对
        ///    （证明"真写进去了"），但菜单里那一项还在，直到资源管理器重启；
        /// ④ ⚠️ 黑名单是**按 CLSID 认的、全局的**：停用它 = 它在**所有**右键位置一起消失
        ///    （不像静态项能只停某一个位置）—— 界面上的提示就是这么写的。</summary>
        public static string BlockExt(CtxEntry e, out bool ok)
        {
            ok = false;
            StringBuilder sb = Head("停用扩展", e);
            if (!e.Shellex)
            {
                sb.AppendLine("结果：没有处理 —— 这一条不是 DLL 扩展项（它是静态菜单项，用「禁用」）。");
                return sb.ToString();
            }
            if (e.ClsidNorm.Length == 0)
            {
                sb.AppendLine("结果：没有处理 —— 这一项的 CLSID 认不出来（登记名不规范），"
                    + "没有一个能写进黑名单的 GUID。原始登记名：" + e.Verb);
                return sb.ToString();
            }
            if (e.BlockedMachine)
            {
                sb.AppendLine("结果：没有处理 —— 系统区那份黑名单里已经有它了（" + BlockedMachineRoot + "）。"
                    + "那是别的工具或安装程序写的，本工具箱不动系统区；它现在本来就是停用的。");
                return sb.ToString();
            }
            if (e.BlockedUser)
            {
                sb.AppendLine("结果：没有处理 —— 用户区那份黑名单里已经有它了（可能就是你刚才停用的）。");
                return sb.ToString();
            }
            string clsid = e.ClsidNorm;
            string name = MenuName(e);
            sb.Append("黑名单键：").AppendLine(BlockedUserRoot);
            try
            {
                using (RegistryKey k = OpenBlocked(true))
                {
                    if (k == null)
                    {
                        sb.AppendLine("结果：没成功 —— 打不开（也建不了）那个黑名单键。");
                        sb.AppendLine(FailWhy("写不进黑名单", e));
                        return sb.ToString();
                    }
                    k.SetValue(clsid, name, RegistryValueKind.String);
                    object back = k.GetValue(clsid, null);
                    if (back == null)
                    {
                        sb.AppendLine("结果：没成功（写完读回是空的）—— 那个键可能不让写。");
                        sb.AppendLine(FailWhy("写不进黑名单", e));
                        return sb.ToString();
                    }
                    sb.AppendLine("· 已写入 " + clsid + " = " + name + "（读回核对过）");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果：没成功 —— " + ex.Message);
                return sb.ToString();
            }
            sb.AppendLine("结果：已停用 —— 它不再出现在**任何位置**的右键菜单里了。");
            sb.AppendLine("⚠️ 要**重启资源管理器**才生效（DLL 是 explorer 启动时加载进去的）："
                + "点窗口底栏的「重启资源管理器」就行。");
            sb.AppendLine("后悔了：点底栏「恢复」把黑名单里那一条删掉，再重启一次资源管理器。");
            ok = true;
            Record("停用扩展", e, clsid + " = " + name, "", "成功");
            return sb.ToString();
        }

        /// <summary>恢复一个 DLL 扩展 = 把**用户区**黑名单里那一条删掉（登记键本来就一个字没动）。
        /// 系统区那份有它的话就删不动（那要管理员、而且是别人的东西）—— 如实说明、不假装成功。</summary>
        public static string UnblockExt(CtxEntry e, out bool ok)
        {
            ok = false;
            StringBuilder sb = Head("恢复扩展", e);
            if (!e.Shellex)
            {
                sb.AppendLine("结果：没有处理 —— 这一条不是 DLL 扩展项（静态菜单项用「恢复」）。");
                return sb.ToString();
            }
            if (!e.BlockedUser)
            {
                if (e.BlockedMachine)
                {
                    sb.AppendLine("结果：没有处理 —— 它只写在**系统区**那份黑名单里（" + BlockedMachineRoot + "），"
                        + "那是别的工具或安装程序写的：本工具箱不动系统区（要管理员，而且会影响所有用户）。"
                        + "想让它回来只能去那个工具/安装程序里取消，或者手工删那条值。");
                }
                else
                {
                    sb.AppendLine("结果：没有处理 —— 用户区那份黑名单里本来就没有它。");
                }
                return sb.ToString();
            }
            string clsid = e.ClsidNorm.Length > 0 ? e.ClsidNorm : e.Verb;
            sb.Append("黑名单键：").AppendLine(BlockedUserRoot);
            try
            {
                using (RegistryKey k = OpenBlocked(false))
                {
                    if (k == null)
                    {
                        sb.AppendLine("结果：没成功 —— 那个黑名单键不存在（没什么可删的？）。");
                        return sb.ToString();
                    }
                    k.DeleteValue(clsid, false);
                    if (k.GetValue(clsid, null) != null)
                    {
                        sb.AppendLine("结果：没成功（删完读回它还在）—— 那个键可能不让写。");
                        sb.AppendLine(FailWhy("删不掉黑名单里那一条", e));
                        return sb.ToString();
                    }
                    sb.AppendLine("· 已删掉黑名单里的 " + clsid);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("结果：没成功 —— " + ex.Message);
                return sb.ToString();
            }
            bool stillMachine = e.BlockedMachine;
            sb.AppendLine(stillMachine
                ? "结果：用户区那条删掉了，可**系统区那份还在**（别的工具写的）→ 它还是不会回到菜单里。"
                : "结果：已恢复 —— 它又会出现在**所有位置**的右键菜单里。");
            sb.AppendLine("⚠️ 要**重启资源管理器**才生效：点窗口底栏的「重启资源管理器」就行。");
            ok = !stillMachine;
            Record("恢复扩展", e, clsid, "", ok ? "成功" : "成功（系统区那份还在，所以菜单里仍然没有它）");
            return sb.ToString();
        }

        public static string BlockMany(List<CtxEntry> items, out int done, out bool ok)
        {
            return Many("停用扩展", items, delegate(CtxEntry e, out bool one) { return BlockExt(e, out one); }, out done, out ok);
        }

        public static string UnblockMany(List<CtxEntry> items, out int done, out bool ok)
        {
            return Many("恢复扩展", items, delegate(CtxEntry e, out bool one) { return UnblockExt(e, out one); }, out done, out ok);
        }

        // ------------------------------------------------------------------ 批量
        /// <summary>批量动作的公共骨架：**逐条做、互不影响** —— 一条失败（比如系统区那份写不进去）
        /// 不让别的条跟着失败，最后给一行总计。每条各自记一笔，所以撤销也是逐条的。</summary>
        private static string Many(string action, List<CtxEntry> items,
            CtxWork work, out int done, out bool ok)
        {
            StringBuilder sb = new StringBuilder();
            done = 0;
            ok = false;
            int failed = 0;
            sb.Append("批量").Append(action).Append("：这一次选了 ")
              .Append(items.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 项。").AppendLine();
            foreach (CtxEntry e in items)
            {
                bool one;
                string report = work(e, out one);
                sb.AppendLine(report.TrimEnd());
                sb.AppendLine();
                if (one) { done++; } else { failed++; }
            }
            sb.Append("合计：").Append(items.Count.ToString(CultureInfo.InvariantCulture)).Append(" 项 —— 成功 ")
              .Append(done.ToString(CultureInfo.InvariantCulture)).Append("、失败 ")
              .Append(failed.ToString(CultureInfo.InvariantCulture)).AppendLine("（失败的逐条原因见上）");
            ok = (failed == 0 && done > 0);
            return sb.ToString();
        }

        /// <summary>批量工作的形状（`CtxMenu.Disable` / `Restore` / `Delete` 三选一）。</summary>
        internal delegate string CtxWork(CtxEntry e, out bool ok);

        public static string DisableMany(List<CtxEntry> items, out int done, out bool ok)
        {
            return Many("禁用", items, delegate(CtxEntry e, out bool one) { return Disable(e, out one); }, out done, out ok);
        }

        public static string RestoreMany(List<CtxEntry> items, out int done, out bool ok)
        {
            return Many("恢复", items, delegate(CtxEntry e, out bool one) { return Restore(e, out one); }, out done, out ok);
        }

        public static string DeleteMany(List<CtxEntry> items, out int done, out bool ok)
        {
            return Many("删除", items, delegate(CtxEntry e, out bool one)
            {
                string backup;
                return Delete(e, out backup, out one);
            }, out done, out ok);
        }

        // ------------------------------------------------------------------ 行图标

        private static readonly Dictionary<string, Image> IconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        /// <summary>每一行显示"这一项自己的图标"：注册表 `Icon` 值指的 exe/dll/ico（扩展项就是它那个
        /// DLL）。**抽不到就不画**（返回 null，那一行的图标位留空、各行的文字仍然对齐）——
        /// 2026-10-09 用户看实图提的：原先退回"按段给的内嵌图标"，结果一整页全是同一个绿色小方块，
        /// 比空着还难看。
        /// ⚠️ 抽图标走 `ExtractIconEx`（系统自己的装载器）：**别用 `new Icon(stream, w, h)` 判好坏**
        /// —— 那个重载对任何 .ico 都可能抛异常，本仓库 2026-10-05 就这么误判过一次（PITFALLS 里记着）。</summary>
        public static Image RowIcon(CtxEntry e)
        {
            int index;
            string src = StripIconIndex(e.IconValue, out index);
            if (src.Length > 0)
            {
                string cacheKey = src + "#" + index.ToString(CultureInfo.InvariantCulture);
                lock (IconCache)
                {
                    Image cached;
                    if (IconCache.TryGetValue(cacheKey, out cached)) { return cached; }
                }
                Image got = ExtractIcon(src, index);
                if (got != null)
                {
                    lock (IconCache) { IconCache[cacheKey] = got; }
                    return got;
                }
            }
            return null;
        }

        /// <summary>`&lt;某个 exe 的路径&gt;,0` → 路径 + 序号；`@shell32.dll,-5` 这种间接写法先解开。
        /// 拿不到就返回空串（调用方据此"不画图标"）。</summary>
        private static string StripIconIndex(string value, out int index)
        {
            index = 0;
            string s = ResolveIndirect(value).Trim();
            if (s.Length == 0) { return ""; }
            if (s.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = s.IndexOf('"', 1);
                if (close > 0)
                {
                    string quoted = s.Substring(1, close - 1);
                    string rest = s.Substring(close + 1).TrimStart(',', ' ');
                    int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
                    return AppPaths.Expand(quoted);
                }
            }
            int comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                int parsed;
                if (int.TryParse(s.Substring(comma + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    index = parsed;
                    s = s.Substring(0, comma).Trim();
                }
            }
            s = AppPaths.Expand(s);
            try { if (!File.Exists(s)) { return ""; } }
            catch { return ""; }
            return s;
        }

        private static Image ExtractIcon(string file, int index)
        {
            IntPtr small = IntPtr.Zero;
            try
            {
                IntPtr large = IntPtr.Zero;
                int got = ExtractIconEx(file, index, out large, out small, 1);
                if (got <= 0 || small == IntPtr.Zero) { return null; }
                using (Icon ic = Icon.FromHandle(small))
                {
                    Bitmap bmp = new Bitmap(16, 15);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawIcon(ic, new Rectangle(0, 0, 16, 16));
                    }
                    return bmp;
                }
            }
            catch { return null; }
            finally
            {
                if (small != IntPtr.Zero) { try { DestroyIcon(small); } catch { } }
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW", ExactSpelling = true)]
        private static extern int ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, int count);

        [DllImport("user32.dll", EntryPoint = "DestroyIcon", ExactSpelling = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        // ------------------------------------------------------------------ 只读出口 / 管理员

        public static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        /// <summary>`rightmenu list` 的正文：给人看的行 + 机器可读的 `zone` / `entry` 行
        /// （回归测试认那两行；窗口那一栏的数字也来自这同一个 List）。</summary>
        public static string Report(string zoneId)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("右键菜单管理（只读：这一条命令一个字节都不写）");
            sb.Append("用户区：").AppendLine(UserRoot);
            sb.Append("系统区：").AppendLine(MachineRoot);
            if (IsTestRoot) { sb.AppendLine("（测试根：没有碰真实的右键菜单）"); }
            sb.AppendLine();

            List<CtxEntry> all = List(zoneId);
            foreach (CtxZone z in Table)
            {
                if (zoneId != null && zoneId.Length > 0
                    && !string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase)) { continue; }
                List<CtxEntry> mine = new List<CtxEntry>();
                // ⚠️ 「黑名单残留」那些**不算任何位置的条数**（它们压根不是这一页菜单里的东西、
                // 跟位置无关）—— 只在下面单独列一段。这条线不划清楚，每个位置的条数都会虚高。
                foreach (CtxEntry e in all) { if (e.ZoneId == z.Id && !e.Zombie) { mine.Add(e); } }
                sb.Append('[').Append(z.Label).Append("] ").Append(z.Note).Append(" —— ")
                  .AppendLine(Summary(mine));
                if (mine.Count == 0) { sb.AppendLine("  （什么都没有）"); }
                int n = 0;
                foreach (CtxEntry e in mine)
                {
                    n++;
                    sb.Append("  ").Append(n.ToString(CultureInfo.InvariantCulture)).Append(". ")
                      .Append(e.Title).Append("　[").Append(e.StateLabel).Append(" · ").Append(e.WhereLabel).Append(']');
                    if (e.Command.Length > 0) { sb.Append("　").Append(Shorten(e.Command, 90)); }
                    else if (e.Note.Length > 0) { sb.Append("　").Append(Shorten(e.Note, 90)); }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // 「黑名单残留」：跟位置无关（黑名单是按 CLSID 认的），所以统一列一段，不塞进任何一页。
            List<CtxEntry> zombies = new List<CtxEntry>();
            foreach (CtxEntry e in all) { if (e.Zombie) { zombies.Add(e); } }
            if (zombies.Count > 0)
            {
                sb.Append("[黑名单残留] ").Append(GroupNote(GroupZombie)).Append(" —— ")
                  .AppendLine(Summary(zombies));
                int zn = 0;
                foreach (CtxEntry e in zombies)
                {
                    zn++;
                    sb.Append("  ").Append(zn.ToString(CultureInfo.InvariantCulture)).Append(". ")
                      .Append(e.Title).Append("　[").Append(e.StateLabel).Append(" · ")
                      .Append(e.WhereLabel).Append(']').AppendLine();
                }
                sb.AppendLine();
            }

            sb.AppendLine("（机器可读：zone 与 entry 两行）");
            foreach (CtxZone z in Table)
            {
                if (zoneId != null && zoneId.Length > 0
                    && !string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase)) { continue; }
                List<CtxEntry> mine = new List<CtxEntry>();
                foreach (CtxEntry e in all) { if (e.ZoneId == z.Id && !e.Zombie) { mine.Add(e); } }
                int active = 0, disabled = 0, shellex = 0, machine = 0;
                foreach (CtxEntry e in mine)
                {
                    if (e.Shellex) { shellex++; } else if (e.Disabled) { disabled++; } else { active++; }
                    if (e.InMachine) { machine++; }
                }
                sb.Append("zone\t").Append(z.Id).Append('\t').Append(z.Label).Append('\t')
                  .Append(mine.Count.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(active.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(disabled.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(shellex.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(machine.ToString(CultureInfo.InvariantCulture)).AppendLine();
                foreach (CtxEntry e in mine)
                {
                    // ⚠️ 这一行**只报"这一项住在哪儿"**，不报"当前进程要不要提权"：
                    // NeedsAdmin 还取决于跑这条命令的进程是不是已经提权了（管理员会话里恒为 false），
                    // 拿它当判据的断言会随环境变红 —— 「住在用户区 / 系统区 / 两边都有」才是稳定事实。
                    sb.Append("entry\t").Append(z.Id).Append('\t').Append(e.Shellex ? "shellex" : "verb").Append('\t')
                      .Append(e.Verb).Append('\t').Append(e.Title).Append('\t')
                      .Append(e.StateLabel).Append('\t')
                      .Append(e.InMachine ? (e.InUser ? "both" : "machine") : "user").Append('\t')
                      .Append(e.Protected ? "protected" : "writable").AppendLine();
                }
            }

            // ---- 系统黑名单（机器可读）----------------------------------------------------------
            // 扩展项的开关**不在菜单项那些键里**，而在这份黑名单里（值名 = CLSID，值数据 = 人看的名字）：
            // 上面那些 entry 行说的是"这一项是不是已停用"，这一节说的是"黑名单里到底有什么" ——
            // 包括机器上已经没有对应扩展的残留条目（卸载残留）。回归测试认这几行。
            Dictionary<string, BlockedHit> bl = ReadBlocked();
            sb.Append("blockeduser\t").Append(BlockedUserRoot).AppendLine();
            sb.Append("blockedmachine\t").Append(BlockedMachineRoot).AppendLine();
            Dictionary<string, bool> registered = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            CollectRegisteredClsids(registered);
            List<string> ids = new List<string>(bl.Keys);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            int orphan = 0;
            foreach (string id in ids)
            {
                BlockedHit h = bl[id];
                bool live = registered.ContainsKey(id);
                if (!live) { orphan++; }
                sb.Append("blocked\t").Append(id).Append('\t')
                  .Append(h.InUser ? (h.InMachine ? "both" : "user") : "machine").Append('\t')
                  .Append(live ? "registered" : "orphan").Append('\t')
                  .Append(h.Name).AppendLine();
            }
            sb.Append("blockedcount\t").Append(bl.Count.ToString(CultureInfo.InvariantCulture))
              .Append('\t').Append(orphan.ToString(CultureInfo.InvariantCulture)).AppendLine();
            return sb.ToString();
        }

        private static string Shorten(string s, int max)
        {
            string t = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length <= max) { return t; }
            return t.Substring(0, max) + "…";
        }
    }
}
