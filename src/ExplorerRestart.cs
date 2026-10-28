// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;

namespace Mxx1Toolbox
{
    /// <summary>「要重启资源管理器才生效」的功能共用的一条路。
    ///
    /// 用户 2026-10-20 的要求原话：「需要重启资源管理器的功能，要自动调用」——
    /// 点了开关就当场重启，不要让人再自己去找那一个按钮（不然会以为"点了没反应"）。
    ///
    /// 三条口径：
    /// ① 命令**只有一个来源** —— `tools\common.json` 里的 `restart-explorer` 那个按钮；
    ///    这里照 id 把它找出来、走 `Launcher.Run` 跑，**绝不重抄一遍** `taskkill /f /im explorer.exe`
    ///    （和右键菜单管理那条路是同一份命令，见 CtxMenuForm.DoRestartExplorer）。
    /// ② 这里只负责"真去做"，**不弹确认框** —— 调用方负责"用户已经同意"这件事
    ///    （点了带确认的按钮，或者点的那个开关自己在提示里写明了会自动重启）。
    /// ③ `MXX1_NO_EXPLORER_RESTART=1` 时只写日志、不真做 —— 回归测试点这些按钮的时候
    ///    绝不许把用户的 explorer 杀掉（和右键菜单管理那条同一个开关）。</summary>
    internal static class ExplorerRestart
    {
        /// <summary>那个「重启资源管理器」按钮的 id（命令的唯一来源）。</summary>
        public const string ToolId = "restart-explorer";

        /// <summary>测试开关：`MXX1_NO_EXPLORER_RESTART=1` 时只写日志。</summary>
        public static bool Disabled
        {
            get
            {
                string v = Environment.GetEnvironmentVariable("MXX1_NO_EXPLORER_RESTART");
                return (v != null) && (v.Trim() == "1");
            }
        }

        /// <summary>那个按钮会执行的命令原文（确认框 / 报告里给用户看的那一行）。</summary>
        public static string CommandText()
        {
            ToolItem t = Find();
            if (t == null) { return ""; }
            try { return Launcher.DescribeCommand(t, Settings.Load(), false); }
            catch { return ""; }
        }

        /// <summary>真的去重启一次。返回 true = 做到了（或者处在测试模式里"该做的那一步"跳过了）。
        /// <paramref name="message"/> 是一句可以直接贴进报告的人话。</summary>
        public static bool Run(string reason, out string message)
        {
            ToolItem t = Find();
            if (t == null)
            {
                message = "工具清单里找不到「重启资源管理器」那个按钮（tools\\common.json 被改过？）"
                    + " —— 资源管理器没有重启，手动重启一次（或注销）就有变化。";
                return false;
            }
            if (Disabled)
            {
                string cmd = CommandText();
                Logger.Write("资源管理器", "（测试模式，没有真重启）" + reason + "：" + cmd);
                message = "（测试模式 MXX1_NO_EXPLORER_RESTART=1：没有真重启资源管理器，只写进了日志）";
                return true;
            }

            LaunchResult r = Launcher.Run(t, Settings.Load(), false, false);
            bool ok = (r != null) && r.Ok && !r.Deferred;
            Logger.Write("资源管理器", "自动重启（" + reason + "）："
                + ((r == null) ? "（没有结果）" : r.Message));
            message = ok
                ? "资源管理器已经自动重启（任务栏 / 桌面会闪一下）—— 改动这下就生效了。"
                : "资源管理器没能自动重启（细节见日志）—— 手动点一次「重启资源管理器」，或者注销一次。";
            return ok;
        }

        /// <summary>按 id 从工具清单里找那一个按钮（找不到返回 null）。</summary>
        private static ToolItem Find()
        {
            try
            {
                List<string> warnings = new List<string>();
                foreach (ToolItem t in ToolRegistry.LoadAll(warnings))
                {
                    if (string.Equals(t.Id, ToolId, StringComparison.OrdinalIgnoreCase)) { return t; }
                }
            }
            catch { }
            return null;
        }
    }
}
