// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
#if !MXX1_NO_JXR
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif

namespace Mxx1Toolbox
{
    /// <summary>JPEG XR（`.jxr`，老名字 HD Photo / `.wdp`）—— 「图片转换」的第九种输出。
    ///
    /// 为什么只能借 Windows 自己的编码器：JPEG XR 是**真编解码器**（变换 + 自适应 Huffman），
    /// 不是 ICO / TGA / DDS 那种"按规范拼字节"就能写的格式。好消息是它是 **Windows 自带的
    /// WIC 原生编解码器**（不是 WebP / HEIF 那种要去商店装扩展的）：
    ///   · 编码器 CLSID `{ac4ce3cb-e1c1-44cd-8215-5a1665509ec2}`（`CLSID_WICWmpEncoder`），
    ///     这台机器上它指向系统目录里的 `wmphoto.dll`（**实测**，见 DESIGN §12.70 ⑨）；
    ///   · 写它最省事的一条路是 .NET 的 WPF：`WmpBitmapEncoder`（薄薄一层 WIC 封装）。
    ///
    /// ⚠️ **给这个 WinForms 程序挂 WPF 引用的代价与边界**（`build.ps1` 里那段）：
    ///   · 要引三个程序集：`PresentationCore` / `WindowsBase` / `System.Xaml`
    ///     （第三个是因为 PresentationCore 里有个类型实现了 `System.Xaml.IUriContext`，
    ///      不加它 csc 直接 CS0012 报错 —— 第一次编译就是这么红的）；
    ///   · 它们在 **GAC** 里（`GAC_32` / `GAC_64` 各一份，`WindowsBase` / `System.Xaml` 在 `GAC_MSIL`），
    ///     不在 `Reference Assemblies`（那台机器没装目标包时那里是空的）；
    ///   · **只在真的转 JXR 那一刻才加载 WPF** —— 这个类里碰 WPF 类型的地方只有 `Write()` 一处，
    ///     探测用的是**注册表 + `Type.GetType` 反射**，所以平时不背那几 MB 的内存。
    ///
    /// ⚠️ **Win7 上的差别**（微软文档 + 实测）：Win7 带的是 HD Photo 那个前身编码器，
    /// **Windows 8 起才"总是输出 JPEG XR 合规码流"**（解码器两代都认）。
    /// 所以同一个"转成 JXR"，Win7 上写出来的可能是 HD Photo 味的码流（Windows 8+ 读得回来）；
    /// 这一版不做区分，卡片上说的是"格式"而不是"哪一代的码流"。
    ///
    /// 探测判据（**不进 WPF**，很便宜）：① 这一份 exe 编译时带没带（`MXX1_NO_JXR`）；
    /// ② 注册表里有没有那个编码器 CLSID；③ 能不能反射到 `WmpBitmapEncoder` 这个类型。
    /// 三条都过才在子菜单里长出「转成 JXR」那一行 —— **不给用户摆一个点了必然失败的菜单项**。</summary>
    internal static class JxrWriter
    {
        /// <summary>这个格式在 `ImgConvert.Targets` 里的词。</summary>
        public const string Target = "jxr";

        /// <summary>`CLSID_WICWmpEncoder` —— WIC 里那个 JPEG XR / HD Photo 编码器。</summary>
        public const string EncoderClsid = "{ac4ce3cb-e1c1-44cd-8215-5a1665509ec2}";

        /// <summary>写出来的文件头 4 字节（HD Photo 1.0 / JPEG XR 的签名 `49 49 BC 01`）。</summary>
        private static readonly byte[] Magic = new byte[] { 0x49, 0x49, 0xBC };

        private static int _state;          // 0 = 还没探过，1 = 能用，2 = 不能用
        private static string _why = "";

        /// <summary>这台机器（这一份 exe）能不能写 JPEG XR。结果缓存在内存里（一次转换问好几遍）。</summary>
        public static bool Available(out string why)
        {
            if (_state == 0) { Probe(); }
            why = _why;
            return _state == 1;
        }

        /// <summary>不吃 out 的重载（给菜单/报告用）。</summary>
        public static bool Available()
        {
            string why;
            return Available(out why);
        }

        /// <summary>不能用的原因（能用时是空串）—— 卡片和日志里要如实说这一句。</summary>
        public static string Reason()
        {
            string why;
            Available(out why);
            return why;
        }

        /// <summary>探一次：编译期有没有带 → 注册表里编码器在不在 → WPF 的类型反射得到吗。
        /// **刻意不走"真编一张试试"**：那是安装按钮 / 状态报告的路径，不该为了回答一句话去加载 WPF。</summary>
        private static void Probe()
        {
#if MXX1_NO_JXR
            _state = 2;
            _why = "这一份 exe 编译时没带 JPEG XR 支持（编它的机器上没找到 .NET 的 WPF 程序集）";
#else
            string dll = EncoderDll();
            if (dll.Length == 0)
            {
                _state = 2;
                _why = "这台 Windows 里没有 JPEG XR 编码器（注册表里找不到 " + EncoderClsid + "）";
                return;
            }
            try
            {
                // ⚠️ 别写 `Type.GetType("…WmpBitmapEncoder, PresentationCore")` —— 只给**简单程序集名**
                // 时它对 GAC 里的强名程序集解析不出类型，而且 `throwOnError: false` 下**只返回 null、
                // 不报错**（实测：WPF 明明装着，探测却永远说"不支持"，菜单里那一行永远不出现）。
                // 先按完整强名 Load，再退回简单名。
                System.Reflection.Assembly asm = null;
                try
                {
                    asm = System.Reflection.Assembly.Load(
                        "PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                }
                catch { }
                if (asm == null)
                {
                    try { asm = System.Reflection.Assembly.Load("PresentationCore"); }
                    catch { }
                }
                if (asm == null)
                {
                    _state = 2;
                    _why = "这台机器上没有 .NET 的 WPF（PresentationCore）—— JPEG XR 输出要借它";
                    return;
                }
                Type t = asm.GetType("System.Windows.Media.Imaging.WmpBitmapEncoder", false);
                if (t == null)
                {
                    _state = 2;
                    _why = "WPF 里没有 WmpBitmapEncoder 这个编码器类（.NET 版本太老？）";
                    return;
                }
            }
            catch (Exception ex)
            {
                _state = 2;
                _why = "读不到 WPF 的编码器类型：" + ex.Message;
                return;
            }
            _state = 1;
            _why = "";
#endif
        }

        /// <summary>编码器 CLSID 指向哪个文件（读得到才算"这台 Windows 有它"）。
        /// 只读注册表、不进 WPF —— 探测和写文件是两条路，探测这条路必须是便宜的。</summary>
        public static string EncoderDll()
        {
            try
            {
                using (RegistryKey k = Registry.ClassesRoot.OpenSubKey("CLSID\\" + EncoderClsid, false))
                {
                    if (k == null) { return ""; }
                    using (RegistryKey srv = k.OpenSubKey("InprocServer32", false))
                    {
                        if (srv == null) { return ""; }
                        object v = srv.GetValue("", null);
                        return (v == null) ? "" : v.ToString();
                    }
                }
            }
            catch { return ""; }
        }

        /// <summary>写成 `.jxr`。返回空串 = 成功，否则一句人话（调用方把它当"这个文件没转成"的原因）。
        ///
        /// ⚠️ 碰 WPF 的代码**全在 `WriteCore` 里**，这里只做"先问能不能用、再调"：
        /// 万一某台机器上真没有 PresentationCore，JIT **只会在调用 `WriteCore` 时才去解析那些类型** ——
        /// 而那时候我们已经知道不能用、根本不会调它。把 WPF 类型写在这个方法体里就会反过来：
        /// JIT 编译 `Write` 这一层时就要解析 WPF 类型 → 直接抛，连"如实说一句为什么"的机会都没有。</summary>
        public static string Write(string path, RawImage img)
        {
            string why;
            if (!Available(out why)) { return why; }
#if MXX1_NO_JXR
            return why;
#else
            string error = WriteCore(path, img);
            if (error.Length > 0) { return error; }
            // 写完了核一眼签名：编码器"在"但写出来是别的东西这种事，不核就会静默溜过去
            return CheckMagic(path);
#endif
        }

#if !MXX1_NO_JXR
        /// <summary>真借 WPF 那层写（只有探测通过了才会走到这里）。</summary>
        private static string WriteCore(string path, RawImage img)
        {
            try
            {
                BitmapSource src = BitmapSource.Create(img.Width, img.Height, 96, 96,
                    PixelFormats.Bgra32, null, img.Pixels, img.Width * 4);
                WmpBitmapEncoder enc = new WmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(src));
                // ⚠️ 两个"质量"旋钮别搞混（实测它们的类型不一样）：
                //    ImageQualityLevel = Single，0..1（默认 0.9，1.0 = 数学无损）← 我们要的是这个；
                //    QualityLevel = Byte，1..255，而且 **1 = 无损**（拿 0.9 往它上面赋会被取整成 1，
                //    于是"想要 0.9 的质量"实际得到的是无损文件 —— 第一版探针就这么写错过）。
                enc.ImageQualityLevel = 0.9f;
                enc.InterleavedAlpha = false;      // 平面 alpha：透明照样保住（实测读回来是 alpha=0）
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    enc.Save(fs);
                }
            }
            catch (Exception ex)
            {
                return "JPEG XR 编码没成功：" + ex.GetType().Name + " " + ex.Message;
            }
            return "";
        }
#endif

        /// <summary>头几字节必须是我们认识的那个签名。</summary>
        private static string CheckMagic(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    byte[] head = new byte[Magic.Length];
                    if (fs.Read(head, 0, head.Length) != head.Length) { return "JPEG XR 写出来的文件是空的"; }
                    for (int i = 0; i < Magic.Length; i++)
                    {
                        if (head[i] != Magic[i])
                        {
                            return "写出来的文件不像 JPEG XR（头 3 字节 " + Hex(head) + "，应该是 49 49 BC）";
                        }
                    }
                }
            }
            catch (Exception ex) { return "JPEG XR 写完读不回来：" + ex.Message; }
            return "";
        }

        private static string Hex(byte[] b)
        {
            List<string> parts = new List<string>();
            foreach (byte x in b) { parts.Add(x.ToString("x2", CultureInfo.InvariantCulture)); }
            return string.Join(" ", parts.ToArray());
        }
    }
}
