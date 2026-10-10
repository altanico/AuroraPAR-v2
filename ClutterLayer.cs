using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraPAR
{
    /// <summary>Rain intensity of the analog scope clutter.</summary>
    internal enum RainLevel
    {
        None,
        Light,
        Moderate,
        Heavy,
        Thunderstorm
    }

    /// <summary>Rain clutter: intensity from the METAR, and the filter set on the console.</summary>
    internal static class RainClutter
    {
        /// <summary>Present weather group of a METAR: -RA, +SHRA, TSRA, DZ, SN...</summary>
        private static readonly Regex Weather = new(@"^(\+|-)?(VC)?(MI|BC|PR|DR|BL|SH|TS|FZ)*(DZ|RA|SN|SG|PL|GR|GS|UP)+$");

        /// <summary>Intensity of the precipitation reported in a METAR (None when there is none).</summary>
        public static RainLevel FromMetar(string? metar)
        {
            if (string.IsNullOrWhiteSpace(metar)) return RainLevel.None;
            RainLevel level = RainLevel.None;
            foreach (string token in metar.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                // The trend and remarks are not the present weather.
                if (token is "RMK" or "TEMPO" or "BECMG" or "NOSIG") break;
                if (!Weather.IsMatch(token) || token.StartsWith("VC")) continue;
                RainLevel found = token.Contains("TS") ? RainLevel.Thunderstorm
                    : token.StartsWith('+') ? RainLevel.Heavy
                    : token.StartsWith('-') || token.Contains("DZ") ? RainLevel.Light
                    : RainLevel.Moderate;
                if (found > level) level = found;
            }
            return level;
        }

        /// <summary>Strength of the clutter, 0 to 1.</summary>
        public static double Strength(RainLevel level) => level switch
        {
            RainLevel.Light => 0.25,
            RainLevel.Moderate => 0.55,
            RainLevel.Heavy => 0.9,
            RainLevel.Thunderstorm => 1.0,
            _ => 0
        };

        public static string DisplayName(RainLevel level) => level switch
        {
            RainLevel.Light => "Light",
            RainLevel.Moderate => "Moderate",
            RainLevel.Heavy => "Heavy",
            RainLevel.Thunderstorm => "Thunderstorm",
            _ => "None"
        };
    }

    /// <summary>
    /// Rain clutter of the analog scope: irregular patches alternating with dots, only inside the antenna beam (with a
    /// short fade at its edges), brighter when the scan beam passes over them, like the echoes. The patches drift a
    /// little and the dots are renewed at every scan cycle. Graphic only: one bitmap per view at half resolution,
    /// updated every other frame.
    /// </summary>
    internal sealed class ClutterLayer
    {
        private const int Tile = 256;
        /// <summary>Screen pixels per bitmap pixel.</summary>
        private const int Pixel = 2;
        /// <summary>Width of the fade at the edges of the beam, as a fraction of the scan (a soft edge, not a cut).</summary>
        private const double EdgeFade = 0.015;
        private const int LutSize = 160;

        private static readonly float[] Coarse = MakeNoise(20, 11);
        private static readonly float[] Fine = MakeNoise(40, 23);
        private static readonly float[] Patch = MakeNoise(10, 5);

        private readonly Image image = new() { IsHitTestVisible = false, Stretch = Stretch.Fill };
        private WriteableBitmap? bitmap;
        private int[] pixels = [];
        private int frame;
        private readonly float[] light = new float[LutSize + 1];

        public Image Element => image;

        /// <summary>Geometry and state of a frame (logical coordinates as in the views).</summary>
        public struct Frame
        {
            public double Width, Height, Time, SweepCycle;
            public bool Elevation, RunwayOnRight, FlipVertically;
            public double XShift, OriginX, OriginY, EndX, LowY, HighY, HorizonY;
            public double Strength, Filter;
            public ScanEffectSpeed Speed;
            public Color Colour;
        }

        public void Hide() => image.Visibility = Visibility.Collapsed;

        public void Update(in Frame f)
        {
            if (f.Strength <= 0 || f.Width < 20 || f.Height < 20 || Math.Abs(f.HighY - f.LowY) < 1 || f.EndX - f.OriginX < 1)
            {
                Hide();
                return;
            }
            image.Visibility = Visibility.Visible;
            int w = (int)Math.Ceiling(f.Width / Pixel), h = (int)Math.Ceiling(f.Height / Pixel);
            if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            {
                bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                pixels = new int[w * h];
                image.Source = bitmap;
                frame = 0;
            }
            image.Width = w * Pixel;
            image.Height = h * Pixel;
            if ((frame++ & 1) != 0) return;

            // Light of the beam by position across the scan (the same law as the echoes).
            for (int i = 0; i <= LutSize; i++)
            {
                double p = -0.1 + 1.2 * i / LutSize;
                light[i] = (float)ScanEffect.BeamLight(f.Time, f.Speed, f.Elevation, p, 0.6);
            }
            // Rain is nearly static compared with an aircraft: the patches creep very slowly (no jumps), the dots stay.
            int cycle = 0;
            int driftX = (int)(f.Time * 0.15);
            int driftY = (int)(f.Time * 0.05);
            double ratio = Tile / f.Width;
            double thr = 0.80 - 0.24 * f.Strength;
            double dotBase = 0.05 * f.Strength;
            double gainFilter = 1 - 0.85 * f.Filter;
            double span = f.EndX - f.OriginX;
            double slope = (f.EndX - f.OriginX);
            Array.Clear(pixels);
            for (int by = 0; by < h; by++)
            {
                double sy = (by + 0.5) * Pixel;
                double ly = f.RunwayOnRight && f.FlipVertically ? f.Height - sy : sy;
                if (ly > f.HorizonY) continue;
                int ty = ((int)(sy * ratio) + driftY) & (Tile - 1);
                for (int bx = 0; bx < w; bx++)
                {
                    double sx = (bx + 0.5) * Pixel;
                    double lx = (f.RunwayOnRight ? f.Width - sx : sx) - f.XShift;
                    double dx = lx - f.OriginX;
                    if (dx <= 1) continue;
                    double y = f.OriginY + (ly - f.OriginY) * slope / dx;
                    double p = (y - f.LowY) / (f.HighY - f.LowY);
                    double edge = Math.Min(p, 1 - p);
                    double mask = Math.Clamp((edge + EdgeFade) / (2 * EdgeFade), 0, 1);
                    if (mask <= 0) continue;
                    int tx = ((int)(sx * ratio) + driftX) & (Tile - 1);
                    int ti = ty * Tile + tx;
                    double range = 0.4 + 0.6 * Math.Min(1, dx / span);
                    double n = 0.6 * Coarse[ti] + 0.4 * Fine[ti];
                    double mottle = 0.4 + 0.6 * Hash(bx, by, 0);
                    double blob = Math.Clamp((n - thr) / 0.12, 0, 1) * mottle * range;
                    double field = Math.Clamp((Patch[ti] - 0.35) * 1.8, 0, 1);
                    double dot = 0;
                    if (Hash(bx, by, cycle + 1) < dotBase * field * range * 3) dot = 0.3 + 0.7 * Hash(bx, by, cycle + 7);
                    double a = (blob * f.Strength * 1.1 + dot);
                    if (a <= 0.01) continue;
                    double lit = light[Math.Clamp((int)((p + 0.1) / 1.2 * LutSize), 0, LutSize)];
                    a = Math.Min(1, a * (0.45 + 0.9 * lit) * mask * gainFilter);
                    if (a <= 0.01) continue;
                    int alpha = (int)(a * 235);
                    pixels[by * w + bx] = (alpha << 24)
                        | ((f.Colour.R * alpha / 255) << 16)
                        | ((f.Colour.G * alpha / 255) << 8)
                        | (f.Colour.B * alpha / 255);
                }
            }
            bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        }

        private static double Hash(int x, int y, int salt)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
            h *= 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFF) / 65536.0;
        }

        /// <summary>Value noise 0..1 on a wrapping tile, with <paramref name="cells"/> cells across.</summary>
        private static float[] MakeNoise(int cells, int seed)
        {
            Random random = new(seed);
            float[] grid = new float[cells * cells];
            for (int i = 0; i < grid.Length; i++) grid[i] = (float)random.NextDouble();
            float[] tile = new float[Tile * Tile];
            double step = (double)cells / Tile;
            for (int y = 0; y < Tile; y++)
            {
                double gy = y * step;
                int y0 = (int)gy;
                double fy = gy - y0;
                fy = fy * fy * (3 - 2 * fy);
                for (int x = 0; x < Tile; x++)
                {
                    double gx = x * step;
                    int x0 = (int)gx;
                    double fx = gx - x0;
                    fx = fx * fx * (3 - 2 * fx);
                    int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
                    double top = grid[y0 * cells + x0] * (1 - fx) + grid[y0 * cells + x1] * fx;
                    double bottom = grid[y1 * cells + x0] * (1 - fx) + grid[y1 * cells + x1] * fx;
                    tile[y * Tile + x] = (float)(top * (1 - fy) + bottom * fy);
                }
            }
            return tile;
        }
    }
}
