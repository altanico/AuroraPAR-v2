using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AuroraPAR
{
    /// <summary>
    /// Profile management and settings. Every change is applied to the screen and saved immediately.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings settings;
        private readonly Action applyToScreen;
        /// <summary>
        /// True while the controls are being filled from the settings, so their change events are ignored.
        /// </summary>
        private bool refreshing;
        /// <summary>
        /// Actions that fill the number fields from the active profile.
        /// </summary>
        private readonly List<Action> refreshers = [];

        internal SettingsWindow(AppSettings settings, Action applyToScreen, Action<Window>? editRunways = null)
        {
            InitializeComponent();
            this.settings = settings;
            this.applyToScreen = applyToScreen;
            EditRunwaysButton.Visibility = editRunways == null ? Visibility.Collapsed : Visibility.Visible;
            EditRunwaysButton.Click += (s, e) => editRunways?.Invoke(this);
            ProfileComboBox.SelectionChanged += ProfileComboBox_SelectionChanged;
            RenameButton.Click += (s, e) => RenameProfile();
            ProfileNameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) RenameProfile();
            };
            NewButton.Click += (s, e) => DuplicateProfile();
            DeleteButton.Click += (s, e) => DeleteProfile();
            ImportButton.Click += (s, e) => ImportProfile();
            ExportButton.Click += (s, e) => ExportProfile();
            ModernRadio.Checked += (s, e) => SetProfileValue(p => p.DisplayMode == DisplayMode.Modern, p => p.DisplayMode = DisplayMode.Modern);
            AnalogRadio.Checked += (s, e) => SetProfileValue(p => p.DisplayMode == DisplayMode.Analog, p => p.DisplayMode = DisplayMode.Analog);
            RunwayLeftRadio.Checked += (s, e) => SetRunwaySide(RunwaySide.Left);
            RunwayRightRadio.Checked += (s, e) => SetRunwaySide(RunwaySide.Right);
            FixedRangeComboBox.ItemsSource = Ranges.Values;
            StartRangeLastRadio.Checked += (s, e) => SetStartupRange(StartupRange.LastUsed);
            StartRangeRunwayRadio.Checked += (s, e) => SetStartupRange(StartupRange.RunwayDefault);
            StartRangeFixedRadio.Checked += (s, e) => SetStartupRange(StartupRange.Fixed);
            ChangeRangeKeepRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.KeepCurrent);
            ChangeRangeRunwayRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.RunwayDefault);
            ChangeRangeFixedRadio.Checked += (s, e) => SetRunwayChangeRange(RunwayChangeRange.Fixed);
            FixedRangeComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || FixedRangeComboBox.SelectedItem is not double range) return;
                Active.PreferredRange = range;
                Commit();
            };
            CloseButton.Click += (s, e) => Close();
            ScanEffectCheck.Checked += (s, e) => SetProfileValue(p => p.ScanEffect, p => p.ScanEffect = true);
            ScanEffectCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ScanEffect, p => p.ScanEffect = false);
            ScanEffectSpeedComboBox.ItemsSource = Enum.GetValues<ScanEffectSpeed>().Select(ScanEffect.DisplayName).ToList();
            ScanEffectSpeedComboBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || ScanEffectSpeedComboBox.SelectedIndex < 0) return;
                ScanEffectSpeed speed = Enum.GetValues<ScanEffectSpeed>()[ScanEffectSpeedComboBox.SelectedIndex];
                SetProfileValue(p => p.ScanEffectSpeed == speed, p => p.ScanEffectSpeed = speed);
            };
            BuildRadarFields();
            BuildSymbolRows();
            HistoryCheck.Checked += (s, e) => SetProfileValue(p => p.HistoryEnabled, p => p.HistoryEnabled = true);
            HistoryCheck.Unchecked += (s, e) => SetProfileValue(p => !p.HistoryEnabled, p => p.HistoryEnabled = false);
            HistoryDotsBox.LostFocus += (s, e) => ApplyHistoryDots();
            HistoryIntervalBox.LostFocus += (s, e) => ApplyHistoryInterval();
            HistoryIntervalBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyHistoryInterval();
            };
            MagVarBox.LostFocus += (s, e) => ApplyMagneticVariation();
            MagVarBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyMagneticVariation();
            };
            HistoryDotsBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplyHistoryDots();
            };
            DisplayStyleButton.Click += (s, e) =>
            {
                DisplayStyleWindow window = new(settings, Commit) { Owner = this };
                window.ShowDialog();
            };
            EditLabelsButton.Click += (s, e) =>
            {
                LabelEditorWindow editor = new(settings, Commit) { Owner = this };
                editor.ShowDialog();
            };
            QnhRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QNH, p => p.PressureReference = PressureReference.QNH);
            QfeRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QFE, p => p.PressureReference = PressureReference.QFE);
            HpaRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.HectoPascal, p => p.PressureUnit = PressureUnit.HectoPascal);
            InHgRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.InchesOfMercury, p => p.PressureUnit = PressureUnit.InchesOfMercury);
            DaDhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.DaDh, p => p.MinimaLabel = MinimaLabel.DaDh);
            OcaOchRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.OcaOch, p => p.MinimaLabel = MinimaLabel.OcaOch);
            MdaMdhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.MdaMdh, p => p.MinimaLabel = MinimaLabel.MdaMdh);
            AltitudeScaleCheck.Checked += (s, e) => SetProfileValue(p => p.ShowAltitudeScale, p => p.ShowAltitudeScale = true);
            AltitudeScaleCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowAltitudeScale, p => p.ShowAltitudeScale = false);
            AltitudeLinesCheck.Checked += (s, e) => SetProfileValue(p => p.ShowAltitudeLines, p => p.ShowAltitudeLines = true);
            AltitudeLinesCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowAltitudeLines, p => p.ShowAltitudeLines = false);
            ScaleFeetRadio.Checked += (s, e) => SetProfileValue(p => p.AltitudeScaleUnit == LengthUnit.Feet, p => p.AltitudeScaleUnit = LengthUnit.Feet);
            ScaleMetresRadio.Checked += (s, e) => SetProfileValue(p => p.AltitudeScaleUnit == LengthUnit.Metres, p => p.AltitudeScaleUnit = LengthUnit.Metres);
            FileLocationText.Text = $"Settings file{(SettingsStore.IsPortable ? " (portable mode)" : "")}: {SettingsStore.FilePath}";
            RefreshControls();
        }

        private Profile Active => settings.Active;

        /// <summary>
        /// Fills the controls from the active profile.
        /// </summary>
        private void RefreshControls()
        {
            refreshing = true;
            try
            {
                ProfileComboBox.ItemsSource = settings.Profiles.Select(p => p.Name).ToList();
                ProfileComboBox.SelectedItem = Active.Name;
                ProfileNameTextBox.Text = Active.Name;
                DeleteButton.IsEnabled = settings.Profiles.Count > 1;
                ModernRadio.IsChecked = Active.DisplayMode == DisplayMode.Modern;
                AnalogRadio.IsChecked = Active.DisplayMode == DisplayMode.Analog;
                RunwayLeftRadio.IsChecked = Active.RunwaySide == RunwaySide.Left;
                RunwayRightRadio.IsChecked = Active.RunwaySide == RunwaySide.Right;
                StartRangeLastRadio.IsChecked = Active.StartupRange == StartupRange.LastUsed;
                StartRangeRunwayRadio.IsChecked = Active.StartupRange == StartupRange.RunwayDefault;
                StartRangeFixedRadio.IsChecked = Active.StartupRange == StartupRange.Fixed;
                ChangeRangeKeepRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.KeepCurrent;
                ChangeRangeRunwayRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.RunwayDefault;
                ChangeRangeFixedRadio.IsChecked = Active.RunwayChangeRange == RunwayChangeRange.Fixed;
                FixedRangeComboBox.SelectedIndex = Ranges.IndexOfClosest(Active.PreferredRange);
                QnhRadio.IsChecked = Active.PressureReference == PressureReference.QNH;
                QfeRadio.IsChecked = Active.PressureReference == PressureReference.QFE;
                HpaRadio.IsChecked = Active.PressureUnit == PressureUnit.HectoPascal;
                InHgRadio.IsChecked = Active.PressureUnit == PressureUnit.InchesOfMercury;
                DaDhRadio.IsChecked = Active.MinimaLabel == MinimaLabel.DaDh;
                OcaOchRadio.IsChecked = Active.MinimaLabel == MinimaLabel.OcaOch;
                MdaMdhRadio.IsChecked = Active.MinimaLabel == MinimaLabel.MdaMdh;
                AltitudeScaleCheck.IsChecked = Active.ShowAltitudeScale;
                AltitudeLinesCheck.IsChecked = Active.ShowAltitudeLines;
                ScaleFeetRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Feet;
                ScaleMetresRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Metres;
                HistoryCheck.IsChecked = Active.HistoryEnabled;
                ScanEffectCheck.IsChecked = Active.ScanEffect;
                ScanEffectSpeedComboBox.SelectedIndex = Array.IndexOf(Enum.GetValues<ScanEffectSpeed>(), Active.ScanEffectSpeed);
                HistoryDotsBox.Text = Active.HistoryDots.ToString(CultureInfo.InvariantCulture);
                HistoryIntervalBox.Text = Active.HistoryInterval.ToString("0.#", CultureInfo.InvariantCulture);
                MagVarBox.Text = MagneticVariation.Format(Active.MagneticVariation);
                MagVarNote.Text = "e.g. 3E or 2W";
                MagVarNote.Foreground = Brushes.Gray;
                foreach (Action refresh in refreshers)
                {
                    refresh();
                }
            }
            finally
            {
                refreshing = false;
            }
        }

        /// <summary>
        /// Saves the settings, applies them to the screen and refreshes the controls.
        /// </summary>
        private void Commit()
        {
            string? error = SettingsStore.Save(settings);
            if (error != null)
            {
                MessageBox.Show(this, $"Cannot save the settings file {SettingsStore.FilePath}:\n{error}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            applyToScreen();
            RefreshControls();
        }

        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (refreshing || ProfileComboBox.SelectedItem is not string name) return;
            settings.ActiveProfile = name;
            Commit();
        }

        private void RenameProfile()
        {
            string name = ProfileNameTextBox.Text.Trim();
            if (name == Active.Name) return;
            if (name.Length == 0)
            {
                MessageBox.Show(this, "The profile name cannot be empty.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (settings.Profiles.Any(p => p != Active && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, $"A profile named \"{name}\" already exists.", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Active.Name = name;
            settings.ActiveProfile = name;
            Commit();
        }

        private void DuplicateProfile()
        {
            Profile copy = Active.Clone();
            copy.Name = settings.UniqueName(Active.Name + " copy");
            settings.Profiles.Add(copy);
            settings.ActiveProfile = copy.Name;
            Commit();
        }

        private void DeleteProfile()
        {
            if (settings.Profiles.Count <= 1) return;
            if (MessageBox.Show(this, $"Delete the profile \"{Active.Name}\"?", "Aurora PAR", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            settings.Profiles.Remove(Active);
            settings.ActiveProfile = settings.Profiles[0].Name;
            Commit();
        }

        private void ImportProfile()
        {
            OpenFileDialog dialog = new()
            {
                Title = "Import profile",
                Filter = "AuroraPAR profile (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                Profile profile = SettingsStore.ImportProfile(dialog.FileName);
                profile.Name = settings.UniqueName(profile.Name.Trim());
                settings.Profiles.Add(profile);
                settings.ActiveProfile = profile.Name;
                Commit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot import the profile:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportProfile()
        {
            SaveFileDialog dialog = new()
            {
                Title = "Export profile",
                Filter = "AuroraPAR profile (*.json)|*.json",
                FileName = SafeFileName(Active.Name) + ".json"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                SettingsStore.ExportProfile(Active, dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Cannot export the profile:\n{ex.Message}", "Aurora PAR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetRunwaySide(RunwaySide side)
        {
            if (refreshing || Active.RunwaySide == side) return;
            Active.RunwaySide = side;
            Commit();
        }

        /// <summary>
        /// Changes a profile option from a radio button or check box, unless it already has that value.
        /// </summary>
        private void SetProfileValue(Func<Profile, bool> alreadySet, Action<Profile> set)
        {
            if (refreshing || alreadySet(Active)) return;
            set(Active);
            Commit();
        }

        private void ApplyMagneticVariation()
        {
            if (refreshing) return;
            string text = MagVarBox.Text.Trim();
            if (text.Length == 0) text = "0";
            if (!MagneticVariation.TryParse(text, out double variation))
            {
                MagVarNote.Text = "not valid: e.g. 3E, 2.5W or -2";
                MagVarNote.Foreground = Brushes.DarkRed;
                return;
            }
            if (variation != Active.MagneticVariation)
            {
                Active.MagneticVariation = variation;
                Commit();
            }
            else
            {
                RefreshControls();
            }
        }

        private void ApplyHistoryInterval()
        {
            if (refreshing) return;
            if (double.TryParse(HistoryIntervalBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && seconds >= Profile.MinHistoryInterval && seconds <= Profile.MaxHistoryInterval)
            {
                if (seconds != Active.HistoryInterval)
                {
                    Active.HistoryInterval = seconds;
                    Commit();
                }
                return;
            }
            System.Media.SystemSounds.Beep.Play();
            HistoryIntervalBox.Text = Active.HistoryInterval.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private void ApplyHistoryDots()
        {
            if (refreshing) return;
            if (int.TryParse(HistoryDotsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dots)
                && dots >= Profile.MinHistoryDots && dots <= Profile.MaxHistoryDots)
            {
                if (dots != Active.HistoryDots)
                {
                    Active.HistoryDots = dots;
                    Commit();
                }
                return;
            }
            System.Media.SystemSounds.Beep.Play();
            HistoryDotsBox.Text = Active.HistoryDots.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One row per symbol (track, threshold, touchdown point, antenna): shape and size.
        /// </summary>
        private void BuildSymbolRows()
        {
            SymbolShape[] all = Enum.GetValues<SymbolShape>();
            AddSymbolRow("Track", p => p.TrackSymbol, all.Where(s => s != SymbolShape.None && s != SymbolShape.Line).ToArray());
            AddSymbolRow("Coasting track", p => p.CoastSymbol, all.Where(s => s != SymbolShape.None && s != SymbolShape.Line).ToArray());
            AddSymbolRow("History dots", p => p.HistorySymbol, all.Where(s => s != SymbolShape.None).ToArray());
            AddSymbolRow("Threshold", p => p.ThresholdSymbol, all);
            AddSymbolRow("Touchdown point", p => p.TouchdownSymbol, all);
            AddSymbolRow("Antenna", p => p.AntennaSymbol, all);
        }

        private void AddSymbolRow(string label, Func<Profile, SymbolSetting> get, SymbolShape[] shapes)
        {
            int row = SymbolGrid.RowDefinitions.Count;
            SymbolGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
            ComboBox shapeBox = new()
            {
                ItemsSource = shapes.Select(shape => new SymbolChoice(shape)).ToList(),
                ItemTemplate = SymbolTemplate,
                Margin = new Thickness(0, 2, 6, 2)
            };
            TextBox sizeBox = new()
            {
                Width = 40,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 4, 2),
                ToolTip = "Size in pixels (2 to 60)"
            };
            TextBlock unit = new() { Text = "px", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, row);
            Grid.SetRow(shapeBox, row);
            Grid.SetRow(sizeBox, row);
            Grid.SetRow(unit, row);
            Grid.SetColumn(shapeBox, 1);
            Grid.SetColumn(sizeBox, 2);
            Grid.SetColumn(unit, 3);
            SymbolGrid.Children.Add(text);
            SymbolGrid.Children.Add(shapeBox);
            SymbolGrid.Children.Add(sizeBox);
            SymbolGrid.Children.Add(unit);
            shapeBox.SelectionChanged += (s, e) =>
            {
                if (refreshing || shapeBox.SelectedIndex < 0) return;
                SymbolShape shape = shapes[shapeBox.SelectedIndex];
                if (shape == get(Active).Shape) return;
                get(Active).Shape = shape;
                Commit();
            };
            void ApplySize()
            {
                if (refreshing) return;
                if (double.TryParse(sizeBox.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double size)
                    && size >= 2 && size <= 60)
                {
                    if (size != get(Active).Size)
                    {
                        get(Active).Size = size;
                        Commit();
                    }
                    return;
                }
                System.Media.SystemSounds.Beep.Play();
                sizeBox.Text = FormatNumber(get(Active).Size);
            }
            sizeBox.LostFocus += (s, e) => ApplySize();
            sizeBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) ApplySize();
            };
            refreshers.Add(() =>
            {
                shapeBox.SelectedIndex = Array.IndexOf(shapes, get(Active).Shape);
                sizeBox.Text = FormatNumber(get(Active).Size);
            });
        }

        /// <summary>
        /// An item of the symbol lists: the drawn symbol followed by its name.
        /// </summary>
        public sealed class SymbolChoice
        {
            internal SymbolChoice(SymbolShape shape)
            {
                Name = Symbols.DisplayName(shape);
                Icon = Symbols.Create(shape, 14);
                Fill = Symbols.IsFilled(shape) ? Brushes.Black : null;
            }

            public string Name { get; }
            public Geometry Icon { get; }
            public Brush? Fill { get; }
        }

        /// <summary>
        /// Shows a symbol choice as the drawn symbol (centred in a small box) and its name.
        /// </summary>
        private static readonly DataTemplate SymbolTemplate = CreateSymbolTemplate();

        private static DataTemplate CreateSymbolTemplate()
        {
            FrameworkElementFactory panel = new(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            FrameworkElementFactory box = new(typeof(Canvas));
            box.SetValue(WidthProperty, 22.0);
            box.SetValue(HeightProperty, 18.0);
            box.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
            FrameworkElementFactory path = new(typeof(System.Windows.Shapes.Path));
            path.SetValue(Canvas.LeftProperty, 11.0);
            path.SetValue(Canvas.TopProperty, 9.0);
            path.SetValue(System.Windows.Shapes.Shape.StrokeProperty, Brushes.Black);
            path.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
            path.SetBinding(System.Windows.Shapes.Path.DataProperty, new Binding(nameof(SymbolChoice.Icon)));
            path.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(SymbolChoice.Fill)));
            box.AppendChild(path);
            FrameworkElementFactory text = new(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(SymbolChoice.Name)));
            text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(box);
            panel.AppendChild(text);
            DataTemplate template = new() { VisualTree = panel };
            template.Seal();
            return template;
        }

        private void BuildRadarFields()
        {
            AddHeader("Approach limits (track green inside, red outside)");
            AddNumberField("Above the glide path", p => p.ApproachAbove, (p, v) => p.ApproachAbove = v, 0.1, 5);
            AddNumberField("Below the glide path", p => p.ApproachBelow, (p, v) => p.ApproachBelow = v, 0.1, 5);
            AddNumberField("Left of the centreline", p => p.ApproachLeft, (p, v) => p.ApproachLeft = v, 0.1, 10);
            AddNumberField("Right of the centreline", p => p.ApproachRight, (p, v) => p.ApproachRight = v, 0.1, 10);
            AddHeader("Scan limits from the antenna (physical limits, fixed)");
            AddNumberField("Up", p => p.ScanUp, (p, v) => p.ScanUp = v, 1, Profile.MaxScanAngle, (p, v) => v > p.ScanDown);
            AddNumberField("Down (negative = below the horizon)", p => p.ScanDown, (p, v) => p.ScanDown = v, -Profile.MaxScanAngle, 30, (p, v) => v < p.ScanUp);
            AddNumberField("Left", p => p.ScanLeft, (p, v) => p.ScanLeft = v, 1, Profile.MaxScanAngle);
            AddNumberField("Right", p => p.ScanRight, (p, v) => p.ScanRight = v, 1, Profile.MaxScanAngle);
            AddHeader("Antenna beam (only the traffic inside the beam is seen)");
            AddNumberField("Elevation width", p => p.BeamElevation, (p, v) => p.BeamElevation = v, 0.5, 90);
            AddNumberField("Azimuth width", p => p.BeamAzimuth, (p, v) => p.BeamAzimuth = v, 0.5, 180);
            AddNumberField("Elevation centre in neutral", p => p.BeamElevationNeutral, (p, v) => p.BeamElevationNeutral = v, -30, 60);
            AddNumberField("Azimuth centre in neutral (+ right)", p => p.BeamAzimuthNeutral, (p, v) => p.BeamAzimuthNeutral = v, -90, 90);
            AddHeader("Antenna tilt (moves the beam inside the scan limits)");
            AddNumberField("Step", p => p.TiltStep, (p, v) => p.TiltStep = v, 0.5, 10);
            AddHeader("Coasting tracks (modern display)");
            AddNumberField("Seconds shown out of the beam (0 = none)", p => p.CoastSeconds, (p, v) => p.CoastSeconds = v, 0, Profile.MaxCoastSeconds, unitText: "s");
            int row = RadarGrid.RowDefinitions.Count;
            RadarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            CheckBox swap = new()
            {
                Content = "Azimuth: swap left and right (as seen from the runway)",
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Not ticked: L/R of the azimuth tilt as seen by the pilot flying the approach (default).\n"
                    + "Ticked: as seen from the runway looking at the approach (controller's view): R is the pilot's left.\n"
                    + "Applies to the AZ TILT knob, the arrow keys, the AZ buttons and the L/R readouts."
            };
            swap.Checked += (s, e) => SetProfileValue(p => p.AzimuthTiltSwapped, p => p.AzimuthTiltSwapped = true);
            swap.Unchecked += (s, e) => SetProfileValue(p => !p.AzimuthTiltSwapped, p => p.AzimuthTiltSwapped = false);
            refreshers.Add(() => swap.IsChecked = Active.AzimuthTiltSwapped);
            Grid.SetRow(swap, row);
            Grid.SetColumnSpan(swap, 3);
            RadarGrid.Children.Add(swap);
        }

        private void AddHeader(string text)
        {
            int row = RadarGrid.RowDefinitions.Count;
            RadarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock header = new()
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, row == 0 ? 0 : 8, 0, 2)
            };
            Grid.SetRow(header, row);
            Grid.SetColumnSpan(header, 3);
            RadarGrid.Children.Add(header);
        }

        /// <summary>
        /// Adds a number field bound to a profile value. The value is applied with Enter or when leaving the field;
        /// an invalid value is refused (the previous one comes back).
        /// </summary>
        private void AddNumberField(string label, Func<Profile, double> get, Action<Profile, double> set, double min, double max,
                                    Func<Profile, double, bool>? isValid = null, string unitText = "°")
        {
            int row = RadarGrid.RowDefinitions.Count;
            RadarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock text = new()
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 8, 2)
            };
            TextBox box = new()
            {
                Width = 60,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 2, 4, 2),
                ToolTip = $"{FormatNumber(min)} to {FormatNumber(max)}"
            };
            TextBlock unit = new()
            {
                Text = unitText,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, row);
            Grid.SetRow(box, row);
            Grid.SetRow(unit, row);
            Grid.SetColumn(box, 1);
            Grid.SetColumn(unit, 2);
            RadarGrid.Children.Add(text);
            RadarGrid.Children.Add(box);
            RadarGrid.Children.Add(unit);
            void Apply()
            {
                if (refreshing) return;
                if (double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    && value >= min && value <= max && (isValid == null || isValid(Active, value)))
                {
                    if (value != get(Active))
                    {
                        set(Active, value);
                        Commit();
                    }
                    return;
                }
                System.Media.SystemSounds.Beep.Play();
                box.Text = FormatNumber(get(Active));
            }
            box.LostFocus += (s, e) => Apply();
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Apply();
            };
            refreshers.Add(() => box.Text = FormatNumber(get(Active)));
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void SetStartupRange(StartupRange startupRange)
        {
            if (refreshing || Active.StartupRange == startupRange) return;
            Active.StartupRange = startupRange;
            Commit();
        }

        private void SetRunwayChangeRange(RunwayChangeRange runwayChangeRange)
        {
            if (refreshing || Active.RunwayChangeRange == runwayChangeRange) return;
            Active.RunwayChangeRange = runwayChangeRange;
            Commit();
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
