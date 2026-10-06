using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// AuroraCoord: only the coordination light panel, for the controller who does not need the PAR display
    /// (usually the tower). It reads the connected callsign from Aurora to link with the radar automatically.
    /// </summary>
    internal static class Program
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Settings next to the program if that file exists (portable), otherwise in %AppData%\AuroraPAR.</summary>
        private static string SettingsPath
        {
            get
            {
                string portable = Path.Combine(AppContext.BaseDirectory, "AuroraCoord.settings.json");
                if (File.Exists(portable)) return portable;
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AuroraPAR");
                return Path.Combine(folder, "AuroraCoord.settings.json");
            }
        }

        [STAThread]
        public static void Main()
        {
            CoordinationSettings settings = Load();
            Application app = new() { ShutdownMode = ShutdownMode.OnMainWindowClose };
            CoordinationWindow window = new(settings, () => Save(settings), null) { Title = "AuroraCoord - Coordination panel" };
            AuroraCallsign aurora = new();
            DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
            bool busy = false;
            timer.Tick += async (s, e) =>
            {
                if (busy) return;
                busy = true;
                try
                {
                    window.SetCallsign(await aurora.Get());
                }
                finally
                {
                    busy = false;
                }
            };
            window.Loaded += async (s, e) =>
            {
                window.SetCallsign(await aurora.Get());
                timer.Start();
            };
            window.Closed += (s, e) =>
            {
                timer.Stop();
                aurora.Dispose();
            };
            app.Run(window);
        }

        private static CoordinationSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    CoordinationSettings? settings = JsonSerializer.Deserialize<CoordinationSettings>(File.ReadAllText(SettingsPath), Json);
                    if (settings != null)
                    {
                        settings.Normalize();
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Damaged file: defaults.
            }
            CoordinationSettings defaults = new();
            defaults.Normalize();
            return defaults;
        }

        private static void Save(CoordinationSettings settings)
        {
            try
            {
                string path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Minimal client of Aurora's local interface (port 1130): only the connected callsign (#CONN).
    /// </summary>
    internal sealed class AuroraCallsign : IDisposable
    {
        private TcpClient? client;
        private StreamReader? reader;
        private StreamWriter? writer;

        /// <summary>Connected callsign, or null when Aurora is not running or not connected to IVAO.</summary>
        public async Task<string?> Get()
        {
            try
            {
                if (client == null || !client.Connected)
                {
                    Close();
                    client = new TcpClient();
                    using CancellationTokenSource connect = new(TimeSpan.FromSeconds(2));
                    await client.ConnectAsync("127.0.0.1", 1130, connect.Token);
                    NetworkStream stream = client.GetStream();
                    reader = new StreamReader(stream, Encoding.ASCII);
                    writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                }
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                await writer!.WriteLineAsync("#CONN".AsMemory(), cts.Token);
                while (true)
                {
                    string? line = await reader!.ReadLineAsync(cts.Token);
                    if (line == null)
                    {
                        Close();
                        return null;
                    }
                    if (line.StartsWith("#CONN", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#CTRL", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] fields = line.Split(';', StringSplitOptions.TrimEntries);
                        return fields.Length >= 2 && fields[1].Length > 0 ? fields[1].ToUpperInvariant() : null;
                    }
                    if (line.StartsWith('$') || line.StartsWith("@ERR", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }
            catch (Exception)
            {
                Close();
                return null;
            }
        }

        private void Close()
        {
            try
            {
                client?.Dispose();
            }
            catch (Exception)
            {
            }
            client = null;
            reader = null;
            writer = null;
        }

        public void Dispose() => Close();
    }
}
