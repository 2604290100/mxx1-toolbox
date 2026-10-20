// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「定时关机 / 重启 / 睡眠」窗口（常用设置 →「电源与笔记本」段，2026-10-20 的 F4）。
    ///
    /// 窗口只管"定哪一条"和"撤掉"：真正的计时在 `src\PowerTimer.cs` 里（关机 / 重启交给 Windows
    /// 自己的倒计时，睡眠靠工具箱自己的一个后台小进程）—— 所以**工具箱关了照样执行**，
    /// 这件事在窗口里、确认框里、状态行里都写明了（不然用户会以为"关了窗口就没了"）。
    ///
    /// 三条口径（和 `PowerForm` / `CtxMenuForm` 一致）：
    /// ① 会改动系统的动作先过条款同意门 + 确认框（确认框里写明"不会替你保存文档"）；
    /// ② 定下去之后随时能撤（「取消定时」= 停掉后台进程 + `shutdown /a` 双保险）；
    /// ③ 状态行每半秒照记录文件重算一遍 —— 过了点而没看到执行的，会如实标成"没执行"，
    ///    不会一直显示"还在计时"骗人。
    ///
    /// ⚠️ 这个窗口**不需要管理员**（当前用户自己就能关机 / 睡眠），所以没有 `PowerForm` 那套提权重开。</summary>
    internal sealed class TimerForm : Mxx1Form
    {
        private readonly Theme _theme;

        private readonly RadioButton _actShutdown = new RadioButton();
        private readonly RadioButton _actRestart = new RadioButton();
        private readonly RadioButton _actSleep = new RadioButton();

        private readonly RadioButton _modeMinutes = new RadioButton();
        private readonly RadioButton _modeClock = new RadioButton();
        private readonly NumericUpDown _minutes = new NumericUpDown();
        private readonly DateTimePicker _clock = new DateTimePicker();
        private readonly Button _quick15 = new Button();
        private readonly Button _quick30 = new Button();
        private readonly Button _quick60 = new Button();

        private readonly Label _status = new Label();
        private readonly TextBox _result = new TextBox();
        private readonly Button _start = new Button();
        private readonly Button _cancel = new Button();
        private readonly Button _refresh = new Button();
        private readonly System.Windows.Forms.Timer _tick = new System.Windows.Forms.Timer();

        private bool _busy = false;

        public TimerForm(Theme theme)
        {
            _theme = theme;
            Text = "定时关机 / 重启 / 睡眠";
            ClientSize = new Size(760, 470);
            MinimumSize = new Size(680, 420);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 2;
            root.RowCount = 8;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 0 说明
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 1 动作
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 2 多少分钟后
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 3 指定时间
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 4 现在
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 5 上一次的结果
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 6 小字提醒
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 7 按钮

            Label intro = new Label();
            intro.AutoSize = true;
            intro.MaximumSize = new Size(710, 0);
            intro.Text = "到点自动关机、重启或者睡眠。关机和重启的倒计时交给 Windows 自己记着，"
                + "睡眠由工具箱自己的一个后台小进程等着 —— 所以工具箱关掉、缩进托盘都不影响"
                + "（这一页不需要管理员权限）。定错了随时点「取消定时」就能撤掉。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);
            root.SetColumnSpan(intro, 2);

            // ---- 到点做什么 ----
            FlowLayoutPanel acts = new FlowLayoutPanel();
            acts.AutoSize = true;
            acts.WrapContents = true;
            acts.Margin = new Padding(0);
            _actShutdown.Text = "关机";
            _actRestart.Text = "重启";
            _actSleep.Text = "睡眠";
            _actShutdown.Checked = true;
            foreach (RadioButton r in new RadioButton[] { _actShutdown, _actRestart, _actSleep })
            {
                r.AutoSize = true;
                r.Margin = new Padding(2, 2, 18, 2);
                acts.Controls.Add(r);
            }
            root.Controls.Add(RowLabel("到点做什么："), 0, 1);
            root.Controls.Add(acts, 1, 1);

            // ---- 多少分钟以后 ----
            FlowLayoutPanel mins = new FlowLayoutPanel();
            mins.AutoSize = true;
            mins.WrapContents = true;
            mins.Margin = new Padding(0);
            _modeMinutes.Text = "多少分钟以后";
            _modeMinutes.AutoSize = true;
            _modeMinutes.Checked = true;
            _modeMinutes.Margin = new Padding(2, 4, 10, 2);
            _minutes.Minimum = 1;
            _minutes.Maximum = PowerTimer.MaxMinutes;
            _minutes.Value = 30;
            _minutes.Width = 66;
            _minutes.Margin = new Padding(2, 2, 4, 2);
            Label unit = new Label();
            unit.AutoSize = true;
            unit.Text = "分钟";
            unit.Margin = new Padding(0, 6, 12, 2);
            mins.Controls.Add(_modeMinutes);
            mins.Controls.Add(_minutes);
            mins.Controls.Add(unit);
            SetupQuick(_quick15, "15 分钟", 15, mins);
            SetupQuick(_quick30, "30 分钟", 30, mins);
            SetupQuick(_quick60, "1 小时", 60, mins);
            root.Controls.Add(RowLabel("多久以后："), 0, 2);
            root.Controls.Add(mins, 1, 2);

            // ---- 指定时间 HH:mm ----
            FlowLayoutPanel ats = new FlowLayoutPanel();
            ats.AutoSize = true;
            ats.WrapContents = true;
            ats.Margin = new Padding(0);
            _modeClock.Text = "指定时间";
            _modeClock.AutoSize = true;
            _modeClock.Margin = new Padding(2, 6, 10, 2);
            _clock.Format = DateTimePickerFormat.Custom;
            _clock.CustomFormat = "HH:mm";
            _clock.ShowUpDown = true;
            _clock.Width = 84;
            _clock.Margin = new Padding(2, 3, 8, 2);
            _clock.Value = RoundToMinute(DateTime.Now.AddHours(1));
            Label clockHint = new Label();
            clockHint.AutoSize = true;
            clockHint.MaximumSize = new Size(380, 0);
            clockHint.Text = "（今天这个点已经过了，就定到明天同一时间；差半分钟以内也算过了）";
            clockHint.Margin = new Padding(0, 6, 2, 2);
            ats.Controls.Add(_modeClock);
            ats.Controls.Add(_clock);
            ats.Controls.Add(clockHint);
            root.Controls.Add(new Label(), 0, 3);
            root.Controls.Add(ats, 1, 3);

            // ---- 现在 ----
            _status.AutoSize = true;
            _status.MaximumSize = new Size(640, 0);
            _status.Text = "现在没有定时任务";
            _status.Margin = new Padding(2, 4, 2, 6);
            root.Controls.Add(RowLabel("现在："), 0, 4);
            root.Controls.Add(_status, 1, 4);

            // ---- 上一次的结果 ----
            _result.Multiline = true;
            _result.ReadOnly = true;
            _result.ScrollBars = ScrollBars.Vertical;
            _result.WordWrap = true;
            _result.Dock = DockStyle.Fill;
            _result.Height = 130;
            _result.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(RowLabel("上一次的结果："), 0, 5);
            root.Controls.Add(_result, 1, 5);

            Label note = new Label();
            note.AutoSize = true;
            note.MaximumSize = new Size(710, 0);
            note.Text = "提醒：关机 / 重启不会替你保存文档 —— 到点前请把手上的东西存好。"
                + "命令行也能撤：Mxx1Toolbox.exe timer status / timer cancel（开始计时只能在界面里点）。";
            note.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(note, 0, 6);
            root.SetColumnSpan(note, 2);

            // ---- 按钮 ----
            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _start.Text = "开始计时";
            StyleButton(_start, 116);
            _start.Click += delegate { DoStart(); };
            _cancel.Text = "取消定时";
            StyleButton(_cancel, 100);
            _cancel.Click += delegate { DoCancel(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 76);
            _refresh.Click += delegate { RefreshState(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_start, 1, 0);
            buttons.Controls.Add(_cancel, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 7);
            root.SetColumnSpan(buttons, 2);

            Controls.Add(root);
            CancelButton = close;

            // ⚠️ 两个"时间模式"单选按钮分别在**两个不同的容器**里（分钟那一行 / 指定时间那一行），
            // WinForms 只会在同一个容器里自动互斥 —— 所以这一对要自己盯着对方（不然两个能同时选上，
            // 而 ChosenAt() 只看 _modeClock，用户会看到"选了分钟却按指定时间算"）。
            _modeMinutes.CheckedChanged += delegate
            {
                if (_modeMinutes.Checked) { _modeClock.Checked = false; }
                UpdateModeUi();
            };
            _modeClock.CheckedChanged += delegate
            {
                if (_modeClock.Checked) { _modeMinutes.Checked = false; }
                UpdateModeUi();
            };
            _tick.Interval = 500;
            _tick.Tick += delegate { RefreshState(); };

            ApplyTheme(theme);
            UpdateModeUi();
            UpdateButtons();
            // ⚠️ 这里不预置"忙"（`PowerForm` 2026-10-19 那个"死窗口"就是这么来的：构造函数先把
            // _busy 打开，Load 里那次刷新被自己挡掉 → 光标一直转圈）。第一次读状态由 RefreshState
            // 自己做，它开头就防重入。
            Load += delegate
            {
                RefreshState();
                _tick.Start();
            };
            FormClosed += delegate
            {
                _tick.Stop();
                _tick.Dispose();
            };
        }

        private static Label RowLabel(string text)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = text;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(2, 6, 10, 2);
            return l;
        }

        private void SetupQuick(Button b, string text, int minutes, FlowLayoutPanel host)
        {
            b.Text = text;
            StyleButton(b, 78);
            b.Margin = new Padding(2, 3, 6, 3);
            b.Click += delegate
            {
                _modeMinutes.Checked = true;
                _minutes.Value = minutes;
                UpdateModeUi();
            };
            host.Controls.Add(b);
        }

        private static DateTime RoundToMinute(DateTime t)
        {
            return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
        }

        private void StyleButton(Button b, int width)
        {
            b.AutoSize = false;
            b.Height = 28;
            b.Width = width;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(6, 2, 0, 2);
            b.TabStop = false;
        }

        public void ApplyTheme(Theme theme)
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
                NumericUpDown n = c as NumericUpDown;
                if (n != null)
                {
                    n.BackColor = theme.InputBack;
                    n.ForeColor = theme.InputText;
                }
                DateTimePicker d = c as DateTimePicker;
                if (d != null)
                {
                    d.BackColor = theme.InputBack;
                    d.ForeColor = theme.InputText;
                }
                RadioButton r = c as RadioButton;
                if (r != null) { r.ForeColor = theme.BarText; }
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

        // ------------------------------------------------------------------ 状态

        /// <summary>照记录文件重算一遍状态行（每半秒一次；也由「刷新」按）。只读，不动任何东西。</summary>
        private void RefreshState()
        {
            if (_busy) { return; }
            if (IsDisposed) { return; }
            TimerStatus st = PowerTimer.Status();
            _status.ForeColor = st.Stale ? _theme.Danger : _theme.BarText;
            _status.Text = PowerTimer.StatusText();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            TimerStatus st = PowerTimer.Status();
            _cancel.Enabled = !_busy && st.Present;
            _start.Enabled = !_busy;
            _refresh.Enabled = !_busy;
            _modeMinutes.Enabled = !_busy;
            _modeClock.Enabled = !_busy;
            _minutes.Enabled = !_busy && _modeMinutes.Checked;
            _clock.Enabled = !_busy && _modeClock.Checked;
            _quick15.Enabled = !_busy && _modeMinutes.Checked;
            _quick30.Enabled = !_busy && _modeMinutes.Checked;
            _quick60.Enabled = !_busy && _modeMinutes.Checked;
            _actShutdown.Enabled = !_busy;
            _actRestart.Enabled = !_busy;
            _actSleep.Enabled = !_busy;
        }

        private void UpdateModeUi()
        {
            _minutes.Enabled = _modeMinutes.Checked;
            _clock.Enabled = _modeClock.Checked;
            _quick15.Enabled = _modeMinutes.Checked;
            _quick30.Enabled = _modeMinutes.Checked;
            _quick60.Enabled = _modeMinutes.Checked;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateButtons();
        }

        private void Flash(string text)
        {
            _result.Text = text;
            Logger.Write("定时关机", text);
        }

        private string SelectedAction()
        {
            if (_actRestart.Checked) { return PowerTimer.ActionRestart; }
            if (_actSleep.Checked) { return PowerTimer.ActionSleep; }
            return PowerTimer.ActionShutdown;
        }

        /// <summary>按两种模式算出"到点是什么时候"（一定在未来）。</summary>
        private DateTime ChosenAt()
        {
            DateTime now = DateTime.Now;
            if (_modeClock.Checked)
            {
                return PowerTimer.AtClock(_clock.Value.Hour, _clock.Value.Minute, now);
            }
            return PowerTimer.FromNow((int)_minutes.Value, now);
        }

        // ------------------------------------------------------------------ 开始

        private void DoStart()
        {
            if (_busy) { return; }
            string action = SelectedAction();
            string label = PowerTimer.LabelOf(action);
            DateTime at = ChosenAt();
            DateTime now = DateTime.Now;
            int seconds = (int)(PowerTimer.Epoch(at) - PowerTimer.Epoch(now));
            if (seconds < PowerTimer.MinSeconds)
            {
                Flash("离现在太近了（不到 " + PowerTimer.MinSeconds + " 秒）：请把时间往后挪一点。");
                return;
            }

            if (!Consent.EnsureAccepted(this, _theme, "要定时" + label))
            {
                Flash("没有同意《免责声明与服务条款》，这个动作被拦下了。");
                return;
            }

            TimerStatus cur = PowerTimer.Status();
            StringBuilder body = new StringBuilder();
            body.Append("会在 ").Append(at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("（").Append(PowerTimer.WhenText(at, now)).Append("，还有 ")
                .Append(PowerTimer.SpanText(seconds)).Append("）执行「").Append(label).AppendLine("」。");
            body.AppendLine();
            if (action == PowerTimer.ActionSleep)
            {
                body.AppendLine("· 到点会直接让电脑睡眠（不问你）。");
                body.AppendLine("· 等着它的是工具箱自己的一个后台小进程：工具箱关了、缩进托盘都不影响；"
                    + "到点前 60 秒会弹一张提示卡，点卡片也能取消。");
            }
            else
            {
                body.AppendLine("· 到点会直接" + label + "，不会问你，也不会替你保存文档 —— 请先把手上的东西存好。");
                body.AppendLine("· 倒计时交给 Windows 自己记着（shutdown.exe /t）：工具箱关了也不影响；"
                    + "到点前 Windows 会自己弹出「Windows 将在一分钟内关闭」的提示。");
            }
            body.AppendLine("· 想取消：回到这个窗口点「取消定时」（命令行也可以：Mxx1Toolbox.exe timer cancel）。");
            body.AppendLine("· 不用管理员权限；电脑中途重启过之后，这条定时就不在了（窗口里会如实标出来）。");
            if (cur.Present)
            {
                body.AppendLine();
                body.AppendLine("注意：现在还有一条定时（" + cur.Record.Label + " · "
                    + cur.Record.AtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "），定这条会先把那一条撤掉。");
            }

            using (ConfirmForm f = new ConfirmForm("要定时" + label + "吗？", body.ToString(), "开始计时", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK)
                {
                    Flash("已取消：没有定这条定时。");
                    return;
                }
            }

            SetBusy(true);
            _status.ForeColor = _theme.BarText;
            _status.Text = "正在定…";
            Application.DoEvents();
            string report;
            int code;
            try
            {
                code = PowerTimer.Start(action, at, out report);
            }
            catch (Exception ex)
            {
                code = 1;
                report = "定的时候出错了：" + ex.Message;
            }
            SetBusy(false);
            _result.Text = report;
            Logger.Write("定时" + label, report.Replace(Environment.NewLine, " "));
            if (code != 0) { _status.ForeColor = _theme.Danger; }
            RefreshState();
        }

        // ------------------------------------------------------------------ 取消

        private void DoCancel()
        {
            if (_busy) { return; }
            TimerStatus cur = PowerTimer.Status();
            if (!cur.Present)
            {
                _result.Text = "现在没有在计时的任务（没有要撤的东西）。";
                return;
            }

            StringBuilder body = new StringBuilder();
            body.Append("会撤掉现在这条定时：").Append(cur.Record.Label).Append(" · ")
                .Append(cur.Record.AtLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .AppendLine("。");
            body.AppendLine();
            if (cur.Record.Mode == "worker")
            {
                body.AppendLine("· 会结束等着它的那个后台小进程（工具箱自己起的，PID "
                    + cur.Record.Pid.ToString(CultureInfo.InvariantCulture) + "）。");
            }
            else
            {
                body.AppendLine("· 会让 Windows 撤掉那次关机倒计时（shutdown /a）。");
                body.AppendLine("· 如果时间已经到了、系统那边本来就没有倒计时，也照样把这条记录清掉。");
            }
            if (cur.Stale)
            {
                body.AppendLine();
                body.AppendLine("（这条其实已经过了时间、也没看到它执行 —— 现在只是把这条旧记录清掉。）");
            }

            using (ConfirmForm f = new ConfirmForm("要取消这条定时吗？", body.ToString(), "取消定时", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            SetBusy(true);
            Application.DoEvents();
            string report;
            bool ok;
            try
            {
                ok = PowerTimer.Cancel(out report);
            }
            catch (Exception ex)
            {
                ok = false;
                report = "取消的时候出错了：" + ex.Message;
            }
            SetBusy(false);
            _result.Text = report;
            Logger.Write("定时" + cur.Record.Label, report.Replace(Environment.NewLine, " "));
            if (!ok) { _status.ForeColor = _theme.Danger; }
            RefreshState();
        }
    }
}
