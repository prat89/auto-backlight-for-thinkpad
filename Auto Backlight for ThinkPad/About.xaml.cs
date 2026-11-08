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
        // PHASE30B7D6M3A_UI_STATE_V1
        private OnlineUpdater.Downloadable _phase30b7d6m3Candidate;
        private bool _phase30b7d6m3Busy;
        private System.Threading.CancellationTokenSource _phase30b7d6m3Cancellation;
        // PHASE30B7D6M3B_LOCAL_REVIEW_STATE_V1
        private string _phase30b7d6m3bStagedMsi;
        // PHASE30B7D6M3C_EXPLICIT_PROTECTED_PREPARE_STATE_V1
        private string _phase30b7d6m3cReviewedMsi;
        // PHASE30B7D6M3D_PREVIEW_STATE_V1
        private string _phase30b7d6m3dProtectedMsi;
        // PHASE30B7D6M3E_CONSENT_CANDIDATE_V1
        private string _phase30b7d6m3eApprovedCandidate;

        private async Task Phase30B2CheckForUpdatesAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy) return;
            _phase30b7d6m3Candidate = null;
            _phase30b7d6m3bStagedMsi = null;
            _phase30b7d6m3cReviewedMsi = null;
            _phase30b7d6m3dProtectedMsi = null;
            _phase30b7d6m3eApprovedCandidate = null;
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

                if (_phase30b2Closed) return;
                switch (result.Status)
                {
                    case OnlineUpdater.CheckStatus.UpdateAvailable:
                        if (Phase30B7D6M3SignedDownloadUiPolicy.CanOfferSignedDownload(result))
                        {
                            _phase30b7d6m3Candidate = result.Update;
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
                if (!_phase30b2Closed)
                    UpdateStatusText.Text = "Update check failed. See application.log for details.";
            }
            finally
            {
                if (!_phase30b2Closed) CheckUpdatesButton.IsEnabled = true;
            }
        }

        // PHASE30B7D6M3A_EXPLICIT_SIGNED_STAGE_UI_V1
        private async Task Phase30B7D6M3PrepareSignedMsiAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy ||
                _phase30b7d6m3Candidate == null) return;
            OnlineUpdater.Downloadable candidate = _phase30b7d6m3Candidate;
            _phase30b7d6m3bStagedMsi = null;
            _phase30b7d6m3cReviewedMsi = null;
            _phase30b7d6m3dProtectedMsi = null;
            _phase30b7d6m3eApprovedCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
            ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
            _phase30b7d6m3Busy = true;
            PrepareSignedMsiButton.IsEnabled = false;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Downloading and checking the publisher-signed MSI; no installation...";
            var cancel = new System.Threading.CancellationTokenSource();
            _phase30b7d6m3Cancellation = cancel;
            try
            {
                OnlineUpdater.StageResult staged;
                using (var updater = new OnlineUpdater())
                    staged = await updater.StageSignedMsiAsync(candidate, cancel.Token);
                if (_phase30b2Closed) return;
                if (Phase30B7D6M3BLocalReview.CanOfferReview(staged))
                {
                    _phase30b7d6m3Candidate = null; // one-shot explicit user action
                    _phase30b7d6m3bStagedMsi = staged.Path;
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
                if (!_phase30b2Closed) UpdateStatusText.Text = "Download cancelled; no installation.";
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3A: signed download failed", ex);
                if (!_phase30b2Closed)
                    UpdateStatusText.Text = "Signed MSI preparation failed. See application.log.";
            }
            finally
            {
                if (Object.ReferenceEquals(_phase30b7d6m3Cancellation, cancel))
                    _phase30b7d6m3Cancellation = null;
                cancel.Dispose();
                _phase30b7d6m3Busy = false;
                if (!_phase30b2Closed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    PrepareSignedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3B_LOCAL_READ_ONLY_REVIEW_UI_V1
        private async Task Phase30B7D6M3BReviewSignedMsiAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy ||
                String.IsNullOrWhiteSpace(_phase30b7d6m3bStagedMsi)) return;
            string path = _phase30b7d6m3bStagedMsi;
            _phase30b7d6m3dProtectedMsi = null;
            _phase30b7d6m3eApprovedCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            _phase30b7d6m3Busy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewSignedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Rechecking local RSA signature, MSI hash and identity (read-only)...";
            try
            {
                bool valid = await Task.Run(() => Phase30B7D6M3BLocalReview.ReviewReadOnly(path));
                if (_phase30b2Closed) return;
                if (!String.Equals(_phase30b7d6m3bStagedMsi, path, StringComparison.Ordinal))
                    return;
                if (valid)
                {
                    // Never offer privileged preparation from the developer EXE.
                    if (Phase30B7D6M3CProtectedHandoff.CanOfferFromUi(path))
                    {
                        _phase30b7d6m3cReviewedMsi = path;
                        PrepareProtectedMsiButton.Visibility = Visibility.Visible;
                        UpdateStatusText.Text = "Publisher signature and MSI verified. " +
                            "Protected copy available by explicit click; MSI installation disabled.";
                    }
                    else
                    {
                        _phase30b7d6m3cReviewedMsi = null;
                        PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                        UpdateStatusText.Text = "Local signed MSI verification passed. " +
                            "Protected copy requires a trusted installed application build.";
                    }
                }
                else
                {
                    _phase30b7d6m3cReviewedMsi = null;
                    PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                    _phase30b7d6m3bStagedMsi = null;
                    ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Local signed MSI review failed; bundle invalid or changed. " +
                        "Installation remains disabled.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase30B7D6M3B: read-only local review failed", ex);
                if (!_phase30b2Closed)
                {
                    _phase30b7d6m3bStagedMsi = null;
                    _phase30b7d6m3cReviewedMsi = null;
                    PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                    ReviewSignedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Local bundle review unavailable. Installation disabled.";
                }
            }
            finally
            {
                _phase30b7d6m3Busy = false;
                if (!_phase30b2Closed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewSignedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3C_EXPLICIT_PROTECTED_STAGE_UI_V1
        private async Task Phase30B7D6M3CPrepareProtectedMsiAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy ||
                String.IsNullOrWhiteSpace(_phase30b7d6m3cReviewedMsi)) return;
            string path = _phase30b7d6m3cReviewedMsi;
            _phase30b7d6m3dProtectedMsi = null;
            _phase30b7d6m3eApprovedCandidate = null;
            InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
            ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
            if (!String.Equals(_phase30b7d6m3bStagedMsi, path, StringComparison.Ordinal) ||
                !Phase30B7D6M3CProtectedHandoff.CanOfferFromUi(path))
            {
                _phase30b7d6m3cReviewedMsi = null;
                PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = "Protected copy refused: file or trust state changed.";
                return;
            }
            if (MessageBox.Show(this,
                "Copy the verified MSI to administrator-protected storage? " +
                "Windows may ask for administrator consent. This will NOT install or run the MSI.",
                "Prepare verified update (no installation)", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _phase30b7d6m3Busy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewSignedMsiButton.IsEnabled = false;
            PrepareProtectedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Rechecking signed bundle and preparing a protected copy...";
            try
            {
                int code = await Task.Run(() => Phase30B7D6M3CProtectedHandoff.RunFromUi(path));
                if (_phase30b2Closed) return;
                _phase30b7d6m3cReviewedMsi = null;
                PrepareProtectedMsiButton.Visibility = Visibility.Collapsed;
                if (code == 0 && Phase30B7D6M3DProtectedPreflightUi.CanOfferFromUi(path))
                {
                    _phase30b7d6m3dProtectedMsi = path;
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
                if (!_phase30b2Closed)
                    UpdateStatusText.Text = "Protected preparation failed. No MSI installation attempted.";
            }
            finally
            {
                _phase30b7d6m3Busy = false;
                if (!_phase30b2Closed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewSignedMsiButton.IsEnabled = true;
                    PrepareProtectedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3D_EXPLICIT_READONLY_INSTALL_PREFLIGHT_UI_V1
        private async Task Phase30B7D6M3DReviewProtectedMsiAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy ||
                String.IsNullOrWhiteSpace(_phase30b7d6m3dProtectedMsi)) return;
            string path = _phase30b7d6m3dProtectedMsi;
            if (!Phase30B7D6M3DProtectedPreflightUi.CanOfferFromUi(path))
            {
                _phase30b7d6m3dProtectedMsi = null;
                _phase30b7d6m3eApprovedCandidate = null;
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
            _phase30b7d6m3Busy = true;
            CheckUpdatesButton.IsEnabled = false;
            ReviewProtectedMsiButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking protected MSI and installation plan (read-only)...";
            try
            {
                int code = await Task.Run(() => Phase30B7D6M3DProtectedPreflightUi.RunFromUi(path));
                if (_phase30b2Closed) return;
                _phase30b7d6m3dProtectedMsi = null; // one-shot; repeat requires fresh review/copy
                _phase30b7d6m3eApprovedCandidate = null;
                InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
                if (code == 0)
                {
                    // PHASE30B7D6M3E_NO_PRODUCTION_INSTALL_YET_V1
                    if (Phase30B7D6M3EInstallConsentGate.CanOfferFromUi(path))
                    {
                        _phase30b7d6m3eApprovedCandidate = path;
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
                if (!_phase30b2Closed)
                {
                    _phase30b7d6m3dProtectedMsi = null;
                    _phase30b7d6m3eApprovedCandidate = null;
                    InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                    ReviewProtectedMsiButton.Visibility = Visibility.Collapsed;
                    UpdateStatusText.Text = "Protected preflight failed. No MSI executed.";
                }
            }
            finally
            {
                _phase30b7d6m3Busy = false;
                if (!_phase30b2Closed)
                {
                    CheckUpdatesButton.IsEnabled = true;
                    ReviewProtectedMsiButton.IsEnabled = true;
                }
            }
        }

        // PHASE30B7D6M3E_USER_INSTALL_CONSENT_UI_DISABLED_V1
        private async Task Phase30B7D6M3EInstallVerifiedUpdateAsync()
        {
            if (_phase30b2Closed || _phase30b7d6m3Busy ||
                String.IsNullOrWhiteSpace(_phase30b7d6m3eApprovedCandidate)) return;
            string path = _phase30b7d6m3eApprovedCandidate;
            if (!Phase30B7D6M3EInstallConsentGate.CanOfferFromUi(path))
            {
                _phase30b7d6m3eApprovedCandidate = null;
                InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = "Production installation is not enabled in this build.";
                return;
            }
            if (MessageBox.Show(this,
                "Install the verified update now? The running application will be closed. " +
                "Save your work first. This action requires administrator approval.",
                "Confirm MSI installation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _phase30b7d6m3Busy = true;
            CheckUpdatesButton.IsEnabled = false;
            InstallVerifiedUpdateButton.IsEnabled = false;
            try
            {
                int status = await Task.Run(() => Phase30B7D6M3EInstallConsentGate.RunFromUi(path));
                if (!_phase30b2Closed)
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
                if (!_phase30b2Closed) UpdateStatusText.Text = "Installation blocked; no MSI started.";
            }
            finally
            {
                _phase30b7d6m3eApprovedCandidate = null;
                _phase30b7d6m3Busy = false;
                if (!_phase30b2Closed)
                {
                    InstallVerifiedUpdateButton.Visibility = Visibility.Collapsed;
                    CheckUpdatesButton.IsEnabled = true;
                }
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
            // Read-only status from the latest protected detached MSI transaction.
            string latestInstall = Phase30BProductionInstaller.LastResultForAbout();
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
            PrepareSignedMsiButton.Click += async (sender, e) => await Phase30B7D6M3PrepareSignedMsiAsync();
            ReviewSignedMsiButton.Click += async (sender, e) => await Phase30B7D6M3BReviewSignedMsiAsync();
            PrepareProtectedMsiButton.Click += async (sender, e) => await Phase30B7D6M3CPrepareProtectedMsiAsync();
            ReviewProtectedMsiButton.Click += async (sender, e) => await Phase30B7D6M3DReviewProtectedMsiAsync();
            InstallVerifiedUpdateButton.Click += async (sender, e) => await Phase30B7D6M3EInstallVerifiedUpdateAsync();
            Closed += (sender, e) =>
            {
                _phase30b2Closed = true;
                if (_phase30b7d6m3Cancellation != null)
                    _phase30b7d6m3Cancellation.Cancel();
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
