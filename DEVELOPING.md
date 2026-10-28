# TealSuite 開発メモ

Windows 用の小さなアプリ群「Teal ○○」です。すべて **C# 5 / WinForms / .NET Framework 4.5** で書かれ、
インストール不要の単体 exe になります。Linux では mono の `mcs`、Windows では OS 標準の `csc.exe` でビルドできます。

| ID (フォルダ・exe 名) | 表示名 | 役割 (日本語のサブタイトル) | 1.0 までの ID |
|---|---|---|---|
| `TealHub` | Teal Hub | アプリ管理 | AppManager |
| `TealPass` | Teal Pass | パスワード管理 | PasswordMaker |
| `TealMemo` | Teal Memo | メモ | Memo |
| `TealClip` | Teal Clip | コピー履歴 | ClipboardHistory |
| `TealVolume` | Teal Volume | 音量操作 | VolumeMixer |
| `TealWallet` | Teal Wallet | サブスク＆買い物リスト | TealSub (1.7.0〜1.8.0 の Teal Sub。1.9.0 で名前を変更) |
| `TealTasks` | Teal Tasks | タスク管理＆カレンダー | (なし。1.7.0 で追加) |

## フォルダ構成

```
apps.json                 アプリの登録簿 (Teal Hub が読む)。ビルド時に dist にコピー
build.sh / build.bat      ビルド (どちらも同じ結果)
src/Common/               全アプリ共通コード (各 exe に一緒にコンパイルされる)
  Theme.cs                色 (テーマカラー #008080)・フォント (Meiryo UI)・DPI スケール S()・アイコン読み込み
  Controls.cs             FlatButton / HeaderBar / FooterBar / CardPanel / InputFrame / FlatSlider / Dialogs
  AppHost.cs              起動処理: 二重起動防止・旧データの引き継ぎ・前面表示・終了要求 (Teal Hub からの「終了」)
  AppInfo.cs              AppSpec (ID・表示名・役割・説明・旧 ID) とバージョン
  DataStore.cs            %APPDATA%\TealSuite\<AppId>\ への JSON / バイナリ保存 (一時ファイル経由で安全に上書き)、旧データの引き継ぎ
  WindowMemory.cs         ウィンドウ位置・大きさの記憶
  AboutDialog.cs          「このアプリについて」
  Native.cs               共通の Win32 API
  Startup.cs              スタートアップ (Run キー) の登録・状態・古い自動起動の置き換え
  Hotkey.cs               全体ショートカット: HotkeySpec (組み合わせの解析・決まり)・GlobalHotkey (RegisterHotKey)
  HotkeyBox.cs            キーの組み合わせを拾う入力欄 (Teal Hub の設定ページ)
  AppPrefs.cs             アプリごとの設定 (ショートカット・常駐)。TealHub\app-settings.json。Teal Hub が書き、各アプリは読むだけ
  ResidentContext.cs      画面を持つアプリの「常駐」(通知領域・×の動き・Hotkey / Reload イベント)
  HubLink.cs              アプリの「設定」から Teal Hub の設定ページを開く (TealHub.exe --settings <ID>)
  Shortcuts.cs            1.5.0 までの shortcuts.json (読むだけ。引き継ぎ用)
src/Apps/<AppId>/         アプリごとのソース。AppMeta.cs に名前 (AppSpec)・バージョン・Main
  refs.txt                (任意) 追加の参照 DLL。1 行 1 つ、# はコメント
assets/icons/             <AppId>.ico (16〜256px) と <AppId>.png。tools/make_icons.py で生成
docs/README.txt           利用者向けの説明 (dist にコピーされる。UTF-8 BOM 付き・CRLF)
tests/                    mono 上のスモークテストとスクリーンショット用スクリプト
tools/                    make_icons.py (アイコン生成・Pillow)、package_update.py (.tealupdate)、release_build.sh (リリース用一式)
.github/workflows/        release.yml (main への push でリリースを作る)
CHANGELOG.md              変更履歴 (リリースの説明文になる)
dist/TealSuite/           ビルド結果 (配布するのはこのフォルダ。git には入れない)
```

配布フォルダの形:

```
TealSuite/
  TealHub.exe  TealHub.ico  apps.json  README.txt
  apps/<AppId>/<AppId>.exe  <AppId>.ico
```

## 名前の決まり (変えるときはここだけ)

表示名は各アプリの `AppMeta.cs` にある `Program.Spec` (AppSpec) と `apps.json` の 2 か所だけに書きます。
画面側はすべて `AppInfo` から取るので、文字列を探して回る必要はありません。

| 使う場所 | 書き方 | 例 |
|---|---|---|
| ウィンドウタイトル | `AppInfo.WindowTitle` | `Teal Pass - パスワード管理` |
| サブ画面のタイトル | `AppInfo.TitleFor("設定")` | `Teal Clip - 設定` |
| ヘッダー | `HeaderBar.ForApp("補足")` / `header.SetDetail("補足")` | 太字で `Teal Pass`、下に `パスワード管理 ・ 補足` |
| フッター右・「このアプリについて」・通知領域 | `AppInfo.DisplayName` / `AppInfo.Role` (自動) | `Teal Pass  v1.3.0` |

`Name` と `Role` は apps.json の `name` / `role` と同じにします (テストで一致を確認)。

## apps.json (アプリの登録)

Teal Hub (TealHub.exe) は、**自分と同じフォルダの apps.json** を読んでカードを並べます
(環境変数 `TEALSUITE_MANIFEST` で別ファイルも指定可、テスト用)。F5 /「再読み込み」で読み直します。

```json
{
  "manifestVersion": 1,
  "suite": "TealSuite",
  "apps": [
    { "id": "TealMemo", "name": "Teal Memo", "role": "メモ", "exe": "apps/TealMemo/TealMemo.exe", "version": "1.3.0",
      "description": "思いついたことをすぐに書き留めるメモ帳。自動保存", "icon": "apps/TealMemo/TealMemo.ico",
      "legacyIds": ["Memo"] }
  ]
}
```

| 項目 | 必須 | 内容 |
|---|---|---|
| `id` | ○ | アプリ ID。`src/Apps/<id>`、exe 名、アイコン名、データフォルダ名 (`%APPDATA%\TealSuite\<id>`)、二重起動防止の名前に使う。英数字のみ。変えるときは下の「ID を変えるとき」に従う |
| `name` | ○ | 表示名 (`Teal ○○`) |
| `role` |  | 日本語の短い役割。カードで名前の横にテーマカラーで表示 |
| `exe` | ○ | apps.json からの相対パス (`/` 区切り) |
| `version` | ○ | `major.minor.patch`。AppMeta.cs の AssemblyVersion と一致させる (build.sh が確認) |
| `description` |  | カードの説明文 (1 行) |
| `icon` |  | .ico の相対パス。無ければ exe のアイコン |
| `self` |  | `true` なら Teal Hub 自身。一覧には出さない (スタートアップはヘッダーのスイッチで切り替え) |
| `legacyIds` |  | 1.0 の旧 ID (`ClipboardHistory` など)。古い自動起動 (旧 exe 名) を見つけて置き換えるのに使う。AppMeta.cs の `Spec.LegacyIds` と揃える |

実行中かどうかは、`exe` の実際のパスと一致するプロセスで判定します。
「終了」は名前付きイベント `Local\TealSuite.<id>.Exit` で終了をお願いし (各アプリは保存してから閉じる)、
数秒たっても残っていれば確認のうえ強制終了します。このため各アプリは必ず `AppHost.Run` / `AppHost.RunContext` から起動してください。

**特別な指示がない限り、新しいアプリはすべて apps.json に登録します。**

## スタートアップ (Windows の起動時に自動で起動)

処理はすべて `src/Common/Startup.cs` にあり、Teal Hub のカードのスイッチと Teal Clip・Teal Volume の設定画面はこれを呼ぶだけです。

- 登録先は `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。値の名前は `TealSuite.<id>`
  (`Startup.ValueName`)、値は `"<exe のフルパス>" --startup` (`Startup.CommandFor`)。管理者権限は不要
- 状態 (`Startup.Get`) は `Off` / `On` / `PathMismatch` (登録された exe が今の exe と違う。フォルダを移したとき) /
  `DisabledByUser` (Windows の設定やタスク マネージャーで無効化。`...\Explorer\StartupApproved\Run` の先頭バイトが奇数)
- `Startup.Enable` は値を書き、StartupApproved の無効化も消す。`Startup.TrySet` は失敗時にメッセージを返し error.log に記録
- Teal Hub は起動時・再読み込み・ウィンドウがアクティブになったとき (5 秒に 1 回まで) に読み直す
- `--startup` (または `/startup`) 付きで起動されると `AppHost.StartedAtLogin` が true になり、
  画面は最小化で起動、Teal Clip は初回のバルーンを出さない。二重起動のときは何も表示せずに終わる
- 古い自動起動 (`Startup.FindLegacy`): Run キーの `TealSuite.*` 以外の値と、スタートアップ フォルダーの
  .lnk/.url/.exe/.bat/.cmd のうち、実行先の exe 名が `legacyIds` の `<旧ID>.exe` のもの
  (.lnk は WScript.Shell で実行先を読む。読めなければファイル名で判定)。Teal Hub の上にお知らせを出し、
  **利用者がボタンを押して確認したときだけ** `Startup.ReplaceLegacy` で置き換える。
  Run の値は `%APPDATA%\TealSuite\TealHub\removed-startup\removed-startup.txt` に記録してから削除、
  ショートカットは同じフォルダへ移動 (削除しない)
- 全ユーザー共通のスタートアップ フォルダー・HKLM は扱わない (管理者権限が要るため)
- レジストリは `IRunKey` で抽象化。テストでは `MemoryRunKey`、画面確認では `FileRunKey` (環境変数 `TEALSUITE_RUNKEY_FILE`) を使う

## 新しいアプリを追加する手順

1. `src/Apps/Teal<名前>/` を作り、既存アプリ (例: TealVolume) の `AppMeta.cs` をコピーして
   `Id`・`Spec` (Name / Role / Description)・`AssemblyVersion("1.0.0.0")` を書き換える。新しいアプリは `LegacyIds` を空 (`new string[0]`) にする。
   - 普通のウィンドウ: `AppHost.Run(Spec, () => new MainForm())`
   - 常駐 (トレイ) アプリ: `AppHost.RunContext(Spec, () => new MyContext())` (TealClip 参照)
2. 画面は共通部品で組む: `Theme.ApplyForm(this)`、`Text = AppInfo.WindowTitle`、`HeaderBar.ForApp(...)`、
   `CardPanel` で区切り、`FooterBar` (左に状態、右に「名前 v1.0.0」)。色・フォントは `Theme` のものだけを使う
   (直接 `Color.FromArgb` を書かない。テーマを変えたときに揃わなくなるため)。
   寸法は 96dpi 基準の値を `Theme.S()` で拡大する (`WindowMemory.Attach` には S() を掛けない素の値を渡す)。
3. アイコン: `tools/make_icons.py` に白いグリフを描く関数を足して `GLYPHS` に登録し、
   `python3 tools/make_icons.py <AppId>` (背景のティールのグラデーションと角丸は共通)。
4. `apps.json` に 1 件追加 (`name` / `role` は Spec と同じに)。
5. `./build.sh <AppId>` (または `build.bat <AppId>`) でビルド → `dist/TealSuite/TealHub.exe` で確認。
6. 必要なら `tests/` にテストを足し、`docs/README.txt` に使い方を追記。

### ID を変えるとき (データの引き継ぎ)
データフォルダは ID で決まるため、ID を変えると以前のデータが見えなくなります。変えるときは
`Spec.LegacyIds` に旧 ID を足してください。`AppHost` が二重起動でないと確認したあと、画面を作る前に
`DataStore.MigrateLegacyData()` を呼び、次のように動きます。

- 新しいフォルダ `%APPDATA%\TealSuite\<新ID>` が空のときだけ、旧フォルダをまるごとコピー
  (1.0 からの移行は `%APPDATA%\AppSuite\<旧ID>`。一時フォルダ `<新ID>.migrating` にコピーしてから名前を変える)
- **旧フォルダは消さない・変更しない**。新しいフォルダに `migrated-from.txt` (引き継ぎ元と日時) を書く
- 新しいフォルダにデータがあれば何もしない (上書きしない)。失敗したら error.log に記録して空のまま起動
- Teal Memo の「以前のメモアプリ (%APPDATA%\MemoApp)」からの取り込みはこの後に動き、notes.json が無いときだけ実行される (二重に取り込まない)

#### 1.9.0 の Teal Sub → Teal Wallet (スイートの中での ID の変更)
`AppMeta.cs` の `LegacyIds = { "TealSub" }` と apps.json の `legacyIds` に旧 ID を書き、`RenameMigration.OldNames` に旧 ID → 旧い表示名を足す。

- データ: `DataStore.MigrateLegacyData()` は `%APPDATA%\TealSuite\<旧ID>` (スイートの中の旧 ID) も引き継ぎ元として探す。旧フォルダは残すので 1.8.0 に戻しても使える
- `RenameMigration` (src/Common) … `MigratePrefs` (app-settings.json の旧 ID の項目を付け替え。新しい項目があれば空の値だけ補う)、
  `MigrateStartup` (Run の `TealSuite.<旧ID>` を消して `TealSuite.<新ID>` で新しい exe を登録。無効にされていたら登録し直さない。新しい exe が無ければ何もしない)、
  `RunForSelf` (アプリ自身が起動時に呼ぶ)。どれも何度呼んでも同じ結果
- `HubMigration` (TealHub) … apps.json を読んだあと、`legacyIds` があるアプリについて上の 2 つを実行し、Teal Hub の上にお知らせを 1 回出す
- apps.json のマージ (`MergeAppsJson`) … 新しい項目の `legacyIds` にある ID の古い項目は、その場所で置き換える (2 つにならない)
- .tealupdate … 差分の `delete` に `apps/TealSub/*` が入る。TealUpdater はスイートのフォルダの exe で動いているもの (TealSub.exe も) を閉じてから入れ替える
- バックアップの復元 (`HubBackup.FolderMap` / `Restore` の renames。HubData.cs) … 1.8.0 のバックアップの `TealSub/` は `TealWallet/` に戻す (復元の前に今のデータを before-restore-*.zip に残すのは 1.8.0 と同じ)
- テストは tests/V190Tests.cs の RunRename / RunUpdate

DPAPI の追加エントロピー (`Vault.cs` の `"AppSuite.PasswordMaker.vault.v1"`、`History.cs` の `"AppSuite.ClipboardHistory.v1"`)
は 1.0 のデータを復号するために **名前を変えても変更しない** でください。

### C# の書き方の制約
Windows 標準の csc.exe (C# 5) でもビルドできるよう、build.sh は `-langversion:5` を付けています。
`?.`、`$"..."`、`nameof`、`=>` のメンバー、getter のみの自動プロパティ、`out var` は使えません。
ソースは UTF-8 で保存します (csc は `/codepage:65001`)。名前空間は `TealSuite.Common` / `TealSuite.Hub` / `TealSuite.Pass` / `TealSuite.Memo` / `TealSuite.Clip` / `TealSuite.Volume` / `TealSuite.Wallet` / `TealSuite.Tasks`。

## 色 (Theme.cs)

テーマカラーは **#008080 (ティール)**。白文字とのコントラスト比 4.8:1 (WCAG AA)。

| 名前 | 色 | 用途 |
|---|---|---|
| Accent / AccentHover / AccentPressed | #008080 / #006E6E / #005C5C | 主ボタン・スライダー・フォーカス枠・リンク・ヘッダー上端の帯 |
| AccentSoft | #E0F2F1 | 選択行・強調の背景 |
| Background / SurfaceAlt | #F3F7F7 / #ECF3F3 | ウィンドウ背景 / ホバー |
| Border / BorderStrong | #DAE4E4 / #BFCFCF | 枠線 |
| Text / SubText | #1A2626 / #5A6B6B | 本文 / 補足 |
| Success / Danger / Warning | #23863F / #D64045 / #D98A0B | 状態表示 (Success はティールと見分けられる緑) |

アイコンの背景は #26A69A → #006969 のグラデーション (tools/make_icons.py の `GRAD_TOP` / `GRAD_BOT`)。

## 常駐 (ResidentContext.cs・Teal Clip / Teal Volume の TrayContext.cs)

1.6.0 から、常駐するかどうかはアプリごとに Teal Hub の設定ページで選ぶ (`app-settings.json` の `resident`)。

- 初期設定: Teal Clip・Teal Wallet・Teal Tasks がオン (起動している間だけ記録・お知らせができるため。apps.json の `resident` と
  各アプリの `Program.ResidentByDefault`)。ほかはオフ。設定ページの説明文は apps.json の `residentNote`。Teal Hub は常に常駐
  (`HubShortcuts.ResidentDefault`)。1.5 でスタートアップに登録していたアプリは常駐オンとして引き継ぐ
- 常駐オン: ×と Esc は隠すだけ (`OnFormClosing` で `CloseReason.UserClosing` のときだけ `e.Cancel`)。最初の 1 回だけ
  バルーンで案内 (tray.json の `trayTipShown`)。`--startup` では画面を出さずに通知領域で待つ。終了は通知領域の「終了」
- 常駐オフ: ×で閉じると終了。`--startup` で起動された場合は何もせず終了 (Teal Clip・Teal Volume)
- スタートアップは常駐オンのときだけ選べる (Teal Hub の設定ページで、オフのときはスイッチが無効。
  前の版の登録が残っているときは「オフにする」だけできる)
- `AppHost.ReloadRequested` (Reload イベント): app-settings.json を読み直す。常駐をオフにしたとき画面が出ていなければ終了
- `AppHost.HotkeyRequested` (Hotkey イベント): Teal Hub がショートカットを受けたときの合図。
  Teal Clip は呼び出し画面、ほかは「後ろにある / 隠れている → 前面」「前面 → 隠す (オフなら最小化)」
- `AppHost.ExitRequested` (Teal Hub の「終了」) と `AppHost.ShowRequested` (2 回目の起動) は今までどおり
- Teal Volume: 表示されたら一覧を更新してタイマーを動かし、隠れている間は止める (CPU を使わない)

## 全体ショートカット (Hotkey.cs)

- 保存形式は `"Alt+V"` / `"Ctrl+Shift+V"` (`HotkeySpec.ToString` / `TryParse`。順番は Ctrl・Shift・Alt・Win)。
  画面表示は `Display` (`Alt + V`)。`"none"` は「なし」(正しい設定)
- 決まり (`HotkeySpec.Validate`。理由の文字列を返す、null なら可):
  - F キー以外は Ctrl・Alt・Win のどれかが必要 (文字だけ・Shift + 文字は不可。Alt + 文字は可)
  - Ctrl だけ + A/C/V/X/Y/Z/S、Alt だけ + F4/Tab/Space/Esc、Ctrl + Esc、Ctrl + Shift + Esc、
    Ctrl + Alt + Delete、Win + L は不可
  - `HotkeySpec.Caution` は不可ではないが注意を出すもの (Alt + 文字はほかのアプリのメニューより優先、F キー単独)
- 登録は `GlobalHotkey.Register` (失敗したら `GlobalHotkey.ConflictMessage`: ほかのアプリが使用中)
- mono (Windows 以外) では RegisterHotKey を呼ばず、`GlobalHotkey.FakeRegister` (テスト用。null なら常に成功) を使う

## ショートカットの受け持ち (AppPrefs.cs・TealHub/HubShortcuts.cs)

1.6.0 から、全アプリのショートカットを Teal Hub だけが登録する。

- 保存先: `%APPDATA%\TealSuite\TealHub\app-settings.json`
  `{"version":1,"apps":[{"id":"TealClip","hotkey":"Ctrl+Shift+V","resident":true}, ...]}`
  - `hotkey` がない = 初期設定 (Teal Hub Alt+H・Teal Clip Ctrl+Shift+V・Teal Volume Alt+V・ほかはなし。`HubShortcuts.BuiltIn`)
- 押されたとき (`HubShortcuts.TriggerApp`): アプリが起動していなければ `<アプリ>.exe --show` で起動
  (`AppHost.ShowByHotkey`)、起動中なら Hotkey イベントを鳴らす。起動直後の連打は待つ (二重起動しない)
- `HubShortcuts.Apply`: 決まりの確認 → ほかのアプリとの重複確認 (`DuplicateOf`) → 登録 → 失敗したら元のキーに戻して理由を返す。
  成功したら保存してそのアプリの Reload イベントを鳴らす
- 設定ページでキーを拾っている間は `SuspendAll` / `ResumeAll` で全部外す
- 引き継ぎ: app-settings.json がないときだけ、1.5 の shortcuts.json (Teal Hub・Teal Clip・Teal Volume) と
  1.3 の Teal Volume settings.json の hotkey を読む (`MigratedHotkey`)。何か引き継いだら初回だけお知らせ (`Migrated`)
- 1.5 の shortcuts.json には書き込まない (1.5 に戻したときはそのまま使われる)

## Teal Hub の設定ページ (TealHub/AppSettingsPage.cs・HubLink.cs)

- 各カードと Teal Hub 自身の行の「⚙ 設定」で、画面がそのアプリの設定ページに切り替わる (「← 戻る」/ Esc で一覧へ)
- 中身: ショートカット (欄をクリック → キーを押す →「登録」/「解除」/「初期設定に戻す」)、常駐、スタートアップ
- アプリ側の「設定」は `HubLink.OpenSettings` → `TealHub.exe --settings <ID>`。Teal Hub が起動中なら
  `TealHub\open-settings.txt` に ID を書いて Show イベントを鳴らし、起動中の Teal Hub が `TakeRequest` で読む (30 秒で無効)

## アップデート ファイル (.tealupdate)

1.4.0 から、フォルダの入れ替えではなくアップデート ファイルで更新する。

```
./build.sh                                              # 新しい版をビルド (dist/TealSuite)
./build.sh package-update                               # 全ファイル入り (full)
./build.sh package-update --from 前の版の TealSuite.zip --notes "変更点"   # 変わったファイルだけ (diff)
```

- 中身は zip: `update.json` (マニフェスト) + `files/<スイート内の相対パス>`
- `update.json`: `format` (1)・`suite` ("TealSuite")・`version`・`minFromVersion`・`created`・`kind` (full / diff)・`notes`・
  `files` (`path`・`sha256`・`size`・`app`)・`delete` (消すファイル)・`apps` (apps.json に入れるアプリの項目)
- パスは `/` 区切りの相対パスのみ (`..`・絶対パス・`backup/`・`.update-staging/` は拒否。`UpdatePackage.CheckPath`)
- TealUpdater.exe (スイートの直下) の流れ:
  1. 読み込み・確認 (suite が一致、今のバージョン ≧ minFromVersion、今より新しい) → 確認画面 (古い → 新しい)
  2. `.update-staging` に取り出して sha256 をすべて確認 (ここまではスイートに何もしない)
  3. スイート内の exe を終了 (`SuiteProcesses`: 終了のお願い → 待つ → 強制終了)
  4. 置き換えるファイル・消すファイル・apps.json を `backup/<元のバージョン>/` にコピー (`backup.json` に記録)
  5. 置き換え・削除 → apps.json をマージ。途中で失敗したら backup から元に戻す
  6. 終了させたアプリと Teal Hub を起動し直す
- TealUpdater.exe 自身も置き換えられるよう、TealUpdater は起動するとまず自分を一時フォルダ (`%TEMP%\TealSuiteUpdater-*`) にコピーしてそちらで動く (古いコピーは次回に掃除)
- `%APPDATA%` には一切触れない (データの形式を変えるときは、今までどおり各アプリが古い形式を読めるようにする)
- 元に戻す: `TealUpdater.exe --rollback` (Hub の「前のバージョンに戻す…」)。まだ戻していない一番新しい backup を復元する
- apps.json のマージ (`UpdateEngine.MergeAppsJson`): 利用者の apps.json を元に `suiteVersion` を更新。
  パッケージの各アプリは id か legacyIds が一致する項目に、パッケージにあるキーだけ上書き
  (利用者が足したキー・アプリはそのまま)。一致しなければ末尾に追加 → Hub に新しいアプリとして出る
- 関連付け: Teal Hub が起動時に `HKCU\Software\Classes\.tealupdate` を登録 (`FileAssoc.Ensure`。場所が変わったら更新)
- 開発用の見本: `tests/make_sample_update.sh` → `/tmp/tealsuite-next/TealSuite-1.5.0.tealupdate`
  (Hub・Pass・Clip を 1.5.0 にし、新しいアプリ Teal Note を足した diff)。**配布物には入れない**
- `--expect-version X.Y.Z`: Teal Hub がダウンロードしたファイルを渡すときに付ける。中身の version が X.Y.Z でなければ断る
  (公開されている版と違うファイルにすり替わっていないかの確認)
- `package_update.py --from 前の zip --all`: 全ファイル入り (kind=full) + 前の版で消えたファイルを delete。
  リリースではこれを使うので、1.4.0 以降ならどの版からでも 1 回で最新にできる (minFrom は 1.4.0)

## オンラインのアップデート確認 (TealHub/OnlineUpdate.cs・HubUpdates.cs)

- 公開場所は `UpdateSource` (OnlineUpdate.cs) の `Owner` / `Repo` の 1 か所だけ (今は `RrioK2065/TealSuite`)
- 確認: `GET https://api.github.com/repos/<Owner>/<Repo>/releases/latest` (認証なし、User-Agent `TealSuite-Hub/<ver>`、TLS 1.2)。
  タグ `v1.5.0` → `1.5.0` を、apps.json の `suiteVersion` と比べる。draft / prerelease は無視
- いつ: Hub の起動から 5 秒後 (通知領域で起動したときは 60 秒後)、その後 1 時間ごとに「前回の確認から 24 時間たったか」を見る。
  設定 (`%APPDATA%\TealSuite\TealHub\settings.json` の `updateCheckOff` / `lastUpdateCheck` / `notifiedVersion`) は
  「アップデート ▾」→「自動で更新を確認する (1 日 1 回)」で切り替え。手動の「更新を確認」はいつでも使える
- 結果: 新しい版があれば画面上のお知らせ (今すぐアップデート・内容を見る・あとで) とボタンの印「● x.y.z ▾」、
  通知領域のお知らせ (版ごとに 1 回)。自動の確認では、オフライン・404 (リリースなし)・403/429 (回数制限) などは何も出さずログだけ
- アセットの選び方: `TealSuite-update-<ver>.tealupdate` → 名前にバージョンを含む .tealupdate → ほかの .tealupdate
- ダウンロード先: `%TEMP%\TealSuite-download\` (古い .tealupdate は消す)。そのあと `TealUpdater.exe "<file>" --expect-version <ver>`
  → いつもの確認画面。`TealSuiteUpdater-*` は TealUpdater 自身のコピー用なので使わない
- 開発用の差し替え: `TEALSUITE_UPDATE_API` (URL か、リリース JSON のファイルのパス)、`TEALSUITE_DOWNLOAD_DIR`、
  `TEALSUITE_NO_UPDATE_CHECK=1` (自動の確認を止める。テストで使用)

### 改ざん対策 (いまの方式)

1. **HTTPS のみ・接続先を限定**: api.github.com / github.com / *.githubusercontent.com 以外 (リダイレクトの最終的な行き先も含む) は断る
2. **sha256**: リリースの `TealSuite-update-<ver>.tealupdate.sha256` (なければリリースの説明文の `<hex>  <ファイル名>` の行) と
   ダウンロードしたファイルを照らし合わせる。値が見つからなければダウンロードしない
3. **大きさ・形式**: アセットの size と一致・zip であること (先頭が `PK`)・200MB まで
4. **TealUpdater の確認**: suite が TealSuite、今より新しい版、minFrom、`--expect-version` の一致、ファイルごとの sha256 (update.json)

署名 (公開鍵を exe に埋め込み、マニフェストを秘密鍵で署名) は入れていない。リリースは GitHub Actions が作るので、
秘密鍵は GitHub の Secrets に置くしかなく、GitHub アカウント (リポジトリへの書き込み権限) を乗っ取られたら鍵も使われてしまい、
いまの方式 (GitHub の HTTPS + sha256) と守れる範囲がほとんど変わらないため。
手元のPCで署名してからアップロードする運用にするなら、そのとき入れる (TealUpdater の `UpdatePackage` に署名の確認を足し、
公開鍵を TealUpdater/TealHub に埋め込む。秘密鍵はリポジトリに入れない。`.gitignore` に `keys/` 済み)。

## リリース (GitHub Actions)

手順:

1. `src/Apps/*/AppMeta.cs` と `apps.json` のバージョンを上げる (下の「バージョンを上げる」。スイートの版 = `suiteVersion`)
2. `CHANGELOG.md` の先頭に `## x.y.z (日付)` の節を書く (これがリリースの説明文とアップデートの確認画面の説明になる)
3. `./build.sh && tests/run_tests.sh` で確認 → main にマージ (push)
4. Actions の **Release** (`.github/workflows/release.yml`) が `v<suiteVersion>` のタグがまだなければ:
   ビルド → テスト (xvfb) → 前のリリースの `TealSuite-<前>.zip` を取得 → `tools/release_build.sh --prev …` →
   `gh release create v<ver>` (zip・.tealupdate・.sha256)
5. 利用者の Teal Hub が 1 日以内に見つけてお知らせする

- タグがもうあるときは何もしない (バージョンを上げ忘れた push で壊れない)。作り直すときはリリースとタグを消してから Actions を再実行
- 手元で同じものを作る: `tools/release_build.sh [--prev 前の TealSuite-x.y.z.zip]` → `release/`
- リリースは `GITHUB_TOKEN` (permissions: contents: write) で作る。GITHUB_TOKEN で作ったリリースは別のワークフローを起動しない

## バージョンを上げる

1. `src/Apps/<AppId>/AppMeta.cs` の `AssemblyVersion` と `AssemblyFileVersion`
2. `apps.json` の `version`

の 2 か所を同じ値に。ずれていると build.sh がエラーにします。
バージョンはフッターと「このアプリについて」、Teal Hub のカードに表示されます。
`TealUpdater/AppMeta.cs` は apps.json の `suiteVersion` と同じにします (build.sh が確認)。
配布は GitHub Actions が作る (上の「リリース」)。手動なら `./build.sh package-update` で作るアップデート ファイル。
保存形式を変えるときは各ファイルの `version` を上げ、古い形式を読めるようにしておくこと。

## Teal Wallet (1.7.0〜1.8.0 は Teal Sub) と Teal Tasks (1.7.0)

どちらも **AI・ネットワークを使わない** (HttpClient / WebRequest を参照しない)。データは `DataStore` の JSON だけ。
画面以外のロジックは静的クラスにまとめ、`DateTime today/now` を引数で受け取る (テストで日付を固定できる)。

- Teal Wallet (`src/Apps/TealWallet/`。名前空間 `TealSuite.Wallet`)
  - `SubModel.cs` … `Subscription` (金額は円の整数 `long`、周期 Monthly / Yearly / Weekly / EveryNMonths、無料期間、最後に使った日、使う頻度、状態)・`SubData` (subs.json)
  - `SubLogic.cs` … 次の更新日 (`NextRenewal`。開始日から n 回目を `AddMonths` で求めるので 1/31 → 2/28 → 3/31 とずれない)、
    月・年あたりの金額 (週払いは ×52÷12)、カテゴリ別、見直しの点数 (`Review`。最大 100、30 点以上が候補、60 点以上が「おすすめ」)、
    お知らせ (`DueNotices`。`id|renew|日付` の印で 1 回だけ)、CSV、解約ページの URL は http / https だけ (`IsOpenableUrl`)
  - `SubForms.cs` … 追加・編集、解約ガイド、設定。`MainForm.cs` … ダッシュボード / 一覧 / 見直し候補 / 解約済み
  - 引数: `--tab=dashboard|list|review|cancelled|shop|wants` (1〜6 でも可。`subs` は list と同じ)
  - 買い物リスト (1.9.0): `ShopModel.cs` … `ShopItem` / `ShopList` / `Purchase` (履歴) / `WantItem` / `ShopData` (shopping.json)。
    `ShopLogic.cs` … 1 行の読み取り (`ParseOne` / `Parse`。「牛乳 2 198円」。数字だけなら 100 未満は数、100 以上は値段)、売り場の推測、
    同じ名前はまとめる (`Add`)、並べ方 (売り場の順 → 足した順、カゴの中は下)、合計 (`Totals`)、買い物を終える (`Finish`)、
    よく買うもの (`FrequentItems`。欲しいものの記録は除く)、月の支出・予算 (`MonthSpend` / `BudgetLeft`)、テキスト (`ToText`)、欲しいもの (`OrderedWants` / `BuyWant`)、見本データ (`Sample`)。
    `ShopForms.cs` … 品の編集・欲しいもの・履歴から追加。`MainForm.Shop.cs` … 買い物リスト / 欲しいもののタブとショートカット。
    バックアップ (JSON) は `SubData.Shopping` に入れる (1.8.0 の subs.json は Shopping なしで読める)
- Teal Tasks (`src/Apps/TealTasks/`)
  - `QuickParser.cs` … 「明日15時 歯医者 #病院」などの日本語の 1 行を `ParseResult` に。全角は先に半角へ (`Normalize`)。
    時刻だけで過ぎていれば明日、「1〜6 時」は午後、1 週間以上前の M/D は来年
  - `TaskLogic.cs` … おすすめ順の点数 (`Score`)、今日のリスト、くり返しの次の回 (`NextDue`。たまった分は飛ばす)、
    完了 / 取り消し (作った次の回を消す)、お知らせ (`DueNotices`。期限を変えたら印が変わるのでもう一度出る)、統計、CSV、見本データ
  - `CalendarView.cs` … 月・週・日の自前描画 (ドラッグで移動、週・日は 15 分きざみ)。`Control` のメンバーと名前がぶつからないよう `Day` / `MoveDay`
  - `TaskForms.cs` … 詳しい追加・編集、設定。`MainForm.cs` … 入力欄・今日 / カレンダー / 一覧 / 統計・元に戻す (1 段)
  - 引数: `--tab=today|calendar|list|stats` (1〜4 でも可)、`--cal=month|week|day`
- お知らせは起動している間だけ (NotifyIcon のバルーン)。最初は 5 秒後、その後 30 秒ごとに確認する
- ショートカットの初期設定はなし (apps.json の `shortcuts` の `default` が空)

## 1.8.0 の追加機能 (全アプリ)

どれも **ネットワークを使わない** (Teal Hub の更新の確認だけは今までどおり)。新しい保存項目はすべて `EmitDefaultValue = false` で、
1.7.0 のファイルはそのまま読める (なければ空・0・null 扱い)。DPAPI のエントロピーと保存形式の印 (Teal Pass の `PMV1` など) は変えていない。
画面以外のロジックは静的クラスにまとめ、日時は引数で受け取る (tests/V180Tests.cs で日付を固定して確認)。

- Teal Hub
  - `HubData.cs` … `HubBackup` (`%APPDATA%\TealSuite` 全体を zip に。`Create` / `Inspect` / `Restore`。復元の前に
    `TealHub\backups\before-restore-….zip` を作る。zip の中の名前は `SafeName` で確かめ、`..`・絶対パスは拒否。
    データ フォルダーの中への保存は不可)、`ChangeLog` (同梱の CHANGELOG.md を `## x.y.z` ごとに読む)、`WhatsNewForm`
  - `HubSettings.LastSeenVersion` … 違えばお知らせ バーに「更新内容を見る」(新しい版のお知らせがあるときはそちらを優先)
  - 参照に `System.IO.Compression.dll` を追加 (`src/Apps/TealHub/refs.txt`。`ZipArchive` だけを使い、`ZipFile` は使わない)
  - 「その他 ▾」メニュー、通知領域の「アプリを開く ▸」
- Teal Pass (`PassTools.cs`・`PassDialogs.cs`)
  - `VaultEntry` に `Favorite`・`History` (以前のパスワード 5 件まで)・`PasswordDate`。`ApplyEdit` で履歴を積む
  - `Check` … 使い回し・弱い (`StrengthMeter` のレベル 1 以下)・365 日以上・空。`ImportCsv` (Chrome / Edge / Firefox の列名)・`ToCsv`
- Teal Memo (`NoteStore.cs`)
  - `Note` に `Pinned`・`InTrash`・`DeletedUtc`。ゴミ箱は 30 日で自動削除 (`PurgeTrash`)。`#タグ` は本文から読む (保存しない)
  - `MemoSettings` (settings.json) に文字の大きさ。`FromText` / `ToText` で .txt の読み書き
- Teal Clip (`History.cs`・`ClipDialogs.cs`)
  - 件数 10 / 20 / 30 / 50 (`Capacity`、ピンは件数 − 1 まで)、`PurgeOlderThan` (自動削除。ピンは消さない)、`AddSnippet` (定型文 = ピン)、`UpdateText`、`Search`
  - `ClipFilter` … 記録しない言葉・パスワードらしい文字 (空白なし 8〜64 文字で、英大小・数字・記号のうち 3 種以上 など)。一時停止は保存しない
- Teal Volume (`VolumeLogic.cs`)
  - `VolumePreset` (アプリのキーごとの音量・ミュート。最大 12。`pid:` のキーは保存しない)、`ToggleMuteAll`、`Arrange` (固定を上・非表示を除く)
  - `VmSettings` に `Presets`・`Pinned`・`Hidden`
- Teal Sub (`SubTools.cs`)
  - `SubPlan.Forecast` (12 か月の支払い予定。解約予定は最後の 1 回まで、無料期間中は出さない)・`ByPayment`・`BudgetText`
  - `Subscription.PriceHistory` (`RecordPrice`)、`SubSettings.MonthlyBudget`、`ImportCsv` (書き出した CSV と同じ列。同じ名前はとばす)
  - `ForecastForm` (Ctrl+P・ダッシュボードの「支払い予定」)
- Teal Tasks (`TaskTools.cs`)
  - くり返しに `Weekdays` (土日を飛ばす)・`Yearly` (2/29 は平年 2/28)。QuickParser は「平日」「毎平日」「毎年」を読む (「毎日」より先に判定)
  - `TaskData.Templates` (最大 30。`dueDays` は今日から何日後か、-1 は期限なし)、`ParseBulk` (行頭の記号・番号・`[ ]` を落として 1 行 1 件、最大 200)、
    `Duplicate`、`CleanupDone` (30 / 90 / 365 日。Ctrl+Z で戻せる。テンプレートは元に戻すの対象外)
  - 「その他 ▾」は開くたびに作り直す (`BuildMoreMenu`)

## ビルド

- Linux: `sudo apt install mono-devel` → `./build.sh` (特定のアプリだけ: `./build.sh TealMemo TealVolume`)
- Windows: `build.bat` (追加のインストールは不要。`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` を使用)
- 共通の参照 DLL は build.sh の `COMMON_REFS` と build.bat の `REFS` の両方に書く。アプリ固有のものは refs.txt。

## テスト (Linux / mono)

```
./build.sh && tests/run_tests.sh                 # ロジックのスモークテスト (旧データの引き継ぎも含む)
Xvfb :99 -screen 0 1400x900x24 &
TEALSUITE_DEMO=1 tests/shot.sh dist/TealSuite/apps/TealVolume/TealVolume.exe out.png
tests/run_tests.sh seed /tmp/seed && TEALSUITE_DATA_DIR=/tmp/seed tests/shot.sh dist/TealSuite/apps/TealPass/TealPass.exe out.png
```

- `TEALSUITE_DATA_DIR` … データの保存先を差し替える (本物のデータを触らない)
- `TEALSUITE_LEGACY_DATA_DIR` … 引き継ぎ元 (1.0 の AppSuite フォルダ) を差し替える。未指定なら TEALSUITE_DATA_DIR と同じ場所
- `TEALSUITE_DEMO=1` … Teal Clip・Teal Volume を見本データで表示
- `TEALSUITE_RUNKEY_FILE` … Run キーの代わりにこのテキストファイル (1 行 1 件 `名前<TAB>コマンド`、先頭 `!` は無効化) を使う
- `TEALSUITE_STARTUP_DIR` … スタートアップ フォルダーを差し替える
- テストは tests/SmokeTests.cs (全体)、CHTests.cs (Teal Clip)、VMTests.cs (Teal Volume)、StartupTests.cs (スタートアップのロジックと Teal Hub のカード)、
  HotkeyTests.cs (ショートカットの決まり・登録の失敗、Teal Volume の常駐オン・オフ・Hotkey イベント・設定)、
  ShortcutTests.cs (受け持ち・重複・解除・Hub の画面)、UpdaterTests.cs (マニフェスト・ハッシュ・適用と元に戻す・apps.json のマージ・確認画面)、
  OnlineUpdateTests.cs (バージョン比較・リリース JSON・アセット選び・sha256・接続先の制限・1 日 1 回と無効の設定・お知らせとボタン・ダウンロード → TealUpdater)、
  SubTasksTests.cs (Teal Sub の更新日・合計・見直しの点数・お知らせ、Teal Tasks の入力の読み取り・おすすめ順・くり返し・お知らせ・統計・画面)、
  V180Tests.cs (1.8.0 の追加機能と、1.7.0 のデータをそのまま読めること)、
  V190Tests.cs (買い物リストの読み取り・ロジック・画面、Teal Sub → Teal Wallet の引き継ぎ (データ・設定・Run・バックアップ)、apps.json のマージと .tealupdate)
- Teal Wallet / Teal Tasks の画面確認: `tests/run_tests.sh seed /tmp/seed` で見本データを入れ、
  `SHOT_ARGS="--tab=calendar --cal=week" TEALSUITE_DATA_DIR=/tmp/seed tests/shot.sh dist/TealSuite/apps/TealTasks/TealTasks.exe out.png`。
  時刻で見た目が変わるので、README の画像は `faketime -f "@2026-10-27 09:20:00"` を前に付けて撮った (seed も同じ時刻で)。
  お知らせのバルーンは起動 5 秒後に出るので、待ち時間は 4 秒にする
- Hub の画面確認 (一覧・設定画面): `mcs` で tests/hub_shots.cs をビルドし `SHOT_ARGS=settings:TealClip tests/shot.sh build/HubShots.exe out.png` (main / main:tall (全アプリが入る高さ) / settings:<AppId>) (ビルド方法はファイルの先頭)
- 1.8.0 で増えた画面 (メニュー・支払い予定・まとめて追加) の確認: tests/v180_shots.cs をビルドし (方法はファイルの先頭)、
  `SHOT_ARGS="hub その他" tests/shot.sh build/V180Shots.exe out.png` (hub / volume / sub / tasks に、押すボタンの文字の先頭を付ける。tasks:bulk は「まとめて追加」)
- 設定画面だけの画面確認: `SHOT_ARGS="--startup /settings" tests/shot.sh dist/TealSuite/apps/TealVolume/TealVolume.exe out.png`
- `MEMOAPP_DATA_DIR` … 以前のメモアプリのデータの場所を差し替える
- ホットキー・クリップボード監視・Core Audio・DPAPI・SmartScreen・本物のレジストリ / .lnk の読み取り・ログイン時の起動などは mono では確認できないので、実機の Windows で確認する。

## 配布

GitHub の Releases (https://github.com/RrioK2065/TealSuite/releases) に Actions が置く。
`dist/TealSuite` フォルダを zip にしたものが TealSuite-x.y.z.zip (新しく入れる人向け)。すでに使っている人にはアップデート ファイルを配る。backup・.update-staging・*.tealupdate を zip に入れないこと。利用者向けの注意 (ブロックの解除、SmartScreen、1.0 からの移行) は README.txt に書いてあります。
