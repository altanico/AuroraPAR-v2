using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        internal SettingsWindow(AppSettings settings, Action applyToScreen)
        {
            InitializeComponent();
            this.settings = settings;
            this.applyToScreen = applyToScreen;
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
            BuildRadarFields();
            QnhRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QNH, p => p.PressureReference = PressureReference.QNH);
            QfeRadio.Checked += (s, e) => SetProfileValue(p => p.PressureReference == PressureReference.QFE, p => p.PressureReference = PressureReference.QFE);
            HpaRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.HectoPascal, p => p.PressureUnit = PressureUnit.HectoPascal);
            InHgRadio.Checked += (s, e) => SetProfileValue(p => p.PressureUnit == PressureUnit.InchesOfMercury, p => p.PressureUnit = PressureUnit.InchesOfMercury);
            DaDhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.DaDh, p => p.MinimaLabel = MinimaLabel.DaDh);
            OcaOchRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.OcaOch, p => p.MinimaLabel = MinimaLabel.OcaOch);
            MdaMdhRadio.Checked += (s, e) => SetProfileValue(p => p.MinimaLabel == MinimaLabel.MdaMdh, p => p.MinimaLabel = MinimaLabel.MdaMdh);
            AltitudeScaleCheck.Checked += (s, e) => SetProfileValue(p => p.ShowAltitudeScale, p => p.ShowAltitudeScale = true);
            AltitudeScaleCheck.Unchecked += (s, e) => SetProfileValue(p => !p.ShowAltitudeScale, p => p.ShowAltitudeScale = false);
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
                ScaleFeetRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Feet;
                ScaleMetresRadio.IsChecked = Active.AltitudeScaleUnit == LengthUnit.Metres;
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

        private void BuildRadarFields()
        {
            AddHeader("Approach limits (track green inside, red outside)");
            AddNumberField("Above the glide path", p => p.ApproachAbove, (p, v) => p.ApproachAbove = v, 0.1, 5);
            AddNumberField("Below the glide path", p => p.ApproachBelow, (p, v) => p.ApproachBelow = v, 0.1, 5);
            AddNumberField("Left of the centreline", p => p.ApproachLeft, (p, v) => p.ApproachLeft = v, 0.1, 10);
            AddNumberField("Right of the centreline", p => p.ApproachRight, (p, v) => p.ApproachRight = v, 0.1, 10);
            AddHeader("Scan limits from the antenna (neutral position)");
            AddNumberField("Up", p => p.ScanUp, (p, v) => p.ScanUp = v, 1, 30, (p, v) => v > p.ScanDown);
            AddNumberField("Down (negative = below the horizon)", p => p.ScanDown, (p, v) => p.ScanDown = v, -10, 10, (p, v) => v < p.ScanUp);
            AddNumberField("Left", p => p.ScanLeft, (p, v) => p.ScanLeft = v, 1, 45);
            AddNumberField("Right", p => p.ScanRight, (p, v) => p.ScanRight = v, 1, 45);
            AddHeader("Antenna tilt");
            AddNumberField("Step", p => p.TiltStep, (p, v) => p.TiltStep = v, 0.5, 10);
            AddNumberField("Maximum", p => p.TiltMax, (p, v) => p.TiltMax = v, 0, 45);
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
                                    Func<Profile, double, bool>? isValid = null)
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
                Text = "°",
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
