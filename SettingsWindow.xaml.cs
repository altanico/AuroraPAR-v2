using System.Windows;
using System.Windows.Controls;
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
