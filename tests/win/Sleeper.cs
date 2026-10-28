// テスト用のダミー アプリ (Windows のみ: tests/SmokeTests.cs の「Teal Hub: 起動と終了」)
//   画面を持たず、終了の合図にも応じずに眠り続ける (mono では代わりにシェル スクリプトを使う)
//   build.ps1 -Tests が build\Sleeper.exe を作る
static class Sleeper
{
    static void Main() { System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite); }
}
