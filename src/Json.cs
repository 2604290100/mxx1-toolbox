// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mxx1.cn
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mxx1Toolbox
{
    /// <summary>Minimal JSON reader for the button manifests (tools\*.json).
    /// Deliberately self contained -- no System.Web.Extensions, no Newtonsoft --
    /// so the single-file exe keeps compiling with plain csc.
    /// Values come back as Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null.</summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException("text"); }
            int i = 0;
            object value = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i < text.Length)
            {
                throw new FormatException("JSON 有多余内容（位置 " + i.ToString(CultureInfo.InvariantCulture) + "）");
            }
            return value;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; }
                else { break; }
            }
        }

        private static bool Match(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length) { return false; }
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0) { return false; }
            i += literal.Length;
            return true;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) { throw new FormatException("JSON 意外结束"); }
            char c = s[i];
            if (c == '{') { return ParseObject(s, ref i); }
            if (c == '[') { return ParseArray(s, ref i); }
            if (c == '"') { return ParseString(s, ref i); }
            if (c == 't' && Match(s, ref i, "true")) { return true; }
            if (c == 'f' && Match(s, ref i, "false")) { return false; }
            if (c == 'n' && Match(s, ref i, "null")) { return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') { throw new FormatException("JSON 对象缺少键（位置 " + i + "）"); }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') { throw new FormatException("JSON 对象的键后面缺少冒号（位置 " + i + "）"); }
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("JSON 对象缺少逗号或右括号（位置 " + i + "）");
            }
            return map;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("JSON 数组缺少逗号或右括号（位置 " + i + "）");
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length) { throw new FormatException("JSON 字符串没有结束引号"); }
                char c = s[i++];
                if (c == '"') { break; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) { throw new FormatException("JSON 转义符后面没有字符"); }
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) { throw new FormatException("JSON \\u 转义不完整"); }
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 不认识的转义符 \\" + e);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') { i++; }
                else { break; }
            }
            if (i == start) { throw new FormatException("JSON 里出现无法识别的字符 '" + s[start] + "'（位置 " + start + "）"); }
            string raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                throw new FormatException("JSON 数字无法解析: " + raw);
            }
            return d;
        }

        // ---- typed accessors (tolerant: a missing key returns the default) ----

        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static List<object> AsArray(object value)
        {
            return value as List<object>;
        }

        public static object GetObject(Dictionary<string, object> o, string key)
        {
            if (o == null) { return null; }
            object v;
            if (!o.TryGetValue(key, out v)) { return null; }
            return v;
        }

        public static string GetString(Dictionary<string, object> o, string key, string fallback)
        {
            if (o == null) { return fallback; }
            object v;
            if (!o.TryGetValue(key, out v) || v == null) { return fallback; }
            string s = v as string;
            if (s != null) { return s; }
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(Dictionary<string, object> o, string key, bool fallback)
        {
            if (o == null) { return fallback; }
            object v;
            if (!o.TryGetValue(key, out v) || v == null) { return fallback; }
            if (v is bool) { return (bool)v; }
            string s = v as string;
            if (s != null)
            {
                s = s.Trim().ToLowerInvariant();
                return (s == "1" || s == "true" || s == "yes" || s == "on");
            }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0; }
            catch { return fallback; }
        }

        public static int GetInt(Dictionary<string, object> o, string key, int fallback)
        {
            if (o == null) { return fallback; }
            object v;
            if (!o.TryGetValue(key, out v) || v == null) { return fallback; }
            if (v is double) { return (int)Math.Round((double)v); }
            string s = v as string;
            int n;
            if (s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { return n; }
            try { return (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)); }
            catch { return fallback; }
        }
    }
}
