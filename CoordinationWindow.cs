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
        private readonly ComboBox roleBox = new() { Width = 200, Height = 22, ItemsSource = new[] { "From callsign", "Radar (PAR / APP)", "Tower", "Monitor (instructor, read-only)" } };
        /// <summary>Texts engraved under the buttons.</summary>
        private readonly TextBlock[] engravings = new TextBlock[CoordinationSettings.Lights + 1];
        /// <summary>B612, the cockpit font, for the engraved texts (embedded in the program).</summary>
        private static readonly FontFamily EngravingFont = new(new Uri("pack://application:,,,/"), "./Fonts/#B612");
        private readonly DispatcherTimer flashTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private readonly DispatcherTimer linkTimer = new() { Interval = TimeSpan.FromSeconds(5) };
        private readonly Action save;
        private CoordinationState state = new();
        private string? callsign;
        private bool radarOnline;
        private bool towerOnline;
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
            link.PresenceChanged += (side, online) => Dispatcher.BeginInvoke(() =>
            {
                if (side == CoordinationRole.Radar) radarOnline = online; else towerOnline = online;
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
            StackPanel root = new() { Margin = new Thickness(12) };
            StackPanel statusRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) };
            statusRow.Children.Add(partnerDot);
            statusRow.Children.Add(status);
            status.Margin = new Thickness(0);
            root.Children.Add(statusRow);
            root.Children.Add(BuildPlate());

            Button optionsButton = new() { Content = "Options ▾", Width = 90, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            optionsButton.Click += (s, e) =>
            {
                options.Visibility = options.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                optionsButton.Content = options.Visibility == Visibility.Visible ? "Options ▴" : "Options ▾";
            };
            root.Children.Add(optionsButton);
            BuildOptions();
            root.Children.Add(options);
            ApplyColors();
            ApplyLabels();
            return root;
        }

        /// <summary>
        /// Instrument plate: the buttons with their engraved texts, the reset button set apart by a groove,
        /// and a screw in each corner.
        /// </summary>
        private UIElement BuildPlate()
        {
            Grid plate = new();
            Border background = new()
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x10)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3C, 0x37), Color.FromRgb(0x28, 0x2A, 0x26), 90)
            };
            plate.Children.Add(background);
            foreach ((HorizontalAlignment h, VerticalAlignment v) in new[]
            {
                (HorizontalAlignment.Left, VerticalAlignment.Top), (HorizontalAlignment.Right, VerticalAlignment.Top),
                (HorizontalAlignment.Left, VerticalAlignment.Bottom), (HorizontalAlignment.Right, VerticalAlignment.Bottom)
            })
            {
                plate.Children.Add(Screw(h, v));
            }
            StackPanel row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(30, 26, 30, 22) };
            for (int i = 0; i < lamps.Length; i++)
            {
                int index = i;
                bool isReset = i == CoordinationSettings.Lights;
                if (isReset)
                {
                    // Groove in the plate before the reset button.
                    StackPanel groove = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 22, 0) };
                    groove.Children.Add(new Border { Width = 2, Background = new SolidColorBrush(Color.FromRgb(0x15, 0x16, 0x14)) });
                    groove.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4C, 0x47)) });
                    row.Children.Add(groove);
                }
                CoordinationLamp lamp = new() { IsReset = isReset };
                lamp.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    if (isReset) ResetLights(); else Press(index);
                };
                lamps[i] = lamp;
                TextBlock engraving = new()
                {
                    FontFamily = EngravingFont,
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xDC)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 7, 0, 0),
                    Height = 15
                };
                engravings[i] = engraving;
                StackPanel cell = new() { Margin = new Thickness(i == 0 || isReset ? 0 : 16, 0, 0, 0), Width = 64 };
                cell.Children.Add(lamp);
                cell.Children.Add(engraving);
                row.Children.Add(cell);
            }
            plate.Children.Add(row);
            return plate;
        }

        private static UIElement Screw(HorizontalAlignment h, VerticalAlignment v)
        {
            Grid screw = new() { Width = 12, Height = 12, HorizontalAlignment = h, VerticalAlignment = v, Margin = new Thickness(9) };
            screw.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Fill = new RadialGradientBrush(Color.FromRgb(0xE0, 0xE0, 0xD8), Color.FromRgb(0x55, 0x56, 0x4F)) { GradientOrigin = new Point(0.35, 0.3) },
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x20)),
                StrokeThickness = 1
            });
            screw.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = 2.5, Y1 = 8, X2 = 9.5, Y2 = 4,
                Stroke = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x30)),
                StrokeThickness = 1.5
            });
            return screw;
        }

        private void ApplyLabels()
        {
            for (int i = 0; i < engravings.Length; i++)
            {
                engravings[i].Text = Options.Labels[i];
            }
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
            roleBox.SelectedIndex = Options.Role switch { CoordinationRole.Radar => 1, CoordinationRole.Tower => 2, CoordinationRole.Monitor => 3, _ => 0 };
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
                Text = "Both panels of an airport are linked automatically: _TWR is the tower, any other callsign the radar. As observer (_OBS), type the airport and choose the role. Monitor: an instructor sees the lights and who is online, without pressing anything.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 10)
            });

            options.Children.Add(Label("Buttons: colour and engraved text (optional, only on this panel; the last one is reset):"));
            Grid colours = new() { Margin = new Thickness(0, 6, 0, 0) };
            colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            colours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            for (int i = 0; i < lamps.Length; i++)
            {
                int index = i;
                colours.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                TextBlock number = new() { Text = $"{i + 1}", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
                FrameworkElement picker = ColorPicker.Create(Options.Colors[i], ColorPicker.StandardColors, hex =>
                {
                    Options.Colors[index] = hex;
                    save();
                    ApplyColors();
                });
                TextBox label = new()
                {
                    Text = Options.Labels[i],
                    Width = 100,
                    Height = 22,
                    MaxLength = CoordinationSettings.MaxLabelLength,
                    CharacterCasing = CharacterCasing.Upper,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ToolTip = "Text engraved under the button, e.g. 12 NM (empty: none)"
                };
                void ApplyLabel()
                {
                    string text = label.Text.Trim().ToUpperInvariant();
                    if (text == Options.Labels[index]) return;
                    Options.Labels[index] = text;
                    save();
                    ApplyLabels();
                }
                label.LostFocus += (s, e) => ApplyLabel();
                label.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter) ApplyLabel();
                };
                Grid.SetRow(number, i); Grid.SetColumn(number, 0);
                Grid.SetRow(picker, i); Grid.SetColumn(picker, 1);
                Grid.SetRow(label, i); Grid.SetColumn(label, 2);
                colours.Children.Add(number);
                colours.Children.Add(picker);
                colours.Children.Add(label);
            }
            options.Children.Add(colours);
            Button defaults = new() { Content = "Default colours and texts", Width = 170, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            defaults.Click += (s, e) =>
            {
                Options.Colors = (string[])CoordinationSettings.DefaultColors.Clone();
                Options.Labels = (string[])CoordinationSettings.DefaultLabels.Clone();
                save();
                BuildOptions();
                ApplyColors();
                ApplyLabels();
            };
            options.Children.Add(defaults);
            Button phone = new() { Content = "Open on phone / tablet...", Width = 170, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            phone.ToolTip = "QR code of the panel for phones and tablets, already set with this airport, role, colours and texts";
            phone.Click += (s, e) =>
            {
                (string? airport, CoordinationRole? role) = Effective();
                new PhoneLinkWindow(Options.PhoneLink(airport, role), airport, role) { Owner = this }.ShowDialog();
            };
            options.Children.Add(phone);
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
            Options.Role = roleBox.SelectedIndex switch { 1 => CoordinationRole.Radar, 2 => CoordinationRole.Tower, 3 => CoordinationRole.Monitor, _ => null };
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
            // Connected as a controller (e.g. LIBV_TWR): who you are online decides, in every program, also over an
            // airport or role chosen earlier in Options (only Monitor, the instructor's read-only panel, stays).
            if (airport != null && role != null && Options.Role != CoordinationRole.Monitor) return (airport, role);
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
            bool partnerOnline;
            string relay;
            if (role == CoordinationRole.Monitor)
            {
                partnerOnline = radarOnline && towerOnline;
                relay = link.Connected
                    ? $"radar {(radarOnline ? "online" : "offline")} · tower {(towerOnline ? "online" : "offline")}"
                    : "connecting...";
            }
            else
            {
                partnerOnline = role == CoordinationRole.Tower ? radarOnline : towerOnline;
                string other = role == CoordinationRole.Tower ? "radar" : "tower";
                relay = link.Connected ? (partnerOnline ? $"{other} online" : $"waiting for the {other}") : "connecting...";
            }
            string side = role switch { CoordinationRole.Tower => "TOWER", CoordinationRole.Monitor => "MONITOR", _ => "RADAR" };
            status.Text = $"{airport} · {side} · {relay}";
            partnerDot.Background = link.Connected && partnerOnline ? new SolidColorBrush(Color.FromRgb(0x50, 0xE0, 0x50))
                : link.Connected ? new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)) : Brushes.Red;
            bool monitor = role == CoordinationRole.Monitor;
            foreach (CoordinationLamp lamp in lamps)
            {
                lamp.Cursor = monitor ? Cursors.Arrow : Cursors.Hand;
                lamp.ToolTip = monitor ? "Monitor: read-only"
                    : lamp.IsReset ? "Reset: switches all the lights off (both panels)" : "Press to call (flashing) or to acknowledge (steady)";
            }
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
            if (role == CoordinationRole.Monitor) return;
            state.Press(light, role.Value);
            UpdateLamps();
            _ = link.Send(state.Copy());
        }

        private void ResetLights()
        {
            (string? airport, CoordinationRole? role) = Effective();
            if (airport == null || role is null or CoordinationRole.Monitor) return;
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
            if (alert && role != CoordinationRole.Monitor) Alert.Play();
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
            double inset = pressed ? 1 : 0;
            // Metal bezel: rounded square.
            LinearGradientBrush bezel = new(Color.FromRgb(0xC4, 0xC6, 0xC0), Color.FromRgb(0x3E, 0x40, 0x3B), 45);
            dc.DrawRoundedRectangle(bezel, new Pen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), 1), new Rect(0.5, 0.5, Size - 1, Size - 1), 10, 10);
            Rect lens = new(6 + inset, 6 + inset, Size - 12 - 2 * inset, Size - 12 - 2 * inset);
            // Lens: dim colour when off, bright when lit.
            Color edge = lit ? Scale(color, 0.8) : Scale(color, IsReset ? 0.6 : 0.3);
            Color middle = lit ? Lighten(color, 0.45) : Scale(color, IsReset ? 0.9 : 0.42);
            RadialGradientBrush glass = new(middle, edge) { GradientOrigin = new Point(0.4, 0.35), RadiusX = 0.75, RadiusY = 0.75 };
            dc.DrawRoundedRectangle(glass, new Pen(new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x08)), 1.5), lens, 7, 7);
            // Reflection on the upper part of the lens.
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), null,
                new Rect(lens.X + 4, lens.Y + 3, lens.Width - 8, 9), 4.5, 4.5);
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

    /// <summary>
    /// QR code and link of the panel for phones and tablets (a web page), already set with the airport, the role,
    /// the colours and the texts of this panel.
    /// </summary>
    internal sealed class PhoneLinkWindow : Window
    {
        public PhoneLinkWindow(string link, string? airport, CoordinationRole? role)
        {
            Title = "Coordination panel on a phone or tablet";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            StackPanel root = new() { Margin = new Thickness(16), MaxWidth = 420 };
            root.Children.Add(new TextBlock
            {
                Text = "Scan the code with the camera of the phone or tablet (or send it the link).",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            });
            Image image = new() { Width = 260, Height = 260, Margin = new Thickness(0, 12, 0, 12) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            try
            {
                using QRCoder.QRCodeGenerator generator = new();
                using QRCoder.QRCodeData data = generator.CreateQrCode(link, QRCoder.QRCodeGenerator.ECCLevel.M);
                byte[] png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
                System.Windows.Media.Imaging.BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(png);
                bitmap.EndInit();
                bitmap.Freeze();
                image.Source = bitmap;
            }
            catch (Exception)
            {
                image.Height = 0;
            }
            root.Children.Add(image);
            string who = airport == null || role == null
                ? "The airport or the role is not known yet: the phone will ask for them."
                : $"The phone joins {airport} as {role.Value.ToString().ToUpperInvariant()}, with the colours and texts of this panel. " +
                  "As an extra panel next to this one, keep the same role; for the other side, change it on the phone (⚙).";
            root.Children.Add(new TextBlock { Text = who, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new TextBlock
            {
                Text = "Tip: on the phone, add the page to the home screen to open it full screen like an app.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 6, 0, 10)
            });
            TextBox linkBox = new() { Text = link, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 60 };
            root.Children.Add(linkBox);
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Button copy = new() { Content = "Copy link", Width = 90, Height = 26 };
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(link);
                    copy.Content = "Copied";
                }
                catch (Exception)
                {
                }
            };
            Button close = new() { Content = "Close", Width = 90, Height = 26, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = true };
            buttons.Children.Add(copy);
            buttons.Children.Add(close);
            root.Children.Add(buttons);
            Content = root;
        }
    }
}
