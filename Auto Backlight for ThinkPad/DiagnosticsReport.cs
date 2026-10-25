using System;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class DiagnosticsReport
    {
        public static string ReportPath => Path.Combine(Path.GetDirectoryName(DiagnosticsLog.FilePath), "hardware-report.txt");
        public static void Write(bool keyboardAvailable, bool screenAvailable, int? maximumKeyboardLevel)
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
                    sb.AppendLine("Keyboard backlight capabilities: Off (0), Low (1), Full (2)" +
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
                File.WriteAllText(Path.Combine(dir,"hardware-report.txt"), sb.ToString(), Encoding.UTF8);
                DiagnosticsLog.Info("Hardware report written to Logs/hardware-report.txt");
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Hardware report generation failed", ex);
            }
        }
    }
}
