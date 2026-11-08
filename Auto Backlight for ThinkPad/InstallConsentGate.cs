// Production install consent policy. A normal Release build delegates to
// ProductionUpdateInstaller only after explicit UI consent and trusted
// package checks. Direct CLI calls are refused. Unsafe lab builds stay blocked.
using System;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class InstallConsentGate
    {
        internal const string ParentSwitch = "--update-production-install-parent";
        internal const string ChildSwitch = "--update-production-install-child";
        internal const int RefusedProductionNotWired = 42;

        // Unsafe MSI lab builds cannot enable the production installer.
        internal static bool ProductionExecutorEnabled { get { return !ReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled; } }

        private static bool CanonicalId(string id)
        {
            Guid value;
            return id != null && id.Length == 32 &&
                Regex.IsMatch(id, "^[0-9a-f]{32}$", RegexOptions.CultureInvariant) &&
                Guid.TryParseExact(id, "N", out value) && value != Guid.Empty &&
                String.Equals(value.ToString("N"), id, StringComparison.Ordinal);
        }

        internal static bool CanOfferFromUi(string stagedMsi)
        {
            // The production installer independently checks the installed EXE and MSI.
            if (!ProductionExecutorEnabled) return false;
            return ProductionUpdateInstaller.CanOfferFromUi(stagedMsi);
        }

        internal static int RunFromUi(string stagedMsi)
        {
            // Recheck the safety gate irrespective of button visibility.
            if (!ProductionExecutorEnabled) return RefusedProductionNotWired;
            if (!CanOfferFromUi(stagedMsi)) return 26;
            return ProductionUpdateInstaller.LaunchFromUi(stagedMsi);
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 2) return 10;
            bool parent = String.Equals(args[0], ParentSwitch, StringComparison.Ordinal);
            bool child = String.Equals(args[0], ChildSwitch, StringComparison.Ordinal);
            if (!parent && !child || args.Length != (parent ? 2 : 3) || !CanonicalId(args[1]))
                return 10;
            if (child && !Regex.IsMatch(args[2], @"^S-1-[0-9]+(?:-[0-9]+)+$"))
                return 10;
            // Direct CLI invocation cannot bypass the UI or turn into a LAB install.
            if (!ProductionExecutorEnabled) return RefusedProductionNotWired;
            return RefusedProductionNotWired; // Standalone CLI install is not authorized.
        }
    }
}
