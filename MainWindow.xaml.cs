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
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
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
            RunwayEditorWindow window = new(dataPath, runways, (RunwayComboBox.SelectedItem as Runway)?.ToString())
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
                default: return;
            }
            e.Handled = true;
        }

        private void TiltAntenna(int elevationSteps, int azimuthSteps)
        {
            if (radar.Tilt(elevationSteps, azimuthSteps))
            {
                InvalidateViews();
            }
        }

        private void NeutralAntenna()
        {
            if (radar.Neutral())
            {
                InvalidateViews();
            }
        }

        private void ApplyProfile()
        {
            Profile profile = settings.Active;
            radar.ApplyProfile(profile);
            viewOptions.Qfe = profile.PressureReference == PressureReference.QFE;
            viewOptions.ShowAltitudeScale = profile.ShowAltitudeScale;
            viewOptions.ScaleInMetres = profile.AltitudeScaleUnit == LengthUnit.Metres;
            DhLabel.Text = $"{Pressure.Names(profile.MinimaLabel).Height} (ft)";
            bool right = profile.RunwaySide == RunwaySide.Right;
            profileView.SetRunwayOnRight(right);
            horizontalView.SetRunwayOnRight(right);
            // Information area in the top corner on the runway side, away from the far end of the scan limits.
            // Leave room for the altitude scale, drawn on the same side.
            double margin = profile.ShowAltitudeScale ? 75 : 4;
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
        private void UpdateInfo()
        {
            Profile profile = settings.Active;
            bool qfe = profile.PressureReference == PressureReference.QFE;
            string pressure = qnh == 0 ? "----" : Pressure.Format(qfe ? Pressure.QfeFromQnh(qnh, runway.Elevation) : qnh, profile.PressureUnit);
            (string altitudeName, string heightName) = Pressure.Names(profile.MinimaLabel);
            string minimum = qfe
                ? $"{heightName} {FormatHeight(runway.MDH)} ft"
                : $"{altitudeName} {FormatHeight(runway.MDH + runway.Elevation)} ft";
            infoText.Text = $"RWY {runway.Designator}\n{(qfe ? "QFE" : "QNH")} {pressure}\n{minimum}";
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
