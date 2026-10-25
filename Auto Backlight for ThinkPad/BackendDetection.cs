/*
 * Read-only service discovery for Lenovo keyboard backlight backends.
 * Presence of a Windows service does NOT prove hardware/protocol compatibility.
 */
using System;
using System.Collections.Generic;
using System.Management;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class BackendDetection
    {
        internal static string[] GetReportLines()
        {
            var result = new List<string>();
            bool ibmFound = false;
            bool energyFound = false;
            try
            {
                using (var query = new ManagementObjectSearcher(
                    "SELECT Name,State,StartMode FROM Win32_SystemDriver"))
                using (var drivers = query.Get())
                {
                    foreach (ManagementObject driver in drivers)
                    {
                        var name = Convert.ToString(driver["Name"]);
                        bool ibm = string.Equals(name, "IBMPMDRV", StringComparison.OrdinalIgnoreCase);
                        bool energy = string.Equals(name, "EnergyDrv", StringComparison.OrdinalIgnoreCase);
                        if (!ibm && !energy) continue;
                        if (ibm) ibmFound = true;
                        if (energy) energyFound = true;
                        result.Add("Backend service " + (ibm ? "IBMPmDrv" : "EnergyDrv") +
                            ": present; state=" + (Convert.ToString(driver["State"]) ?? "unknown") +
                            "; start mode=" + (Convert.ToString(driver["StartMode"]) ?? "unknown"));
                    }
                }
                if (!ibmFound) result.Add("Backend service IBMPmDrv: not found");
                if (!energyFound) result.Add("Backend service EnergyDrv: not found");
            }
            catch (Exception ex)
            {
                // WMI failure must never prevent keyboard initialization.
                result.Add("Backend service detection: unavailable (WMI query failed)");
                DiagnosticsLog.Error("Read-only backend service detection failed", ex);
            }
            result.Add("EnergyDrv protocol support: not tested (no hardware commands sent)");
            result.Add("Active backend policy: IBMPmDrv only (v5.1)");
            return result.ToArray();
        }
    }
}
