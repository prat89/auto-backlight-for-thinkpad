// PHASE30B7D6I_PROTECTED_MSI_EXECUTOR_LAB_V1
// Experimental, EXPLICITLY REQUESTED MSI installation path; not wired to UI.
// The privileged app EXITS before Windows Installer begins removing old EXE.
// Independent Windows PowerShell (system binary) persists the result.
// Do not expose ExecuteSwitch as an automatic update without an installed,
// trusted EXE identity gate and UX consent. This is a laboratory feature.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6IProtectedMsiExecutorLab
    {
        internal const string DrySwitch = "--update-install-transaction-dryrun";
        internal const string ExecuteSwitch = "--update-install-transaction-lab";
        internal const string ExplicitConsent = "I_ACCEPT_REAL_MSI_INSTALLATION_LAB_20261107";
        private const string PsTemplate = "$ErrorActionPreference='Stop'\n$ProgressPreference='SilentlyContinue'\n$txn=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__TXN_DIR__'))\n$msi=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MSI__'))\n$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MANIFEST__'))\n$signature=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__SIGNATURE__'))\n$msiexec=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MSIEXEC__'))\n$log=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__LOG__'))\n$expectMsi='__MSI_SHA__'; $expectManifest='__MAN_SHA__'; $expectSignature='__SIG_SHA__'\n$parentId=__PARENT_PID__; $parentTicks=__PARENT_TICKS__\n$transactionId='__TXN_ID__'; $stageId='__STAGE_ID__'\n$started=$false; $exitCode=-1; $hashes=$false; $parentGone=$false\n$claimOwned=$false\n$appFound=0; $appStopped=0; $appStopComplete=$false\n$appStopMode='NONE'; $appInstalledHashVerified=$false\n$status='FAILED_BEFORE_MSI'; $reboot=$false; $errorType='NONE'\ntry {\n    # Exclusive one-shot claim is written BEFORE any other action. Never retry\n    # an interrupted attempt automatically: installation state is then unknown.\n    $claim=Join-Path $txn 'worker.claim'\n    $file=[IO.File]::Open($claim,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)\n    $claimOwned=$true\n    try { $data=[Text.Encoding]::ASCII.GetBytes('CLAIMED'); $file.Write($data,0,$data.Length); $file.Flush() }\n    finally { $file.Dispose() }\n    $deadline=[DateTime]::UtcNow.AddSeconds(45)\n    while ([DateTime]::UtcNow -lt $deadline) {\n        $p=Get-Process -Id $parentId -ErrorAction SilentlyContinue\n        if ($null -eq $p) { $parentGone=$true; break }\n        try { if ($p.StartTime.ToUniversalTime().Ticks -ne $parentTicks) { $parentGone=$true; break } }\n        catch { $parentGone=$true; break }\n        Start-Sleep -Milliseconds 150\n    }\n    if (-not $parentGone) { throw 'Launcher did not exit before deadline.' }\n    if (-not (Test-Path -LiteralPath $msiexec -PathType Leaf)) { throw 'System msiexec not found.' }\n    if ((Get-Item -LiteralPath $msiexec).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'System msiexec is a reparse point.' }\n    $h1=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash\n    $h2=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash\n    $h3=(Get-FileHash -LiteralPath $signature -Algorithm SHA256).Hash\n    if ($h1 -cne $expectMsi -or $h2 -cne $expectManifest -or $h3 -cne $expectSignature) { throw 'Protected stage hash mismatch.' }\n    $hashes=$true\n    # PHASE30B7D6L_PRE_MSI_LEGACY_138_PROCESS_SHUTDOWN_V1\n    # The installed v1.3.8 does NOT implement a remote graceful-exit endpoint.\n    # Only the official v1.3.8 binary is eligible for a bounded, audited fallback.\n    # Stop by verified PID+owner+session+canonical path; NEVER by a wildcard kill.\n    $expectedExe=[IO.Path]::GetFullPath([Text.Encoding]::UTF8.GetString(\n      [Convert]::FromBase64String('__OLD_EXE__')))\n    $expectedInstalledHash='45510B15D235E5FD352D5BBF38922FD3C83E7E24C51CC0877273829A3406ACF8'\n    if (-not [IO.File]::Exists($expectedExe)) { throw 'Official 1.3.8 executable missing before shutdown.' }\n    if (([IO.File]::GetAttributes($expectedExe) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {\n        throw 'Installed EXE is a reparse point.'\n    }\n    if ((Get-FileHash -LiteralPath $expectedExe -Algorithm SHA256).Hash -cne $expectedInstalledHash) {\n        throw 'Installed EXE hash is not the pinned official 1.3.8 binary.'\n    }\n    $appInstalledHashVerified=$true\n    $actorSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value\n    $actorSession=[Diagnostics.Process]::GetCurrentProcess().SessionId\n    $candidates=@(Get-Process -Name 'Auto Backlight for ThinkPad' -ErrorAction SilentlyContinue)\n    $appFound=$candidates.Count\n    if ($appFound -gt 1) { throw 'Ambiguous multiple same-name app processes; refusing installation.' }\n    foreach ($candidate in $candidates) {\n        $candidatePid=[int]$candidate.Id\n        $wmi=Get-CimInstance -ClassName Win32_Process -Filter ('ProcessId='+$candidatePid) -ErrorAction Stop\n        if ($null -eq $wmi) { throw 'Candidate disappeared during identity validation.' }\n        $owner=Invoke-CimMethod -InputObject $wmi -MethodName GetOwnerSid -ErrorAction Stop\n        if ([int]$owner.ReturnValue -ne 0 -or [string]$owner.Sid -cne $actorSid) {\n            throw 'App process belongs to another user.'\n        }\n        if ([int]$candidate.SessionId -ne $actorSession) {\n            throw 'App process belongs to another Windows session.'\n        }\n        $actualExe=[IO.Path]::GetFullPath([string]$wmi.ExecutablePath)\n        if (-not [string]::Equals($actualExe,$expectedExe,[StringComparison]::OrdinalIgnoreCase)) {\n            throw 'Unexpected same-name executable path.'\n        }\n        $current=Get-Process -Id $candidatePid -ErrorAction Stop\n        if ($current.StartTime.ToUniversalTime().Ticks -ne $candidate.StartTime.ToUniversalTime().Ticks) {\n            throw 'Process PID recycled during validation.'\n        }\n        # CloseMainWindow is best-effort. Tray v1.3.8 has WPF OnExplicitShutdown\n        # and may not react to WM_CLOSE. Fallback is permitted only for pinned\n        # v1.3.8, after explicit interactive approval in the outer 6K/6L runner.\n        [void]$current.CloseMainWindow()\n        if ($current.WaitForExit(5000)) {\n            $appStopMode='CLOSE_MAIN_WINDOW'\n        } else {\n            $again=Get-Process -Id $candidatePid -ErrorAction Stop\n            if ($again.StartTime.ToUniversalTime().Ticks -ne $candidate.StartTime.ToUniversalTime().Ticks) {\n                throw 'Process PID changed before legacy fallback.'\n            }\n            $again.Kill()\n            if (-not $again.WaitForExit(10000)) { throw 'Official 1.3.8 process did not stop.' }\n            $appStopMode='PINNED_138_LEGACY_TERMINATE'\n        }\n        $appStopped++\n    }\n    if (@(Get-Process -Name 'Auto Backlight for ThinkPad' -ErrorAction SilentlyContinue).Count -ne 0) {\n        throw 'Old application still running; MSI will not start.'\n    }\n    $appStopComplete=$true\n    # PHASE30B7D6L_END_PRE_MSI_SHUTDOWN\n    $psi=New-Object System.Diagnostics.ProcessStartInfo\n    $psi.FileName=$msiexec\n    $psi.Arguments='/i \"'+$msi+'\" /qn /norestart /L*v \"'+$log+'\"'\n    $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true\n    $psi.WorkingDirectory=[IO.Path]::GetDirectoryName($msiexec)\n    $process=[Diagnostics.Process]::Start($psi)\n    if ($null -eq $process) { throw 'Could not start Windows Installer.' }\n    $started=$true\n    try { $process.WaitForExit(); $exitCode=[int]$process.ExitCode }\n    finally { $process.Dispose() }\n    switch ($exitCode) {\n        0 { $status='MSI_RETURNED_SUCCESS' }\n        3010 { $status='MSI_RETURNED_REBOOT_REQUIRED'; $reboot=$true }\n        1602 { $status='MSI_CANCELLED' }\n        1603 { $status='MSI_FAILED' }\n        1618 { $status='ANOTHER_INSTALL_ACTIVE' }\n        default { $status='MSI_OTHER_EXIT_CODE' }\n    }\n} catch {\n    if (-not $claimOwned) { exit 42 } # A replay must NOT overwrite the active worker's report.\n    $errorType=$_.Exception.GetType().Name\n    if ($started) { $status='MSI_OUTCOME_UNCONFIRMED' }\n    else { $status='FAILED_BEFORE_MSI' }\n}\n# Atomic report, independent of the app EXE, never used to authorize retry.\ntry {\n    $body='TRANSACTION_ID='+$transactionId+\"`n\"+\n      'STAGE_ID='+$stageId+\"`n\"+\n      'PARENT_EXITED='+[string]$parentGone+\"`n\"+\n      'POST_EXIT_HASHES_VERIFIED='+[string]$hashes+\"`n\"+\n      'APP_INSTALLED_HASH_VERIFIED='+[string]$appInstalledHashVerified+\"`n\"+\n      'APP_PROCESSES_FOUND='+[string]$appFound+\"`n\"+\n      'APP_PROCESSES_STOPPED='+[string]$appStopped+\"`n\"+\n      'APP_STOP_MODE='+$appStopMode+\"`n\"+\n      'APP_STOP_COMPLETE='+[string]$appStopComplete+\"`n\"+\n      'MSI_PROCESS_STARTED='+[string]$started+\"`n\"+\n      'MSI_EXIT_CODE='+[string]$exitCode+\"`n\"+\n      'STATUS='+$status+\"`n\"+\n      'REBOOT_REQUIRED='+[string]$reboot+\"`n\"+\n      'INSTALLER_LOG_EXISTS='+[string](Test-Path -LiteralPath $log -PathType Leaf)+\"`n\"+\n      'ERROR_TYPE='+$errorType+\"`n\"+\n      \"REGISTRATION_AND_TASK_AUDIT_STILL_REQUIRED=TRUE`n\"\n    $temp=Join-Path $txn 'result.tmp'; $result=Join-Path $txn 'result.txt'\n    if ([IO.File]::Exists($result) -or [IO.File]::Exists($temp)) { throw 'Result already exists.' }\n    [IO.File]::WriteAllText($temp,$body,(New-Object Text.UTF8Encoding($false)))\n    [IO.File]::Move($temp,$result)\n} catch { exit 41 }\nif ($status -eq 'FAILED_BEFORE_MSI' -or $status -eq 'MSI_OUTCOME_UNCONFIRMED') { exit 40 }\nexit 0\n";
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
        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
        }
        private static string B64(string str)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(str));
        }
        private static FileSecurity UserReadableLockedFile()
        {
            var sec = new FileSecurity();
            sec.SetAccessRuleProtection(true, false);
            sec.SetOwner(AdminSid);
            sec.AddAccessRule(new FileSystemAccessRule(AdminSid,
                FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(SystemSid,
                FileSystemRights.FullControl, AccessControlType.Allow));
            sec.AddAccessRule(new FileSystemAccessRule(UsersSid,
                FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            return sec;
        }
        private static void WriteOnce(string path, string content)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            // Atomic create with already protected ACL: no open window for swaps.
            using (var file = new FileStream(path, FileMode.CreateNew,
                FileSystemRights.Write, FileShare.None, 4096, FileOptions.WriteThrough,
                UserReadableLockedFile()))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
        }
        // Pure inspection function; useful for offline tests without UAC/MSI.
        internal static string ExitMeaning(int code)
        {
            switch (code)
            {
                case 0: return "MSI_RETURNED_SUCCESS";
                case 3010: return "MSI_RETURNED_REBOOT_REQUIRED";
                case 1602: return "MSI_CANCELLED";
                case 1603: return "MSI_FAILED";
                case 1618: return "ANOTHER_INSTALL_ACTIVE";
                default: return "MSI_OTHER_EXIT_CODE";
            }
        }
        private static void StartDetachedLab(string id,
            Phase30B7D6GProtectedInstallPreflight.FixedCommandPlan plan)
        {
            // PHASE30B7D6M1_DEFENSE_IN_DEPTH_V1
            if (!Phase30B7D6MReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled)
                throw new InvalidOperationException("Real MSI laboratory mode is disabled in this build.");
            // Every attempt gets a unique transaction ID. A prior 6H report
            // for this same signed stage cannot block a new authorized attempt.
            string txnId = Guid.NewGuid().ToString("N");
            if (!ValidId(txnId))
                throw new InvalidDataException("Invalid transaction identifier.");
            string stage = Path.GetDirectoryName(plan.Package);
            string manifest = Path.Combine(stage, "update-manifest-v1.txt");
            string signature = Path.Combine(stage, "update-manifest-v1.sig");
            string sys = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System));
            string ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(ps) || IsReparse(ps))
                throw new InvalidDataException("System PowerShell executable missing or redirected.");
            string preflightRoot = Path.GetFullPath(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData));
            string futureFolder = Path.Combine(preflightRoot, "AutoBacklightUpdateTransactions", txnId);
            string log = Path.Combine(futureFolder, "msiexec.log");
            foreach (string p in new string[] {futureFolder, plan.Package, plan.Msiexec, manifest,
                signature, log, ps})
                if (p.IndexOfAny(new char[] { '\"', '\r', '\n' }) >= 0)
                    throw new InvalidDataException("Invalid command path.");
            string hMsi = HashFile(plan.Package), hManifest = HashFile(manifest),
                hSignature = HashFile(signature);
            // Signature, MSI identity, installed product and ACL are assessed a
            // SECOND TIME immediately before materializing execution intent.
            Phase30B7D6GProtectedInstallPreflight.BuildReadOnlyPlan(id);
            // Only after all checks succeed, create the one-time transaction.
            string folder = Phase30B7D6HProtectedTransactionHandoffLab.CreateTransaction(txnId);
            if (!Directory.Exists(folder) || IsReparse(folder) ||
                !String.Equals(folder, futureFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Protected transaction directory invalid.");
            using (var self = Process.GetCurrentProcess())
            {
                string script = PsTemplate
                    .Replace("__TXN_DIR__", B64(folder))
                    .Replace("__MSI__", B64(plan.Package))
                    .Replace("__MANIFEST__", B64(manifest))
                    .Replace("__SIGNATURE__", B64(signature))
                    .Replace("__MSIEXEC__", B64(plan.Msiexec))
                    .Replace("__OLD_EXE__", B64(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Auto Backlight for ThinkPad", "Auto Backlight for ThinkPad.exe")))
                    .Replace("__LOG__", B64(log))
                    .Replace("__MSI_SHA__", hMsi)
                    .Replace("__MAN_SHA__", hManifest)
                    .Replace("__SIG_SHA__", hSignature)
                    .Replace("__PARENT_PID__", self.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("__PARENT_TICKS__", self.StartTime.ToUniversalTime().Ticks.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("__TXN_ID__", txnId)
                    .Replace("__STAGE_ID__", id);
                string intent = "TRANSACTION_ID=" + txnId + "\nSTAGE_ID=" + id +
                    "\nINSTALL_AUTHORIZATION=EXPLICIT_LAB_CLI\n" +
                    "CREATED_UTC=" + DateTime.UtcNow.ToString("o") +
                    "\nSTATUS=PREPARED\nMSI_SHA256=" + hMsi + "\n";
                WriteOnce(Path.Combine(folder, "intent.txt"), intent);
                string worker = Path.Combine(folder, "worker.ps1");
                WriteOnce(worker, script);
                // Never wait for MSI here: this EXE can be killed by MSI custom
                // actions that terminate other processes with the same name.
                var psi = new ProcessStartInfo {
                    FileName = ps,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + worker + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = sys
                };
                using (Process monitor = Process.Start(psi))
                {
                    if (monitor == null) throw new InvalidOperationException("Worker launch failed.");
                }
            }
        }
        internal static int Run(string[] args)
        {
            if (args == null || (args.Length != 2 && args.Length != 3) ||
                (args[0] != DrySwitch && args[0] != ExecuteSwitch) ||
                !ValidId(args[1])) return 10;
            if (args[0] == DrySwitch && args.Length != 2) return 10;
            if (args[0] == ExecuteSwitch &&
                (args.Length != 3 || args[2] != ExplicitConsent)) return 10;
            // PHASE30B7D6M1_REAL_MSI_LAB_COMPILE_TIME_GATE_V1
            if (args[0] == ExecuteSwitch &&
                !Phase30B7D6MReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled)
                return 15; // lab execution forbidden in normal Release/Debug builds
            if (!Elevated()) return 13;
            try
            {
                // Both modes perform the exact same trusted, read-only checks.
                var plan = Phase30B7D6GProtectedInstallPreflight.BuildReadOnlyPlan(args[1]);
                if (String.IsNullOrEmpty(plan.Package) || String.IsNullOrEmpty(plan.Msiexec) ||
                    String.IsNullOrEmpty(plan.LogFile)) return 14;
                if (args[0] == DrySwitch) return 0;  // Never starts any process.
                StartDetachedLab(args[1], plan);     // ONLY explicit lab commit.
                return 0; // Worker STARTED, not MSI success! See result.txt.
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Protected MSI executor refused: " + ex.GetType().Name);
                return 14;
            }
        }
    }
}
