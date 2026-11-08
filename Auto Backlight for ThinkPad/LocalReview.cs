// PHASE30B7D6M3B_LOCAL_SIGNED_REVIEW_V1
// Independent, explicit local read-only review of user-writable staging.
// A PASS is NEVER authorization to install: privileged code must recheck
// source, copy to a protected directory, and re-verify the signed bundle.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class LocalReview
    {
        internal static bool ProductionInstallEnabled { get { return false; } }

        internal static bool CanOfferReview(OnlineUpdater.StageResult result)
        {
            if (result == null || result.Status != OnlineUpdater.StageStatus.Staged ||
                String.IsNullOrWhiteSpace(result.Path) ||
                String.IsNullOrWhiteSpace(result.Sha256) || result.SizeBytes < 8 ||
                !Regex.IsMatch(result.Sha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
                return false;
            try { return StageDirectory(result.Path) != null; }
            catch (Exception) { return false; }
        }

        private static string StageDirectory(string msiPath)
        {
            if (String.IsNullOrWhiteSpace(msiPath) || !Path.IsPathRooted(msiPath) ||
                !String.Equals(Path.GetExtension(msiPath), ".msi", StringComparison.OrdinalIgnoreCase))
                return null;
            string root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auto Backlight for ThinkPad", "UpdateStaging"));
            string stage = Path.GetDirectoryName(Path.GetFullPath(msiPath));
            if (String.IsNullOrWhiteSpace(stage) ||
                !String.Equals(Path.GetDirectoryName(stage), root, StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Path.GetFileName(stage), "^[0-9a-f]{32}$", RegexOptions.CultureInvariant))
                return null;
            return stage;
        }

        // Validates publisher signature, MSI digest, version and native MSI identity.
        // Returns false for absent/altered/untrusted bundles. No elevated action.
        internal static bool ReviewReadOnly(string msiPath)
        {
            try
            {
                string stage = StageDirectory(msiPath);
                if (stage == null) return false;
                return SingleExeUpdateAssessment.Run(new string[] {
                    SingleExeUpdateAssessment.Switch, stage }) == 0;
            }
            catch (Exception) { return false; }
        }
    }
}
