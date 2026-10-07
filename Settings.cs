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
        /// <summary>
        /// Scan limits from the antenna. Since <see cref="ScanModel"/> 1: the physical limits of the antenna (fixed).
        /// In older profiles (model 0): the limits of the old beam in neutral position, moved by the tilt up to
        /// <see cref="TiltMax"/> (converted by <see cref="MigrateScanModel"/>). The defaults are the old ones, so a
        /// new profile is converted in the same way.
        /// </summary>
        public double ScanUp { get; set; } = 8;
        public double ScanDown { get; set; } = -1;
        public double ScanLeft { get; set; } = 10;
        public double ScanRight { get; set; } = 10;
        /// <summary>Antenna tilt step.</summary>
        public double TiltStep { get; set; } = 2;
        /// <summary>Old maximum tilt: only read to convert the profiles of model 0 (the tilt range now follows from the beam).</summary>
        public double TiltMax { get; set; } = 10;
        /// <summary>0: scan limits of the old model (beam = scan limits, moved by the tilt); 1: scan limits + antenna beam.</summary>
        public int ScanModel { get; set; }
        /// <summary>Antenna beam: width (degrees) and centre in neutral position (EL up from the horizon, AZ right of the centreline).</summary>
        public double BeamElevation { get; set; } = 9;
        public double BeamAzimuth { get; set; } = 20;
        public double BeamElevationNeutral { get; set; } = 3.5;
        public double BeamAzimuthNeutral { get; set; }
        /// <summary>Draw the edges of the antenna beam (off: the beam is shown only by the thicker range marks).</summary>
        public bool ShowBeamEdges { get; set; }
        /// <summary>
        /// Narrow antenna beam moved by the tilt (an advanced function). Off: the beam is the scan limits, no tilt.
        /// On in the profiles of the earlier versions (same picture), off in a new profile (<see cref="CreateNew"/>).
        /// </summary>
        public bool BeamEnabled { get; set; } = true;
        /// <summary>
        /// Elevation centre of the beam in neutral = the glide path angle in use (instead of
        /// <see cref="BeamElevationNeutral"/>). On by default.
        /// </summary>
        public bool BeamElevationNeutralAuto { get; set; } = true;
        /// <summary>
        /// Coasting tracks (modern display): seconds a track out of the beam is still shown at its estimated
        /// position (0 = hidden at once).
        /// </summary>
        public double CoastSeconds { get; set; } = 8;
        /// <summary>Track filter of the modern display: smooth tracks (see TrackFilter).</summary>
        public TrackSmoothing TrackSmoothing { get; set; } = TrackSmoothing.Light;
        public const double MaxCoastSeconds = 30;
        /// <summary>Largest scan limit angle (a limit near 90° would be drawn far away).</summary>
        public const double MaxScanAngle = 80;
        /// <summary>
        /// Azimuth tilt left/right swapped: false (default) = left/right as seen by the pilot flying the approach;
        /// true = as seen from the runway looking at the approach (controller's view). Applies to the AZ TILT knob,
        /// the arrow keys, the buttons and the L/R readouts; the antenna itself is the same.
        /// </summary>
        public bool AzimuthTiltSwapped { get; set; }

        // Units and references.
        public PressureReference PressureReference { get; set; } = PressureReference.QNH;
        public PressureUnit PressureUnit { get; set; } = PressureUnit.HectoPascal;
        public MinimaLabel MinimaLabel { get; set; } = MinimaLabel.DaDh;
        public bool ShowAltitudeScale { get; set; } = true;
        /// <summary>Horizontal lines every 1000 (in the unit of the scale) in the vertical view: off by default.</summary>
        public bool ShowAltitudeLines { get; set; }
        /// <summary>
        /// Unit of heights: altitude scale and labels (altitude, deviations in ft or m, vertical speed in ft/min or m/s).
        /// </summary>
        public LengthUnit AltitudeScaleUnit { get; set; } = LengthUnit.Feet;

        // Tracks and labels.
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
        /// <summary>Track ID label field: a random two-digit ID for each new track (an assigned ID is shown anyway).</summary>
        public bool RandomTrackIds { get; set; } = true;
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
        /// <summary>Symbol of a coasting track (estimated position, out of the beam).</summary>
        public SymbolSetting CoastSymbol { get; set; } = new(SymbolShape.Diamond, 12);

        /// <summary>Antenna scan effect: a sweeping beam drawn over the views, graphic only (no effect on the data).</summary>
        public bool ScanEffect { get; set; } = true;
        /// <summary>Modern display, or analog scope (round phosphor screen, echoes lit by the beam, no labels, knobs).</summary>
        public DisplayMode DisplayMode { get; set; } = DisplayMode.Modern;
        /// <summary>Range marks drawn at each display range.</summary>
        public RangeMarkSettings RangeMarks { get; set; } = RangeMarkSettings.Default();
        /// <summary>Colours, dash styles and widths of the modern display.</summary>
        public DisplayStyleSettings Style { get; set; } = DisplayStyleSettings.CreateDefault();
        /// <summary>Phosphor colour of the analog scope (#RRGGBB).</summary>
        public string AnalogColor { get; set; } = "#A8FF60";
        /// <summary>
        /// Brightness of the radar picture, percent (100 = normal), modern display and analog scope. Above 100 the
        /// colours are boosted (for dim monitors).
        /// </summary>
        public int BrightnessModern { get; set; } = 100;
        public int BrightnessAnalog { get; set; } = 100;
        public const int MinBrightness = 10;
        public const int BrightnessStep = 10;
        public const int MaxBrightness = 150;
        /// <summary>Distance reminders for all runways (each runway can have its own too, see AppSettings).</summary>
        public List<DistanceReminder> Reminders { get; set; } = [];
        /// <summary>
        /// Elevation view: distance text below the horizon line and reminder markers above it; false: text above, markers below.
        /// </summary>
        public bool RangeTextBelowHorizon { get; set; }
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
            if (!Enum.IsDefined(TrackSmoothing)) TrackSmoothing = TrackSmoothing.Light;
            if (!Enum.IsDefined(DisplayMode)) DisplayMode = DisplayMode.Modern;
            RangeMarks ??= RangeMarkSettings.Default();
            RangeMarks.Normalize();
            Style ??= DisplayStyleSettings.CreateDefault();
            Style.Normalize();
            if (!ColorText.TryParse(AnalogColor, out _)) AnalogColor = "#A8FF60";
            BrightnessModern = Math.Clamp(BrightnessModern, MinBrightness, MaxBrightness);
            BrightnessAnalog = Math.Clamp(BrightnessAnalog, MinBrightness, MaxBrightness);
            Reminders ??= [];
            DistanceReminder.Normalize(Reminders);
            if (double.IsNaN(MagneticVariation) || Math.Abs(MagneticVariation) > 90) MagneticVariation = 0;
            TrackSymbol = NormalizeSymbol(TrackSymbol, new(SymbolShape.CrossCircle, 12));
            ThresholdSymbol = NormalizeSymbol(ThresholdSymbol, new(SymbolShape.Line, 10));
            TouchdownSymbol = NormalizeSymbol(TouchdownSymbol, new(SymbolShape.Line, 12));
            AntennaSymbol = NormalizeSymbol(AntennaSymbol, new(SymbolShape.Square, 8));
            HistorySymbol = NormalizeSymbol(HistorySymbol, new(SymbolShape.FilledCircle, 3));
            CoastSymbol = NormalizeSymbol(CoastSymbol, new(SymbolShape.Diamond, 12));
            CoastSeconds = double.IsNaN(CoastSeconds) ? 8 : Math.Clamp(CoastSeconds, 0, MaxCoastSeconds);
            MigrateScanModel();
            if (double.IsNaN(TiltStep) || TiltStep <= 0) TiltStep = 2;
            if (double.IsNaN(ScanUp) || double.IsNaN(ScanDown) || ScanUp <= ScanDown) { ScanUp = 18; ScanDown = -11; }
            ScanUp = Math.Min(ScanUp, MaxScanAngle);
            ScanDown = Math.Max(ScanDown, -MaxScanAngle);
            if (ScanUp <= ScanDown) { ScanUp = 18; ScanDown = -11; }
            ScanLeft = double.IsNaN(ScanLeft) || ScanLeft <= 0 ? 20 : Math.Min(ScanLeft, MaxScanAngle);
            ScanRight = double.IsNaN(ScanRight) || ScanRight <= 0 ? 20 : Math.Min(ScanRight, MaxScanAngle);
            BeamElevation = double.IsNaN(BeamElevation) ? 9 : Math.Clamp(BeamElevation, 0.1, 90);
            BeamAzimuth = double.IsNaN(BeamAzimuth) ? 20 : Math.Clamp(BeamAzimuth, 0.1, 180);
            if (double.IsNaN(BeamElevationNeutral)) BeamElevationNeutral = (ScanUp + ScanDown) / 2;
            if (double.IsNaN(BeamAzimuthNeutral)) BeamAzimuthNeutral = 0;
        }

        /// <summary>
        /// Profile created when there is none yet (first start): wide scan limits that work at once, no antenna beam
        /// and no tilt (advanced functions, turned on in the settings).
        /// </summary>
        public static Profile CreateNew()
        {
            Profile profile = new()
            {
                ScanModel = 1,
                ScanUp = 10,
                ScanDown = -1,
                ScanLeft = 15,
                ScanRight = 15,
                BeamEnabled = false
            };
            profile.NarrowBeam();
            profile.Normalize();
            return profile;
        }

        /// <summary>
        /// Beam a few degrees narrower than the scan limits, centred: set when the antenna beam is turned on, so the
        /// tilt has some room at once and the user sees how it works.
        /// </summary>
        /// <param name="onlyWhereFull">Only an axis whose beam is as wide as the scan limits (a beam already set is kept).</param>
        public void NarrowBeam(bool onlyWhereFull = false)
        {
            if (!onlyWhereFull || BeamElevation >= ScanUp - ScanDown - 0.01)
            {
                BeamElevation = Math.Max(1, ScanUp - ScanDown - 4);
                BeamElevationNeutral = (ScanUp + ScanDown) / 2;
            }
            if (!onlyWhereFull || BeamAzimuth >= ScanLeft + ScanRight - 0.01)
            {
                BeamAzimuth = Math.Max(1, ScanLeft + ScanRight - 6);
                BeamAzimuthNeutral = (ScanRight - ScanLeft) / 2;
            }
        }

        /// <summary>
        /// Converts a profile of the old scan model (scan limits moved by the tilt up to TiltMax) to the scan limits
        /// + antenna beam model, with the same picture: the scan limits become the old ones widened by the maximum
        /// tilt, the beam is the old sector and its neutral position the old neutral sector. Done once.
        /// </summary>
        public void MigrateScanModel()
        {
            if (ScanModel >= 1) return;
            // Old values not valid: the old defaults first.
            if (double.IsNaN(ScanUp) || double.IsNaN(ScanDown) || ScanUp <= ScanDown) { ScanUp = 8; ScanDown = -1; }
            if (double.IsNaN(ScanLeft) || ScanLeft <= 0) ScanLeft = 10;
            if (double.IsNaN(ScanRight) || ScanRight <= 0) ScanRight = 10;
            double tilt = double.IsNaN(TiltMax) ? 0 : Math.Clamp(TiltMax, 0, 45);
            BeamElevation = Math.Max(0.1, ScanUp - ScanDown);
            BeamAzimuth = Math.Max(0.1, ScanLeft + ScanRight);
            BeamElevationNeutral = (ScanUp + ScanDown) / 2;
            BeamAzimuthNeutral = (ScanRight - ScanLeft) / 2;
            ScanUp += tilt;
            ScanDown -= tilt;
            ScanLeft += tilt;
            ScanRight += tilt;
            ScanModel = 1;
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
        /// Glide path angle of the approach in use when the program was closed (runways with several approaches in
        /// runways.par); only a published one is restored.
        /// </summary>
        public double? LastGlideSlope { get; set; }
        /// <summary>
        /// Size and position of the main window when the program was closed, restored at start.
        /// </summary>
        public WindowPlacement? Window { get; set; }
        /// <summary>
        /// Distance reminders of single runways, by "ICAO DESIGNATOR" (in addition to those of the profile).
        /// </summary>
        public Dictionary<string, List<DistanceReminder>> RunwayReminders { get; set; } = [];
        /// <summary>Coordination light panel with the tower.</summary>
        public CoordinationSettings Coordination { get; set; } = new();

        /// <summary>Reminders of a runway (created empty if needed).</summary>
        public List<DistanceReminder> RemindersOf(string runway)
        {
            if (!RunwayReminders.TryGetValue(runway, out List<DistanceReminder>? list) || list == null)
            {
                list = [];
                RunwayReminders[runway] = list;
            }
            return list;
        }

        /// <summary>Reminders drawn for a runway: those of the active profile, then those of the runway.</summary>
        public IEnumerable<DistanceReminder> RemindersFor(Runway runway)
        {
            IEnumerable<DistanceReminder> own = RunwayReminders.TryGetValue(runway.ToString(), out List<DistanceReminder>? list) && list != null ? list : [];
            return Active.Reminders.Concat(own);
        }

        /// <summary>
        /// Reminders drawn for a runway with several approaches (lines of the file with different names, e.g.
        /// "LIPC 11 2.8" and "LIPC 11 2.5"): those of the profile, then those saved for any of its lines.
        /// </summary>
        public IEnumerable<DistanceReminder> RemindersFor(IEnumerable<string> runwayNames)
        {
            IEnumerable<DistanceReminder> own = runwayNames.Distinct()
                .SelectMany(name => RunwayReminders.TryGetValue(name, out List<DistanceReminder>? list) && list != null ? list : []);
            return Active.Reminders.Concat(own);
        }

        [JsonIgnore]
        public Profile Active => Profiles.FirstOrDefault(p => p.Name == ActiveProfile) ?? Profiles[0];

        /// <summary>
        /// Repairs a file edited by hand or written by another version: at least one profile,
        /// unique non-empty names, a valid active profile.
        /// </summary>
        public void Normalize()
        {
            Profiles ??= [];
            RunwayReminders ??= [];
            Coordination ??= new();
            Coordination.Normalize();
            foreach (string key in RunwayReminders.Keys.ToList())
            {
                if (RunwayReminders[key] == null || RunwayReminders[key].Count == 0) RunwayReminders.Remove(key);
                else DistanceReminder.Normalize(RunwayReminders[key]);
            }
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
                Profiles.Add(Profile.CreateNew());
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
