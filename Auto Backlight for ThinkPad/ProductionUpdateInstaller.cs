
// PHASE30B_PRODUCTION_INSTALLER_RC1
// Installed-only, UI-consent-bound production MSI transaction.
// LAB switches remain separately compile-time disabled. No arbitrary URLs/paths
// or unsigned installers can reach this worker. Runtime Windows tests required.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ProductionUpdateInstaller
    {
        internal const string ChildSwitch = "--update-production-approved-child";
        internal const int Refused = 42;
        private const string AppProcessName = "Auto Backlight for ThinkPad";
        private const string MutexPrefix = @"Local\ABTP.UpdateInstall.v1.";

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint pid);

        internal static string Sid()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                if (identity == null || identity.User == null)
                    throw new UnauthorizedAccessException("No Windows SID");
                else return identity.User.Value;
        }

        private static bool Elevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return identity != null &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool Id(string value)
        {
            Guid parsed;
            return !String.IsNullOrEmpty(value) &&
                Regex.IsMatch(value, @"^[a-f0-9]{32}$", RegexOptions.CultureInvariant) &&
                Guid.TryParseExact(value, "N", out parsed) && parsed != Guid.Empty &&
                String.Equals(value, parsed.ToString("N"), StringComparison.Ordinal);
        }

        internal static string MutexName(string sid)
        {
            if (String.IsNullOrEmpty(sid) ||
                !Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$"))
                throw new ArgumentException("Invalid SID");
            return MutexPrefix + sid;
        }

        // Executed for every normal GUI startup, BEFORE WPF starts.
        // The detached worker holds this same mutex through msiexec.
        internal static bool StartupAllowed()
        {
            try
            {
                using (var mutex = new Mutex(false, MutexName(Sid())))
                {
                    bool entered = false;
                    try
                    {
                        entered = mutex.WaitOne(0);
                        return entered;
                    }
                    catch (AbandonedMutexException)
                    {
                        entered = true;
                        return false; // crash or ambiguous ownership: fail closed
                    }
                    finally { if (entered) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception) { return false; }
        }

        internal static bool CanOfferFromUi(string stagedMsi)
        {
            if (ReleaseSafetyGate.UnsafeRealMsiLabBuildEnabled) return false;
            if (TrustedUacBoundary.AuditSelf() != "PASS") return false;
            return Id(ProtectedHandoff.ExtractStageId(stagedMsi));
        }

        private static PipeSecurity UiPipeSecurity()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            var current = new SecurityIdentifier(Sid());
            security.SetOwner(current);
            security.AddAccessRule(new PipeAccessRule(current, PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            return security;
        }

        // Invoked ONLY from About after the user's Yes confirmation. Production
        // ParentSwitch intentionally remains disabled for standalone CLI calls.
        internal static int LaunchFromUi(string stagedMsi)
        {
            if (!CanOfferFromUi(stagedMsi)) return 26;
            string id = ProtectedHandoff.ExtractStageId(stagedMsi);
            if (!Id(id) || !LocalReview.ReviewReadOnly(stagedMsi)) return 34;
            try
            {
                string exe = TrustedUacBoundary.ExpectedInstalledExe();
                string sid = Sid();
                string pipeName = "ABTP_Approve_" + Guid.NewGuid().ToString("N");
                using (var self = Process.GetCurrentProcess())
                using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 512,
                    UiPipeSecurity()))
                {
                    string cli = ChildSwitch + " " + id + " " + sid + " " + self.Id + " " +
                        self.StartTime.ToUniversalTime().Ticks + " " + pipeName;
                    var psi = new ProcessStartInfo {
                        FileName = exe, Arguments = cli, UseShellExecute = true,
                        Verb = "runas", ErrorDialog = false,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    using (Process child = Process.Start(psi))
                    {
                        if (child == null) return 24;
                        IAsyncResult pending = pipe.BeginWaitForConnection(null, null);
                        try
                        {
                            if (!pending.AsyncWaitHandle.WaitOne(90000)) return 25;
                            pipe.EndWaitForConnection(pending);
                        }
                        finally { pending.AsyncWaitHandle.Dispose(); }
                        uint pid;
                        if (!GetNamedPipeClientProcessId(
                            pipe.SafePipeHandle.DangerousGetHandle(), out pid) || pid != child.Id)
                            return 33;
                        byte[] approval = Encoding.ASCII.GetBytes("APPROVED:" + id + "\n");
                        pipe.Write(approval, 0, approval.Length);
                        pipe.Flush();
                        if (!child.WaitForExit(120000)) return 25;
                        return child.ExitCode;
                    }
                }
            }
            catch (Win32Exception ex) { return ex.NativeErrorCode == 1223 ? 23 : 24; }
            catch (Exception) { return 36; }
        }

        // Only the UI-created NamedPipeServerStream can authorize the elevated
        // child: the kernel must identify the server as the pinned GUI process.
        // A fake CLI caller cannot obtain a valid UI approval by inventing a pipe.
        private static bool ReceiveUiApproval(string id, string sid, int guiPid,
            long guiStart, string pipeName)
        {
            if (!Elevated() || Sid() != sid || !Id(id) || guiPid <= 0 || guiStart <= 0 ||
                String.IsNullOrWhiteSpace(pipeName) ||
                !Regex.IsMatch(pipeName, @"^ABTP_Approve_[a-f0-9]{32}$")) return false;
            try
            {
                using (var gui = Process.GetProcessById(guiPid))
                using (var self = Process.GetCurrentProcess())
                {
                    if (gui.HasExited || gui.SessionId != self.SessionId ||
                        gui.StartTime.ToUniversalTime().Ticks != guiStart ||
                        !String.Equals(Path.GetFullPath(gui.MainModule.FileName),
                            TrustedUacBoundary.ExpectedInstalledExe(),
                            StringComparison.OrdinalIgnoreCase)) return false;
                    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In))
                    {
                        client.Connect(15000);
                        uint serverPid;
                        if (!GetNamedPipeServerProcessId(
                            client.SafePipeHandle.DangerousGetHandle(), out serverPid) ||
                            serverPid != guiPid) return false;
                        byte[] b = new byte[128];
                        int count = client.Read(b, 0, b.Length);
                        return Encoding.ASCII.GetString(b, 0, count) == "APPROVED:" + id + "\n";
                    }
                }
            }
            catch (Exception) { return false; }
        }

        // A privileged command-line switch without a live, installed UI pipe is
        // refused even if the stage ID, SID and paths look plausible.
        internal static int RunApprovedChild(string[] args)
        {
            if (args == null || args.Length != 6 || args[0] != ChildSwitch ||
                !Id(args[1]) || !Regex.IsMatch(args[2], @"^S-1-[0-9]+(?:-[0-9]+)+$") ||
                !Regex.IsMatch(args[3], @"^[0-9]{1,10}$") ||
                !Regex.IsMatch(args[4], @"^[0-9]{15,20}$")) return 10;
            int parentPid; long parentTicks;
            if (!Int32.TryParse(args[3], out parentPid) ||
                !Int64.TryParse(args[4], out parentTicks)) return 10;
            if (TrustedUacBoundary.AuditSelf() != "PASS" || !Elevated())
                return 26;
            if (!ReceiveUiApproval(args[1], args[2], parentPid, parentTicks, args[5]))
                return Refused;
            try
            {
                // Independently validate the already-protected MSI after UAC.
                ProtectedInstallPreflight.FixedCommandPlan plan =
                    ProtectedInstallPreflight.BuildReadOnlyPlan(args[1]);
                if (String.IsNullOrEmpty(plan.Package) || String.IsNullOrEmpty(plan.Msiexec))
                    return 34;
                LaunchWorker(args[1], parentPid, parentTicks, plan);
                return 0;  // Worker armed; NOT an MSI success report.
            }
            catch (Exception) { return 36; }
        }

        private static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static string Base64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static bool IsReparse(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static FileSecurity LockedReportFile()
        {
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.SetOwner(admin);
            security.AddAccessRule(new FileSystemAccessRule(admin, FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));
            return security;
        }

        private static void WriteOnce(string path, string content)
        {
            byte[] data = new UTF8Encoding(false).GetBytes(content);
            using (var f = new FileStream(path, FileMode.CreateNew, FileSystemRights.Write,
                FileShare.None, 4096, FileOptions.WriteThrough, LockedReportFile()))
            {
                f.Write(data, 0, data.Length);
                f.Flush(true);
            }
        }

        private static void LaunchWorker(string id, int parentPid, long parentTicks,
            ProtectedInstallPreflight.FixedCommandPlan plan)
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string folder = ProtectedTransactionHandoffLab
                .CreateTransaction(transactionId);
            string stage = Path.GetDirectoryName(plan.Package);
            string manifest = Path.Combine(stage, "update-manifest-v1.txt");
            string signature = Path.Combine(stage, "update-manifest-v1.sig");
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powershell = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
            string log = Path.Combine(folder, "msiexec.log");
            if (!File.Exists(powershell) || IsReparse(powershell) ||
                !File.Exists(plan.Msiexec) || IsReparse(plan.Msiexec))
                throw new InvalidDataException("System worker or MSI executable missing.");
            foreach (string p in new [] { folder, plan.Package, manifest, signature,
                powershell, plan.Msiexec, log })
                if (p.IndexOfAny(new [] { '\"', '\r', '\n' }) >= 0)
                    throw new InvalidDataException("Unsafe command-line path.");

            string gate = WorkerPrelaunchContract
                .BuildProductionReadOnlyPrelaunchWorker();
            string template = WorkerTemplate
                .Replace("__MUTEX__", MutexName(Sid()))
                .Replace("__TXN__", Base64(folder))
                .Replace("__MSI__", Base64(plan.Package))
                .Replace("__MANIFEST__", Base64(manifest))
                .Replace("__SIG__", Base64(signature))
                .Replace("__MSIEXEC__", Base64(plan.Msiexec))
                .Replace("__LOG__", Base64(log))
                .Replace("__GATE__", Convert.ToBase64String(Encoding.Unicode.GetBytes(gate)))
                .Replace("__MSI_HASH__", Sha256(plan.Package))
                .Replace("__MAN_HASH__", Sha256(manifest))
                .Replace("__SIG_HASH__", Sha256(signature))
                .Replace("__PARENT_PID__", parentPid.ToString())
                .Replace("__PARENT_TICKS__", parentTicks.ToString())
                .Replace("__TXN_ID__", transactionId)
                .Replace("__STAGE_ID__", id);
            string worker = Path.Combine(folder, "worker.ps1");
            WriteOnce(Path.Combine(folder, "intent.txt"),
                "TRANSACTION_ID=" + transactionId + "\nSTAGE_ID=" + id +
                "\nCONSENT=LIVE_UI_PIPE\nMSI_SHA256=" + Sha256(plan.Package) + "\n");
            WriteOnce(worker, template);
            var psi = new ProcessStartInfo {
                FileName = powershell,
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + worker + "\"",
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = sys
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("Worker start failed.");
                string armed = Path.Combine(folder, "armed.txt");
                DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                while (DateTime.UtcNow < deadline)
                {
                    if (File.Exists(armed)) return;
                    if (p.HasExited) throw new InvalidOperationException("Worker exited before arm.");
                    Thread.Sleep(100);
                }
                throw new TimeoutException("Worker did not acquire restart lock.");
            }
        }

        internal static string LastResultForAbout()
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                    ProtectedTransactionHandoffLab.TransactionRootName);
                if (!Directory.Exists(root) || IsReparse(root)) return null;
                string latest = null; DateTime best = DateTime.MinValue;
                foreach (var folder in Directory.GetDirectories(root))
                {
                    string id = Path.GetFileName(folder);
                    if (!Id(id) || IsReparse(folder)) continue;
                    string result = Path.Combine(folder, "result.txt");
                    if (!File.Exists(result) || IsReparse(result)) continue;
                    DateTime utc = File.GetLastWriteTimeUtc(result);
                    if (utc > best)
                    {
                        string value = File.ReadAllText(result);
                        if (value.Length < 2048 && value.Contains("PRODUCTION_WORKER=TRUE"))
                        {
                            best = utc; latest = value;
                        }
                    }
                }
                if (latest == null) return null;
                var status = Regex.Match(latest, @"(?m)^STATUS=([A-Z0-9_]+)$");
                var code = Regex.Match(latest, @"(?m)^MSI_EXIT_CODE=(-?[0-9]+)$");
                if (!status.Success || !code.Success) return null;
                return "Last MSI update: " + status.Groups[1].Value +
                    " (installer exit code " + code.Groups[1].Value + ").";
            }
            catch (Exception) { return null; }
        }

        // Worker is a protected, immutable, elevated script. It outlives the EXE
        // and records MSI result independently. Every variable is derived from
        // trusted system paths or canonical GUID IDs, not from user text.
        private const string WorkerTemplate = @"# PHASE30B_PRODUCTION_WORKER_RC1
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$txn=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__TXN__'))
$msi=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MSI__'))
$manifest=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MANIFEST__'))
$signature=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__SIG__'))
$installer=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MSIEXEC__'))
$log=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__LOG__'))
$gateEncoded='__GATE__'
$shaMsi='__MSI_HASH__';$shaManifest='__MAN_HASH__';$shaSignature='__SIG_HASH__'
$parentPid=__PARENT_PID__; $parentStart=__PARENT_TICKS__
$transactionId='__TXN_ID__';$stageId='__STAGE_ID__'
$lockName='__MUTEX__'
$started=$false; $exitCode=-1; $status='FAILED_BEFORE_MSI'; $reboot=$false
$armed=$false; $parentGone=$false; $hashes=$false; $gateReady=$false
$lock=$null;$locked=$false
try {
    $lock=New-Object System.Threading.Mutex($false,$lockName)
    try { $locked=$lock.WaitOne(0) } catch [Threading.AbandonedMutexException] {
        $locked=$true; throw 'Abandoned update lock'
    }
    if (-not $locked) { throw 'Another update transaction holds restart lock' }
    [IO.File]::WriteAllText((Join-Path $txn 'armed.txt'), 'ARMED', [Text.Encoding]::ASCII)
    $armed=$true
    $deadline=[DateTime]::UtcNow.AddSeconds(50)
    while ([DateTime]::UtcNow -lt $deadline) {
        $p=Get-Process -Id $parentPid -ErrorAction SilentlyContinue
        if ($null -eq $p) { $parentGone=$true;break }
        if ($p.StartTime.ToUniversalTime().Ticks -ne $parentStart) { $parentGone=$true;break }
        Start-Sleep -Milliseconds 160
    }
    if (-not $parentGone) { throw 'GUI did not exit within 50 seconds' }
    if ((Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -cne $shaMsi -or
        (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -cne $shaManifest -or
        (Get-FileHash -LiteralPath $signature -Algorithm SHA256).Hash -cne $shaSignature) {
        throw 'Publisher-verified package changed after parent exit'
    }
    $hashes=$true
    $sys=[Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $ps=Join-Path $sys 'WindowsPowerShell\v1.0\powershell.exe'
    if (-not [IO.File]::Exists($ps) -or
        ([IO.File]::GetAttributes($ps) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'System PowerShell not trusted'
    }
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$ps
    $psi.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$gateEncoded
    $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.WorkingDirectory=$sys
    $psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    $gate=[Diagnostics.Process]::Start($psi)
    if ($null -eq $gate) { throw 'PreMSI worker did not start' }
    try {
        if (-not $gate.WaitForExit(25000)) { throw 'PreMSI gate timed out' }
        $gout=([string]$gate.StandardOutput.ReadToEnd()).Trim()
        $gerr=([string]$gate.StandardError.ReadToEnd()).Trim()
        if ([int]$gate.ExitCode -ne 0 -or $gout -cne 'PREMSI_GATE=READY' -or
            $gerr.Length -ne 0) { throw 'PreMSI gate did not return exact READY' }
    } finally { $gate.Dispose() }
    $gateReady=$true
    # The cross-process lock is held from before process audit through MSI.
    # Every normal GUI startup checks this same lock and exits if unavailable.
    $psi=New-Object Diagnostics.ProcessStartInfo
    $psi.FileName=$installer
    $psi.Arguments='/i ""'+$msi+'"" /qn /norestart /L*v ""'+$log+'""'
    $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.WorkingDirectory=[IO.Path]::GetDirectoryName($installer)
    $install=[Diagnostics.Process]::Start($psi)
    if ($null -eq $install) { throw 'Windows Installer could not start' }
    $started=$true
    try { $install.WaitForExit(); $exitCode=[int]$install.ExitCode }
    finally { $install.Dispose() }
    switch ($exitCode) {
      0 { $status='MSI_RETURNED_SUCCESS' }
      3010 { $status='MSI_RETURNED_REBOOT_REQUIRED';$reboot=$true }
      1602 { $status='MSI_CANCELLED' }
      1603 { $status='MSI_FAILED' }
      1618 { $status='ANOTHER_INSTALL_ACTIVE' }
      default { $status='MSI_OTHER_EXIT_CODE' }
    }
} catch {
    if ($started -and $exitCode -lt 0) { $status='MSI_OUTCOME_UNCONFIRMED' }
} finally {
    try {
        $body='PRODUCTION_WORKER=TRUE'+""`n""+
          'TRANSACTION_ID='+$transactionId+""`n""+
          'STAGE_ID='+$stageId+""`n""+
          'WORKER_ARMED='+[string]$armed+""`n""+
          'GUI_EXITED='+[string]$parentGone+""`n""+
          'PACKAGE_HASHES_VERIFIED='+[string]$hashes+""`n""+
          'PREMSI_GATE_READY='+[string]$gateReady+""`n""+
          'MSI_STARTED='+[string]$started+""`n""+
          'MSI_EXIT_CODE='+[string]$exitCode+""`n""+
          'STATUS='+$status+""`n""+
          'REBOOT_REQUIRED='+[string]$reboot+""`n""+
          'POSTINSTALL_REGISTRATION_AND_TASK_AUDIT_REQUIRED=TRUE'+""`n""
        $temp=Join-Path $txn 'result.tmp';$result=Join-Path $txn 'result.txt'
        if (-not [IO.File]::Exists($result) -and -not [IO.File]::Exists($temp)) {
            [IO.File]::WriteAllText($temp,$body, (New-Object Text.UTF8Encoding($false)))
            [IO.File]::Move($temp,$result)
        }
    } catch { }
    if ($locked) { try { $lock.ReleaseMutex() } catch { } }
    if ($null -ne $lock) { $lock.Dispose() }
}
";
    }
}

