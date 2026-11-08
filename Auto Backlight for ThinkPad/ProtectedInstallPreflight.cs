// PHASE30B7D6G_PROTECTED_INSTALL_PREFLIGHT_ONLY_V1
// Safe read-only installation plan. No process creation, UAC or MSI execution.
using System;
using System.IO;
using System.Security.Principal;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ProtectedInstallPreflight
    {
        internal const string Switch = "--update-install-preflight";

        private static bool Elevated()
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return id != null &&
                    new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool ValidId(string value)
        {
            Guid id;
            return !String.IsNullOrEmpty(value) && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out id) && id != Guid.Empty &&
                String.Equals(id.ToString("N"), value, StringComparison.Ordinal);
        }

        // This is a specification, not executable process-launch code.
        internal sealed class FixedCommandPlan
        {
            internal string Msiexec;
            internal string Arguments;
            internal string LogFile;
            internal string Package;
        }

        internal static FixedCommandPlan BuildReadOnlyPlan(string id)
        {
            if (!ValidId(id)) throw new InvalidDataException("Invalid bundle id.");
            if (!Elevated()) throw new UnauthorizedAccessException("Administrator required.");
            string msi = ProtectedUpdateStaging.VerifyProtectedReadOnly(id);
            string systemDir = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System));
            string exe = Path.Combine(systemDir, "msiexec.exe");
            if (!File.Exists(exe) || (File.GetAttributes(exe) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Windows Installer executable missing.");
            // PHASE30B7D6H_TRANSACTION_LOG_SEPARATION_V1
            // Signed bundle stays exactly three files. All future logs live in
            // a separate protected transaction directory (created by executor).
            string programData = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData));
            string log = Path.Combine(programData, "AutoBacklightUpdateTransactions", id, "msiexec.log");
            if (File.Exists(log) || Directory.Exists(log))
                throw new InvalidDataException("Preflight log destination already occupied.");
            // All values are derived from system directories and a checked GUID.
            // Future executor must repeat verification and require explicit consent.
            foreach (string item in new string[] { exe, msi, log })
                if (item.IndexOfAny(new char[] { '\"', '\r', '\n' }) >= 0)
                    throw new InvalidDataException("Unsafe command path.");
            return new FixedCommandPlan {
                Msiexec = exe,
                Package = msi,
                LogFile = log,
                Arguments = "/i \"" + msi + "\" /qn /norestart /L*v \"" + log + "\""
            };
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 2 || args[0] != Switch ||
                !ValidId(args[1])) return 10;
            if (!Elevated()) return 13;
            try
            {
                FixedCommandPlan plan = BuildReadOnlyPlan(args[1]);
                if (plan == null || String.IsNullOrWhiteSpace(plan.Package) ||
                    String.IsNullOrWhiteSpace(plan.Msiexec)) return 14;
                return 0; // plan assessed, NOT executed
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Install preflight refused: " + ex.GetType().Name);
                return 14;
            }
        }
    }
}
