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
using Auto_Backlight_for_ThinkPad.RawInput;
using System;
using System.IO;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Capture raw input from recognized built-in ThinkPad pointing devices and keyboard.
    /// Adapted for LEN0402 and tolerant of missing/renamed devices.
    /// </summary>
    public class Capture
    {
        public event EventHandler CaptureEvent;
        public bool IncludeExternalDevices { get; set; } = false;
        // Phase 22 diagnostic counters. No device identifiers or input contents are retained.
        private int _rescanCount, _rescanFailures, _arrivals, _removals;
        private int _lastDeviceCount = -1, _lastInternalCount, _lastExternalCount;
        private string _lastRescanTime = "not yet";
        public string DiagnosticSnapshot()
        {
            return "Capture enabled=" + _enabled + "; external opt-in=" + IncludeExternalDevices +
                "; global handler=attached; registered classes=" + (_enabled ? "requested (keyboard/mouse/touchpad)" : "not requested") +
                "; rescans=" + _rescanCount + "; failures=" + _rescanFailures +
                "; last rescan=" + _lastRescanTime +
                "; last enumerated=" + (_lastDeviceCount < 0 ? "unknown" : _lastDeviceCount.ToString()) +
                "; eligible internal=" + _lastInternalCount + "; eligible external=" + _lastExternalCount +
                "; arrivals=" + _arrivals + "; removals=" + _removals;
        }
        // PHASE20C1_DYNAMIC_RAWINPUT: device-independent subscription survives Bluetooth arrivals.
        private static bool IsEligible(Device dev, out bool internalDevice)
        {
            internalDevice = false;
            if (dev == null || string.IsNullOrEmpty(dev.Path)) return false;
            bool keyboard = dev.Type == Device.DeviceType.Keyboard;
            bool mouse = dev.Type == Device.DeviceType.Mouse;
            bool touchpad = dev.Type == Device.DeviceType.Hid && dev.Info is HidInfo &&
                ((HidInfo)dev.Info).usagePage == 0xD && ((HidInfo)dev.Info).usage == 0x5;
            internalDevice = ((keyboard || mouse) &&
                dev.Path.StartsWith(@"\\?\ACPI#", StringComparison.OrdinalIgnoreCase)) || touchpad;
            return keyboard || mouse || touchpad;
        }
        public void RefreshDevices()
        {
            try
            {
                int total = 0, internalCount = 0, externalCount = 0;
                foreach (Device device in _ri.EnumerateDevices())
                {
                    total++;
                    bool internalDevice;
                    if (IsEligible(device, out internalDevice))
                    {
                        if (internalDevice) internalCount++; else externalCount++;
                    }
                }
                _rescanCount++;
                _lastDeviceCount = total;
                _lastInternalCount = internalCount;
                _lastExternalCount = externalCount;
                _lastRescanTime = DateTimeOffset.Now.ToString("o");
                DiagnosticsLog.Info("Raw Input rescan completed; devices=" + total +
                    "; internal=" + internalCount + "; external=" + externalCount);
            }
            catch (Exception ex)
            {
                _rescanFailures++;
                DiagnosticsLog.Error("Raw Input rescan failed", ex);
            }
        }
        public void Start() => Enabled = true;
        public void Stop() => Enabled = false;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    if (value)
                    {
                        _ri.RegisterDeviceCollections(new DeviceCollection[] { _keyboardColl, _mouseColl, _touchpadColl });
                    }
                    else
                    {
                        _ri.UnregisterDeviceCollections();
                    }
                }
            }
        }

        public Capture(RawInput.RawInput ri)
        {
            _ri = ri;
            _mouseColl.usagePage = 0x1;
            _mouseColl.usage = 0x2;
            _mouseColl.flags = DeviceCollection.Flag.RIDEV_INPUTSINK | DeviceCollection.Flag.RIDEV_DEVNOTIFY;
            _keyboardColl.usagePage = 0x1;
            _keyboardColl.usage = 0x6;
            _keyboardColl.flags = DeviceCollection.Flag.RIDEV_INPUTSINK | DeviceCollection.Flag.RIDEV_DEVNOTIFY;
            _touchpadColl.usagePage = 0xd;
            _touchpadColl.usage = 0x5;
            _touchpadColl.flags = DeviceCollection.Flag.RIDEV_INPUTSINK | DeviceCollection.Flag.RIDEV_DEVNOTIFY;
            // Subscribe once to the global stream, rather than individual startup device objects.
            // RawInput's device dictionary is updated on WM_INPUT_DEVICE_CHANGE.
            _ri.InputReceived += (sender, e) =>
            {
                if (!_enabled) return;
                bool internalDevice;
                if (!IsEligible(e.Device, out internalDevice)) return;
                if (!internalDevice && !IncludeExternalDevices) return;
                CaptureEvent?.Invoke(this, EventArgs.Empty);
            };
            _ri.DeviceAdded += (sender, e) =>
            {
                _arrivals++;
                bool internalDevice;
                if (IsEligible(e.Device, out internalDevice))
                    DiagnosticsLog.Info("Raw Input device arrived: " +
                        (internalDevice ? "internal" : "external") + "; type=" + e.Device.Type +
                        "; externalOptIn=" + IncludeExternalDevices);
            };
            _ri.DeviceRemoved += (sender, e) =>
            {
                _removals++;
                bool internalDevice;
                if (IsEligible(e.Device, out internalDevice))
                    DiagnosticsLog.Info("Raw Input device removed: type=" + e.Device.Type);
            };

            var log = new StringBuilder();
            log.AppendLine("Auto Backlight for ThinkPad - Raw Input device diagnostics");
            log.AppendLine("Timestamp: " + DateTime.Now.ToString("o"));
            log.AppendLine("Built-in devices are monitored; external devices require opt-in.");
            int internalCount = 0, externalCount = 0;
            foreach (Device dev in _ri.EnumeratedDevices)
            {
                string path = dev.Path ?? string.Empty;
                bool keyboard = dev.Type == Device.DeviceType.Keyboard;
                bool mouse = dev.Type == Device.DeviceType.Mouse;
                bool touchpad = false;
                string hidDescription = "";
                if (dev.Type == Device.DeviceType.Hid && dev.Info is HidInfo)
                {
                    var hid = (HidInfo)dev.Info;
                    hidDescription = string.Format(" VID={0:X4} PID={1:X4} UsagePage={2:X} Usage={3:X}",
                        hid.vendorID, hid.productID, hid.usagePage, hid.usage);
                    touchpad = hid.usagePage == 0xD && hid.usage == 0x5;
                }
                // This is a heuristic, NOT an infallible built-in/external classification.
                // ACPI paths generally indicate internal devices. Digitizer touchpads count
                // as internal so modern Synaptics devices work without a hardcoded VID/PID.
                bool internalDevice = ((keyboard || mouse) &&
                    path.StartsWith(@"\\?\ACPI#", StringComparison.OrdinalIgnoreCase)) || touchpad;
                bool eligible = path.Length > 0 && (keyboard || mouse || touchpad);
                if (eligible)
                {
                    if (internalDevice) internalCount++; else externalCount++;
                }
                log.AppendLine(string.Format("{0} Type={1} Path={2}{3}",
                    !eligible ? "[SKIPPED]" : internalDevice ? "[INTERNAL]" : "[EXTERNAL-OPT-IN]",
                    dev.Type, path, hidDescription));
            }
            log.AppendLine(string.Format("Internal={0}, external candidates={1}", internalCount, externalCount));
            log.AppendLine("Phase20C1: global Raw Input event subscription; device notifications enabled.");
            if (internalCount == 0)
                log.AppendLine("WARNING: no internal inputs matched; device-classification may need adjustment.");

            // Diagnostics must never prevent the application from starting.
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Auto Backlight for ThinkPad");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "raw-input-devices.log"), log.ToString());
            }
            catch (Exception) { /* Logging is best-effort only. */ }
        }

        private bool _enabled = false;
        private DeviceCollection _mouseColl = new DeviceCollection();
        private DeviceCollection _touchpadColl = new DeviceCollection();
        private DeviceCollection _keyboardColl = new DeviceCollection();
        private RawInput.RawInput _ri;
    }
}
