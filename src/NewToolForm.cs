// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>图形化的「新建按钮」/「编辑按钮」窗口（用户层 tools.json）。
    ///
    /// Four button types are offered, all of which the launcher already understands:
    ///   启动程序 (exe) / 打开文件夹·网址·系统页面 (open) / 运行脚本文件 (script + path) /
    ///   运行命令 (script + inline). Everything is laid out with TableLayoutPanel rows.</summary>
    internal sealed class NewToolForm : Mxx1Form
    {
        private const string KindExe = "exe";
        private const string KindOpen = "open";
        private const string KindScript = "script";
        private const string KindCommand = "command";

        private readonly TableLayoutPanel _root;
        private readonly ToolTip _tips = new ToolTip();

        private readonly TextBox _nameBox = new TextBox();
        private readonly ComboBox _kindBox = new ComboBox();
        private readonly TextBox _targetBox = new TextBox();
        private readonly Button _browse = MakeButton("浏览…");
        private readonly TextBox _argsBox = new TextBox();
        private readonly ComboBox _shellBox = new ComboBox();
        private readonly TextBox _hintBox = new TextBox();
        private readonly ComboBox _tabBox = new ComboBox();
        private readonly CheckBox _adminBox = new CheckBox();
        private readonly CheckBox _dangerBox = new CheckBox();
        private readonly Label _targetLabel = new Label();
        private readonly Label _argsLabel = new Label();
        private readonly Label _shellLabel = new Label();
        private readonly Label _tip = new Label();

        /// <summary>The button as edited. Read by MainForm after DialogResult.OK.</summary>
        public ToolItem Result;

        public NewToolForm(ToolItem existing, ToolItem prefill, Theme theme)
        {
            ToolItem seed = existing != null ? existing : prefill;
            bool editing = existing != null;

            Text = editing ? "编辑按钮" : "新建按钮";
            ClientSize = new Size(600, 400);
            MinimumSize = new Size(520, 380);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _nameBox.Text = seed != null ? seed.Name : "";
            _nameBox.MaxLength = 24;
            AddRow("按钮名称", _nameBox);

            _kindBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _kindBox.Items.AddRange(new object[] { "启动程序（exe）", "打开文件夹 / 网址 / 系统页面", "运行脚本文件", "运行一行命令" });
            _kindBox.SelectedIndex = 0;
            _kindBox.SelectedIndexChanged += delegate { SyncRows(); };
            AddRow("类型", _kindBox);

            TableLayoutPanel targetRow = new TableLayoutPanel();
            targetRow.Dock = DockStyle.Fill;
            targetRow.AutoSize = true;
            targetRow.ColumnCount = 2;
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _targetBox.Dock = DockStyle.Fill;
            _browse.Click += delegate { Browse(); };
            targetRow.Controls.Add(_targetBox);
            targetRow.Controls.Add(_browse);
            _targetLabel.AutoSize = true;
            _targetLabel.Text = "程序路径";
            _targetLabel.Margin = new Padding(2, 6, 10, 6);
            _root.Controls.Add(_targetLabel);
            _root.Controls.Add(targetRow);
            targetRow.Margin = new Padding(2, 4, 2, 4);

            _argsBox.Dock = DockStyle.Fill;
            _argsLabel.AutoSize = true;
            _argsLabel.Text = "启动参数";
            _argsLabel.Margin = new Padding(2, 6, 10, 6);
            AddRowControl(_argsLabel, _argsBox);

            _shellBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _shellBox.Items.AddRange(new object[] { "PowerShell", "cmd" });
            _shellBox.SelectedIndex = 0;
            _shellBox.Width = 140;
            _shellLabel.AutoSize = true;
            _shellLabel.Text = "用哪个壳";
            _shellLabel.Margin = new Padding(2, 6, 10, 6);
            AddRowControl(_shellLabel, _shellBox);

            _hintBox.Dock = DockStyle.Fill;
            AddRow("说明", _hintBox);
            _tips.SetToolTip(_hintBox, "鼠标停在按钮上时显示；搜索也会匹配这里的内容。可以留空。");

            _tabBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _tabBox.Dock = DockStyle.Fill;
            foreach (string id in Tabs.Ids) { _tabBox.Items.Add(Tabs.Display(id)); }
            _tabBox.SelectedIndex = Tabs.Index(Tabs.Mine);
            AddRow("放在哪个页签", _tabBox);

            _adminBox.Text = "以管理员身份运行";
            _adminBox.AutoSize = true;
            AddRow("", _adminBox);

            _dangerBox.Text = "危险按钮（深红文字 + 执行前二次确认）";
            _dangerBox.AutoSize = true;
            AddRow("", _dangerBox);

            _tip.AutoSize = true;
            _tip.MaximumSize = new Size(430, 0);
            _tip.Margin = new Padding(2, 8, 2, 4);
            _root.Controls.Add(_tip);
            _root.SetColumnSpan(_tip, 2);

            TableLayoutPanel buttons = new TableLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = true;
            buttons.ColumnCount = 3;
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button ok = MakeButton(editing ? "保存修改" : "创建按钮");
            ok.Click += delegate { Accept(); };
            Button cancel = MakeButton("取消");
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(new Label());
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            _root.Controls.Add(buttons);
            _root.SetColumnSpan(buttons, 2);

            // Seed the fields from the button being edited (or a dropped file).
            if (seed != null)
            {
                _kindBox.SelectedIndex = KindIndex(seed.Kind, seed.Inline.Length > 0);
                if (seed.Kind == "open") { _targetBox.Text = seed.Target; }
                else if (seed.Kind == "script" && seed.Inline.Length > 0) { _targetBox.Text = seed.Inline; }
                else { _targetBox.Text = seed.Path; }
                _argsBox.Text = seed.Args;
                _shellBox.SelectedIndex = (seed.Shell == "cmd") ? 1 : 0;
                _hintBox.Text = seed.Hint;
                _adminBox.Checked = seed.RunAsAdmin;
                _dangerBox.Checked = seed.Danger;
                _tabBox.SelectedIndex = Tabs.Index(seed.Tab);
                if (seed.Tab == Tabs.Mine && seed.Order >= 10) { _tabBox.SelectedIndex = Tabs.Index(Tabs.Mine); }
            }

            Controls.Add(_root);
            AcceptButton = ok;
            CancelButton = cancel;
            SyncRows();
            ApplyTheme(theme);
            EnableDrop(this);      // 拖进来直接填这个窗口，不再开第二个（见 EnableDrop 的注释）
        }

        // ---------------------------------------------------------------- layout helpers

        private void AddRow(string label, Control field)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.Text = label;
            l.Margin = new Padding(2, 6, 10, 6);
            AddRowControl(l, field);
        }

        private void AddRowControl(Control label, Control field)
        {
            field.Margin = new Padding(2, 4, 2, 4);
            _root.Controls.Add(label);
            _root.Controls.Add(field);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(90, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(4, 4, 0, 0);
            return b;
        }

        private int KindIndex(string kind, bool inline)
        {
            if (kind == "open") { return 1; }
            if (kind == "script") { return inline ? 3 : 2; }
            return 0;
        }

        /// <summary>Shows only the rows that matter for the selected type.</summary>
        private void SyncRows()
        {
            int kind = _kindBox.SelectedIndex;
            bool isExe = (kind == 0);
            bool isOpen = (kind == 1);
            bool isScriptFile = (kind == 2);
            bool isCommand = (kind == 3);

            _targetLabel.Text = isOpen ? "目标" : (isCommand ? "要执行的命令" : (isScriptFile ? "脚本文件" : "程序路径"));
            _targetBox.Multiline = false;
            _browse.Visible = !isOpen && !isCommand;
            _argsLabel.Visible = _argsBox.Visible = (isExe || isScriptFile);
            _shellLabel.Visible = _shellBox.Visible = (isScriptFile || isCommand);
            _dangerBox.Visible = !isOpen;

            if (isOpen)
            {
                _tip.Text = "可以填文件夹、网址，或系统页面：比如 %USERPROFILE%\\Downloads、"
                    + "https://mxx1.cn、shell:startup、ms-settings:windowsupdate。";
            }
            else if (isExe)
            {
                _tip.Text = "填一个 exe；也可以只写文件名，工具箱会先去旁边的 bin-tools 文件夹里找。";
            }
            else if (isScriptFile)
            {
                _tip.Text = "填一个 .ps1 / .bat / .cmd 脚本文件；跑完的输出会显示在结果窗口里。";
            }
            else
            {
                _tip.Text = "直接写一行命令，用 PowerShell 或 cmd 执行；输出会显示在结果窗口里。";
            }
        }

        // ---------------------------------------------------------------- 拖进来就能填

        /// <summary>Lets the window itself accept drops, so a file dropped while it is open fills
        /// THIS window instead of opening a second one. Every child control needs AllowDrop: the
        /// drop goes to whatever control is under the cursor.</summary>
        private void EnableDrop(Control c)
        {
            try
            {
                c.AllowDrop = true;
                c.DragEnter += OnDropEnter;
                c.DragDrop += OnDropHere;
            }
            catch { }
            foreach (Control child in c.Controls) { EnableDrop(child); }
        }

        private void OnDropEnter(object sender, DragEventArgs e)
        {
            bool files = (e.Data != null) && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = files ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDropHere(object sender, DragEventArgs e)
        {
            string[] files = null;
            try { files = e.Data.GetData(DataFormats.FileDrop) as string[]; }
            catch { }
            if (files == null || files.Length == 0) { return; }
            ToolItem d = DroppedFile.Draft(files[0]);
            if (d == null) { return; }

            _kindBox.SelectedIndex = KindIndex(d.Kind, d.Inline.Length > 0);
            if (d.Kind == "open") { _targetBox.Text = d.Target; }
            else
            {
                _targetBox.Text = d.Path;
                if (_argsBox.Text.Trim().Length == 0) { _argsBox.Text = d.Args; }
            }
            if (d.Kind == "script") { _shellBox.SelectedIndex = (d.Shell == "cmd") ? 1 : 0; }
            if (_nameBox.Text.Trim().Length == 0) { _nameBox.Text = d.Name; }
            if (d.Hint.Length > 0) { _hintBox.Text = d.Hint; }
            SyncRows();
            _tip.Text = "已按拖进来的东西填好：" + files[0] + (files.Length > 1 ? "（只取了第一个）" : "");
            _targetBox.Focus();
        }

        private void Browse()
        {
            int kind = _kindBox.SelectedIndex;
            if (kind == 2)
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择脚本";
                    dlg.Filter = "脚本 (*.ps1;*.bat;*.cmd;*.vbs)|*.ps1;*.bat;*.cmd;*.vbs|所有文件 (*.*)|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _targetBox.Text = dlg.FileName;
                        _shellBox.SelectedIndex = IsCmdFile(dlg.FileName) ? 1 : 0;
                        GuessName(dlg.FileName);
                    }
                }
                return;
            }
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择程序";
                dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _targetBox.Text = dlg.FileName;
                    GuessName(dlg.FileName);
                }
            }
        }

        private static bool IsCmdFile(string path)
        {
            string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
            return ext == ".bat" || ext == ".cmd";
        }

        private void GuessName(string path)
        {
            if (_nameBox.Text.Trim().Length == 0)
            {
                try { _nameBox.Text = Path.GetFileNameWithoutExtension(path); }
                catch { }
            }
        }

        // ---------------------------------------------------------------- collect

        private void Accept()
        {
            string name = _nameBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "请先给按钮起个名字。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _nameBox.Focus();
                return;
            }
            int kind = _kindBox.SelectedIndex;
            string value = _targetBox.Text.Trim();
            if (value.Length == 0)
            {
                MessageBox.Show(this, "请填要启动的内容（程序 / 目标 / 脚本 / 命令）。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _targetBox.Focus();
                return;
            }

            bool wantsFile = (kind == 0 || kind == 2);
            if (wantsFile)
            {
                string full = AppPaths.Resolve(value);
                if (!File.Exists(full))
                {
                    DialogResult answer = MessageBox.Show(this,
                        "这个文件现在不存在：" + Environment.NewLine + full + Environment.NewLine + Environment.NewLine
                        + "仍然保存吗？（以后把文件放进 " + AppPaths.PayloadDirName + " 文件夹就能用）",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) { return; }
                }
            }

            ToolItem t = new ToolItem();
            t.Id = "";
            t.Name = name;
            t.Tab = Tabs.Ids[Math.Max(0, _tabBox.SelectedIndex)];
            t.Segment = 1;
            t.Order = 0;
            t.UserLayer = true;
            t.Source = "tools.json（用户）";
            t.Hint = _hintBox.Text.Trim();
            t.RunAsAdmin = _adminBox.Checked;
            t.Danger = _dangerBox.Checked;
            t.Confirm = _dangerBox.Checked;

            if (kind == 0)
            {
                t.Kind = "exe";
                t.Path = value;
                t.Args = _argsBox.Text.Trim();
            }
            else if (kind == 1)
            {
                t.Kind = "open";
                t.Target = value;
            }
            else if (kind == 2)
            {
                t.Kind = "script";
                t.Path = value;
                t.Args = _argsBox.Text.Trim();
                t.Shell = (_shellBox.SelectedIndex == 1) ? "cmd" : "powershell";
            }
            else
            {
                t.Kind = "script";
                t.Inline = value;
                t.Shell = (_shellBox.SelectedIndex == 1) ? "cmd" : "powershell";
            }

            Result = t;
            DialogResult = DialogResult.OK;
            Close();
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            ApplyTo(_root, theme);
        }

        private static void ApplyTo(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label) { c.ForeColor = theme.BarText; c.BackColor = Color.Transparent; }
                else if (c is CheckBox) { c.ForeColor = theme.ButtonText; c.BackColor = theme.FormBack; }
                else if (c is TextBox || c is ComboBox)
                {
                    c.BackColor = theme.InputBack;
                    c.ForeColor = theme.InputText;
                }
                else if (c is Button)
                {
                    Button b = (Button)c;
                    b.BackColor = theme.ButtonBack;
                    b.ForeColor = theme.ButtonText;
                    b.FlatAppearance.BorderColor = theme.ButtonBorder;
                    b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
                }
                if (c.Controls.Count > 0) { ApplyTo(c, theme); }
            }
        }
    }
}
