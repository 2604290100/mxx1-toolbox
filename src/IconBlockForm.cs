// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「修复桌面图标黑块」窗口：一块只读的现状报告 + **两种修法**各一颗按钮。
    ///
    /// 用户 2026-10-26 的原话：「修改【修复桌面图标黑块】不能只是换回箭头，而是要兼容一下，
    /// 要 2 种方法：一个去图标后的修复方法、一个修改回箭头的方法」——
    /// 所以那颗按钮不再直接删值，而是开这个窗口让他挑：
    ///   ·「保持无箭头 · 只治黑块」= 小箭头继续不显示，把 `Shell Icons\29` 换成**系统自带的空白图标
    ///     imageres.dll,197**（2026-10-26 实机验证：`shell32.dll,-50` 和自生成的透明 ico 文件在这台
    ///     机器上都会被画成黑块，只有 dll 资源索引这条路干净 —— 无黑块、无小箭头）；
    ///   ·「恢复系统默认小箭头」= 把两处 `29` 删掉，小箭头回到系统默认样子。
    ///
    /// 三条口径（和「文件关联体检」/「合盖与电源键动作」一致）：
    /// ① 点开这个窗口**什么都不改**，只读一遍注册表给你看，所以这里不弹确认框；
    /// ② 两条修法每一次都过条款门 + 确认框；不是管理员时弹一次 UAC 带管理员重开窗口，
    ///    重开之后要再点一次；
    /// ③ 写之前先把原值记到磁盘（记不下来就一个字都不改），所以「还原修复前图标」永远点得动。</summary>
    internal sealed class IconBlockForm : Mxx1Form
    {
        private readonly Theme _theme;

        private readonly TextBox _report = new TextBox();
        private readonly Label _status = new Label();
        private readonly Button _keep = new Button();
        private readonly Button _arrow = new Button();
        private readonly Button _refresh = new Button();

        private volatile bool _busy = false;

        public IconBlockForm(Theme theme)
        {
            _theme = theme;
            Text = "修复桌面图标黑块";
            ClientSize = new Size(880, 600);
            MinimumSize = new Size(760, 500);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 报告区吃掉剩下的高度
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(830, 0);
            intro.Margin = new Padding(2, 0, 2, 6);
            intro.Text = "桌面 / 快捷方式图标左下角那块黑方块，是「Shell Icons\\29」里那个自定义图标值画出来的。"
                + "这个窗口给你两种修法 —— 想继续不显示小箭头，就点「保持无箭头 · 只治黑块」；"
                + "想干脆把小箭头要回来，就点「恢复系统默认小箭头」。"
                + "两种都会先把原值记下来，随时能点外面那颗「还原修复前图标」退回原样。";
            root.Controls.Add(intro, 0, 0);

            Label cap = new Label();
            cap.AutoSize = true;
            cap.Margin = new Padding(2, 2, 2, 2);
            cap.Text = "现在的样子（只读；点开这个窗口什么都没改）：";
            root.Controls.Add(cap, 0, 1);

            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Both;
            _report.WordWrap = false;
            _report.Dock = DockStyle.Fill;
            _report.Height = 340;
            _report.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_report, 0, 2);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(830, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 3);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _keep.Text = "保持无箭头 · 只治黑块";
            StyleButton(_keep, 176);
            _keep.Click += delegate { DoFix(true); };
            _arrow.Text = "恢复系统默认小箭头";
            StyleButton(_arrow, 156);
            _arrow.Click += delegate { DoFix(false); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 84);
            _refresh.Click += delegate { RefreshState(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_keep, 1, 0);
            buttons.Controls.Add(_arrow, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 4);

            Label hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(830, 0);
            hint.Margin = new Padding(2, 0, 2, 0);
            hint.Text = "两条修法都要管理员权限，写完都会自动重启一次资源管理器（桌面会闪一下，是正常的）。"
                + "不是管理员时点下去会弹一次 UAC，重开这个窗口之后要再点一次才会生效。";
            root.Controls.Add(hint, 0, 5);

            Controls.Add(root);
            CancelButton = close;
            KeyPreview = true;
            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5) { RefreshState(); e.Handled = true; }
            };

            ApplyTheme(theme);
            // ⚠️ 构造函数里**不许**先 SetBusyUi(true)：RefreshState() 开头就是 `if (_busy) return;`，
            // 会把自己挡掉、窗口定格在"忙"上（坑 78/79 那次「鼠标移上去一直转圈」就是这么来的）。
            Load += delegate { RefreshState(); };
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Width = width;
            b.Height = 30;
            b.Margin = new Padding(4, 2, 0, 2);
        }

        private void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            foreach (Control c in Controls) { c.BackColor = theme.FormBack; }
            ApplyThemeRecursive(this, theme);
        }

        private static void ApplyThemeRecursive(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = theme.FormBack;
                Label l = c as Label;
                if (l != null) { l.ForeColor = theme.BarText; }
                TextBox t = c as TextBox;
                if (t != null)
                {
                    t.BackColor = t.ReadOnly ? theme.LogBack : theme.InputBack;
                    t.ForeColor = t.ReadOnly ? theme.LogText : theme.InputText;
                }
                Button b = c as Button;
                if (b != null)
                {
                    b.BackColor = theme.ButtonBack;
                    b.ForeColor = theme.ButtonText;
                    b.FlatAppearance.BorderColor = theme.ButtonBorder;
                    b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                }
                if (c.Controls.Count > 0) { ApplyThemeRecursive(c, theme); }
            }
        }

        // ------------------------------------------------------------------ 读

        private void RefreshState()
        {
            if (_busy) { return; }
            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在读注册表里的 Shell Icons\\29…";
            Application.DoEvents();
            try
            {
                _report.Text = SysReg.IconBlockReport();
                _status.Text = "上面就是现在的样子：挑一种修法点下去就行（两种都要管理员）。";
            }
            catch (Exception ex)
            {
                _report.Text = "读不了这台机器的 Shell Icons\\29。" + Environment.NewLine
                    + "  " + ex.Message + Environment.NewLine + Environment.NewLine
                    + "  （读的是 HKCU / HKLM 的 SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Shell Icons；"
                    + "读不到多半是系统被精简 / 权限被限制。）";
                _status.Text = "读不到现在的样子，这一页修不了。";
                Logger.Write("修复图标黑块", "读失败：" + ex.Message);
            }
            SetBusyUi(false);
        }

        private void SetBusyUi(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _keep.Enabled = !busy;
            _arrow.Enabled = !busy;
            _refresh.Enabled = !busy;
        }

        private void Flash(string text)
        {
            _status.Text = text;
            _status.ForeColor = _theme.Danger;
        }

        // ------------------------------------------------------------------ 两种修法

        /// <summary>keepHidden = 小箭头继续隐藏（写空白图）/ false = 恢复系统默认小箭头（删值）。</summary>
        private void DoFix(bool keepHidden)
        {
            if (_busy) { return; }
            string what = keepHidden ? "保持无箭头" : "恢复系统默认小箭头";
            if (!Consent.EnsureAccepted(this, _theme, "要改动系统的「" + what + "」"))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            StringBuilder body = new StringBuilder();
            if (keepHidden)
            {
                body.AppendLine("会把 HKCU 与 HKLM 两处的 Shell Icons\\29 换成系统自带的空白图标（imageres.dll,197）：");
                body.AppendLine("  · 小箭头**继续不显示**（和「去掉小箭头」一样）；");
                body.AppendLine("  · 换掉的是那个会被画成黑方块的值 —— 桌面上的黑块应该就没了。");
            }
            else
            {
                body.AppendLine("会把 HKCU 与 HKLM 两处的 Shell Icons\\29 删掉：");
                body.AppendLine("  · 小箭头回到系统默认样子；");
                body.AppendLine("  · 桌面上的黑块也没了。");
            }
            body.AppendLine();
            body.AppendLine("要动的是注册表，需要管理员权限；改之前会先把原值记到：" + SysReg.BackupPath);
            body.AppendLine("写完之后会一处一处读回来核对，然后自动重启一次资源管理器（桌面会闪一下）。");
            body.AppendLine("想退回原样，点外面那颗「还原修复前图标」（按刚才记下的原值写回去）。");
            using (ConfirmForm f = new ConfirmForm("要这样修桌面图标吗？", body.ToString(), "开始修复", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            if (!Launcher.IsAdmin())
            {
                OfferElevation(what);
                return;
            }

            SetBusyUi(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在改（写完还要重启一次资源管理器）…";
            Application.DoEvents();
            Logger.Write("修复图标黑块", what + "：开始（" + (keepHidden ? "写 imageres.dll,197" : "删 29") + "）");
            bool ok;
            string report = SysReg.FixIconBlock(keepHidden, out ok);
            Finish(report, ok, what);
        }

        private void Finish(string report, bool ok, string what)
        {
            if (IsDisposed) { return; }
            SetBusyUi(false);
            StringBuilder sb = new StringBuilder();
            sb.Append(report);
            sb.AppendLine();
            sb.AppendLine();
            // 报告后面再念一遍"现在的样子"：用户一眼就能看到值是不是真的换了。
            try { sb.Append(SysReg.IconBlockReport()); }
            catch (Exception ex) { sb.Append("（现在的样子读不出来：" + ex.Message + "）"); }
            _report.Text = sb.ToString();

            _status.ForeColor = ok ? _theme.BarText : _theme.Danger;
            _status.Text = ok
                ? (what + "完成 —— 结果在上面的报告里；桌面应该已经重画过了。")
                : (what + "没全部成功 —— 上面带 × 的就是没成的那些（多半是缺管理员权限）。");
            Logger.Write("修复图标黑块", (ok ? "" : "部分失败 · ") + what + "：" + report.Trim());
        }

        // ------------------------------------------------------------------ 不是管理员

        /// <summary>不是管理员时的两条路（同「文件关联体检」/「合盖与电源键动作」）：说清楚 →
        /// 弹一次 UAC 带管理员重开这个窗口 → 重开之后**要再点一次**。
        /// 命令行那条 `iconfix` 只开窗口，不带任何"要改什么"的参数。</summary>
        private void OfferElevation(string what)
        {
            string body = "改 Shell Icons\\29（HKCU 与 HKLM 两处）要用管理员权限才行。" + Environment.NewLine
                + Environment.NewLine
                + "点「确定」之后：会弹一个 UAC 窗口，请点「是」；这个窗口会关掉，然后带管理员权限重新打开。"
                + "因为写注册表只能由你在窗口里点出来，所以重开之后要再点一次「" + what + "」才会真的生效。";
            using (ConfirmForm f = new ConfirmForm("需要管理员权限", body, "确定", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath, "iconfix");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                Logger.Write("修复图标黑块", "不是管理员：已请求以管理员身份重开窗口");
                Close();
            }
            catch (Exception ex)
            {
                Flash("请求管理员权限失败：" + ex.Message);
                Logger.Write("修复图标黑块", "请求管理员权限失败：" + ex.Message);
            }
        }
    }
}
