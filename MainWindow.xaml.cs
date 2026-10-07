using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuroraPAR
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// How often the METAR (QNH) is requested again. It changes rarely, so there is no need to ask every refresh.
        /// </summary>
        private static readonly TimeSpan QnhRefreshInterval = TimeSpan.FromSeconds(60);
        private int qnh = 0;
        private DateTime lastQnhUpdate = DateTime.MinValue;
        private string lastQnhIcao = "";
        /// <summary>
        /// Set when the QNH must be requested again as soon as possible (e.g. right after (re)connecting).
        /// </summary>
        private volatile bool qnhRefreshNeeded = true;
        /// <summary>
        /// Delay between two connection attempts while Aurora is not reachable.
        /// </summary>
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
        /// <summary>
        /// Aircraft received at the last refresh, so the screen can be redrawn immediately
        /// (range or runway change, window resize) without waiting for Aurora.
        /// </summary>
        private volatile List<Aircraft> lastAircrafts = [];
        private readonly System.Timers.Timer timer;
        private readonly ProfileView profileView;
        private readonly HorizontalView horizontalView;
        /// <summary>Clock of the antenna scan effect.</summary>
        private readonly System.Diagnostics.Stopwatch sweepClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Knobs of the analog console.</summary>
        private const string KnobHelp = "Click the right half: one step clockwise; left half: counter-clockwise (the pointer shows the direction). Mouse wheel. Centre: drag up/down";
        private readonly Knob rangeKnob = new() { Title = "RANGE NM", ToolTip = $"Range. {KnobHelp}." };
        private readonly Knob elevationKnob = new() { Title = "EL TILT", ToolTip = $"Antenna elevation tilt. {KnobHelp}; double click on the centre: neutral." };
        private readonly Knob azimuthKnob = new() { Title = "AZ TILT", ToolTip = $"Antenna azimuth tilt. {KnobHelp}; double click on the centre: neutral." };
        private readonly Knob brightnessKnob = new() { Title = "BRT", Positions = Profile.MaxBrightness / Profile.BrightnessStep, ToolTip = $"Brightness of the scope, 10% to 150% (above 100% for dim monitors). {KnobHelp}; double click on the centre: 100%." };
        private readonly Knob dhKnob = new() { Title = "DH", ToolTip = $"Decision height, 10 ft per step. {KnobHelp}; double click on the centre: runway value." };
        /// <summary>Analog: published approaches of the runway (one detent per glide path angle in runways.par).</summary>
        private readonly Knob glidePathKnob = new() { Title = "GP DEG", ToolTip = $"Glide path: the approaches of this runway in runways.par (one line per angle). {KnobHelp}; double click on the centre: first approach of the file." };
        /// <summary>Analog mode: readouts and lamps next to the scope.</summary>
        private readonly ConsolePanel consolePanel = new();
        private static readonly Brush PanelTextBrush = CreateFrozenBrush(Color.FromRgb(0xD8, 0xD8, 0xD0));
        private readonly Brush glassBrush = ScopeBezel.CreateGlassBrush();
        private readonly Aurora aurora;
        private readonly Distance[] distances = Ranges.Values.Select(v => (Distance)v).ToArray();
        /// <summary>
        /// Runway file, next to the program (not in the current directory, which depends on how the program is started).
        /// </summary>
        private readonly string dataPath = Path.Combine(AppContext.BaseDirectory, "runways.par");
        /// <summary>All the lines of the runway file (one per approach).</summary>
        private Runway[] runways = [];
        /// <summary>
        /// Runways of the drop-down: one per airport and designator. Lines with the same airport and designator and
        /// a different glide path are approaches of the same runway, chosen with the GP selector.
        /// </summary>
        private Runway[] runwayList = [];
        /// <summary>Published approaches of the current runway, by glide path angle.</summary>
        private Runway[] approaches = [];
        /// <summary>Published approach in use (<see cref="runway"/> is it, or its unpublished copy with another angle).</summary>
        private Runway? selectedApproach;
        /// <summary>True while the GP selector is filled by the program.</summary>
        private bool updatingGlidePath;
        /// <summary>
        /// True while the runway list is reloaded after editing, so the current range is kept.
        /// </summary>
        private bool reloadingRunways;
        private volatile bool Open = true;
        private readonly AppSettings settings;
        /// <summary>
        /// True while the last session's runway is being restored at start.
        /// </summary>
        private bool restoringSession;
        /// <summary>
        /// Information area (runway, QNH, connection status), created once and updated at every refresh.
        /// </summary>
        private readonly StackPanel infoPanel = new();
        private readonly TextBlock infoText = new() { FontSize = 14, Foreground = Brushes.White };
        /// <summary>Glide path, with the "unpublished approach" warning.</summary>
        private readonly TextBlock glidePathText = new() { FontSize = 14, Foreground = Brushes.White, Background = Brushes.Transparent };
        /// <summary>Missed approach point distance, on its own line for its tooltip.</summary>
        private readonly TextBlock missedApproachText = new()
        {
            FontSize = 14,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            ToolTip = "MAPt DIST - missed approach point: distance in NM from the touchdown point where the glide path reaches the decision height in use (DH / OCH).\nCompare it with the approach chart (MAPt / RPI DIST) to check runways.par; it is the distance for \"approach terminating at ...\".\nThe DH does not change with the glide path angle, the MAPt does: with an unpublished angle this is the new MAPt."
        };
        /// <summary>Final course, on its own line for its tooltip.</summary>
        private readonly TextBlock courseText = new()
        {
            FontSize = 14,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            ToolTip = "Final course (magnetic). Calculated, not the published value: runway true heading from runways.par corrected with the magnetic variation (of the runway, or the default in Settings), rounded to the degree. Check it against the approach chart."
        };
        private readonly TextBlock infoText2 = new() { FontSize = 14, Foreground = Brushes.White };
        /// <summary>
        /// Antenna tilt reminder, shown only when the antenna is not in neutral position.
        /// </summary>
        private readonly TextBlock tiltText = new() { FontSize = 14, Foreground = Brushes.Orange, FontWeight = FontWeights.Bold };
        /// <summary>
        /// Approach and scan limits and antenna tilt, shared by both views.
        /// </summary>
        private readonly Radar radar = new();
        private readonly ViewOptions viewOptions = new();
        private readonly TextBlock statusText = new() { FontSize = 14 };
        private readonly TextBlock dataText = new() { FontSize = 14 };
        /// <summary>
        /// Above this interval between position updates, Aurora's traffic refresh rate is too slow for a PAR.
        /// </summary>
        private const double MaxGoodDataInterval = 1.5;
        private readonly RefreshRateMonitor refreshMonitor = new();
        private readonly VerticalSpeedEstimator verticalSpeed = new();
        /// <summary>
        /// Measured interval between position updates from Aurora (s), NaN when unknown. Written by the refresh timer.
        /// </summary>
        private double dataInterval = double.NaN;
        private Runway runway = new()
        {
            ICAO = "EDDF",
            Designator = "TEST",
            Elevation = 364,
            Heading = 70,
            Latitude = 50.02766757167606,
            Longitude = 8.534360261721835,
            LengthM = 1000,
            WidthM = 60,
            Distance = 10
        };
        public MainWindow()
        {
            InitializeComponent();
            settings = SettingsStore.Load();
            RestoreWindowPlacement();
            this.Loaded += MainWindow_Loaded;
            DistanceComboBox.ItemsSource = distances;
            DistanceComboBox.SelectedIndex = IndexOfDistance(runway.Distance);//10 nm
            DistanceComboBox.SelectionChanged += DistanceComboBox_SelectionChanged;
            profileView = new(Vertical, runway, radar, viewOptions);
            horizontalView = new(Horizontal, runway, radar, viewOptions);
            infoPanel.Children.Add(infoText);
            // One entry per runway: "LIPC 11" for the lines "LIPC 11 2.8" and "LIPC 11 2.5".
            RunwayComboBox.DisplayMemberPath = nameof(Runway.DisplayName);
            IcaoFilterBox.TextChanged += (s, e) =>
            {
                if (!settingFilter) ApplyRunwayFilter(userTyped: true);
            };
            IcaoFilterBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && RunwayComboBox.Items.Count > 0)
                {
                    RunwayComboBox.Focus();
                    RunwayComboBox.IsDropDownOpen = true;
                    e.Handled = true;
                }
            };
            infoPanel.Children.Add(ToolTips.KeepOpen(courseText));
            infoPanel.Children.Add(glidePathText);
            infoPanel.Children.Add(ToolTips.KeepOpen(missedApproachText));
            foreach (Knob knob in new[] { rangeKnob, elevationKnob, azimuthKnob, dhKnob, glidePathKnob, brightnessKnob }) ToolTips.KeepOpen(knob);
            BrightnessDownButton.Click += (s, e) => ChangeBrightness(-1);
            BrightnessUpButton.Click += (s, e) => ChangeBrightness(1);
            BrightnessPanel.MouseWheel += (s, e) =>
            {
                // Handled here, so the wheel over the control does not also change the range.
                ChangeBrightness(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            infoPanel.Children.Add(infoText2);
            infoPanel.Children.Add(tiltText);
            infoPanel.Children.Add(statusText);
            infoPanel.Children.Add(dataText);
            Panel.SetZIndex(infoPanel, 20);
            Vertical.Children.Add(infoPanel);
            aurora = new();
            this.Closing += MainWindow_Closing;
            // AutoReset = false: the next refresh is started only when the previous one has finished,
            // so refreshes never overlap even when Aurora answers slowly.
            timer = new()
            {
                Interval = 100,
                AutoReset = false
            };
            timer.Elapsed += Timer_Elapsed;
            Vertical.SizeChanged += (s, e) => InvalidateViews();
            Horizontal.SizeChanged += (s, e) => InvalidateViews();
            SettingsButton.Click += SettingsButton_Click;
            TiltUpButton.Click += (s, e) => TiltAntenna(1, 0);
            TiltDownButton.Click += (s, e) => TiltAntenna(-1, 0);
            TiltLeftButton.Click += (s, e) => TiltAntenna(0, -AzimuthSign);
            TiltRightButton.Click += (s, e) => TiltAntenna(0, AzimuthSign);
            TiltNeutralButton.Click += (s, e) => NeutralAntenna();
            LabelsButton.Click += (s, e) => ToggleLabels();
            ModeButton.Click += (s, e) => ToggleDisplayMode();
            CoordinationButton.Click += (s, e) => OpenCoordination();
            BuildKnobs();
            ScopeHost.SizeChanged += (s, e) => LayoutDisplay();
            // Console panel at most about a quarter of the display (it is scaled to fit, see the XAML).
            DisplayArea.SizeChanged += (s, e) => ConsoleHost.MaxWidth = Math.Max(90, Math.Min(DisplayArea.ActualWidth * 0.26, 300));
            ConsoleViewbox.Child = consolePanel;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            RunwaysButton.Click += RunwaysButton_Click;
            DhUpButton.Click += (s, e) => SetDecisionHeight(runway.MDH + DecisionHeightStep);
            DhDownButton.Click += (s, e) => SetDecisionHeight(runway.MDH - DecisionHeightStep);
            DhTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) ApplyDecisionHeightText();
            };
            DhTextBox.LostFocus += (s, e) => ApplyDecisionHeightText();
            DhTextBox.Text = FormatHeight(runway.MDH);
            GlidePathComboBox.SelectionChanged += (s, e) =>
            {
                if (updatingGlidePath || GlidePathComboBox.SelectedIndex < 0 || GlidePathComboBox.SelectedIndex >= approaches.Length) return;
                // Back from an unpublished angle the DH in use is kept (it does not depend on the angle).
                SelectApproach(approaches[GlidePathComboBox.SelectedIndex], keepDecisionHeight: runway.IsUnpublished);
            };
            GlidePathComboBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ApplyGlidePathText();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    UpdateGlidePathSelector();
                    Keyboard.ClearFocus();
                    e.Handled = true;
                }
            };
            GlidePathComboBox.LostKeyboardFocus += (s, e) =>
            {
                if (!GlidePathComboBox.IsKeyboardFocusWithin) ApplyGlidePathText();
            };
            ApplyProfile();
            // Antenna scan effect: redrawn at every frame of the screen (graphic only, independent of the traffic refresh).
            CompositionTarget.Rendering += (s, e) => RenderSweep();
        }

        /// <summary>Coordination light panel (one window, opened on demand).</summary>
        private CoordinationWindow? coordinationWindow;
        /// <summary>Callsign this Aurora is connected with, checked every few seconds.</summary>
        private string? connectedCallsign;
        private DateTime lastCallsignCheck = DateTime.MinValue;
        private static readonly TimeSpan CallsignCheckInterval = TimeSpan.FromSeconds(10);

        private void OpenCoordination()
        {
            if (coordinationWindow == null)
            {
                coordinationWindow = new CoordinationWindow(settings.Coordination, () => SettingsStore.Save(settings), connectedCallsign);
                coordinationWindow.Closed += (s, e) =>
                {
                    coordinationWindow = null;
                    CoordinationButton.Tag = "Unlit";
                };
                coordinationWindow.Show();
                CoordinationButton.Tag = "Lit";
            }
            else
            {
                if (coordinationWindow.WindowState == WindowState.Minimized) coordinationWindow.WindowState = WindowState.Normal;
                coordinationWindow.Activate();
            }
        }

        private void RenderSweep()
        {
            Profile profile = settings.Active;
            double t = sweepClock.Elapsed.TotalSeconds;
            // The analog scope always has its beam.
            bool enabled = profile.ScanEffect || viewOptions.Analog;
            profileView.RenderSweep(enabled, t, profile.ScanEffectSpeed);
            horizontalView.RenderSweep(enabled, t, profile.ScanEffectSpeed);
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// Knobs of the analog console: range, antenna tilt (elevation, azimuth) and decision height.
        /// </summary>
        private void BuildKnobs()
        {
            rangeKnob.Positions = Ranges.Values.Length;
            rangeKnob.LabelFor = i => Ranges.Values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            rangeKnob.Turned += steps =>
            {
                DistanceComboBox.SelectedIndex = Math.Clamp(DistanceComboBox.SelectedIndex + steps, 0, DistanceComboBox.Items.Count - 1);
                UpdateKnobs();
            };
            elevationKnob.Turned += steps => TiltAntenna(steps, 0);
            elevationKnob.Reset += () =>
            {
                if (radar.NeutralElevation()) InvalidateViews();
                UpdateKnobs();
            };
            azimuthKnob.Turned += steps => TiltAntenna(0, steps * AzimuthSign);
            azimuthKnob.Reset += () =>
            {
                if (radar.NeutralAzimuth()) InvalidateViews();
                UpdateKnobs();
            };
            dhKnob.Turned += steps => SetDecisionHeight(runway.MDH + steps * DecisionHeightStep);
            dhKnob.Reset += () => SetDecisionHeight(runway.DefaultMDH);
            glidePathKnob.Turned += steps =>
            {
                int index = Array.IndexOf(approaches, selectedApproach);
                if (approaches.Length > 0) SelectApproach(approaches[Math.Clamp(index + steps, 0, approaches.Length - 1)]);
                UpdateKnobs();
            };
            glidePathKnob.Reset += () =>
            {
                Runway? first = runwayList.FirstOrDefault(r => r.RunwayKey == runway.RunwayKey);
                if (first != null && approaches.Contains(first)) SelectApproach(first);
                UpdateKnobs();
            };
            brightnessKnob.LabelFor = i => i == 0 ? "MIN" : i == 100 / Profile.BrightnessStep - 1 ? "100" : i == Profile.MaxBrightness / Profile.BrightnessStep - 1 ? "MAX" : null;
            brightnessKnob.Turned += steps => ChangeBrightness(steps);
            brightnessKnob.Reset += () => SetBrightness(100);
            foreach (Knob knob in new[] { rangeKnob, elevationKnob, azimuthKnob, dhKnob, glidePathKnob, brightnessKnob })
            {
                KnobPanel.Children.Add(knob);
            }
        }

        /// <summary>
        /// Puts the knobs in the position of the current range and tilt (also changed with keyboard and mouse wheel).
        /// </summary>
        private void UpdateKnobs()
        {
            int steps = radar.TiltSteps;
            foreach ((Knob knob, double tilt, string low, string high) in new[]
            {
                (elevationKnob, radar.TiltElevation, "DN", "UP"),
                (azimuthKnob, radar.TiltAzimuth * AzimuthSign, "L", "R")
            })
            {
                if (knob.Positions != 2 * steps + 1)
                {
                    knob.Positions = 2 * steps + 1;
                    knob.LabelFor = i => i == 0 ? low : i == steps ? "0" : i == 2 * steps ? high : null;
                    knob.InvalidateVisual();
                }
                knob.Index = steps + (radar.TiltStep > 0 ? (int)Math.Round(tilt / radar.TiltStep) : 0);
            }
            rangeKnob.Index = Math.Max(0, DistanceComboBox.SelectedIndex);
            // Locked selector of the published approaches, only when the runway has more than one.
            glidePathKnob.Visibility = approaches.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (approaches.Length > 1)
            {
                if (glidePathKnob.Positions != approaches.Length || glidePathKnob.Tag != approaches)
                {
                    Runway[] list = approaches;
                    glidePathKnob.Positions = list.Length;
                    glidePathKnob.Tag = list;
                    glidePathKnob.LabelFor = i => i >= 0 && i < list.Length ? Runway.FormatGlideSlope(list[i].GlideSlope) : null;
                    glidePathKnob.InvalidateVisual();
                }
                glidePathKnob.Index = Math.Max(0, Array.IndexOf(approaches, selectedApproach));
            }
        }

        /// <summary>
        /// Sign of the azimuth tilt as shown and commanded: 1 = as seen by the pilot, -1 = swapped (as seen from the
        /// runway), see <see cref="Profile.AzimuthTiltSwapped"/>.
        /// </summary>
        private int AzimuthSign => settings.Active.AzimuthTiltSwapped ? -1 : 1;

        /// <summary>
        /// Brightness of the radar picture by steps of 10% (from 10% to 150%), separate for the modern display and
        /// the analog scope, saved in the profile.
        /// </summary>
        private void ChangeBrightness(int steps)
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int current = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            SetBrightness(current + steps * Profile.BrightnessStep);
        }

        private void SetBrightness(int percent)
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int current = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            int value = Math.Clamp(percent, Profile.MinBrightness, Profile.MaxBrightness);
            if (value == current) return;
            if (analog) profile.BrightnessAnalog = value; else profile.BrightnessModern = value;
            SettingsStore.Save(settings);
            // Above 100% the colours change: the views are drawn again with the boosted theme.
            if (current > 100 || value > 100) ApplyProfile(); else ApplyBrightness();
        }

        /// <summary>Boost of the colours above 100% brightness: 0 (up to 100%) to 1 (at the maximum).</summary>
        private double BrightnessBoost(Profile profile)
        {
            int percent = profile.DisplayMode == DisplayMode.Analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            return Math.Clamp((percent - 100) / (double)(Profile.MaxBrightness - 100), 0, 1);
        }

        /// <summary>
        /// Dims what is drawn on the radar views (lines, tracks, labels, texts). The modern display dims all of it,
        /// the analog scope only the phosphor: frame, glass and console panel stay as they are.
        /// </summary>
        private void ApplyBrightness()
        {
            Profile profile = settings.Active;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            int percent = analog ? profile.BrightnessAnalog : profile.BrightnessModern;
            // Up to 100% the picture is dimmed; above it the colours of the theme are boosted instead.
            Vertical.Opacity = Math.Min(percent, 100) / 100.0;
            Horizontal.Opacity = Math.Min(percent, 100) / 100.0;
            BrightnessText.Text = $"{percent}%";
            brightnessKnob.Index = Math.Clamp(percent / Profile.BrightnessStep - 1, 0, brightnessKnob.Positions - 1);
        }

        /// <summary>
        /// Modern display or analog scope: switched with the button or the A key, saved in the profile.
        /// </summary>
        private void ToggleDisplayMode()
        {
            Profile profile = settings.Active;
            profile.DisplayMode = profile.DisplayMode == DisplayMode.Analog ? DisplayMode.Modern : DisplayMode.Analog;
            SettingsStore.Save(settings);
            ApplyProfile();
        }

        /// <summary>Glow of the phosphor of the analog scope.</summary>
        private readonly System.Windows.Media.Effects.DropShadowEffect phosphorGlow = new()
        {
            Color = Theme.DefaultPhosphor,
            ShadowDepth = 0,
            BlurRadius = 8,
            Opacity = 0.85
        };

        /// <summary>
        /// Switches the window between the modern display and the analog console.
        /// </summary>
        private void ApplyDisplayMode(bool analog)
        {
            // Analog: the information is on the console panel instead of the screen.
            infoPanel.Visibility = analog ? Visibility.Collapsed : Visibility.Visible;
            ConsoleHost.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            foreach (Canvas canvas in new[] { Vertical, Horizontal })
            {
                // Transparent in the analog mode: the glass is behind, and the glow follows only the drawn lines.
                canvas.Background = analog ? Brushes.Transparent : viewOptions.Theme.Brush(StyleElement.Background);
                canvas.Effect = analog ? phosphorGlow : null;
            }
            if (analog)
            {
                ControlPanel.Background = new SolidColorBrush(ScopeBezel.PanelColor);
                DhLabel.Foreground = PanelTextBrush;
                IcaoLabel.Foreground = PanelTextBrush;
            }
            else
            {
                ControlPanel.ClearValue(Border.BackgroundProperty);
                DhLabel.ClearValue(TextBlock.ForegroundProperty);
                IcaoLabel.ClearValue(TextBlock.ForegroundProperty);
            }
            ApplyControlStyles(analog);
            Visibility modern = analog ? Visibility.Collapsed : Visibility.Visible;
            DistanceComboBox.Visibility = modern;
            TiltPanel.Visibility = modern;
            BrightnessPanel.Visibility = modern;
            DhDownButton.Visibility = modern;
            DhUpButton.Visibility = modern;
            GlidePathPanel.Visibility = modern;
            LabelsButton.Visibility = modern;
            // The analog scope has only the published approaches (no free angle).
            if (analog && runway.IsUnpublished && selectedApproach != null)
            {
                SelectApproach(selectedApproach, keepDecisionHeight: true);
            }
            KnobPanel.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            LayoutDisplay();
            UpdateKnobs();
            ApplyBrightness();
        }

        /// <summary>
        /// Analog mode: buttons as the keys of an equipment (engraved capital labels, lamps), and the text fields
        /// as readout windows (dark, amber text). Modern display: the normal controls.
        /// </summary>
        private void ApplyControlStyles(bool analog)
        {
            (Button Button, string Text)[] buttons =
            [
                (SettingsButton, "Settings..."),
                (RunwaysButton, "Runways..."),
                (CoordinationButton, "Coordination"),
                (ModeButton, analog ? "Modern (A)" : "Analog (A)")
            ];
            foreach ((Button button, string text) in buttons)
            {
                if (analog) button.Style = (Style)FindResource("ConsoleButton"); else button.ClearValue(StyleProperty);
                button.Content = analog ? text.Replace("...", "").ToUpperInvariant() : text;
            }
            // Lamps: the analog scope is on; the coordination key is lit while its panel is open.
            ModeButton.Tag = "Lit";
            CoordinationButton.Tag = coordinationWindow != null ? "Lit" : "Unlit";
            if (analog)
            {
                IcaoFilterBox.Style = (Style)FindResource("ConsoleBox");
                DhTextBox.Style = (Style)FindResource("ConsoleBox");
                RunwayComboBox.Style = (Style)FindResource("ConsoleCombo");
            }
            else
            {
                IcaoFilterBox.ClearValue(StyleProperty);
                DhTextBox.ClearValue(StyleProperty);
                RunwayComboBox.ClearValue(StyleProperty);
            }
        }

        /// <summary>
        /// Places the views: full area in the modern display; in the analog mode a round screen in a metal ring
        /// on the console panel, with the two views one above the other inside it (as on the old PAR scopes).
        /// </summary>
        private void LayoutDisplay()
        {
            bool analog = viewOptions.Analog;
            bool right = settings.Active.RunwaySide == RunwaySide.Right;
            double width = ScopeHost.ActualWidth;
            double height = ScopeHost.ActualHeight;
            if (!analog || width <= 0 || height <= 0)
            {
                ScopeArea.ClearValue(WidthProperty);
                ScopeArea.ClearValue(HeightProperty);
                ScopeArea.HorizontalAlignment = HorizontalAlignment.Stretch;
                ScopeArea.VerticalAlignment = VerticalAlignment.Stretch;
                ScopeArea.Clip = null;
                ScopeArea.Background = null;
                Vertical.Margin = new Thickness(0);
                Horizontal.Margin = new Thickness(0);
                BezelLayer.Children.Clear();
                DisplayArea.Background = viewOptions.Theme.Brush(StyleElement.Background);
                return;
            }
            double ring = Math.Max(5, Math.Min(width, height) * 0.018);
            // The screen is as wide as the window allows; in a low window its top and bottom (only frame and
            // glass) are cut by the window edges, up to MinVisibleFraction of the diameter, so the views stay large.
            const double MinVisibleFraction = 0.75;
            double radius = Math.Min(width / 2 - ring - 4, height / (2 * MinVisibleFraction));
            radius = Math.Max(60, Math.Min(radius, Math.Max(width, height)));
            double diameter = 2 * radius;
            double visible = Math.Min(diameter, height);
            double cut = (diameter - visible) / 2;
            ScopeArea.Width = diameter;
            ScopeArea.Height = visible;
            ScopeArea.HorizontalAlignment = HorizontalAlignment.Center;
            ScopeArea.VerticalAlignment = VerticalAlignment.Center;
            ScopeArea.Clip = new EllipseGeometry(new Point(radius, visible / 2), radius, radius);
            ScopeArea.Background = glassBrush;
            // The antenna side is moved in, so the origin of both views is inside the circle; the top and bottom
            // margins shrink as the circle is cut by the window.
            double inner = diameter * 0.12;
            double outer = diameter * 0.04;
            double edge = Math.Max(0, diameter * 0.07 - cut);
            Vertical.Margin = right ? new Thickness(outer, edge, inner, 0) : new Thickness(inner, edge, outer, 0);
            Horizontal.Margin = right ? new Thickness(outer, 0, inner, edge) : new Thickness(inner, 0, outer, edge);
            DisplayArea.Background = new SolidColorBrush(ScopeBezel.PanelColor);
            ScopeBezel.Draw(BezelLayer, new Size(width, height), new Point(width / 2, height / 2), radius, ring);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            coordinationWindow?.Close();
            Open = false;
            timer.Stop();
            aurora.Close();
            // Remember runway and range for the next start.
            if (selectedApproach != null)
            {
                settings.LastRunway = runway.ToString();
                settings.LastRange = runway.Distance;
                settings.LastGlideSlope = selectedApproach?.GlideSlope;
            }
            SaveWindowPlacement();
            SettingsStore.Save(settings);
        }

        private const double MinWindowWidth = 400;
        private const double MinWindowHeight = 300;

        /// <summary>
        /// Restores the window size and position of the last session. If the saved position is no longer on
        /// any screen (e.g. a monitor was disconnected or the layout changed), the window keeps the saved size
        /// (reduced to fit if needed) but is centred on the primary screen.
        /// </summary>
        private void RestoreWindowPlacement()
        {
            WindowPlacement? p = settings.Window;
            if (p == null || !double.IsFinite(p.Left) || !double.IsFinite(p.Top)
                || !double.IsFinite(p.Width) || !double.IsFinite(p.Height))
            {
                return;
            }
            Rect allScreens = new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Width = Math.Clamp(p.Width, MinWindowWidth, Math.Max(MinWindowWidth, allScreens.Width));
            Height = Math.Clamp(p.Height, MinWindowHeight, Math.Max(MinWindowHeight, allScreens.Height));
            // The title bar must be reachable with the mouse: its centre and both of its ends must be on screen.
            double titleY = p.Top + 10;
            bool visible = allScreens.Contains(new Point(p.Left + Width / 2, titleY))
                && allScreens.Contains(new Point(p.Left + 40, titleY))
                && allScreens.Contains(new Point(p.Left + Width - 40, titleY));
            if (visible)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = p.Left;
                Top = p.Top;
            }
            else
            {
                Width = Math.Min(Width, SystemParameters.WorkArea.Width);
                Height = Math.Min(Height, SystemParameters.WorkArea.Height);
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            // Maximize after setting the normal bounds, so the window is maximized on the screen it was on.
            if (p.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        /// <summary>
        /// Saves the window's normal bounds (also when maximized or minimized) and whether it is maximized.
        /// </summary>
        private void SaveWindowPlacement()
        {
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;
            settings.Window = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized
            };
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                runways = await DataFile.GetRunways(dataPath);
                runwayList = GroupRunways(runways);
                // The filter is for the session: it starts empty (the runway of the last session is still selected).
                SetFilterText("");
                ApplyRunwayFilter(userTyped: false);
                if (runways.Length == 0)
                {
                    MessageBox.Show(this, $"No valid runway found in {Path.GetFullPath(dataPath)}.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot read the runway file {Path.GetFullPath(dataPath)}:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            RunwayComboBox.SelectionChanged += RunwayComboBox_SelectionChanged;
            // Restore the runway used last time.
            // The last runway is saved with the name of its line ("LIPC 11 2.8"): its runway in the list, and its angle.
            Runway? lastLine = runways.FirstOrDefault(r => r.ToString() == settings.LastRunway);
            Runway? last = lastLine == null ? null : runwayList.FirstOrDefault(r => r.RunwayKey == lastLine.RunwayKey);
            restoreGlideSlope = settings.LastGlideSlope ?? lastLine?.GlideSlope;
            if (last != null)
            {
                restoringSession = true;
                ShowAllIfHidden(last);
                RunwayComboBox.SelectedItem = last;
                restoringSession = false;
            }
            // Range at start, as chosen in the profile.
            Profile profile = settings.Active;
            double startRange = profile.StartupRange switch
            {
                StartupRange.LastUsed when last != null && settings.LastRange is double lastRange => lastRange,
                StartupRange.Fixed => profile.PreferredRange,
                _ => runway.DefaultDistance
            };
            DistanceComboBox.SelectedIndex = IndexOfDistance(startRange);
            timer.Start();
            await ConnectionLoop();
        }

        private void RunwayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Runway first)
            {
                // The list was only filtered again: the runway in use stays as it is.
                if (filteringRunways && selectedApproach != null && first.RunwayKey == selectedApproach.RunwayKey) return;
                Runway previous = runway;
                approaches = ApproachesOf(first);
                // Approach: the one of the last session or the one in use before reloading the file (same angle),
                // otherwise the first line of the file for this runway.
                double? wantedGlideSlope = restoringSession ? restoreGlideSlope
                    : reloadingRunways ? (selectedApproach?.GlideSlope) : null;
                Runway r = (wantedGlideSlope is double gp ? approaches.FirstOrDefault(a => Math.Abs(a.GlideSlope - gp) < 0.005) : null) ?? first;
                selectedApproach = r;
                runway = r;
                // The decision height changed on the fly is not kept: back to the runway file value.
                runway.MDH = runway.DefaultMDH;
                // New runway, new antenna: back to neutral.
                radar.Neutral();
                DhTextBox.Text = FormatHeight(runway.MDH);
                profileView.SetRunway(runway);
                horizontalView.SetRunway(runway);
                // Range for the new runway, as chosen in the profile (at start the startup rule applies instead).
                Profile profile = settings.Active;
                double range = restoringSession ? r.DefaultDistance : reloadingRunways ? previous.Distance : profile.RunwayChangeRange switch
                {
                    RunwayChangeRange.KeepCurrent => previous.Distance,
                    RunwayChangeRange.Fixed => profile.PreferredRange,
                    _ => r.DefaultDistance
                };
                DistanceComboBox.SelectedIndex = IndexOfDistance(range);
                // Make sure runway and distance box always agree, even if the index did not change.
                if (DistanceComboBox.SelectedItem is Distance d)
                {
                    runway.Distance = d;
                }
                UpdateGlidePathSelector();
                InvalidateViews();
            }
        }

        /// <summary>True while the filter box is changed by the program.</summary>
        private bool settingFilter;
        /// <summary>True while the runway list is filtered again (the runway in use must not be selected again).</summary>
        private bool filteringRunways;
        /// <summary>Callsign whose airport has already been proposed in the filter (once per connection).</summary>
        private string? suggestedCallsign;

        private void SetFilterText(string text)
        {
            settingFilter = true;
            try { IcaoFilterBox.Text = text; }
            finally { settingFilter = false; }
        }

        /// <summary>
        /// Shows in the runway list only the airports starting with the letters typed in the ICAO box (all when
        /// empty). If no airport matches, the box turns red and the list is not changed. <paramref name="userTyped"/>:
        /// an airport with a single runway is then selected at once, and with several the list opens when the ICAO is
        /// complete.
        /// </summary>
        private void ApplyRunwayFilter(bool userTyped, bool keepSelection = true)
        {
            string text = IcaoFilterBox.Text.Trim().ToUpperInvariant();
            Runway[] filtered = text.Length == 0 ? runwayList
                : runwayList.Where(r => r.ICAO.StartsWith(text, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length == 0 && runwayList.Length > 0)
            {
                IcaoFilterBox.Background = new SolidColorBrush(viewOptions.Analog ? Color.FromRgb(0x5A, 0x10, 0x10) : Color.FromRgb(0xFF, 0xC8, 0xC8));
                return;
            }
            IcaoFilterBox.ClearValue(Control.BackgroundProperty);
            filteringRunways = true;
            try
            {
                RunwayComboBox.ItemsSource = filtered;
                // The runway in use stays shown if it is in the list.
                Runway? current = selectedApproach == null || !keepSelection ? null : filtered.FirstOrDefault(r => r.RunwayKey == selectedApproach.RunwayKey);
                RunwayComboBox.SelectedItem = current;
            }
            finally
            {
                filteringRunways = false;
            }
            if (!userTyped || RunwayComboBox.SelectedItem != null) return;
            if (filtered.Length == 1)
            {
                RunwayComboBox.SelectedItem = filtered[0];
            }
            else if (text.Length == 4 && filtered.Length > 1)
            {
                RunwayComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>Empties the filter if it hides the given runway (e.g. the one of the last session).</summary>
        private void ShowAllIfHidden(Runway runwayToShow)
        {
            if (RunwayComboBox.ItemsSource is Runway[] shown && shown.Contains(runwayToShow)) return;
            SetFilterText("");
            ApplyRunwayFilter(userTyped: false);
        }

        /// <summary>
        /// The airport of the callsign connected in Aurora (LIPC_APP: LIPC), if the file has it, has priority over
        /// anything typed in the filter: it is set there, and if the runway in use belongs to another airport the first
        /// runway of this one is selected. Once per callsign, so later changes by the controller are respected.
        /// Without a matching callsign nothing changes (the runway of the last session stays).
        /// </summary>
        private void SuggestAirport(string? callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign) || callsign == suggestedCallsign) return;
            suggestedCallsign = callsign;
            string icao = callsign.Split('_', '-')[0].Trim().ToUpperInvariant();
            if (icao.Length != 4 || !runwayList.Any(r => string.Equals(r.ICAO, icao, StringComparison.OrdinalIgnoreCase))) return;
            SetFilterText(icao);
            ApplyRunwayFilter(userTyped: false);
            if (selectedApproach == null || !string.Equals(selectedApproach.ICAO, icao, StringComparison.OrdinalIgnoreCase))
            {
                RunwayComboBox.SelectedItem = runwayList.First(r => string.Equals(r.ICAO, icao, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Glide path of the approach to restore at start.</summary>
        private double? restoreGlideSlope;

        /// <summary>One entry per runway (airport and designator without the angle), the first line of the file for each.</summary>
        private static Runway[] GroupRunways(Runway[] lines)
        {
            return lines.GroupBy(r => r.RunwayKey).Select(g => g.First()).ToArray();
        }

        /// <summary>Published approaches of a runway: the lines of the file with the same airport and designator, by angle.</summary>
        private Runway[] ApproachesOf(Runway first)
        {
            string key = first.RunwayKey;
            return runways.Where(r => r.RunwayKey == key)
                .GroupBy(r => Math.Round(r.GlideSlope, 3))
                .Select(g => g.First())
                .OrderBy(r => r.GlideSlope)
                .ToArray();
        }

        /// <summary>
        /// Another published approach of the same runway (other glide path angle): its own line of the file (DH,
        /// touchdown...), same range, antenna tilt and tracks. <paramref name="keepDecisionHeight"/>: the DH in use is
        /// kept (back from an unpublished angle), otherwise the DH of the line.
        /// </summary>
        private void SelectApproach(Runway approach, bool keepDecisionHeight = false)
        {
            if (approach == runway) { UpdateGlidePathSelector(); return; }
            double mdh = runway.MDH;
            double distance = runway.Distance;
            selectedApproach = approach;
            runway = approach;
            runway.MDH = keepDecisionHeight ? mdh : runway.DefaultMDH;
            runway.Distance = distance;
            DhTextBox.Text = FormatHeight(runway.MDH);
            profileView.SetRunway(runway, keepTracks: true);
            horizontalView.SetRunway(runway, keepTracks: true);
            UpdateGlidePathSelector();
            InvalidateViews();
        }

        /// <summary>
        /// Modern display: an angle typed by the controller that is not one of the published approaches. Same runway
        /// and DH in use, only the glide path (and so the touchdown point if not given in the file, and the MAPt) changes.
        /// </summary>
        private void SetUnpublishedGlideSlope(double degrees)
        {
            Runway basis = selectedApproach ?? runway;
            Runway copy = basis.WithGlideSlope(degrees);
            copy.MDH = runway.MDH;
            copy.Distance = runway.Distance;
            runway = copy;
            profileView.SetRunway(runway, keepTracks: true);
            horizontalView.SetRunway(runway, keepTracks: true);
            UpdateGlidePathSelector();
            InvalidateViews();
        }

        /// <summary>Lowest and highest glide path angle that can be typed (degrees).</summary>
        private const double MinGlideSlope = 1.0;
        private const double MaxGlideSlope = 7.0;

        /// <summary>Reads the angle typed in the GP box: a published one selects that approach, another one is unpublished.</summary>
        private void ApplyGlidePathText()
        {
            if (updatingGlidePath) return;
            string text = GlidePathComboBox.Text.Trim().Replace(',', '.').TrimEnd('°').Trim();
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double degrees)
                || degrees < MinGlideSlope || degrees > MaxGlideSlope)
            {
                UpdateGlidePathSelector();
                return;
            }
            degrees = Math.Round(degrees, 2);
            Runway? published = approaches.FirstOrDefault(a => Math.Abs(a.GlideSlope - degrees) < 0.005);
            if (published != null)
            {
                SelectApproach(published, keepDecisionHeight: runway.IsUnpublished);
            }
            else if (viewOptions.Analog)
            {
                UpdateGlidePathSelector();
            }
            else if (Math.Abs(degrees - runway.GlideSlope) >= 0.005)
            {
                SetUnpublishedGlideSlope(degrees);
            }
            else
            {
                UpdateGlidePathSelector();
            }
        }

        /// <summary>Fills the GP box (published angles) and shows the angle in use, orange when unpublished.</summary>
        private void UpdateGlidePathSelector()
        {
            updatingGlidePath = true;
            try
            {
                List<string> items = approaches.Select(a => Runway.FormatGlideSlope(a.GlideSlope)).ToList();
                if (GlidePathComboBox.ItemsSource is not List<string> current || !current.SequenceEqual(items))
                {
                    GlidePathComboBox.ItemsSource = items;
                }
                GlidePathComboBox.SelectedIndex = runway.IsUnpublished ? -1 : Array.IndexOf(approaches, runway);
                GlidePathComboBox.Text = Runway.FormatGlideSlope(runway.GlideSlope);
                if (runway.IsUnpublished)
                {
                    GlidePathComboBox.Foreground = Brushes.DarkOrange;
                    GlidePathComboBox.FontWeight = FontWeights.Bold;
                    GlidePathLabel.Text = "GP (°) UNPUBL.";
                }
                else
                {
                    GlidePathComboBox.ClearValue(Control.ForegroundProperty);
                    GlidePathComboBox.ClearValue(Control.FontWeightProperty);
                    GlidePathLabel.Text = "GP (°)";
                }
                GlidePathPanel.ToolTip = (approaches.Length > 1
                        ? $"Glide path: {approaches.Length} published approaches for this runway in runways.par ({string.Join(", ", items)}°): choose one in the list."
                        : "Glide path of this runway in runways.par.")
                    + $"\nAnother angle ({MinGlideSlope:0.0} to {MaxGlideSlope:0.0}) can be typed and confirmed with Enter: it is an UNPUBLISHED APPROACH (shown in orange)."
                    + "\nThe DH stays the same; the missed approach point (MAPt DIST) moves with the angle."
                    + "\nEsc: back to the angle in use. Not saved: choosing the runway again goes back to the file.";
            }
            finally
            {
                updatingGlidePath = false;
            }
            if (viewOptions.Analog) UpdateKnobs();
        }

        private const double DecisionHeightStep = 10;

        private static string FormatHeight(double feet)
        {
            return feet.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Changes the decision height of the current runway for this session only (never saved).
        /// </summary>
        private void SetDecisionHeight(double feet)
        {
            runway.MDH = Math.Clamp(Math.Round(feet), 0, 5000);
            DhTextBox.Text = FormatHeight(runway.MDH);
            InvalidateViews();
        }

        private void ApplyDecisionHeightText()
        {
            if (double.TryParse(DhTextBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double feet)
                || double.TryParse(DhTextBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out feet))
            {
                if (Math.Round(feet) != runway.MDH)
                {
                    SetDecisionHeight(feet);
                    return;
                }
            }
            DhTextBox.Text = FormatHeight(runway.MDH);
        }

        private async void RunwaysButton_Click(object sender, RoutedEventArgs e)
        {
            RunwayEditorWindow window = new(dataPath, runways, (selectedApproach ?? RunwayComboBox.SelectedItem as Runway)?.ToString(), settings, () =>
            {
                SettingsStore.Save(settings);
                InvalidateViews();
            })
            {
                Owner = this
            };
            window.ShowDialog();
            if (window.Saved)
            {
                await ReloadRunways();
            }
        }

        /// <summary>
        /// Reads the runway file again after editing and selects the same runway as before, if it still exists.
        /// </summary>
        private async Task ReloadRunways()
        {
            string? currentName = selectedApproach?.RunwayKey;
            double? unpublished = runway.IsUnpublished ? runway.GlideSlope : null;
            try
            {
                runways = await DataFile.GetRunways(dataPath);
                runwayList = GroupRunways(runways);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot read the runway file {dataPath}:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            reloadingRunways = true;
            try
            {
                // New objects read from the file: the runway is selected again below.
                ApplyRunwayFilter(userTyped: false, keepSelection: false);
                Runway? same = runwayList.FirstOrDefault(r => r.RunwayKey == currentName);
                if (same != null)
                {
                    ShowAllIfHidden(same);
                    RunwayComboBox.SelectedItem = same;
                    // An unpublished angle in use stays (modern display), unless the file now has it.
                    if (unpublished is double degrees && !viewOptions.Analog
                        && !approaches.Any(a => Math.Abs(a.GlideSlope - degrees) < 0.005))
                    {
                        SetUnpublishedGlideSlope(degrees);
                    }
                }
            }
            finally
            {
                reloadingRunways = false;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow window = new(settings, ApplyProfile)
            {
                Owner = this
            };
            window.ShowDialog();
        }

        /// <summary>
        /// Applies the active profile's settings to the screen.
        /// </summary>
        /// <summary>
        /// Keyboard: arrows tilt the antenna (up/down elevation, left/right azimuth), Home brings it back to neutral.
        /// Ignored while typing in a text field.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;
            switch (e.Key)
            {
                case Key.Up: TiltAntenna(1, 0); break;
                case Key.Down: TiltAntenna(-1, 0); break;
                case Key.Left: TiltAntenna(0, -AzimuthSign); break;
                case Key.Right: TiltAntenna(0, AzimuthSign); break;
                case Key.Home: NeutralAntenna(); break;
                case Key.L: ToggleLabels(); break;
                case Key.A: ToggleDisplayMode(); break;
                default: return;
            }
            e.Handled = true;
        }

        /// <summary>
        /// Shows or hides the labels of all tracks, in both views (for this session).
        /// </summary>
        private void ToggleLabels()
        {
            // The analog scope has no labels.
            if (viewOptions.Analog) return;
            viewOptions.ShowLabels = !viewOptions.ShowLabels;
            if (viewOptions.ShowLabels)
            {
                // Showing the labels again also brings back those hidden one by one.
                profileView.ShowAllLabels();
                horizontalView.ShowAllLabels();
            }
            LabelsButton.Content = viewOptions.ShowLabels ? "Hide labels (L)" : "Show labels (L)";
            Redraw();
        }

        private void TiltAntenna(int elevationSteps, int azimuthSteps)
        {
            if (radar.Tilt(elevationSteps, azimuthSteps))
            {
                InvalidateViews();
            }
            UpdateKnobs();
        }

        private void NeutralAntenna()
        {
            if (radar.Neutral())
            {
                InvalidateViews();
            }
            UpdateKnobs();
        }

        private void ApplyProfile()
        {
            Profile profile = settings.Active;
            radar.ApplyProfile(profile);
            viewOptions.Qfe = profile.PressureReference == PressureReference.QFE;
            bool analog = profile.DisplayMode == DisplayMode.Analog;
            viewOptions.Analog = analog;
            viewOptions.Theme = analog
                ? Theme.Analog(ColorText.Parse(profile.AnalogColor, Theme.DefaultPhosphor), BrightnessBoost(profile))
                : Theme.Modern(profile.Style, BrightnessBoost(profile));
            viewOptions.RangeMarks = profile.RangeMarks;
            // Reminders of all the lines (approaches) of the runway.
            viewOptions.Reminders = r => settings.RemindersFor(runways.Where(x => x.RunwayKey == r.RunwayKey).Select(x => x.ToString()).Append(r.ToString()));
            viewOptions.RangeTextBelowHorizon = profile.RangeTextBelowHorizon;
            phosphorGlow.Color = viewOptions.Theme.Glow;
            // The old scopes had no altitude scale.
            viewOptions.ShowAltitudeScale = profile.ShowAltitudeScale && !analog;
            viewOptions.ScaleInMetres = profile.AltitudeScaleUnit == LengthUnit.Metres;
            viewOptions.HistoryEnabled = profile.HistoryEnabled;
            viewOptions.HistoryDots = profile.HistoryDots;
            viewOptions.HistoryInterval = profile.HistoryInterval;
            viewOptions.TrackSymbol = profile.TrackSymbol;
            viewOptions.ThresholdSymbol = profile.ThresholdSymbol;
            viewOptions.TouchdownSymbol = profile.TouchdownSymbol;
            viewOptions.AntennaSymbol = profile.AntennaSymbol;
            viewOptions.HistorySymbol = profile.HistorySymbol;
            viewOptions.ElevationLabel = profile.ElevationLabel;
            viewOptions.AzimuthLabel = profile.AzimuthLabel;
            viewOptions.Version++;
            DhLabel.Text = $"{Pressure.Names(profile.MinimaLabel).Height} (ft)";
            bool right = profile.RunwaySide == RunwaySide.Right;
            profileView.SetRunwayOnRight(right);
            horizontalView.SetRunwayOnRight(right);
            // Information area in the top corner on the runway side, away from the far end of the scan limits.
            // Leave room for the altitude scale, drawn on the same side.
            double margin = viewOptions.ShowAltitudeScale ? 75 : 4;
            ApplyDisplayMode(analog);
            if (right)
            {
                infoPanel.ClearValue(Canvas.LeftProperty);
                Canvas.SetRight(infoPanel, margin);
            }
            else
            {
                infoPanel.ClearValue(Canvas.RightProperty);
                Canvas.SetLeft(infoPanel, margin);
            }
            Canvas.SetTop(infoPanel, 0);
            TextAlignment alignment = right ? TextAlignment.Right : TextAlignment.Left;
            infoText.TextAlignment = alignment;
            courseText.TextAlignment = alignment;
            glidePathText.TextAlignment = alignment;
            missedApproachText.TextAlignment = alignment;
            infoText2.TextAlignment = alignment;
            statusText.TextAlignment = alignment;
            dataText.TextAlignment = alignment;
            tiltText.TextAlignment = alignment;
            InvalidateViews();
        }

        /// <summary>
        /// Keeps trying to (re)connect to Aurora while the window is open.
        /// It runs separately from the refresh timer, so a slow or failing connection attempt
        /// (about 2 s on Windows when Aurora is closed) never delays the drawing.
        /// </summary>
        private async Task ConnectionLoop()
        {
            while (Open)
            {
                if (!aurora.Connected)
                {
                    if (await aurora.TryConnect())
                    {
                        // Ask for the QNH again right after (re)connecting.
                        qnhRefreshNeeded = true;
                    }
                }
                await Task.Delay(ReconnectDelay);
            }
        }

        /// <summary>
        /// Index of the available display distance closest to the given one.
        /// </summary>
        private int IndexOfDistance(double distance)
        {
            return Ranges.IndexOfClosest(distance);
        }

        private void DistanceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ( e.AddedItems.Count > 0 && e.AddedItems[0] is Distance d)
            {
                runway.Distance = d;
                // Redraw now with the last known traffic: the range change is immediate.
                InvalidateViews();
            }
        }
        private async void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                Runway current = runway;
                List<Aircraft> aircrafts = [];
                if (aurora.Connected)
                {
                    if (qnhRefreshNeeded || current.ICAO != lastQnhIcao || DateTime.UtcNow - lastQnhUpdate >= QnhRefreshInterval)
                    {
                        qnhRefreshNeeded = false;
                        int nqnh = await aurora.GetQNH(current);
                        if (nqnh != 0)
                        {
                            qnh = nqnh;
                        }
                        else if (current.ICAO != lastQnhIcao)
                        {
                            // No METAR for the new airport: do not keep showing the old airport's QNH.
                            qnh = 0;
                        }
                        lastQnhIcao = current.ICAO;
                        lastQnhUpdate = DateTime.UtcNow;
                    }
                    if (DateTime.UtcNow - lastCallsignCheck >= CallsignCheckInterval)
                    {
                        lastCallsignCheck = DateTime.UtcNow;
                        string? own = await aurora.GetConnectedCallsign();
                        connectedCallsign = own;
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            coordinationWindow?.SetCallsign(own);
                            SuggestAirport(own);
                        });
                    }
                    string[] callsigns = await aurora.GetTrafficList();
                    foreach (string callsign in callsigns)
                    {
                        Aircraft? aircraft = await aurora.GetTrafficPosition(callsign);
                        if (aircraft != null)
                        {
                            aircrafts.Add(aircraft);
                        }
                    }
                }
                if (!aurora.Connected && connectedCallsign != null)
                {
                    connectedCallsign = null;
                    lastCallsignCheck = DateTime.MinValue;
                    _ = Dispatcher.BeginInvoke(() => coordinationWindow?.SetCallsign(null));
                }
                verticalSpeed.Update(aircrafts, DateTime.UtcNow);
                if (aurora.Connected)
                {
                    refreshMonitor.Update(aircrafts, DateTime.UtcNow);
                    dataInterval = refreshMonitor.IntervalSeconds;
                }
                else
                {
                    refreshMonitor.Reset();
                    dataInterval = double.NaN;
                }
                lastAircrafts = aircrafts;
                Draw();
            }
            catch (Exception)
            {
                // Never let a single failed refresh crash the program; the next refresh will try again.
            }
            finally
            {
                if (Open)
                {
                    timer.Start();
                }
            }
        }
        /// <summary>
        /// Redraw from the refresh timer's thread.
        /// </summary>
        private void Draw()
        {
            if (!Open) return;
            Dispatcher.Invoke(Redraw);
        }
        /// <summary>
        /// Rebuilds the static parts of both views (after a change of runway, range, size or settings) and redraws.
        /// </summary>
        private void InvalidateViews()
        {
            profileView.Invalidate();
            horizontalView.Invalidate();
            Redraw();
        }
        /// <summary>
        /// Updates both views with the last known traffic. Must be called on the window's (UI) thread.
        /// Only what changed is redrawn: static elements are rebuilt only after <see cref="InvalidateViews"/>.
        /// </summary>
        private void Redraw()
        {
            if (!Open) return;
            List<Aircraft> aircrafts = lastAircrafts;
            UpdateInfo();
            profileView.Render(aircrafts);
            horizontalView.Render(aircrafts);
        }
        private static void SetText(TextBlock block, string text)
        {
            if (block.Text != text) block.Text = text;
        }

        private void UpdateInfo()
        {
            if (viewOptions.Analog) UpdateKnobs();
            Profile profile = settings.Active;
            bool qfe = profile.PressureReference == PressureReference.QFE;
            string pressure = qnh == 0 ? "----" : Pressure.Format(qfe ? Pressure.QfeFromQnh(qnh, runway.Elevation) : qnh, profile.PressureUnit);
            (string altitudeName, string heightName) = Pressure.Names(profile.MinimaLabel);
            string minimum = qfe
                ? $"{heightName} {FormatHeight(runway.MDH)} ft"
                : $"{altitudeName} {FormatHeight(runway.MDH + runway.Elevation)} ft";
            string course = runway.FinalCourse(profile.MagneticVariation).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            string glidePath = Runway.FormatGlideSlope(runway.GlideSlope);
            string missedApproach = runway.MissedApproachPointText();
            // Texts changed only when different, so an open tooltip is not disturbed by the refresh.
            SetText(infoText, $"RWY {runway.BaseDesignator}");
            SetText(courseText, $"CRS {course}");
            bool unpublished = runway.IsUnpublished;
            SetText(glidePathText, unpublished ? $"GP {glidePath}° UNPUBLISHED APPROACH" : $"GP {glidePath}°");
            SetText(missedApproachText, $"MAPt DIST {missedApproach}{(missedApproach == "----" ? "" : " NM")}");
            Brush highlight = unpublished ? Brushes.Orange : Brushes.White;
            FontWeight weight = unpublished ? FontWeights.Bold : FontWeights.Normal;
            foreach (TextBlock block in new[] { glidePathText, missedApproachText })
            {
                if (block.Foreground != highlight) block.Foreground = highlight;
                if (block.FontWeight != weight) block.FontWeight = weight;
            }
            SetText(infoText2, $"{(qfe ? "QFE" : "QNH")} {pressure}\n{minimum}");
            if (viewOptions.Analog)
            {
                consolePanel.Update(new ConsoleData(
                    runway.ICAO,
                    runway.BaseDesignator,
                    course,
                    qfe ? "QFE" : "QNH",
                    pressure,
                    qfe ? heightName : altitudeName,
                    FormatHeight(qfe ? runway.MDH : runway.MDH + runway.Elevation),
                    runway.GlideSlope,
                    missedApproach,
                    runway.Distance,
                    radar.TiltElevation,
                    radar.TiltAzimuth,
                    profile.AzimuthTiltSwapped,
                    aurora.Connected,
                    dataInterval,
                    !double.IsNaN(dataInterval) && dataInterval > MaxGoodDataInterval));
            }
            // Antenna tilt: shown only when not neutral, so the controller does not forget it.
            if (radar.IsNeutral)
            {
                tiltText.Visibility = Visibility.Collapsed;
            }
            else
            {
                List<string> parts = [];
                if (radar.TiltElevation != 0)
                {
                    parts.Add($"EL TILT {Math.Abs(radar.TiltElevation).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {(radar.TiltElevation > 0 ? "UP" : "DN")}");
                }
                if (radar.TiltAzimuth != 0)
                {
                    double shown = radar.TiltAzimuth * AzimuthSign;
                    parts.Add($"AZ TILT {Math.Abs(shown).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {(shown > 0 ? "R" : "L")}");
                }
                tiltText.Text = string.Join("\n", parts);
                tiltText.Visibility = Visibility.Visible;
            }
            if (aurora.Connected)
            {
                statusText.Text = "STS OK";
                statusText.Foreground = Brushes.Green;
            }
            else
            {
                statusText.Text = "STS FAIL";
                statusText.Foreground = Brushes.Red;
            }
            // Measured update interval of the traffic (shown when there is moving traffic to measure it).
            double interval = dataInterval;
            if (double.IsNaN(interval))
            {
                dataText.Visibility = Visibility.Collapsed;
            }
            else
            {
                dataText.Visibility = Visibility.Visible;
                string seconds = interval.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                if (interval > MaxGoodDataInterval)
                {
                    dataText.Text = $"DATA {seconds}s - SET AURORA TRAFFIC REFRESH TO 0.5s";
                    dataText.Foreground = Brushes.Red;
                }
                else
                {
                    dataText.Text = $"DATA {seconds}s";
                    dataText.Foreground = Brushes.Green;
                }
            }
        }
        private void Window_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            int i = DistanceComboBox.SelectedIndex;
            if (e.Delta > 0)
            {
                if((i+1) < DistanceComboBox.Items.Count)
                {
                    DistanceComboBox.SelectedIndex = i + 1;
                }
            } else
            {
                if((i-1) >= 0)
                {
                    DistanceComboBox.SelectedIndex = i - 1;
                }
            }
        }
    }
}
