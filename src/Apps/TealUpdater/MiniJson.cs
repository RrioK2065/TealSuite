// TealUpdater - 小さな JSON 読み書き (項目の順番と、知らない項目をそのまま残すため)
//  apps.json をまとめ直すとき、利用者が足したアプリや項目を消さないように DataContract ではなくこちらを使う
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TealSuite.Updater
{
    /// <summary>数値 (元の書き方のまま残す)</summary>
    public class JNum
    {
        public readonly string Raw;
        public JNum(string raw) { Raw = raw; }
        public double Value { get { return double.Parse(Raw, CultureInfo.InvariantCulture); } }
        public override string ToString() { return Raw; }
    }

    /// <summary>順番を保つ JSON オブジェクト。値は string / JNum / bool / null / List&lt;object&gt; / JObj</summary>
    public class JObj
    {
        readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
        public IEnumerable<string> Keys { get { return items.Select(i => i.Key); } }
        public int Count { get { return items.Count; } }
        public bool Has(string key) { return IndexOf(key) >= 0; }
        int IndexOf(string key) { for (int i = 0; i < items.Count; i++) if (items[i].Key == key) return i; return -1; }

        public object this[string key]
        {
            get { int i = IndexOf(key); return i < 0 ? null : items[i].Value; }
            set { int i = IndexOf(key); if (i < 0) items.Add(new KeyValuePair<string, object>(key, value)); else items[i] = new KeyValuePair<string, object>(key, value); }
        }

        /// <summary>after の直後に入れる (after がなければ末尾)。すでにあれば値だけ変える</summary>
        public void InsertAfter(string after, string key, object value)
        {
            if (Has(key)) { this[key] = value; return; }
            int i = IndexOf(after);
            items.Insert(i < 0 ? items.Count : i + 1, new KeyValuePair<string, object>(key, value));
        }

        public bool Remove(string key) { int i = IndexOf(key); if (i < 0) return false; items.RemoveAt(i); return true; }

        public string Str(string key) { var v = this[key]; return v == null ? null : v is string ? (string)v : v is JNum ? ((JNum)v).Raw : v is bool ? ((bool)v ? "true" : "false") : null; }
        public bool Bool(string key) { var v = this[key]; return v is bool && (bool)v; }
        public int Int(string key, int def) { var v = this[key] as JNum; int r; return v != null && int.TryParse(v.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def; }
        public long Long(string key, long def) { var v = this[key] as JNum; long r; return v != null && long.TryParse(v.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def; }
        public List<object> Arr(string key) { return this[key] as List<object>; }
        public JObj Obj(string key) { return this[key] as JObj; }
        public List<string> Strs(string key)
        {
            var a = Arr(key);
            return a == null ? new List<string>() : a.OfType<string>().ToList();
        }

        public JObj Clone() { return (JObj)Json.Clone(this); }
    }

    public static class Json
    {
        public static object Parse(string s)
        {
            if (s == null) throw new FormatException("空です");
            if (s.Length > 0 && s[0] == '\uFEFF') s = s.Substring(1);
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw Err(s, i, "余分な文字があります");
            return v;
        }

        public static JObj ParseObj(string s)
        {
            var o = Parse(s) as JObj;
            if (o == null) throw new FormatException("JSON のオブジェクトではありません");
            return o;
        }

        static FormatException Err(string s, int i, string what)
        {
            int line = 1; for (int k = 0; k < i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException("JSON の " + line + " 行目: " + what);
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw Err(s, i, "途中で終わっています");
            char c = s[i];
            if (c == '{')
            {
                var o = new JObj(); i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Err(s, i, "項目名がありません");
                    var k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Err(s, i, "「:」がありません");
                    i++;
                    o[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw Err(s, i, "「,」か「}」がありません");
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw Err(s, i, "「,」か「]」がありません");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            if (c == '-' || char.IsDigit(c))
            {
                int st = i; i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                var raw = s.Substring(st, i - st);
                double d;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw Err(s, st, "数値が正しくありません");
                return new JNum(raw);
            }
            throw Err(s, i, "読めない文字があります (" + c + ")");
        }

        static string Str(string s, ref int i)
        {
            i++; var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 > s.Length) throw Err(s, i, "\\u が途中で終わっています");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: throw Err(s, i, "知らないエスケープ (\\" + e + ")");
                }
            }
            throw Err(s, i, "文字列が閉じていません");
        }

        public static object Clone(object v)
        {
            var o = v as JObj;
            if (o != null) { var n = new JObj(); foreach (var k in o.Keys) n[k] = Clone(o[k]); return n; }
            var a = v as List<object>;
            if (a != null) return a.Select(Clone).ToList();
            return v;
        }

        /// <summary>2 スペースで字下げして書く。中身が単純な短い配列・オブジェクトは 1 行にまとめる (apps.json と同じ見た目)</summary>
        public static string Write(object v)
        {
            var sb = new StringBuilder();
            Write(sb, v, 0);
            sb.Append('\n');
            return sb.ToString();
        }

        static bool Simple(object v) { return !(v is JObj) && !(v is List<object>); }

        static void Write(StringBuilder sb, object v, int ind)
        {
            var o = v as JObj;
            var a = v as List<object>;
            if (o != null)
            {
                if (o.Count == 0) { sb.Append("{}"); return; }
                if (ind > 0 && o.Count <= 3 && o.Keys.All(k => Simple(o[k])))
                {
                    sb.Append('{');
                    sb.Append(string.Join(", ", o.Keys.Select(k => Quote(k) + ": " + Inline(o[k]))));
                    sb.Append('}');
                    return;
                }
                sb.Append("{\n");
                int n = 0;
                foreach (var k in o.Keys)
                {
                    sb.Append(' ', (ind + 1) * 2).Append(Quote(k)).Append(": ");
                    Write(sb, o[k], ind + 1);
                    if (++n < o.Count) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', ind * 2).Append('}');
                return;
            }
            if (a != null)
            {
                if (a.Count == 0) { sb.Append("[]"); return; }
                if (a.All(Simple)) { sb.Append('[').Append(string.Join(", ", a.Select(Inline))).Append(']'); return; }
                sb.Append("[\n");
                for (int i = 0; i < a.Count; i++)
                {
                    sb.Append(' ', (ind + 1) * 2);
                    Write(sb, a[i], ind + 1);
                    if (i < a.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(' ', ind * 2).Append(']');
                return;
            }
            sb.Append(Inline(v));
        }

        static string Inline(object v)
        {
            if (v == null) return "null";
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is JNum) return ((JNum)v).Raw;
            if (v is int || v is long) return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (v is string) return Quote((string)v);
            var sb = new StringBuilder(); Write(sb, v, 1); return sb.ToString();
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
