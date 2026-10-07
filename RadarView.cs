using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Display options of the views, taken from the active profile (plus the labels on/off switch of the session).
    /// </summary>
    internal class ViewOptions
    {
        /// <summary>Incremented when the options change, so tracks rebuild their symbol and label.</summary>
        public int Version { get; set; }
        /// <summary>True: heights above the threshold (QFE); false: altitudes (QNH).</summary>
        public bool Qfe { get; set; }
        public bool ShowAltitudeScale { get; set; } = true;
        /// <summary>Heights, deviations and vertical speed in metres (true) or feet (false).</summary>
        public bool ScaleInMetres { get; set; }
        /// <summary>Labels shown (switch of the session, not saved).</summary>
        public bool ShowLabels { get; set; } = true;
        public bool HistoryEnabled { get; set; } = true;
        public int HistoryDots { get; set; } = 50;
        /// <summary>Seconds between two history dots.</summary>
        public double HistoryInterval { get; set; } = 2;
        public SymbolSetting TrackSymbol { get; set; } = new(SymbolShape.CrossCircle, 12);
        public SymbolSetting ThresholdSymbol { get; set; } = new(SymbolShape.Line, 10);
        public SymbolSetting TouchdownSymbol { get; set; } = new(SymbolShape.Line, 12);
        public SymbolSetting AntennaSymbol { get; set; } = new(SymbolShape.Square, 8);
        public SymbolSetting HistorySymbol { get; set; } = new(SymbolShape.FilledCircle, 3);
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
        /// <summary>Analog scope: monochrome phosphor, echoes lit by the beam, no labels.</summary>
        public bool Analog { get; set; }
        /// <summary>Colours, widths and dash styles of the elements.</summary>
        public Theme Theme { get; set; } = Theme.Modern(DisplayStyleSettings.CreateDefault());
        /// <summary>Range marks drawn at each range.</summary>
        public RangeMarkSettings RangeMarks { get; set; } = RangeMarkSettings.Default();
        /// <summary>Distance reminders of a runway (those of the profile and those of the runway).</summary>
        public Func<Runway, IEnumerable<DistanceReminder>> Reminders { get; set; } = _ => [];
        /// <summary>Distance text below the horizon line and reminder markers above it (otherwise the opposite).</summary>
        public bool RangeTextBelowHorizon { get; set; }
    }

    /// <summary>
    /// Common part of the profile (elevation) and azimuth views.
    ///
    /// Static elements (runway, scan and approach limits, range marks...) are built only when something they
    /// depend on changes (runway, range, window size, orientation, settings): see <see cref="Invalidate"/>.
    /// Aircraft tracks (symbol, history dots, label, leader line) are created once per callsign and then only
    /// moved and updated.
    ///
    /// Labels: by default 45° up-right of the track; they can be dragged with the mouse (a leader line then joins
    /// them to the track), double click puts them back, right click hides one, right click on the track shows it again.
    ///
    /// The drawing code of the views works in "logical" coordinates, in pixels, as if the runway were on the left
    /// (x grows from the runway towards the approach). <see cref="ToScreenX"/> and <see cref="ToScreenY"/>
    /// mirror them when the runway is shown on the right.
    /// </summary>
    internal abstract class RadarView
    {
        private const int StaticZIndex = 0;
        private const int SweepZIndex = 5;
        private const int HistoryZIndex = 9;
        private const int TrackZIndex = 11;
        private const int LabelZIndex = 12;
        /// <summary>Default label position: this many pixels right of and above the track (45°).</summary>
        private const double LabelDistance = 14;

        protected readonly Canvas Canvas;
        protected Runway Runway;
        /// <summary>
        /// Approach limits, scan limits and antenna tilt (shared by both views).
        /// </summary>
        protected readonly Radar Radar;
        protected readonly ViewOptions Options;
        protected double xscale = 1;
        protected double yscale = 1;
        protected bool RunwayOnRight { get; private set; }

        private bool staticValid = false;
        private readonly List<UIElement> staticElements = [];
        private readonly Dictionary<string, Track> tracks = [];
        /// <summary>Label being dragged with the mouse, and where the drag started.</summary>
        private Track? dragging;
        /// <summary>Shared geometry of the history dots, rebuilt when the options change.</summary>
        private Geometry historyGeometry = Geometry.Empty;
        private int historyGeometryVersion = -1;
        private Point dragStart;
        /// <summary>Clock of the history dots.</summary>
        private static readonly System.Diagnostics.Stopwatch HistoryClock = System.Diagnostics.Stopwatch.StartNew();
        /// <summary>Lines of the antenna scan effect (beam and glow), created when first needed.</summary>
        private readonly List<Line> sweepLines = [];
        /// <summary>Echo of an aircraft on the analog scope: a small blob, longer along the range.</summary>
        private static readonly Geometry EchoGeometry = CreateEchoGeometry();
        private Vector dragStartOffset;

        /// <summary>
        /// Screen elements and state of one aircraft, reused at every refresh.
        /// </summary>
        protected sealed class Track
        {
            public readonly Path Symbol = new() { StrokeThickness = 2 };
            public readonly Border Label = new() { Background = Brushes.Transparent, Padding = new Thickness(1) };
            public readonly Grid LabelGrid = new();
            public TextBlock[] Cells = [];
            public int LayoutVersion = -1;
            public int SymbolVersion = -1;
            public readonly Line Leader = new() { StrokeThickness = 1 };
            public readonly List<Path> Dots = [];
            public int DotsVersion = -1;
            /// <summary>Previous positions, in world coordinates (see <see cref="ToWorld"/>), oldest first.</summary>
            /// <summary>Inside: the aircraft was inside the scan limits there (only those dots are drawn).</summary>
            public readonly List<(double Along, double Value, bool Inside)> History = [];
            public (double Along, double Value, bool Inside) LastWorld;
            public double LastLatitude = double.NaN;
            public double LastLongitude = double.NaN;
            public double LastAltitude = double.NaN;
            /// <summary>Time of the last history dot (see <see cref="HistoryClock"/>).</summary>
            public double LastHistoryTime = double.NegativeInfinity;
            /// <summary>Screen position of the track.</summary>
            public Point Position;
            /// <summary>Logical position of the track (see the class description).</summary>
            public Point Logical;
            /// <summary>True when the label was dragged: <see cref="Offset"/> is then its top-left corner from the track.</summary>
            public bool Moved;
            public Vector Offset;
            /// <summary>Label hidden with a right click.</summary>
            public bool LabelHidden;
            public Brush Color = Brushes.Green;
            /// <summary>Colour of the history dots (plots), inside or outside the approach limits like the track.</summary>
            public Brush PlotColor = Brushes.Green;
            public string Callsign = "";

            public IEnumerable<UIElement> Elements()
            {
                yield return Symbol;
                yield return Label;
                yield return Leader;
                foreach (Path dot in Dots) yield return dot;
            }
        }

        protected RadarView(Canvas canvas, Runway runway, Radar radar, ViewOptions options)
        {
            Canvas = canvas;
            Runway = runway;
            Radar = radar;
            Options = options;
            // Lines going beyond the view (e.g. a tilted scan limit) must not be drawn over the other view.
            Canvas.ClipToBounds = true;
            // Right click near a track (not only exactly on its small symbol) hides or shows its label.
            Canvas.MouseRightButtonUp += Canvas_MouseRightButtonUp;
        }

        /// <summary>
        /// True when the vertical axis must also be mirrored with the runway on the right
        /// (azimuth view: mirroring both axes is a 180° rotation, which keeps left/right of the approach correct).
        /// </summary>
        protected virtual bool FlipVertically => false;

        protected abstract void CalculateScale();
        protected abstract void DrawStatic();
        /// <summary>
        /// Position of the aircraft in world coordinates of this view: distance along the centreline from the
        /// threshold (NM, positive on the approach side) and the second coordinate (height in ft, or lateral offset in NM).
        /// World coordinates (not pixels) are kept in the history, so it stays correct when zooming.
        /// </summary>
        protected abstract (double Along, double Value) ToWorld(Aircraft aircraft);
        /// <summary>Logical position (pixels) of a world position.</summary>
        protected abstract Point WorldToLogical(double along, double value);
        /// <summary>Colour of the track: green inside the approach limits, red outside.</summary>
        protected abstract bool IsWithinLimits(Aircraft aircraft);
        /// <summary>Label layout of this view.</summary>
        protected abstract LabelLayout Layout { get; }
        /// <summary>True for the elevation view (for the antenna scan effect).</summary>
        protected abstract bool IsElevation { get; }
        /// <summary>Logical position of the antenna, origin of the scan effect beam.</summary>
        protected abstract Point SweepOrigin();
        /// <summary>
        /// Logical end of the scan effect beam at the end of the display: <paramref name="position"/> 0 on the
        /// lower/left scan limit, 1 on the upper/right one (tilt included).
        /// </summary>
        protected abstract Point SweepEnd(double position);
        /// <summary>False when the track is outside the drawable area (it is then hidden).</summary>
        protected virtual bool IsDrawable(Point logical) => true;

        public void SetRunway(Runway runway)
        {
            if (runway != Runway)
            {
                // Positions and history refer to the previous runway.
                ClearTracks();
            }
            Runway = runway;
            Invalidate();
        }

        public void SetRunwayOnRight(bool value)
        {
            if (RunwayOnRight != value)
            {
                RunwayOnRight = value;
                foreach (Track track in tracks.Values)
                {
                    track.Moved = false;
                }
                Invalidate();
            }
        }

        /// <summary>
        /// Rebuilds the static elements at the next <see cref="Render"/> (call after a change of range, size, settings...).
        /// </summary>
        public void Invalidate()
        {
            staticValid = false;
        }

        private void ClearTracks()
        {
            foreach (Track track in tracks.Values)
            {
                RemoveTrack(track);
            }
            tracks.Clear();
            dragging = null;
        }

        private void RemoveTrack(Track track)
        {
            foreach (UIElement element in track.Elements().ToList())
            {
                Canvas.Children.Remove(element);
            }
        }

        /// <summary>
        /// Updates the view. Must be called on the window's (UI) thread.
        /// </summary>
        public void Render(IReadOnlyList<Aircraft> aircrafts)
        {
            if (Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            if (!staticValid)
            {
                foreach (UIElement element in staticElements)
                {
                    Canvas.Children.Remove(element);
                }
                staticElements.Clear();
                CalculateScale();
                DrawStatic();
                staticValid = true;
            }
            HashSet<string> present = [];
            foreach (Aircraft aircraft in aircrafts)
            {
                if (!present.Add(aircraft.Callsign)) continue;
                if (!tracks.TryGetValue(aircraft.Callsign, out Track? track))
                {
                    track = CreateTrack();
                    track.Callsign = aircraft.Callsign;
                    tracks[aircraft.Callsign] = track;
                }
                UpdateTrack(track, aircraft);
            }
            // Remove the tracks of aircraft no longer received from Aurora.
            foreach (string callsign in tracks.Keys.Where(c => !present.Contains(c)).ToList())
            {
                if (dragging == tracks[callsign]) dragging = null;
                RemoveTrack(tracks[callsign]);
                tracks.Remove(callsign);
            }
        }

        /// <summary>
        /// Draws the antenna scan effect at time <paramref name="t"/> (seconds), or hides it when
        /// <paramref name="enabled"/> is false. Graphic only: the tracks are not affected.
        /// </summary>
        public void RenderSweep(bool enabled, double t, ScanEffectSpeed speed)
        {
            if (!enabled || !staticValid || Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0)
            {
                foreach (Line line in sweepLines) line.Visibility = Visibility.Collapsed;
                foreach (Track track in tracks.Values) track.Symbol.Opacity = 1;
                return;
            }
            var beams = new (double? Position, double Opacity)[ScanEffect.Lines];
            ScanEffect.Compute(t, speed, IsElevation, beams);
            while (sweepLines.Count < beams.Length)
            {
                Line line = new()
                {
                    StrokeThickness = sweepLines.Count == 0 ? 2 : 3,
                    IsHitTestVisible = false
                };
                Panel.SetZIndex(line, SweepZIndex);
                Canvas.Children.Add(line);
                sweepLines.Add(line);
            }
            Point origin = SweepOrigin();
            for (int i = 0; i < beams.Length; i++)
            {
                Line line = sweepLines[i];
                if (beams[i].Position is not double position)
                {
                    line.Visibility = Visibility.Collapsed;
                    continue;
                }
                Point end = SweepEnd(position);
                line.X1 = ToScreenX(origin.X);
                line.Y1 = ToScreenY(origin.Y);
                line.X2 = ToScreenX(end.X);
                line.Y2 = ToScreenY(end.Y);
                line.Stroke = Options.Theme.Sweep;
                line.Opacity = beams[i].Opacity;
                line.Visibility = Visibility.Visible;
            }
            UpdateEchoBrightness(t, speed);
        }

        /// <summary>
        /// Analog scope: each echo lights up when the beam passes over it, then fades until the next pass,
        /// like the phosphor of the old screens. The position is always the latest one received from Aurora.
        /// </summary>
        private void UpdateEchoBrightness(double t, ScanEffectSpeed speed)
        {
            if (!Options.Analog)
            {
                foreach (Track track in tracks.Values) track.Symbol.Opacity = 1;
                return;
            }
            Point origin = SweepOrigin();
            Point low = SweepEnd(0);
            Point high = SweepEnd(1);
            foreach (Track track in tracks.Values)
            {
                if (track.Symbol.Visibility != Visibility.Visible) continue;
                // Position of the echo across the scan (0..1), from its direction seen from the antenna.
                double position = 0.5;
                double dx = track.Logical.X - origin.X;
                if (dx > 1 && Math.Abs(high.Y - low.Y) > 1)
                {
                    double y = origin.Y + (track.Logical.Y - origin.Y) * (low.X - origin.X) / dx;
                    position = (y - low.Y) / (high.Y - low.Y);
                }
                double age = ScanEffect.SinceLastPass(t, speed, IsElevation, position);
                track.Symbol.Opacity = 0.18 + 0.82 * Math.Exp(-age / 0.6);
            }
        }

        private static Geometry CreateEchoGeometry()
        {
            // Elongated vertically (turned 90 degrees from the first version).
            EllipseGeometry geometry = new(new Point(0, 0), 2.2, 5);
            geometry.Freeze();
            return geometry;
        }

        private void UpdateTrack(Track track, Aircraft aircraft)
        {
            (double along, double value) = ToWorld(aircraft);
            // History: a dot every HistoryInterval seconds, at a new position given by Aurora.
            bool changed = aircraft.Latitude != track.LastLatitude || aircraft.Longitude != track.LastLongitude || aircraft.Altitude != track.LastAltitude;
            if (changed)
            {
                double now = HistoryClock.Elapsed.TotalSeconds;
                if (!double.IsNaN(track.LastLatitude) && now - track.LastHistoryTime >= Options.HistoryInterval - 0.05)
                {
                    track.LastHistoryTime = now;
                    track.History.Add(track.LastWorld);
                    if (track.History.Count > Profile.MaxHistoryDots)
                    {
                        track.History.RemoveAt(0);
                    }
                }
                track.LastLatitude = aircraft.Latitude;
                track.LastLongitude = aircraft.Longitude;
                track.LastAltitude = aircraft.Altitude;
            }
            bool inside = Radar.IsInsideScan(aircraft, Runway);
            track.LastWorld = (along, value, inside);

            Point logical = WorldToLogical(along, value);
            if (!inside || !IsDrawable(logical))
            {
                SetTrackVisible(track, false);
                return;
            }
            bool within = IsWithinLimits(aircraft);
            track.Color = Options.Theme.Brush(within ? StyleElement.TrackInside : StyleElement.TrackOutside);
            track.PlotColor = Options.Theme.Brush(within ? StyleElement.PlotInside : StyleElement.PlotOutside);
            track.Logical = logical;
            track.Position = new Point(ToScreenX(logical.X), ToScreenY(logical.Y));
            // Track symbol.
            if (track.SymbolVersion != Options.Version)
            {
                track.Symbol.Data = Options.Analog ? EchoGeometry : Symbols.Create(Options.TrackSymbol.Shape, Options.TrackSymbol.Size);
                track.SymbolVersion = Options.Version;
            }
            track.Symbol.Stroke = track.Color;
            track.Symbol.Fill = Options.Analog || Symbols.IsFilled(Options.TrackSymbol.Shape) ? track.Color : Brushes.Transparent;
            Canvas.SetLeft(track.Symbol, track.Position.X);
            Canvas.SetTop(track.Symbol, track.Position.Y);
            track.Symbol.Visibility = Visibility.Visible;
            UpdateHistory(track);
            UpdateLabel(track, aircraft);
        }

        private void SetTrackVisible(Track track, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            foreach (UIElement element in track.Elements())
            {
                element.Visibility = v;
            }
        }

        /// <summary>
        /// History dots: the last N previous positions, in the current colour of the track.
        /// </summary>
        private void UpdateHistory(Track track)
        {
            int count = Options.HistoryEnabled ? Math.Min(track.History.Count, Options.HistoryDots) : 0;
            if (track.DotsVersion != Options.Version)
            {
                // Symbol of the history dots changed: rebuild them.
                foreach (Path old in track.Dots) Canvas.Children.Remove(old);
                track.Dots.Clear();
                track.DotsVersion = Options.Version;
            }
            if (historyGeometryVersion != Options.Version)
            {
                historyGeometry = Symbols.Create(Options.HistorySymbol.Shape, Options.HistorySymbol.Size);
                historyGeometryVersion = Options.Version;
            }
            bool filled = Symbols.IsFilled(Options.HistorySymbol.Shape);
            while (track.Dots.Count < count)
            {
                Path dot = new() { Data = historyGeometry, StrokeThickness = 1, IsHitTestVisible = false };
                Panel.SetZIndex(dot, HistoryZIndex);
                Canvas.Children.Add(dot);
                track.Dots.Add(dot);
            }
            while (track.Dots.Count > count)
            {
                Canvas.Children.Remove(track.Dots[^1]);
                track.Dots.RemoveAt(track.Dots.Count - 1);
            }
            int first = track.History.Count - count;
            for (int i = 0; i < count; i++)
            {
                (double along, double value, bool wasInside) = track.History[first + i];
                Point logical = WorldToLogical(along, value);
                Path dot = track.Dots[i];
                dot.Stroke = track.PlotColor;
                dot.Fill = filled ? track.PlotColor : null;
                // Analog scope: the older the position, the dimmer its glow.
                dot.Opacity = Options.Analog ? 0.08 + 0.42 * (i + 1) / count : 1;
                Canvas.SetLeft(dot, ToScreenX(logical.X));
                Canvas.SetTop(dot, ToScreenY(logical.Y));
                // A radar shows only what its antenna saw: no dots where the aircraft was outside the scan limits.
                dot.Visibility = wasInside && IsDrawable(logical) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateLabel(Track track, Aircraft aircraft)
        {
            LabelLayout layout = Layout;
            if (!Options.ShowLabels || Options.Analog || track.LabelHidden || layout.IsEmpty)
            {
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
                return;
            }
            if (track.LayoutVersion != Options.Version)
            {
                BuildLabel(track, layout);
                track.LayoutVersion = Options.Version;
            }
            for (int i = 0; i < track.Cells.Length; i++)
            {
                LabelField field = layout.Cells[i];
                if (field != LabelField.None)
                {
                    track.Cells[i].Text = LabelFormatter.Format(field, aircraft, Runway, Options);
                    track.Cells[i].Foreground = Options.Theme.Brush(StyleElement.LabelText);
                }
            }
            track.Label.Visibility = Visibility.Visible;
            PositionLabel(track);
        }

        /// <summary>
        /// Builds the grid of text cells of a label from the layout.
        /// </summary>
        private static void BuildLabel(Track track, LabelLayout layout)
        {
            Grid grid = track.LabelGrid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            for (int r = 0; r < layout.Rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < layout.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            track.Cells = new TextBlock[layout.Rows * layout.Columns];
            for (int r = 0; r < layout.Rows; r++)
            {
                for (int c = 0; c < layout.Columns; c++)
                {
                    TextBlock cell = new()
                    {
                        FontSize = 12,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, c < layout.Columns - 1 ? 8 : 0, 0),
                        Visibility = layout.Get(r, c) == LabelField.None ? Visibility.Collapsed : Visibility.Visible
                    };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                    track.Cells[r * layout.Columns + c] = cell;
                }
            }
        }

        /// <summary>
        /// Places the label (default position, or where it was dragged) and its leader line.
        /// </summary>
        private void PositionLabel(Track track)
        {
            track.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = track.Label.DesiredSize;
            Point topLeft = track.Moved
                ? track.Position + track.Offset
                : new Point(track.Position.X + LabelDistance, track.Position.Y - LabelDistance - size.Height);
            Canvas.SetLeft(track.Label, topLeft.X);
            Canvas.SetTop(track.Label, topLeft.Y);
            if (track.Moved)
            {
                // Leader line from the track to the nearest point of the label.
                double x = Math.Clamp(track.Position.X, topLeft.X, topLeft.X + size.Width);
                double y = Math.Clamp(track.Position.Y, topLeft.Y, topLeft.Y + size.Height);
                track.Leader.X1 = track.Position.X;
                track.Leader.Y1 = track.Position.Y;
                track.Leader.X2 = x;
                track.Leader.Y2 = y;
                track.Leader.Stroke = track.Color;
                track.Leader.Visibility = Visibility.Visible;
            }
            else
            {
                track.Leader.Visibility = Visibility.Collapsed;
            }
        }

        private Track CreateTrack()
        {
            Track track = new();
            track.Label.Child = track.LabelGrid;
            Panel.SetZIndex(track.Symbol, TrackZIndex);
            Panel.SetZIndex(track.Leader, HistoryZIndex);
            Panel.SetZIndex(track.Label, LabelZIndex);
            track.Leader.IsHitTestVisible = false;
            track.Label.Cursor = Cursors.SizeAll;
            ToolTips.KeepOpen(track.Label);
            track.Label.ToolTip = "Drag to move · double click: back to its place · right click: hide (right click near the track or key L twice to show it again)";
            Canvas.Children.Add(track.Symbol);
            Canvas.Children.Add(track.Leader);
            Canvas.Children.Add(track.Label);

            track.Label.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2)
                {
                    track.Moved = false;
                    PositionLabel(track);
                    return;
                }
                dragging = track;
                dragStart = e.GetPosition(Canvas);
                dragStartOffset = new Point(Canvas.GetLeft(track.Label), Canvas.GetTop(track.Label)) - track.Position;
                track.Label.CaptureMouse();
            };
            track.Label.MouseMove += (s, e) =>
            {
                if (dragging != track || !track.Label.IsMouseCaptured) return;
                track.Offset = dragStartOffset + (e.GetPosition(Canvas) - dragStart);
                track.Moved = true;
                PositionLabel(track);
            };
            track.Label.MouseLeftButtonUp += (s, e) =>
            {
                if (track.Label.IsMouseCaptured) track.Label.ReleaseMouseCapture();
                dragging = null;
                e.Handled = true;
            };
            track.Label.MouseRightButtonUp += (s, e) =>
            {
                track.LabelHidden = true;
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
                e.Handled = true;
            };
            return track;
        }

        /// <summary>Maximum distance in pixels between a right click and a track for the click to apply to it.</summary>
        private const double TrackClickDistance = 20;

        /// <summary>
        /// Right click on the view: with a single track nearby its label is hidden or shown at once; with several
        /// tracks nearby (close formation) or with labels hidden elsewhere, a menu lists them by callsign.
        /// </summary>
        private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Options.Analog) return;
            Point click = e.GetPosition(Canvas);
            List<Track> nearby = tracks.Values
                .Where(t => t.Symbol.Visibility == Visibility.Visible && (t.Position - click).Length <= TrackClickDistance)
                .OrderBy(t => (t.Position - click).Length)
                .ToList();
            List<Track> hiddenElsewhere = tracks.Values
                .Where(t => t.LabelHidden && !nearby.Contains(t))
                .OrderBy(t => t.Callsign)
                .ToList();
            if (nearby.Count == 0 && hiddenElsewhere.Count == 0) return;
            e.Handled = true;
            if (nearby.Count == 1 && hiddenElsewhere.Count == 0)
            {
                ToggleLabel(nearby[0]);
                return;
            }
            ContextMenu menu = new() { PlacementTarget = Canvas };
            foreach (Track track in nearby)
            {
                AddMenuItem(menu, $"{track.Callsign}: {(track.LabelHidden ? "show" : "hide")} label", () => ToggleLabel(track));
            }
            if (hiddenElsewhere.Count > 0)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new Separator());
                foreach (Track track in hiddenElsewhere)
                {
                    AddMenuItem(menu, $"{track.Callsign}: show label", () => ToggleLabel(track));
                }
            }
            if (tracks.Values.Count(t => t.LabelHidden) > 1)
            {
                menu.Items.Add(new Separator());
                AddMenuItem(menu, "Show all hidden labels", ShowAllLabels);
            }
            menu.IsOpen = true;
        }

        private static void AddMenuItem(ContextMenu menu, string header, Action action)
        {
            MenuItem item = new() { Header = header };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }

        private static void ToggleLabel(Track track)
        {
            track.LabelHidden = !track.LabelHidden;
            if (track.LabelHidden)
            {
                track.Label.Visibility = Visibility.Collapsed;
                track.Leader.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Shows again the labels hidden one by one with a right click.
        /// </summary>
        public void ShowAllLabels()
        {
            foreach (Track track in tracks.Values)
            {
                track.LabelHidden = false;
            }
        }

        /// <summary>
        /// Shift of the logical x on the screen: the antenna is kept at <see cref="LeftMargin"/> from the edge
        /// whatever the range (the part of the runway behind the antenna, outside the scan, is then off the view).
        /// </summary>
        protected double XShift;

        /// <summary>Distance of the antenna from the edge of the view (room for the altitude scale when shown).</summary>
        protected double LeftMargin => Options.ShowAltitudeScale ? 70 : 24;

        /// <summary>
        /// Horizontal scale and shift: the antenna at <see cref="LeftMargin"/>, the end of the range 50 px from the
        /// other edge.
        /// </summary>
        protected void CalculateHorizontalScale()
        {
            double antenna = Radar.AntennaFromRunwayEnd(Runway);
            double span = Math.Max(0.1, Runway.Distance + Runway.LengthNM - antenna);
            xscale = Math.Max(1e-6, Canvas.ActualWidth - 50 - LeftMargin) / span;
            XShift = LeftMargin - antenna * xscale;
        }

        protected double ToScreenX(double x)
        {
            x += XShift;
            return RunwayOnRight ? Canvas.ActualWidth - x : x;
        }

        protected double ToScreenY(double y)
        {
            return RunwayOnRight && FlipVertically ? Canvas.ActualHeight - y : y;
        }

        /// <summary>
        /// Adds a static line. Coordinates are logical (see class description).
        /// </summary>
        /// <summary>
        /// Adds a static line with the colour, width and dash style of an element; <paramref name="dashed"/> forces a
        /// dashed line (e.g. glide path between touchdown and threshold).
        /// </summary>
        protected void AddLine(double x1, double y1, double x2, double y2, StyleElement element, bool dashed = false)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = Options.Theme.Brush(element),
                StrokeThickness = Options.Theme.Width(element),
                StrokeDashArray = Theme.DashArray(dashed ? LineDash.Dashed : Options.Theme.Dash(element)),
                IsHitTestVisible = false
            };
            AddStatic(line);
        }

        protected Brush Brush(StyleElement element) => Options.Theme.Brush(element);

        /// <summary>Reminders of the runway within the displayed range.</summary>
        protected List<DistanceReminder> VisibleReminders()
        {
            return Options.Reminders(Runway).Where(r => r.Distance > 0 && r.Distance <= Runway.Distance + 1e-6).ToList();
        }

        /// <summary>True if a reminder line replaces the range mark at this distance.</summary>
        protected static bool HasReminderLine(List<DistanceReminder> reminders, double distance)
        {
            return reminders.Any(r => r.HasLine && Math.Abs(r.Distance - distance) < 0.005);
        }

        /// <summary>Colour of a reminder: its own, or the phosphor on the analog scope.</summary>
        private Brush ReminderBrush(DistanceReminder reminder)
        {
            if (Options.Theme.IsAnalog) return Options.Theme.Brush(StyleElement.TrackInside);
            SolidColorBrush brush = new(ColorText.Parse(reminder.Color, System.Windows.Media.Colors.Orange));
            brush.Freeze();
            return brush;
        }

        /// <summary>Line of a reminder (logical coordinates), with its note as tooltip.</summary>
        protected void AddReminderLine(DistanceReminder reminder, double x1, double y1, double x2, double y2)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = ReminderBrush(reminder),
                StrokeThickness = reminder.Width,
                StrokeDashArray = Theme.DashArray(reminder.Dash),
                IsHitTestVisible = false
            };
            AddStatic(line);
            // Invisible wider line on top: the tooltip shows anywhere near the line, not only on its few pixels.
            Line hitArea = new()
            {
                X1 = line.X1,
                Y1 = line.Y1,
                X2 = line.X2,
                Y2 = line.Y2,
                Stroke = Brushes.Transparent,
                StrokeThickness = Math.Max(12, reminder.Width + 8),
                ToolTip = reminder.ToolTip()
            };
            ToolTips.KeepOpen(hitArea);
            AddStatic(hitArea);
        }

        /// <summary>Marker of a reminder centred on a logical point, with its note as tooltip.</summary>
        protected void AddReminderMarker(DistanceReminder reminder, double x, double y)
        {
            Brush brush = ReminderBrush(reminder);
            Path path = new()
            {
                Data = Symbols.Create(reminder.Symbol, reminder.Size),
                Stroke = brush,
                StrokeThickness = 2,
                Fill = Symbols.IsFilled(reminder.Symbol) ? brush : null,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(path, ToScreenX(x));
            Canvas.SetTop(path, ToScreenY(y));
            AddStatic(path);
            // Invisible square around the symbol: the tooltip shows on the whole symbol and a little around it.
            double side = Math.Max(20, reminder.Size + 10);
            Border hitArea = new()
            {
                Width = side,
                Height = side,
                Background = Brushes.Transparent,
                ToolTip = reminder.ToolTip()
            };
            ToolTips.KeepOpen(hitArea);
            Canvas.SetLeft(hitArea, ToScreenX(x) - side / 2);
            Canvas.SetTop(hitArea, ToScreenY(y) - side / 2);
            AddStatic(hitArea);
        }

        protected void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness, bool dashed = false)
        {
            Line line = new()
            {
                X1 = ToScreenX(x1),
                Y1 = ToScreenY(y1),
                X2 = ToScreenX(x2),
                Y2 = ToScreenY(y2),
                Stroke = stroke,
                StrokeThickness = thickness
            };
            if (dashed)
            {
                line.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            AddStatic(line);
        }

        /// <summary>
        /// Adds a static text anchored at a logical point; <paramref name="dx"/> is a screen offset of its left side.
        /// With <paramref name="aboveAnchor"/> the text's bottom is at the anchor, otherwise its top.
        /// </summary>
        protected void AddText(string text, double x, double y, double dx, Brush foreground, bool aboveAnchor)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = Options.RangeMarks.TextSize,
                Foreground = foreground
            };
            double top = ToScreenY(y);
            if (aboveAnchor)
            {
                top -= TextHeight(textBlock);
            }
            Canvas.SetLeft(textBlock, ToScreenX(x) + dx);
            Canvas.SetTop(textBlock, top);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Adds a static text near the side of the view where the runway is, vertically centred on logical y.
        /// </summary>
        protected void AddSideText(string text, double y, Brush foreground)
        {
            TextBlock textBlock = new()
            {
                Text = text,
                FontSize = 11,
                Foreground = foreground
            };
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double left = RunwayOnRight ? Canvas.ActualWidth - 12 - textBlock.DesiredSize.Width : 12;
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, ToScreenY(y) - textBlock.DesiredSize.Height / 2);
            AddStatic(textBlock);
        }

        /// <summary>
        /// Adds a static symbol of the library centred on a logical point.
        /// </summary>
        protected void AddSymbol(SymbolSetting symbol, double x, double y, Brush brush)
        {
            if (symbol.Shape == SymbolShape.None) return;
            Path path = new()
            {
                Data = Symbols.Create(symbol.Shape, symbol.Size),
                Stroke = brush,
                StrokeThickness = 2,
                Fill = Symbols.IsFilled(symbol.Shape) ? brush : null,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(path, ToScreenX(x));
            Canvas.SetTop(path, ToScreenY(y));
            AddStatic(path);
        }

        private void AddStatic(UIElement element)
        {
            Panel.SetZIndex(element, StaticZIndex);
            Canvas.Children.Add(element);
            staticElements.Add(element);
        }

        protected static double TextHeight(TextBlock textBlock)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Height;
        }
    }
}
