// PHASE30B7D6H_PROTECTED_TRANSACTION_HANDOFF_LAB_V1
// Integration lab. Elevated parent verifies the signed/ACL-locked package,
// starts an elevated SYSTEM POWERSHELL process which outlives the parent,
// and rechecks SHA256 after the parent exits. NO msiexec or MSI execution.
// NOT PRODUCTION authorization: development EXE resides in user-writable Git.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6HProtectedTransactionHandoffLab
    {
        internal const string Switch = "--update-detached-preflight-lab";
        internal const string TransactionRootName = "AutoBacklightUpdateTransactions";
        private const string MonitorTemplate = "$ErrorActionPreference='Stop'\n$folder=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__FOLDER__'))\n$msi=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MSI__'))\n$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MANIFEST__'))\n$signature=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__SIGNATURE__'))\n$expectedMsi='__MSI_SHA__'\n$expectedManifest='__MAN_SHA__'\n$expectedSignature='__SIG_SHA__'\n$launcherId=__PID__\n$launcherStartTicks=__TICKS__\n$success=$false\ntry {\n    $deadline=[DateTime]::UtcNow.AddSeconds(30)\n    $launcherGone=$false\n    while ([DateTime]::UtcNow -lt $deadline) {\n        $p=Get-Process -Id $launcherId -ErrorAction SilentlyContinue\n        if ($null -eq $p) { $launcherGone=$true; break }\n        try {\n            if ($p.StartTime.ToUniversalTime().Ticks -ne $launcherStartTicks) { $launcherGone=$true; break }\n        } catch { $launcherGone=$true; break }\n        Start-Sleep -Milliseconds 120\n    }\n    if (-not $launcherGone) { throw 'Launcher still exists after timeout.' }\n    $h1=(Get-FileHash -LiteralPath $msi -Algorithm SHA256 -ErrorAction Stop).Hash\n    $h2=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256 -ErrorAction Stop).Hash\n    $h3=(Get-FileHash -LiteralPath $signature -Algorithm SHA256 -ErrorAction Stop).Hash\n    if ($h1 -cne $expectedMsi -or $h2 -cne $expectedManifest -or $h3 -cne $expectedSignature) {\n        throw 'Protected package hashes changed after launcher exit.'\n    }\n    $body=\"LAUNCHER_GONE=TRUE`nPOST_EXIT_HASHES=PASS`nSIGNED_STAGE_UNCHANGED=TRUE`nMSI_EXECUTED=FALSE`nUAC_REQUESTED_BY_MONITOR=FALSE`n\"\n    $success=$true\n} catch {\n    $body=\"POST_EXIT_HASHES=FAIL`nMSI_EXECUTED=FALSE`nUAC_REQUESTED_BY_MONITOR=FALSE`n\"\n}\ntry {\n    $temp=Join-Path $folder 'result.tmp'\n    $result=Join-Path $folder 'result.txt'\n    if ([IO.File]::Exists($temp) -or [IO.File]::Exists($result)) { throw 'Report destination already exists.' }\n    [IO.File]::WriteAllText($temp,$body,(New-Object System.Text.UTF8Encoding($false)))\n    [IO.File]::Move($temp,$result)\n} catch { exit 1 }\nif (-not $success) { exit 1 }\n";
        private static readonly SecurityIdentifier AdminSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemSid =
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier UsersSid =
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        private static bool Elevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return identity != null &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        private static bool ValidId(string value)
        {
            Guid parsed;
            return !String.IsNullOrEmpty(value) && value.Length == 32 &&
                Guid.TryParseExact(value, "N", out parsed) && parsed != Guid.Empty &&
                String.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);
        }
        private static bool IsReparse(string p)
        {
            return (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0;
        }
        private static DirectorySecurity LockedReadableDir()
        {
            var result = new DirectorySecurity();
            result.SetAccessRuleProtection(true, false);
            result.SetOwner(AdminSid);
            InheritanceFlags flags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            result.AddAccessRule(new FileSystemAccessRule(AdminSid, FileSystemRights.FullControl,
                flags, PropagationFlags.None, AccessControlType.Allow));
            result.AddAccessRule(new FileSystemAccessRule(SystemSid, FileSystemRights.FullControl,
                flags, PropagationFlags.None, AccessControlType.Allow));
            result.AddAccessRule(new FileSystemAccessRule(UsersSid, FileSystemRights.ReadAndExecute,
                flags, PropagationFlags.None, AccessControlType.Allow));
            return result;
        }
        private static void VerifyDirectory(string path)
        {
            if (!Directory.Exists(path) || IsReparse(path))
                throw new InvalidDataException("Unsafe transaction folder.");
            DirectorySecurity sec = Directory.GetAccessControl(path,
                AccessControlSections.Owner | AccessControlSections.Access);
            SecurityIdentifier owner = (SecurityIdentifier)sec.GetOwner(typeof(SecurityIdentifier));
            if (!sec.AreAccessRulesProtected ||
                (!owner.Equals(AdminSid) && !owner.Equals(SystemSid)))
                throw new UnauthorizedAccessException("Unsafe transaction ACL or owner.");
            int seen = 0;
            foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                if (rule.IsInherited || rule.AccessControlType != AccessControlType.Allow)
                    throw new UnauthorizedAccessException("Unexpected transaction ACE.");
                bool elevated = sid.Equals(AdminSid) || sid.Equals(SystemSid);
                bool users = sid.Equals(UsersSid);
                FileSystemRights readonlyMask = FileSystemRights.ReadAndExecute |
                    FileSystemRights.Synchronize;
                if ((!elevated && !users) ||
                    (users && (rule.FileSystemRights & ~readonlyMask) != 0) ||
                    (elevated && (rule.FileSystemRights & FileSystemRights.FullControl) !=
                        FileSystemRights.FullControl))
                    throw new UnauthorizedAccessException("Unsafe transaction rights.");
                seen++;
            }
            if (seen != 3) throw new UnauthorizedAccessException("Incomplete transaction ACL.");
        }
        private static string ProgramData()
        {
            string dir = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData));
            if (!Directory.Exists(dir) || IsReparse(dir))
                throw new InvalidDataException("Invalid ProgramData path.");
            return dir;
        }
        // PHASE30B7D6I_REUSABLE_PROTECTED_TRANSACTION_FACTORY_V1
        // Generates an independent, one-time transaction id for each install attempt.
        internal static string CreateTransaction(string id)
        {
            string root = Path.Combine(ProgramData(), TransactionRootName);
            if (Directory.Exists(root)) VerifyDirectory(root);
            else Directory.CreateDirectory(root, LockedReadableDir());
            VerifyDirectory(root);
            string folder = Path.Combine(root, id);
            if (Directory.Exists(folder) || File.Exists(folder))
                throw new InvalidDataException("Transaction id already used.");
            Directory.CreateDirectory(folder, LockedReadableDir());
            VerifyDirectory(folder);
            return folder;
        }
        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
        }
        private static string Enc(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 2 || args[0] != Switch ||
                !ValidId(args[1])) return 10;
            if (!Elevated()) return 13;
            try
            {
                // Previously verified stage must contain EXACTLY 3 locked-down files.
                var plan = Phase30B7D6GProtectedInstallPreflight.BuildReadOnlyPlan(args[1]);
                string stage = Path.GetDirectoryName(plan.Package);
                string manifest = Path.Combine(stage, "update-manifest-v1.txt");
                string signature = Path.Combine(stage, "update-manifest-v1.sig");
                string msiSha = HashFile(plan.Package);
                string manifestSha = HashFile(manifest);
                string signatureSha = HashFile(signature);
                string report = CreateTransaction(args[1]);
                // The future install log is OUTSIDE the signed bundle.
                if (!String.Equals(plan.LogFile, Path.Combine(report, "msiexec.log"),
                    StringComparison.OrdinalIgnoreCase)) return 31;
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(ps) || IsReparse(ps)) return 32;
                using (var self = Process.GetCurrentProcess())
                {
                    string script = MonitorTemplate
                        .Replace("__FOLDER__", Enc(report))
                        .Replace("__MSI__", Enc(plan.Package))
                        .Replace("__MANIFEST__", Enc(manifest))
                        .Replace("__SIGNATURE__", Enc(signature))
                        .Replace("__MSI_SHA__", msiSha)
                        .Replace("__MAN_SHA__", manifestSha)
                        .Replace("__SIG_SHA__", signatureSha)
                        .Replace("__PID__", self.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Replace("__TICKS__", self.StartTime.ToUniversalTime().Ticks.ToString(
                            System.Globalization.CultureInfo.InvariantCulture));
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                    var psi = new ProcessStartInfo {
                        FileName = ps,
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                        UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = sys
                    };
                    using (Process monitor = Process.Start(psi))
                    {
                        if (monitor == null) return 33;
                    }
                }
                return 0; // background hash check ONLY; no MSI process exists
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Detached protected transaction lab refused: " + ex.GetType().Name);
                return 34;
            }
        }
    }
}
