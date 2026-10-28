// Windows でだけ確かめること (GitHub Actions の windows-latest。mono では「skip」と出して何もしない)
//   - 本物の DPAPI (Teal Pass の vault.dat・Teal Clip の history.dat): 中身が DPAPI の形式で、
//     エントロピーが違うと開けない・同じなら開ける (エントロピーは 1.0 から変えていない)
//   - 本物のレジストリ (HKCU\…\Run): テスト用の名前で書いて読んで消す
extern alias PM;
extern alias CH;
extern alias AM;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

static class WinTests
{
    public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

    // DPAPI の blob の先頭: dwVersion (1) + 既定のプロバイダー {df9d8cd0-1501-11d1-8c7a-00c04fc297eb}
    static readonly byte[] DpapiHeader = { 0x01, 0, 0, 0, 0xD0, 0x8C, 0x9D, 0xDF, 0x01, 0x15, 0xD1, 0x11, 0x8C, 0x7A, 0x00, 0xC0, 0x4F, 0xC2, 0x97, 0xEB };

    static bool HasHeader(byte[] b, int offset)
    {
        if (b == null || b.Length < offset + DpapiHeader.Length) return false;
        for (int i = 0; i < DpapiHeader.Length; i++) if (b[offset + i] != DpapiHeader[i]) return false;
        return true;
    }

    static bool Throws(Action a) { try { a(); return false; } catch (CryptographicException) { return true; } catch { return false; } }

    public static void RunDpapi(Action<bool, string> Check)
    {
        if (!IsWindows) { Console.WriteLine("  skip Windows のみ (本物の DPAPI)"); return; }
        // Teal Pass
        PM::TealSuite.Common.AppInfo.Init("TealPass", "Teal Pass", "");
        var v = new PM::TealSuite.Pass.VaultFile();
        v.Entries.Add(new PM::TealSuite.Pass.VaultEntry { Site = "Windows テスト", UserId = "ci", Password = "Win-DPAPI-秘密" });
        PM::TealSuite.Pass.VaultStore.Save(v);
        var raw = File.ReadAllBytes(PM::TealSuite.Common.DataStore.PathOf("vault.dat"));
        Check(Encoding.ASCII.GetString(raw, 0, 4) == "PMV1" && HasHeader(raw, 4), "Teal Pass: vault.dat は PMV1 + 本物の DPAPI の blob");
        var blob = raw.Skip(4).ToArray();
        var plain = ProtectedData.Unprotect(blob, Encoding.UTF8.GetBytes("AppSuite.PasswordMaker.vault.v1"), DataProtectionScope.CurrentUser);
        Check(Encoding.UTF8.GetString(plain).Contains("Win-DPAPI-秘密"), "Teal Pass: 決まったエントロピー (AppSuite.PasswordMaker.vault.v1) で開ける");
        Check(Throws(() => ProtectedData.Unprotect(blob, Encoding.UTF8.GetBytes("wrong"), DataProtectionScope.CurrentUser))
            && Throws(() => ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser)), "Teal Pass: エントロピーが違うと開けない");
        var back = PM::TealSuite.Pass.VaultStore.Load();
        Check(back.Entries.Count == 1 && back.Entries[0].Password == "Win-DPAPI-秘密", "Teal Pass: 読み戻せる");
        var corrupt = (byte[])raw.Clone(); corrupt[corrupt.Length - 5] ^= 0xFF;
        Check(Throws(() => PM::TealSuite.Pass.VaultStore.Unprotect(corrupt)), "Teal Pass: 壊れたファイルは開けない (例外になる)");

        // Teal Clip
        CH::TealSuite.Common.AppInfo.Init("TealClip", "Teal Clip", "");
        var h = new CH::TealSuite.Clip.History();
        h.Add("Windows のコピー履歴 秘密", DateTime.Now);
        CH::TealSuite.Clip.HistoryStore.Save(h);
        var hraw = File.ReadAllBytes(CH::TealSuite.Common.DataStore.PathOf(CH::TealSuite.Clip.HistoryStore.FileName));
        Check(HasHeader(hraw, 0), "Teal Clip: history.dat は本物の DPAPI の blob");
        var hplain = ProtectedData.Unprotect(hraw, Encoding.UTF8.GetBytes("AppSuite.ClipboardHistory.v1"), DataProtectionScope.CurrentUser);
        Check(Encoding.UTF8.GetString(hplain).Contains("Windows のコピー履歴"), "Teal Clip: 決まったエントロピー (AppSuite.ClipboardHistory.v1) で開ける");
        Check(Throws(() => ProtectedData.Unprotect(hraw, Encoding.UTF8.GetBytes("AppSuite.PasswordMaker.vault.v1"), DataProtectionScope.CurrentUser)), "Teal Clip: Teal Pass のエントロピーでは開けない");
        var hb = CH::TealSuite.Clip.HistoryStore.Load();
        Check(hb.Items.Count == 1 && hb.Items[0].Text == "Windows のコピー履歴 秘密", "Teal Clip: 読み戻せる");
    }

    public static void RunRegistry(Action<bool, string> Check)
    {
        if (!IsWindows) { Console.WriteLine("  skip Windows のみ (本物のレジストリ)"); return; }
        var rk = new AM::TealSuite.Common.RegistryRunKey();
        var name = "TealSuite.CiTest" + System.Diagnostics.Process.GetCurrentProcess().Id;
        var cmd = "\"C:\\TealSuite テスト\\apps\\TealWallet\\TealWallet.exe\" --startup";
        try
        {
            rk.Set(name, cmd);
            Check(rk.Get(name) == cmd && rk.Names().Contains(name), "HKCU\\…\\Run に書いて読める (空白・日本語のパス)");
            Check(!rk.IsDisabledByUser(name), "タスク マネージャーで無効にはなっていない");
            rk.Delete(name);
            Check(rk.Get(name) == null && !rk.Names().Contains(name), "消せる");
        }
        finally { try { rk.Delete(name); } catch { } }
    }
}
