using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace AuroraPAR
{
    /// <summary>
    /// Display style of the active profile: range marks at each range (and, in the next tabs, colours and lines).
    /// Every change is applied and saved at once.
    /// </summary>
    public partial class DisplayStyleWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action commit;
        private bool building;

        internal DisplayStyleWindow(AppSettings settings, Action commit)
        {
            InitializeComponent();
            this.settings = settings;
            this.commit = commit;
            CloseButton.Click += (s, e) => Close();
            RangeMarksDefaultButton.Click += (s, e) =>
            {
                Active.RangeMarks = RangeMarkSettings.Default();
                commit();
                BuildRangeMarks();
            };
            BuildRangeMarks();
        }

        private Profile Active => settings.Active;

        /// <summary>
        /// Table of the range marks: one row per display range, a check box per type of mark and the marks
        /// where the distance is written.
        /// </summary>
        private void BuildRangeMarks()
        {
            building = true;
            Grid grid = RangeMarkGrid;
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            foreach (MarkInterval _ in MarkIntervals.All)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(grid, Header("Range"), 0, 0);
            for (int c = 0; c < MarkIntervals.All.Length; c++)
            {
                AddCell(grid, Header("every " + MarkIntervals.Name(MarkIntervals.All[c])), 0, c + 1);
            }
            AddCell(grid, Header("Distance written on"), 0, MarkIntervals.All.Length + 1);

            List<string> textChoices = ["none", .. MarkIntervals.All.Select(i => "every " + MarkIntervals.Name(i))];
            int r = 1;
            foreach (RangeMarkRow row in Active.RangeMarks.Rows.OrderByDescending(x => x.Range))
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                AddCell(grid, new TextBlock
                {
                    Text = row.Range.ToString("0.#", CultureInfo.InvariantCulture) + " NM",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold
                }, r, 0);
                for (int c = 0; c < MarkIntervals.All.Length; c++)
                {
                    MarkInterval interval = MarkIntervals.All[c];
                    // Marks larger than the range would never be drawn.
                    bool possible = MarkIntervals.Nm(interval) <= row.Range + 1e-6;
                    CheckBox check = new()
                    {
                        IsChecked = row.Lines.Contains(interval),
                        IsEnabled = possible,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    RangeMarkRow target = row;
                    check.Checked += (s, e) => SetLine(target, interval, true);
                    check.Unchecked += (s, e) => SetLine(target, interval, false);
                    AddCell(grid, check, r, c + 1);
                }
                ComboBox text = new()
                {
                    ItemsSource = textChoices,
                    SelectedIndex = row.Text is MarkInterval t ? Array.IndexOf(MarkIntervals.All, t) + 1 : 0,
                    Width = 110,
                    Height = 22,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                RangeMarkRow textTarget = row;
                text.SelectionChanged += (s, e) =>
                {
                    if (building) return;
                    textTarget.Text = text.SelectedIndex <= 0 ? null : MarkIntervals.All[text.SelectedIndex - 1];
                    commit();
                };
                AddCell(grid, text, r, MarkIntervals.All.Length + 1);
                r++;
            }
            building = false;
        }

        private void SetLine(RangeMarkRow row, MarkInterval interval, bool on)
        {
            if (building) return;
            if (on && !row.Lines.Contains(interval)) row.Lines.Add(interval);
            if (!on) row.Lines.Remove(interval);
            row.Lines.Sort();
            commit();
        }

        private static TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.DimGray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Bottom
            };
        }

        private static void AddCell(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }
}
