// Phase 1311: post-install relaunch belongs to the original GUI user session,
// NEVER to the elevated detached MSI worker. Only observed signed-worker results
// from the protected transaction directory permit a relaunch.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class PostUpdateRelaunch
    {
        // The observer is a child of the GUI process, not of the UAC worker.
        // Thus it inherits the GUI's interactive token (which may itself be elevated).
        // It is launched only AFTER a signed worker is armed, immediately before shutdown.
        private const string Monitor = @"$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$stage='__STAGE__'
$target='__TARGET__'
$pidGui=__PID__
$ticksGui=__TICKS__
$root=[IO.Path]::Combine([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData),'AutoBacklightUpdateTransactions')
$exe=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__EXE__'))
$lockName=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__MUTEX__'))
$deadline=[DateTime]::UtcNow.AddMinutes(20)
function Reparse([string]$p) {
    return (([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -ne 0)
}
function MutexFree {
    $mx=$null;$held=$false;$abandoned=$false
    try {
        $mx=New-Object Threading.Mutex($false,$lockName)
        try { $held=$mx.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $held=$true;$abandoned=$true }
        return ($held -and -not $abandoned)
    } finally {
        if ($held) { try { $mx.ReleaseMutex() } catch {} }
        if ($null -ne $mx) { $mx.Dispose() }
    }
}
function Notice([string]$message) {
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        [void][Windows.Forms.MessageBox]::Show($message,'Auto Backlight for ThinkPad update',
            [Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information)
    } catch {}
}
try {
    if ($stage -cnotmatch '^[a-f0-9]{32}$' -or $target -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { exit 31 }
    $guiGone=$false
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not $guiGone) {
            $p=Get-Process -Id $pidGui -ErrorAction SilentlyContinue
            if ($null -eq $p) { $guiGone=$true }
            else {
                try { if ($p.StartTime.ToUniversalTime().Ticks -ne $ticksGui) { $guiGone=$true } }
                catch { $guiGone=$true }
            }
            if (-not $guiGone) { Start-Sleep -Milliseconds 300; continue }
        }
        if (-not [IO.Directory]::Exists($root)) { Start-Sleep -Milliseconds 500; continue }
        if (Reparse $root) { exit 32 }
        $found=@()
        foreach ($dir in [IO.Directory]::GetDirectories($root)) {
            $id=[IO.Path]::GetFileName($dir)
            if ($id -cnotmatch '^[a-f0-9]{32}$') { continue }
            if (Reparse $dir) { exit 33 }
            $result=[IO.Path]::Combine($dir,'result.txt')
            if (-not [IO.File]::Exists($result)) { continue }
            if (Reparse $result) { exit 34 }
            $length=([IO.FileInfo]$result).Length
            if ($length -lt 30 -or $length -gt 2048) { continue }
            $body=[IO.File]::ReadAllText($result)
            if (-not [Regex]::IsMatch($body,'(?m)^PRODUCTION_WORKER=TRUE\r?$')) { continue }
            if (-not [Regex]::IsMatch($body,'(?m)^STAGE_ID='+$stage+'\r?$')) { continue }
            if (-not [Regex]::IsMatch($body,'(?m)^TRANSACTION_ID='+$id+'\r?$')) { exit 35 }
            if (-not [Regex]::IsMatch($body,'(?m)^GUI_EXITED=True\r?$')) { exit 36 }
            $status=[Regex]::Match($body,'(?m)^STATUS=([A-Z0-9_]+)\r?$')
            $code=[Regex]::Match($body,'(?m)^MSI_EXIT_CODE=(-?[0-9]+)\r?$')
            if (-not $status.Success -or -not $code.Success) { exit 37 }
            if (@('MSI_RETURNED_SUCCESS','MSI_RETURNED_REBOOT_REQUIRED','MSI_CANCELLED','MSI_FAILED',
                   'ANOTHER_INSTALL_ACTIVE','MSI_OTHER_EXIT_CODE','FAILED_BEFORE_MSI',
                   'MSI_OUTCOME_UNCONFIRMED') -cnotcontains $status.Groups[1].Value) { exit 38 }
            $found+=@{Status=$status.Groups[1].Value;Code=$code.Groups[1].Value}
        }
        if ($found.Count -gt 1) { exit 39 }
        if ($found.Count -eq 1) {
            if (-not (MutexFree)) { Start-Sleep -Milliseconds 400; continue }
            if (-not [IO.File]::Exists($exe) -or (Reparse $exe)) { exit 40 }
            # Another launcher (e.g. a scheduled task) may race with relaunch.
            # The GUI now uses an atomic singleton; wait for a surviving process.
            $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
            $alive=$false
            for ($attempt=0; $attempt -lt 3; $attempt++) {
                $running=@(Get-Process -Name 'Auto Backlight for ThinkPad' -ErrorAction SilentlyContinue |
                    Where-Object { $_.SessionId -eq $session -and -not $_.HasExited })
                if ($running.Count -eq 0) {
                    Start-Process -FilePath $exe -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -ErrorAction Stop
                }
                Start-Sleep -Milliseconds 1200
                $survivors=@(Get-Process -Name 'Auto Backlight for ThinkPad' -ErrorAction SilentlyContinue |
                    Where-Object { $_.SessionId -eq $session -and -not $_.HasExited })
                if ($survivors.Count -gt 0) { $alive=$true; break }
            }
            $r=$found[0]
            $actualVersion='UNCONFIRMED'
            try {
                $v=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
                $actualVersion=([string]$v.FileMajorPart)+'.'+([string]$v.FileMinorPart)+'.'+([string]$v.FileBuildPart)
            } catch {}
            if (-not $alive) {
                Notice ('Update finished, but the app did not restart. Please start it manually.')
            } elseif ($r.Status -ceq 'MSI_RETURNED_SUCCESS' -and $r.Code -ceq '0' -and $actualVersion -ceq $target) {
                Notice ('Updated to v'+$actualVersion+'.')
            } elseif ($r.Status -ceq 'MSI_RETURNED_REBOOT_REQUIRED') {
                Notice ('Windows Installer requested a restart (3010). Current EXE version: '+$actualVersion+'. Please restart Windows.')
            } else {
                Notice ('The update was not confirmed. Worker status: '+$r.Status+', exit code: '+$r.Code+'. Current EXE version: '+$actualVersion+'. Check About or application.log.')
            }
            exit 0
        }
        Start-Sleep -Milliseconds 450
    }
    # No authenticated result or lock release. Never infer MSI success.
    exit 41
} catch { exit 42 }
";

        private static string Enc(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        internal static bool Arm(string stagedMsi, Version expectedVersion)
        {
            try
            {
                string stageId = ProtectedHandoff.ExtractStageId(stagedMsi);
                if (String.IsNullOrWhiteSpace(stageId) ||
                    !Regex.IsMatch(stageId, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant) ||
                    expectedVersion == null || expectedVersion.Build < 0 ||
                    TrustedUacBoundary.AuditSelf() != "PASS") return false;
                string exe = TrustedUacBoundary.ExpectedInstalledExe();
                string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string powershell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(powershell) ||
                    (File.GetAttributes(powershell) & FileAttributes.ReparsePoint) != 0)
                    return false;
                using (Process me = Process.GetCurrentProcess())
                {
                    string script = Monitor
                        .Replace("__STAGE__", stageId)
                        .Replace("__TARGET__", expectedVersion.ToString(3))
                        .Replace("__PID__", me.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Replace("__TICKS__", me.StartTime.ToUniversalTime().Ticks.ToString(
                            System.Globalization.CultureInfo.InvariantCulture))
                        .Replace("__EXE__", Enc(exe))
                        .Replace("__MUTEX__", Enc(ProductionUpdateInstaller.MutexName(
                            ProductionUpdateInstaller.Sid())));
                    string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                    var psi = new ProcessStartInfo {
                        FileName = powershell,
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                        UseShellExecute = false, CreateNoWindow = true,
                        WorkingDirectory = system
                    };
                    using (Process p = Process.Start(psi))
                        return p != null && !p.HasExited;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Phase1311: post-MSI relaunch observer not armed", ex);
                return false;
            }
        }
    }
}
