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
        /// Sends one command and waits for the one-line answer.
        /// Returns null (and marks the connection as lost) on any network error or timeout.
        /// </summary>
        private async Task<string?> Request(string command)
        {
            if (!connected) return null;
            await semaphore.WaitAsync();
            try
            {
                if (!connected || writer == null || reader == null) return null;
                using CancellationTokenSource cts = new(ResponseTimeout);
                await writer.WriteLineAsync(command.AsMemory(), cts.Token);
                await writer.FlushAsync(cts.Token);
                string? message = await reader.ReadLineAsync(cts.Token);
                if (message == null)
                {
                    // Aurora closed the connection.
                    Disconnect();
                }
                return message;
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

        public async Task<string[]> GetTrafficList()
        {
            string? message = await Request("#TR");
            if (message != null)
            {
                return message.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1..];
            }
            return [];
        }

        public async Task<Aircraft?> GetTrafficPosition(string callsign)
        {
            if (string.IsNullOrEmpty(callsign)) return null;
            string? message = await Request($"#TRPOS;{callsign}");
            if (message != null && message.Contains("#TRPOS"))
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
        public async Task<int> GetQNH(Runway runway)
        {
            string? message = await Request($"#METAR;{runway.ICAO}");
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
