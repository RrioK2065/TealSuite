// Teal Pass (パスワード管理) - パスワード生成と強度判定
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace TealSuite.Pass
{
    [DataContract]
    public class PasswordOptions
    {
        [DataMember(Name = "length", Order = 1)] public int Length = 16;
        [DataMember(Name = "upper", Order = 2)] public bool Upper = true;
        [DataMember(Name = "lower", Order = 3)] public bool Lower = true;
        [DataMember(Name = "digits", Order = 4)] public bool Digits = true;
        [DataMember(Name = "symbols", Order = 5)] public bool Symbols = true;
        [DataMember(Name = "excludeAmbiguous", Order = 6)] public bool ExcludeAmbiguous = true;

        public const int MinLength = 4, MaxLength = 64;
        public PasswordOptions Normalize()
        {
            Length = Math.Max(MinLength, Math.Min(MaxLength, Length));
            if (!Upper && !Lower && !Digits && !Symbols) Lower = true;
            return this;
        }
    }

    public static class PasswordGenerator
    {
        public const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        public const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
        public const string DigitChars = "0123456789";
        public const string SymbolChars = "!@#$%^&*()-_=+[]{};:,.?/~";
        /// <summary>見間違えやすい文字 (0/O/o, 1/l/I/|, 5/S, 2/Z, 8/B, 引用符など)</summary>
        public const string AmbiguousChars = "0Oo1lI|5S2Z8B`'\"";

        public static List<string> Sets(PasswordOptions o)
        {
            var sets = new List<string>();
            Func<string, string> f = s => o.ExcludeAmbiguous ? new string(s.Where(c => AmbiguousChars.IndexOf(c) < 0).ToArray()) : s;
            if (o.Upper) sets.Add(f(UpperChars));
            if (o.Lower) sets.Add(f(LowerChars));
            if (o.Digits) sets.Add(f(DigitChars));
            if (o.Symbols) sets.Add(f(SymbolChars));
            return sets.Where(s => s.Length > 0).ToList();
        }

        /// <summary>暗号論的乱数で生成。選んだ文字種は必ず 1 文字以上含める。</summary>
        public static string Generate(PasswordOptions o)
        {
            o.Normalize();
            var sets = Sets(o);
            var all = string.Concat(sets);
            var chars = new char[o.Length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                int i = 0;
                // 各文字種から 1 文字ずつ (長さが足りる範囲で)
                foreach (var s in sets) { if (i >= chars.Length) break; chars[i++] = s[Uniform(rng, s.Length)]; }
                while (i < chars.Length) chars[i++] = all[Uniform(rng, all.Length)];
                // Fisher-Yates でシャッフル
                for (int k = chars.Length - 1; k > 0; k--)
                {
                    int j = Uniform(rng, k + 1);
                    var t = chars[k]; chars[k] = chars[j]; chars[j] = t;
                }
            }
            var result = new string(chars);
            Array.Clear(chars, 0, chars.Length);
            return result;
        }

        /// <summary>0..n-1 の一様乱数 (剰余の偏りが出ないよう棄却法)</summary>
        public static int Uniform(RandomNumberGenerator rng, int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException("n");
            var buf = new byte[4];
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)n);
            while (true)
            {
                rng.GetBytes(buf);
                uint v = BitConverter.ToUInt32(buf, 0);
                if (v < limit) return (int)(v % (uint)n);
            }
        }
    }

    public struct Strength
    {
        public int Level;      // 0..4
        public double Bits;
        public string Label;
    }

    public static class StrengthMeter
    {
        public static readonly string[] Labels = { "とても弱い", "弱い", "普通", "強い", "とても強い" };

        /// <summary>文字種と長さからの推定エントロピーで判定 (繰り返しや単純な並びは減点)</summary>
        public static Strength Evaluate(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return new Strength { Level = 0, Bits = 0, Label = "未入力" };
            int pool = 0;
            if (pw.Any(char.IsUpper)) pool += 26;
            if (pw.Any(char.IsLower)) pool += 26;
            if (pw.Any(char.IsDigit)) pool += 10;
            if (pw.Any(c => !char.IsLetterOrDigit(c) && c < 128)) pool += 32;
            if (pw.Any(c => c >= 128)) pool += 100;
            int distinct = pw.Distinct().Count();
            // 同じ文字ばかり・連続した並びは実効長を減らす
            int effective = Math.Min(pw.Length, distinct * 2);
            int seq = 0;
            for (int i = 1; i < pw.Length; i++) if (Math.Abs(pw[i] - pw[i - 1]) <= 1) seq++;
            effective = Math.Max(1, effective - seq / 2);
            double bits = effective * Math.Log(Math.Max(pool, 2), 2);
            int level = bits < 28 ? 0 : bits < 40 ? 1 : bits < 60 ? 2 : bits < 80 ? 3 : 4;
            return new Strength { Level = level, Bits = bits, Label = Labels[level] };
        }
    }
}
