using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Coordination light panel between the radar (PAR) and the tower: five coloured lights and a reset button,
    /// the same on both sides. The first side that presses a light makes it flash on both panels, with an alert
    /// on the other side; when the other side presses it the light becomes steady. Reset switches all off.
    /// No text on the lights: their meaning is up to the controllers.
    /// </summary>
    internal sealed class CoordinationWindow : Window
    {
        private readonly CoordinationLink link = new();
        private readonly CoordinationLamp[] lamps = new CoordinationLamp[CoordinationSettings.Lights + 1];
        private readonly TextBlock status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
        private readonly Border partnerDot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel options = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
        private readonly TextBox airportBox = new() { Width = 70, Height = 22, VerticalContentAlignment = VerticalAlignment.Center, CharacterCasing = CharacterCasing.Upper, MaxLength = 4 };
        private readonly ComboBox roleBox = new() { Width = 130, Height = 22, ItemsSource = new[] { "From callsign", "Radar (PAR / APP)", "Tower" } };
        private readonly DispatcherTimer flashTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly DispatcherTimer linkTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly Action save;
        private CoordinationState state = new();
        private string? callsign;
        private bool partnerOnline;
        private bool flashPhase;
        private bool loading;

        private CoordinationSettings Options { get; }

        /// <summary>Panel colour, as the console of the analog scope.</summary>
        private static readonly Color PanelColor = Color.FromRgb(0x2E, 0x30, 0x2C);

        /// <param name="options">Options of the panel (saved by <paramref name="save"/>).</param>
        /// <param name="callsign">Callsign connected in Aurora, if known (see <see cref="SetCallsign"/>).</param>
        public CoordinationWindow(CoordinationSettings options, Action save, string? callsign)
        {
            Options = options;
            this.save = save;
            this.callsign = callsign;
            Title = "Aurora PAR - Coordination";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.CanMinimize;
            Background = new SolidColorBrush(PanelColor);
            Topmost = Options.Topmost;
            try
            {
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AuroraPAR.ico"));
            }
            catch (Exception)
            {
            }
            Content = BuildContent();
            link.StateReceived += s => Dispatcher.BeginInvoke(() => Received(s));
            link.PartnerChanged += online => Dispatcher.BeginInvoke(() =>
            {
                partnerOnline = online;
                UpdateStatus();
            });
            flashTimer.Tick += (s, e) =>
            {
                flashPhase = !flashPhase;
                UpdateLamps();
            };
            flashTimer.Start();
            linkTimer.Tick += async (s, e) =>
            {
                await link.KeepAlive();
                UpdateStatus();
            };
            linkTimer.Start();
            Closed += (s, e) =>
            {
                flashTimer.Stop();
                linkTimer.Stop();
                link.Dispose();
            };
            _ = Rejoin();
        }

        private UIElement BuildContent()
        {
            StackPanel root = new() { Margin = new Thickness(14) };
            StackPanel statusRow = new() { Orientation = Orientation.Horizontal };
            statusRow.Children.Add(partnerDot);
            statusRow.Children.Add(status);
            root.Children.Add(statusRow);

            StackPanel row = new() { Orientation = Orientation.Horizontal };
            for (int i = 0; i < lamps.Length; i++)
            {
                int index = i;
                bool isReset = i == CoordinationSettings.Lights;
                CoordinationLamp lamp = new() { Margin = new Thickness(i == 0 ? 0 : 10, 0, 0, 0), IsReset = isReset };
                lamp.ToolTip = isReset ? "Reset: switches all the lights off (both panels)" : "Press to call (flashing) or to acknowledge (steady)";
                lamp.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (isReset) ResetLights(); else Press(index);
                };
                lamps[i] = lamp;
                row.Children.Add(lamp);
            }
            root.Children.Add(row);

            Button optionsButton = new() { Content = "Options ▾", Width = 90, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            optionsButton.Click += (s, e) =>
            {
                options.Visibility = options.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                optionsButton.Content = options.Visibility == Visibility.Visible ? "Options ▴" : "Options ▾";
            };
            root.Children.Add(optionsButton);
            BuildOptions();
            root.Children.Add(options);
            ApplyColors();
            return root;
        }

        private TextBlock Label(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        private void BuildOptions()
        {
            loading = true;
            options.Children.Clear();
            StackPanel pairing = new() { Orientation = Orientation.Horizontal };
            pairing.Children.Add(Label("Airport:"));
            airportBox.Text = Options.Airport ?? "";
            airportBox.ToolTip = "Empty: from the callsign you are connected with (e.g. LIRF_APP). Needed when connected as observer.";
            pairing.Children.Add(airportBox);
            pairing.Children.Add(new Border { Width = 16 });
            pairing.Children.Add(Label("Role:"));
            roleBox.SelectedIndex = Options.Role switch { CoordinationRole.Radar => 1, CoordinationRole.Tower => 2, _ => 0 };
            pairing.Children.Add(roleBox);
            options.Children.Add(pairing);
            airportBox.LostFocus += (s, e) => PairingChanged();
            airportBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) PairingChanged();
            };
            roleBox.SelectionChanged += (s, e) => PairingChanged();

            options.Children.Add(new TextBlock
            {
                Text = "Both panels of an airport are linked automatically: _TWR is the tower, any other callsign the radar. As observer (_OBS), type the airport and choose the role.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 10)
            });

            options.Children.Add(Label("Colours of the lights (the last one is the reset button):"));
            Grid colours = new() { Margin = new Thickness(0, 6, 0, 0) };
            colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            for (int i = 0; i < lamps.Length; i++)
            {
                int index = i;
                if (i % 2 == 0) colours.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                FrameworkElement picker = ColorPicker.Create(Options.Colors[i], ColorPicker.StandardColors, hex =>
                {
                    Options.Colors[index] = hex;
                    save();
                    ApplyColors();
                });
                StackPanel cell = new() { Orientation = Orientation.Horizontal };
                cell.Children.Add(new TextBlock { Text = $"{i + 1}", Width = 14, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                cell.Children.Add(picker);
                Grid.SetRow(cell, i / 2);
                Grid.SetColumn(cell, i % 2);
                colours.Children.Add(cell);
            }
            options.Children.Add(colours);
            Button defaults = new() { Content = "Default colours", Width = 120, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            defaults.Click += (s, e) =>
            {
                Options.Colors = (string[])CoordinationSettings.DefaultColors.Clone();
                save();
                BuildOptions();
                ApplyColors();
            };
            options.Children.Add(defaults);
            CheckBox topmost = new()
            {
                Content = "Always on top",
                IsChecked = Options.Topmost,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD0)),
                Margin = new Thickness(0, 10, 0, 0)
            };
            topmost.Checked += (s, e) => SetTopmost(true);
            topmost.Unchecked += (s, e) => SetTopmost(false);
            options.Children.Add(topmost);
            loading = false;
        }

        private void SetTopmost(bool value)
        {
            Options.Topmost = value;
            Topmost = value;
            save();
        }

        private void PairingChanged()
        {
            if (loading) return;
            string airport = airportBox.Text.Trim().ToUpperInvariant();
            Options.Airport = airport.Length == 0 ? null : airport;
            Options.Role = roleBox.SelectedIndex switch { 1 => CoordinationRole.Radar, 2 => CoordinationRole.Tower, _ => null };
            save();
            _ = Rejoin();
        }

        /// <summary>Callsign the controller is connected with (from Aurora), checked periodically.</summary>
        public void SetCallsign(string? value)
        {
            if (value == callsign) return;
            callsign = value;
            _ = Rejoin();
        }

        private (string? Airport, CoordinationRole? Role) Effective()
        {
            (string? airport, CoordinationRole? role) = CoordinationSettings.FromCallsign(callsign);
            return (Options.Airport ?? airport, Options.Role ?? role);
        }

        private async Task Rejoin()
        {
            (string? airport, CoordinationRole? role) = Effective();
            state = new CoordinationState();
            UpdateLamps();
            if (airport == null || role == null)
            {
                options.Visibility = Visibility.Visible;
                await link.Join(null, CoordinationRole.Radar);
            }
            else
            {
                await link.Join(airport, role.Value);
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            (string? airport, CoordinationRole? role) = Effective();
            if (airport == null || role == null)
            {
                status.Text = callsign == null
                    ? "Not connected to IVAO: type the airport and choose the role in Options."
                    : $"{callsign}: type the airport and choose the role in Options.";
                partnerDot.Background = Brushes.Gray;
                return;
            }
            string side = role == CoordinationRole.Tower ? "TOWER" : "RADAR";
            string other = role == CoordinationRole.Tower ? "radar" : "tower";
            string relay = link.Connected ? (partnerOnline ? $"{other} online" : $"waiting for the {other}") : "connecting...";
            status.Text = $"{airport} · {side} · {relay}";
            partnerDot.Background = link.Connected && partnerOnline ? new SolidColorBrush(Color.FromRgb(0x50, 0xE0, 0x50))
                : link.Connected ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)) : Brushes.Red;
        }

        private void Press(int light)
        {
            (string? airport, CoordinationRole? role) = Effective();
            if (airport == null || role == null)
            {
                options.Visibility = Visibility.Visible;
                SystemSounds.Beep.Play();
                return;
            }
            state.Press(light, role.Value);
            UpdateLamps();
            _ = link.Send(state.Copy());
        }

        private void ResetLights()
        {
            if (Effective().Airport == null) return;
            state.Reset();
            UpdateLamps();
            _ = link.Send(state.Copy());
        }

        private void Received(CoordinationState received)
        {
            CoordinationRole? role = Effective().Role;
            bool alert = false;
            for (int i = 0; i < CoordinationSettings.Lights; i++)
            {
                // A new call from the other side: alert.
                if (received.Lights[i] == LightState.Flashing && received.CalledBy[i] != role
                    && !(state.Lights[i] == LightState.Flashing && state.CalledBy[i] == received.CalledBy[i]))
                {
                    alert = true;
                }
            }
            state = received;
            UpdateLamps();
            if (alert) Alert.Play();
        }

        private void ApplyColors()
        {
            for (int i = 0; i < lamps.Length; i++)
            {
                lamps[i].Color = ColorText.Parse(Options.Colors[i], System.Windows.Media.Colors.White);
            }
            UpdateLamps();
        }

        private void UpdateLamps()
        {
            for (int i = 0; i < CoordinationSettings.Lights; i++)
            {
                lamps[i].Lit = state.Lights[i] switch
                {
                    LightState.Steady => true,
                    LightState.Flashing => flashPhase,
                    _ => false
                };
            }
        }

    }

    /// <summary>Round lit push button of the coordination panel.</summary>
    internal sealed class CoordinationLamp : FrameworkElement
    {
        private const double Size = 58;
        private bool lit;
        private bool pressed;
        private Color color = System.Windows.Media.Colors.White;
        private readonly DropShadowEffect glow = new() { ShadowDepth = 0, BlurRadius = 18, Opacity = 0.9 };

        public bool IsReset { get; set; }

        public CoordinationLamp()
        {
            Width = Size;
            Height = Size;
            Cursor = Cursors.Hand;
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                glow.Color = value;
                InvalidateVisual();
            }
        }

        public bool Lit
        {
            get => lit;
            set
            {
                if (lit == value) return;
                lit = value;
                Effect = lit ? glow : null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            pressed = true;
            InvalidateVisual();
            base.OnMouseLeftButtonDown(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            pressed = false;
            InvalidateVisual();
            base.OnMouseLeftButtonUp(e);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            if (pressed)
            {
                pressed = false;
                InvalidateVisual();
            }
            base.OnMouseLeave(e);
        }

        private static Color Scale(Color c, double k) =>
            Color.FromRgb((byte)Math.Clamp(c.R * k, 0, 255), (byte)Math.Clamp(c.G * k, 0, 255), (byte)Math.Clamp(c.B * k, 0, 255));

        private static Color Lighten(Color c, double t) =>
            Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

        protected override void OnRender(DrawingContext dc)
        {
            Point center = new(Size / 2, Size / 2);
            double r = Size / 2;
            // Metal bezel.
            LinearGradientBrush bezel = new(Color.FromRgb(0xC4, 0xC6, 0xC0), Color.FromRgb(0x3E, 0x40, 0x3B), 45);
            dc.DrawEllipse(bezel, new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), 1), center, r - 1, r - 1);
            double lens = r - 7 - (pressed ? 1 : 0);
            // Lens: dim colour when off, bright when lit.
            Color edge = lit ? Scale(color, 0.75) : Scale(color, IsReset ? 0.6 : 0.28);
            Color middle = lit ? Lighten(color, 0.55) : Scale(color, IsReset ? 0.9 : 0.42);
            RadialGradientBrush glass = new(middle, edge) { GradientOrigin = new Point(0.4, 0.35) };
            dc.DrawEllipse(glass, new Pen(new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08)), 1.5), center, lens, lens);
            // Reflection.
            RadialGradientBrush shine = new(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF));
            dc.DrawEllipse(shine, null, new Point(center.X - lens * 0.3, center.Y - lens * 0.35), lens * 0.45, lens * 0.3);
        }
    }

    /// <summary>Alert sound of a new call: two short tones, generated once.</summary>
    internal static class Alert
    {
        private static readonly byte[] Wave = Create();

        public static void Play()
        {
            try
            {
                using SoundPlayer player = new(new MemoryStream(Wave));
                player.Play();
            }
            catch (Exception)
            {
                SystemSounds.Exclamation.Play();
            }
        }

        private static byte[] Create()
        {
            const int rate = 22050;
            List<short> samples = [];
            void Tone(double frequency, double seconds)
            {
                int n = (int)(rate * seconds);
                for (int i = 0; i < n; i++)
                {
                    double envelope = Math.Min(1, Math.Min(i, n - i) / (rate * 0.01));
                    samples.Add((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * 9000 * envelope));
                }
            }
            void Silence(double seconds) => samples.AddRange(new short[(int)(rate * seconds)]);
            Tone(880, 0.14);
            Silence(0.06);
            Tone(1175, 0.18);
            using MemoryStream stream = new();
            using BinaryWriter w = new(stream);
            int data = samples.Count * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + data);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8.ToArray());
            w.Write(data);
            foreach (short s in samples) w.Write(s);
            w.Flush();
            return stream.ToArray();
        }
    }
}
