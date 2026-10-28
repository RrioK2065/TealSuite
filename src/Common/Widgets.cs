// TealSuite 共通: 画面づくりの小物 (1.7.0 から。Teal Wallet・Teal Tasks で使う)
//  - Lay        … Dock=Top で上から順に積む・入力欄 + ボタンの 1 行・小見出し
//  - SegmentTabs … 画面の切り替えタブ (キーボードの ←→ / Ctrl+1〜9 でも切り替え)
//  - Fmt        … 金額・日付・時間の日本語表記
//  - FormGrid / DialogKit … 入力ダイアログ (見出し + 入力欄の格子、下のボタン列)
//  - RowList / Draw / StatTile … 自分で描く一覧・行の描画・大きな数字のタイル
//  - FileKit    … CSV の書き出し、JSON バックアップと復元
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace TealSuite.Common
{
    public static class Lay
    {
        /// <summary>上から順に Dock=Top で積む (WinForms は後から追加したものが上に来るので逆順に追加)</summary>
        public static void Stack(Control parent, params Control[] topToBottom)
        {
            for (int i = topToBottom.Length - 1; i >= 0; i--)
            {
                var c = topToBottom[i];
                if (c == null) continue;
                if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top;
                parent.Controls.Add(c);
            }
        }

        /// <summary>入力欄 + 右側のコントロール群の 1 行 (左から field | c0 | c1 …)</summary>
        public static Panel Row(Control field, params Control[] right)
        {
            var row = new Panel { BackColor = Color.Transparent };
            int h = field == null ? 0 : field.Height;
            foreach (var c in right) h = Math.Max(h, c.Height);
            h = Math.Max(h, Theme.S(Theme.ButtonHeight));
            if (field != null) { field.Dock = DockStyle.Fill; row.Controls.Add(field); }
            for (int i = 0; i < right.Length; i++)
            {
                row.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Theme.S(6), BackColor = Color.Transparent });
                right[i].Dock = DockStyle.Right;
                row.Controls.Add(right[i]);
            }
            row.Height = h;
            return row;
        }

        /// <summary>左から順に並べる 1 行 (折り返しなし)</summary>
        public static FlowLayoutPanel Flow(params Control[] items)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, AutoSize = false };
            int h = Theme.S(Theme.ButtonHeight);
            foreach (var c in items)
            {
                if (c == null) continue;
                c.Margin = new Padding(0, 0, Theme.S(6), 0);
                f.Controls.Add(c);
                h = Math.Max(h, c.Height + c.Margin.Vertical);
            }
            f.Height = h;
            return f;
        }

        public static Label SectionLabel(string text)
        {
            return new Label { Text = text, Font = Theme.Small, ForeColor = Theme.SubText, AutoSize = false, Height = Theme.Small.Height + Theme.S(8), TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false, BackColor = Color.Transparent };
        }

        /// <summary>折り返す説明文 (幅に合わせて高さを自動で決める)</summary>
        public static Label Note(string text, Color? color = null)
        {
            var l = new WrapLabel { Text = text, Font = Theme.Small, ForeColor = color ?? Theme.SubText, BackColor = Color.Transparent };
            return l;
        }

        /// <summary>ComboBox (選ぶだけ) を枠つきで</summary>
        public static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Text };
            c.Items.AddRange(items);
            if (items.Length > 0) c.SelectedIndex = 0;
            c.Height = c.PreferredHeight;
            return c;
        }
    }

    /// <summary>入力ダイアログの格子 (見出し + 入力欄を columns 列で並べる。高さは中身に合わせて自動)</summary>
    public class FormGrid : TableLayoutPanel
    {
        readonly int colWidth;
        public FormGrid(int columns, int columnWidth)
        {
            colWidth = Theme.S(columnWidth);
            ColumnCount = columns;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            BackColor = Theme.Surface;
            for (int i = 0; i < columns; i++) ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, colWidth + Theme.S(12)));
        }
        /// <summary>この列幅で span 列ぶんのときの入力欄の幅</summary>
        public int FieldWidth(int span) { return colWidth * span + Theme.S(12) * (span - 1); }
        /// <summary>見出しつきで 1 マス (span 列ぶん) に置く。c の幅は自動で合わせる (Inline のときはそのまま)</summary>
        public T Add<T>(string label, T c, int span = 1) where T : Control
        {
            var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0, 0, Theme.S(12), Theme.S(4)), Padding = Padding.Empty };
            if (label != null)
            {
                var l = Lay.SectionLabel(label);
                l.Width = FieldWidth(span); l.Margin = Padding.Empty;
                box.Controls.Add(l);
            }
            if (!(c is FlowLayoutPanel)) c.Width = FieldWidth(span);
            c.Margin = new Padding(0, Theme.S(2), 0, 0);
            box.Controls.Add(c);
            Controls.Add(box);
            SetColumnSpan(box, span);
            return c;
        }
        /// <summary>1 マスを丸ごと使う (ボタンの行など)</summary>
        public T AddRaw<T>(T c, int span) where T : Control
        {
            c.Margin = new Padding(0, Theme.S(4), Theme.S(12), Theme.S(4));
            Controls.Add(c);
            SetColumnSpan(c, span);
            return c;
        }
        /// <summary>横に並べる (チェックボックス + 日付など)</summary>
        public static FlowLayoutPanel Inline(params Control[] items)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Padding = Padding.Empty };
            foreach (var c in items)
            {
                c.Margin = new Padding(0, 0, Theme.S(6), 0);
                f.Controls.Add(c);
            }
            return f;
        }
        /// <summary>日付の入力 (yyyy/MM/dd (曜))</summary>
        public static DateTimePicker DatePicker(int width = 160)
        {
            return new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy/MM/dd (ddd)", Font = Theme.Body, Width = Theme.S(width) };
        }
        public static CheckBox Check(string text)
        {
            return new CheckBox { Text = text, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent, UseMnemonic = false, Padding = new Padding(0, Theme.S(4), 0, 0) };
        }
    }

    /// <summary>ダイアログの下のボタン列 (右寄せ。先に渡したものほど右)</summary>
    public static class DialogKit
    {
        public static FlowLayoutPanel Buttons(params FlatButton[] rightToLeft)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Dock = DockStyle.Fill };
            foreach (var b in rightToLeft) { b.Margin = new Padding(Theme.S(6), 0, 0, 0); f.Controls.Add(b); }
            return f;
        }
        /// <summary>中身に合わせて大きさが決まるダイアログの土台</summary>
        public static void Setup(Form f, string title)
        {
            Theme.ApplyForm(f);
            f.Text = title;
            f.BackColor = Theme.Surface;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false; f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.KeyPreview = true;
            f.AutoSize = true; f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Padding = Theme.P(16, 12, 6, 12);
        }
    }

    /// <summary>ファイルへの書き出し・バックアップ (JSON) と復元</summary>
    public static class FileKit
    {
        /// <summary>テキストを保存 (UTF-8 BOM つき: Excel で文字化けしない)。保存したパスか null</summary>
        public static string SaveText(IWin32Window owner, string text, string defaultName, string filter)
        {
            using (var d = new SaveFileDialog { FileName = defaultName, Filter = filter, OverwritePrompt = true })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                try
                {
                    DataStore.WriteFileAtomic(d.FileName, new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());
                    return d.FileName;
                }
                catch (Exception ex) { Dialogs.Error(owner, "保存できませんでした。\n" + ex.Message); return null; }
            }
        }

        /// <summary>データ全体を JSON で保存 (バックアップ)</summary>
        public static string ExportJson<T>(IWin32Window owner, T data, string baseName)
        {
            using (var d = new SaveFileDialog { FileName = baseName + "-backup-" + DateTime.Now.ToString("yyyyMMdd") + ".json", Filter = "バックアップ (*.json)|*.json", OverwritePrompt = true })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                try { DataStore.WriteFileAtomic(d.FileName, DataStore.ToJson(data)); return d.FileName; }
                catch (Exception ex) { Dialogs.Error(owner, "保存できませんでした。\n" + ex.Message); return null; }
            }
        }

        /// <summary>バックアップを読み込む。valid が false を返すものは読まない。今のファイルは .before-restore-日時 に残す</summary>
        public static T ImportJson<T>(IWin32Window owner, string currentFile, Func<T, bool> valid) where T : class
        {
            using (var d = new OpenFileDialog { Filter = "バックアップ (*.json)|*.json|すべてのファイル (*.*)|*.*" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                T data = null;
                try { data = DataStore.FromJson<T>(File.ReadAllBytes(d.FileName)); } catch { data = null; }
                if (data == null || !valid(data)) { Dialogs.Error(owner, "このファイルは " + AppInfo.DisplayName + " のバックアップとして読み込めませんでした。"); return null; }
                if (!Dialogs.Confirm(owner, "今のデータをバックアップの内容で置き換えます。よろしいですか?\n(今のデータは念のためデータフォルダに残します)")) return null;
                try
                {
                    var cur = DataStore.PathOf(currentFile);
                    if (File.Exists(cur)) File.Copy(cur, cur + ".before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                }
                catch { }
                return data;
            }
        }
    }

    /// <summary>1.8.0: テキストの読み込み・CSV の読み取り・ファイル名づくり (画面なし。テストできる)</summary>
    public static class TextKit
    {
        /// <summary>バイト列を文字に (BOM → UTF-8 → だめなら Shift_JIS)。改行は \n にそろえる</summary>
        public static string Decode(byte[] b)
        {
            if (b == null) return "";
            string s;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) s = System.Text.Encoding.UTF8.GetString(b, 3, b.Length - 3);
            else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) s = System.Text.Encoding.Unicode.GetString(b, 2, b.Length - 2);
            else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) s = System.Text.Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            else
            {
                try { s = new System.Text.UTF8Encoding(false, true).GetString(b); }
                catch (System.Text.DecoderFallbackException)
                {
                    System.Text.Encoding sjis;
                    try { sjis = System.Text.Encoding.GetEncoding(932); } catch { sjis = System.Text.Encoding.Default; }
                    s = sjis.GetString(b);
                }
            }
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>CSV を行と列に分ける ("" で囲んだ中の , と改行もそのまま)。空の行は飛ばす</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '\uFEFF') text = text.Substring(1);
            var row = new List<string>();
            var cell = new System.Text.StringBuilder();
            bool quoted = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else cell.Append(c);
                    continue;
                }
                if (c == '"') { quoted = true; any = true; }
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); any = true; }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    if (any || row.Any(x => x.Length > 0)) rows.Add(row);
                    row = new List<string>(); any = false;
                }
                else { cell.Append(c); any = true; }
            }
            row.Add(cell.ToString());
            if (any || row.Any(x => x.Length > 0)) rows.Add(row);
            return rows;
        }

        /// <summary>CSV の 1 マス ("" で囲む必要があれば囲む。Excel で式にならないよう = + - @ で始まるものは ' をつける)</summary>
        public static string CsvCell(string v)
        {
            v = v ?? "";
            if (v.Length > 0 && "=+-@".IndexOf(v[0]) >= 0) v = "'" + v;
            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) v = "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        /// <summary>見出しの列番号を探す (大文字小文字を区別しない。候補のどれか)。なければ -1</summary>
        public static int Column(List<string> header, params string[] names)
        {
            for (int i = 0; i < header.Count; i++)
            {
                var h = (header[i] ?? "").Trim().Trim('\uFEFF');
                foreach (var n in names) if (string.Equals(h, n, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
        public static string Cell(List<string> row, int i) { return i >= 0 && i < row.Count ? (row[i] ?? "").Trim() : ""; }

        /// <summary>ファイル名に使えない文字を除いて短くする。used にある名前とかぶったら (2)(3)… をつける</summary>
        public static string SafeFileName(string title, string ext, ICollection<string> used)
        {
            var bad = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }));
            var sb = new System.Text.StringBuilder();
            foreach (var ch in (title ?? "").Trim()) sb.Append(bad.Contains(ch) || char.IsControl(ch) ? '_' : ch);
            var name = sb.ToString().Trim().TrimEnd('.', ' ');
            if (name.Length > 40) name = name.Substring(0, 40).TrimEnd('.', ' ');
            if (name.Length == 0) name = "無題";
            var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "LPT1", "LPT2", "LPT3" };
            if (reserved.Contains(name.ToUpperInvariant())) name = "_" + name;
            string cand = name + ext;
            for (int k = 2; used != null && used.Any(u => string.Equals(u, cand, StringComparison.OrdinalIgnoreCase)); k++) cand = name + " (" + k + ")" + ext;
            if (used != null) used.Add(cand);
            return cand;
        }
    }

    /// <summary>幅に合わせて折り返し、高さを自分で決めるラベル (Dock=Top 用。文字が切れない)</summary>
    public class WrapLabel : Label
    {
        public WrapLabel() { AutoSize = false; UseMnemonic = false; Padding = new Padding(0, Theme.S(2), 0, Theme.S(2)); }
        void Fit()
        {
            if (Width <= 0) return;
            var sz = TextRenderer.MeasureText(Text ?? "", Font, new Size(Math.Max(10, Width - Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int h = (string.IsNullOrEmpty(Text) ? 0 : sz.Height) + Padding.Vertical;
            if (Height != h) Height = h;
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Fit(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(PaintUtil.ParentBack(this));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>画面の切り替えタブ (下線つき)。←→ キーで移動、SelectedChanged で通知</summary>
    public class SegmentTabs : Control
    {
        readonly List<string> items = new List<string>();
        readonly List<Rectangle> rects = new List<Rectangle>();
        int selected, hover = -1;
        public event EventHandler SelectedChanged;
        public SegmentTabs(params string[] labels)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            items.AddRange(labels);
            Font = Theme.Bold; TabStop = true; Cursor = Cursors.Hand;
            Height = Font.Height + Theme.S(18);
            BackColor = Theme.Surface;
        }
        public int Count { get { return items.Count; } }
        public string this[int i] { get { return items[i]; } }
        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int v = Math.Max(0, Math.Min(items.Count - 1, value));
                if (v == selected) return;
                selected = v; Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }
        void Layout_()
        {
            rects.Clear();
            int x = Theme.S(12);
            foreach (var t in items)
            {
                int w = TextRenderer.MeasureText(t, Font).Width + Theme.S(28);
                rects.Add(new Rectangle(x, 0, w, Height));
                x += w + Theme.S(2);
            }
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { SelectedIndex = (selected + items.Count - 1) % items.Count; e.Handled = true; }
            if (e.KeyCode == Keys.Right) { SelectedIndex = (selected + 1) % items.Count; e.Handled = true; }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Layout_();
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Layout_();
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h >= 0) SelectedIndex = h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Layout_();
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i];
                bool sel = i == selected;
                if (i == hover && !sel) using (var b = new SolidBrush(Theme.SurfaceAlt)) g.FillRectangle(b, r.X, r.Y + Theme.S(4), r.Width, r.Height - Theme.S(8));
                TextRenderer.DrawText(g, items[i], Font, r, sel ? Theme.Accent : Theme.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X + Theme.S(8), r.Bottom - Theme.S(3), r.Width - Theme.S(16), Theme.S(3));
                if (sel && Focused) using (var p = new Pen(Theme.AccentLight) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, r.X + 2, r.Y + Theme.S(4), r.Width - 5, r.Height - Theme.S(10));
            }
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>自分で描く行のリスト (ListBox。↑↓・Home/End・Enter などのキー操作はそのまま使える)</summary>
    public class RowList : ListBox
    {
        /// <summary>行を描く (g, 行の範囲, 項目, 選択中か)。背景と区切り線は描いてある</summary>
        public Action<Graphics, Rectangle, object, bool> PaintRow;
        public string EmptyText = "";
        public RowList(int lines)
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            ItemHeight = Math.Min(255, Theme.Body.Height * lines + Theme.S(10) + Theme.S(4) * (lines - 1));
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        public object Selected { get { return SelectedItem; } }
        /// <summary>
        /// 表示されたとき・大きさが変わったとき: 選んでいる行が一番上から見える範囲にあれば一番上から見せる
        /// (mono などで、先に選んだ行まで勝手に送られて先頭の行が隠れることがあるため)
        /// </summary>
        void ShowFromTop()
        {
            if (!IsHandleCreated || Items.Count == 0 || TopIndex == 0 || ItemHeight <= 0) return;
            int last = Math.Max(SelectedIndex, 0);
            if ((last + 1) * ItemHeight <= ClientSize.Height) try { TopIndex = 0; } catch { }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ShowFromTop(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) ShowFromTop(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); ShowFromTop(); }
        public void SetItems(System.Collections.IEnumerable list, Func<object, bool> keepSelected = null)
        {
            object keep = SelectedItem;
            int top = TopIndex;
            BeginUpdate();
            Items.Clear();
            int sel = -1, i = 0;
            foreach (var o in list)
            {
                Items.Add(o);
                if (keepSelected != null && sel < 0 && keepSelected(o)) sel = i;
                i++;
            }
            if (sel >= 0) SelectedIndex = sel;
            else if (Items.Count > 0) SelectedIndex = 0;
            if (top < Items.Count) try { TopIndex = top; } catch { }
            EndUpdate();
            Invalidate();
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Surface)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.X + Theme.S(10), e.Bounds.Bottom - 1, e.Bounds.Right - Theme.S(10), e.Bounds.Bottom - 1);
            if (PaintRow != null)
            {
                var st = g.Save();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.SetClip(e.Bounds);
                PaintRow(g, e.Bounds, Items[e.Index], sel);
                g.Restore(st);
            }
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // 空のときの案内 (WM_PAINT の後に重ねて描く)
            if (m.Msg == 0x000F && Items.Count == 0 && !string.IsNullOrEmpty(EmptyText))
                using (var g = CreateGraphics())
                    TextRenderer.DrawText(g, EmptyText, Theme.Body, new Rectangle(Theme.S(16), Theme.S(16), Width - Theme.S(32), Height - Theme.S(32)), Theme.SubText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    /// <summary>行の中の描画の小物</summary>
    public static class Draw
    {
        public const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        public static void Text(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags extra = 0)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, Line | extra);
        }
        public static int Width(Graphics g, string s, Font f)
        {
            return TextRenderer.MeasureText(g, s ?? "", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }
        /// <summary>角丸の小さなラベル (右端 x から左へ置き、左端の x を返す)。alignRight=false なら x から右へ</summary>
        public static int Chip(Graphics g, string s, Color fore, Color back, int x, int cy, bool alignRight)
        {
            var f = Theme.Small;
            int w = Width(g, s, f) + Theme.S(12), h = f.Height + Theme.S(4);
            int left = alignRight ? x - w : x;
            var r = new Rectangle(left, cy - h / 2, w, h);
            using (var path = Theme.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), h / 2f))
            using (var b = new SolidBrush(back)) g.FillPath(b, path);
            TextRenderer.DrawText(g, s, f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            return alignRight ? left : left + w;
        }
    }

    /// <summary>大きな数字のタイル (ダッシュボード用)</summary>
    public class StatTile : Control
    {
        string caption = "", value = "", note = "";
        public Color ValueColor = Theme.Text;
        public StatTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = PreferredHeight;
        }
        public static Font ValueFont { get { return big ?? (big = Theme.MakeFont(new[] { "Meiryo UI", "Yu Gothic UI", "Noto Sans CJK JP", "MS UI Gothic" }, 20f, FontStyle.Bold)); } }
        static Font big;
        public int PreferredHeight { get { return Theme.Small.Height * 2 + ValueFont.Height + Theme.S(30); } }
        public void Set(string caption, string value, string note, Color? valueColor = null)
        {
            this.caption = caption; this.value = value; this.note = note; ValueColor = valueColor ?? Theme.Text;
            Invalidate();
        }
        public string ValueText { get { return value; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(PaintUtil.ParentBack(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rr = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(rr, Theme.S(Theme.Radius + 2)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            int x = Theme.S(16), w = Width - Theme.S(32), y = Theme.S(10);
            Draw.Text(g, caption, Theme.Small, Theme.SubText, new Rectangle(x, y, w, Theme.Small.Height + 2)); y += Theme.Small.Height + Theme.S(4);
            Draw.Text(g, value, ValueFont, ValueColor, new Rectangle(x, y, w, ValueFont.Height + 2)); y += ValueFont.Height + Theme.S(4);
            Draw.Text(g, note, Theme.Small, Theme.SubText, new Rectangle(x, y, w, Theme.Small.Height + 2));
        }
    }

    /// <summary>日本語の表記 (円・日付・時間)</summary>
    public static class Fmt
    {
        static readonly string[] Dow = { "日", "月", "火", "水", "木", "金", "土" };
        public static string Yen(decimal v) { return "¥" + Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.InvariantCulture); }
        public static string Yen(long v) { return "¥" + v.ToString("#,0", CultureInfo.InvariantCulture); }
        public static string DayOfWeek(DateTime d) { return Dow[(int)d.DayOfWeek]; }
        /// <summary>10/28(水)</summary>
        public static string MonthDay(DateTime d) { return d.Month + "/" + d.Day + "(" + DayOfWeek(d) + ")"; }
        /// <summary>2026/10/28(水)</summary>
        public static string Date(DateTime d) { return d.Year + "/" + d.Month + "/" + d.Day + "(" + DayOfWeek(d) + ")"; }
        public static string Time(DateTime d) { return d.ToString("H:mm", CultureInfo.InvariantCulture); }
        /// <summary>90 → 1時間30分</summary>
        public static string Minutes(int m)
        {
            if (m <= 0) return "";
            if (m < 60) return m + "分";
            return (m / 60) + "時間" + (m % 60 == 0 ? "" : (m % 60) + "分");
        }
        /// <summary>今日 / 明日 / 3日後 / 2日前</summary>
        public static string Relative(DateTime day, DateTime today)
        {
            int n = (int)(day.Date - today.Date).TotalDays;
            if (n == 0) return "今日";
            if (n == 1) return "明日";
            if (n == 2) return "明後日";
            if (n == -1) return "昨日";
            return n > 0 ? "あと" + n + "日" : (-n) + "日前";
        }
        public static string Iso(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static string IsoTime(DateTime d) { return d.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture); }
        public static DateTime? ParseIso(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DateTime d;
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }
    }
}
