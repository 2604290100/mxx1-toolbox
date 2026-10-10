// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Mxx1Toolbox
{
    /// <summary>
    /// 「免责声明 / 服务协议」窗口。
    ///
    /// 文本来自**仓库里的 docs\DISCLAIMER.md**（编译时以资源名 Disclaimer.md 内嵌进 exe），
    /// 所以窗口内容与仓库文档永远一致，不需要在这里再抄一份 —— 抄两份必然会分叉。
    /// 显示前做一个很轻的 Markdown 清理（去掉 # / ** / 反引号），让它在文本框里仍然好读。
    /// 「首次运行的使用条款确认」（ConsentForm）用的也是这一份文本，所以"用户看到的"和
    /// "他同意的"永远是同一份（同意记录存的是这份正文的指纹，见 Consent.cs）。
    /// </summary>
    internal sealed class DisclaimerForm : Mxx1Form
    {
        private const string ResourceName = "Disclaimer.md";

        /// <summary>正文的正本在哪（命令行 `disclaimer` 会把它打出来）。</summary>
        internal const string SourceHint = "docs/DISCLAIMER.md（编译时内嵌进 exe 的这个资源，窗口显示的就是它）";

        /// <summary>只在内嵌资源丢失时兜底（正常构建不会走到这里；build.ps1 会自检资源名）。</summary>
        private const string Fallback =
            "免责声明与服务条款\r\n\r\n"
            + "本工具按 GPL-3.0-or-later 许可「按原样」提供，不提供任何形式的担保。\r\n"
            + "它是个启动器：按钮会启动别的程序；「隐私设置」「常用设置」会写 HKCU 下的注册表值\r\n"
            + "（写入前记原值，随时能一键还原）；「右键增强」会往 HKCU\\Software\\Classes 写自己那几个键；\r\n"
            + "「解除文件占用」在确认后可以结束你勾选的进程或关掉它们持有的文件句柄。\r\n"
            + "本工具不收集任何个人数据；启动时的更新检查只读版本号（先问 mxx1.cn 的资源接口，\r\n"
            + "读不到再退到 GitHub 公开接口），可用环境变量 MXX1_NO_UPDATE=1 完全关闭。\r\n\r\n"
            + "完整内容见仓库的 docs/DISCLAIMER.md。";

        private readonly Theme _theme;
        private readonly TableLayoutPanel _root;
        private readonly Label _head;
        private readonly TextBox _body;
        private readonly TableLayoutPanel _buttons;

        public DisclaimerForm(Theme theme)
        {
            _theme = theme;
            Text = "免责声明与服务条款";
            ClientSize = new Size(660, 500);
            MinimumSize = new Size(480, 380);
            // 两个都必须 false：Min=true/Max=false 时 Win10 会在标题栏画一个灰掉的最大化方框。
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Font;

            _root = new TableLayoutPanel();
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 1;
            _root.RowCount = 3;
            _root.Padding = new Padding(12, 10, 12, 10);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _head = new Label();
            _head.AutoSize = true;
            _head.Margin = new Padding(2, 2, 2, 6);
            _head.Text = "首次使用前请读一遍；继续使用即视为你已经读过并同意下面全部内容。";
            _root.Controls.Add(_head, 0, 0);

            _body = new TextBox();
            _body.Multiline = true;
            _body.ReadOnly = true;
            _body.ScrollBars = ScrollBars.Vertical;
            _body.WordWrap = true;
            _body.Dock = DockStyle.Fill;
            _body.Font = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _body.Text = LoadText().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            _body.SelectionStart = 0;
            _body.SelectionLength = 0;
            _root.Controls.Add(_body, 0, 1);

            _buttons = new TableLayoutPanel();
            _buttons.Dock = DockStyle.Fill;
            _buttons.AutoSize = true;
            _buttons.ColumnCount = 3;
            _buttons.RowCount = 1;
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Button repo = MakeButton("打开仓库里的正本");
            repo.Click += delegate { OpenRepoDoc(); };
            Button close = MakeButton("我已阅读");
            close.Click += delegate { Close(); };

            _buttons.Controls.Add(new Label(), 0, 0);
            _buttons.Controls.Add(repo, 1, 0);
            _buttons.Controls.Add(close, 2, 0);
            _root.Controls.Add(_buttons, 0, 2);

            Controls.Add(_root);
            AcceptButton = close;
            CancelButton = close;
            ApplyTheme(theme);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(84, 26);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.UseVisualStyleBackColor = false;
            b.Margin = new Padding(4, 6, 0, 0);
            return b;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = theme.FormBack;
            _root.BackColor = theme.FormBack;
            _head.BackColor = theme.FormBack;
            _head.ForeColor = theme.BarText;
            _body.BackColor = theme.LogBack;
            _body.ForeColor = theme.LogText;
            foreach (Control c in _buttons.Controls)
            {
                Button b = c as Button;
                if (b == null) { continue; }
                b.BackColor = theme.ButtonBack;
                b.ForeColor = theme.ButtonText;
                b.FlatAppearance.BorderColor = theme.ButtonBorder;
                b.FlatAppearance.MouseOverBackColor = theme.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = theme.ButtonPressed;
            }
        }

        /// <summary>打开仓库里的 docs/DISCLAIMER.md（网络不通也不弹错，静默就算了）。</summary>
        private void OpenRepoDoc()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    AboutForm.RepoUrl + "/blob/main/docs/DISCLAIMER.md") { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>读内嵌的 docs\DISCLAIMER.md，转成适合文本框显示的纯文本。</summary>
        internal static string LoadText()
        {
            string raw = null;
            try
            {
                Assembly asm = typeof(DisclaimerForm).Assembly;
                using (Stream s = asm.GetManifestResourceStream(ResourceName))
                {
                    if (s != null)
                    {
                        using (StreamReader sr = new StreamReader(s, Encoding.UTF8, true))
                        {
                            raw = sr.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("免责声明", "读取内嵌正文失败：" + ex.Message);
            }
            if (string.IsNullOrEmpty(raw)) { return Fallback; }
            return ToPlainText(raw);
        }

        /// <summary>正文原样（还没做 Markdown 清理）—— 指纹算的是清理之后的那一份，
        /// 也就是窗口里真正显示的、用户真正同意的那个字符串。见 Consent.CurrentHash。</summary>
        internal static string PlainText()
        {
            return LoadText();
        }

        /// <summary>命令行 `disclaimer` 用的就是它（不带 Markdown 记号）。</summary>
        internal static string ToPlainText(string markdown)
        {
            string[] lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            List<string> outp = new List<string>();
            foreach (string lineRaw in lines)
            {
                string line = lineRaw.TrimEnd();
                string t = line.TrimStart();
                // 标题：去掉 # 记号，但保留文字
                int h = 0;
                while (h < t.Length && t[h] == '#') { h++; }
                if (h > 0 && h < t.Length && t[h] == ' ') { t = t.Substring(h + 1); }
                if (t.StartsWith("> ")) { t = t.Substring(2); }
                if (t.StartsWith("- ")) { t = "· " + t.Substring(2); }
                t = t.Replace("**", "").Replace("`", "");
                // Markdown 链接 [文字](地址) 还原成 "文字（地址）"
                t = System.Text.RegularExpressions.Regex.Replace(t, @"\[([^\]]+)\]\(([^)]+)\)", "$1（$2）");
                outp.Add(t);
            }
            // 连续空行压成一个
            StringBuilder sb = new StringBuilder();
            bool blank = false;
            foreach (string l in outp)
            {
                if (l.Trim().Length == 0)
                {
                    if (blank) { continue; }
                    blank = true;
                }
                else { blank = false; }
                sb.Append(l).Append("\r\n");
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }
    }
}
