using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;

namespace AuroraPAR
{
    internal class Aurora
    {
        /// <summary>
        /// Maximum time to wait for an answer from Aurora before treating the connection as lost.
        /// </summary>
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2);
        private static readonly Regex QnhHpa = new(@"\bQ(\d{4})\b");
        private static readonly Regex QnhInHg = new(@"\bA(\d{4})\b");

        private TcpClient? client;
        private NetworkStream? stream;
        private StreamReader? reader;
        private StreamWriter? writer;
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private volatile bool connected = false;
        private volatile bool closed = false;

        public bool Connected => connected;

        /// <summary>
        /// Single connection attempt. Returns true when connected.
        /// Called again (e.g. by the refresh timer) while not connected to reconnect automatically.
        /// </summary>
        public async Task<bool> TryConnect()
        {
            if (connected) return true;
            if (closed) return false;
            await semaphore.WaitAsync();
            try
            {
                Disconnect();
                consecutiveTimeouts = 0;
                client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, 1130);
                stream = client.GetStream();
                reader = new(stream);
                writer = new(stream);
                connected = true;
            }
            catch (Exception)
            {
                // Aurora not running or not accepting connections yet.
                Disconnect();
            }
            finally
            {
                semaphore.Release();
            }
            return connected;
        }

        /// <summary>
        /// Number of consecutive unanswered requests after which the connection is considered dead and reopened.
        /// </summary>
        private const int MaxConsecutiveTimeouts = 3;
        private int consecutiveTimeouts = 0;

        /// <summary>
        /// Sends one command and waits for its one-line answer.
        /// Aurora repeats the command at the start of every answer (e.g. "#TRPOS;ABC123;..."),
        /// so <paramref name="isAnswer"/> checks that the line read is really the answer to this command.
        /// Lines that are not (a late answer to an earlier request that timed out, or any other
        /// unexpected line) are discarded. Without this check a single late answer shifts every
        /// following answer by one: each aircraft then receives another aircraft's (or the METAR's)
        /// answer, is rejected, and traffic disappears until the program is restarted.
        /// Returns null on timeout or network error.
        /// </summary>
        private async Task<string?> Request(string command, Func<string, bool> isAnswer)
        {
            if (!connected) return null;
            await semaphore.WaitAsync();
            try
            {
                if (!connected || writer == null || reader == null) return null;
                using CancellationTokenSource cts = new(ResponseTimeout);
                await writer.WriteLineAsync(command.AsMemory(), cts.Token);
                await writer.FlushAsync(cts.Token);
                while (true)
                {
                    string? message = await reader.ReadLineAsync(cts.Token);
                    if (message == null)
                    {
                        // Aurora closed the connection.
                        Disconnect();
                        return null;
                    }
                    if (isAnswer(message))
                    {
                        consecutiveTimeouts = 0;
                        return message;
                    }
                    // Not the answer to this command: skip it and keep reading.
                }
            }
            catch (OperationCanceledException)
            {
                // No answer in time. A single missing answer is tolerated (any late answer will be
                // skipped by the check above); repeated ones mean the connection is dead.
                consecutiveTimeouts++;
                if (consecutiveTimeouts >= MaxConsecutiveTimeouts)
                {
                    Disconnect();
                }
                return null;
            }
            catch (Exception)
            {
                Disconnect();
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// True when <paramref name="message"/> starts with <paramref name="prefix"/> followed by ';' or the end of the line.
        /// </summary>
        private static bool StartsWithField(string message, string prefix)
        {
            return message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (message.Length == prefix.Length || message[prefix.Length] == ';');
        }

        public async Task<string[]> GetTrafficList()
        {
            string? message = await Request("#TR", m => StartsWithField(m, "#TR"));
            if (message != null)
            {
                return message.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1..];
            }
            return [];
        }

        public async Task<Aircraft?> GetTrafficPosition(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return null;
            string? message = await Request($"#TRPOS;{callsign}", m => StartsWithField(m, $"#TRPOS;{callsign}"));
            if (message != null)
            {
                string[] data = message.Split(';');
                // Fields 3 to 7 are used, so at least 8 fields are needed.
                if (data.Length >= 8
                    && TryParseNumber(data[3], out double track)
                    && TryParseNumber(data[4], out double altitude)
                    && TryParseNumber(data[5], out double speed)
                    && TryParseNumber(data[6], out double latitude)
                    && TryParseNumber(data[7], out double longitude))
                {
                    return new Aircraft()
                    {
                        Callsign = callsign,
                        Track = track,
                        Altitude = altitude,
                        Speed = speed,
                        Latitude = latitude,
                        Longitude = longitude,
                    };
                }
            }
            return null;
        }

        /// <summary>
        /// QNH in hPa from the METAR of the runway's airport, or 0 when not available.
        /// Altimeter settings in inches of mercury (e.g. A2992) are converted to hPa.
        /// </summary>
        /// <summary>
        /// Callsign this Aurora is connected to the network with (e.g. LIRF_APP), or null when not connected
        /// to IVAO or when Aurora does not answer. The documentation gives the answer as #CTRL;CALLSIGN, the
        /// command is usually echoed (#CONN;CALLSIGN): both are accepted; an error line means "not connected".
        /// </summary>
        public async Task<string?> GetConnectedCallsign()
        {
            string? message = await Request("#CONN", m => StartsWithField(m, "#CONN") || StartsWithField(m, "#CTRL")
                || m.StartsWith('$') || m.StartsWith("@ERR", StringComparison.OrdinalIgnoreCase));
            if (message == null || !message.StartsWith('#')) return null;
            string[] fields = message.Split(';', StringSplitOptions.TrimEntries);
            return fields.Length >= 2 && fields[1].Length > 0 ? fields[1].ToUpperInvariant() : null;
        }

        public async Task<int> GetQNH(Runway runway)
        {
            string? message = await Request($"#METAR;{runway.ICAO}", m => StartsWithField(m, "#METAR"));
            if (message != null)
            {
                Match q = QnhHpa.Match(message);
                if (q.Success)
                {
                    return int.Parse(q.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                Match a = QnhInHg.Match(message);
                if (a.Success)
                {
                    double inHg = int.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0;
                    return (int)Math.Round(inHg * 33.8639);
                }
            }
            return 0;
        }

        /// <summary>
        /// Parses a number written with a dot as decimal separator, independently of the Windows language settings.
        /// </summary>
        private static bool TryParseNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void Disconnect()
        {
            connected = false;
            try { writer?.Dispose(); } catch (Exception) { }
            try { reader?.Dispose(); } catch (Exception) { }
            try { stream?.Dispose(); } catch (Exception) { }
            try { client?.Dispose(); } catch (Exception) { }
            writer = null;
            reader = null;
            stream = null;
            client = null;
        }

        public void Close()
        {
            closed = true;
            Disconnect();
        }
    }
}
