using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuroraPAR
{
    /// <summary>
    /// Window of the test traffic (key T). Top: new aircraft (distance, speed, SSR, initial offsets) and the list.
    /// Control: the readouts the instructor needs while flying (best vertical speed of the glide path, actual vertical
    /// speed, actual turn rate), the turn rate (1.5°/s, 3°/s, Free), the vertical buttons (less descent / Optimal GP /
    /// more descent, in steps aligned on the best vertical speed) beside the stick, Final CRS under it. Closing the
    /// window removes the test traffic.
    /// </summary>
    internal sealed class TestTrafficWindow : Window
    {
        private const double PadSize = 150;
        private const double KnobSize = 22;
        private const double PadReach = (PadSize - KnobSize) / 2;
        private static readonly Brush ReadoutBack = Frozen(Color.FromRgb(0x1B, 0x1C, 0x19));
        private static readonly Brush ReadoutText = Frozen(Color.FromRgb(0xFF, 0xB8, 0x40));
        private static readonly Brush ReadoutLabel = Frozen(Color.FromRgb(0x9A, 0x9A, 0x90));
        private static readonly Brush SelectedBack = Frozen(Color.FromRgb(0x00, 0x78, 0xD4));

        private readonly TestTraffic traffic;
        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox speedBox = NumberBox("140");
        private readonly TextBox squawkBox = NumberBox("7001");
        private readonly TextBox lateralBox = NumberBox("0");
        private readonly TextBox heightBox = NumberBox("0");
        private readonly ListBox list = new() { Height = 64, Width = 110 };
        private readonly TextBlock info = new() { Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock bestLabel = ReadoutCaption("BEST VS");
        private readonly TextBlock bestValue = ReadoutNumber();
        private readonly TextBlock actualValue = ReadoutNumber();
        private readonly TextBlock turnValue = ReadoutNumber();
        private readonly Button[] turnButtons = new Button[3];
        private readonly CheckBox autoCheck = new() { Content = "Auto (intercepts centreline and GP)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        private readonly CheckBox auroraCheck = new()
        {
            Content = "Aurora-like data (positions every 0.5 s, altitude in steps)",
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "As the real data from Aurora, to test the track smoothing. Off: perfect positions."
        };
        private readonly Button pauseButton = SmallButton("Pause");
        private readonly Canvas pad = new() { Width = PadSize, Height = PadSize, Background = Frozen(Color.FromRgb(0x22, 0x24, 0x22)), Cursor = Cursors.Hand };
        private readonly Ellipse knob = new() { Width = KnobSize, Height = KnobSize, Fill = Frozen(Color.FromRgb(0x70, 0xD0, 0x70)), Stroke = Brushes.Black, IsHitTestVisible = false };
        private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(0.25) };
        private bool updatingList;

        internal TestTrafficWindow(TestTraffic traffic)
        {
            this.traffic = traffic;
            Title = "Aurora PAR - Test traffic";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 400 };

            // New aircraft.
            StackPanel newPanel = new();
            WrapPanel first = new();
            first.Children.Add(Caption("Distance"));
            first.Children.Add(distanceBox);
            first.Children.Add(Caption("NM   Speed"));
            first.Children.Add(speedBox);
            first.Children.Add(Caption("kt   SSR A"));
            first.Children.Add(squawkBox);
            newPanel.Children.Add(first);
            DockPanel second = new() { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
            Button addButton = new() { Content = "Add", Padding = new Thickness(14, 2, 14, 2), FontWeight = FontWeights.SemiBold };
            addButton.Click += (s, e) => AddPlane();
            DockPanel.SetDock(addButton, Dock.Right);
            second.Children.Add(addButton);
            StackPanel offsets = new() { Orientation = Orientation.Horizontal };
            offsets.Children.Add(Caption("Offset"));
            offsets.Children.Add(lateralBox);
            offsets.Children.Add(Caption("m (+R/−L)"));
            offsets.Children.Add(heightBox);
            offsets.Children.Add(Caption("ft (+above/−below GP)"));
            second.Children.Add(offsets);
            newPanel.Children.Add(second);
            root.Children.Add(Group("New aircraft", newPanel));

            // Aircraft.
            StackPanel aircraftPanel = new() { Orientation = Orientation.Horizontal };
            aircraftPanel.Children.Add(list);
            aircraftPanel.Children.Add(info);
            list.SelectionChanged += (s, e) =>
            {
                if (!updatingList) ShowSelected();
            };
            root.Children.Add(Group("Aircraft", aircraftPanel));

            // Control.
            StackPanel control = new();
            Grid readouts = new() { Background = ReadoutBack, Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < 3; i++) readouts.ColumnDefinitions.Add(new ColumnDefinition());
            AddReadout(readouts, 0, bestLabel, bestValue, "ft/min");
            AddReadout(readouts, 1, ReadoutCaption("ACTUAL VS"), actualValue, "ft/min");
            AddReadout(readouts, 2, ReadoutCaption("TURN"), turnValue, "actual");
            control.Children.Add(readouts);

            // Turn rate keys over the stick, as wide as it.
            UniformGrid turnRow = new() { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
            (string Text, TestTurnMode Mode, string Tip)[] modes =
            [
                ("1.5°/s", TestTurnMode.Half, "Half rate: 1.5°/s with the stick at its edge."),
                ("3°/s", TestTurnMode.Standard, "Rate one: 3°/s with the stick at its edge."),
                ("Free", TestTurnMode.Free, "The longer the stick is held at its edge, the faster the turn (up to 10°/s).")
            ];
            for (int i = 0; i < modes.Length; i++)
            {
                TestTurnMode mode = modes[i].Mode;
                Button button = new() { Content = modes[i].Text, ToolTip = modes[i].Tip, Padding = new Thickness(2, 2, 2, 2), Margin = new Thickness(1, 0, 1, 0) };
                button.Click += (s, e) =>
                {
                    traffic.TurnMode = mode;
                    ShowTurnMode();
                };
                turnButtons[i] = button;
                turnRow.Children.Add(button);
            }
            // Stick in the middle column: turn rate keys over it, turn keys and Final CRS under it (as wide as it),
            // vertical speed keys on its left.
            Grid stickArea = new() { HorizontalAlignment = HorizontalAlignment.Center };
            stickArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            stickArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PadSize + 2) });
            stickArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stickArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(PadSize + 2) });
            stickArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock turnLabel = new() { Text = "Turn rate:", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 6) };
            stickArea.Children.Add(turnLabel);
            Grid.SetColumn(turnRow, 1);
            stickArea.Children.Add(turnRow);
            StackPanel vertical = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Button less = WideButton("− rate of desc.", "Reduce rate of descent: 100 ft/min less, on the steps through the best vertical speed.");
            less.Click += (s, e) => ChangeSelected(p =>
            {
                p.HoldGlidePath = false;
                p.Auto = false;
                p.VerticalSpeed = TestTraffic.StepVerticalSpeed(p.VerticalSpeed, p.BestVerticalSpeed, up: true);
            });
            Button optimal = WideButton("= rate of desc.", "Resume normal rate of descent: the vertical speed of the glide path for this speed, kept also when the speed or course change. The offset from the glide path stays.");
            optimal.FontWeight = FontWeights.SemiBold;
            optimal.Click += (s, e) => ChangeSelected(p =>
            {
                p.HoldGlidePath = true;
                p.Auto = false;
            });
            Button more = WideButton("+ rate of desc.", "Increase rate of descent: 100 ft/min more, on the steps through the best vertical speed.");
            more.Click += (s, e) => ChangeSelected(p =>
            {
                p.HoldGlidePath = false;
                p.Auto = false;
                p.VerticalSpeed = TestTraffic.StepVerticalSpeed(p.VerticalSpeed, p.BestVerticalSpeed, up: false);
            });
            vertical.Children.Add(less);
            vertical.Children.Add(optimal);
            vertical.Children.Add(more);
            Grid.SetColumn(vertical, 0);
            Grid.SetRow(vertical, 1);
            stickArea.Children.Add(vertical);

            Border padBorder = new() { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = pad };
            pad.Children.Add(new Line { X1 = PadSize / 2, Y1 = 4, X2 = PadSize / 2, Y2 = PadSize - 4, Stroke = Brushes.DimGray });
            pad.Children.Add(new Line { X1 = 4, Y1 = PadSize / 2, X2 = PadSize - 4, Y2 = PadSize / 2, Stroke = Brushes.DimGray });
            pad.Children.Add(PadText("UP", PadSize / 2 - 8, 2));
            pad.Children.Add(PadText("DN", PadSize / 2 - 8, PadSize - 16));
            pad.Children.Add(PadText("L", 4, PadSize / 2 - 16));
            pad.Children.Add(PadText("R", PadSize - 12, PadSize / 2 - 16));
            pad.Children.Add(knob);
            CentreKnob();
            pad.ToolTip = "Control stick: hold left/right to turn the selected aircraft at the turn rate chosen above, up/down to change its vertical speed. Released, the course and the vertical speed reached stay.";
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
            Grid.SetColumn(padBorder, 1);
            Grid.SetRow(padBorder, 1);
            stickArea.Children.Add(padBorder);

            Button finalCourse = new()
            {
                Content = "Final CRS",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(14, 3, 14, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = "Turns back to the final course at the turn rate chosen (3°/s with Free). The offset from the centreline stays."
            };
            finalCourse.Click += (s, e) => ChangeSelected(p =>
            {
                p.BackToFinal = true;
                p.Auto = false;
            });
            finalCourse.Margin = new Thickness(2, 0, 2, 0);
            finalCourse.Padding = new Thickness(2, 3, 2, 3);
            finalCourse.HorizontalAlignment = HorizontalAlignment.Stretch;
            // Turn keys: held, as the stick at its edge (left or right); a short click turns for at least 1 s.
            Grid lateral = new() { Margin = new Thickness(0, 6, 0, 0) };
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            Button left = TurnKey("◀ L", -1);
            Button rightKey = TurnKey("R ▶", 1);
            Grid.SetColumn(finalCourse, 1);
            Grid.SetColumn(rightKey, 2);
            lateral.Children.Add(left);
            lateral.Children.Add(finalCourse);
            lateral.Children.Add(rightKey);
            Grid.SetColumn(lateral, 1);
            Grid.SetRow(lateral, 2);
            stickArea.Children.Add(lateral);
            control.Children.Add(stickArea);
            root.Children.Add(Group("Control", control));

            // Service buttons.
            WrapPanel service = new() { Margin = new Thickness(0, 2, 0, 0) };
            Button onPath = SmallButton("On GP / CL");
            onPath.ToolTip = "Puts the aircraft exactly on the centreline and the glide path, on the final course, at once.";
            onPath.Click += (s, e) => ChangeSelected(p =>
            {
                p.Lateral = 0;
                p.Height = double.NaN;
                p.VerticalSpeed = 0;
                p.Course = 0;
                p.TurnRate = 0;
                p.BackToFinal = false;
                p.HoldGlidePath = true;
            });
            service.Children.Add(onPath);
            autoCheck.Click += (s, e) => ChangeSelected(p =>
            {
                p.Auto = autoCheck.IsChecked == true;
                p.BackToFinal = false;
            });
            service.Children.Add(autoCheck);
            pauseButton.Click += (s, e) =>
            {
                traffic.Paused = !traffic.Paused;
                pauseButton.Content = traffic.Paused ? "Resume" : "Pause";
            };
            service.Children.Add(pauseButton);
            Button remove = SmallButton("Remove");
            remove.Click += (s, e) =>
            {
                if (list.SelectedItem is string callsign) traffic.Remove(callsign);
                RefreshList();
            };
            service.Children.Add(remove);
            Button removeAll = SmallButton("Remove all");
            removeAll.Click += (s, e) =>
            {
                traffic.Clear();
                RefreshList();
            };
            service.Children.Add(removeAll);
            root.Children.Add(service);

            root.Children.Add(auroraCheck);
            auroraCheck.IsChecked = traffic.AuroraLike;
            auroraCheck.Click += (s, e) => traffic.AuroraLike = auroraCheck.IsChecked == true;
            Content = root;

            ShowTurnMode();
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

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new(color);
            brush.Freeze();
            return brush;
        }

        private static GroupBox Group(string header, UIElement content) => new()
        {
            Header = header,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = content
        };

        private static TextBox NumberBox(string text) => new()
        {
            Text = text,
            Width = 42,
            Height = 22,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        /// <summary>Shortest turn of a click on a turn key (seconds at full stick).</summary>
        private const double TurnKeySeconds = 1;

        /// <summary>
        /// Key that turns the selected aircraft as the stick at its edge while held (left −1, right +1); a short click
        /// still turns for <see cref="TurnKeySeconds"/>. Released, the course reached stays.
        /// </summary>
        private Button TurnKey(string text, int side)
        {
            Button key = new()
            {
                Content = text,
                Padding = new Thickness(2, 3, 2, 3),
                ToolTip = $"Turn {(side < 0 ? "left" : "right")} at the turn rate chosen while held (a click: at least 1 s). Released, the course reached stays."
            };
            DateTime pressed = DateTime.MinValue;
            DispatcherTimer stop = new();
            stop.Tick += (s, e) =>
            {
                stop.Stop();
                if (!key.IsPressed) SetStickX(0);
            };
            key.PreviewMouseLeftButtonDown += (s, e) =>
            {
                pressed = DateTime.UtcNow;
                stop.Stop();
                SetStickX(side);
            };
            void Release()
            {
                if (pressed == DateTime.MinValue) return;
                double held = (DateTime.UtcNow - pressed).TotalSeconds;
                pressed = DateTime.MinValue;
                if (held >= TurnKeySeconds)
                {
                    SetStickX(0);
                }
                else
                {
                    stop.Interval = TimeSpan.FromSeconds(TurnKeySeconds - held);
                    stop.Start();
                }
            }
            key.PreviewMouseLeftButtonUp += (s, e) => Release();
            key.LostMouseCapture += (s, e) => Release();
            return key;
        }

        private void SetStickX(double x) => ChangeSelected(p =>
        {
            p.StickX = x;
            if (x != 0)
            {
                p.Auto = false;
                p.BackToFinal = false;
            }
            else
            {
                p.StickHeld = 0;
            }
        });

        private static Button SmallButton(string text) => new() { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 2, 8, 2) };

        private static Button WideButton(string text, string tip) => new()
        {
            Content = text,
            ToolTip = tip,
            Height = 28,
            Margin = new Thickness(0, 3, 0, 3)
        };

        private static TextBlock Caption(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };

        private static TextBlock ReadoutCaption(string text) => new()
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = ReadoutLabel,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static TextBlock ReadoutNumber() => new()
        {
            Text = "---",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = ReadoutText,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static void AddReadout(Grid grid, int column, TextBlock caption, TextBlock value, string unit)
        {
            StackPanel cell = new() { Margin = new Thickness(4, 6, 4, 6) };
            cell.Children.Add(caption);
            cell.Children.Add(value);
            cell.Children.Add(new TextBlock { Text = unit, FontSize = 10, Foreground = ReadoutLabel, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        private static TextBlock PadText(string text, double x, double y)
        {
            TextBlock block = new() { Text = text, Foreground = Brushes.Gray, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            return block;
        }

        private static bool TryNumber(TextBox box, double min, double max, out double value)
        {
            return double.TryParse(box.Text.Trim().Replace(',', '.').Replace('−', '-'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value >= min && value <= max;
        }

        private void AddPlane()
        {
            if (!TryNumber(distanceBox, 0.5, 40, out double distance) || !TryNumber(speedBox, 40, 400, out double speed)
                || !TryNumber(lateralBox, -10000, 10000, out double lateral) || !TryNumber(heightBox, -3000, 5000, out double height))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            string code = squawkBox.Text.Trim();
            string? squawk = code.Length == 4 && code.All(c => c >= '0' && c <= '7') && code != "0000" ? code : null;
            string callsign = traffic.Add(distance, speed, squawk, lateral, height);
            RefreshList();
            list.SelectedItem = callsign;
        }

        private void ChangeSelected(Action<TestTraffic.Plane> change)
        {
            if (list.SelectedItem is string callsign) traffic.Change(callsign, change);
        }

        private void ShowTurnMode()
        {
            TestTurnMode[] modes = [TestTurnMode.Half, TestTurnMode.Standard, TestTurnMode.Free];
            for (int i = 0; i < turnButtons.Length; i++)
            {
                bool on = traffic.TurnMode == modes[i];
                if (on)
                {
                    turnButtons[i].Background = SelectedBack;
                    turnButtons[i].Foreground = Brushes.White;
                }
                else
                {
                    turnButtons[i].ClearValue(BackgroundProperty);
                    turnButtons[i].ClearValue(ForegroundProperty);
                }
            }
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
                info.Text = traffic.Count == 0 ? "No test aircraft:\nAdd one above." : "";
                bestValue.Text = actualValue.Text = turnValue.Text = "---";
                bestLabel.Text = "BEST VS";
                return;
            }
            info.Text = string.Format(CultureInfo.InvariantCulture, "{0}\n{1:0.0} NM from touchdown\n{2:0} kt{3}",
                plane.Callsign, plane.Distance, plane.Speed, plane.Squawk != null ? " · SSR A" + plane.Squawk : "");
            bestLabel.Text = string.Format(CultureInfo.InvariantCulture, "BEST VS (GP {0:0.0#}°)", plane.GlideSlope);
            bestValue.Text = Rounded(plane.BestVerticalSpeed);
            actualValue.Text = Rounded(plane.VerticalSpeed);
            turnValue.Text = Math.Abs(plane.TurnRate) < 0.05 ? "0.0°/s"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0}°/s {1}", Math.Abs(plane.TurnRate), plane.TurnRate > 0 ? "R" : "L");
            if (autoCheck.IsChecked != plane.Auto) autoCheck.IsChecked = plane.Auto;
        }

        private static string Rounded(double feetPerMinute)
        {
            double value = Math.Round(feetPerMinute / 10) * 10;
            return value.ToString("+0;−0;0", CultureInfo.InvariantCulture);
        }

        private void CentreKnob()
        {
            Canvas.SetLeft(knob, PadSize / 2 - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 - KnobSize / 2);
        }

        /// <summary>Stick moved: X turns, Y changes the vertical speed, proportionally (inside a circle).</summary>
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
            // A small dead zone in the middle, so a click on the centre does nothing.
            if (Math.Abs(x) < 0.08) x = 0;
            if (Math.Abs(y) < 0.08) y = 0;
            ChangeSelected(p =>
            {
                p.StickX = x;
                p.StickY = y;
                p.Auto = false;
                if (x != 0) p.BackToFinal = false;
                if (y != 0) p.HoldGlidePath = false;
            });
        }

        private void ReleaseKnob()
        {
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
            // The course and the vertical speed reached stay.
            ChangeSelected(p =>
            {
                p.StickX = 0;
                p.StickY = 0;
                p.StickHeld = 0;
            });
        }
    }
}
