// PHASE30B7D6M1_PRODUCTION_RELEASE_SAFETY_GATE_V1
// Release safety gate: normal builds MUST NOT execute laboratory MSI installs.
// A separate, deliberate compile-time symbol is required to re-enable LAB
// tests. Never define it in production or in the main project file.
using System;
using System.Reflection;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6MReleaseSafetyGate
    {
        internal const string Switch = "--update-release-audit";

        internal static bool UnsafeRealMsiLabBuildEnabled
        {
            get
            {
#if PHASE30B7D6M_ENABLE_UNSAFE_REAL_MSI_LAB
                return true; // LAB BUILDS ONLY, explicit compile-time opt-in.
#else
                return false; // all ordinary Debug/Release builds fail closed.
#endif
            }
        }

        // Status is intentionally a HOLD until actual trusted production UI,
        // installed-path elevation, and post-install result UX are implemented.
        // Version bump alone MUST NOT turn a lab-only updater into a release.
        internal static string AuditStatus()
        {
            if (UnsafeRealMsiLabBuildEnabled)
                return "BLOCK_UNSAFE_MSI_LAB_COMPILED";
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null || version.Major != 1 || version.Minor != 3 ||
                version.Build != 9 || version.Revision != 0)
                return "HOLD_ASSEMBLY_VERSION_NOT_1_3_9";
            return "HOLD_PRODUCTION_RC_PHYSICAL_UPGRADE_NOT_VALIDATED";
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 1 || args[0] != Switch)
                return 10;
            string status = AuditStatus();
            if (status == "BLOCK_UNSAFE_MSI_LAB_COMPILED") return 23;
            if (status == "HOLD_ASSEMBLY_VERSION_NOT_1_3_9") return 21;
            return 22; // Not release-ready; never a false PASS.
        }
    }
}
