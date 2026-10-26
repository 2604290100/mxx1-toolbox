// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「关掉系统代理」窗口（2026-10-23 的 Q1）。
    ///
    /// 三条口径（和后端 `src\ProxyConfig.cs` 一一对应）：
    /// ① 只关**浏览器用的那一层**（WinINET）——不用管理员；系统级（WinHTTP）只原样显示，不动它；
    /// ② 点「关掉系统代理」要过《使用条款》同意门 + 确认框，改之前的原值会记下来，随时能恢复；
    /// ③ 窗口一打开就把"现在是什么样"读出来写清楚（代理开着没开、PAC、WPAD、本机端口通不通），
    ///    读完之前不假装"已经好了"。
    ///
    /// ⚠️ 构造函数里**不许**先把自己设成"忙" —— PowerForm 那次「鼠标移上去一直转圈」就是
    /// 构造函数先 SetBusy(true)，而 Load 里的读取一开头 `if (_busy) return;` 把自己挡掉了，
    /// 窗口永远定格在转圈 + 全灰（见 PITFALLS 坑 78）。</summary>
    internal sealed class ProxyForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly TextBox _report = new TextBox();
        private readonly Label _status = new Label();
        private readonly Button _off = new Button();
        private readonly Button _restore = new Button();
        private readonly Button _winSettings = new Button();
        private readonly Button _refresh = new Button();

        private volatile bool _busy = false;
        private ProxyState _state = null;

        public ProxyForm(Theme theme)
        {
            _theme = theme;
            Text = "关掉系统代理（网页打不开时用）";
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(720, 440);
            MinimizeBox = true;   // 最小化按钮：点它把主窗口一起收进任务栏（2026-10-26 主副窗口关联）
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(780, 0);
            intro.Text = "网页全都打不开、但 QQ / 微信还能收消息 —— 多半是代理软件退出时没把系统代理关掉，"
                + "浏览器还在往一个已经不存在的代理上跑。"
                + Environment.NewLine
                + "这里只关「浏览器用的那一层」（系统代理服务器 / 自动配置脚本），不用管理员；"
                + "改之前的原值会记下来，点「恢复上次的代理设置」就能回去。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            _report.Multiline = true;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Vertical;
            _report.WordWrap = true;
            _report.BorderStyle = BorderStyle.FixedSingle;
            _report.Dock = DockStyle.Fill;
            _report.Margin = new Padding(2, 0, 2, 6);
            _report.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            root.Controls.Add(_report, 0, 1);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(780, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 6;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 5; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _off.Text = "关掉系统代理";
            StyleButton(_off, 116);
            _off.Click += delegate { TurnOff(); };
            _restore.Text = "恢复上次的代理设置";
            StyleButton(_restore, 148);
            _restore.Click += delegate { RestoreSaved(); };
            _winSettings.Text = "Windows 的代理页";
            StyleButton(_winSettings, 128);
            _winSettings.Click += delegate { OpenWindowsProxyPage(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 76);
            _refresh.Click += delegate { Reload(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 70);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_winSettings, 1, 0);
            buttons.Controls.Add(_refresh, 2, 0);
            buttons.Controls.Add(_restore, 3, 0);
            buttons.Controls.Add(_off, 4, 0);
            buttons.Controls.Add(close, 5, 0);
            root.Controls.Add(buttons, 0, 3);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate { Reload(); };
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Height = 26;
            b.Width = width;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(6, 2, 0, 2);
            b.TabStop = false;
        }

        private void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            ApplyThemeRecursive(this, theme);
        }

        private static void ApplyThemeRecursive(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = theme.FormBack;
                Label l = c as Label;
                if (l != null) { l.ForeColor = theme.BarText; }
                TextBox tb = c as TextBox;
                if (tb != null)
                {
                    tb.BackColor = theme.LogBack;
                    tb.ForeColor = theme.LogText;
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

        private void Reload()
        {
            if (_busy) { return; }
            SetBusy(true, "正在读系统代理的设置 …");
            Thread t = new Thread(delegate ()
            {
                ProxyState st;
                try { st = ProxyConfig.Read(); }
                catch (Exception ex)
                {
                    st = new ProxyState();
                    st.Error = "读系统代理的设置时出错了：" + ex.Message;
                }
                try { BeginInvoke((MethodInvoker)delegate { Render(st); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Render(ProxyState st)
        {
            if (IsDisposed) { return; }
            SetBusy(false, "");
            _state = st;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("现在的状态");
            if (st.Error.Length > 0)
            {
                sb.AppendLine("  " + st.Error);
            }
            else
            {
                sb.AppendLine("  系统代理：" + st.Describe());
                sb.AppendLine("  自动配置脚本（PAC）：" + (st.Pac.Length > 0 ? st.Pac : "没有"));
                sb.AppendLine("  自动检测设置（WPAD）：" + (st.AutoDetect ? "开着" : "关着"));
                if (st.Probe.Length > 0) { sb.AppendLine("  本机代理端口：" + st.Probe); }
            }
            sb.AppendLine("");
            sb.AppendLine("系统级（WinHTTP）代理（netsh 原样显示，只有管理员能改，这个按钮不动它）");
            if (st.WinHttp.Length > 0)
            {
                foreach (string line in st.WinHttp.Replace("\r\n", "\n").Split('\n'))
                {
                    string t = line.Trim();
                    if (t.Length > 0) { sb.AppendLine("  " + t); }
                }
            }
            else
            {
                sb.AppendLine("  （读不出来。这一项不影响浏览器，Edge / Chrome 用的是上面那一层）");
            }
            sb.AppendLine("");
            sb.AppendLine("上次关掉之前记下的设置");
            if (st.HasBackup)
            {
                string sum = ProxyConfig.BackupSummary();
                sb.AppendLine("  " + (sum.Length > 0 ? sum : "（记录在，但里面没有内容）"));
                sb.AppendLine("  记录文件：" + ProxyConfig.BackupPath());
            }
            else
            {
                sb.AppendLine("  还没有记录（这个工具还没关过代理）。第一次点「关掉系统代理」时会自动记下来。");
            }
            _report.Text = sb.ToString();

            if (st.Error.Length > 0)
            {
                _status.Text = st.Error;
            }
            else if (!st.Enabled && st.Pac.Length == 0 && !st.AutoDetect)
            {
                _status.Text = "读完了：现在没有开系统代理（浏览器直连），不用关。"
                    + (st.HasBackup ? "想回到以前记下的设置，就点「恢复上次的代理设置」。" : "");
            }
            else
            {
                _status.Text = "读完了：现在是「" + st.Describe() + "」。点「关掉系统代理」之前会问你一次，改之前的原值会记下来。";
            }
            UpdateButtons();
            Logger.Write("关掉系统代理", "读到：" + (st.Error.Length > 0 ? st.Error : st.Describe()));
        }

        private void UpdateButtons()
        {
            bool has = (_state != null) && (_state.Error.Length == 0);
            bool needsOff = has && (_state.Enabled || _state.Pac.Length > 0 || _state.AutoDetect);
            _off.Enabled = !_busy && needsOff;
            _restore.Enabled = !_busy && (_state != null) && _state.HasBackup;
            _refresh.Enabled = !_busy;
            _winSettings.Enabled = !_busy;
        }

        // ------------------------------------------------------------------ 写

        private void TurnOff()
        {
            if (_busy || _state == null) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要把系统代理关掉（" + _state.Describe() + "）"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次什么都没改。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.AppendLine("现在：" + _state.Describe());
            if (_state.Probe.Length > 0) { body.AppendLine(_state.Probe); }
            body.AppendLine();
            body.AppendLine("要做的：把「使用代理服务器」和「自动配置脚本」关掉，浏览器改成直连。");
            body.AppendLine("改之前的原值会记下来，随时能点「恢复上次的代理设置」写回去。");
            body.AppendLine();
            body.AppendLine("注意：如果代理软件还开着，它可能过一会儿又给你设回来 —— 那就再点一次，"
                + "或者先把代理软件退干净。");
            using (ConfirmForm f = new ConfirmForm("要关掉系统代理吗？", body.ToString(), "关掉代理", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在关系统代理 …");
            Logger.Write("关掉系统代理", "开始：现在 " + _state.Describe());
            Thread t = new Thread(delegate ()
            {
                string message;
                bool ok;
                try { ok = ProxyConfig.Off(out message); }
                catch (Exception ex) { ok = false; message = "关代理时出错了：" + ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate { Finish("关掉系统代理", ok, message); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void RestoreSaved()
        {
            if (_busy || _state == null) { return; }
            if (!Consent.EnsureAccepted(this, _theme, "要按上次记下的原值恢复系统代理"))
            {
                _status.Text = "没有同意《免责声明与服务条款》，这次什么都没改。";
                return;
            }

            string sum = ProxyConfig.BackupSummary();
            StringBuilder body = new StringBuilder();
            body.AppendLine("上次关掉之前：");
            body.AppendLine("  " + (sum.Length > 0 ? sum : "（记录里没有内容）"));
            body.AppendLine();
            body.AppendLine("点「恢复」会按这份记录写回去（包括「开 / 关」本身）。");
            body.AppendLine("如果你现在已经在别的地方设了新的代理，恢复会把那些设置盖掉。");
            using (ConfirmForm f = new ConfirmForm("要恢复上次的代理设置吗？", body.ToString(), "恢复", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true, "正在恢复上次的代理设置 …");
            Logger.Write("关掉系统代理", "开始恢复上次的原值");
            Thread t = new Thread(delegate ()
            {
                string message;
                bool ok;
                try { ok = ProxyConfig.Restore(out message); }
                catch (Exception ex) { ok = false; message = "恢复时出错了：" + ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate { Finish("恢复上次的代理设置", ok, message); }); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Finish(string what, bool ok, string message)
        {
            if (IsDisposed) { return; }
            SetBusy(false, "");
            _status.Text = message;
            Logger.Write(what, (ok ? "成功：" : "失败：") + message);
            Reload();
        }

        private void OpenWindowsProxyPage()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("ms-settings:network-proxy");
                psi.UseShellExecute = true;
                Process.Start(psi);
                _status.Text = "已打开 Windows 自己的代理设置页（「手动设置代理」就在里面）。";
                Logger.Write("关掉系统代理", "打开 ms-settings:network-proxy");
            }
            catch (Exception ex)
            {
                _status.Text = "打不开 Windows 的代理设置页（" + ex.Message + "）。这台系统可能是 Win7，"
                    + "可以在「控制面板 → Internet 选项 → 连接 → 局域网设置」里看。";
                Logger.Write("关掉系统代理", "打开 ms-settings:network-proxy 失败：" + ex.Message);
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (text.Length > 0) { _status.Text = text; }
            UpdateButtons();
        }
    }
}
