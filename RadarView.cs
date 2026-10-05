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
        public SymbolSetting TrackSymbol { get; set; } = new(SymbolShape.CrossCircle, 12);
        public SymbolSetting ThresholdSymbol { get; set; } = new(SymbolShape.Line, 10);
        public SymbolSetting TouchdownSymbol { get; set; } = new(SymbolShape.Line, 12);
        public SymbolSetting AntennaSymbol { get; set; } = new(SymbolShape.Square, 8);
        public LabelLayout ElevationLabel { get; set; } = LabelLayout.DefaultElevation();
        public LabelLayout AzimuthLabel { get; set; } = LabelLayout.DefaultAzimuth();
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
        private const int HistoryZIndex = 9;
        private const int TrackZIndex = 11;
        private const int LabelZIndex = 12;
        private const double HistoryDotSize = 3;
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
        private Point dragStart;
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
            public readonly List<Ellipse> Dots = [];
            /// <summary>Previous positions, in world coordinates (see <see cref="ToWorld"/>), oldest first.</summary>
            public readonly List<(double Along, double Value)> History = [];
            public (double Along, double Value) LastWorld;
            public double LastLatitude = double.NaN;
            public double LastLongitude = double.NaN;
            public double LastAltitude = double.NaN;
            /// <summary>Screen position of the track.</summary>
            public Point Position;
            /// <summary>True when the label was dragged: <see cref="Offset"/> is then its top-left corner from the track.</summary>
            public bool Moved;
            public Vector Offset;
            /// <summary>Label hidden with a right click.</summary>
            public bool LabelHidden;
            public Brush Color = Brushes.Green;

            public IEnumerable<UIElement> Elements()
            {
                yield return Symbol;
                yield return Label;
                yield return Leader;
                foreach (Ellipse dot in Dots) yield return dot;
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
        protected abstract Brush TrackColor(Aircraft aircraft);
        /// <summary>Label layout of this view.</summary>
        protected abstract LabelLayout Layout { get; }
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

        private void UpdateTrack(Track track, Aircraft aircraft)
        {
            (double along, double value) = ToWorld(aircraft);
            // History: the previous position is added each time Aurora gives a new one (one "antenna sweep").
            bool changed = aircraft.Latitude != track.LastLatitude || aircraft.Longitude != track.LastLongitude || aircraft.Altitude != track.LastAltitude;
            if (changed)
            {
                if (!double.IsNaN(track.LastLatitude))
                {
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
            track.LastWorld = (along, value);

            Point logical = WorldToLogical(along, value);
            if (!Radar.IsInsideScan(aircraft, Runway) || !IsDrawable(logical))
            {
                SetTrackVisible(track, false);
                return;
            }
            track.Color = TrackColor(aircraft);
            track.Position = new Point(ToScreenX(logical.X), ToScreenY(logical.Y));
            // Track symbol.
            if (track.SymbolVersion != Options.Version)
            {
                track.Symbol.Data = Symbols.Create(Options.TrackSymbol.Shape, Options.TrackSymbol.Size);
                track.SymbolVersion = Options.Version;
            }
            track.Symbol.Stroke = track.Color;
            track.Symbol.Fill = Symbols.IsFilled(Options.TrackSymbol.Shape) ? track.Color : Brushes.Transparent;
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
            while (track.Dots.Count < count)
            {
                Ellipse dot = new() { Width = HistoryDotSize, Height = HistoryDotSize, IsHitTestVisible = false };
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
                (double along, double value) = track.History[first + i];
                Point logical = WorldToLogical(along, value);
                Ellipse dot = track.Dots[i];
                dot.Fill = track.Color;
                Canvas.SetLeft(dot, ToScreenX(logical.X) - HistoryDotSize / 2);
                Canvas.SetTop(dot, ToScreenY(logical.Y) - HistoryDotSize / 2);
                dot.Visibility = Visibility.Visible;
            }
        }

        private void UpdateLabel(Track track, Aircraft aircraft)
        {
            LabelLayout layout = Layout;
            if (!Options.ShowLabels || track.LabelHidden || layout.IsEmpty)
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
            track.Label.ToolTip = "Drag to move · double click: back to its place · right click: hide (right click on the track to show it again)";
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
            track.Symbol.MouseRightButtonUp += (s, e) =>
            {
                track.LabelHidden = false;
                e.Handled = true;
            };
            return track;
        }

        protected double ToScreenX(double x)
        {
            return RunwayOnRight ? Canvas.ActualWidth - x : x;
        }

        protected double ToScreenY(double y)
        {
            return RunwayOnRight && FlipVertically ? Canvas.ActualHeight - y : y;
        }

        /// <summary>
        /// Adds a static line. Coordinates are logical (see class description).
        /// </summary>
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
                FontSize = 12,
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
