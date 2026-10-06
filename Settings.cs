using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuroraPAR
{
    internal enum RunwaySide
    {
        Left,
        Right
    }

    /// <summary>
    /// Display range used when the program starts.
    /// </summary>
    internal enum StartupRange
    {
        /// <summary>The range in use when the program was last closed.</summary>
        LastUsed,
        /// <summary>The default range of the runway, from runways.par.</summary>
        RunwayDefault,
        /// <summary>Always <see cref="Profile.PreferredRange"/>.</summary>
        Fixed
    }

    /// <summary>
    /// Display range used when another runway is selected.
    /// </summary>
    internal enum RunwayChangeRange
    {
        /// <summary>The default range of the new runway, from runways.par.</summary>
        RunwayDefault,
        /// <summary>The range currently in use is kept.</summary>
        KeepCurrent,
        /// <summary>Always <see cref="Profile.PreferredRange"/>.</summary>
        Fixed
    }

    internal enum PressureReference
    {
        /// <summary>Altitudes (above mean sea level).</summary>
        QNH,
        /// <summary>Heights above the runway threshold.</summary>
        QFE
    }

    internal enum PressureUnit
    {
        HectoPascal,
        InchesOfMercury
    }

    /// <summary>
    /// Name used for the minimum: the altitude form with QNH, the height form with QFE.
    /// </summary>
    internal enum MinimaLabel
    {
        DaDh,
        OcaOch,
        MdaMdh
    }

    internal enum LengthUnit
    {
        Feet,
        Metres
    }

    /// <summary>
    /// Pressure conversions and formatting.
    /// </summary>
    internal static class Pressure
    {
        public const double HectoPascalPerInch = 33.8639;

        /// <summary>
        /// QFE at the given elevation from the QNH (standard atmosphere), in hPa.
        /// </summary>
        public static double QfeFromQnh(double qnhHectoPascal, double elevationFt)
        {
            double metres = elevationFt * 0.3048;
            return qnhHectoPascal * Math.Pow(1 - 0.0065 * metres / 288.15, 5.25588);
        }

        public static string Format(double hectoPascal, PressureUnit unit)
        {
            return unit == PressureUnit.InchesOfMercury
                ? (hectoPascal / HectoPascalPerInch).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                : Math.Round(hectoPascal).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The two names of a minimum: (altitude, height), e.g. ("OCA", "OCH").
        /// </summary>
        public static (string Altitude, string Height) Names(MinimaLabel label)
        {
            return label switch
            {
                MinimaLabel.OcaOch => ("OCA", "OCH"),
                MinimaLabel.MdaMdh => ("MDA", "MDH"),
                _ => ("DA", "DH")
            };
        }
    }

    /// <summary>
    /// Display ranges available, in NM.
    /// </summary>
    internal static class Ranges
    {
        public static readonly double[] Values = [1, 2.5, 5, 10, 15, 20];

        /// <summary>
        /// Index of the available range closest to the given one.
        /// </summary>
        public static int IndexOfClosest(double range)
        {
            int best = 0;
            for (int i = 1; i < Values.Length; i++)
            {
                if (Math.Abs(Values[i] - range) < Math.Abs(Values[best] - range))
                {
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// A named set of user preferences. New preferences are added here as properties with a default value:
    /// profiles saved by older versions simply get the default for the missing ones.
    /// </summary>
    internal class Profile
    {
        public string Name { get; set; } = AppSettings.DefaultProfileName;
        /// <summary>
        /// Side of the screen where the runway is shown. With the runway on the right the profile view is
        /// mirrored horizontally and the azimuth view is rotated by 180°, so left/right of the approach stay correct.
        /// </summary>
        public RunwaySide RunwaySide { get; set; } = RunwaySide.Left;
        /// <summary>
        /// Display range used when the program starts.
        /// </summary>
        public StartupRange StartupRange { get; set; } = StartupRange.LastUsed;
        /// <summary>
        /// Display range used when another runway is selected.
        /// </summary>
        public RunwayChangeRange RunwayChangeRange { get; set; } = RunwayChangeRange.RunwayDefault;
        /// <summary>
        /// Preferred range in NM, used at start and/or on runway change when set to Fixed.
        /// </summary>
        public double PreferredRange { get; set; } = 15;

        // Radar equipment (see Radar): angles in degrees.
        /// <summary>Approach limits from the ideal glide path and centreline (green inside, red outside).</summary>
        public double ApproachAbove { get; set; } = 0.5;
        public double ApproachBelow { get; set; } = 0.5;
        public double ApproachLeft { get; set; } = 1.5;
        public double ApproachRight { get; set; } = 1.5;
        /// <summary>Scan limits from the antenna, in neutral position.</summary>
        public double ScanUp { get; set; } = 8;
        public double ScanDown { get; set; } = -1;
        public double ScanLeft { get; set; } = 10;
        public double ScanRight { get; set; } = 10;
        /// <summary>Antenna tilt step and maximum.</summary>
        public double TiltStep { get; set; } = 2;
        public double TiltMax { get; set; } = 10;

        // Units and references.
        public PressureReference PressureReference { get; set; } = PressureReference.QNH;
        public PressureUnit PressureUnit { get; set; } = PressureUnit.HectoPascal;
        public MinimaLabel MinimaLabel { get; set; } = MinimaLabel.DaDh;
        public bool ShowAltitudeScale { get; set; } = true;
        /// <summary>
        /// Unit of heights: altitude scale and labels (altitude, deviations in ft or m, vertical speed in ft/min or m/s).
        /// </summary>
        public LengthUnit AltitudeScaleUnit { get; set; } = LengthUnit.Feet;

        // Tracks and labels.
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
        /// <summary>History tails: previous positions of each track.</summary>
        public bool HistoryEnabled { get; set; } = true;
        public int HistoryDots { get; set; } = 50;
        /// <summary>
        /// Seconds between two history dots. Aurora sends a position every 0.5 s: one dot per update made the
        /// tail look like a continuous line.
        /// </summary>
        public double HistoryInterval { get; set; } = 2;
        public SymbolSetting TrackSymbol { get; set; } = new(SymbolShape.CrossCircle, 12);
        public SymbolSetting ThresholdSymbol { get; set; } = new(SymbolShape.Line, 10);
        public SymbolSetting TouchdownSymbol { get; set; } = new(SymbolShape.Line, 12);
        public SymbolSetting AntennaSymbol { get; set; } = new(SymbolShape.Square, 8);
        public SymbolSetting HistorySymbol { get; set; } = new(SymbolShape.FilledCircle, 3);

        /// <summary>Antenna scan effect: a sweeping beam drawn over the views, graphic only (no effect on the data).</summary>
        public bool ScanEffect { get; set; } = true;
        /// <summary>Modern display, or analog scope (round phosphor screen, echoes lit by the beam, no labels, knobs).</summary>
        public DisplayMode DisplayMode { get; set; } = DisplayMode.Modern;
        /// <summary>
        /// Default magnetic variation (degrees, East positive) for the final course of runways without their own
        /// value in runways.par (the headings in the file are true).
        /// </summary>
        public double MagneticVariation { get; set; }
        public ScanEffectSpeed ScanEffectSpeed { get; set; } = ScanEffectSpeed.Normal;

        public const int MinHistoryDots = 3;
        public const int MaxHistoryDots = 100;
        public const double MinHistoryInterval = 0.5;
        public const double MaxHistoryInterval = 10;

        /// <summary>
        /// Repairs values missing or out of range (profiles from older versions or edited by hand).
        /// </summary>
        public void Normalize()
        {
            ElevationLabel ??= LabelLayout.DefaultElevation();
            AzimuthLabel ??= LabelLayout.DefaultAzimuth();
            ElevationLabel.Normalize();
            AzimuthLabel.Normalize();
            HistoryDots = Math.Clamp(HistoryDots, MinHistoryDots, MaxHistoryDots);
            HistoryInterval = double.IsNaN(HistoryInterval) ? 2 : Math.Clamp(HistoryInterval, MinHistoryInterval, MaxHistoryInterval);
            if (!Enum.IsDefined(ScanEffectSpeed)) ScanEffectSpeed = ScanEffectSpeed.Normal;
            if (!Enum.IsDefined(DisplayMode)) DisplayMode = DisplayMode.Modern;
            if (double.IsNaN(MagneticVariation) || Math.Abs(MagneticVariation) > 90) MagneticVariation = 0;
            TrackSymbol = NormalizeSymbol(TrackSymbol, new(SymbolShape.CrossCircle, 12));
            ThresholdSymbol = NormalizeSymbol(ThresholdSymbol, new(SymbolShape.Line, 10));
            TouchdownSymbol = NormalizeSymbol(TouchdownSymbol, new(SymbolShape.Line, 12));
            AntennaSymbol = NormalizeSymbol(AntennaSymbol, new(SymbolShape.Square, 8));
            HistorySymbol = NormalizeSymbol(HistorySymbol, new(SymbolShape.FilledCircle, 3));
        }

        private static SymbolSetting NormalizeSymbol(SymbolSetting? symbol, SymbolSetting fallback)
        {
            if (symbol == null || !Enum.IsDefined(symbol.Shape)) return fallback;
            symbol.Size = Math.Clamp(symbol.Size, 2, 60);
            return symbol;
        }

        /// <summary>
        /// Deep copy (through JSON, so it stays correct when nested settings are added).
        /// </summary>
        public Profile Clone()
        {
            return JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        }
    }

    /// <summary>
    /// Main window bounds in its normal (not maximized) state, plus whether it was maximized.
    /// </summary>
    internal class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }

    /// <summary>
    /// Everything saved in the settings file: the profiles, the active one and the last session state.
    /// </summary>
    internal class AppSettings
    {
        public const string DefaultProfileName = "Default";
        public int Version { get; set; } = 1;
        public string ActiveProfile { get; set; } = DefaultProfileName;
        public List<Profile> Profiles { get; set; } = [];
        /// <summary>
        /// Runway selected when the program was closed ("ICAO DESIGNATOR"), restored at start.
        /// </summary>
        public string? LastRunway { get; set; }
        /// <summary>
        /// Display range (NM) used when the program was closed, restored together with the runway.
        /// </summary>
        public double? LastRange { get; set; }
        /// <summary>
        /// Size and position of the main window when the program was closed, restored at start.
        /// </summary>
        public WindowPlacement? Window { get; set; }

        [JsonIgnore]
        public Profile Active => Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];

        /// <summary>
        /// Repairs a file edited by hand or written by another version: at least one profile,
        /// unique non-empty names, a valid active profile.
        /// </summary>
        public void Normalize()
        {
            Profiles ??= [];
            List<Profile> valid = Profiles.Where(p => p != null).ToList();
            Profiles = [];
            foreach (Profile p in valid)
            {
                p.Normalize();
                p.Name = UniqueName(string.IsNullOrWhiteSpace(p.Name) ? "Profile" : p.Name.Trim());
                Profiles.Add(p);
            }
            if (Profiles.Count == 0)
            {
                Profiles.Add(new Profile());
            }
            if (!Profiles.Any(p => p.Name == ActiveProfile))
            {
                ActiveProfile = Profiles[0].Name;
            }
        }

        /// <summary>
        /// The given name, or the name followed by " (2)", " (3)"... if already used.
        /// </summary>
        public string UniqueName(string name)
        {
            if (!Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
            for (int i = 2; ; i++)
            {
                string candidate = $"{name} ({i})";
                if (!Profiles.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                {
                    return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Reads and writes the settings file.
    /// Location: the user's settings folder (Windows %AppData%\AuroraPAR, Linux ~/.config/AuroraPAR, macOS the
    /// equivalent), so settings survive program updates and work on every platform.
    /// Portable mode: if a file named <see cref="FileName"/> exists next to the program (even empty), that one is used.
    /// </summary>
    internal static class SettingsStore
    {
        public const string FileName = "AuroraPAR.settings.json";

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string PortablePath => Path.Combine(AppContext.BaseDirectory, FileName);
        public static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPAR", FileName);
        public static bool IsPortable => File.Exists(PortablePath);
        public static string FilePath => IsPortable ? PortablePath : UserPath;

        /// <summary>
        /// Loads the settings. A missing or empty file gives the defaults. A damaged file is kept as
        /// "...json.bad" (so nothing is lost) and the defaults are used.
        /// </summary>
        public static AppSettings Load()
        {
            string path = FilePath;
            AppSettings? settings = null;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
                }
            }
            catch (Exception)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { }
                settings = null;
            }
            settings ??= new AppSettings();
            settings.Normalize();
            return settings;
        }

        /// <summary>
        /// Saves the settings. The file is written to a temporary file first and then moved,
        /// so a crash while saving cannot leave a half-written settings file.
        /// Returns the error message, or null on success.
        /// </summary>
        public static string? Save(AppSettings settings)
        {
            string path = FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temp, path, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static void ExportProfile(Profile profile, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
        }

        /// <summary>
        /// Reads a profile exported with <see cref="ExportProfile"/>. Throws if the file is not a valid profile.
        /// </summary>
        public static Profile ImportProfile(string path)
        {
            Profile? profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), JsonOptions);
            if (profile == null)
            {
                throw new InvalidDataException("The file does not contain an AuroraPAR profile.");
            }
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = Path.GetFileNameWithoutExtension(path);
            }
            profile.Normalize();
            return profile;
        }
    }
}
