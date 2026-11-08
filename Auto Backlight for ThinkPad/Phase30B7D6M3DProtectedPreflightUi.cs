// PHASE30B7D6M3D_PROTECTED_PREFLIGHT_UI_V1
// Explicit, READ-ONLY protected MSI preflight, NOT installation authorization.
// Parent and elevated child must be the ACL-audited installed EXE. No user-controlled
// executable/path crosses the UAC boundary. No MSI execution or changes to files.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6M3DProtectedPreflightUi
    {
        internal const string ParentSwitch = "--update-trusted-preflight-parent";
        internal const string ChildSwitch = "--update-trusted-preflight-child";
        internal static bool InstallationEnabled { get { return false; } }

        private static bool Elevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return id != null && new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string Sid()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                if (id == null || id.User == null) throw new UnauthorizedAccessException("No SID");
                return id.User.Value;
            }
        }

        private static bool ValidId(string value)
        {
            Guid parsed;
            return value != null && value.Length == 32 &&
                Regex.IsMatch(value, "^[0-9a-f]{32}$", RegexOptions.CultureInvariant) &&
                Guid.TryParseExact(value, "N", out parsed) && parsed != Guid.Empty &&
                String.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);
        }

        internal static bool CanOfferFromUi(string stagedMsi)
        {
            if (Phase30B7D6M3CProtectedHandoff.ExtractStageId(stagedMsi) == null)
                return false;
            if (InstallationEnabled) return false;
            return Phase30B7D6M2TrustedUacBoundary.AuditSelf() == "PASS";
        }

        internal static int RunFromUi(string stagedMsi)
        {
            if (!CanOfferFromUi(stagedMsi)) return 26;
            string id = Phase30B7D6M3CProtectedHandoff.ExtractStageId(stagedMsi);
            if (!ValidId(id)) return 10;
            return Run(new string[] { ParentSwitch, id });
        }

        private static int ReadOnlyPreflight(string id, string requestedSid)
        {
            if (!Elevated()) return 13;
            if (!String.Equals(Sid(), requestedSid, StringComparison.Ordinal)) return 33;
            if (Phase30B7D6M2TrustedUacBoundary.AuditSelf() != "PASS") return 26;

            // Revalidate ACL, publisher RSA signature, MSI digest, native MSI identity,
            // AND registered installed-product identity from protected ProgramData.
            string protectedMsi = Phase30B7D5ProtectedUpdateStaging.VerifyProtectedReadOnly(id);
            var plan = Phase30B7D6GProtectedInstallPreflight.BuildReadOnlyPlan(id);
            if (plan == null || String.IsNullOrWhiteSpace(plan.Package) ||
                !String.Equals(plan.Package, protectedMsi, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFileName(plan.Msiexec), "msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                String.IsNullOrWhiteSpace(plan.LogFile) ||
                Path.GetDirectoryName(plan.LogFile) == Path.GetDirectoryName(protectedMsi))
                return 35;
            return 0; // READ-ONLY CHECK. No process, MSI, log or transaction was created.
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 2) return 10;
            bool parent = String.Equals(args[0], ParentSwitch, StringComparison.Ordinal);
            bool child = String.Equals(args[0], ChildSwitch, StringComparison.Ordinal);
            if (!parent && !child || args.Length != (parent ? 2 : 3) || !ValidId(args[1]))
                return 10;
            if (child && !Regex.IsMatch(args[2], @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 10;
            try
            {
                if (Phase30B7D6M2TrustedUacBoundary.AuditSelf() != "PASS") return 26;
                if (child) return ReadOnlyPreflight(args[1], args[2]);
                string sid = Sid();
                if (!Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 33;
                if (Elevated()) return ReadOnlyPreflight(args[1], sid);
                string exe = Phase30B7D6M2TrustedUacBoundary.ExpectedInstalledExe();
                var psi = new ProcessStartInfo {
                    FileName = exe, Arguments = ChildSwitch + " " + args[1] + " " + sid,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    Verb = "runas", UseShellExecute = true, ErrorDialog = false
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return 24;
                    if (!p.WaitForExit(120000)) return 25;
                    return p.ExitCode;
                }
            }
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? 23 : 24; }
            catch (UnauthorizedAccessException) { return 33; }
            catch (Exception) { return 36; }
        }
    }
}
