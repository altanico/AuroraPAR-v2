using System.Windows.Media;

namespace AuroraPAR
{
    /// <summary>Colour helpers ("#RRGGBB" strings).</summary>
    internal static class ColorText
    {
        public static bool TryParse(string? text, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                object? value = ColorConverter.ConvertFromString(text.Trim());
                if (value is Color c)
                {
                    color = c;
                    return true;
                }
            }
            catch (FormatException)
            {
            }
            return false;
        }

        public static Color Parse(string? text, Color fallback)
        {
            return TryParse(text, out Color color) ? color : fallback;
        }

        public static string ToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
    }
}
