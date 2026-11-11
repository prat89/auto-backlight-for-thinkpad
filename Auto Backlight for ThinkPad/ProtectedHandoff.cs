// Trusted elevated handoff for protected update staging.
// Production staging preparation ONLY. Never execute MSI, modify Task Scheduler,
// change user settings, or authorize installation from mutable user staging.
// Only the currently installed, ACL-audited EXE may cross the UAC boundary.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ProtectedHandoff
    {
        internal const string ParentSwitch = "--update-trusted-stage-parent";
        internal const string ChildSwitch = "--update-trusted-stage-child";
        internal static bool ProductionMsiInstallationEnabled { get { return false; } }

        private static bool Elevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return id != null && new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string CurrentSid()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                if (id == null || id.User == null) throw new UnauthorizedAccessException("No Windows SID");
                return id.User.Value;
            }
        }

        private static bool CanonicalId(string value)
        {
            Guid parsed;
            return value != null && value.Length == 32 &&
                Regex.IsMatch(value, "^[0-9a-f]{32}$", RegexOptions.CultureInvariant) &&
                Guid.TryParseExact(value, "N", out parsed) && parsed != Guid.Empty &&
                String.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);
        }

        private static string SourceDirectory(string id)
        {
            if (!CanonicalId(id)) throw new InvalidDataException("Invalid stage id");
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new InvalidDataException("LocalAppData unavailable");
            return Path.Combine(root, "Auto Backlight for ThinkPad", "UpdateStaging", id);
        }

        internal static string ExtractStageId(string msiPath)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(msiPath) || !Path.IsPathRooted(msiPath) ||
                    !String.Equals(Path.GetExtension(msiPath), ".msi", StringComparison.OrdinalIgnoreCase))
                    return null;
                string directory = Path.GetDirectoryName(Path.GetFullPath(msiPath));
                if (String.IsNullOrWhiteSpace(directory)) return null;
                string id = Path.GetFileName(directory);
                return CanonicalId(id) &&
                    String.Equals(directory, SourceDirectory(id), StringComparison.OrdinalIgnoreCase)
                    ? id : null;
            }
            catch (Exception) { return null; }
        }

        internal static bool CanOfferFromUi(string msiPath)
        {
            string id = ExtractStageId(msiPath);
            if (id == null) return false;
            // Neither a user-writable developer EXE nor an unsafe LAB build may elevate.
            if (TrustedUacBoundary.AuditSelf() != "PASS") return false;
            // The About dialog has already performed its explicit async review.
            // Repeat the full assessment in RunFromUi on a worker thread.
            return !ProductionMsiInstallationEnabled;
        }

        internal static int RunFromUi(string msiPath)
        {
            string id = ExtractStageId(msiPath);
            if (id == null || !CanOfferFromUi(msiPath) ||
                !LocalReview.ReviewReadOnly(msiPath)) return 26;
            return Run(new string[] { ParentSwitch, id });
        }

        private static int PrepareProtected(string id, string sid)
        {
            if (!Elevated()) return 13;
            if (!String.Equals(CurrentSid(), sid, StringComparison.Ordinal)) return 33;
            if (TrustedUacBoundary.AuditSelf() != "PASS") return 26;
            string source = SourceDirectory(id);
            // Recheck after UAC; the user-controlled source may have changed.
            if (SingleExeUpdateAssessment.Run(new string[] {
                    SingleExeUpdateAssessment.Switch, source }) != 0) return 34;
            int staged = ProtectedUpdateStaging.Run(new string[] {
                ProtectedUpdateStaging.Switch, source });
            if (staged != 0) return 35;
            // The only authoritative completion is a fresh verification of protected copies.
            ProtectedUpdateStaging.VerifyProtectedReadOnly(id);
            return 0; // Protected COPY succeeded. This is NOT MSI installation success.
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 2) return 10;
            bool parent = String.Equals(args[0], ParentSwitch, StringComparison.Ordinal);
            bool child = String.Equals(args[0], ChildSwitch, StringComparison.Ordinal);
            if (!parent && !child) return 10;
            if ((parent && args.Length != 2) || (child && args.Length != 3) ||
                !CanonicalId(args[1])) return 10;
            if (child && !Regex.IsMatch(args[2], @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 10;
            try
            {
                if (TrustedUacBoundary.AuditSelf() != "PASS") return 26;
                if (child) return PrepareProtected(args[1], args[2]);
                string sid = CurrentSid();
                if (!Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 33;
                string source = SourceDirectory(args[1]);
                // Parent checks before a UAC request; child checks it again independently.
                if (SingleExeUpdateAssessment.Run(new string[] {
                        SingleExeUpdateAssessment.Switch, source }) != 0) return 34;
                if (Elevated())
                {
                    // highestAvailable may already run elevated; UI click is still explicit.
                    return PrepareProtected(args[1], sid);
                }
                string installedExe = TrustedUacBoundary.ExpectedInstalledExe();
                var psi = new ProcessStartInfo {
                    FileName = installedExe,
                    Arguments = ChildSwitch + " " + args[1] + " " + sid,
                    WorkingDirectory = Path.GetDirectoryName(installedExe),
                    Verb = "runas", UseShellExecute = true, ErrorDialog = false
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return 24;
                    if (!p.WaitForExit(180000)) return 25;
                    return p.ExitCode;
                }
            }
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? 23 : 24; }
            catch (UnauthorizedAccessException) { return 33; }
            catch (Exception) { return 36; }
        }
    }
}
