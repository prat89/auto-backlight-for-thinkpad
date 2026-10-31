using System;
using System.IO;
using System.Management;
using System.Reflection;
using Microsoft.Win32;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class DiagnosticsReport
    {
        public static string ReportPath => Path.Combine(Path.GetDirectoryName(DiagnosticsLog.FilePath), "hardware-report.txt");
        public static void Write(bool keyboardAvailable, bool screenAvailable, int? maximumKeyboardLevel, string rawInputSnapshot = null, string startupTiming = null)
        {
            // Intentional: no user name, machine name, serial number, input events or camera frames.
            try
            {
                var dir = Path.GetDirectoryName(DiagnosticsLog.FilePath);
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("Auto Backlight for ThinkPad - hardware diagnostics");
                sb.AppendLine("Generated (local): " + DateTimeOffset.Now.ToString("o"));
                sb.AppendLine("Application version: " + Assembly.GetExecutingAssembly().GetName().Version);
                sb.AppendLine("OS: " + Environment.OSVersion.VersionString);
                sb.AppendLine("--- Startup timings (monotonic, diagnostic only) ---");
                sb.AppendLine(startupTiming ?? "unavailable");
                sb.AppendLine("--- Raw Input snapshot (no keypresses or device paths) ---");
                sb.AppendLine(rawInputSnapshot ?? "unavailable (keyboard controller not initialized)");
                sb.AppendLine("Keyboard controller: " + (keyboardAvailable ? "initialized" : "unavailable"));
                sb.AppendLine("Screen controller: " + (screenAvailable ? "initialized" : "unavailable"));
                sb.AppendLine("Keyboard brightness maximum reported by driver: " +
                    (maximumKeyboardLevel.HasValue ? maximumKeyboardLevel.Value.ToString() : "unavailable"));
                sb.AppendLine("Keyboard brightness control backend: " +
                    (keyboardAvailable ? "IBMPMDRV" : "unavailable"));
                // Read-only capability interpretation. Does not modify backlight hardware.
                if (!maximumKeyboardLevel.HasValue)
                    sb.AppendLine("Keyboard backlight capabilities: unavailable (driver/controller not initialized)");
                else if (maximumKeyboardLevel.Value < 0)
                    sb.AppendLine("Keyboard backlight capabilities: invalid driver level");
                else if (maximumKeyboardLevel.Value == 0)
                    sb.AppendLine("Keyboard backlight capabilities: no illuminated level reported");
                else if (maximumKeyboardLevel.Value == 1)
                    sb.AppendLine("Keyboard backlight capabilities: Off (0), Low (1)");
                else
                    sb.AppendLine("Keyboard backlight capabilities: Off (0), Low (1), High (2)" +
                        (maximumKeyboardLevel.Value > 2 ? ", additional levels reported (not exposed by UI)" : ""));
                try
                {
                    using (var query = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                    using (var items = query.Get())
                        foreach (ManagementObject item in items)
                        {
                            sb.AppendLine("Manufacturer: " + (item["Manufacturer"] ?? "unknown"));
                            sb.AppendLine("Model: " + (item["Model"] ?? "unknown"));
                            break;
                        }
                }
                catch (Exception ex) { DiagnosticsLog.Error("Hardware model diagnostic failed", ex); }
                try
                {
                    using (var query = new ManagementObjectSearcher("SELECT Name,State FROM Win32_SystemDriver WHERE Name='IBMPMDRV' OR Name='EnergyDrv'"))
                    using (var items = query.Get())
                        foreach (ManagementObject item in items)
                            sb.AppendLine("Driver: " + item["Name"] + " / " + item["State"]);
                }
                catch (Exception ex) { DiagnosticsLog.Error("Driver diagnostic failed", ex); }
                sb.AppendLine("--- Backend discovery (read-only) ---");
                foreach (string line in BackendDetection.GetReportLines())
                    sb.AppendLine(line);
                AppendExtendedDiagnostics(sb);
                // PHASE20A_READ_ONLY_DRIVER_PROBE: report raw GET only when controller initialized.
                sb.AppendLine("--- Keyboard driver probe (read-only) ---");
                sb.AppendLine("Backend: " + (keyboardAvailable ? "IBMPmDrv" : "unavailable"));
                if (keyboardAvailable)
                    sb.AppendLine(Backlight.ReadOnlyDriverProbe());
                else
                {
                    sb.AppendLine("GET operation: skipped (keyboard controller unavailable)");
                    sb.AppendLine("SET verification: not performed (read-only mode)");
                }
                File.WriteAllText(Path.Combine(dir,"hardware-report.txt"), sb.ToString(), Encoding.UTF8);
                DiagnosticsLog.Info("Hardware report written to Logs/hardware-report.txt");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Hardware report generation failed", ex);
            }
        }

        // Phase 17: read-only diagnostic snapshot. Do not mutate Lenovo settings or service state.
        private static void AppendExtendedDiagnostics(StringBuilder sb)
        {
            sb.AppendLine("--- Lenovo OSD diagnostics (read-only) ---");
            string osdPath = @"Software\Lenovo\OnScreenDisplay";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(osdPath, false))
                {
                    sb.AppendLine("OSD registry key (HKCU): " + (key == null ? "missing" : "present"));
                    if (key == null)
                    {
                        sb.AppendLine("OSD Enable: missing (key not found)");
                        sb.AppendLine("OSD menu eligibility (registry): unavailable - key missing");
                    }
                    else if (Array.IndexOf(key.GetValueNames(), "Enable") < 0)
                    {
                        sb.AppendLine("OSD Enable: missing");
                        sb.AppendLine("OSD menu eligibility (registry): eligible - Enable missing; created only on user toggle (Phase 21)");
                    }
                    else
                    {
                        RegistryValueKind kind = key.GetValueKind("Enable");
                        object val = key.GetValue("Enable", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                        string display = val is int ? ((int)val).ToString() : (val == null ? "null" : "non-integer");
                        sb.AppendLine("OSD Enable: " + display + " (" + kind + ")");
                        bool supported = kind == RegistryValueKind.DWord && val is int && ((int)val == 0 || (int)val == 1);
                        sb.AppendLine("OSD menu eligibility (registry): " + (supported ? "eligible" : "unavailable - unsupported Enable type/value"));
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("OSD registry inspection: failed (" + ex.GetType().Name + ")");
                DiagnosticsLog.Error("OSD registry inspection failed", ex);
            }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Auto Backlight for ThinkPad", false))
                {
                    if (key == null || Array.IndexOf(key.GetValueNames(), "DisableOnScreenDisplay") < 0)
                        sb.AppendLine("OSD saved preference: not set");
                    else
                    {
                        RegistryValueKind kind = key.GetValueKind("DisableOnScreenDisplay");
                        object val = key.GetValue("DisableOnScreenDisplay", null);
                        string state = kind == RegistryValueKind.DWord && val is int && ((int)val == 0 || (int)val == 1)
                            ? (((int)val == 1) ? "disabled" : "enabled") : "invalid value/type";
                        sb.AppendLine("OSD saved preference: " + state + " (" + kind + ")");
                    }
                    sb.AppendLine("Legacy application settings data: " +
                        (key != null && Array.IndexOf(key.GetValueNames(), "Settings") >= 0 ? "present (integrity not checked by report)" : "not found"));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Application registry inspection: failed (" + ex.GetType().Name + ")");
                DiagnosticsLog.Error("Application registry inspection failed", ex);
            }
            sb.AppendLine("OSD menu actual UI state: not inspected by this report");
            sb.AppendLine("OSD initialization and settings restore errors: see application.log");
            try
            {
                int count = 0;
                using (var q = new ManagementObjectSearcher("SELECT Name,ExecutablePath FROM Win32_Process WHERE Name='TPOSD.exe'"))
                using (var results = q.Get())
                    foreach (ManagementObject item in results)
                    {
                        count++;
                        string path = item["ExecutablePath"] as string;
                        string version = "unavailable";
                        if (!String.IsNullOrEmpty(path))
                        {
                            try { version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unavailable"; }
                            catch (Exception) { version = "inaccessible"; }
                        }
                        sb.AppendLine("TPOSD process: running; version=" + version + "; executable=" + SafeExecutableName(path));
                    }
                if (count == 0) sb.AppendLine("TPOSD process: not running");
            }
            catch (Exception ex)
            {
                sb.AppendLine("TPOSD process: inspection failed (" + ex.GetType().Name + ")");
                DiagnosticsLog.Error("TPOSD inspection failed", ex);
            }
            try
            {
                bool found = false;
                using (var q = new ManagementObjectSearcher("SELECT Name,State,StartMode,PathName FROM Win32_Service WHERE Name='TPHKLOAD'"))
                using (var results = q.Get())
                    foreach (ManagementObject item in results)
                    {
                        found = true;
                        sb.AppendLine("TPHKLOAD service: " + (item["State"] ?? "unknown") +
                            "; start mode=" + (item["StartMode"] ?? "unknown") +
                            "; executable=" + SafeExecutableName(item["PathName"] as string));
                    }
                if (!found) sb.AppendLine("TPHKLOAD service: not found");
            }
            catch (Exception ex)
            {
                sb.AppendLine("TPHKLOAD service: inspection failed (" + ex.GetType().Name + ")");
                DiagnosticsLog.Error("TPHKLOAD service inspection failed", ex);
            }
            sb.AppendLine("--- System firmware (read-only; serial number omitted) ---");
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Manufacturer,SMBIOSBIOSVersion FROM Win32_BIOS"))
                using (var results = q.Get())
                    foreach (ManagementObject item in results)
                    {
                        sb.AppendLine("BIOS manufacturer: " + (item["Manufacturer"] ?? "unknown"));
                        sb.AppendLine("BIOS version: " + (item["SMBIOSBIOSVersion"] ?? "unknown"));
                        break;
                    }
            }
            catch (Exception ex)
            {
                sb.AppendLine("BIOS information: unavailable (" + ex.GetType().Name + ")");
                DiagnosticsLog.Error("BIOS diagnostic failed", ex);
            }
        }

        private static string SafeExecutableName(string path)
        {
            if (String.IsNullOrEmpty(path)) return "unavailable";
            try { return Path.GetFileName(path.Trim().Trim('"')); }
            catch (Exception) { return "unavailable"; }
        }
    }
}
