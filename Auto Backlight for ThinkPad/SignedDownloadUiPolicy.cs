// Signed update download policy for the UI.
// Explicit user-triggered signed download only. NEVER UAC, stage elevation,
// Worker handoff or MSI execution is controlled by a separate install authorization gate.
using System;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class SignedDownloadUiPolicy
    {
        internal static bool ProductionMsiInstallationEnabled { get { return false; } }

        internal static bool CanOfferSignedDownload(OnlineUpdater.UpdateCheckResult result)
        {
            if (result == null || result.Status != OnlineUpdater.CheckStatus.UpdateAvailable ||
                result.LatestVersion == null || result.Update == null ||
                result.Update.Version == null || result.Update.Url == null ||
                result.Update.Sha256Digest == null) return false;
            return result.Update.Version.Equals(result.LatestVersion) &&
                Regex.IsMatch(result.Update.Sha256Digest, "^[0-9a-fA-F]{64}$",
                    RegexOptions.CultureInvariant);
            // StageSignedMsiAsync performs the authoritative publisher signature,
            // URL, MSI hash and asset integrity checks AFTER explicit click.
        }
    }
}
