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
        private readonly Knob rangeKnob = new() { Title = "RANGE NM", ToolTip = "Range. Turn with the mouse wheel, drag up/down or click right/left." };
        private readonly Knob elevationKnob = new() { Title = "EL TILT", ToolTip = "Antenna elevation tilt. Turn with the mouse wheel, drag up/down or click right/left; double click: neutral." };
        private readonly Knob azimuthKnob = new() { Title = "AZ TILT", ToolTip = "Antenna azimuth tilt. Turn with the mouse wheel, drag up/down or click right/left; double click: neutral." };
        private readonly Knob dhKnob = new() { Title = "DH", ToolTip = "Decision height, 10 ft per step. Turn with the mouse wheel, drag up/down or click right/left; double click: runway value." };
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
        private Runway[] runways = [];
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
            infoPanel.Children.Add(ToolTips.KeepOpen(courseText));
            foreach (Knob knob in new[] { rangeKnob, elevationKnob, azimuthKnob, dhKnob }) ToolTips.KeepOpen(knob);
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
            TiltLeftButton.Click += (s, e) => TiltAntenna(0, -1);
            TiltRightButton.Click += (s, e) => TiltAntenna(0, 1);
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
                coordinationWindow.Closed += (s, e) => coordinationWindow = null;
                coordinationWindow.Show();
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
            azimuthKnob.Turned += steps => TiltAntenna(0, steps);
            azimuthKnob.Reset += () =>
            {
                if (radar.NeutralAzimuth()) InvalidateViews();
                UpdateKnobs();
            };
            dhKnob.Turned += steps => SetDecisionHeight(runway.MDH + steps * DecisionHeightStep);
            dhKnob.Reset += () => SetDecisionHeight(runway.DefaultMDH);
            foreach (Knob knob in new[] { rangeKnob, elevationKnob, azimuthKnob, dhKnob })
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
                (azimuthKnob, radar.TiltAzimuth, "L", "R")
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
            }
            else
            {
                ControlPanel.ClearValue(Border.BackgroundProperty);
                DhLabel.ClearValue(TextBlock.ForegroundProperty);
            }
            Visibility modern = analog ? Visibility.Collapsed : Visibility.Visible;
            DistanceComboBox.Visibility = modern;
            TiltPanel.Visibility = modern;
            DhDownButton.Visibility = modern;
            DhUpButton.Visibility = modern;
            LabelsButton.Visibility = modern;
            KnobPanel.Visibility = analog ? Visibility.Visible : Visibility.Collapsed;
            ModeButton.Content = analog ? "Modern (A)" : "Analog (A)";
            LayoutDisplay();
            UpdateKnobs();
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
            if (RunwayComboBox.SelectedItem is Runway selected)
            {
                settings.LastRunway = selected.ToString();
                settings.LastRange = selected.Distance;
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
                RunwayComboBox.ItemsSource = runways;
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
            Runway? last = runways.FirstOrDefault(r => r.ToString() == settings.LastRunway);
            if (last != null)
            {
                restoringSession = true;
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
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Runway r)
            {
                Runway previous = runway;
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
                InvalidateViews();
            }
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
            RunwayEditorWindow window = new(dataPath, runways, (RunwayComboBox.SelectedItem as Runway)?.ToString(), settings, () =>
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
            string? currentName = (RunwayComboBox.SelectedItem as Runway)?.ToString();
            try
            {
                runways = await DataFile.GetRunways(dataPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot read the runway file {dataPath}:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            reloadingRunways = true;
            try
            {
                RunwayComboBox.ItemsSource = runways;
                Runway? same = runways.FirstOrDefault(r => r.ToString() == currentName);
                if (same != null)
                {
                    RunwayComboBox.SelectedItem = same;
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
                case Key.Left: TiltAntenna(0, -1); break;
                case Key.Right: TiltAntenna(0, 1); break;
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
                ? Theme.Analog(ColorText.Parse(profile.AnalogColor, Theme.DefaultPhosphor))
                : Theme.Modern(profile.Style);
            viewOptions.RangeMarks = profile.RangeMarks;
            viewOptions.Reminders = settings.RemindersFor;
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
                        _ = Dispatcher.BeginInvoke(() => coordinationWindow?.SetCallsign(own));
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
            string glidePath = runway.GlideSlope.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
            // Texts changed only when different, so an open tooltip is not disturbed by the refresh.
            SetText(infoText, $"RWY {runway.Designator}");
            SetText(courseText, $"CRS {course}");
            SetText(infoText2, $"GP {glidePath}°\n{(qfe ? "QFE" : "QNH")} {pressure}\n{minimum}");
            if (viewOptions.Analog)
            {
                consolePanel.Update(new ConsoleData(
                    runway.ICAO,
                    runway.Designator,
                    course,
                    qfe ? "QFE" : "QNH",
                    pressure,
                    qfe ? heightName : altitudeName,
                    FormatHeight(qfe ? runway.MDH : runway.MDH + runway.Elevation),
                    runway.GlideSlope,
                    runway.Distance,
                    radar.TiltElevation,
                    radar.TiltAzimuth,
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
                    parts.Add($"AZ TILT {Math.Abs(radar.TiltAzimuth).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} {(radar.TiltAzimuth > 0 ? "R" : "L")}");
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
