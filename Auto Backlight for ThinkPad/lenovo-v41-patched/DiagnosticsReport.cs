using System;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class DiagnosticsReport
    {
        public static void Write(bool keyboardAvailable, bool screenAvailable)
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
