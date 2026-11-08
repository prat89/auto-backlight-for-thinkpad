// PHASE30B7D6M3E_INSTALL_CONSENT_GATE_V1
// The UI and CLI are intentionally FAIL-CLOSED. This is a typed future
// production-install boundary, NOT a functional MSI executor. No COM, UAC,
// process launches, filesystem writes or scheduled-task changes here.
// DO NOT flip this to true until a separately validated production worker
// replaces the 1.3.8-specific LAB worker and implements durable reporting.
using System;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6M3EInstallConsentGate
    {
        internal const string ParentSwitch = "--update-production-install-parent";
        internal const string ChildSwitch = "--update-production-install-child";
        internal const int RefusedProductionNotWired = 42;

        // PHASE30B7D6M3E_HARD_STOP_V1: two independent runtime refusal paths.
        internal static bool ProductionExecutorEnabled { get { return !Phase30B7D6MReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled; } }

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
            // This policy NEVER enables Install on an installed or dev EXE in 6M.3E.
            if (!ProductionExecutorEnabled) return false;
            return Phase30BProductionInstaller.CanOfferFromUi(stagedMsi);
        }

        internal static int RunFromUi(string stagedMsi)
        {
            // Double-check the disabled gate regardless of button Visibility.
            if (!ProductionExecutorEnabled) return RefusedProductionNotWired;
            if (!CanOfferFromUi(stagedMsi)) return 26;
            return Phase30BProductionInstaller.LaunchFromUi(stagedMsi);
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
            return RefusedProductionNotWired; // Worker not implemented; fail closed.
        }
    }
}
