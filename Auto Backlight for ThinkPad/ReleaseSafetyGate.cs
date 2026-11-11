// Release safety gate for unsafe lab MSI execution and version readiness.
// Release safety gate: normal builds MUST NOT execute laboratory MSI installs.
// A separate, deliberate compile-time symbol is required to re-enable LAB
// tests. Never define it in production or in the main project file.
using System;
using System.Reflection;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ReleaseSafetyGate
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

        // The signed MSI updater and automatic relaunch were validated on
        // v1.3.13 -> v1.3.14. The new v1.4.0 settings-recovery and opt-in
        // auto-install behavior require their own runtime acceptance tests.
        // HOLD is intentionally retained until those tests have passed.
        internal static string AuditStatus()
        {
            if (UnsafeRealMsiLabBuildEnabled)
                return "BLOCK_UNSAFE_MSI_LAB_COMPILED";
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null || version.Major != 1 || version.Minor != 4 ||
                version.Build != 0 || version.Revision != 0)
                return "HOLD_ASSEMBLY_VERSION_NOT_1_4_0";
            return "HOLD_V140_RUNTIME_VALIDATION_PENDING";
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 1 || args[0] != Switch)
                return 10;
            string status = AuditStatus();
            if (status == "BLOCK_UNSAFE_MSI_LAB_COMPILED") return 23;
            if (status == "HOLD_ASSEMBLY_VERSION_NOT_1_4_0") return 21;
            return 22; // v1.4.0 runtime acceptance pending; never report a false PASS.
        }
    }
}
