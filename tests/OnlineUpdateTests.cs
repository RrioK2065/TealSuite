// Teal Hub のオンライン アップデート確認 (OnlineUpdate.cs・HubUpdates.cs) のテスト
// 通信は FakeFetcher で差し替える (本物の GitHub には問い合わせない)
extern alias AM;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using H = AM::TealSuite.Hub;
using C = AM::TealSuite.Common;

static class OnlineUpdateTests
{
    static Action<bool, string> Check;

    class FakeFetcher : H.WebFetcher
    {
        public readonly Dictionary<string, H.FetchResult> Map = new Dictionary<string, H.FetchResult>();
        public readonly List<string> Calls = new List<string>();
        public H.FetchResult Default = new H.FetchResult { Status = 0, Error = "offline" };
        public override H.FetchResult Fetch(string url, bool binary, long maxBytes, Action<long, long> progress)
        {
            Calls.Add(url);
            H.FetchResult r;
            if (!Map.TryGetValue(url, out r)) return Default;
            if (progress != null && r.Body != null) { progress(r.Body.Length / 2, r.Body.Length); progress(r.Body.Length, r.Body.Length); }
            return r;
        }
        public void Ok(string url, byte[] body) { Map[url] = new H.FetchResult { Status = 200, Body = body, FinalUri = new Uri(url) }; }
        public void Ok(string url, string body) { Ok(url, Encoding.UTF8.GetBytes(body)); }
    }

    static string FixturePath(string dist)
    {
        // dist = dist/TealSuite → リポジトリ直下の tests/fixtures
        return Path.GetFullPath(Path.Combine(dist, "..", "..", "tests", "fixtures", "release-latest.json"));
    }

    static byte[] MakePackage()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        using (var w = new StreamWriter(z.CreateEntry("update.json").Open())) w.Write("{\"format\":1}");
        return ms.ToArray();
    }

    static bool Throws(Action a, string contains)
    {
        try { a(); return false; }
        catch (InvalidOperationException ex) { return contains == null || ex.Message.Contains(contains); }
    }

    public static void RunLogic(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        Environment.SetEnvironmentVariable("TEALSUITE_UPDATE_API", null);
        // バージョン
        Check(H.OnlineUpdate.VersionFromTag("v1.6.0") == "1.6.0" && H.OnlineUpdate.VersionFromTag("1.6") == "1.6" && H.OnlineUpdate.VersionFromTag("latest") == null && H.OnlineUpdate.VersionFromTag("v1.6.0-beta") == null, "タグ → バージョン (v1.6.0 → 1.6.0、ほかは無視)");
        Check(H.OnlineUpdate.CompareVersions("1.10.0", "1.9.0") > 0 && H.OnlineUpdate.CompareVersions("1.5", "1.5.0") == 0 && H.OnlineUpdate.CompareVersions("1.4.9", "1.5.0") < 0 && H.OnlineUpdate.CompareVersions("2.0.0", "1.99.99") > 0, "バージョンの比べ方 (数字ごと)");
        Check(H.UpdateSource.ApiUrl == "https://api.github.com/repos/RrioK2065/TealSuite/releases/latest", "確認先: " + H.UpdateSource.ApiUrl);

        // リリース JSON
        var json = File.ReadAllBytes(FixturePath(dist));
        var r = H.OnlineUpdate.ParseRelease(json);
        Check(r != null && r.Version == "1.7.0" && r.Title == "TealSuite 1.7.0" && r.Assets.Count == 4 && r.PageUrl.EndsWith("/tag/v1.7.0"), "リリース JSON を読む (1.7.0・アセット 4 個)");
        Check(r.Package != null && r.Package.Name == "TealSuite-update-1.7.0.tealupdate" && r.Package.Size == 798000, "アップデート ファイルを選ぶ: " + (r.Package != null ? r.Package.Name : "null"));
        Check(r.Checksum != null && r.Checksum.Name == "TealSuite-update-1.7.0.tealupdate.sha256", "対になる .sha256 を見つける");
        Check(r.Summary.StartsWith("Teal Memo に検索"), "お知らせの 1 行目: " + r.Summary);
        var text = Encoding.UTF8.GetString(json);
        Check(H.OnlineUpdate.ParseRelease(Encoding.UTF8.GetBytes(text.Replace("\"draft\": false", "\"draft\": true"))) == null, "下書きは無視");
        Check(H.OnlineUpdate.ParseRelease(Encoding.UTF8.GetBytes(text.Replace("\"prerelease\": false", "\"prerelease\": true"))) == null, "プレリリースは無視");

        // アセットの選び方
        Func<string, H.GhAsset> A = n => new H.GhAsset { Name = n, Url = "https://github.com/x/" + n };
        var sel = H.OnlineUpdate.SelectPackage(new[] { A("TealSuite-1.7.0.zip"), A("Other-1.6.0.tealupdate"), A("TealSuite-1.7.0.tealupdate") }, "1.7.0");
        Check(sel != null && sel.Name == "TealSuite-1.7.0.tealupdate", "決まった名前がなければ、バージョンを含む .tealupdate (" + (sel != null ? sel.Name : "null") + ")");
        Check(H.OnlineUpdate.SelectPackage(new[] { A("TealSuite-1.7.0.zip"), A("a.sha256") }, "1.7.0") == null, ".tealupdate がなければ選ばない");
        Check(H.OnlineUpdate.SelectPackage(new[] { A("x.tealupdate"), A("TealSuite-update-1.7.0.tealupdate") }, "1.7.0").Name == "TealSuite-update-1.7.0.tealupdate", "決まった名前を優先");

        // sha256
        var h = new string('a', 64);
        Check(H.OnlineUpdate.ParseShaFile(h + "  TealSuite-update-1.7.0.tealupdate\n", "TealSuite-update-1.7.0.tealupdate") == h, ".sha256 (sha256sum 形式) を読む");
        Check(H.OnlineUpdate.ParseShaFile(h.ToUpperInvariant() + "\r\n", "x.tealupdate") == h, "値だけの .sha256 も読む (小文字にそろえる)");
        Check(H.OnlineUpdate.ParseShaFile(h + "  other.zip", "x.tealupdate") == null && H.OnlineUpdate.ParseShaFile("hello", "x") == null, "違うファイルの値・形の違う中身は使わない");
        Check(H.OnlineUpdate.ShaFromNotes(r.Notes, "TealSuite-update-1.7.0.tealupdate") == new string('1', 64) && H.OnlineUpdate.ShaFromNotes(r.Notes, "TealSuite-1.7.0.zip") == new string('0', 64), "説明文の sha256 の行を読む");
        Check(H.OnlineUpdate.ShaFromNotes("| `" + h + "` | `x.tealupdate` |", "x.tealupdate") == h && H.OnlineUpdate.ShaFromNotes("なし", "x.tealupdate") == null, "表の形でも読める・なければ null");
        Check(H.OnlineUpdate.Sha256Hex(Encoding.ASCII.GetBytes("abc")) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "sha256 の計算");

        // 接続先
        Check(H.UpdateSource.IsAllowed(new Uri("https://api.github.com/x")) && H.UpdateSource.IsAllowed(new Uri("https://objects.githubusercontent.com/y")) && H.UpdateSource.IsAllowed(new Uri("https://release-assets.githubusercontent.com/z")), "GitHub の HTTPS は使う");
        Check(!H.UpdateSource.IsAllowed(new Uri("http://github.com/x")) && !H.UpdateSource.IsAllowed(new Uri("https://github.com.evil.example/x")) && !H.UpdateSource.IsAllowed(new Uri("https://example.com/x")) && !H.UpdateSource.IsAllowed(new Uri("https://evilgithubusercontent.com/x")), "HTTP・ほかの場所は使わない");

        // 1 日 1 回・無効
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var s = new H.HubSettings();
        Check(H.OnlineUpdate.IsDue(s, now), "まだ確認していなければ確認する");
        s.LastUpdateCheck = "2026-10-26T01:00:00Z";
        Check(!H.OnlineUpdate.IsDue(s, now), "24 時間たっていなければ確認しない");
        s.LastUpdateCheck = "2026-10-25T11:59:00Z";
        Check(H.OnlineUpdate.IsDue(s, now), "24 時間たったら確認する");
        s.LastUpdateCheck = "2027-01-01T00:00:00Z";
        Check(H.OnlineUpdate.IsDue(s, now), "時計が戻った (前回が未来) ときも確認する");
        s.UpdateCheckOff = true;
        Check(!H.OnlineUpdate.IsDue(s, now), "自動の確認をオフにしたら確認しない");
        var sj = C.DataStore.FromJson<H.HubSettings>(C.DataStore.ToJson(s));
        Check(sj.UpdateCheckOff && sj.LastUpdateCheck == "2027-01-01T00:00:00Z", "設定の保存と読み込み");
        var oldSettings = C.DataStore.FromJson<H.HubSettings>(Encoding.UTF8.GetBytes("{\"trayTipShown\":true}"));
        Check(oldSettings.TrayTipShown && !oldSettings.UpdateCheckOff && oldSettings.LastUpdateCheck == null, "1.4 の設定ファイル: 自動の確認はオン");

        // 確認の結果
        var api = H.UpdateSource.ApiUrl;
        var http = new FakeFetcher();
        var ou = new H.OnlineUpdate(http);
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Offline && http.Calls.Count == 1 && http.Calls[0] == api, "つながらない → Offline");
        http.Map[api] = new H.FetchResult { Status = 404 };
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.NoRelease, "404 (リリースなし) → NoRelease");
        http.Map[api] = new H.FetchResult { Status = 403, RateRemaining = "0" };
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.RateLimited, "403 → RateLimited");
        http.Map[api] = new H.FetchResult { Status = 500 };
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Error, "500 → Error");
        http.Map[api] = new H.FetchResult { Status = 200, Body = Encoding.UTF8.GetBytes("<html>"), FinalUri = new Uri(api) };
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Error, "壊れた応答 → Error");
        http.Map[api] = new H.FetchResult { Status = 200, Body = json, FinalUri = new Uri("https://evil.example/latest") };
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Error, "GitHub 以外への転送 → Error");
        http.Ok(api, json);
        var cr = ou.Check("1.6.0");
        Check(cr.Outcome == H.CheckOutcome.NewVersion && cr.IsNew && cr.Release.Version == "1.7.0", "新しいバージョン (1.6.0 → 1.7.0)");
        Check(ou.Check("1.7.0").Outcome == H.CheckOutcome.UpToDate && ou.Check("1.8.0").Outcome == H.CheckOutcome.UpToDate, "同じか新しければ最新");
        http.Ok(api, text.Replace(".tealupdate\"", ".bin\""));
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Error, "新しい版に .tealupdate がなければ Error");
        var fixture = FixturePath(dist);
        Environment.SetEnvironmentVariable("TEALSUITE_UPDATE_API", fixture);
        var calls = http.Calls.Count;
        Check(ou.Check("1.6.0").IsNew && http.Calls.Count == calls, "TEALSUITE_UPDATE_API に見本のファイル (通信しない)");
        Environment.SetEnvironmentVariable("TEALSUITE_UPDATE_API", "http://example.com/latest");
        Check(ou.Check("1.6.0").Outcome == H.CheckOutcome.Error && http.Calls.Count == calls, "差し替え先も HTTPS の GitHub だけ");
        Environment.SetEnvironmentVariable("TEALSUITE_UPDATE_API", null);

        // ダウンロード
        var dl = Path.Combine(tmp, "download");
        if (Directory.Exists(dl)) Directory.Delete(dl, true);
        Environment.SetEnvironmentVariable("TEALSUITE_DOWNLOAD_DIR", dl);
        try
        {
            var pkg = MakePackage();
            var sha = H.OnlineUpdate.Sha256Hex(pkg);
            Func<H.ReleaseInfo> fresh = () => { var x = H.OnlineUpdate.ParseRelease(json); x.Package.Size = pkg.Length; return x; };
            var rel = fresh();
            http.Ok(rel.Package.Url, pkg);
            http.Ok(rel.Checksum.Url, sha + "  TealSuite-update-1.7.0.tealupdate\n");
            Directory.CreateDirectory(dl);
            File.WriteAllText(Path.Combine(dl, "TealSuite-update-1.5.9.tealupdate"), "old");
            var pcts = new List<int>();
            var path = ou.Download(rel, p => pcts.Add(p));
            Check(path == Path.Combine(dl, "TealSuite-update-1.7.0.tealupdate") && File.ReadAllBytes(path).SequenceEqual(pkg), "ダウンロードして保存: " + path);
            Check(!File.Exists(Path.Combine(dl, "TealSuite-update-1.5.9.tealupdate")), "前にダウンロードした古いファイルは消す");
            Check(pcts.Count > 0 && pcts.Last() == 100, "進み具合 (" + string.Join(",", pcts) + ")");

            http.Ok(rel.Checksum.Url, new string('b', 64) + "  TealSuite-update-1.7.0.tealupdate\n");
            File.Delete(path);
            Check(Throws(() => ou.Download(fresh(), null), "一致しません") && !File.Exists(path), "sha256 が違えば保存しない");

            var noSha = fresh(); noSha.Checksum = null; noSha.Notes = "変更点だけ";
            Check(Throws(() => ou.Download(noSha, null), "sha256 が公開されていない"), "確認用の値がなければダウンロードしない");
            var notesSha = fresh(); notesSha.Checksum = null; notesSha.Notes = "```\n" + sha + "  TealSuite-update-1.7.0.tealupdate\n```";
            Check(ou.Download(notesSha, null) == path, ".sha256 がなければ説明文の値で確かめる");

            var shortSize = fresh(); shortSize.Package.Size = pkg.Length + 10; shortSize.Checksum = null; shortSize.Notes = notesSha.Notes;
            Check(Throws(() => ou.Download(shortSize, null), "途中で切れました"), "大きさが違えば使わない");

            var notZip = Encoding.ASCII.GetBytes("not a zip file");
            var nz = fresh(); nz.Package.Size = notZip.Length; nz.Checksum = null; nz.Notes = H.OnlineUpdate.Sha256Hex(notZip) + "  TealSuite-update-1.7.0.tealupdate";
            http.Ok(nz.Package.Url, notZip);
            Check(Throws(() => ou.Download(nz, null), "アップデート ファイルではありません"), "zip でなければ使わない");
            http.Ok(nz.Package.Url, pkg);

            var evil = fresh(); evil.Package.Url = "https://evil.example/TealSuite-update-1.7.0.tealupdate";
            calls = http.Calls.Count;
            Check(Throws(() => ou.Download(evil, null), "GitHub 以外"), "GitHub 以外の URL からはダウンロードしない");
            var redirected = fresh(); redirected.Checksum = null; redirected.Notes = notesSha.Notes;
            http.Map[redirected.Package.Url] = new H.FetchResult { Status = 200, Body = pkg, FinalUri = new Uri("https://evil.example/x") };
            Check(Throws(() => ou.Download(redirected, null), "想定外"), "GitHub 以外への転送は使わない");
            var badName = fresh(); badName.Package.Name = "../x.tealupdate";
            Check(Throws(() => ou.Download(badName, null), null), "おかしな名前は使わない");
        }
        finally { Environment.SetEnvironmentVariable("TEALSUITE_DOWNLOAD_DIR", null); }
    }

    /// <summary>画面: お知らせ・ボタンの印・メニュー・今すぐアップデート → TealUpdater (--expect-version)</summary>
    public static void RunUi(Action<bool, string> check, string tmp, string dist)
    {
        Check = check;
        var oldData = Environment.GetEnvironmentVariable("TEALSUITE_DATA_DIR");
        var data = Path.Combine(tmp, "online-ui");
        if (Directory.Exists(data)) Directory.Delete(data, true);
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", data);
        Environment.SetEnvironmentVariable("TEALSUITE_MANIFEST", Path.Combine(dist, "apps.json"));
        Environment.SetEnvironmentVariable("TEALSUITE_UPDATE_API", null);
        var dl = Path.Combine(tmp, "online-ui-dl");
        Environment.SetEnvironmentVariable("TEALSUITE_DOWNLOAD_DIR", dl);
        C.AppInfo.Init(new C.AppSpec { Id = "TealHub", Name = "Teal Hub", Role = "アプリ管理", LegacyIds = new[] { "AppManager" } });
        C.Startup.RunKey = new C.MemoryRunKey();
        var launched = new List<string>();
        H.UpdaterLauncher.StartProcess = (exe, a) => launched.Add(Path.GetFileName(exe) + " " + a);
        var f = new H.MainForm(null);
        try
        {
            f.LoadManifest();
            Check(f.SuiteVersion == "1.9.1", "今のバージョン: " + f.SuiteVersion);
            var settings = new H.HubSettings();
            int saves = 0;
            var http = new FakeFetcher();
            var u = new H.HubUpdates(f, settings, () => saves++, http) { Synchronous = true };
            var msgs = new List<string>(); var notes = new List<H.ReleaseInfo>(); var balloons = new List<string>();
            u.Message = m => msgs.Add(m);
            u.ShowNotes = r => notes.Add(r);
            u.Balloon = (t, b) => balloons.Add(t);
            var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
            u.UtcNow = () => now;
            f.Updates = u;
            Check(f.UpdateButtonLabel == "アップデート ▾" && f.NoticeBars.All(b => !b.Message.Contains("新しいバージョン")), "最初は印もお知らせもない");

            // 自動: オフライン → 何も出さない・記録しない (次にまた確認)
            Check(u.AutoCheck() && msgs.Count == 0 && settings.LastUpdateCheck == null && u.Available == null, "つながらなければ何も出さず、確認した時刻も残さない");
            // 自動: 404 → 何も出さない・記録する
            var api = H.UpdateSource.ApiUrl;
            http.Map[api] = new H.FetchResult { Status = 404 };
            Check(u.AutoCheck() && msgs.Count == 0 && settings.LastUpdateCheck == "2026-10-26T12:00:00Z", "リリースがなくても何も出さない (時刻は記録)");
            Check(!u.AutoCheck(), "同じ日にはもう確認しない");
            // 手動: 404 → 結果を出す
            u.CheckNow();
            Check(msgs.Count == 1 && msgs[0].Contains("まだ公開"), "「更新を確認」では結果を出す: " + (msgs.Count > 0 ? msgs[0] : ""));

            // 新しいバージョン (自動)
            var json = Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath(dist)).Replace("1.7.0", "2.0.0")); // 今は 1.9.1 なので、見本は 1 つ先の版にする;
            http.Ok(api, json);
            now = now.AddHours(25);
            int before = saves;
            Check(u.AutoCheck() && u.Available != null && u.Available.Version == "2.0.0" && msgs.Count == 1 && notes.Count == 0, "翌日の自動の確認で 2.0.0 を見つける (画面は出さない)");
            Check(balloons.Count == 1 && balloons[0].Contains("2.0.0") && settings.NotifiedVersion == "2.0.0" && saves > before, "通知領域に 1 回お知らせして、設定を保存");
            Check(f.UpdateButtonLabel.StartsWith("●") && f.UpdateButtonLabel.Contains("2.0.0"), "ボタンに印: " + f.UpdateButtonLabel);
            var bar = f.NoticeBars.FirstOrDefault(b => b.Message.Contains("2.0.0"));
            Check(bar != null && bar.Action.Text == "今すぐアップデート" && bar.Extra != null && bar.Extra.Text == "内容を見る" && bar.Dismiss != null, "お知らせ: 今すぐアップデート・内容を見る・あとで");
            Check(bar != null && bar.Message.Contains("今は 1.9.1") && bar.Message.Contains("Teal Memo に検索"), "お知らせにバージョンと変更点の 1 行目: " + (bar != null ? bar.Message.Replace("\n", " / ") : ""));
            now = now.AddHours(25);
            u.AutoCheck();
            Check(balloons.Count == 1, "同じバージョンの通知領域のお知らせは 1 回だけ");

            bar.Extra.SimulateClick();
            Check(notes.Count == 1 && notes[0].Version == "2.0.0", "「内容を見る」で変更点");
            var cleaned = H.ReleaseNotesForm.CleanNotes(u.Available.Notes);
            Check(cleaned.Contains("・Teal Memo に検索") && !cleaned.Contains("1111111111") && !cleaned.Contains("sha256"), "変更点の画面には確認用の値を出さない");
            using (var nf = new H.ReleaseNotesForm(u, u.Available))
                Check(nf.Text.Contains("2.0.0") && nf.Notes.Text.Contains("Teal Clip の履歴") && nf.BtnUpdate.Text == "今すぐアップデート", "変更点の画面");

            // メニュー
            f.BuildUpdateMenu();
            var items = f.UpdateMenu.Items.OfType<ToolStripMenuItem>().ToList();
            Check(items.Any(i => i.Text == "2.0.0 にアップデート…") && items.Any(i => i.Text == "更新を確認") && items.Any(i => i.Text == "リリース ページを開く"), "メニュー: " + string.Join(" / ", items.Select(i => i.Text)));
            var auto = items.First(i => i.Text.StartsWith("自動で更新を確認"));
            Check(auto.Checked, "自動の確認は最初オン");
            auto.PerformClick();
            Check(settings.UpdateCheckOff && !u.AutoCheckEnabled, "メニューで自動の確認をオフ");
            now = now.AddDays(3);
            int count = u.CheckCount;
            Check(!u.AutoCheck() && u.CheckCount == count, "オフなら自動では確認しない");
            u.CheckNow();
            Check(u.CheckCount == count + 1 && notes.Count == 2, "オフでも「更新を確認」は使える (新しい版の内容を出す)");
            f.BuildUpdateMenu();
            Check(!f.UpdateMenu.Items.OfType<ToolStripMenuItem>().First(i => i.Text.StartsWith("自動で更新を確認")).Checked, "メニューのチェックも外れる");
            u.SetAutoCheck(true);

            // あとで
            bar = f.NoticeBars.First(b => b.Message.Contains("2.0.0"));
            bar.Dismiss.SimulateClick();
            Check(!f.NoticeBars.Any(b => b.Message.Contains("2.0.0")) && f.UpdateButtonLabel.StartsWith("●"), "「あとで」でお知らせは消えるが、ボタンの印は残る");

            // 今すぐアップデート: ダウンロード失敗 → 案内
            msgs.Clear();
            u.StartUpdate();
            Check(msgs.Count == 1 && launched.Count == 0 && !u.Downloading, "ダウンロードできなければ案内だけ: " + (msgs.Count > 0 ? msgs[0].Replace("\n", " ") : ""));
            // 成功 → TealUpdater に --expect-version 付きで渡す
            var pkg = MakePackage();
            var rel = u.Available;
            rel.Package.Size = pkg.Length;
            http.Ok(rel.Package.Url, pkg);
            http.Ok(rel.Checksum.Url, H.OnlineUpdate.Sha256Hex(pkg) + "  " + rel.Package.Name + "\n");
            f.BuildUpdateMenu();
            u.StartUpdate();
            var expectPath = Path.Combine(dl, "TealSuite-update-2.0.0.tealupdate");
            Check(launched.Count == 1 && launched[0] == "TealUpdater.exe \"" + expectPath + "\" --expect-version \"2.0.0\"", "ダウンロード → TealUpdater に渡す: " + (launched.Count > 0 ? launched[0] : "(なし)"));
            Check(File.Exists(expectPath) && !u.Downloading && u.DownloadPercent == -1, "ダウンロード先は TealSuite-download");

            // 最新になったら印は消える
            http.Ok(api, Encoding.UTF8.GetString(json).Replace("v2.0.0\"", "v1.9.1\""));
            msgs.Clear();
            u.CheckNow();
            Check(u.Available == null && f.UpdateButtonLabel == "アップデート ▾" && msgs.Count == 1 && msgs[0].Contains("最新"), "最新なら印を消して「最新のバージョンです」");
            // 通信の失敗では、前に見つけた新しい版のお知らせを消さない
            http.Ok(api, json); u.CheckNow();
            http.Map.Remove(api); msgs.Clear(); u.CheckNow();
            Check(u.Available != null && msgs.Count == 1 && msgs[0].Contains("つながらない"), "つながらなくても前に見つけたお知らせは残す");
            u.Dispose();
        }
        finally
        {
            f.Dispose();
            H.UpdaterLauncher.StartProcess = null;
            Environment.SetEnvironmentVariable("TEALSUITE_DOWNLOAD_DIR", null);
            Environment.SetEnvironmentVariable("TEALSUITE_DATA_DIR", oldData);
        }
    }
}
