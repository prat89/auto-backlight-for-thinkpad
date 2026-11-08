// PHASE30B7D6B_UAC_PROBE_ONLY_V1
// A deliberately inert experiment that proves a second process of the SAME
// EXE can start with UAC and report success without starting WPF or an MSI.
// IMPORTANT: this is NOT the production updater; no file, registry or task edits.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class UacProbe
    {
        internal const string ParentSwitch = "--update-uac-probe";
        internal const string ChildSwitch = "--update-elevated-probe";

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity != null &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        internal static int Run(string[] arguments)
        {
            if (arguments == null || arguments.Length != 1)
                return 10;

            // The elevated child has ONLY this inert capability. No path arguments,
            // no stage copying, no MSI execution, no commands from an untrusted user.
            if (String.Equals(arguments[0], ChildSwitch, StringComparison.Ordinal))
                return IsElevated() ? 0 : 13;
            if (!String.Equals(arguments[0], ParentSwitch, StringComparison.Ordinal))
                return 10;
            // Reject running the parent probe elevated: the experiment must show
            // the transition from a normal token to an administrator token.
            if (IsElevated()) return 21;

            try
            {
                string ownExe = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                if (!File.Exists(ownExe) ||
                    (File.GetAttributes(ownExe) & FileAttributes.ReparsePoint) != 0)
                    return 22;
                var start = new ProcessStartInfo
                {
                    FileName = ownExe,
                    Arguments = ChildSwitch,
                    WorkingDirectory = Path.GetDirectoryName(ownExe),
                    Verb = "runas",                 // Normal, visible Windows UAC prompt
                    UseShellExecute = true,
                    ErrorDialog = false,
                };
                using (Process child = Process.Start(start))
                {
                    if (child == null) return 24;
                    // Do not kill the child on timeout: the inert child should
                    // immediately exit. No installer is ever running here.
                    if (!child.WaitForExit(60000)) return 25;
                    return child.ExitCode == 0 ? 0 : 26;
                }
            }
            catch (Win32Exception ex)
            {
                // 1223 = ERROR_CANCELLED (the UAC prompt was declined).
                return ex.NativeErrorCode == 1223 ? 23 : 24;
            }
            catch (Exception)
            {
                return 24;
            }
        }
    }
}
