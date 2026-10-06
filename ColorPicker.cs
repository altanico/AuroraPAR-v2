using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AuroraPAR
{
    /// <summary>Colour choice used in the settings windows.</summary>
    internal static class ColorPicker
    {
        /// <summary>Standard colours offered for the elements (any other colour can be typed as #RRGGBB).</summary>
        public static readonly (string Name, string Hex)[] StandardColors =
        [
            ("Green", "#008000"), ("Light green", "#00FF00"), ("Dark green", "#004D00"), ("Yellow", "#FFFF00"),
            ("Amber", "#FFB000"), ("Orange", "#FF8000"), ("Red", "#FF0000"), ("Dark red", "#A00000"),
            ("Cadet blue", "#5F9EA0"), ("Cyan", "#00FFFF"), ("Blue", "#4080FF"), ("Magenta", "#FF00FF"),
            ("White", "#FFFFFF"), ("Light grey", "#C0C0C0"), ("Grey", "#808080"), ("Dark grey", "#404040"), ("Black", "#000000")
        ];

        /// <summary>Phosphors of the analog scope, as on real radar screens.</summary>
        public static readonly (string Name, string Hex)[] Phosphors =
        [
            ("Yellow-green (P39)", "#A8FF60"), ("Amber / yellow", "#FFB830"), ("Green (P1)", "#50FF50"),
            ("Orange", "#FF8C30"), ("Blue-white", "#C8E8FF")
        ];

        /// <summary>
        /// Colour picker: a list of named colours with their swatch, and a box to type any colour as #RRGGBB.
        /// </summary>
        public static FrameworkElement Create(string current, (string Name, string Hex)[] choices, Action<string> changed)
        {
            StackPanel panel = new() { Orientation = Orientation.Horizontal };
            ComboBox combo = new() { Width = 160, Height = 22 };
            TextBox hexBox = new()
            {
                Width = 70,
                Height = 22,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Any colour as #RRGGBB (Enter to apply)"
            };
            List<(string Name, string Hex)> items = choices.ToList();
            string normalized = ColorText.ToHex(ColorText.Parse(current, System.Windows.Media.Colors.White));
            if (!items.Any(i => string.Equals(i.Hex, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(("Custom", normalized));
            }
            foreach ((string name, string hex) in items)
            {
                StackPanel row = new() { Orientation = Orientation.Horizontal, Tag = hex };
                row.Children.Add(new Rectangle
                {
                    Width = 14,
                    Height = 14,
                    Fill = new SolidColorBrush(ColorText.Parse(hex, System.Windows.Media.Colors.White)),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                combo.Items.Add(row);
            }
            bool updating = true;
            combo.SelectedIndex = items.FindIndex(i => string.Equals(i.Hex, normalized, StringComparison.OrdinalIgnoreCase));
            hexBox.Text = normalized;
            updating = false;
            combo.SelectionChanged += (s, e) =>
            {
                if (updating || combo.SelectedItem is not FrameworkElement item || item.Tag is not string hex) return;
                hexBox.Text = hex;
                changed(hex);
            };
            void ApplyText()
            {
                if (!ColorText.TryParse(hexBox.Text, out Color color))
                {
                    hexBox.Background = new SolidColorBrush(Color.FromRgb(255, 215, 215));
                    return;
                }
                hexBox.ClearValue(TextBox.BackgroundProperty);
                string hex = ColorText.ToHex(color);
                hexBox.Text = hex;
                int index = items.FindIndex(i => string.Equals(i.Hex, hex, StringComparison.OrdinalIgnoreCase));
                updating = true;
                combo.SelectedIndex = index;
                updating = false;
                changed(hex);
            }
            hexBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyText();
            };
            hexBox.LostFocus += (s, e) =>
            {
                if (!string.Equals(hexBox.Text, normalized, StringComparison.OrdinalIgnoreCase)) ApplyText();
            };
            panel.Children.Add(combo);
            panel.Children.Add(hexBox);
            return panel;
        }
    }
}
