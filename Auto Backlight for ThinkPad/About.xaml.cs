/*
 * Copyright 2019 Parth Patel
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Window
    {
        // PHASE30B2_UPDATE_UI: no MSI download or installation in this phase.
        private bool _phase30b2Closed;

        private async Task Phase30B2CheckForUpdatesAsync()
        {
            if (_phase30b2Closed) return;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking the latest stable GitHub release...";
            try
            {
                OnlineUpdater.UpdateCheckResult result;
                using (var updater = new OnlineUpdater())
                    result = await updater.CheckForUpdatesAsync();

                if (_phase30b2Closed) return;
                switch (result.Status)
                {
                    case OnlineUpdater.CheckStatus.UpdateAvailable:
                        UpdateStatusText.Text = "New version v" + result.LatestVersion.ToString(3) +
                            " available. In-app MSI installation is not enabled yet.";
                        if (MessageBox.Show(this,
                            "Version v" + result.LatestVersion.ToString(3) + " is available.\n\n" +
                            "MSI installation from the app is not implemented yet.\n" +
                            "Open the project's GitHub Releases page?",
                            "Update available", MessageBoxButton.YesNo,
                            MessageBoxImage.Information) == MessageBoxResult.Yes)
                        {
                            OpenProjectUrl("https://github.com/prat89/auto-backlight-for-thinkpad/releases");
                        }
                        break;
                    case OnlineUpdater.CheckStatus.UpToDate:
                        UpdateStatusText.Text = "You're up to date (" +
                            Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + ").";
                        break;
                    case OnlineUpdater.CheckStatus.NoInstallableMsi:
                        UpdateStatusText.Text = "A newer release exists, but its MSI could not be uniquely verified.";
                        break;
                    default:
                        UpdateStatusText.Text = "Update check unavailable: " + result.Message;
                        break;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B2: manual update check failed", ex);
                if (!_phase30b2Closed)
                    UpdateStatusText.Text = "Update check failed. See application.log for details.";
            }
            finally
            {
                if (!_phase30b2Closed) CheckUpdatesButton.IsEnabled = true;
            }
        }

        // PHASE30B2A_ABOUT_CAPTION_THEME: use Windows APP theme for the native caption and icon.
        // This intentionally does not modify About body styling or updater behavior.
        private const string Phase30B2AThemePath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const int Phase30B2ADwmDarkTitle = 20;
        private const int Phase30B2ADwmCaptionColor = 35;
        private const int Phase30B2ADwmTextColor = 36;
        private bool? _phase30b2aLastDark;
        private bool _phase30b2aSubscribed;
        private DispatcherTimer _phase30b2aThemeRetry;
        private ImageSource _phase30b2aLightCaptionIcon;
        private ImageSource _phase30b2aDarkCaptionIcon;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
            ref int value, int size);

        private bool Phase30B2AReadDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Phase30B2AThemePath, false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme", null);
                    return value is int && (int)value == 0;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: Windows preference unavailable; " + ex.GetType().Name);
                return false;
            }
        }

        private void Phase30B2ARefreshIcon(bool dark)
        {
            try
            {
                if (dark)
                {
                    if (_phase30b2aDarkCaptionIcon == null)
                        _phase30b2aDarkCaptionIcon = BitmapFrame.Create(
                            new Uri("pack://application:,,,/Resources/TrayIcons/TrayIcon.PngConcept.ForDarkTaskbar.ico", UriKind.Absolute));
                    Icon = _phase30b2aDarkCaptionIcon;
                }
                else
                {
                    if (_phase30b2aLightCaptionIcon == null)
                        _phase30b2aLightCaptionIcon = BitmapFrame.Create(
                            new Uri("pack://application:,,,/Resources/TrayIcons/TrayIcon.PngConcept.ForLightTaskbar.ico", UriKind.Absolute));
                    Icon = _phase30b2aLightCaptionIcon;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: window icon unavailable; " + ex.GetType().Name);
            }
        }

        private void Phase30B2AApplyTitleBar(bool dark)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int enabled = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, Phase30B2ADwmDarkTitle, ref enabled, sizeof(int));
                // DWM caption and text color values use COLORREF (0x00BBGGRR).
                int caption = dark ? 0x002B2522 : 0x00FDFBFA;
                int text = dark ? 0x00F6F4F3 : 0x00322620;
                DwmSetWindowAttribute(hwnd, Phase30B2ADwmCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, Phase30B2ADwmTextColor, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: title bar unavailable; " + ex.GetType().Name);
            }
        }

        private void Phase30B2ARefreshTheme(bool forceTitleBar = false)
        {
            bool dark = Phase30B2AReadDarkMode();
            bool changed = !_phase30b2aLastDark.HasValue || _phase30b2aLastDark.Value != dark;
            if (changed)
            {
                Phase30B2ARefreshIcon(dark);
                _phase30b2aLastDark = dark;
            }
            if (changed || forceTitleBar) Phase30B2AApplyTitleBar(dark);
        }

        private void Phase30B2AThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Phase30B2ARefreshTheme();
                    // The registry preference can lag behind this Windows event.
                    if (_phase30b2aThemeRetry == null)
                    {
                        _phase30b2aThemeRetry = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(250)
                        };
                        _phase30b2aThemeRetry.Tick += (o, args) =>
                        {
                            _phase30b2aThemeRetry.Stop();
                            Phase30B2ARefreshTheme();
                        };
                    }
                    _phase30b2aThemeRetry.Stop();
                    _phase30b2aThemeRetry.Start();
                }));
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: event dispatch ignored; " + ex.GetType().Name);
            }
        }

        private void Phase30B2AInitializeTheme()
        {
            SourceInitialized += (sender, e) => Phase30B2ARefreshTheme(true);
            Activated += (sender, e) => Phase30B2ARefreshTheme();
            Closed += (sender, e) =>
            {
                if (_phase30b2aThemeRetry != null) _phase30b2aThemeRetry.Stop();
                if (_phase30b2aSubscribed)
                {
                    SystemEvents.UserPreferenceChanged -= Phase30B2AThemeChanged;
                    _phase30b2aSubscribed = false;
                }
            };
            try
            {
                SystemEvents.UserPreferenceChanged += Phase30B2AThemeChanged;
                _phase30b2aSubscribed = true;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: preference events unavailable; " + ex.GetType().Name);
            }
            Phase30B2ARefreshTheme();
        }

        /// <summary>
        /// Load informational strings and connect the buttons
        /// </summary>
        public About()
        {
            InitializeComponent();
            Phase30B2AInitializeTheme();

            // Load strings
            Header.Content = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product;
            Subheader.Content = "Version: " + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion + " (" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>().Configuration + ")";
            Body.Text = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>().Description;
            Footer.Content = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>().Copyright;

            // Load License.rtf document
            LicenseHeader.Content = "This software is licensed under the Apache License Version 2.0:";
            TextRange content = new TextRange(License.ContentStart, License.ContentEnd);
            string file = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "License.rtf");
            if (content.CanLoad(DataFormats.Rtf))
                content.Load(File.OpenRead(file), DataFormats.Rtf);
            StartAutoBox.IsChecked = 0 == Installer.DoCmd(Environment.GetEnvironmentVariable("ComSpec"), string.Format("/c schtasks /Query /TN {0} | findstr /C:Running /C:Ready", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""));

            // Connect actions to buttons
            VisitButton.Click += (sender, e) =>
            {
                // Open url in web browser
                var uri = new Uri(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().First((i) => i.Key == "WebUrl").Value); ;
                if (uri.AbsoluteUri != "")
                {
                    var proc = new Process();
                    proc.StartInfo.FileName = uri.AbsoluteUri;
                    proc.Start();
                }
            };
            StartAutoBox.Click += (sender, e) =>
            {
                try
                {
                    if (StartAutoBox.IsChecked == true) Installer.DoCmd("schtasks", string.Format("/Change /TN {0} /ENABLE", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""), true);
                    else Installer.DoCmd("schtasks", string.Format("/Change /TN {0} /Disable", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""), true);
                }
                catch (Exception)
                {
                }
                StartAutoBox.IsChecked = 0 == Installer.DoCmd(Environment.GetEnvironmentVariable("ComSpec"), string.Format("/c schtasks /Query /TN {0} | findstr /C:Running /C:Ready", "\"" + "\\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product + "\""));
            };
            // Stored separately from legacy BinaryFormatter settings; OFF for new users.
            AutoInstallUpdatesBox.IsChecked = Phase30B2UpdatePreferences.ReadAutoInstallUpdates();
            AutoInstallUpdatesBox.Click += (sender, e) =>
            {
                bool requested = AutoInstallUpdatesBox.IsChecked == true;
                try
                {
                    Phase30B2UpdatePreferences.SaveAutoInstallUpdates(requested);
                    UpdateStatusText.Text = requested
                        ? "Preference saved. Automatic MSI installation is NOT active in this version."
                        : "Notify-only preference saved. Automatic MSI installation is not active yet.";
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Error("Phase30B2: could not save update preference", ex);
                    AutoInstallUpdatesBox.IsChecked = Phase30B2UpdatePreferences.ReadAutoInstallUpdates();
                    UpdateStatusText.Text = "Could not save update preference; check application.log.";
                }
            };
            CheckUpdatesButton.Click += async (sender, e) => await Phase30B2CheckForUpdatesAsync();
            Closed += (sender, e) => _phase30b2Closed = true;
            CloseButton.Click += (sender, e) => Close();
            UpdateButton.Click += (sender, e) => OpenProjectUrl("https://github.com/prat89/auto-backlight-for-thinkpad/releases");
        }
        // Open only a trusted project page. Never launch an installer.
        private static void OpenProjectUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the web browser: " + ex.Message,
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
