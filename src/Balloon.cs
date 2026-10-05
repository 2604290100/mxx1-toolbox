// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>右下角那个系统通知（托盘气泡）。
    ///
    /// 为什么要有它：「一键解除占用」是**不弹窗口**的（用户 2026-10-05 要的），可是"点了什么都
    /// 不发生"同样不行 —— 用户 2026-10-04 就报过「点击确认以后也没有成功或者失败的反馈」。
    /// 气泡是这里最合适的东西：不抢焦点、不用点、几秒后自己消失，点一下还能开那个详细窗口。
    ///
    /// 两个已知边界（写在这里，免得以后当成 bug 查）：
    ///   ① 它需要在托盘里**短暂出现一个图标**（Shell_NotifyIcon 的规矩），所以任务栏右下角会
    ///      闪一下图标 —— 这是系统通知的标准做法，不是"工具箱常驻托盘"；
    ///   ② 用户关掉系统通知（Win10 / Win11 的专注助手、通知设置）时气泡可能看不到；
    ///      那种情况下动作已经做完了、结果也写进日志了，不会丢。
    ///
    /// 前置条件：STA + Application.EnableVisualStyles()（Program.Main 开头已经做了）。</summary>
    internal static class Balloon
    {
        /// <summary>弹一个气泡，等它显示完（最长 ms 毫秒）再撤掉图标、返回。</summary>
        public static void Show(string title, string text, int ms)
        {
            try
            {
                using (NotifyIcon tray = new NotifyIcon())
                {
                    Icon icon = AppIcon.ForTray();
                    tray.Icon = (icon == null) ? SystemIcons.Information : icon;
                    tray.Text = "萌新工具箱";
                    tray.Visible = true;
                    tray.BalloonTipTitle = (title == null) ? "" : title;
                    tray.BalloonTipText = Clip(text, 250);   // 系统对气泡正文的长度有限制
                    tray.BalloonTipIcon = ToolTipIcon.Info;
                    tray.ShowBalloonTip(ms);

                    // 必须有消息循环：ShowBalloonTip 之后马上退出进程的话，图标和气泡会立刻消失。
                    Timer timer = new Timer();
                    timer.Interval = (ms < 1000) ? 1000 : ms;
                    timer.Tick += delegate { timer.Stop(); Application.ExitThread(); };
                    timer.Start();
                    Application.Run();
                    timer.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Write("气泡提示", "没弹出来（不影响已经做完的事）：" + ex.Message);
            }
        }

        private static string Clip(string s, int max)
        {
            if (s == null) { return ""; }
            s = s.Replace("\r\n", " ").Replace("\n", " ").Trim();
            return (s.Length <= max) ? s : (s.Substring(0, max - 1) + "…");
        }
    }
}
