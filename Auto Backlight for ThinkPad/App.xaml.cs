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

using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Main control flow of the application
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Access to Registry key for this application
        /// </summary>
        public static RegistryKey RegistryKey;

        /// <summary>
        /// Main application
        /// </summary>
        private readonly System.Diagnostics.Stopwatch _phase22Clock = System.Diagnostics.Stopwatch.StartNew();
        private readonly System.Collections.Generic.List<string> _phase22Timings =
            new System.Collections.Generic.List<string>();
        private long _phase22LastMs;
        private void _Phase22Checkpoint(string name)
        {
            long elapsed = _phase22Clock.ElapsedMilliseconds;
            long delta = elapsed - _phase22LastMs;
            _phase22LastMs = elapsed;
            string line = name + ": elapsed=" + elapsed + "ms; since previous=" + delta + "ms";
            _phase22Timings.Add(line);
            DiagnosticsLog.Info("Startup timing: " + line);
        }
        private string _Phase22Report()
        {
            return string.Join(Environment.NewLine, _phase22Timings.ToArray());
        }
        public App()
        {
            DiagnosticsLog.Info("App constructor started; PID=" + System.Diagnostics.Process.GetCurrentProcess().Id);
            // v5.5 startup checkpoint: expose where initialization stops.
            // Check for updates in Release mode
// v5.2: automatic update checks disabled; users visit fork Releases manually.
            DiagnosticsLog.Info("Startup checkpoint: InitializeComponent begin");
            try
            {
                InitializeComponent();
                DiagnosticsLog.Info("Startup checkpoint: InitializeComponent complete");
            _Phase22Checkpoint("InitializeComponent");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Startup checkpoint: InitializeComponent failed", ex);
                throw;
            }

            // Hook startup / exit actions
            Startup += _Starting;
            Exit += _Exiting;
            SessionEnding += (sender, e) => _Exiting(null, null);

            // Initialize controller components
            DiagnosticsLog.Info("Startup checkpoint: resolve Settings begin");
            _settings = (Settings)FindResource("Settings");
            DiagnosticsLog.Info("Startup checkpoint: resolve Settings complete");
            _Phase22Checkpoint("Settings resource");
            DiagnosticsLog.Info("Startup checkpoint: resolve HiddenWindow begin");
            var c = (Panel)((Window)FindResource("HiddenWindow")).Content;
            DiagnosticsLog.Info("Startup checkpoint: resolve HiddenWindow complete");
            _Phase22Checkpoint("Hidden window resource");
            var pm = c.Children.OfType<PowerManagement.PowerManagement>().First();
            var ri = c.Children.OfType<RawInput.RawInput>().First();
            var gh = c.Children.OfType<GlobalHotKey>().First();
            DiagnosticsLog.Info("Startup checkpoint: input and power components resolved");
            _Phase22Checkpoint("Input and power resources");
            try
            {
                var phase221Clock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    _autoKeyboard = new AutoKeyboardBacklightController(pm, ri);
                }
                finally
                {
                    DiagnosticsLog.Info("Phase22.1 startup: Keyboard controller constructor elapsed=" +
                        phase221Clock.ElapsedMilliseconds + "ms");
                }
                DiagnosticsLog.Info("Keyboard controller initialized");
            _Phase22Checkpoint("Keyboard controller");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Keyboard controller failed; screen features remain available", ex);
                _autoKeyboard = null;
            }
            try
            {
                _autoScreen = new AutoScreenBrightnessController(gh);
                DiagnosticsLog.Info("Screen controller initialized");
            _Phase22Checkpoint("Screen controller");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Screen controller failed; keyboard features remain available", ex);
                _autoScreen = null;
            }
            if (_autoScreen != null) _autoScreen.BrightnessChanged += (se, ev) =>
            {
                _lastPoint = new DataPoint(ev.Intensity, ev.Brightness);
                // Update the calibration graph if it is open
                if (_calibration != null)
                {
                    _calibration.CurrentPoint = new DataPoint(ev.Intensity, ev.Brightness);
                }
            };
            if (_autoKeyboard != null) _autoKeyboard.Activity += (se, ev) =>
            {
                // Record changes in OnLevel, used next time application runs
                if (ev.Act == AutoKeyboardBacklightController.ActivityEventArgs.Activity.OnLevelChanged)
                {
                    _settings.Keyboard_OnLevel = _autoKeyboard.OnLevel;
                }
                // Refresh screen brightness on power resume events
                if (ev.Act == AutoKeyboardBacklightController.ActivityEventArgs.Activity.PowerResume)
                {
                    if (_autoScreen != null && _autoScreen.Enabled) _ = _autoScreen.Retrigger();
                }
            };

            // Triggers loading of the icon & menu in tray
            DiagnosticsLog.Info("Startup checkpoint: tray resources begin");
            FindResource("SysTrayMenu");
            FindResource("SysTrayIcon");
            DiagnosticsLog.Info("Tray icon initialized");
            _Phase22Checkpoint("Tray resources");
            DiagnosticsLog.Info("Startup checkpoint: hardware report begin");
            DiagnosticsReport.Write(_autoKeyboard != null, _autoScreen != null,
                _autoKeyboard != null ? (int?)_autoKeyboard.MaximumBrightnessLevel : null,
                _autoKeyboard != null ? _autoKeyboard.RawInputDiagnostics : null,
                _Phase22Report());
            DiagnosticsLog.Info("Startup checkpoint: App constructor complete");
            _Phase22Checkpoint("App constructor complete");
        }

        private Settings _settings;
        private AutoKeyboardBacklightController _autoKeyboard;
        private AutoScreenBrightnessController _autoScreen;
        private object _lastPoint;

        // Setup on app startup
        private void _Starting(object sender, StartupEventArgs e)
        {
            DiagnosticsLog.Info("Startup event: settings restore begin");
            // Null registry key means no persistence; existing unreadable bytes stay untouched.
            try
            {
                RegistryKey = Registry.CurrentUser.CreateSubKey(@"Software\" + Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>().Product);
                if (RegistryKey == null)
                    throw new InvalidOperationException("Settings registry key could not be opened");

                object saved = RegistryKey.GetValue("Settings");
                if (saved == null)
                {
                    DiagnosticsLog.Info("Settings restore: no saved values; using defaults; external input=" + _settings.Keyboard_ExternalDevices);
                    if (!_settings.SaveToRegistry(RegistryKey))
                        DiagnosticsLog.Info("Settings: initial registry write failed; using defaults in memory");
                    else
                        RegistryKey.Flush();
                }
                else if (saved is byte[] && _settings.RestoreFromRegistry(RegistryKey))
                {
                    DiagnosticsLog.Info("Settings restore: saved values loaded; external input=" + _settings.Keyboard_ExternalDevices);
                }
                else
                {
                    DiagnosticsLog.Info("Settings restore: saved values unreadable; using defaults; external input=" + _settings.Keyboard_ExternalDevices);
                    DiagnosticsLog.Info("Settings: existing registry bytes preserved; persistence disabled for this session");
                    RegistryKey.Dispose();
                    RegistryKey = null;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Settings registry unavailable; using in-memory settings", ex);
                if (RegistryKey != null)
                {
                    try { RegistryKey.Dispose(); } catch (Exception) { }
                    RegistryKey = null;
                }
            }
            _InitializeLenovoOsdPreference();
            _settings.PropertyChanged += _Settings_PropertyChanged;
            _settings.Update();
            DiagnosticsLog.Info("Startup event: settings applied");
        }
        // The Lenovo OSD switch is global to the current Windows user.
        // It is persisted separately so existing binary serialized settings stay compatible.
        private const string LenovoOsdRegistryPath = @"Software\Lenovo\OnScreenDisplay";
        private const string LenovoOsdPreferenceName = "DisableOnScreenDisplay";

        // OSD independent registry access (v1.3.0 fix): the serialized Settings
        // value may be unreadable across assembly versions. Keep those bytes intact,
        // but open a separate handle to the app key for this independent preference.
        private RegistryKey _OpenOsdPreferenceKey()
        {
            string product = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyProductAttribute>().Product;
            return Registry.CurrentUser.CreateSubKey(@"Software\" + product);
        }

        private void _InitializeLenovoOsdPreference()
        {
            try
            {
                // Do not create Lenovo's key if its OSD functionality is absent.
                using (RegistryKey lenovo = Registry.CurrentUser.OpenSubKey(LenovoOsdRegistryPath, false))
                {
                    object actual = lenovo == null ? null : lenovo.GetValue("Enable", null);
                    // PHASE19_OSD_LOGGING: distinguish absent/unsupported registry configuration.
                    if (lenovo == null)
                        DiagnosticsLog.Info("Lenovo OSD: unavailable; Lenovo registry key missing");
                    else if (Array.IndexOf(lenovo.GetValueNames(), "Enable") < 0)
                        DiagnosticsLog.Info("Lenovo OSD: Enable missing; eligible for opt-in creation on user toggle");
                    else if (lenovo.GetValueKind("Enable") != RegistryValueKind.DWord)
                        DiagnosticsLog.Info("Lenovo OSD: unexpected Enable type (legacy validation retained)=" + lenovo.GetValueKind("Enable"));
                    else if (!(actual is int) || ((int)actual != 0 && (int)actual != 1))
                        DiagnosticsLog.Info("Lenovo OSD: unavailable; Enable value unsupported=" + (actual == null ? "null" : actual.ToString()));
                    // PHASE21_MISSING_OSD_ENABLE: missing value is supported, but never created on startup.
                    // A missing Lenovo key or malformed existing value remains unsupported.
                    bool missingEnable = lenovo != null &&
                        Array.IndexOf(lenovo.GetValueNames(), "Enable") < 0;
                    if (missingEnable)
                    {
                        _settings.LenovoOsd_Disabled = false; // no value means OSD visible
                        _settings.LenovoOsd_Available = true;
                        DiagnosticsLog.Info("Lenovo OSD: Enable missing; option available; no registry value created at startup");
                        return;
                    }
                    if (lenovo == null || lenovo.GetValueKind("Enable") != RegistryValueKind.DWord ||
                        !(actual is int) || ((int)actual != 0 && (int)actual != 1))
                        return;
                    DiagnosticsLog.Info("Lenovo OSD: init; Enable=" + actual + "; registryType=" + lenovo.GetValueKind("Enable"));
                    using (RegistryKey osdKey = _OpenOsdPreferenceKey())
                    {
                        if (osdKey == null)
                        {
                            DiagnosticsLog.Info("Lenovo OSD: separate preference registry unavailable; menu item unavailable");
                            return;
                        }
                        object stored = osdKey.GetValue(LenovoOsdPreferenceName, null);
                        DiagnosticsLog.Info("Lenovo OSD: saved preference=" + (stored == null ? "missing" : stored.ToString()) +
                            "; type=" + (stored == null ? "missing" : osdKey.GetValueKind(LenovoOsdPreferenceName).ToString()));
                        bool desired;
                        if (stored is int && ((int)stored == 0 || (int)stored == 1))
                        {
                            desired = (int)stored == 1;
                        }
                        else
                        {
                            // First use: adopt the CURRENT Lenovo setting. Do not touch
                            // the binary Settings value, even if legacy restore failed.
                            desired = (int)actual == 0;
                            osdKey.SetValue(LenovoOsdPreferenceName, desired ? 1 : 0, RegistryValueKind.DWord);
                            osdKey.Flush();
                            DiagnosticsLog.Info("Lenovo OSD: adopted current preference; disabled=" + desired);
                        }
                        DiagnosticsLog.Info("Lenovo OSD: init resolved; disabled=" + desired +
                            "; Enable=" + actual + "; action=" + (((int)actual == 0) != desired ? "apply" : "none"));
                        _settings.LenovoOsd_Disabled = desired;
                        _settings.LenovoOsd_Available = true;
                        if (((int)actual == 0) != desired)
                            _ApplyLenovoOsdPreference(desired);
                        else
                            DiagnosticsLog.Info("Lenovo OSD: current state already matches stored preference");
                    }
                }
            }
            catch (Exception ex)
            {
                _settings.LenovoOsd_Available = false;
                DiagnosticsLog.Error("Lenovo OSD: initialization failed", ex);
            }
        }

        private void _ApplyLenovoOsdPreference(bool disabled)
        {
            try
            {
                if (!_settings.LenovoOsd_Available) return;
                DiagnosticsLog.Info("Lenovo OSD: change requested; disabled=" + disabled + "; target Enable=" + (disabled ? 0 : 1));
                using (RegistryKey osdKey = _OpenOsdPreferenceKey())
                {
                    if (osdKey == null)
                        throw new InvalidOperationException("Lenovo OSD preference registry unavailable");
                    // Persist separately: never rewrite legacy binary Settings.
                    osdKey.SetValue(LenovoOsdPreferenceName, disabled ? 1 : 0, RegistryValueKind.DWord);
                    osdKey.Flush();
                }
                using (RegistryKey lenovo = Registry.CurrentUser.OpenSubKey(LenovoOsdRegistryPath, true))
                {
                    if (lenovo == null) throw new InvalidOperationException("Lenovo OSD key disappeared");
                    object current = lenovo.GetValue("Enable", null);
                    // PHASE21_MISSING_OSD_ENABLE: create only the absent DWORD after user action.
                    bool missingEnable = Array.IndexOf(lenovo.GetValueNames(), "Enable") < 0;
                    if (!missingEnable && (lenovo.GetValueKind("Enable") != RegistryValueKind.DWord ||
                        !(current is int) || ((int)current != 0 && (int)current != 1)))
                        throw new InvalidOperationException("Lenovo OSD Enable setting has unsupported type/value");
                    int expected = disabled ? 0 : 1;
                    DiagnosticsLog.Info("Lenovo OSD: before change; Enable=" + (missingEnable ? "missing" : current.ToString()) + "; expected=" + expected);
                    if (missingEnable || (int)current != expected)
                    {
                        lenovo.SetValue("Enable", expected, RegistryValueKind.DWord);
                        lenovo.Flush();
                        DiagnosticsLog.Info("Lenovo OSD: Enable " + (missingEnable ? "created" : "set") + " to " + expected);
                    }
                    else DiagnosticsLog.Info("Lenovo OSD: Enable already " + expected);
                    // Read-back is diagnostic only; it never retries or modifies settings.
                    object verified = lenovo.GetValue("Enable", null);
                    bool matches = verified is int && ((int)verified == expected) &&
                        lenovo.GetValueKind("Enable") == RegistryValueKind.DWord;
                    DiagnosticsLog.Info("Lenovo OSD: read-back; Enable=" + (verified == null ? "missing" : verified.ToString()) +
                        "; type=" + (verified == null ? "missing" : lenovo.GetValueKind("Enable").ToString()) +
                        "; matchesRequest=" + matches);
                    if (!matches)
                        DiagnosticsLog.Info("WARNING Lenovo OSD: preference/Enable mismatch after write; savedDisabled=" + disabled +
                            "; expectedEnable=" + expected);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Lenovo OSD: could not apply requested preference; possible preference/Enable mismatch; disabled=" + disabled, ex);
            }
        }

        // Connect changes in settings (from gui bindings in xaml) to controllers
        private void _Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
            if (e.PropertyName == nameof(_settings.LenovoOsd_Available)) return;
            if (e.PropertyName == nameof(_settings.LenovoOsd_Disabled))
            {
                _ApplyLenovoOsdPreference(_settings.LenovoOsd_Disabled);
                return; // Do not rewrite the legacy binary settings for an independent option.
            }
            if (_autoKeyboard != null && e.PropertyName == nameof(_settings.Keyboard_Enabled))
                _autoKeyboard.Enabled = _settings.Keyboard_Enabled;

            if (_autoKeyboard != null && e.PropertyName == nameof(_settings.Keyboard_InactivityTimeout))
                _autoKeyboard.InactivityTimeout = _settings.Keyboard_InactivityTimeout;

            if (_autoKeyboard != null && e.PropertyName == nameof(_settings.Keyboard_ExternalDevices))
                _autoKeyboard.IncludeExternalDevices = _settings.Keyboard_ExternalDevices;

            if (_autoKeyboard != null && e.PropertyName == nameof(_settings.Keyboard_OnLevel))
                _autoKeyboard.OnLevel = _settings.Keyboard_OnLevel;

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_Enabled))
                _autoScreen.Enabled = _settings.Screen_Enabled;

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_PollPeriod))
                _autoScreen.PollPeriod = _settings.Screen_PollPeriod;

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_HotKeyEnabled))
                _autoScreen.HotKeyEnabled = _settings.Screen_HotKeyEnabled;

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_HotKey))
                _autoScreen.HotKey = HotKey.Parse(_settings.Screen_HotKey);

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_LearnedPoints))
                _autoScreen.LearnedPoints = _settings.Screen_LearnedPoints.Select(p => new DataPoint(p.Item1, p.Item2)).ToList();

            if (_autoScreen != null && e.PropertyName == nameof(_settings.Screen_Curvature))
                _autoScreen.Curvature = _settings.Screen_Curvature;

            if (RegistryKey != null)
            {
                if (_settings.SaveToRegistry(RegistryKey))
                {
                    RegistryKey.Flush();
                    DiagnosticsLog.Info("Settings save succeeded; property=" + e.PropertyName);
                }
                else
                    DiagnosticsLog.Info("Settings save FAILED; property=" + e.PropertyName);
            }
            else
                DiagnosticsLog.Info("Settings save skipped: registry persistence unavailable; property=" + e.PropertyName);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Setting change failed: " + e.PropertyName, ex);
                // Keep the tray responsive if a device becomes unavailable after startup.
            }
        }
        // Stop and cleanup on app exit
        private void _Exiting(object sender, ExitEventArgs e)
        {
            try { if (_autoKeyboard != null) _autoKeyboard.Stop(); }
            catch (Exception ex) { DiagnosticsLog.Error("Keyboard shutdown failed", ex); }
            try { if (_autoScreen != null) _autoScreen.Stop(); }
            catch (Exception ex) { DiagnosticsLog.Error("Screen shutdown failed", ex); }

            try
            {
                if (RegistryKey != null)
                {
                    if (_settings != null)
                    {
                        if (_settings.SaveToRegistry(RegistryKey))
                        {
                            RegistryKey.Flush();
                            DiagnosticsLog.Info("Settings exit save succeeded");
                        }
                        else
                            DiagnosticsLog.Info("Settings exit save FAILED");
                    }
                    RegistryKey.Close();
                    RegistryKey = null;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Settings shutdown persistence failed", ex);
            }
        }
        // Show the screen brightness vs ambient light level calibration window
        private void _ShowCalibration(object sender, RoutedEventArgs e)
        {
            if (_autoScreen == null)
            {
                MessageBox.Show("The screen brightness controller is unavailable. Check the diagnostic log.",
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_calibration == null)
            {
                // Create new window from saved points if not already open
                _calibration = new Calibration(_settings.Screen_LearnedPoints.Select(p => new DataPoint(p.Item1, p.Item2)).ToList(), _settings.Screen_Curvature);
                if (_lastPoint != null) _calibration.CurrentPoint = (DataPoint)_lastPoint;
                var isEn = _autoScreen.Enabled;
                var isHkEn = _autoScreen.HotKeyEnabled;
                _autoScreen.Enabled = false;
                _autoScreen.HotKeyEnabled = false;
                var tempScreen = new AutoScreenBrightnessController(null);
                tempScreen.BrightnessChanged += (sen, eve) =>
                {
                    _calibration.CurrentPoint = new DataPoint(eve.Intensity, eve.Brightness);
                };
                _calibration.Refresh += (sen, eve) =>
                {
                    tempScreen.LearnedPoints = eve.LearnedPoints;
                    tempScreen.Curvature = eve.Curvature;
                    _ = tempScreen.Retrigger();
                };
                _calibration.ManualScreenBrightness += (sen, eve) =>
                {
                    byte br = (byte)Math.Round(eve.ScreenBrightness * 100.0);
                    tempScreen.SetBrightnessSlider(br);
                    _lastPoint = new DataPoint(_calibration.CurrentPoint.X, eve.ScreenBrightness);
                    _calibration.CurrentPoint = (DataPoint)_lastPoint;
                };
                _calibration.Closed += (sen, eve) =>
                {
                    _calibration = null;
                    _autoScreen.Enabled = isEn;
                    _autoScreen.HotKeyEnabled = isHkEn;
                };
                _calibration.Apply += (sen, eve) =>
                {
                    // Transfer points on Apply
                    _settings.Screen_Curvature = _calibration.Curvature;
                    _settings.Screen_LearnedPoints.Clear();
                    _settings.Screen_LearnedPoints.AddRange(_calibration.LearnedPoints.Select(p => new Tuple<double, double>(p.X, p.Y)).ToList());
                    _settings.Screen_LearnedPoints = _settings.Screen_LearnedPoints;

                    _ = _autoScreen.Retrigger();
                };
            }
            _calibration.Show();
        }
        // The tray context menu captures/deactivates focus while closing.
        // A short one-shot delay lets the menu release focus before showing the dialog.
        // PHASE18_EXPORT_APPLICATION_LOG: export a read-only snapshot of application.log.
        // Match the existing diagnostics action: release the tray menu before opening an owned dialog.
        private void _ExportApplicationLog(object sender, RoutedEventArgs e)
        {
            DiagnosticsLog.Info("Export application log requested");
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            EventHandler tick = null;
            tick = (o, args) =>
            {
                timer.Stop();
                timer.Tick -= tick;
                _ExportApplicationLogCore();
            };
            timer.Tick += tick;
            timer.Start();
        }

        private void _ExportApplicationLogCore()
        {
            Window owner = null;
            try
            {
                string source = DiagnosticsLog.FilePath;
                if (!System.IO.File.Exists(source))
                {
                    MessageBox.Show("No application log is available yet.",
                        "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                owner = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = 10,
                    Top = 10,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = true,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Opacity = 0.01,
                    Topmost = true
                };
                owner.Show();
                owner.Activate();

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export application log",
                    FileName = "application.log",
                    Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
                    AddExtension = true,
                    DefaultExt = ".log",
                    OverwritePrompt = true,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };
                bool? result = dialog.ShowDialog(owner);
                if (result != true) return;

                string destination = System.IO.Path.GetFullPath(dialog.FileName);
                if (String.Equals(System.IO.Path.GetFullPath(source), destination,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The original application log cannot be overwritten.");

                // Read an active log without stopping logging; allow concurrent append and rotation.
                // Write to a temporary sibling, then replace the destination only after a full copy.
                string temp = destination + ".phase18-" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var input = new System.IO.FileStream(source, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete))
                    using (var output = new System.IO.FileStream(temp, System.IO.FileMode.CreateNew,
                        System.IO.FileAccess.Write, System.IO.FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                    if (System.IO.File.Exists(destination))
                        System.IO.File.Replace(temp, destination, null);
                    else
                        System.IO.File.Move(temp, destination);
                }
                finally
                {
                    if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
                }
                DiagnosticsLog.Info("Application log exported by user");
                MessageBox.Show("Application log exported successfully.",
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Application log export failed", ex);
                MessageBox.Show("Could not export the application log. See application.log for details.",
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (owner != null) owner.Close();
            }
        }

        private void _ExportDiagnosticReport(object sender, RoutedEventArgs e)
        {
            DiagnosticsLog.Info("Export diagnostic report requested");
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            EventHandler tick = null;
            tick = (o, args) =>
            {
                timer.Stop();
                timer.Tick -= tick;
                _ExportDiagnosticReportCore();
            };
            timer.Tick += tick;
            timer.Start();
        }

        private void _ExportDiagnosticReportCore()
        {
            Window owner = null;
            try
            {
                DiagnosticsReport.Write(_autoKeyboard != null, _autoScreen != null,
                    _autoKeyboard != null ? (int?)_autoKeyboard.MaximumBrightnessLevel : null,
                    _autoKeyboard != null ? _autoKeyboard.RawInputDiagnostics : null,
                    _Phase22Report());
                var source = DiagnosticsReport.ReportPath;
                if (!System.IO.File.Exists(source))
                    throw new System.IO.FileNotFoundException("Diagnostic report was not generated", source);

                // Provide a real, visible top-level HWND to own the common dialog.
                // The transparent 1x1 window remains practically invisible to the user.
                owner = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = 10,
                    Top = 10,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = true,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Opacity = 0.01,
                    Topmost = true
                };
                owner.Show();
                owner.Activate();

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export hardware diagnostic report",
                    FileName = "auto-backlight-hardware-report.txt",
                    Filter = "Text files (*.txt)|*.txt",
                    AddExtension = true,
                    DefaultExt = ".txt",
                    OverwritePrompt = true
                };
                DiagnosticsLog.Info("Opening owned diagnostic SaveFileDialog");
                bool? result = dialog.ShowDialog(owner);
                DiagnosticsLog.Info("Owned SaveFileDialog returned " +
                    (result.HasValue ? result.Value.ToString() : "null"));
                if (result != true) return;
                System.IO.File.Copy(source, dialog.FileName, true);
                DiagnosticsLog.Info("Hardware diagnostic report exported by user");
                MessageBox.Show("Diagnostic report exported successfully.",
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Diagnostic export failed", ex);
                MessageBox.Show("Could not export the diagnostic report. See application.log for details.",
                    "Auto Backlight for ThinkPad", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (owner != null) owner.Close();
            }
        }

        // Show the application about window
        private void _ShowAbout(object sender, RoutedEventArgs e)
        {
            if (_about == null)
            {
                // Create new window if not already open
                _about = new About();
                _about.Closed += (sen, eve) => _about = null;
            }
            _about.Show();
        }
        private About _about;
        private Calibration _calibration;

        // Exit from menu performs clean shut down of application
        private void _ExitApplication(object sender, RoutedEventArgs e) => Shutdown();

        #region Boxes

        // Show initial text
        private void _HotKeyBox_Init(object sender, EventArgs e)
        {
            TextBox tb = (TextBox)sender;
            tb.Text = _settings.Screen_HotKey.ToString();
        }
        // HotKey box, capture keyboard shortcut as it is pressed
        private void _HotKeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            e.Handled = true;
            var mod = Keyboard.Modifiers;
            var key = e.Key;

            // When Alt is pressed, SystemKey is used instead
            if (key == Key.System)
                key = e.SystemKey;

            // Pressing delete, backspace or escape without modifiers clears the current value
            if (mod == ModifierKeys.None &&
                (key == Key.Delete || key == Key.Back || key == Key.Escape))
            {
                tb.Clear();
                return;
            }
            // If no actual key was pressed yet
            if (key == Key.LeftCtrl ||
                key == Key.RightCtrl ||
                key == Key.LeftAlt ||
                key == Key.RightAlt ||
                key == Key.LeftShift ||
                key == Key.RightShift ||
                key == Key.LWin ||
                key == Key.RWin)
            {
                tb.Text = new HotKey(mod, Key.None).ToString();
                return;
            }
            tb.Text = new HotKey(mod, key).ToString();
        }
        // HotKey box, block the shortcut from triggering while user is editing
        private void _HotKeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            if (_autoScreen != null) _autoScreen.HotKeyEnabled = false;
        }
        // HotKey box, save final shortcut after user is done editing, unblock shortcut
        private void _HotKeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TextBox tb = (TextBox)sender;
            _settings.Screen_HotKey = tb.Text;
            if (_autoScreen != null) _autoScreen.HotKeyEnabled = _settings.Screen_HotKeyEnabled;
        }

        private MenuItem _scm;
        private MenuItem _kbm;
        private MenuItem _hkm;
        private void ScreenMenu_Init(object sender, EventArgs e) => _scm = (MenuItem)sender;
        private void ScreenCombo_Enter(object sender, MouseEventArgs e) => _scm.IsCheckable = false;
        private void ScreenCombo_Leave(object sender, MouseEventArgs e) => _scm.IsCheckable = true;
        private void KeyboardMenu_Init(object sender, EventArgs e) => _kbm = (MenuItem)sender;
        private void KeyboardCombo_Enter(object sender, MouseEventArgs e) => _kbm.IsCheckable = false;
        private void KeyboardCombo_Leave(object sender, MouseEventArgs e) => _kbm.IsCheckable = true;
        private void HotkeyMenu_Init(object sender, EventArgs e) => _hkm = (MenuItem)sender;
        private void HotkeyBox_Enter(object sender, MouseEventArgs e) => _hkm.IsCheckable = false;
        private void HotkeyBox_Leave(object sender, MouseEventArgs e) => _hkm.IsCheckable = true;

        #endregion Boxes
    }
    /// <summary>
    /// Round data-point coordinate for display, range 0 to 100, no decimals
    /// </summary>
    public class Rounder : IValueConverter
    {
        /// <summary>
        /// From double to string
        /// </summary>
        /// <param name="value">input double</param>
        /// <param name="targetType">double</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns></returns>
        public object Convert(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(double) && targetType == typeof(string))
            {
                if (value == null) return "";
                double val = (double)value;
                int rounded = (int)Math.Round(val);
                return rounded.ToString();
            }
            throw new ArgumentException();
        }
        /// <summary>
        /// From user string to double
        /// </summary>
        /// <param name="value">string</param>
        /// <param name="targetType">double</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns>double</returns>
        public object ConvertBack(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(string) && targetType == typeof(double))
            {
                if (value == null) return double.NaN;
                string str = (string)value;
                double dbl;
                if (double.TryParse(str, out dbl))
                    if (dbl >= 0 && dbl <= 100) return dbl;
                return double.NaN;
            }
            throw new ArgumentException();
        }
    }
    /// <summary>
    /// Format time in seconds as printable friendly string
    /// </summary>
    public class TimeFormatter : IValueConverter
    {
        /// <summary>
        /// Convert seconds to friendly display string
        /// </summary>
        /// <param name="value">time in seconds (double)</param>
        /// <param name="targetType">string</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns>friendly display string, "" if error</returns>
        public object Convert(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(double) && targetType == typeof(string))
            {
                if (value == null) return "";
                double val = (double)value;
                if (val == double.PositiveInfinity) return "Never";

                var ts = TimeSpan.FromSeconds(val);
                string[] strs = { "", "", "" };
                strs[0] = ts.Hours > 0 ? ts.Hours.ToString() + " hr" : "";
                strs[1] = ts.Minutes > 0 ? ts.Minutes.ToString() + " min" : "";
                strs[2] = ts.Seconds > 0 ? ts.Seconds.ToString() + " sec" : "";
                return string.Join(" ", strs).Trim();
            }
            throw new ArgumentException();
        }
        /// <summary>
        /// Parse user string into seconds
        /// </summary>
        /// <param name="value">user string</param>
        /// <param name="targetType">double</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns>time in seconds</returns>
        public object ConvertBack(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(string) && targetType == typeof(double))
            {
                if (value == null) return double.NaN;
                string str = (string)value;

                // Clean to only lowercase alphanumeric
                str = str.Trim().ToLower();
                str = Regex.Replace(str, @"[^a-z\d]+", " ");
                if (str == "never" || str.StartsWith("inf")) return double.PositiveInfinity;

                // Contains h,m,s labels
                if (str.Any(c => char.IsLetter(c)))
                {
                    var h = Regex.Match(str, @"(\d+\s*h[a-z]*)");
                    if (h.Success) str = str.Remove(h.Index, h.Length);
                    var m = Regex.Match(str, @"(\d+\s*m[a-z]*)");
                    if (m.Success) str = str.Remove(m.Index, m.Length);
                    var s = Regex.Match(str, @"(\d+\s*s[a-z]*)");
                    if (s.Success) str = str.Remove(s.Index, s.Length);
                    if (str.Trim().Length > 0) return double.PositiveInfinity;

                    TimeSpan ts = new TimeSpan();
                    string hs = new string(h.Value.TakeWhile(c => char.IsDigit(c)).ToArray());
                    string ms = new string(m.Value.TakeWhile(c => char.IsDigit(c)).ToArray());
                    string ss = new string(s.Value.TakeWhile(c => char.IsDigit(c)).ToArray());
                    if (hs.Length > 0) ts += TimeSpan.FromHours(int.Parse(hs));
                    if (ms.Length > 0) ts += TimeSpan.FromMinutes(int.Parse(ms));
                    if (ss.Length > 0) ts += TimeSpan.FromSeconds(int.Parse(ss));
                    if (ts.TotalSeconds < 1) return double.PositiveInfinity;
                    return ts.TotalSeconds;
                }
                // Contains no labels, only separators
                else
                {
                    var strs = str.Split(null).ToList();
                    int ins = 3 - strs.Count;
                    if (ins >= 3 || ins < 0) return double.PositiveInfinity;
                    for (int i = 0; i < ins; i++) strs.Insert(0, "");

                    TimeSpan ts = new TimeSpan();
                    string hs = strs[0];
                    string ms = strs[1];
                    string ss = strs[2];
                    if (hs.Length > 0) ts += TimeSpan.FromHours(int.Parse(hs));
                    if (ms.Length > 0) ts += TimeSpan.FromMinutes(int.Parse(ms));
                    if (ss.Length > 0) ts += TimeSpan.FromSeconds(int.Parse(ss));
                    if (ts.TotalSeconds < 1) return double.PositiveInfinity;
                    return ts.TotalSeconds;
                }
            }
            throw new ArgumentException();
        }
    }
    /// <summary>
    /// Format brightness level (0/1/2) into friendly text
    /// </summary>
    public class BrightnessLevelFormatter : IValueConverter
    {
        /// <summary>
        /// Convert level to friendly display string
        /// </summary>
        /// <param name="value">0/1/2</param>
        /// <param name="targetType">string</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns>friendly display string</returns>
        public object Convert(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(int) && targetType == typeof(string))
            {
                if (value == null) return "";
                int val = (int)value;
                if (val == 0) return "Off";
                if (val == 1) return "Low";
                if (val == 2) return "High";
            }
            throw new ArgumentException();
        }
        /// <summary>
        /// Parse string into level
        /// </summary>
        /// <param name="value">string</param>
        /// <param name="targetType">int</param>
        /// <param name="parameter">unused</param>
        /// <param name="culture">unused</param>
        /// <returns>level 0/1/2</returns>
        public object ConvertBack(object value, Type targetType, object parameter = null, CultureInfo culture = null)
        {
            if (value.GetType() == typeof(string) && targetType == typeof(int))
            {
                if (value == null) return -1;
                string str = (string)value;
                if (str == "Off") return 0;
                if (str == "Low") return 1;
                if (str == "High" || str == "Full") return 2;
            }
            throw new ArgumentException();
        }
    }
}