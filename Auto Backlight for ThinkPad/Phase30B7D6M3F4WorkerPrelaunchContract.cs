// PHASE30B7D6M3F4_DETACHED_PREMSI_READONLY_CONTRACT_V1
// This produces an independent, read-only pre-MSI process gate harness.
// It does NOT launch an MSI or grant installation authority.
// A successful gate is only a POINT-IN-TIME process absence observation;
// atomic prevention of app restart and privileged transaction binding
// are STILL REQUIRED before shipping a production worker.
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6M3F4WorkerPrelaunchContract
    {
        // The nested audit is the immutable Phase 3F.3 gate, run in a SEPARATE
        // system PowerShell 5.1 process; no user-supplied process name on the
        // production path. The *outer* process will later be the MSI worker.
        private const string WorkerTemplate = @"# PHASE30B7D6M3F4_PREMSI_READONLY_WORKER_V1
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$encodedGate='__ENCODED_GATE__'
try {
    $sys=[Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $ps=Join-Path $sys 'WindowsPowerShell\v1.0\powershell.exe'
    if (-not [IO.File]::Exists($ps)) { throw 'MISSING_SYSTEM_POWERSHELL' }
    if (([IO.File]::GetAttributes($ps) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'REDIRECTED_SYSTEM_POWERSHELL'
    }
    $psi=New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName=$ps
    $psi.Arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand '+$encodedGate
    $psi.WorkingDirectory=$sys
    $psi.UseShellExecute=$false
    $psi.CreateNoWindow=$true
    $psi.RedirectStandardOutput=$true
    $psi.RedirectStandardError=$true
    $child=[Diagnostics.Process]::Start($psi)
    if ($null -eq $child) { throw 'PROCESS_GATE_LAUNCH_FAILED' }
    try {
        if (-not $child.WaitForExit(15000)) {
            try { $child.Kill(); [void]$child.WaitForExit(3000) } catch { }
            throw 'PROCESS_GATE_TIMEOUT'
        }
        $code=[int]$child.ExitCode
        $stdout=([string]$child.StandardOutput.ReadToEnd()).Trim()
        $stderr=([string]$child.StandardError.ReadToEnd()).Trim()
    }
    finally { $child.Dispose() }
    if ($stderr.Length -ne 0) { throw 'PROCESS_GATE_STDERR' }
    if ($code -eq 0 -and $stdout -ceq 'PROCESS_GATE=ABSENT;SCANS=3') {
        Write-Output 'PREMSI_GATE=READY'
        exit 0
    }
    if ($code -eq 41 -and $stdout -match '^PROCESS_GATE=BLOCKED;SCAN=[1-3];COUNT=[1-9][0-9]*$') {
        Write-Output 'PREMSI_GATE=BLOCKED'
        exit 41
    }
    throw 'PROCESS_GATE_FAIL_CLOSED'
}
catch {
    Write-Output 'PREMSI_GATE=ERROR'
    exit 42
}
";

        private static string WrapReadOnlyGate(string gateScript)
        {
            if (String.IsNullOrEmpty(gateScript) ||
                !gateScript.StartsWith("# PHASE30B7D6M3F3_WORKER_ABSENCE_AUDIT_V1",
                    StringComparison.Ordinal))
                throw new ArgumentException("Invalid gate source.");
            // PowerShell -EncodedCommand uses UTF-16LE. Literal Base64 contains
            // no quotes/metacharacters, therefore no command-line interpolation.
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(gateScript));
            return WorkerTemplate.Replace("__ENCODED_GATE__", encoded);
        }

        internal static string BuildProductionReadOnlyPrelaunchWorker()
        {
            return WrapReadOnlyGate(Phase30B7D6M3F3DetachedWorkerGate
                .BuildProductionReadOnlyWorkerGate());
        }

        // Pure, non-CLI test seam: this is not accepted by any installation
        // command, UI action, or privileged production worker.
        internal static string BuildSyntheticReadOnlyPrelaunchWorker(string testProcessName)
        {
            return WrapReadOnlyGate(Phase30B7D6M3F3DetachedWorkerGate
                .BuildSyntheticReadOnlyWorkerGate(testProcessName, 3, 100));
        }

        // Pure diagnostic classification for future one-shot status parsing.
        // A zero exit code without the exact success marker is not success.
        internal static string ClassifyReadOnlyGate(int exitCode,
            string stdout, string stderr)
        {
            if (!String.IsNullOrWhiteSpace(stderr)) return "ERROR";
            string s = (stdout ?? String.Empty).Trim();
            if (exitCode == 0 && s == "PREMSI_GATE=READY") return "READY";
            if (exitCode == 41 && s == "PREMSI_GATE=BLOCKED") return "BLOCKED";
            return "ERROR";
        }
    }
}
