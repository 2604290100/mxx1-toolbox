// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>「输出设备体检」窗口（GAP-02）：列出所有音频端点 + 谁是默认 + **真切**默认设备。</summary>
    internal sealed class SoundForm : Mxx1Form
    {
        private readonly Theme _theme;
        private readonly ListView _list = new ListView();
        private readonly Label _status = new Label();
        private readonly Button _setDefault = new Button();
        private readonly Button _openSettings = new Button();
        private readonly Button _refresh = new Button();

        private List<AudioDevice> _devices = new List<AudioDevice>();

        public SoundForm(Theme theme)
        {
            _theme = theme;
            Text = "输出设备体检（哪个在响、哪个是默认）";
            ClientSize = new Size(1000, 560);
            MinimumSize = new Size(840, 440);
            MinimizeBox = false;
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
            intro.MaximumSize = new Size(950, 0);
            intro.Text = "这一页看的是 Windows 认得的音频设备（喇叭 / 耳机 / 麦克风 / 蓝牙耳机）："
                + "哪些正在用、哪些被停用了、哪个是默认设备。"
                + Environment.NewLine
                + "「设为默认」是真的改系统里的默认设备（普通 / 多媒体 / 通话三个角色一起改），"
                + "改完随时可以再选回另一个；不删设备、不禁用设备、不动音量。";
            intro.Margin = new Padding(2, 0, 2, 8);
            root.Controls.Add(intro, 0, 0);

            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Dock = DockStyle.Fill;
            _list.Margin = new Padding(2, 0, 2, 6);
            _list.Columns.Add("设备", 300);
            _list.Columns.Add("方向", 60);
            _list.Columns.Add("状态", 90);
            _list.Columns.Add("默认", 130);
            _list.Columns.Add("说明", 380);
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.DoubleClick += delegate { SetSelectedDefault(); };
            root.Controls.Add(_list, 0, 1);

            _status.AutoSize = true;
            _status.MaximumSize = new Size(950, 0);
            _status.Margin = new Padding(2, 0, 2, 6);
            root.Controls.Add(_status, 0, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 5;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) { buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
            _setDefault.Text = "设为默认设备";
            StyleButton(_setDefault, 132);
            _setDefault.Click += delegate { SetSelectedDefault(); };
            _openSettings.Text = "打开声音设置";
            StyleButton(_openSettings, 120);
            _openSettings.Click += delegate { OpenSoundSettings(); };
            _refresh.Text = "刷新";
            StyleButton(_refresh, 76);
            _refresh.Click += delegate { RefreshList(); };
            Button close = new Button();
            close.Text = "关闭";
            StyleButton(close, 76);
            close.Click += delegate { Close(); };
            buttons.Controls.Add(new Label(), 0, 0);
            buttons.Controls.Add(_setDefault, 1, 0);
            buttons.Controls.Add(_openSettings, 2, 0);
            buttons.Controls.Add(_refresh, 3, 0);
            buttons.Controls.Add(close, 4, 0);
            root.Controls.Add(buttons, 0, 3);

            Controls.Add(root);
            CancelButton = close;
            ApplyTheme(theme);
            Load += delegate { RefreshList(); };
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

        public void ApplyTheme(Theme theme)
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
                ListView lv = c as ListView;
                if (lv != null)
                {
                    lv.BackColor = theme.LogBack;
                    lv.ForeColor = theme.LogText;
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

        private void RefreshList()
        {
            // 枚举是毫秒级的（就几个设备），不上后台线程；但先把光标换成"忙"，用户知道点到了。
            Cursor = Cursors.WaitCursor;
            _refresh.Enabled = false;
            try
            {
                _devices = AudioDevices.List(true);
                Fill();
            }
            finally
            {
                _refresh.Enabled = true;
                Cursor = Cursors.Default;
            }
        }

        private void Fill()
        {
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (AudioDevice d in _devices)
                {
                    ListViewItem row = new ListViewItem(d.Name);
                    row.SubItems.Add(d.Flow);
                    row.SubItems.Add(d.StateText);
                    row.SubItems.Add(d.IsDefault ? d.DefaultRoles : "");
                    row.SubItems.Add(d.Problem.Length > 0 ? d.Problem : "");
                    row.Tag = d;
                    if (d.State != 1) { row.ForeColor = _theme.Danger; }
                    _list.Items.Add(row);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            int outN = 0, inN = 0, bad = 0;
            string defOut = "";
            foreach (AudioDevice d in _devices)
            {
                if (d.FlowValue == 0) { outN++; } else { inN++; }
                if (d.State != 1) { bad++; }
                if (d.FlowValue == 0 && d.IsDefault && defOut.Length == 0) { defOut = d.Name; }
            }
            string[] svc = AudioDevices.Services();
            string line = "输出 " + outN.ToString(CultureInfo.InvariantCulture) + " 个、输入 " + inN.ToString(CultureInfo.InvariantCulture)
                + " 个（其中有 " + bad.ToString(CultureInfo.InvariantCulture) + " 个停用 / 没插 / 不在了）；"
                + "默认输出：" + (defOut.Length > 0 ? defOut : "（系统没给默认输出）")
                + "；音频服务：" + string.Join("、", svc);
            if (AudioDevices.LastError.Length > 0) { line += "　⚠ " + AudioDevices.LastError; }
            _status.Text = line;
            Logger.Write("输出设备体检", line);
            UpdateButtons();
        }

        private AudioDevice Selected()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as AudioDevice;
        }

        private void UpdateButtons()
        {
            AudioDevice d = Selected();
            bool can = d != null && d.State == 1;
            _setDefault.Enabled = can;
        }

        // ------------------------------------------------------------------ 改默认

        private void SetSelectedDefault()
        {
            AudioDevice d = Selected();
            if (d == null) { return; }
            if (d.State != 1)
            {
                MessageBox.Show(this,
                    "这个设备现在的状态是「" + d.StateText + "」，设成默认也不会响。" + Environment.NewLine + Environment.NewLine
                    + "已停用的要在「设备管理器」里重新启用；没插着的先看线 / 蓝牙。",
                    "输出设备体检", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (d.IsDefault && d.DefaultRoles == "普通 / 多媒体 / 通话")
            {
                _status.Text = "「" + d.Name + "」已经是三个角色的默认设备了，没有改动。";
                return;
            }

            System.Text.StringBuilder body = new System.Text.StringBuilder();
            body.AppendLine("把「" + d.Name + "」设成默认设备。");
            body.AppendLine();
            body.AppendLine("具体改的事：普通 / 多媒体 / 通话三个角色都指向它（和设置应用里点「设为默认值」的效果一样）。");
            body.AppendLine("改完可以随时再选另一个设备设回来 —— 不删设备、不禁用设备、不动音量。");
            body.AppendLine();
            body.AppendLine("如果设完还是没声音：看看这个设备的音量是不是 0、线插紧没有，或者点「打开声音设置」再确认一次。");
            using (ConfirmForm f = new ConfirmForm("要把「" + d.Name + "」设成默认设备吗？", body.ToString(), "设为默认", _theme))
            {
                if (f.ShowDialog(this) != DialogResult.OK) { return; }
            }

            Cursor = Cursors.WaitCursor;
            _setDefault.Enabled = false;
            try
            {
                string message;
                bool ok = AudioDevices.SetDefault(d.Id, out message);
                Logger.Write("输出设备体检", "设为默认「" + d.Name + "」：" + (ok ? "成功" : "失败") + " —— " + message);
                _status.Text = (ok ? "" : "⚠ ") + message;
                if (!ok)
                {
                    MessageBox.Show(this, message, "输出设备体检", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
                RefreshList();
            }
        }

        private void OpenSoundSettings()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound")
                {
                    UseShellExecute = true
                });
                Logger.Write("输出设备体检", "打开声音设置（ms-settings:sound）");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "这台系统打不开「设置」应用（Win7 上没有）。" + Environment.NewLine
                    + "声音设备请到「控制面板 → 声音」里看。" + Environment.NewLine + Environment.NewLine
                    + ex.Message,
                    "输出设备体检", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
