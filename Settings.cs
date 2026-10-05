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
        /// Deep copy (through JSON, so it stays correct when nested settings are added).
        /// </summary>
        public Profile Clone()
        {
            return JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(this, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        }
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
            return profile;
        }
    }
}
