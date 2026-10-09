using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>
    /// Editor of a custom symbol: drawn on a grid of 15 × 15 cells (click or drag), which gives a vector path; the path
    /// can also be typed or corrected by hand (SVG / WPF path syntax, box of 15 units centred on 0, 0). Preview at
    /// three sizes. The colour and the size are those of the symbol in the settings.
    /// </summary>
    internal sealed class SymbolEditorWindow : Window
    {
        private const double CellSize = 18;
        private static readonly Brush CellOn = Brushes.Black;
        private static readonly Brush CellOff = Brushes.White;
        private static readonly Brush PreviewBrush = new SolidColorBrush(Color.FromRgb(0x30, 0xE0, 0x30));

        private readonly int box = Symbols.CustomBox;
        private readonly Border[] cells;
        private readonly char[] state;
        private readonly TextBox pathBox = new()
        {
            Height = 70,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas")
        };
        private readonly TextBlock pathNote = new() { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        private readonly CheckBox filledCheck = new() { Content = "Filled (otherwise only the outline)", Margin = new Thickness(0, 6, 0, 0) };
        private readonly StackPanel previews = new() { Orientation = Orientation.Horizontal, Background = Brushes.Black, Height = 70 };
        private bool painting;
        private char paintValue;
        private bool updating;
        /// <summary>The path was typed by hand (the grid no longer matches it).</summary>
        private bool typed;

        public string? ResultPath { get; private set; }
        public string? ResultCells { get; private set; }
        public bool ResultFilled { get; private set; }

        internal SymbolEditorWindow(string title, SymbolSetting current)
        {
            Title = $"Aurora PAR - Custom symbol: {title}";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            cells = new Border[box * box];
            state = (current.Cells is string saved && saved.Length == box * box ? saved : new string('0', box * box)).ToCharArray();

            // Fixed width: the path box wraps its text instead of widening the window.
            StackPanel root = new() { Margin = new Thickness(10), Width = box * CellSize + 12 + 200 };
            root.Children.Add(new TextBlock
            {
                Text = "Draw on the grid (click or drag; a drawn cell clears it), or type the path below.",
                Margin = new Thickness(0, 0, 0, 6)
            });
            Grid grid = new() { Width = box * CellSize, Height = box * CellSize, HorizontalAlignment = HorizontalAlignment.Left, Background = CellOff, Cursor = Cursors.Pen };
            for (int i = 0; i < box; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }
            for (int i = 0; i < box * box; i++)
            {
                Border cell = new()
                {
                    BorderBrush = (i / box == box / 2 || i % box == box / 2) ? Brushes.LightSteelBlue : Brushes.Gainsboro,
                    BorderThickness = new Thickness(0.5),
                    Background = state[i] == '1' ? CellOn : CellOff
                };
                Grid.SetRow(cell, i / box);
                Grid.SetColumn(cell, i % box);
                grid.Children.Add(cell);
                cells[i] = cell;
            }
            grid.MouseLeftButtonDown += (s, e) =>
            {
                int index = CellAt(e.GetPosition(grid));
                if (index < 0) return;
                painting = true;
                paintValue = state[index] == '1' ? '0' : '1';
                grid.CaptureMouse();
                Paint(index);
            };
            grid.MouseMove += (s, e) =>
            {
                if (painting) Paint(CellAt(e.GetPosition(grid)));
            };
            grid.MouseLeftButtonUp += (s, e) =>
            {
                painting = false;
                grid.ReleaseMouseCapture();
            };

            StackPanel top = new() { Orientation = Orientation.Horizontal };
            top.Children.Add(grid);
            StackPanel side = new() { Margin = new Thickness(12, 0, 0, 0), Width = 200 };
            side.Children.Add(new TextBlock { Text = "Preview (12, 24, 48 px):" });
            side.Children.Add(previews);
            Button clear = new() { Content = "Clear the grid", Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left };
            clear.Click += (s, e) =>
            {
                for (int i = 0; i < state.Length; i++) state[i] = '0';
                typed = false;
                RefreshCells();
                FromGrid();
            };
            side.Children.Add(clear);
            side.Children.Add(filledCheck);
            side.Children.Add(new TextBlock
            {
                Text = "The colour and the size are those of the symbol in the settings. The centre of the grid (blue lines) is the position of the aircraft.",
                Foreground = Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });
            top.Children.Add(side);
            root.Children.Add(top);

            root.Children.Add(new TextBlock { Text = "Path (SVG / WPF syntax, box of 15 units centred on 0,0, e.g. M -6,0 L 6,0 M 0,-6 L 0,6):", Margin = new Thickness(0, 10, 0, 2) });
            root.Children.Add(pathBox);
            root.Children.Add(pathNote);

            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            Button ok = new() { Content = "OK", Width = 80, Height = 26, IsDefault = true };
            ok.Click += (s, e) =>
            {
                if (Symbols.CreateCustom(pathBox.Text, 24) == null)
                {
                    System.Media.SystemSounds.Beep.Play();
                    pathNote.Text = "The path is empty or not valid.";
                    pathNote.Foreground = Brushes.DarkRed;
                    return;
                }
                ResultPath = pathBox.Text.Trim();
                ResultCells = typed ? null : new string(state);
                ResultFilled = filledCheck.IsChecked == true;
                DialogResult = true;
            };
            Button cancel = new() { Content = "Cancel", Width = 80, Height = 26, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            updating = true;
            filledCheck.IsChecked = current.Shape == SymbolShape.Custom ? current.Filled : true;
            if (current.Shape == SymbolShape.Custom && current.Cells == null && !string.IsNullOrWhiteSpace(current.Path))
            {
                typed = true;
                pathBox.Text = current.Path;
            }
            else
            {
                pathBox.Text = Symbols.PathFromCells(new string(state));
            }
            updating = false;
            pathBox.TextChanged += (s, e) =>
            {
                if (updating) return;
                typed = true;
                ShowPreview();
            };
            filledCheck.Click += (s, e) => ShowPreview();
            ShowPreview();
        }

        private int CellAt(Point p)
        {
            int column = (int)(p.X / CellSize);
            int row = (int)(p.Y / CellSize);
            return column < 0 || row < 0 || column >= box || row >= box ? -1 : row * box + column;
        }

        private void Paint(int index)
        {
            if (index < 0 || state[index] == paintValue) return;
            state[index] = paintValue;
            cells[index].Background = paintValue == '1' ? CellOn : CellOff;
            typed = false;
            FromGrid();
        }

        private void RefreshCells()
        {
            for (int i = 0; i < cells.Length; i++) cells[i].Background = state[i] == '1' ? CellOn : CellOff;
        }

        /// <summary>The path of the drawn cells (cells are filled squares: the shape is drawn filled).</summary>
        private void FromGrid()
        {
            updating = true;
            pathBox.Text = Symbols.PathFromCells(new string(state));
            filledCheck.IsChecked = true;
            updating = false;
            ShowPreview();
        }

        private void ShowPreview()
        {
            previews.Children.Clear();
            bool valid = Symbols.CreateCustom(pathBox.Text, 24) != null;
            pathNote.Text = valid ? (typed ? "Typed path (the grid does not show it)." : "Path of the drawing.") : "Empty or not a valid path.";
            pathNote.Foreground = valid ? Brushes.Gray : Brushes.DarkRed;
            foreach (double size in new[] { 12.0, 24.0, 48.0 })
            {
                Canvas canvas = new() { Width = 64, Height = 70 };
                if (Symbols.CreateCustom(pathBox.Text, size) is Geometry geometry)
                {
                    System.Windows.Shapes.Path path = new()
                    {
                        Data = geometry,
                        Stroke = PreviewBrush,
                        StrokeThickness = 1.5,
                        Fill = filledCheck.IsChecked == true ? PreviewBrush : null
                    };
                    Canvas.SetLeft(path, 32);
                    Canvas.SetTop(path, 35);
                    canvas.Children.Add(path);
                }
                previews.Children.Add(canvas);
            }
        }
    }
}
