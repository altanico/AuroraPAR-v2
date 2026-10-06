using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace AuroraPAR
{
    /// <summary>Side of the coordination: the radar (approach / PAR) or the tower.</summary>
    internal enum CoordinationRole
    {
        Radar,
        Tower
    }

    internal enum LightState
    {
        Off,
        /// <summary>Called by one side, waiting for the other side.</summary>
        Flashing,
        /// <summary>Acknowledged by the other side.</summary>
        Steady
    }

    /// <summary>Options of the coordination panel (saved with the settings, not in the profiles).</summary>
    internal class CoordinationSettings
    {
        public const int Lights = 5;
        public static readonly string[] DefaultColors = ["#FFFFFF", "#2E6BFF", "#FFD400", "#FF2020", "#20C840", "#101010"];

        /// <summary>Airport typed by hand (needed when connected as observer); null: from the callsign.</summary>
        public string? Airport { get; set; }
        /// <summary>Role chosen by hand; null: from the callsign (_TWR = tower, anything else = radar).</summary>
        public CoordinationRole? Role { get; set; }
        /// <summary>Colours of the five lights and of the reset button.</summary>
        public string[] Colors { get; set; } = (string[])DefaultColors.Clone();
        /// <summary>Panel always on top of the other windows.</summary>
        public bool Topmost { get; set; } = true;

        public void Normalize()
        {
            if (Colors == null || Colors.Length != DefaultColors.Length) Colors = (string[])DefaultColors.Clone();
            for (int i = 0; i < Colors.Length; i++)
            {
                if (!ColorText.TryParse(Colors[i], out _)) Colors[i] = DefaultColors[i];
            }
            if (Airport != null)
            {
                Airport = Airport.Trim().ToUpperInvariant();
                if (Airport.Length == 0) Airport = null;
            }
            if (Role is CoordinationRole role && !Enum.IsDefined(role)) Role = null;
        }

        /// <summary>
        /// Airport and role from the connected callsign: "LIRF_APP" = LIRF, radar; "LIRF_TWR" = LIRF, tower.
        /// Observers ("..._OBS") and callsigns without an airport give nothing.
        /// </summary>
        public static (string? Airport, CoordinationRole? Role) FromCallsign(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign)) return (null, null);
            string[] parts = callsign.Trim().ToUpperInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return (null, null);
            string suffix = parts[^1];
            if (suffix == "OBS") return (null, null);
            string? airport = parts[0].Length == 4 && parts[0].All(char.IsLetter) ? parts[0] : null;
            return (airport, suffix == "TWR" ? CoordinationRole.Tower : CoordinationRole.Radar);
        }
    }

    /// <summary>
    /// State of the lights shared by the two panels.
    /// Same rule for every light: the first side that presses it makes it flash (and the other side hears an
    /// alert); when the other side presses it, it becomes steady (acknowledged). The meaning of each light is
    /// up to the controllers. Reset switches everything off.
    /// </summary>
    internal class CoordinationState
    {
        public LightState[] Lights { get; set; } = new LightState[CoordinationSettings.Lights];
        /// <summary>Side that made each light flash.</summary>
        public CoordinationRole?[] CalledBy { get; set; } = new CoordinationRole?[CoordinationSettings.Lights];
        /// <summary>Who sent this state (to ignore our own messages) and when.</summary>
        public string Sender { get; set; } = "";
        public long Time { get; set; }

        public CoordinationState Copy()
        {
            return new CoordinationState
            {
                Lights = (LightState[])Lights.Clone(),
                CalledBy = (CoordinationRole?[])CalledBy.Clone(),
                Sender = Sender,
                Time = Time
            };
        }

        /// <summary>A side presses a light.</summary>
        public void Press(int light, CoordinationRole role)
        {
            switch (Lights[light])
            {
                case LightState.Off:
                    Lights[light] = LightState.Flashing;
                    CalledBy[light] = role;
                    break;
                case LightState.Flashing when CalledBy[light] != role:
                    Lights[light] = LightState.Steady;
                    break;
                case LightState.Flashing:
                    // Pressed again by the side that called: call cancelled.
                    Lights[light] = LightState.Off;
                    CalledBy[light] = null;
                    break;
            }
        }

        public void Reset()
        {
            Lights = new LightState[CoordinationSettings.Lights];
            CalledBy = new CoordinationRole?[CoordinationSettings.Lights];
        }

        public bool IsValid => Lights?.Length == CoordinationSettings.Lights && CalledBy?.Length == CoordinationSettings.Lights;
    }

    /// <summary>
    /// Link between the radar and tower panels of the same airport through a public MQTT relay (encrypted
    /// connection, nothing to install, no port to open). The state of the lights is a retained message, so a
    /// panel started later shows the current lights at once. Each side also publishes whether it is online.
    /// </summary>
    internal sealed class CoordinationLink : IDisposable
    {
        private const string Broker = "broker.emqx.io";
        private const int Port = 8883;
        private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

        private readonly string clientId = "aurorapar-" + Guid.NewGuid().ToString("N")[..12];
        private readonly MqttFactory factory = new();
        private IMqttClient? client;
        private string? channel;
        private CoordinationRole role;
        private readonly CancellationTokenSource stop = new();
        private readonly SemaphoreSlim gate = new(1, 1);

        /// <summary>New state received from the other panel (raised on a background thread).</summary>
        public event Action<CoordinationState>? StateReceived;
        /// <summary>The other side went online or offline (raised on a background thread).</summary>
        public event Action<bool>? PartnerChanged;

        public bool Connected => client?.IsConnected == true;
        public string? Airport { get; private set; }

        /// <summary>Channel of an airport: a name that is not trivial to guess, the same for both sides.</summary>
        private static string ChannelOf(string airport)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("AuroraPAR coordination panel " + airport));
            return $"aurorapar/coord/v1/{airport}-{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
        }

        private static CoordinationRole Other(CoordinationRole r) => r == CoordinationRole.Radar ? CoordinationRole.Tower : CoordinationRole.Radar;

        /// <summary>
        /// Joins the channel of an airport with a role (or leaves when <paramref name="airport"/> is null).
        /// Reconnects by itself while joined.
        /// </summary>
        public async Task Join(string? airport, CoordinationRole newRole)
        {
            await gate.WaitAsync();
            try
            {
                if (airport == Airport && newRole == role && (airport == null || Connected)) return;
                await DisconnectInternal();
                Airport = airport;
                role = newRole;
                channel = airport == null ? null : ChannelOf(airport);
                PartnerChanged?.Invoke(false);
                if (channel != null) await ConnectInternal();
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Called periodically: reconnects after a network problem.</summary>
        public async Task KeepAlive()
        {
            if (channel == null || Connected || stop.IsCancellationRequested) return;
            await gate.WaitAsync();
            try
            {
                if (channel != null && !Connected) await ConnectInternal();
            }
            finally
            {
                gate.Release();
            }
        }

        private string PresenceTopic(CoordinationRole r) => $"{channel}/presence/{r}";
        private string StateTopic => $"{channel}/state";

        private async Task ConnectInternal()
        {
            if (channel == null) return;
            try
            {
                client?.Dispose();
                client = factory.CreateMqttClient();
                client.ApplicationMessageReceivedAsync += OnMessage;
                MqttClientOptions options = new MqttClientOptionsBuilder()
                    .WithTcpServer(Broker, Port)
                    .WithTlsOptions(o => o.UseTls())
                    .WithClientId(clientId)
                    .WithCleanSession()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(20))
                    .WithWillTopic(PresenceTopic(role))
                    .WithWillPayload("offline")
                    .WithWillRetain()
                    .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await client.ConnectAsync(options, timeout.Token);
                MqttClientSubscribeOptions subscribe = factory.CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(StateTopic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .WithTopicFilter(f => f.WithTopic(PresenceTopic(Other(role))).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build();
                await client.SubscribeAsync(subscribe, timeout.Token);
                await Publish(PresenceTopic(role), "online");
            }
            catch (Exception)
            {
                // No network or relay not reachable: KeepAlive tries again.
            }
        }

        private async Task DisconnectInternal()
        {
            if (client == null) return;
            try
            {
                if (client.IsConnected)
                {
                    await Publish(PresenceTopic(role), "offline");
                    await client.DisconnectAsync();
                }
            }
            catch (Exception)
            {
            }
            client.ApplicationMessageReceivedAsync -= OnMessage;
            client.Dispose();
            client = null;
        }

        private async Task Publish(string topic, string payload)
        {
            if (client?.IsConnected != true) return;
            MqttApplicationMessage message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag()
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await client.PublishAsync(message, stop.Token);
        }

        /// <summary>Sends the new state of the lights to the other panel.</summary>
        public async Task Send(CoordinationState state)
        {
            if (channel == null) return;
            state.Sender = clientId;
            state.Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            try
            {
                await Publish(StateTopic, JsonSerializer.Serialize(state, Json));
            }
            catch (Exception)
            {
            }
        }

        private Task OnMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
                if (topic == StateTopic)
                {
                    CoordinationState? state = payload.Length == 0 ? new CoordinationState() : JsonSerializer.Deserialize<CoordinationState>(payload, Json);
                    if (state != null && state.IsValid && state.Sender != clientId)
                    {
                        StateReceived?.Invoke(state);
                    }
                }
                else if (topic == PresenceTopic(Other(role)))
                {
                    PartnerChanged?.Invoke(payload == "online");
                }
            }
            catch (Exception)
            {
                // A malformed message is ignored.
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            stop.Cancel();
            try
            {
                // On a pool thread: waiting on the UI thread for a task that resumes there would hang.
                Task.Run(DisconnectInternal).Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }
        }
    }
}
