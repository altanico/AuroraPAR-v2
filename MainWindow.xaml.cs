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
        private ProfileView profileView;
        private HorizontalView horizontalView;
        private readonly Aurora aurora;
        private readonly Distance[] distances = new Distance[] {1, 2.5, 5, 10, 15, 20};
        private string dataPath = "runways.par";
        private volatile bool Open = true;
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
            this.Loaded += MainWindow_Loaded;
            DistanceComboBox.ItemsSource = distances;
            DistanceComboBox.SelectedIndex = IndexOfDistance(runway.Distance);//10 nm
            DistanceComboBox.SelectionChanged += DistanceComboBox_SelectionChanged;
            profileView = new(Vertical, runway);
            horizontalView = new(Horizontal, runway);
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
            Vertical.SizeChanged += (s, e) => Redraw();
            Horizontal.SizeChanged += (s, e) => Redraw();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Open = false;
            timer.Stop();
            aurora.Close();
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Runway[] runways = await DataFile.GetRunways(dataPath);
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
                profileView = new(Vertical, runway);
                horizontalView = new(Horizontal, runway);
                DistanceComboBox.SelectedIndex = IndexOfDistance(defaultDistance);
                // Make sure runway and distance box always agree, even if the index did not change.
                if (DistanceComboBox.SelectedItem is Distance d)
                {
                    runway.Distance = d;
                }
                Redraw();
            }
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
                Redraw();
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
        /// Redraws both views with the last known traffic. Must be called on the window's (UI) thread.
        /// </summary>
        private void Redraw()
        {
            if (!Open) return;
            List<Aircraft> aircrafts = lastAircrafts;
            Vertical.Children.Clear();
            Horizontal.Children.Clear();
            DrawInfo();
            profileView.Draw(aircrafts);
            horizontalView.Draw(aircrafts);
        }
        private void DrawInfo()
        {
            TextBlock info = new()
            {
                Text = $"RWY {runway.Designator}\nQNH {qnh}",
                FontSize = 14,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(info, 0);
            Canvas.SetTop(info, 0);
            Vertical.Children.Add(info);
            TextBlock sts = new()
            {
                FontSize = 14
            };
            if (aurora.Connected)
            {
                sts.Text = "STS OK";
                sts.Foreground = Brushes.Green;
            } else
            {
                sts.Text = "STS FAIL";
                sts.Foreground = Brushes.Red;
            }
            Canvas.SetLeft(sts, 0);
            info.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetTop(sts, info.DesiredSize.Height);
            Vertical.Children.Add(sts);
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
