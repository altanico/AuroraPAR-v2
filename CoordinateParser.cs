using System.Globalization;
using System.Text.RegularExpressions;

namespace AuroraPAR
{
    /// <summary>
    /// Reads latitudes and longitudes written in any common format and converts them to decimal degrees
    /// (north and east positive). Accepted, for example:
    ///   44.838694   44,838694   -0.701   N44.838694   0.701W
    ///   44°50'19.3"N   44 50 19.3 N   44°50.32'N   N44 50.32
    ///   445019N   445019.30N   0004204W   4450N   00042W   (AIP compact format)
    /// "O" (Ovest) is accepted for west. A latitude and a longitude together in one text
    /// (e.g. "445019N 0004204W" or "44.8387, -0.701") can be read with <see cref="TryParsePair"/>.
    /// </summary>
    internal static class CoordinateParser
    {
        private static readonly Regex Numbers = new(@"\d+(?:\.\d+)?");
        private static readonly Regex AllowedBody = new(@"^[0-9.\s°'""]+$");
        private static readonly Regex BadDot = new(@"\.(?!\d)|(?<!\d)\.");
        private static readonly Regex DecimalComma = new(@"(\d),(\d)");

        /// <summary>
        /// Upper case, typographic quotes and degree signs unified; decimal commas converted when requested.
        /// </summary>
        private static string Normalize(string text, bool convertDecimalComma)
        {
            string s = text.Trim().ToUpperInvariant()
                .Replace('’', '\'').Replace('′', '\'').Replace('‘', '\'').Replace('´', '\'').Replace('`', '\'')
                .Replace('″', '"').Replace('“', '"').Replace('”', '"')
                .Replace("''", "\"")
                .Replace('º', '°').Replace('˚', '°');
            if (convertDecimalComma)
            {
                s = DecimalComma.Replace(s, "$1.$2");
            }
            return s;
        }

        private static bool IsLatitudeLetter(char c) => c == 'N' || c == 'S';
        private static bool IsLongitudeLetter(char c) => c == 'E' || c == 'W' || c == 'O';

        /// <summary>
        /// Reads one coordinate. <paramref name="latitude"/> tells which one is expected (limits ±90 or ±180).
        /// </summary>
        public static bool TryParse(string text, bool latitude, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = Normalize(text, convertDecimalComma: true);
            int sign = 1;
            char? hemisphere = null;
            if (s.Length > 0 && (IsLatitudeLetter(s[0]) || IsLongitudeLetter(s[0])))
            {
                hemisphere = s[0];
                s = s[1..].Trim();
            }
            else if (s.Length > 0 && (IsLatitudeLetter(s[^1]) || IsLongitudeLetter(s[^1])))
            {
                hemisphere = s[^1];
                s = s[..^1].Trim();
            }
            if (hemisphere is char h)
            {
                if (latitude != IsLatitudeLetter(h)) return false;
                if (h == 'S' || h == 'W' || h == 'O') sign = -1;
            }
            if (s.StartsWith('-') || s.StartsWith('+'))
            {
                // A sign together with a hemisphere letter is ambiguous.
                if (hemisphere != null) return false;
                if (s[0] == '-') sign = -1;
                s = s[1..].Trim();
            }
            if (s.Length == 0 || !AllowedBody.IsMatch(s) || BadDot.IsMatch(s)) return false;

            MatchCollection numbers = Numbers.Matches(s);
            double max = latitude ? 90 : 180;
            double degrees;
            if (numbers.Count == 1)
            {
                string number = numbers[0].Value;
                bool hasSymbols = s.IndexOfAny(['°', '\'', '"']) >= 0;
                int integerDigits = number.Split('.')[0].Length;
                double plain = double.Parse(number, CultureInfo.InvariantCulture);
                bool compact = !hasSymbols && IsCompactLength(integerDigits, latitude) && (hemisphere != null || plain > max);
                if (compact)
                {
                    if (!TryParseCompact(number, latitude, out degrees)) return false;
                }
                else
                {
                    degrees = plain;
                }
            }
            else if (numbers.Count == 2 || numbers.Count == 3)
            {
                // Degrees minutes [seconds]: only the last value may have decimals.
                for (int i = 0; i < numbers.Count - 1; i++)
                {
                    if (numbers[i].Value.Contains('.')) return false;
                }
                double d = double.Parse(numbers[0].Value, CultureInfo.InvariantCulture);
                double m = double.Parse(numbers[1].Value, CultureInfo.InvariantCulture);
                double sec = numbers.Count == 3 ? double.Parse(numbers[2].Value, CultureInfo.InvariantCulture) : 0;
                if (m >= 60 || sec >= 60) return false;
                degrees = d + m / 60 + sec / 3600;
            }
            else
            {
                return false;
            }
            if (degrees > max) return false;
            value = sign * degrees;
            return true;
        }

        /// <summary>
        /// Lengths of the integer part in the AIP compact format: latitude DDMM or DDMMSS, longitude DDDMM or DDDMMSS.
        /// </summary>
        private static bool IsCompactLength(int integerDigits, bool latitude)
        {
            return latitude ? integerDigits == 4 || integerDigits == 6 : integerDigits == 5 || integerDigits == 7;
        }

        private static bool TryParseCompact(string number, bool latitude, out double degrees)
        {
            degrees = 0;
            string[] parts = number.Split('.');
            string digits = parts[0];
            string fraction = parts.Length > 1 ? "." + parts[1] : "";
            int degreeDigits = latitude ? 2 : 3;
            double d = int.Parse(digits[..degreeDigits], CultureInfo.InvariantCulture);
            string rest = digits[degreeDigits..];
            double m, s = 0;
            if (rest.Length == 2)
            {
                m = double.Parse(rest + fraction, CultureInfo.InvariantCulture);
            }
            else
            {
                m = int.Parse(rest[..2], CultureInfo.InvariantCulture);
                s = double.Parse(rest[2..] + fraction, CultureInfo.InvariantCulture);
            }
            if (m >= 60 || s >= 60) return false;
            degrees = d + m / 60 + s / 3600;
            return true;
        }

        /// <summary>
        /// Reads a latitude and a longitude written together, in this order, in any of the accepted formats,
        /// separated by spaces, a comma, a semicolon or a slash.
        /// </summary>
        public static bool TryParsePair(string text, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // First without converting decimal commas (so "44.8,8.5" splits at the comma), then with.
            foreach (bool convert in new[] { false, true })
            {
                foreach ((string a, string b) in SplitCandidates(Normalize(text, convert)))
                {
                    if (TryParse(a, true, out latitude) && TryParse(b, false, out longitude))
                    {
                        return true;
                    }
                }
            }
            latitude = longitude = 0;
            return false;
        }

        private static IEnumerable<(string, string)> SplitCandidates(string s)
        {
            int ns = s.IndexOfAny(['N', 'S']);
            int ew = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (IsLongitudeLetter(s[i])) { ew = i; break; }
            }
            if (ns >= 0 && ew >= 0)
            {
                // Letters before the numbers (N44 50 W000 42): split before the longitude letter;
                // letters after the numbers (445019N 0004204W): split after the latitude letter.
                if (ns == 0 && ew > 0) yield return (s[..ew], s[ew..]);
                else if (ns + 1 < s.Length) yield return (s[..(ns + 1)], s[(ns + 1)..]);
            }
            foreach (char separator in new[] { ';', '/', '|', ',' })
            {
                int i = s.IndexOf(separator);
                if (i > 0 && i < s.Length - 1) yield return (s[..i], s[(i + 1)..]);
            }
            string[] words = Regex.Split(s.Trim(), @"\s+");
            for (int k = 1; k < words.Length; k++)
            {
                yield return (string.Join(' ', words[..k]), string.Join(' ', words[k..]));
            }
        }

        /// <summary>
        /// Decimal degrees with the precision used in the runway file.
        /// </summary>
        public static string Format(double degrees)
        {
            return degrees.ToString("0.0#######", CultureInfo.InvariantCulture);
        }
    }
}
