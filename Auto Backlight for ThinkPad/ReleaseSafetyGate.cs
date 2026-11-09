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

        // Release builds include a production update flow and passed a direct
        // 1.3.8-to-1.3.9-RC manual MSI upgrade with settings and task preserved.
        // Keep HOLD until an About-driven upgrade to a newer signed public
        // GitHub release has passed end-to-end validation.
        internal static string AuditStatus()
        {
            if (UnsafeRealMsiLabBuildEnabled)
                return "BLOCK_UNSAFE_MSI_LAB_COMPILED";
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null || version.Major != 1 || version.Minor != 3 ||
                version.Build != 11 || version.Revision != 0)
                return "HOLD_ASSEMBLY_VERSION_NOT_1_3_11";
            return "HOLD_ABOUT_SIGNED_UPDATE_END_TO_END_NOT_VALIDATED";
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 1 || args[0] != Switch)
                return 10;
            string status = AuditStatus();
            if (status == "BLOCK_UNSAFE_MSI_LAB_COMPILED") return 23;
            if (status == "HOLD_ASSEMBLY_VERSION_NOT_1_3_11") return 21;
            return 22; // End-to-end update unverified; never report a false PASS.
        }
    }
}
