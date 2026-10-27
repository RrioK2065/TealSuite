// コピー履歴のロジックテスト
extern alias CH;
using System;
using System.Linq;
using H = CH::TealSuite.Clip;
using HC = CH::TealSuite.Common;

static class CHTests
{
    public static void Run(Action<bool, string> Check)
    {
        CH::TealSuite.Common.AppInfo.Init("TealClip", "Teal Clip", "");
        var t0 = new DateTime(2026, 10, 1, 9, 0, 0);
        var h = new H.History();
        for (int i = 1; i <= 12; i++) h.Add("item" + i, t0.AddMinutes(i));
        Check(h.Items.Count == 10, "10 件まで保存 (" + h.Items.Count + ")");
        Check(h.Items[0].Text == "item12" && h.Items[9].Text == "item3", "新しい順・古いものから消える");
        Check(!h.Add("   \r\n"), "空白だけは記録しない");
        h.Add("item5", t0.AddMinutes(20));
        Check(h.Items.Count == 10 && h.Items[0].Text == "item5" && h.Items.Count(x => x.Text == "item5") == 1, "同じ文字は重複させず先頭へ");
        Check(!h.Add("item5", t0.AddMinutes(21)), "先頭と同じならそのまま");

        var old = h.Items.First(x => x.Text == "item3");
        Check(h.SetPinned(old, true), "ピン留め");
        Check(h.Items[0] == old, "ピン留めは先頭に固定");
        for (int i = 13; i <= 30; i++) h.Add("item" + i, t0.AddMinutes(30 + i));
        Check(h.Items.Contains(old) && h.Items.Count == 10, "ピン留めは古くても消えない");
        foreach (var it in h.Items.ToList()) h.SetPinned(it, true);
        Check(h.PinnedCount == H.History.MaxPins, "ピン留めは最大 " + H.History.MaxPins + " 件 (新しいコピーの枠を残す)");
        h.Add("new!", t0.AddHours(5));
        Check(h.Items.Any(x => x.Text == "new!") && h.Items.Count == 10, "全部ピン留めでも新しいコピーは入る");
        Check(h.Remove(old) && !h.Items.Contains(old), "1 件削除");
        h.Clear();
        Check(h.Items.Count == 0, "すべて削除");

        // 保存と読み込み
        var h2 = new H.History();
        h2.Add("秘密のメモ: ABC", t0); h2.Add("二行目\r\nあり", t0.AddMinutes(1));
        H.HistoryStore.Save(h2);
        var raw = System.IO.File.ReadAllBytes(CH::TealSuite.Common.DataStore.PathOf(H.HistoryStore.FileName));
        var rawText = System.Text.Encoding.UTF8.GetString(raw) + System.Text.Encoding.Unicode.GetString(raw);
        Check(!rawText.Contains("秘密") && !rawText.Contains("ABC"), "history.dat に平文が含まれない");
        var h3 = H.HistoryStore.Load();
        Check(h3.Items.Count == 2 && h3.Items[0].Text == "二行目\r\nあり", "保存して読み戻せる (改行も保持)");
        Check(h3.Items[0].OneLine == "二行目 ⏎ あり", "一覧用の 1 行表示");

        // ショートカット
        HC.HotkeySpec k;
        Check(H.TrayContext.DefaultHotkey.ToString() == "Ctrl+Shift+V", "初期設定は Ctrl+Shift+V");
        Check(HC.HotkeySpec.TryParse("Ctrl+Shift+V", out k) && k.Key == System.Windows.Forms.Keys.V && k.Mods == (HC.HotMods.Ctrl | HC.HotMods.Shift), "文字列から読み込み");
        Check(HC.HotkeySpec.TryParse("alt+win+1", out k) && k.ToString() == "Alt+Win+1", "数字キー・大文字小文字を問わない");
        Check(HC.HotkeySpec.TryParse("F9", out k), "F キーは単独でも可");
        Check(!HC.HotkeySpec.TryParse("V", out k), "文字キー単独は不可");
        Check(!HC.HotkeySpec.TryParse("Shift+V", out k), "Shift+文字だけは不可");
        Check(!HC.HotkeySpec.TryParse("Ctrl+C", out k), "Ctrl+C は不可");
        Check(!HC.HotkeySpec.TryParse("Ctrl+Hoge", out k) && !HC.HotkeySpec.TryParse("", out k), "おかしな文字列は不可");
        var fromKey = HC.HotkeySpec.FromKeyEvent(System.Windows.Forms.Keys.B | System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt, false);
        Check(fromKey.ToString() == "Ctrl+Alt+B" && HC.HotkeySpec.Validate(fromKey) == null, "押したキーから組み立て (Ctrl+Alt+B)");
        Check(HC.HotkeySpec.FromKeyEvent(System.Windows.Forms.Keys.ShiftKey | System.Windows.Forms.Keys.Shift, false).IsEmpty, "修飾キーだけでは未確定");
        Check(HC.HotkeySpec.Validate(H.TrayContext.DefaultHotkey) == null, "初期設定は有効");
    }
}
