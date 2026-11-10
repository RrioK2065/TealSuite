// Teal Macro - 記録した入力 (低レベル フックで受け取ったもの) を、読みやすい手順に直す (画面・Windows に関係しない計算)
//  1. Teal Macro のショートカット (記録の開始・終了など) のキーを取り除く。記録を始める前から押していたキーを離したもの、
//     最後まで離さなかったキー、Teal Macro の画面の上のクリックも取り除く
//  2. 続けて動いたマウスは 1 つの「マウスを動かす」にまとめる (間引いて最大 MaxPathPoints か所)
//  3. 同じ場所で押して離したボタンは「クリック」、押して離したキーは「キー」、Ctrl などを押しながらのキーは「キー Ctrl + C」にまとめる
//  4. それぞれの手順の前の間 (ミリ秒) を Delay に入れる。最初の手順の前の間と、最後の (停止ボタンへの) マウスの動きは入れない
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TealSuite.Common;

namespace TealSuite.Macro
{
    public enum RawKind { Move, Down, Up, Wheel, KeyDown, KeyUp }

    /// <summary>フックで受け取った 1 つの入力</summary>
    public class RawEvent
    {
        public RawKind Kind;
        /// <summary>ミリ秒 (記録の始めからでも、Windows の時刻でもよい。差だけを使う)</summary>
        public long Time;
        public int X, Y;
        public MouseBtn Button;
        public int Vk;
        public int Delta;
        /// <summary>Teal Macro 自身の画面の上 (クリックを記録しない)</summary>
        public bool Own;

        public static RawEvent Mv(long t, int x, int y) { return new RawEvent { Kind = RawKind.Move, Time = t, X = x, Y = y }; }
        public static RawEvent Btn(long t, bool down, MouseBtn b, int x, int y) { return new RawEvent { Kind = down ? RawKind.Down : RawKind.Up, Time = t, Button = b, X = x, Y = y }; }
        public static RawEvent Key(long t, bool down, Keys k) { return new RawEvent { Kind = down ? RawKind.KeyDown : RawKind.KeyUp, Time = t, Vk = (int)k }; }
        public override string ToString() { return Time + " " + Kind + " " + (Kind == RawKind.KeyDown || Kind == RawKind.KeyUp ? ((Keys)Vk).ToString() : X + "," + Y); }
    }

    public static class Recorder
    {
        public const int MaxPathPoints = 400;
        /// <summary>動きを間引く間隔 (ミリ秒)</summary>
        public const int MoveSampleMs = 15;
        /// <summary>押して離すまでがこれより短く、ほとんど動かなければ「クリック」</summary>
        public const int ClickMaxMs = 800, ClickSlopPx = 4;

        /// <summary>記録した入力を手順にする</summary>
        /// <param name="ignore">取り除くショートカット (Teal Macro の記録の開始・終了・緊急停止など)</param>
        public static List<MacroStep> Build(IList<RawEvent> raw, IEnumerable<HotkeySpec> ignore, bool recordMoves)
        {
            var ev = Clean(raw ?? new List<RawEvent>(), ignore ?? new HotkeySpec[0]);
            if (!recordMoves) ev = ev.Where(e => e.Kind != RawKind.Move).ToList();
            // 最後の動き (停止ボタンへ向かう) は使わない
            while (ev.Count > 0 && ev[ev.Count - 1].Kind == RawKind.Move) ev.RemoveAt(ev.Count - 1);

            var steps = new List<MacroStep>();
            long lastTime = ev.Count > 0 ? ev[0].Time : 0;
            int i = 0;
            while (i < ev.Count)
            {
                var e = ev[i];
                int delay = (int)Math.Max(0, Math.Min(MacroDef.MaxWaitMs, e.Time - lastTime));
                MacroStep st;
                int used = 1;
                long end = e.Time;
                if (e.Kind == RawKind.Move)
                {
                    int j = i;
                    while (j < ev.Count && ev[j].Kind == RawKind.Move) j++;
                    st = PathStep(ev, i, j);
                    used = j - i;
                    end = ev[j - 1].Time;
                }
                else if (e.Kind == RawKind.Down)
                {
                    // 押す → (少しの動き) → 離す = クリック
                    int j = i + 1;
                    while (j < ev.Count && ev[j].Kind == RawKind.Move && Near(ev[j], e)) j++;
                    if (j < ev.Count && ev[j].Kind == RawKind.Up && ev[j].Button == e.Button && ev[j].Time - e.Time <= ClickMaxMs && Near(ev[j], e))
                    {
                        st = MacroStep.Click(e.X, e.Y, e.Button);
                        used = j - i + 1;
                        end = ev[j].Time;
                    }
                    else st = new MacroStep { Kind = MacroStep.KDown, X = e.X, Y = e.Y, Button = MacroStep.BtnName(e.Button) };
                }
                else if (e.Kind == RawKind.Up) st = new MacroStep { Kind = MacroStep.KUp, X = e.X, Y = e.Y, Button = MacroStep.BtnName(e.Button) };
                else if (e.Kind == RawKind.Wheel) st = new MacroStep { Kind = MacroStep.KWheel, X = e.X, Y = e.Y, Delta = e.Delta };
                else if (e.Kind == RawKind.KeyDown)
                {
                    int n; HotkeySpec combo;
                    if (TryCombo(ev, i, out n, out combo))
                    {
                        st = MacroStep.KeyPress(combo.ToString());
                        used = n;
                        end = ev[i + n - 1].Time;
                    }
                    else st = new MacroStep { Kind = MacroStep.KKeyDown, Key = KeyName(e.Vk) };
                }
                else st = new MacroStep { Kind = MacroStep.KKeyUp, Key = KeyName(e.Vk) };

                st.Delay = steps.Count == 0 ? 0 : delay;
                steps.Add(st);
                lastTime = end;
                i += used;
                if (steps.Count >= MacroDef.MaxSteps) break;
            }
            // ダブルクリック: 同じ場所のクリック 2 回 (間がダブルクリックの時間内) をまとめる
            for (int k = steps.Count - 1; k > 0; k--)
            {
                var a = steps[k - 1]; var b = steps[k];
                if (a.Kind == MacroStep.KClick && b.Kind == MacroStep.KClick && a.Clicks == 1 && b.Clicks == 1 && a.Button == b.Button
                    && Math.Abs(a.X - b.X) <= ClickSlopPx && Math.Abs(a.Y - b.Y) <= ClickSlopPx && b.Delay <= 400)
                {
                    a.Clicks = 2;
                    steps.RemoveAt(k);
                }
            }
            return steps;
        }

        static string KeyName(int vk)
        {
            var k = (Keys)vk;
            switch (k)
            {
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "Shift";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "Alt";
                case Keys.LWin: case Keys.RWin: return "Win";
            }
            return HotkeySpec.KeyName(k);
        }

        static bool Near(RawEvent a, RawEvent b) { return Math.Abs(a.X - b.X) <= ClickSlopPx && Math.Abs(a.Y - b.Y) <= ClickSlopPx; }

        static MacroStep PathStep(List<RawEvent> ev, int from, int to)
        {
            var pts = new List<PathPoint>();
            long t0 = ev[from].Time;
            long lastT = 0;
            bool first = true;
            for (int k = from; k < to; k++)
            {
                var e = ev[k];
                bool last = k == to - 1;
                if (!first && !last && e.Time - lastT < MoveSampleMs) continue;
                first = false;
                pts.Add(new PathPoint { T = (int)Math.Min(MacroDef.MaxWaitMs, e.Time - t0), X = e.X, Y = e.Y });
                lastT = e.Time;
            }
            // 多すぎれば間引く (最初と最後は残す)
            if (pts.Count > MaxPathPoints)
            {
                var thin = new List<PathPoint>();
                double stepN = (pts.Count - 1) / (double)(MaxPathPoints - 1);
                for (int n = 0; n < MaxPathPoints; n++) thin.Add(pts[(int)Math.Round(n * stepN)]);
                pts = thin;
            }
            var endP = pts[pts.Count - 1];
            var st = MacroStep.Move(endP.X, endP.Y);
            if (pts.Count > 1) st.Path = pts;
            return st;
        }

        /// <summary>修飾キーを押す → キーを押して離す → 修飾キーを離す (または単独のキーを押して離す) をまとめる</summary>
        static bool TryCombo(List<RawEvent> ev, int i, out int used, out HotkeySpec combo)
        {
            used = 0; combo = new HotkeySpec();
            int j = i;
            var mods = new List<int>();
            while (j < ev.Count && ev[j].Kind == RawKind.KeyDown && KeyText.IsModifierVk(ev[j].Vk) && !mods.Contains(ev[j].Vk)) { mods.Add(ev[j].Vk); j++; }
            // 修飾キーの自動リピート (押しっぱなしで同じ KeyDown が続く) はとばす
            while (j < ev.Count && ev[j].Kind == RawKind.KeyDown && mods.Contains(ev[j].Vk)) j++;
            if (j + 1 >= ev.Count) return false;
            var d = ev[j];
            if (d.Kind != RawKind.KeyDown) return false;
            if (KeyText.IsModifierVk(d.Vk)) return false;
            int k = j + 1;
            while (k < ev.Count && ev[k].Kind == RawKind.KeyDown && ev[k].Vk == d.Vk) k++;   // 自動リピートは押しっぱなしとして残すため、まとめない
            if (k != j + 1) return false;
            if (k >= ev.Count || ev[k].Kind != RawKind.KeyUp || ev[k].Vk != d.Vk) return false;
            k++;
            var left = new List<int>(mods);
            while (left.Count > 0 && k < ev.Count && ev[k].Kind == RawKind.KeyUp && left.Contains(ev[k].Vk)) { left.Remove(ev[k].Vk); k++; }
            if (left.Count > 0) return false;
            combo.Key = (Keys)d.Vk;
            foreach (var m in mods) combo.Mods |= KeyText.ModOf(m);
            used = k - i;
            return true;
        }

        /// <summary>
        /// 取り除くもの: 押していないのに離したキー・ボタン (記録の前から押していた)、最後まで離さなかったキー・ボタン、
        /// ショートカットのキー (そのとき押していた修飾キーごと)、Teal Macro の画面の上のクリック
        /// </summary>
        public static List<RawEvent> Clean(IList<RawEvent> raw, IEnumerable<HotkeySpec> ignore)
        {
            var hot = ignore.Where(h => !h.IsEmpty).ToList();
            var keep = new bool[raw.Count];
            var downKeys = new Dictionary<int, int>();     // vk → KeyDown の位置
            var downBtns = new Dictionary<MouseBtn, int>();
            var droppedKeys = new HashSet<int>();          // 離すまで捨てるキー
            var droppedBtns = new HashSet<MouseBtn>();
            for (int i = 0; i < raw.Count; i++)
            {
                var e = raw[i];
                switch (e.Kind)
                {
                    case RawKind.Move: case RawKind.Wheel:
                        keep[i] = !(e.Kind == RawKind.Wheel && e.Own);
                        break;
                    case RawKind.Down:
                        if (e.Own) { droppedBtns.Add(e.Button); break; }
                        if (downBtns.ContainsKey(e.Button)) break;
                        downBtns[e.Button] = i; keep[i] = true;
                        break;
                    case RawKind.Up:
                        if (droppedBtns.Remove(e.Button)) break;
                        if (!downBtns.ContainsKey(e.Button)) break;   // 記録の前から押していた
                        downBtns.Remove(e.Button); keep[i] = true;
                        break;
                    case RawKind.KeyDown:
                        if (droppedKeys.Contains(e.Vk)) break;
                        if (!KeyText.IsModifierVk(e.Vk))
                        {
                            var mods = HotMods.None;
                            foreach (var vk in downKeys.Keys) mods |= KeyText.ModOf(vk);
                            if (hot.Any(h => h.Key == (Keys)e.Vk && h.Mods == mods))
                            {
                                // ショートカット: このキーと、いま押している修飾キーを捨てる
                                droppedKeys.Add(e.Vk);
                                foreach (var kv in downKeys.ToList()) { keep[kv.Value] = false; droppedKeys.Add(kv.Key); downKeys.Remove(kv.Key); }
                                break;
                            }
                        }
                        if (downKeys.ContainsKey(e.Vk)) { keep[i] = !KeyText.IsModifierVk(e.Vk); break; }   // 押しっぱなし (自動リピート)。Shift などは 1 回だけ
                        downKeys[e.Vk] = i; keep[i] = true;
                        break;
                    case RawKind.KeyUp:
                        if (droppedKeys.Remove(e.Vk)) break;
                        if (!downKeys.ContainsKey(e.Vk)) break;   // 記録の前から押していた (記録開始のショートカットなど)
                        downKeys.Remove(e.Vk); keep[i] = true;
                        break;
                }
            }
            // 最後まで離さなかったもの (記録を止めたショートカットの修飾キーなど) は、押したところから捨てる
            foreach (var kv in downKeys) DropFrom(raw, keep, kv.Value, e => (e.Kind == RawKind.KeyDown) && e.Vk == kv.Key);
            foreach (var kv in downBtns) keep[kv.Value] = false;
            var list = new List<RawEvent>();
            for (int i = 0; i < raw.Count; i++) if (keep[i]) list.Add(raw[i]);
            return list;
        }

        static void DropFrom(IList<RawEvent> raw, bool[] keep, int from, Func<RawEvent, bool> same)
        {
            for (int i = from; i < raw.Count; i++) if (same(raw[i])) keep[i] = false;
        }
    }
}
