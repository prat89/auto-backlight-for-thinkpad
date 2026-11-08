// PHASE30B7D6C_STAGE_UAC_LAB_ONLY_V1
// Two processes of ONE EXE: parent asks for UAC, elevated child copies a
// signed test bundle to protected staging.  NO msiexec, MSI API writes,
// registry/task changes, UI integration, or application shutdown.
// NOT PRODUCTION: the developer bin\Release EXE is user-writable. A release
// must instead invoke and validate a trusted EXE from Program Files.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class StageUacHandoff
    {
        internal const string ParentSwitch = "--update-stage-uac-lab";
        internal const string ChildSwitch = "--update-elevated-stage-lab";

        private static string CurrentSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null || identity.User == null)
                    throw new UnauthorizedAccessException("Windows SID unavailable.");
                return identity.User.Value;
            }
        }

        private static bool Elevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity != null &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool ValidId(string value)
        {
            Guid parsed;
            return !String.IsNullOrEmpty(value) && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out parsed) &&
                !String.Equals(parsed.ToString("N"), Guid.Empty.ToString("N"), StringComparison.Ordinal) &&
                String.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);
        }

        private static string SourcePath(string id)
        {
            if (!ValidId(id)) throw new InvalidDataException("Invalid staging identifier.");
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new InvalidDataException("Missing user LocalAppData.");
            return Path.Combine(root, "Auto Backlight for ThinkPad", "UpdateStaging", id);
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length < 2 || args.Length > 3) return 10;
            bool parent = String.Equals(args[0], ParentSwitch, StringComparison.Ordinal);
            bool child = String.Equals(args[0], ChildSwitch, StringComparison.Ordinal);
            if (!parent && !child) return 10;
            if ((parent && args.Length != 2) || (child && args.Length != 3)) return 10;
            if (!ValidId(args[1])) return 10;

            try
            {
                if (child)
                {
                    if (!Elevated()) return 13;
                    // UAC credentials from another account must not silently
                    // read a different LocalAppData or install as someone else.
                    if (!String.Equals(CurrentSid(), args[2], StringComparison.Ordinal)) return 33;
                    return ProtectedUpdateStaging.Run(new string[] {
                        ProtectedUpdateStaging.Switch,
                        SourcePath(args[1])
                    });
                }

                if (Elevated()) return 21;
                string stage = SourcePath(args[1]);
                if (SingleExeUpdateAssessment.Run(new string[] {
                    SingleExeUpdateAssessment.Switch, stage
                }) != 0) return 34;
                string ownExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                if (!File.Exists(ownExe) ||
                    (File.GetAttributes(ownExe) & FileAttributes.ReparsePoint) != 0)
                    return 22;

                // All arguments have strict grammars: id [0-9a-f]{32}, Windows SID.
                // No arbitrary user-controlled source paths cross the UAC boundary.
                string sid = CurrentSid();
                if (!System.Text.RegularExpressions.Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$"))
                    return 35;
                var psi = new ProcessStartInfo {
                    FileName = ownExe,
                    Arguments = ChildSwitch + " " + args[1] + " " + sid,
                    WorkingDirectory = Path.GetDirectoryName(ownExe),
                    Verb = "runas",
                    UseShellExecute = true,
                    ErrorDialog = false
                };
                using (Process elevatedChild = Process.Start(psi))
                {
                    if (elevatedChild == null) return 24;
                    if (!elevatedChild.WaitForExit(120000)) return 25;
                    return elevatedChild.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                return ex.NativeErrorCode == 1223 ? 23 : 24;
            }
            catch (Exception)
            {
                return 36;
            }
        }
    }
}
