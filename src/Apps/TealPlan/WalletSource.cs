// Teal Plan (1.10.0): Teal Wallet のサブスクの支払い日と無料期間の終わりをカレンダーに出す (読むだけ)
//  - subs.json を読むだけ。書き換えない (支払い日の計算は Teal Wallet と同じ共通の Renewal)
//  - Teal Wallet が入っていない・ファイルがない・壊れているときは何も出さない (エラーも出さない)
//  - クリックすると Teal Wallet でそのサブスクを開く (MainForm.OpenItem)
using System;
using System.Collections.Generic;
using System.Drawing;
using TealSuite.Common;

namespace TealSuite.Tasks
{
    public class WalletPlanSource : IPlanSource
    {
        public const string SourceId = "wallet";
        public static readonly Color PayColor = Color.FromArgb(0x2F, 0x6F, 0xC8);
        readonly Func<DateTime> today;

        public WalletPlanSource(Func<DateTime> today) { this.today = today ?? (() => DateTime.Today); }

        public string Name { get { return "Teal Wallet"; } }

        public IEnumerable<CalItem> Items(DateTime from, DateTime to)
        {
            var list = new List<CalItem>();
            WalletView w;
            try { w = SuiteRead.Wallet(); } catch { return list; }
            if (w == null || w.Subs.Count == 0) return list;
            foreach (var d in WalletCalc.Dues(w.Subs, today().Date, from.Date, to))
            {
                list.Add(new CalItem
                {
                    Key = SourceId + ":" + d.Sub.Id + ":" + Fmt.Iso(d.Day),
                    Title = d.Title,
                    Badge = d.Badge,
                    Start = d.Day,
                    End = d.Day.AddDays(1),
                    AllDay = true,
                    Color = d.TrialEnd ? Theme.Warning : PayColor,
                    SourceName = Name,
                    Tag = d,
                });
            }
            return list;
        }

        /// <summary>カレンダーの項目から Teal Wallet のサブスクの id (Teal Wallet の項目でなければ null)</summary>
        public static string SubIdOf(CalItem x)
        {
            var d = x == null ? null : x.Tag as PayDue;
            return d != null && d.Sub != null ? d.Sub.Id : null;
        }
    }
}
