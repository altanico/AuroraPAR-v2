using System.Globalization;
using System.Text.RegularExpressions;
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
    /// Window of the test traffic (key T). Top: new aircraft (distance, speed, SSR, initial offsets), the wind and the
    /// list. Control: the readouts the instructor needs while flying (best vertical speed of the glide path, actual
    /// vertical speed, actual turn rate, heading, drift), the turn rate (1.5°/s, 3°/s, Free), the rate of descent keys
    /// (reduce / normal / increase, in steps aligned on the best vertical speed) beside the stick, ▲ UP / ▼ DN and
    /// ◀ L / Final CRS / R ▶ under it, and the heading given by the controller. Closing the window removes the test
    /// traffic.
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
        /// <summary>Wind in a METAR: 27015KT, 27015G25KT, VRB03KT, 27008MPS.</summary>
        private static readonly Regex MetarWind = new(@"\b(\d{3}|VRB)(\d{2,3})(?:G(\d{2,3}))?(KT|MPS|KMH)\b");

        private readonly TestTraffic traffic;
        private readonly Func<double> variation;
        private readonly Func<string?> metar;
        private readonly TextBox distanceBox = NumberBox("10");
        private readonly TextBox speedBox = NumberBox("140");
        private readonly TextBox squawkBox = NumberBox("7001");
        private readonly TextBox lateralBox = NumberBox("0");
        private readonly TextBox heightBox = NumberBox("0");
        private readonly TextBox windFromBox = NumberBox("0");
        private readonly TextBox windSpeedBox = NumberBox("0");
        private readonly TextBox windGustBox = NumberBox("0");
        private readonly TextBlock windMagnetic = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 4, 0) };
        private readonly TextBox headingBox = NumberBox("");
        private readonly TextBox reactionBox = NumberBox("0.5");
        /// <summary>Pilot reaction: the heading instruction is carried out this long after it is given.</summary>
        private readonly DispatcherTimer reaction = new();
        private int pendingSide;
        private string? pendingCallsign;
        private readonly ListBox list = new() { Height = 64, Width = 110 };
        private readonly TextBlock info = new() { Margin = new Thickness(10, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock bestLabel = ReadoutCaption("BEST VS");
        private readonly TextBlock bestValue = ReadoutNumber();
        private readonly TextBlock actualValue = ReadoutNumber();
        private readonly TextBlock turnValue = ReadoutNumber();
        private readonly TextBlock headingValue = ReadoutNumber();
        private readonly TextBlock driftValue = ReadoutNumber();
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
        // Deflection given by the keys ◀ L / R ▶ / ▲ UP / ▼ DN (the knob shows it).
        private double keyX;
        private double keyY;

        internal TestTrafficWindow(TestTraffic traffic, Action<Window>? openJoystick = null, Func<double>? variation = null, Func<string?>? metar = null)
        {
            this.traffic = traffic;
            this.variation = variation ?? (() => 0);
            this.metar = metar ?? (() => null);
            Title = "Aurora PAR - Test traffic";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            StackPanel root = new() { Margin = new Thickness(10), Width = 430 };

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

            // Wind (all the test aircraft).
            WrapPanel windPanel = new();
            windPanel.Children.Add(Caption("From"));
            windPanel.Children.Add(windFromBox);
            windPanel.Children.Add(Caption("°T"));
            windPanel.Children.Add(windMagnetic);
            windPanel.Children.Add(windSpeedBox);
            windPanel.Children.Add(Caption("kt   gusts +"));
            windPanel.Children.Add(windGustBox);
            windPanel.Children.Add(Caption("kt"));
            Button fromMetar = new() { Content = "From METAR", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), ToolTip = "Copies the wind of the METAR of the airport in use (received from Aurora). A METAR gives the direction in degrees true." };
            fromMetar.Click += (s, e) => WindFromMetar();
            windPanel.Children.Add(fromMetar);
            windFromBox.Text = traffic.WindFrom.ToString("000", CultureInfo.InvariantCulture);
            windSpeedBox.Text = traffic.WindSpeed.ToString("0", CultureInfo.InvariantCulture);
            windGustBox.Text = traffic.WindGust.ToString("0", CultureInfo.InvariantCulture);
            foreach (TextBox box in new[] { windFromBox, windSpeedBox, windGustBox }) box.TextChanged += (s, e) => ApplyWind();
            GroupBox windGroup = Group("Wind (rough: the same at all heights)", windPanel);
            windGroup.ToolTip = "The aircraft fly their heading through the air: a crosswind makes them drift off the centreline unless the heading is corrected, a headwind lowers the ground speed and so the rate of descent on the glide path. Gusts: the wind grows at random up to this much more.";
            root.Children.Add(windGroup);

            // Aircraft.
            StackPanel aircraftPanel = new() { Orientation = Orientation.Horizontal };
            aircraftPanel.Children.Add(list);
            aircraftPanel.Children.Add(info);
            list.SelectionChanged += (s, e) =>
            {
                // Keys or stick held on the previous aircraft: let it go. The joystick held over goes to the aircraft now selected.
                ReleaseManual();
                ApplyJoystick();
                if (!updatingList) ShowSelected();
            };
            root.Children.Add(Group("Aircraft", aircraftPanel));

            // Control.
            StackPanel control = new();
            Grid readouts = new() { Background = ReadoutBack, Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < 5; i++) readouts.ColumnDefinitions.Add(new ColumnDefinition());
            AddReadout(readouts, 0, bestLabel, bestValue, "ft/min");
            AddReadout(readouts, 1, ReadoutCaption("ACTUAL VS"), actualValue, "ft/min");
            AddReadout(readouts, 2, ReadoutCaption("TURN"), turnValue, "°/s");
            AddReadout(readouts, 3, ReadoutCaption("HDG"), headingValue, "°M");
            AddReadout(readouts, 4, ReadoutCaption("DRIFT"), driftValue, "° (wind)");
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
            // Stick in the middle column: turn rate keys over it; ▲ UP, ▼ DN and ◀ L / Final CRS / R ▶ under it, as
            // wide as it; the rate of descent keys on its left.
            Grid stickArea = new() { HorizontalAlignment = HorizontalAlignment.Center };
            stickArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            stickArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PadSize + 2) });
            for (int i = 0; i < 5; i++) stickArea.RowDefinitions.Add(new RowDefinition { Height = i == 1 ? new GridLength(PadSize + 2) : GridLength.Auto });
            TextBlock turnLabel = new() { Text = "Turn rate:", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 6) };
            stickArea.Children.Add(turnLabel);
            Grid.SetColumn(turnRow, 1);
            stickArea.Children.Add(turnRow);
            StackPanel vertical = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Button less = WideButton("Reduce rate of desc.", "Reduce rate of descent: 100 ft/min less, on the steps through the best vertical speed.");
            less.Click += (s, e) => StepRate(up: true);
            Button optimal = WideButton("Normal rate of desc.", "Resume normal rate of descent: the vertical speed of the glide path for this ground speed, kept also when the speed, heading or wind change. The offset from the glide path stays.");
            optimal.FontWeight = FontWeights.SemiBold;
            optimal.Click += (s, e) => NormalRate();
            Button more = WideButton("Increase rate of desc.", "Increase rate of descent: 100 ft/min more, on the steps through the best vertical speed.");
            more.Click += (s, e) => StepRate(up: false);
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
            pad.ToolTip = "Control stick: hold left/right to turn the selected aircraft at the turn rate chosen above, up/down to change its vertical speed. Released, the heading and the vertical speed reached stay.";
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

            // ▲ UP right above ▼ DN, under the stick: held = stick at its top / bottom edge.
            Button up = StickKey("▲ UP", 0, 1, "Climb / reduce the descent as the stick at its top edge while held (a click: at least 1 s). Released, the vertical speed reached stays.");
            up.Margin = new Thickness(2, 6, 2, 0);
            Grid.SetColumn(up, 1);
            Grid.SetRow(up, 2);
            stickArea.Children.Add(up);
            Button down = StickKey("▼ DN", 0, -1, "Increase the descent as the stick at its bottom edge while held (a click: at least 1 s). Released, the vertical speed reached stays.");
            down.Margin = new Thickness(2, 2, 2, 0);
            Grid.SetColumn(down, 1);
            Grid.SetRow(down, 3);
            stickArea.Children.Add(down);

            Button finalCourse = new()
            {
                Content = "Final CRS",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(2, 3, 2, 3),
                Margin = new Thickness(2, 0, 2, 0),
                ToolTip = "Turns the heading back to the final course at the turn rate chosen (3°/s with Free). With a crosswind the aircraft then drifts: correct the heading. The offset from the centreline stays."
            };
            finalCourse.Click += (s, e) => FinalCourse();
            // Turn keys: held, as the stick at its edge (left or right); a short click turns for at least 1 s.
            Grid lateral = new() { Margin = new Thickness(0, 6, 0, 0) };
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lateral.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            Button left = StickKey("◀ L", -1, 0, "Turn left at the turn rate chosen while held (a click: at least 1 s). Released, the heading reached stays.");
            Button rightKey = StickKey("R ▶", 1, 0, "Turn right at the turn rate chosen while held (a click: at least 1 s). Released, the heading reached stays.");
            Grid.SetColumn(finalCourse, 1);
            Grid.SetColumn(rightKey, 2);
            lateral.Children.Add(left);
            lateral.Children.Add(finalCourse);
            lateral.Children.Add(rightKey);
            Grid.SetColumn(lateral, 1);
            Grid.SetRow(lateral, 4);
            stickArea.Children.Add(lateral);
            control.Children.Add(stickArea);

            // Heading given by the controller.
            StackPanel headingRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
            headingRow.Children.Add(Caption("Heading"));
            headingBox.MaxLength = 3;
            headingBox.ToolTip = "Heading given by the controller, magnetic (e.g. 275). Enter: turn the shortest way. Mouse wheel: 1° per step (Shift: 10°), the aircraft turns the shortest way after the pilot reaction.";
            headingBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    GiveHeading(0);
                    e.Handled = true;
                }
            };
            headingBox.PreviewMouseWheel += (s, e) =>
            {
                WheelHeading(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            };
            reaction.Tick += (s, e) =>
            {
                reaction.Stop();
                TurnToHeading(pendingSide, pendingCallsign);
            };
            headingRow.Children.Add(headingBox);
            headingRow.Children.Add(Caption("°M"));
            foreach ((string text, int side, string tip) in new[]
            {
                ("Turn L", -1, "Turn left to the heading."),
                ("HDG", 0, "Turn to the heading, the shortest way."),
                ("Turn R", 1, "Turn right to the heading.")
            })
            {
                Button button = new() { Content = text, ToolTip = tip + " At the turn rate chosen (3°/s with Free); the stick cancels it.", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
                button.Click += (s, e) => GiveHeading(side);
                headingRow.Children.Add(button);
            }
            control.Children.Add(headingRow);
            StackPanel reactionRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            reactionRow.ToolTip = "Pilots are not instantaneous: a heading given (box, wheel, Turn L / HDG / Turn R) is carried out this long after it is given (0 to 5 s).";
            reactionRow.Children.Add(Caption("Pilot reaction to the heading:"));
            reactionRow.Children.Add(reactionBox);
            reactionRow.Children.Add(Caption("s"));
            control.Children.Add(reactionRow);
            root.Children.Add(Group("Control", control));

            // Service buttons.
            WrapPanel service = new() { Margin = new Thickness(0, 2, 0, 0) };
            Button onPath = SmallButton("On GP / CL");
            onPath.ToolTip = "Puts the aircraft exactly on the centreline and the glide path at once, with the heading that holds the final course in this wind.";
            onPath.Click += (s, e) => OnPath();
            service.Children.Add(onPath);
            autoCheck.Click += (s, e) => ChangeSelected(p =>
            {
                p.Auto = autoCheck.IsChecked == true;
                p.TargetHeading = double.NaN;
            });
            service.Children.Add(autoCheck);
            pauseButton.Click += (s, e) => TogglePause();
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
            Button joystickButton = SmallButton("Joystick...");
            joystickButton.ToolTip = "A real joystick or gamepad for the test aircraft and the antenna tilt: device, buttons, live test.";
            joystickButton.Click += (s, e) => openJoystick?.Invoke(this);
            if (openJoystick != null) service.Children.Add(joystickButton);
            root.Children.Add(service);

            root.Children.Add(auroraCheck);
            auroraCheck.IsChecked = traffic.AuroraLike;
            auroraCheck.Click += (s, e) => traffic.AuroraLike = auroraCheck.IsChecked == true;
            Content = root;

            ShowTurnMode();
            ApplyWind();
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

        /// <summary>Shortest action of a click on ▲ UP / ▼ DN (seconds at full stick).</summary>
        private const double StickKeySeconds = 1;
        /// <summary>A press of ◀ L / R ▶ shorter than this is a click: the heading changes by <see cref="TurnClickDegrees"/>.</summary>
        private const double TurnClickSeconds = 0.35;
        private const double TurnClickDegrees = 1;

        /// <summary>
        /// Key that acts as the stick at one of its edges while held (x: left −1 / right +1, or y: down −1 / up +1); a
        /// short click still acts for <see cref="StickKeySeconds"/>. The knob of the stick moves with it. Released,
        /// the heading and vertical speed reached stay.
        /// </summary>
        private Button StickKey(string text, int x, int y, string tip)
        {
            Button key = new() { Content = text, Padding = new Thickness(2, 3, 2, 3), ToolTip = tip };
            DateTime pressed = DateTime.MinValue;
            DispatcherTimer stop = new();
            // Turn keys: heading when pressed, so that a click turns exactly 1°.
            double pressHeading = double.NaN;
            string? pressCallsign = null;
            void Set(int value)
            {
                if (x != 0) SetKeyStick(value * x, keyY);
                else SetKeyStick(keyX, value * y);
            }
            stop.Tick += (s, e) =>
            {
                stop.Stop();
                if (key.IsPressed) return;
                if (x != 0)
                {
                    // End of the short show of a click: the knob back to the centre.
                    if (keyX == 0 && keyY == 0 && joystickCallsign == null && !pad.IsMouseCaptured) CentreKnob();
                }
                else
                {
                    Set(0);
                }
            };
            key.PreviewMouseLeftButtonDown += (s, e) =>
            {
                pressed = DateTime.UtcNow;
                stop.Stop();
                pressCallsign = list.SelectedItem as string;
                pressHeading = double.NaN;
                // Several quick clicks add up: from the heading still being reached, if any.
                if (pressCallsign != null) traffic.Change(pressCallsign, p => pressHeading = double.IsNaN(p.TargetHeading) ? p.Heading : p.TargetHeading);
                Set(1);
            };
            void Release()
            {
                if (pressed == DateTime.MinValue) return;
                double held = (DateTime.UtcNow - pressed).TotalSeconds;
                pressed = DateTime.MinValue;
                if (x != 0 && held < TurnClickSeconds && pressCallsign != null && !double.IsNaN(pressHeading))
                {
                    // A click on ◀ L / R ▶: exactly 1° from the heading when pressed; the knob shows it for a moment.
                    Set(0);
                    double target = TestTraffic.Wrap(pressHeading + x * TurnClickDegrees);
                    traffic.Change(pressCallsign, p =>
                    {
                        p.TargetHeading = target;
                        p.TargetTurn = 0;
                        p.Auto = false;
                    });
                    ShowKnob(x, 0);
                    stop.Interval = TimeSpan.FromSeconds(0.3);
                    stop.Start();
                    return;
                }
                if (x != 0 || held >= StickKeySeconds)
                {
                    Set(0);
                }
                else
                {
                    stop.Interval = TimeSpan.FromSeconds(StickKeySeconds - held);
                    stop.Start();
                }
            }
            key.PreviewMouseLeftButtonUp += (s, e) => Release();
            key.LostMouseCapture += (s, e) => Release();
            return key;
        }

        /// <summary>Aircraft moved by the keys or the stick of the window (released there, even if the selection changed).</summary>
        private string? manualCallsign;

        /// <summary>The selection changed while keys or the stick were held: the previous aircraft keeps its heading and vertical speed.</summary>
        private void ReleaseManual()
        {
            if (manualCallsign == null || manualCallsign == list.SelectedItem as string) return;
            traffic.Change(manualCallsign, p =>
            {
                p.StickX = 0;
                p.StickY = 0;
                p.StickHeld = 0;
            });
            manualCallsign = null;
            keyX = keyY = 0;
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
        }

        /// <summary>Deflection of the keys: given to the selected aircraft and shown by the knob.</summary>
        private void SetKeyStick(double x, double y)
        {
            keyX = x;
            keyY = y;
            if (x != 0 || y != 0)
            {
                ShowKnob(x, y);
                manualCallsign = list.SelectedItem as string;
            }
            else if (joystickCallsign == null && !pad.IsMouseCaptured)
            {
                CentreKnob();
            }
            string? target = x == 0 && y == 0 ? manualCallsign ?? list.SelectedItem as string : list.SelectedItem as string;
            if (x == 0 && y == 0) manualCallsign = null;
            if (target == null) return;
            traffic.Change(target, p =>
            {
                p.StickX = x;
                p.StickY = y;
                if (x != 0)
                {
                    p.Auto = false;
                    p.TargetHeading = double.NaN;
                }
                else
                {
                    p.StickHeld = 0;
                }
                if (y != 0)
                {
                    p.Auto = false;
                    p.HoldGlidePath = false;
                }
            });
            // Keys released: a real joystick still deflected takes over again.
            if (x == 0 && y == 0 && (joystickX != 0 || joystickY != 0))
            {
                joystickCallsign = null;
                ApplyJoystick();
            }
        }

        private static Button SmallButton(string text) => new() { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 2, 8, 2) };

        /// <summary>Rate of descent key: the column as wide as the stick, all the same width, text centred.</summary>
        private static Button WideButton(string text, string tip) => new()
        {
            Content = text,
            ToolTip = tip,
            Height = 28,
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalContentAlignment = HorizontalAlignment.Center
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
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ReadoutText,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static void AddReadout(Grid grid, int column, TextBlock caption, TextBlock value, string unit)
        {
            StackPanel cell = new() { Margin = new Thickness(2, 6, 2, 6) };
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

        /// <summary>Wind of the boxes to the traffic (each box only when valid); the magnetic direction beside it.</summary>
        private void ApplyWind()
        {
            if (TryNumber(windFromBox, 0, 360, out double from)) traffic.WindFrom = from % 360;
            if (TryNumber(windSpeedBox, 0, 100, out double speed)) traffic.WindSpeed = speed;
            if (TryNumber(windGustBox, 0, 50, out double gust)) traffic.WindGust = gust;
            ShowWindMagnetic();
        }

        /// <summary>Magnetic direction of the wind (the variation follows the runway in use).</summary>
        private void ShowWindMagnetic()
        {
            int magnetic = (int)Math.Round(traffic.WindFrom - variation(), MidpointRounding.AwayFromZero);
            magnetic = (magnetic % 360 + 360) % 360;
            windMagnetic.Text = $"({(magnetic == 0 ? 360 : magnetic):000}°M)";
        }

        private void WindFromMetar()
        {
            Match match = metar() is string text ? MetarWind.Match(text) : Match.Empty;
            if (!match.Success)
            {
                System.Media.SystemSounds.Beep.Play();
                MessageBox.Show(this, "No METAR wind received from Aurora for the airport in use.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            double factor = match.Groups[4].Value switch
            {
                "MPS" => 1.94384,
                "KMH" => 0.539957,
                _ => 1
            };
            double speed = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * factor;
            double gust = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * factor - speed : 0;
            // Variable: the direction stays.
            if (match.Groups[1].Value != "VRB") windFromBox.Text = match.Groups[1].Value;
            windSpeedBox.Text = Math.Round(speed).ToString(CultureInfo.InvariantCulture);
            windGustBox.Text = Math.Round(Math.Max(0, gust)).ToString(CultureInfo.InvariantCulture);
            ApplyWind();
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

        /// <summary>An aircraft is selected (the joystick moves it in the automatic role).</summary>
        internal bool HasSelection => list.SelectedItem is string;

        private void FinalCourse() => ChangeSelected(p =>
        {
            p.TargetHeading = 0;
            p.TargetTurn = 0;
            p.Auto = false;
        });

        /// <summary>Turns the selected aircraft to the heading of the box (magnetic): left −1, right 1, shortest 0.</summary>
        /// <summary>A heading instruction (box, wheel, buttons): carried out after the pilot reaction; a new one replaces it.</summary>
        private void GiveHeading(int side)
        {
            if (list.SelectedItem is not string callsign)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            pendingSide = side;
            pendingCallsign = callsign;
            reaction.Stop();
            double delay = TryNumber(reactionBox, 0, 5, out double seconds) ? seconds : 0.5;
            if (delay <= 0)
            {
                TurnToHeading(side, callsign);
                return;
            }
            reaction.Interval = TimeSpan.FromSeconds(delay);
            reaction.Start();
        }

        /// <summary>Mouse wheel over the heading box: 1° per step (Shift: 10°), from the heading of the aircraft when empty.</summary>
        private void WheelHeading(int direction)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            if (!int.TryParse(headingBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360)
            {
                TestTraffic.Plane? plane = traffic.List().FirstOrDefault(p => p.Callsign == list.SelectedItem as string);
                if (plane == null)
                {
                    System.Media.SystemSounds.Beep.Play();
                    return;
                }
                heading = (int)Math.Round(plane.HeadingTrue - variation(), MidpointRounding.AwayFromZero);
                step = 0;
            }
            heading = ((heading + direction * step) % 360 + 360) % 360;
            if (heading == 0) heading = 360;
            headingBox.Text = heading.ToString("000", CultureInfo.InvariantCulture);
            headingBox.CaretIndex = headingBox.Text.Length;
            GiveHeading(0);
        }

        /// <summary>Turns the aircraft to the heading of the box (magnetic): left −1, right 1, shortest 0.</summary>
        private void TurnToHeading(int side, string? callsign)
        {
            string text = headingBox.Text.Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int heading) || heading < 0 || heading > 360 || callsign == null)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            double magneticVariation = variation();
            traffic.Change(callsign, p =>
            {
                p.TargetHeading = TestTraffic.Wrap(heading + magneticVariation - p.FinalTrue);
                p.TargetTurn = side;
                p.Auto = false;
            });
        }

        private void NormalRate() => ChangeSelected(p =>
        {
            p.HoldGlidePath = true;
            p.Auto = false;
        });

        /// <summary>Reduce (up) or increase (down) the rate of descent by one step.</summary>
        private void StepRate(bool up) => ChangeSelected(p =>
        {
            p.HoldGlidePath = false;
            p.Auto = false;
            p.VerticalSpeed = TestTraffic.StepVerticalSpeed(p.VerticalSpeed, p.BestVerticalSpeed, up);
        });

        private void OnPath() => ChangeSelected(p =>
        {
            p.Lateral = 0;
            p.Height = double.NaN;
            p.VerticalSpeed = 0;
            p.Heading = p.CrabHeading;
            p.TurnRate = 0;
            p.TargetHeading = double.NaN;
            p.HoldGlidePath = true;
        });

        private void TogglePause()
        {
            traffic.Paused = !traffic.Paused;
            pauseButton.Content = traffic.Paused ? "Resume" : "Pause";
        }

        /// <summary>A command from a joystick button.</summary>
        internal void RunJoystickCommand(JoystickCommand command)
        {
            switch (command)
            {
                case JoystickCommand.FinalCourse: FinalCourse(); break;
                case JoystickCommand.NormalRate: NormalRate(); break;
                case JoystickCommand.ReduceRate: StepRate(up: true); break;
                case JoystickCommand.IncreaseRate: StepRate(up: false); break;
                case JoystickCommand.OnPath: OnPath(); break;
                case JoystickCommand.Pause: TogglePause(); break;
                case JoystickCommand.TurnRate:
                    traffic.TurnMode = traffic.TurnMode switch
                    {
                        TestTurnMode.Half => TestTurnMode.Standard,
                        TestTurnMode.Standard => TestTurnMode.Free,
                        _ => TestTurnMode.Half
                    };
                    ShowTurnMode();
                    break;
                case JoystickCommand.NextAircraft:
                    if (list.ItemsSource is List<string> callsigns && callsigns.Count > 0)
                    {
                        int index = list.SelectedItem is string selected ? callsigns.IndexOf(selected) : -1;
                        list.SelectedItem = callsigns[(index + 1) % callsigns.Count];
                    }
                    break;
            }
        }

        // Joystick deflection last given (pad: x right, y up) and the aircraft it moves.
        private double joystickX;
        private double joystickY;
        private string? joystickCallsign;

        /// <summary>
        /// The real joystick moved (only when it changes): as the stick of the window, which follows it. Released
        /// (0, 0), the heading and vertical speed reached stay. Ignored while the stick is held with the mouse.
        /// </summary>
        internal void JoystickStick(double x, double y)
        {
            joystickX = x;
            joystickY = y;
            ApplyJoystick();
        }

        private void ApplyJoystick()
        {
            if (pad.IsMouseCaptured) return;
            string? selected = list.SelectedItem as string;
            // Another aircraft was moved: let it go first.
            if (joystickCallsign != null && joystickCallsign != selected)
            {
                traffic.Change(joystickCallsign, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
                joystickCallsign = null;
                CentreKnob();
            }
            if (selected == null) return;
            double x = joystickX;
            double y = joystickY;
            if (x == 0 && y == 0)
            {
                if (joystickCallsign == null) return;
                joystickCallsign = null;
                CentreKnob();
                traffic.Change(selected, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
                return;
            }
            joystickCallsign = selected;
            ShowKnob(x, y);
            traffic.Change(selected, p =>
            {
                p.StickX = x;
                p.StickY = y;
                p.Auto = false;
                if (x != 0) p.TargetHeading = double.NaN;
                if (y != 0) p.HoldGlidePath = false;
            });
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
            ShowWindMagnetic();
        }

        private void ShowSelected()
        {
            TestTraffic.Plane? plane = traffic.List().FirstOrDefault(p => p.Callsign == list.SelectedItem as string);
            if (plane == null)
            {
                info.Text = traffic.Count == 0 ? "No test aircraft:\nAdd one above." : "";
                bestValue.Text = actualValue.Text = turnValue.Text = headingValue.Text = driftValue.Text = "---";
                bestLabel.Text = "BEST VS";
                return;
            }
            info.Text = string.Format(CultureInfo.InvariantCulture, "{0}\n{1:0.0} NM from touchdown\n{2:0} kt, ground speed {3:0} kt{4}",
                plane.Callsign, plane.Distance, plane.Speed, plane.GroundSpeed, plane.Squawk != null ? "\nSSR A" + plane.Squawk : "");
            bestLabel.Text = string.Format(CultureInfo.InvariantCulture, "BEST VS {0:0.0#}°", plane.GlideSlope);
            bestValue.Text = Rounded(plane.BestVerticalSpeed);
            actualValue.Text = Rounded(plane.VerticalSpeed);
            turnValue.Text = Math.Abs(plane.TurnRate) < 0.05 ? "0.0"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", Math.Abs(plane.TurnRate), plane.TurnRate > 0 ? "R" : "L");
            int magnetic = (int)Math.Round(plane.HeadingTrue - variation(), MidpointRounding.AwayFromZero);
            magnetic = (magnetic % 360 + 360) % 360;
            headingValue.Text = (magnetic == 0 ? 360 : magnetic).ToString("000", CultureInfo.InvariantCulture);
            int drift = (int)Math.Round(plane.Drift);
            driftValue.Text = drift == 0 ? "0" : $"{Math.Abs(drift)} {(drift > 0 ? "R" : "L")}";
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

        /// <summary>The knob at a deflection (x right, y up, −1..1), inside the circle of the pad.</summary>
        private void ShowKnob(double x, double y)
        {
            double length = Math.Sqrt(x * x + y * y);
            double scale = length > 1 ? PadReach / length : PadReach;
            Canvas.SetLeft(knob, PadSize / 2 + x * scale - KnobSize / 2);
            Canvas.SetTop(knob, PadSize / 2 - y * scale - KnobSize / 2);
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
            manualCallsign = list.SelectedItem as string;
            ChangeSelected(p =>
            {
                p.StickX = x;
                p.StickY = y;
                if (x != 0 || y != 0) p.Auto = false;
                if (x != 0) p.TargetHeading = double.NaN;
                if (y != 0) p.HoldGlidePath = false;
            });
        }

        private void ReleaseKnob()
        {
            if (pad.IsMouseCaptured) pad.ReleaseMouseCapture();
            CentreKnob();
            // The heading and the vertical speed reached stay.
            if ((manualCallsign ?? list.SelectedItem as string) is string released)
            {
                traffic.Change(released, p =>
                {
                    p.StickX = 0;
                    p.StickY = 0;
                    p.StickHeld = 0;
                });
            }
            manualCallsign = null;
            // A real joystick still deflected takes over again.
            if (joystickCallsign != null || joystickX != 0 || joystickY != 0)
            {
                joystickCallsign = null;
                ApplyJoystick();
            }
        }
    }
}
