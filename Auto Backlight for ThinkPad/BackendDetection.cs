/*
 * Read-only service discovery for Lenovo keyboard backlight backends.
 * Presence of a Windows service does NOT prove hardware/protocol compatibility.
 */
using System;
using System.Collections.Generic;
using System.ServiceProcess;
using Microsoft.Win32;

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
                // Read-only Windows Service Control Manager enumeration. It does not
                // open Lenovo devices, issue IOCTLs, change service state or use WMI.
                // GetDevices(), not GetServices(), includes kernel driver services.
                var drivers = ServiceController.GetDevices();
                foreach (ServiceController driver in drivers)
                {
                    using (driver)
                    {
                        string name = driver.ServiceName;
                        bool ibm = string.Equals(name, "IBMPMDRV", StringComparison.OrdinalIgnoreCase);
                        bool energy = string.Equals(name, "EnergyDrv", StringComparison.OrdinalIgnoreCase);
                        if (!ibm && !energy) continue;
                        if (ibm) ibmFound = true;
                        if (energy) energyFound = true;
                        string state;
                        try { state = driver.Status.ToString(); }
                        catch (Exception ex)
                        {
                            DiagnosticsLog.Error("Backend service status unavailable", ex);
                            state = "unknown";
                        }
                        result.Add("Backend service " + (ibm ? "IBMPmDrv" : "EnergyDrv") +
                            ": present; state=" + state + "; start mode=" + GetStartMode(name));
                    }
                }
                if (!ibmFound) result.Add("Backend service IBMPmDrv: not found");
                if (!energyFound) result.Add("Backend service EnergyDrv: not found");
            }
            catch (Exception ex)
            {
                // Diagnostic discovery is never a prerequisite for IBMPmDrv hardware control.
                result.Add("Backend service detection: unavailable (SCM enumeration failed)");
                DiagnosticsLog.Error("Read-only SCM backend service detection failed", ex);
            }
            result.Add("EnergyDrv protocol support: not tested (no hardware commands sent)");
            result.Add("Active backend policy: IBMPmDrv only (v5.1)");
            return result.ToArray();
        }

        private static string GetStartMode(string serviceName)
        {
            // Read-only registry lookup for the same StartMode field formerly shown
            // by WMI. Failure must not interfere with startup.
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    if (key == null) return "unknown";
                    object value = key.GetValue("Start");
                    if (!(value is int)) return "unknown";
                    switch ((int)value)
                    {
                        case 0: return "Boot";
                        case 1: return "System";
                        case 2: return "Auto";
                        case 3: return "Manual";
                        case 4: return "Disabled";
                        default: return "unknown";
                    }
                }
            }
            catch (Exception) { return "unknown"; }
        }
    }
}
