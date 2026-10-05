using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        private readonly Distance[] distances = new Distance[] {1, 2.5, 5, 10, 15, 20};
        private string dataPath = "runways.par";
        private volatile bool Open = true;
        private readonly AppSettings settings;
        /// <summary>
        /// Information area (runway, QNH, connection status), created once and updated at every refresh.
        /// </summary>
        private readonly StackPanel infoPanel = new();
        private readonly TextBlock infoText = new() { FontSize = 14, Foreground = Brushes.White };
        private readonly TextBlock statusText = new() { FontSize = 14 };
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
            this.Loaded += MainWindow_Loaded;
            DistanceComboBox.ItemsSource = distances;
            DistanceComboBox.SelectedIndex = IndexOfDistance(runway.Distance);//10 nm
            DistanceComboBox.SelectionChanged += DistanceComboBox_SelectionChanged;
            profileView = new(Vertical, runway);
            horizontalView = new(Horizontal, runway);
            infoPanel.Children.Add(infoText);
            infoPanel.Children.Add(statusText);
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
            SettingsStore.Save(settings);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Runway[] runways = [];
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
            // Restore the runway and range used last time.
            Runway? last = runways.FirstOrDefault(r => r.ToString() == settings.LastRunway);
            if (last != null)
            {
                RunwayComboBox.SelectedItem = last;
                if (settings.LastRange is double range)
                {
                    DistanceComboBox.SelectedIndex = IndexOfDistance(range);
                }
            }
            timer.Start();
            await ConnectionLoop();
        }

        private void RunwayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Runway r)
            {
                // Read the runway's default distance before the distance box can change it.
                double defaultDistance = r.Distance;
                runway = r;
                profileView.SetRunway(runway);
                horizontalView.SetRunway(runway);
                DistanceComboBox.SelectedIndex = IndexOfDistance(defaultDistance);
                // Make sure runway and distance box always agree, even if the index did not change.
                if (DistanceComboBox.SelectedItem is Distance d)
                {
                    runway.Distance = d;
                }
                InvalidateViews();
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
        private void ApplyProfile()
        {
            bool right = settings.Active.RunwaySide == RunwaySide.Right;
            profileView.SetRunwayOnRight(right);
            horizontalView.SetRunwayOnRight(right);
            // Information area in the top corner on the runway side, away from the far end of the scan limits.
            if (right)
            {
                infoPanel.ClearValue(Canvas.LeftProperty);
                Canvas.SetRight(infoPanel, 4);
            }
            else
            {
                infoPanel.ClearValue(Canvas.RightProperty);
                Canvas.SetLeft(infoPanel, 0);
            }
            Canvas.SetTop(infoPanel, 0);
            TextAlignment alignment = right ? TextAlignment.Right : TextAlignment.Left;
            infoText.TextAlignment = alignment;
            statusText.TextAlignment = alignment;
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
            int best = 0;
            for (int i = 1; i < distances.Length; i++)
            {
                if (Math.Abs(distances[i] - distance) < Math.Abs(distances[best] - distance))
                {
                    best = i;
                }
            }
            return best;
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
            infoText.Text = $"RWY {runway.Designator}\nQNH {qnh}";
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
