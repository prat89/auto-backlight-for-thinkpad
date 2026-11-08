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
        private bool _aboutClosed;
        // PHASE30B7D6M3A_UI_STATE_V1
        private OnlineUpdater.Downloadable _updateCandidate;
        private bool _updateOperationBusy;
        private System.Threading.CancellationTokenSource _updateCancellation;
        // PHASE30B7D6M3B_LOCAL_REVIEW_STATE_V1
        private string _stagedMsi;
        // PHASE30B7D6M3C_EXPLICIT_PROTECTED_PREPARE_STATE_V1
        private string _reviewedMsi;
        // PHASE30B7D6M3D_PREVIEW_STATE_V1
        private string _protectedMsi;
        // PHASE30B7D6M3E_CONSENT_CANDIDATE_V1
        private string _approvedMsiCandidate;

        private async Task CheckForUpdatesInAboutAsync()
        {
            if (_aboutClosed || _updateOperationBusy) return;
            _updateCandidate = null;
            _stagedMsi = null;
            _reviewedMsi = null;
            _protectedMsi = null;
            _approvedMsiCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
            ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
            PrepareSignedMsiButton.Visibility = Visibility.Collapsed;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking the latest stable GitHub release...";
            try
            {
                OnlineUpdater.UpdateCheckResult result;
                using (var updater = new OnlineUpdater())
                    result = await updater.CheckForUpdatesAsync();

                if (_aboutClosed) return;
                switch (result.Status)
                {
                    case OnlineUpdater.CheckStatus.UpdateAvailable:
                        if (SignedDownloadUiPolicy.CanOfferSignedDownload(result))
                        {
                            _updateCandidate = result.Update;
                            PrepareSignedMsiButton.Visibility = Visibility.Visible;
                            UpdateStatusText.Text = "Version v" + result.LatestVersion.ToString(3) +
                                " available. You may download and verify its signed MSI. " +
                                "Installation requires a separate explicit confirmation.";
                        }
                        else
                            UpdateStatusText.Text = "New release metadata is insufficient for a signed download.";
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
                if (!_aboutClosed)
                    UpdateStatusText.Text = "Update check failed. See application.log for details.";
            }
            finally
            {
                if (!_aboutClosed) CheckUpdatesButton.IsEnabled = true;
            }
        }

        // PHASE30B7D6M3A_EXPLICIT_SIGNED_STAGE_UI_V1
        private async Task PrepareSignedMsiAsync()
        {
            if (_aboutClosed || _updateOperationBusy ||
                _updateCandidate == null) return;
            OnlineUpdater.Downloadable candidate = _updateCandidate;
            _stagedMsi = null;
            _reviewedMsi = null;
            _protectedMsi = null;
            _approvedMsiCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
            ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
            _updateOperationBusy = true;
            PrepareSignedMsiButton.IsEnabled = false;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Downloading and checking the publisher-signed MSI; no installation...";
            var cancel = new System.Threading.CancellationTokenSource();
            _updateCancellation = cancel;
            try
            {
                OnlineUpdater.StageResult staged;
                using (var updater = new OnlineUpdater())
                    staged = await updater.StageSignedMsiAsync(candidate, cancel.Token);
                if (_aboutClosed) return;
                if (LocalReview.CanOfferReview(staged))
                {
                    _updateCandidate = null; // one-shot explicit user action
                    _stagedMsi = staged.Path;
                    PrepareSignedMsiButton.Visibility = Visibility.Collapsed;
                    ReviewSignedMsiButton.Visibility = Visibility.Visible;
                    UpdateStatusText.Text = "Signed MSI downloaded to user staging. " +
                        "You may verify it again locally. Not installed.";
                }
                else if (staged.Status == OnlineUpdater.StageStatus.Staged)
                    UpdateStatusText.Text = "Downloaded MSI cannot be reviewed safely. Installation disabled.";
                else
                    UpdateStatusText.Text = "Signed MSI preparation failed: " + staged.Message;
            }
            catch (OperationCanceledException)
            {
                if (!_aboutClosed) UpdateStatusText.Text = "Download cancelled; no installation.";
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3A: signed download failed", ex);
                if (!_aboutClosed)
                    UpdateStatusText.Text = "Signed MSI preparation failed. See application.log.";
            }
            finally
            {
                if (Object.ReferenceEquals(_updateCancellation, cancel))
                    _updateCancellation = null;
                cancel.Dispose();
                _updateOperationBusy = false;
                if (!_aboutClosed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    PrepareSignedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3B_LOCAL_READ_ONLY_REVIEW_UI_V1
        private async Task ReviewSignedMsiAsync()
        {
            if (_aboutClosed || _updateOperationBusy ||
                String.IsNullOrWhiteSpace(_stagedMsi)) return;
            string path = _stagedMsi;
            _protectedMsi = null;
            _approvedMsiCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            _updateOperationBusy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewSignedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Rechecking local RSA signature, MSI hash and identity (read-only)...";
            try
            {
                bool valid = await Task.Run(() => LocalReview.ReviewReadOnly(path));
                if (_aboutClosed) return;
                if (!String.Equals(_stagedMsi, path, StringComparison.Ordinal))
                    return;
                if (valid)
                {
                    // Never offer privileged preparation from the developer EXE.
                    if (ProtectedHandoff.CanOfferFromUi(path))
                    {
                        _reviewedMsi = path;
                        PrepareProtectedMsiButton.Visibility = Visibility.Visible;
                        UpdateStatusText.Text = "Publisher signature and MSI verified. " +
                            "Protected copy available by explicit click; MSI installation disabled.";
                    }
                    else
                    {
                        _reviewedMsi = null;
                        PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                        UpdateStatusText.Text = "Local signed MSI verification passed. " +
                            "Protected copy requires a trusted installed application build.";
                    }
                }
                else
                {
                    _reviewedMsi = null;
                    PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                    _stagedMsi = null;
                    ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Local signed MSI review failed; bundle invalid or changed. " +
                        "Installation remains disabled.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3B: read-only local review failed", ex);
                if (!_aboutClosed)
                {
                    _stagedMsi = null;
                    _reviewedMsi = null;
                    PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                    ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Local bundle review unavailable. Installation disabled.";
                }
            }
            finally
            {
                _updateOperationBusy = false;
                if (!_aboutClosed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewSignedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3C_EXPLICIT_PROTECTED_STAGE_UI_V1
        private async Task PrepareProtectedMsiAsync()
        {
            if (_aboutClosed || _updateOperationBusy ||
                String.IsNullOrWhiteSpace(_reviewedMsi)) return;
            string path = _reviewedMsi;
            _protectedMsi = null;
            _approvedMsiCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            if (!String.Equals(_stagedMsi, path, StringComparison.Ordinal) ||
                !ProtectedHandoff.CanOfferFromUi(path))
            {
                _reviewedMsi = null;
                PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = "Protected copy refused: file or trust state changed.";
                return;
            }
            if (MessageBox.Show(this,
                "Copy the verified MSI to administrator-protected storage? " +
                "Windows may ask for administrator consent. This will NOT install or run the MSI.",
                "Prepare verified update (no installation)", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _updateOperationBusy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewSignedMsiButton.IsEnabled = false;
            PrepareProtectedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Rechecking signed bundle and preparing a protected copy...";
            try
            {
                int code = await Task.Run(() => ProtectedHandoff.RunFromUi(path));
                if (_aboutClosed) return;
                _reviewedMsi = null;
                PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                if (code == 0 && ProtectedPreflightUi.CanOfferFromUi(path))
                {
                    _protectedMsi = path;
                    ReviewProtectedMsiButton.Visibility = Visibility.Visible;
                    UpdateStatusText.Text = "Signed MSI copied into protected storage. " +
                        "You may review the installation plan (read-only); installation disabled.";
                }
                else if (code == 0)
                    UpdateStatusText.Text = "Protected copy completed, but trusted preflight UI unavailable.";
                else
                    UpdateStatusText.Text = "Protected preparation not completed (code " + code +
                        "). No MSI installation was attempted.";
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3C: protected copy failed", ex);
                if (!_aboutClosed)
                    UpdateStatusText.Text = "Protected preparation failed. No MSI installation attempted.";
            }
            finally
            {
                _updateOperationBusy = false;
                if (!_aboutClosed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewSignedMsiButton.IsEnabled = true;
                    PrepareProtectedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3D_EXPLICIT_READONLY_INSTALL_PREFLIGHT_UI_V1
        private async Task ReviewProtectedMsiAsync()
        {
            if (_aboutClosed || _updateOperationBusy ||
                String.IsNullOrWhiteSpace(_protectedMsi)) return;
            string path = _protectedMsi;
            if (!ProtectedPreflightUi.CanOfferFromUi(path))
            {
                _protectedMsi = null;
                _approvedMsiCandidate = null;
                InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = "Read-only preflight unavailable: trusted installation required.";
                return;
            }
            if (MessageBox.Show(this,
                "Verify the protected MSI and check installation readiness? " +
                "Windows may ask for administrator consent. This does NOT run or install MSI.",
                "Review installation readiness (no installation)", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _updateOperationBusy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewProtectedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking protected MSI and installation plan (read-only)...";
            try
            {
                int code = await Task.Run(() => ProtectedPreflightUi.RunFromUi(path));
                if (_aboutClosed) return;
                _protectedMsi = null; // one-shot; repeat requires fresh review/copy
                _approvedMsiCandidate = null;
                InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
                if (code == 0)
                {
                    // PHASE30B7D6M3E_NO_PRODUCTION_INSTALL_YET_V1
                    if (InstallConsentGate.CanOfferFromUi(path))
                    {
                        _approvedMsiCandidate = path;
                        InstallVerifiedUpdateButton.IsEnabled = true;
                        InstallVerifiedUpdateButton.Visibility = Visibility.Visible;
                    }
                    UpdateStatusText.Text = "Protected installation preflight passed. " +
                        "Choose Install verified update to authorize the signed MSI.";
                }
                else
                    UpdateStatusText.Text = "Protected preflight not completed (code " + code +
                        "). No MSI executed.";
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3D: protected preflight failed", ex);
                if (!_aboutClosed)
                {
                    _protectedMsi = null;
                    _approvedMsiCandidate = null;
                    InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                    ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Protected preflight failed. No MSI executed.";
                }
            }
            finally
            {
                _updateOperationBusy = false;
                if (!_aboutClosed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewProtectedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3E_USER_INSTALL_CONSENT_UI_DISABLED_V1
        private async Task InstallVerifiedUpdateAsync()
        {
            if (_aboutClosed || _updateOperationBusy ||
                String.IsNullOrWhiteSpace(_approvedMsiCandidate)) return;
            string path = _approvedMsiCandidate;
            if (!InstallConsentGate.CanOfferFromUi(path))
            {
                _approvedMsiCandidate = null;
                InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = "Production installation is not enabled in this build.";
                return;
            }
            if (MessageBox.Show(this,
                "Install the verified update now? The running application will be closed. " +
                "Save your work first. This action requires administrator approval.",
                "Confirm MSI installation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _updateOperationBusy = true;
            CheckUpdatesButton.IsEnabled = false;
            InstallVerifiedUpdateButton.IsEnabled = false;
            try
            {
                int status = await Task.Run(() => InstallConsentGate.RunFromUi(path));
                if (!_aboutClosed)
                {
                    if (status == 0)
                    {
                        UpdateStatusText.Text = "Protected update worker armed. Closing the app for MSI upgrade...";
                        // The detached worker waits for this GUI process to exit.
                        Application.Current.Shutdown();
                        return;
                    }
                    UpdateStatusText.Text = "Installation was not authorized (code " + status +
                        "). Review the package and try again.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3E: install authorization refused", ex);
                if (!_aboutClosed) UpdateStatusText.Text = "Installation blocked; no MSI started.";
            }
            finally
            {
                _approvedMsiCandidate = null;
                _updateOperationBusy = false;
                if (!_aboutClosed)
                {
                    InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                    CheckUpdatesButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B2A_ABOUT_CAPTION_THEME: use Windows APP theme for the native caption and icon.
        // This intentionally does not modify About body styling or updater behavior.
        private const string ThemePath =
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const int DwmDarkTitle = 20;
        private const int DwmCaptionColor = 35;
        private const int DwmTextColor = 36;
        private bool? _lastDark;
        private bool _subscribed;
        private DispatcherTimer _themeRetry;
        private ImageSource _lightCaptionIcon;
        private ImageSource _darkCaptionIcon;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
            ref int value, int size);

        private bool ReadDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemePath, false))
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

        private void RefreshIcon(bool dark)
        {
            try
            {
                if (dark)
                {
                    if (_darkCaptionIcon == null)
                        _darkCaptionIcon = BitmapFrame.Create(
                            new Uri("pack://application:,,,/Resources/TrayIcons/TrayIcon.PngConcept.ForDarkTaskbar.ico", UriKind.Absolute));
                    Icon = _darkCaptionIcon;
                }
                else
                {
                    if (_lightCaptionIcon == null)
                        _lightCaptionIcon = BitmapFrame.Create(
                            new Uri("pack://application:,,,/Resources/TrayIcons/TrayIcon.PngConcept.ForLightTaskbar.ico", UriKind.Absolute));
                    Icon = _lightCaptionIcon;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: window icon unavailable; " + ex.GetType().Name);
            }
        }

        private void ApplyTitleBar(bool dark)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int enabled = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmDarkTitle, ref enabled, sizeof(int));
                // DWM caption and text color values use COLORREF (0x00BBGGRR).
                int caption = dark ? 0x002B2522 : 0x00FDFBFA;
                int text = dark ? 0x00F6F4F3 : 0x00322620;
                DwmSetWindowAttribute(hwnd, DwmCaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, DwmTextColor, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: title bar unavailable; " + ex.GetType().Name);
            }
        }

        private void RefreshTheme(bool forceTitleBar = false)
        {
            bool dark = ReadDarkMode();
            bool changed = !_lastDark.HasValue || _lastDark.Value != dark;
            if (changed)
            {
                RefreshIcon(dark);
                _lastDark = dark;
            }
            if (changed || forceTitleBar) ApplyTitleBar(dark);
        }

        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshTheme();
                    // The registry preference can lag behind this Windows event.
                    if (_themeRetry == null)
                    {
                        _themeRetry = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(250)
                        };
                        _themeRetry.Tick += (o, args) =>
                        {
                            _themeRetry.Stop();
                            RefreshTheme();
                        };
                    }
                    _themeRetry.Stop();
                    _themeRetry.Start();
                }));
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: event dispatch ignored; " + ex.GetType().Name);
            }
        }

        private void InitializeTheme()
        {
            SourceInitialized += (sender, e) => RefreshTheme(true);
            Activated += (sender, e) => RefreshTheme();
            Closed += (sender, e) =>
            {
                if (_themeRetry != null) _themeRetry.Stop();
                if (_subscribed)
                {
                    SystemEvents.UserPreferenceChanged -= ThemeChanged;
                    _subscribed = false;
                }
            };
            try
            {
                SystemEvents.UserPreferenceChanged += ThemeChanged;
                _subscribed = true;
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Info("About theme: preference events unavailable; " + ex.GetType().Name);
            }
            RefreshTheme();
        }

        /// <summary>
        /// Load informational strings and connect the buttons
        /// </summary>
        public About()
        {
            InitializeComponent();
            InitializeTheme();
            // Read-only status from the latest protected detached MSI transaction.
            string latestInstall = ProductionUpdateInstaller.LastResultForAbout();
            if (!String.IsNullOrWhiteSpace(latestInstall))
                UpdateStatusText.Text = latestInstall;

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
            AutoInstallUpdatesBox.IsChecked = UpdaterPreferences.ReadAutoInstallUpdates();
            AutoInstallUpdatesBox.Click += (sender, e) =>
            {
                bool requested = AutoInstallUpdatesBox.IsChecked == true;
                try
                {
                    UpdaterPreferences.SaveAutoInstallUpdates(requested);
                    UpdateStatusText.Text = requested
                        ? "Preference saved. Automatic MSI installation is NOT active in this version."
                        : "Notify-only preference saved. Automatic MSI installation is not active yet.";
                }
                catch (Exception ex)
                {
                    DiagnosticsLog.Error("Phase30B2: could not save update preference", ex);
                    AutoInstallUpdatesBox.IsChecked = UpdaterPreferences.ReadAutoInstallUpdates();
                    UpdateStatusText.Text = "Could not save update preference; check application.log.";
                }
            };
            CheckUpdatesButton.Click += async (sender, e) => await CheckForUpdatesInAboutAsync();
            PrepareSignedMsiButton.Click += async (sender, e) => await PrepareSignedMsiAsync();
            ReviewSignedMsiButton.Click += async (sender, e) => await ReviewSignedMsiAsync();
            PrepareProtectedMsiButton.Click += async (sender, e) => await PrepareProtectedMsiAsync();
            ReviewProtectedMsiButton.Click += async (sender, e) => await ReviewProtectedMsiAsync();
            InstallVerifiedUpdateButton.Click += async (sender, e) => await InstallVerifiedUpdateAsync();
            Closed += (sender, e) =>
            {
                _aboutClosed = true;
                if (_updateCancellation != null)
                    _updateCancellation.Cancel();
            };
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
