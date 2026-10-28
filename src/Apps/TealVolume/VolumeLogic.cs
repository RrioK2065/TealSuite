// Teal Volume (音量操作) - 1.8.0: プリセット・まとめてミュート・固定 / 非表示 (画面なし。テストできる)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace TealSuite.Volume
{
    [DataContract]
    public class PresetApp
    {
        /// <summary>アプリの exe のパス (システム音は "#system")</summary>
        [DataMember(Name = "key", Order = 1)] public string Key = "";
        [DataMember(Name = "name", Order = 2)] public string Name = "";
        [DataMember(Name = "volume", Order = 3)] public int Volume;
        [DataMember(Name = "muted", Order = 4, EmitDefaultValue = false)] public bool Muted;
    }

    /// <summary>名前つきの音量の組み合わせ (例: 「会議」「ゲーム」「夜」)</summary>
    [DataContract]
    public class VolumePreset
    {
        [DataMember(Name = "name", Order = 1)] public string Name = "";
        /// <summary>全体の音量 (%)。-1 なら変えない</summary>
        [DataMember(Name = "master", Order = 2)] public int Master = -1;
        [DataMember(Name = "masterMuted", Order = 3, EmitDefaultValue = false)] public bool MasterMuted;
        [DataMember(Name = "apps", Order = 4)] public List<PresetApp> Apps = new List<PresetApp>();
        [OnDeserializing] void Defaults(StreamingContext c) { Master = -1; Apps = new List<PresetApp>(); }

        public string Summary
        {
            get
            {
                var parts = new List<string>();
                if (Master >= 0) parts.Add("全体 " + (MasterMuted ? "ミュート" : Master + "%"));
                foreach (var a in (Apps ?? new List<PresetApp>()).Take(3)) parts.Add(a.Name + " " + (a.Muted ? "ミュート" : a.Volume + "%"));
                if (Apps != null && Apps.Count > 3) parts.Add("ほか " + (Apps.Count - 3) + " 個");
                return string.Join("・", parts);
            }
        }
    }

    public static class VolumeLogic
    {
        public const int MaxPresets = 12;

        /// <summary>保存してよいキー (pid だけのものは次に起動したら変わるので保存しない)</summary>
        public static bool IsStable(string key) { return !string.IsNullOrEmpty(key) && !key.StartsWith("pid:", StringComparison.Ordinal); }

        static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        /// <summary>今の音量をプリセットにする</summary>
        public static VolumePreset Capture(string name, IAudioBackend audio, IList<AudioApp> apps, bool includeMaster)
        {
            var p = new VolumePreset { Name = (name ?? "").Trim() };
            if (includeMaster && audio != null && audio.HasMaster) { p.Master = AudioNames.ToPercent(audio.MasterVolume); p.MasterMuted = audio.MasterMuted; }
            foreach (var a in apps ?? new List<AudioApp>())
                if (IsStable(a.Key) && !p.Apps.Any(x => Same(x.Key, a.Key)))
                    p.Apps.Add(new PresetApp { Key = a.Key, Name = a.Name, Volume = AudioNames.ToPercent(a.Volume), Muted = a.Muted });
            return p;
        }

        /// <summary>プリセットを当てる (今動いていないアプリはとばす)。変えたアプリの数</summary>
        public static int Apply(VolumePreset p, IAudioBackend audio, IList<AudioApp> apps)
        {
            if (p == null || audio == null) return 0;
            if (p.Master >= 0 && audio.HasMaster)
            {
                audio.SetMasterVolume(AudioNames.FromPercent(p.Master));
                if (audio.MasterMuted != p.MasterMuted) audio.SetMasterMute(p.MasterMuted);
            }
            int n = 0;
            foreach (var pa in p.Apps ?? new List<PresetApp>())
                foreach (var a in (apps ?? new List<AudioApp>()).Where(x => Same(x.Key, pa.Key)))
                {
                    audio.SetVolume(a, AudioNames.FromPercent(pa.Volume));
                    if (a.Muted != pa.Muted) audio.SetMute(a, pa.Muted);
                    n++;
                }
            return n;
        }

        /// <summary>同じ名前があれば置き換え、なければ追加 (最大 MaxPresets)。追加・置き換えできたら true</summary>
        public static bool Save(List<VolumePreset> list, VolumePreset p)
        {
            if (list == null || p == null || p.Name.Length == 0) return false;
            int i = list.FindIndex(x => string.Equals(x.Name, p.Name, StringComparison.CurrentCultureIgnoreCase));
            if (i >= 0) { list[i] = p; return true; }
            if (list.Count >= MaxPresets) return false;
            list.Add(p);
            return true;
        }

        /// <summary>まだミュートしていないアプリが 1 つでもあれば全部ミュート、全部ミュートなら全部解除。ミュートしたら true</summary>
        public static bool ToggleMuteAll(IAudioBackend audio, IList<AudioApp> apps)
        {
            var list = (apps ?? new List<AudioApp>()).ToList();
            bool mute = list.Any(a => !a.Muted);
            foreach (var a in list) if (a.Muted != mute) audio.SetMute(a, mute);
            return mute;
        }

        public static bool AllMuted(IList<AudioApp> apps) { return apps != null && apps.Count > 0 && apps.All(a => a.Muted); }

        /// <summary>表示順: 固定したもの (固定した順) → ほか (元の順)。隠したものは showHidden でなければ出さない</summary>
        public static List<AudioApp> Arrange(IList<AudioApp> apps, IList<string> pinned, IList<string> hidden, bool showHidden)
        {
            pinned = pinned ?? new List<string>(); hidden = hidden ?? new List<string>();
            var src = (apps ?? new List<AudioApp>()).Where(a => showHidden || !hidden.Any(h => Same(h, a.Key))).ToList();
            var top = pinned.SelectMany(k => src.Where(a => Same(a.Key, k))).Distinct().ToList();
            return top.Concat(src.Where(a => !top.Contains(a))).ToList();
        }

        public static bool Contains(IList<string> keys, string key) { return keys != null && keys.Any(k => Same(k, key)); }

        /// <summary>キーの付け外し (戻り値: 付けたら true)</summary>
        public static bool Toggle(List<string> keys, string key)
        {
            if (keys.RemoveAll(k => Same(k, key)) > 0) return false;
            keys.Add(key);
            return true;
        }
    }
}

namespace TealSuite.Volume
{
    using System.Windows.Forms;
    using TealSuite.Common;

    /// <summary>プリセットの名前を入力する</summary>
    public class PresetNameForm : Form
    {
        public readonly TextBox NameBox = new TextBox();
        public readonly ToggleSwitch MasterSwitch = new ToggleSwitch("全体の音量もいっしょに保存する");
        public string PresetName { get { return NameBox.Text.Trim(); } }

        public PresetNameForm(string initial)
        {
            DialogKit.Setup(this, "今の音量をプリセットに保存");
            AutoSize = false;
            ClientSize = Theme.S(420, 250);
            Padding = Theme.P(16, 12, 16, 12);
            NameBox.Text = initial ?? ""; NameBox.Font = Theme.Body; NameBox.MaxLength = 30; NameBox.BorderStyle = BorderStyle.None;
            Ui.SetCue(NameBox, "例: 会議、ゲーム、夜");
            MasterSwitch.Checked = true;
            var ok = new FlatButton("保存", ButtonKind.Primary).FitWidth(88);
            var cancel = new FlatButton("キャンセル", ButtonKind.Secondary).FitWidth(88);
            ok.Click += (s, e) => { if (PresetName.Length == 0) { NameBox.Focus(); return; } DialogResult = DialogResult.OK; };
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = ok; CancelButton = cancel;
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(Theme.ButtonHeight + 12), Padding = Theme.P(0, 10, 0, 0) };
            bar.Controls.Add(DialogKit.Buttons(ok, cancel));
            var frame = new InputFrame(NameBox);
            Lay.Stack(this,
                Lay.SectionLabel("プリセットの名前"),
                frame,
                Ui.Spacer(8),
                Lay.Note("今のアプリごとの音量とミュートを覚えます。同じ名前なら上書きします。"),
                Ui.Spacer(8),
                MasterSwitch);
            Controls.Add(bar);
            Shown += (s, e) => { NameBox.Focus(); NameBox.SelectAll(); };
        }
    }
}
