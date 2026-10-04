// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Security.Cryptography;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>
    /// 《免责声明与服务条款》的同意状态（记录在 %LOCALAPPDATA%\mxx1-toolbox\settings.ini 的
    /// AgreedDisclaimer 里）。
    ///
    /// 为什么记"正文指纹"而不是记一个 true/false：
    ///   * 条款改过（新增权限说明、改责任范围）之后，旧的"已同意"就不该继续算数 —— 指纹对不上
    ///     就会重新要求确认，不需要人工去重置谁的状态；
    ///   * 也因此不需要维护一个"条款版本号"，少一处会忘记同步的地方。
    ///
    /// 只影响**界面**：命令行（list / run / status / checkupdate…）是非交互场景（脚本 / CI），
    /// 不弹确认框，沿用"继续使用即视为同意"，但会在日志里留一行痕
    /// （见 docs/DISCLAIMER.md 第 5.1 节）。
    /// </summary>
    internal static class Consent
    {
        /// <summary>当前条款正文的指纹（16 位十六进制小写）。</summary>
        internal static string CurrentHash()
        {
            try
            {
                // 用的就是窗口里显示的那份纯文本，保证"用户看到的"和"他同意的"是同一份
                string text = DisclaimerForm.LoadText().Replace("\r\n", "\n").Replace("\r", "\n").Trim();
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 8; i++) { sb.Append(h[i].ToString("x2")); }
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Logger.Write("条款", "算正文指纹失败：" + ex.Message);
                return "";
            }
        }

        internal static string StoredHash()
        {
            try { return Settings.Load().AgreedDisclaimer ?? ""; }
            catch (Exception) { return ""; }
        }

        internal static string AgreedAt()
        {
            try { return Settings.Load().AgreedAt ?? ""; }
            catch (Exception) { return ""; }
        }

        /// <summary>已同意**且**同意的是当前这版正文。</summary>
        internal static bool IsAccepted()
        {
            string cur = CurrentHash();
            if (cur.Length == 0) { return false; }
            return string.Equals(StoredHash(), cur, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>记录同意。返回记录后是否真的处于"已同意"状态。</summary>
        internal static bool Accept()
        {
            try
            {
                Settings s = Settings.Load();
                s.AgreedDisclaimer = CurrentHash();
                s.AgreedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                s.Save();
                Logger.Write("条款", "已同意当前这版条款 指纹=" + s.AgreedDisclaimer);
            }
            catch (Exception ex) { Logger.Write("条款", "记录同意失败：" + ex.Message); }
            return IsAccepted();
        }

        /// <summary>清掉同意记录（下次打开界面会重新要求确认；排障 / 测试用）。</summary>
        internal static bool Reset()
        {
            try
            {
                Settings s = Settings.Load();
                s.AgreedDisclaimer = "";
                s.AgreedAt = "";
                s.Save();
                Logger.Write("条款", "同意记录已清除");
            }
            catch (Exception ex) { Logger.Write("条款", "清除同意记录失败：" + ex.Message); }
            return !IsAccepted();
        }

        /// <summary>给 status / consent 命令用的一句话状态：agreed / required。</summary>
        internal static string StateId()
        {
            return IsAccepted() ? "agreed" : "required";
        }

        /// <summary>界面里的确认门：没同意过（或条款改过）就弹《使用条款确认》，
        /// 同意返回 true；拒绝 / 直接关掉窗口返回 false —— 调用方负责"退出程序"或"拦下这个动作"。
        /// `reason` 只进日志，用来事后看清是哪个入口要的确认。</summary>
        internal static bool EnsureAccepted(System.Windows.Forms.IWin32Window owner, Theme theme, string reason)
        {
            if (IsAccepted()) { return true; }
            Logger.Write("条款", "要求确认：" + reason);
            try
            {
                using (ConsentForm f = new ConsentForm(theme))
                {
                    System.Windows.Forms.DialogResult r =
                        (owner == null) ? f.ShowDialog() : f.ShowDialog(owner);
                    if (r == System.Windows.Forms.DialogResult.OK && Accept()) { return true; }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("条款", "弹确认窗口失败：" + ex.Message);
                return false;
            }
            Logger.Write("条款", "用户没有同意：" + reason);
            return false;
        }
    }
}
