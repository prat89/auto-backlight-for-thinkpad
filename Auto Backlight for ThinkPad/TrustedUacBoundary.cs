// Trusted installed-application elevation boundary.
// Production foundation: fail-closed canonical Program Files path and ACL audit.
// This stage does NOT authorize MSI, update staging, modification, or UI wiring.
// It offers an INERT UAC probe ONLY when the running EXE is itself trusted.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class TrustedUacBoundary
    {
        internal const string AuditSwitch = "--update-trusted-path-audit";
        internal const string ParentSwitch = "--update-trusted-uac-probe";
        internal const string ChildSwitch = "--update-trusted-uac-child";

        private const string AdminSid = "S-1-5-32-544";
        private const string SystemSid = "S-1-5-18";
        private const string CreatorOwnerSid = "S-1-3-0";
        private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        private const string AppDir = "Auto Backlight for ThinkPad";
        private const string ExecutableName = "Auto Backlight for ThinkPad.exe";

        private static bool Elevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                return id != null && new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string CurrentSid()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                if (id == null || id.User == null) throw new UnauthorizedAccessException("SID unavailable");
                return id.User.Value;
            }
        }

        private static bool TrustedSid(string sid)
        {
            return String.Equals(sid, AdminSid, StringComparison.Ordinal) ||
                   String.Equals(sid, SystemSid, StringComparison.Ordinal) ||
                   String.Equals(sid, TrustedInstallerSid, StringComparison.Ordinal);
        }

        private static void AuditObjectAcl(string path, bool directory)
        {
            FileSystemSecurity security = directory ?
                (FileSystemSecurity)Directory.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access) :
                File.GetAccessControl(path, AccessControlSections.Owner | AccessControlSections.Access);
            SecurityIdentifier owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !TrustedSid(owner.Value))
                throw new UnauthorizedAccessException("Installed file/folder owner is not trusted.");

            // Conservatively reject any explicit/inherited writable ALLOW for
            // principals other than SYSTEM, Administrators, TrustedInstaller;
            // CREATOR OWNER is accepted ONLY with the trusted owner above.
            FileSystemRights writable = FileSystemRights.WriteData | FileSystemRights.AppendData |
                FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes |
                FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (AuthorizationRule baseRule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                FileSystemAccessRule rule = baseRule as FileSystemAccessRule;
                if (rule == null || rule.AccessControlType != AccessControlType.Allow) continue;
                if ((rule.FileSystemRights & writable) == 0) continue;
                SecurityIdentifier principal = rule.IdentityReference as SecurityIdentifier;
                if (principal == null || (!TrustedSid(principal.Value) &&
                    !String.Equals(principal.Value, CreatorOwnerSid, StringComparison.Ordinal)))
                    throw new UnauthorizedAccessException("Writable ACL grant to untrusted principal.");
            }
        }

        private static void NoReparse(string path, bool directory)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Reparse point in executable path.");
            if (directory ? (attributes & FileAttributes.Directory) == 0 :
                (attributes & FileAttributes.Directory) != 0)
                throw new InvalidDataException("File/directory type mismatch.");
        }

        internal static string ExpectedInstalledExe()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new InvalidDataException("Native Program Files unavailable.");
            return Path.GetFullPath(Path.Combine(root, AppDir, ExecutableName));
        }

        // Internal method can be reflection-tested from a normal PowerShell;
        // only exact EXE path accepted, never a path passed across the UAC boundary.
        internal static string AuditPath(string candidate)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(candidate) || !Path.IsPathRooted(candidate))
                    return "REJECT_INVALID_PATH";
                string expected = ExpectedInstalledExe();
                if (!String.Equals(Path.GetFullPath(candidate), expected, StringComparison.OrdinalIgnoreCase))
                    return "REJECT_NOT_CANONICAL_INSTALLED_PATH";
                string appFolder = Path.GetDirectoryName(expected);
                string programFiles = Path.GetDirectoryName(appFolder);
                if (!File.Exists(expected) || !Directory.Exists(appFolder) ||
                    !Directory.Exists(programFiles)) return "REJECT_MISSING_FILE_OR_FOLDER";
                NoReparse(programFiles, true);
                NoReparse(appFolder, true);
                NoReparse(expected, false);
                AuditObjectAcl(programFiles, true);
                AuditObjectAcl(appFolder, true);
                AuditObjectAcl(expected, false);
                return "PASS";
            }
            catch (UnauthorizedAccessException) { return "REJECT_ACL_OR_REPARSE"; }
            catch (Exception) { return "REJECT_UNVERIFIABLE"; }
        }

        internal static string AuditSelf()
        {
            if (ReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled)
                return "REJECT_UNSAFE_LAB_BUILD";
            string self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string status = AuditPath(self);
            if (status != "PASS") return status;
            // Registered product must match the currently running EXE version;
            // a compiled developer EXE cannot impersonate an installed release.
            return InstalledMsiGuard.RunAudit(new string[] {
                InstalledMsiGuard.AuditSwitch }) == 0 ? "PASS" :
                "REJECT_INSTALLED_MSI_IDENTITY";
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 1) return 10;
            string mode = args[0];
            if (String.Equals(mode, AuditSwitch, StringComparison.Ordinal))
                return args.Length == 1 ? (AuditSelf() == "PASS" ? 0 : 26) : 10;
            bool parent = String.Equals(mode, ParentSwitch, StringComparison.Ordinal);
            bool child = String.Equals(mode, ChildSwitch, StringComparison.Ordinal);
            if (!parent && !child) return 10;
            if (args.Length != (parent ? 1 : 2)) return 10;
            if (child)
            {
                if (!Regex.IsMatch(args[1], @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 10;
                if (!Elevated()) return 13;
                if (!String.Equals(CurrentSid(), args[1], StringComparison.Ordinal)) return 33;
                return AuditSelf() == "PASS" ? 0 : 26;
            }
            if (Elevated()) return 21;
            if (AuditSelf() != "PASS") return 26; // no UAC from user-writable dev EXE
            try
            {
                string sid = CurrentSid();
                if (!Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$")) return 35;
                string installedExe = ExpectedInstalledExe();
                // No arbitrary executable or source path ever crosses UAC.
                var psi = new ProcessStartInfo {
                    FileName = installedExe,
                    Arguments = ChildSwitch + " " + sid,
                    WorkingDirectory = Path.GetDirectoryName(installedExe),
                    UseShellExecute = true,
                    Verb = "runas",
                    ErrorDialog = false
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return 24;
                    if (!p.WaitForExit(60000)) return 25; // do NOT kill or install
                    return p.ExitCode == 0 ? 0 : 27;
                }
            }
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? 23 : 24; }
            catch (Exception) { return 24; }
        }
    }
}
