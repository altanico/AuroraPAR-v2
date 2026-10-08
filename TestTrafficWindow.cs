using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Window of the test traffic (key T): adds virtual aircraft on the glide path and the centreline and moves the
    /// selected one with a joystick (left/right = lateral offset from the centreline, up/down = height above or
    /// below the glide path; on release the offset reached is kept). Closing the window removes the test traffic.
    /// </summary>
    internal sealed class TestTrafficWindow : Window
    {
        private const double PadSize = 160;
        private const double KnobSize = 22;
        private const double PadReach = (PadSize - KnobSize) / 2;

        private readonly TestTraffic traffic;
        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox speedBox = NumberBox("140");
        private readonly TextBox squawkBox = NumberBox("7001");
        private readonly ListBox list = new() { Height = 90, Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox autoCheck = new() { Content = "Auto: back onto the glide path and centreline by itself", Margin = new Thickness(0, 6, 0, 0) };
        private readonly CheckBox auroraCheck = new()
        {
            Content = "Aurora-like data (positions every 0.5 s, altitude in steps)",
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "As the real data from Aurora, to test the track smoothing. Off: perfect positions."
        };
        private readonly Button pauseButton = new() { Content = "Pause", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
        private readonly Canvas pad = new() { Width = PadSize, Height = PadSize, Background = new SolidColorBrush(Color.FromRgb(0x22, 0x24, 0x22)), Cursor = Cursors.Hand };
        private readonly Ellipse knob = new() { Width = KnobSize, Height = KnobSize, Fill = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0x70)), Stroke = Brushes.Black, IsHitTestVisible = false };
        private readonly TextBlock status = new() { Margin = new Thickness(0, 6, 0, 0), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(0.5) };
        private bool updatingList;

        internal TestTrafficWindow(TestTraffic traffic)
        {
            this.traffic = traffic;
            Title = "Aurora PAR - Test traffic";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 330 };
            root.Children.Add(new TextBlock
            {
                Text = "Virtual aircraft for tests, shown as traffic from Aurora (also without Aurora). Closing this window removes them.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray
            });

            // New aircraft.
            WrapPanel add = new() { Margin = new Thickness(0, 8, 0, 0) };
            add.Children.Add(Caption("Distance"));
            add.Children.Add(distanceBox);
            add.Children.Add(Caption("NM  Speed"));
            add.Children.Add(speedBox);
            add.Children.Add(Caption("kt  SSR A"));
            add.Children.Add(squawkBox);
            Button addButton = new() { Content = "Add", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
            addButton.Click += (s, e) => AddPlane();
            add.Children.Add(addButton);
            root.Children.Add(add);

            root.Children.Add(list);
            list.SelectionChanged += (s, e) =>
            {
                if (updatingList) return;
                ShowSelected();
            };
            root.Children.Add(autoCheck);
            autoCheck.Click += (s, e) => ChangeSelected(p => p.Auto = autoCheck.IsChecked == true);

            // Joystick.
            Grid padArea = new() { Margin = new Thickness(0, 8, 0, 0) };
            padArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PadSize + 10) });
            padArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border padBorder = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = pad, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            pad.Children.Add(new Line { X1 = PadSize / 2, Y1 = 4, X2 = PadSize / 2, Y2 = PadSize - 4, Stroke = Brushes.DimGray });
            pad.Children.Add(new Line { X1 = 4, Y1 = PadSize / 2, X2 = PadSize - 4, Y2 = PadSize / 2, Stroke = Brushes.DimGray });
            pad.Children.Add(PadText("UP", PadSize / 2 - 8, 2));
            pad.Children.Add(PadText("DN", PadSize / 2 - 8, PadSize - 16));
            pad.Children.Add(PadText("L", 4, PadSize / 2 - 16));
            pad.Children.Add(PadText("R", PadSize - 12, PadSize / 2 - 16));
            pad.Children.Add(knob);
            CentreKnob();
            pad.ToolTip = "Like a control stick: hold left/right to turn the selected aircraft off the centreline heading (as seen by the pilot), up/down to climb or descend relative to the glide path. Released, the heading and the climb/descent reached stay. 'Final TRK + GP': back parallel to the centreline and the glide path.";
            pad.MouseLeftButtonDown += (s, e) =>
            {
                pad.CaptureMouse();
                MoveKnob(e.GetPosition(pad));
                e.Handled = true;
            };
            pad.MouseMove += (s, e) =>
            {
                if (pad.IsMouseCaptured) MoveKnob(e.GetPosition(pad));
            };
            pad.MouseLeftButtonUp += (s, e) => ReleaseKnob();
            pad.LostMouseCapture += (s, e) => ReleaseKnob();
            Grid.SetColumn(padBorder, 0);
            padArea.Children.Add(padBorder);

            // Quick buttons.
            WrapPanel quick = new();
            foreach ((string text, Action<TestTraffic.Plane> action) in new (string, Action<TestTraffic.Plane>)[]
            {
                ("On GP / CL", p => { p.Lateral = 0; p.HeightOffset = 0; p.LateralRate = 0; p.HeightRate = 0; }),
                ("Final TRK + GP", p => { p.LateralRate = 0; p.HeightRate = 0; }),
                ("+200 ft", p => { p.HeightOffset += 200; p.Auto = false; }),
                ("−200 ft", p => { p.HeightOffset -= 200; p.Auto = false; }),
                ("Left 300 m", p => { p.Lateral -= 300 / 1852.0; p.Auto = false; }),
                ("Right 300 m", p => { p.Lateral += 300 / 1852.0; p.Auto = false; })
            })
            {
                Button button = new() { Content = text, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
                button.Click += (s, e) => { ChangeSelected(action); ShowSelected(); };
                quick.Children.Add(button);
            }
            pauseButton.Click += (s, e) =>
            {
                traffic.Paused = !traffic.Paused;
                pauseButton.Content = traffic.Paused ? "Resume" : "Pause";
            };
            quick.Children.Add(pauseButton);
            Button remove = new() { Content = "Remove", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (s, e) =>
            {
                if (list.SelectedItem is string callsign) traffic.Remove(callsign);
                RefreshList();
            };
            quick.Children.Add(remove);
            Button removeAll = new() { Content = "Remove all", Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2) };
            removeAll.Click += (s, e) =>
            {
                traffic.Clear();
                RefreshList();
            };
            quick.Children.Add(removeAll);
            Grid.SetColumn(quick, 1);
            padArea.Children.Add(quick);
            root.Children.Add(padArea);

            root.Children.Add(auroraCheck);
            auroraCheck.IsChecked = traffic.AuroraLike;
            auroraCheck.Click += (s, e) => traffic.AuroraLike = auroraCheck.IsChecked == true;
            root.Children.Add(status);
            Content = root;

            refresh.Tick += (s, e) => RefreshList();
            refresh.Start();
            Closed += (s, e) =>
            {
                refresh.Stop();
                traffic.Clear();
                traffic.Paused = false;
            };
            RefreshList();
        }

        private static TextBox NumberBox(string text) => new()
        {
            Text = text,
            Width = 42,
            Height = 22,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        private static TextBlock Caption(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };

        private static TextBlock PadText(string text, double x, double y)
        {
            TextBlock block = new() { Text = text, Foreground = Brushes.Gray, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            return block;
        }

        private static bool TryNumber(TextBox box, double min, double max, out double value)
        {
            return double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value >= min && value <= max;
        }

        private void AddPlane()
        {
            if (!TryNumber(distanceBox, 0.5, 40, out double distance) || !TryNumber(speedBox, 40, 400, out double speed))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            string code = squawkBox.Text.Trim();
            string? squawk = code.Length == 4 && code.All(c => c >= '0' && c <= '7') && code != "0000" ? code : null;
            string callsign = traffic.Add(distance, speed, squawk);
            RefreshList();
            list.SelectedItem = callsign;
        }

        private void ChangeSelected(Action<TestTraffic.Plane> change)
        {
            if (list.SelectedItem is string callsign) traffic.Change(callsign, change);
        }

        /// <summary>The list follows the aircraft (landed ones go away); the selection stays.</summary>
        private void RefreshList()
        {
            List<string> callsigns = traffic.List().Select(p => p.Callsign).ToList();
            if (list.ItemsSource is not List<string> current || !current.SequenceEqual(callsigns))
            {
                string? selected = list.SelectedItem as string;
                updatingList = true;
                list.ItemsSource = callsigns;
                list.SelectedItem = selected != null && callsigns.Contains(selected) ? selected : callsigns.LastOrDefault();
                updatingList = false;
            }
            ShowSelected();
        }

        private void ShowSelected()
        {
            TestTraffic.Plane? plane = traffic.List().FirstOrDefault(p => p.Callsign == list.SelectedItem as string);
            if (plane == null)
            {
                status.Text = traffic.Count == 0 ? "No test aircraft: Add one." : "";
                return;
            }
            autoCheck.IsChecked = plane.Auto;
            double metres = plane.Lateral * 1852;
            double headingOff = Math.Atan2(plane.LateralRate * 3600, Math.Max(1, plane.Speed)) * 180 / Math.PI;
            status.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}: {1:0.0} NM from touchdown, {2} {3:0} m, {4} {5:0} ft, {6:0} kt\nHeading {7:+0.0;-0.0;0.0}° off the centreline, {8:+0;-0;0} ft/min from the glide path",
                plane.Callsign, plane.Distance, metres >= 0 ? "R" : "L", Math.Abs(metres),
                plane.HeightOffset >= 0 ? "above" : "below", Math.Abs(plane.HeightOffset), plane.Speed,
                headingOff, plane.HeightRate * 60);
        }

        private void CentreKnob()
        {
            Canvas.SetLeft(knob, PadSize / 2 - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 - KnobSize / 2);
        }

        /// <summary>Joystick moved: rates proportional to the distance from the centre (inside a circle).</summary>
        private void MoveKnob(Point point)
        {
            if (list.SelectedItem is not string) return;
            double dx = point.X - PadSize / 2;
            double dy = point.Y - PadSize / 2;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > PadReach)
            {
                dx *= PadReach / length;
                dy *= PadReach / length;
            }
            Canvas.SetLeft(knob, PadSize / 2 + dx - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 + dy - KnobSize / 2);
            double x = dx / PadReach;
            double y = -dy / PadReach;
            ChangeSelected(p =>
            {
                p.StickX = x;
                p.StickY = y;
                p.Auto = false;
            });
            autoCheck.IsChecked = false;
        }

        private void ReleaseKnob()
        {
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
            ChangeSelected(p =>
            {
                // Heading and climb/descent reached stay.
                p.StickX = 0;
                p.StickY = 0;
            });
        }
    }
}
