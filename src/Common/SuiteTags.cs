// TealSuite 共通 (1.10.0): Teal Memo と Teal Plan で #タグ を共有する
//  - タグの見つけ方 (Teal Memo の本文の #タグ と同じきまり)
//  - 両方のアプリのタグを集めて、入力中に候補を出す (TagSuggest)
//  - Teal Hub の検索で「#タグ」と入れると、両方のアプリの項目が出る
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public static class SuiteTags
    {
        /// <summary>本文の #タグ (Teal Memo と同じ。前が文字・# のときと、数字だけのタグは数えない)</summary>
        public static readonly Regex TagRe = new Regex(@"(?<![\w#＃&])[#＃]([^\s#＃、。，．,.!！?？()（）「」『』\[\]【】<>＜＞""'”’:：;；/／]{1,30})");

        public static List<string> Parse(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match m in TagRe.Matches(text))
            {
                var t = m.Groups[1].Value;
                if (t.All(char.IsDigit)) continue;
                if (!list.Any(x => Same(x, t))) list.Add(t);
            }
            return list;
        }

        public static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        /// <summary>「#家」「＃家」「家」→「家」</summary>
        public static string Clean(string tag) { return (tag ?? "").Trim().TrimStart('#', '＃').Trim(); }

        /// <summary>タグをまとめる (大文字小文字は同じとみなす。多い順、同じなら名前順)</summary>
        public static List<string> Merge(IEnumerable<string> tags)
        {
            return (tags ?? Enumerable.Empty<string>()).Select(Clean).Where(t => t.Length > 0)
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCulture)
                .Select(g => g.First()).ToList();
        }

        /// <summary>Teal Memo (ゴミ箱以外) の本文のタグ (ファイルから)</summary>
        public static IEnumerable<string> MemoTags() { return SuiteRead.Memo().Live.SelectMany(n => n.Tags); }

        /// <summary>Teal Plan のタスクと予定のタグ (ファイルから)</summary>
        public static IEnumerable<string> PlanTags()
        {
            var p = SuiteRead.Plan();
            return p.Tasks.SelectMany(t => t.Tags ?? new List<string>()).Concat(p.Events.SelectMany(e => e.Tags ?? new List<string>()));
        }

        /// <summary>
        /// 両方のアプリのタグ。selfAppId のアプリは、ファイルの代わりに selfTags (いま開いているデータ) を使う
        /// (保存前の変更も候補に出るように)
        /// </summary>
        public static List<string> Collect(string selfAppId, IEnumerable<string> selfTags)
        {
            var all = new List<string>();
            if (selfTags != null) all.AddRange(selfTags);
            try { if (selfAppId != SuiteRead.MemoId) all.AddRange(MemoTags()); } catch { }
            try { if (selfAppId != SuiteRead.PlanId) all.AddRange(PlanTags()); } catch { }
            return Merge(all);
        }

        /// <summary>タグのリストに tag があるか</summary>
        public static bool Has(IEnumerable<string> tags, string tag)
        {
            tag = Clean(tag);
            return tags != null && tag.Length > 0 && tags.Any(t => Same(Clean(t), tag));
        }
    }

    /// <summary>
    /// TextBox に #タグ の候補を出す。カーソルの前の言葉が「#xx」(requireHash = false なら「xx」でも) のとき、
    /// 下に候補を出す。↑↓ で選んで Enter か Tab で入れる。Esc で閉じる
    /// </summary>
    public class TagSuggest
    {
        public const int MaxItems = 8;
        readonly TextBox box;
        readonly Func<IEnumerable<string>> source;
        readonly bool requireHash;
        readonly ListBox list = new ListBox();
        List<string> cache;
        DateTime cacheAt;
        int tokenStart = -1;

        public static TagSuggest Attach(TextBox box, Func<IEnumerable<string>> source, bool requireHash)
        {
            return new TagSuggest(box, source, requireHash);
        }

        TagSuggest(TextBox box, Func<IEnumerable<string>> source, bool requireHash)
        {
            this.box = box; this.source = source; this.requireHash = requireHash;
            list.Visible = false;
            list.TabStop = false;
            list.IntegralHeight = false;
            list.BorderStyle = BorderStyle.FixedSingle;
            list.Font = box.Font;
            list.MouseClick += (s, e) => { int i = list.IndexFromPoint(e.Location); if (i >= 0) { list.SelectedIndex = i; Accept(); } };
            box.TextChanged += (s, e) => Update();
            box.KeyUp += (s, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) Update(); };
            box.MouseUp += (s, e) => Hide();
            box.PreviewKeyDown += (s, e) => { if (Visible && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)) e.IsInputKey = true; };
            box.KeyDown += (s, e) => { if (HandleKey(e.KeyCode)) { e.Handled = true; e.SuppressKeyPress = true; } };
            box.Leave += (s, e) => { if (box.IsHandleCreated) box.BeginInvoke(new Action(() => { if (!list.Focused) Hide(); })); else Hide(); };
            box.Disposed += (s, e) => { try { if (list.Parent != null) list.Parent.Controls.Remove(list); list.Dispose(); } catch { } };
        }

        public bool Visible { get { return list.Visible; } }
        /// <summary>出ている候補 (テスト用)</summary>
        public List<string> Items { get { return list.Visible ? list.Items.Cast<string>().ToList() : new List<string>(); } }
        public ListBox ListControl { get { return list; } }

        /// <summary>カーソルの前の言葉 (#xx なら xx)。候補を出さない場所なら null</summary>
        public static string Token(string text, int caret, bool requireHash, out int start)
        {
            start = -1;
            if (text == null || caret < 0 || caret > text.Length) return null;
            // カーソルの後ろが言葉の途中なら出さない
            if (caret < text.Length && !char.IsWhiteSpace(text[caret]) && text[caret] != ',' && text[caret] != '、') return null;
            int i = caret;
            while (i > 0 && !char.IsWhiteSpace(text[i - 1]) && text[i - 1] != ',' && text[i - 1] != '、' && text[i - 1] != '，') i--;
            var word = text.Substring(i, caret - i);
            bool hash = word.Length > 0 && (word[0] == '#' || word[0] == '＃');
            if (hash) { if (word.Length > 1 && (word[1] == '#' || word[1] == '＃')) return null; start = i; return word.Substring(1); }
            if (requireHash || word.Length == 0) return null;
            start = i;
            return word;
        }

        /// <summary>prefix で始まるタグ (多い順のまま)、そのあと prefix を含むタグ。入力と同じタグは出さない</summary>
        public static List<string> Candidates(string prefix, IEnumerable<string> tags, int max = MaxItems)
        {
            prefix = prefix ?? "";
            var all = (tags ?? Enumerable.Empty<string>()).Where(t => !string.IsNullOrEmpty(t) && !SuiteTags.Same(t, prefix)).ToList();
            var starts = all.Where(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            var contains = prefix.Length == 0 ? Enumerable.Empty<string>() : all.Where(t => !t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && t.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0);
            return starts.Concat(contains).Take(max).ToList();
        }

        List<string> Tags()
        {
            // ファイルを読むのは 3 秒に 1 回まで
            if (cache == null || (DateTime.UtcNow - cacheAt).TotalSeconds > 3)
            {
                try { cache = (source() ?? Enumerable.Empty<string>()).ToList(); } catch { cache = new List<string>(); }
                cacheAt = DateTime.UtcNow;
            }
            return cache;
        }

        /// <summary>いまの入力に合わせて候補を出し直す</summary>
        public void Update()
        {
            if (!box.Visible || box.ReadOnly) { Hide(); return; }
            int start;
            var tok = Token(box.Text, box.SelectionStart, requireHash, out start);
            if (tok == null || box.SelectionLength > 0) { Hide(); return; }
            var items = Candidates(tok, Tags());
            if (items.Count == 0) { Hide(); return; }
            tokenStart = start;
            Show(items);
        }

        void Show(List<string> items)
        {
            var form = box.FindForm();
            if (form == null) return;
            if (list.Parent != form) { if (list.Parent != null) list.Parent.Controls.Remove(list); form.Controls.Add(list); }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var t in items) list.Items.Add(t);
            list.EndUpdate();
            list.SelectedIndex = 0;
            int ih = Math.Max(list.ItemHeight, list.Font.Height + 2);
            int w = Math.Max(120, items.Max(t => TextRenderer.MeasureText("#" + t, list.Font).Width) + 24);
            int h = ih * items.Count + 4;
            Point caretPt;
            try { caretPt = box.GetPositionFromCharIndex(Math.Max(0, Math.Min(tokenStart, box.TextLength - 1))); } catch { caretPt = Point.Empty; }
            if (box.TextLength == 0 || tokenStart >= box.TextLength) caretPt = new Point(caretPt.X, Math.Max(0, caretPt.Y));
            var p = form.PointToClient(box.PointToScreen(new Point(Math.Max(0, Math.Min(caretPt.X, box.Width - 40)), caretPt.Y + box.Font.Height + 4)));
            if (!box.Multiline) p = form.PointToClient(box.PointToScreen(new Point(Math.Max(0, Math.Min(caretPt.X, box.Width - 40)), box.Height + 2)));
            if (p.X + w > form.ClientSize.Width) p.X = Math.Max(0, form.ClientSize.Width - w);
            if (p.Y + h > form.ClientSize.Height)
            {
                var above = form.PointToClient(box.PointToScreen(new Point(0, box.Multiline ? caretPt.Y : 0))).Y - h - 2;
                p.Y = above >= 0 ? above : Math.Max(0, form.ClientSize.Height - h);
            }
            list.Bounds = new Rectangle(p, new Size(w, h));
            list.Visible = true;
            list.BringToFront();
        }

        public void Hide() { if (list.Visible) list.Visible = false; }

        /// <summary>キーを処理したら true</summary>
        public bool HandleKey(Keys k)
        {
            if (!Visible) return false;
            switch (k)
            {
                case Keys.Down: list.SelectedIndex = (list.SelectedIndex + 1) % list.Items.Count; return true;
                case Keys.Up: list.SelectedIndex = (list.SelectedIndex - 1 + list.Items.Count) % list.Items.Count; return true;
                case Keys.Enter: case Keys.Tab: Accept(); return true;
                case Keys.Escape: Hide(); return true;
            }
            return false;
        }

        /// <summary>選んでいる候補を入れる (「#xx」を「#タグ 」に置きかえる)</summary>
        public void Accept()
        {
            if (!Visible || list.SelectedIndex < 0 || tokenStart < 0) { Hide(); return; }
            var tag = (string)list.SelectedItem;
            int caret = Math.Min(box.SelectionStart, box.TextLength);
            var text = box.Text;
            if (tokenStart > caret) { Hide(); return; }
            bool hash = tokenStart < text.Length && (text[tokenStart] == '#' || text[tokenStart] == '＃');
            var ins = (hash ? text[tokenStart].ToString() : "") + tag + " ";
            Hide();
            var after = text.Substring(caret);
            if (after.StartsWith(" ")) after = after.Substring(1);
            box.Text = text.Substring(0, tokenStart) + ins + after;
            box.SelectionStart = tokenStart + ins.Length;
            box.SelectionLength = 0;
            Hide();
            box.Focus();
        }
    }
}
