// Independent read-only worker process gate.
// This builds an independent Windows PowerShell 5.1 pre-MSI process audit.
// NO msiexec invocation, UAC, process termination, or integration with the
// Production execution requires a separately authorized installation workflow.
// Future detached worker MUST execute the immutable production gate AFTER its
// launcher exits and immediately BEFORE invoking msiexec. A successful scan
// is only a point-in-time observation: coordinating app startup/locking and
// the actual MSI in-use logic is still required before release authorization.
using System;
using System.Text.RegularExpressions;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class DetachedWorkerGate
    {
        private const string ProductionProcessName = "Auto Backlight for ThinkPad";
        private const int ProductionPasses = 3;
        private const int ProductionPauseMilliseconds = 200;

        // SCRIPT DOES NOT START AN INSTALLER. It is meant to run in an
        // independent PowerShell process, not in the same-name application.
        private const string Script = @"# PHASE30B7D6M3F3_WORKER_ABSENCE_AUDIT_V1
$ErrorActionPreference='Stop'
$target='__PROCESS_NAME__'
$passes=__PASSES__
$pauseMs=__PAUSE_MS__
try {
    if ($passes -lt 2 -or $passes -gt 5 -or $pauseMs -lt 0 -or $pauseMs -gt 2000) {
        throw 'INVALID_GATE_CONFIGURATION'
    }
    for ($scan=1; $scan -le $passes; $scan++) {
        $instances=@([System.Diagnostics.Process]::GetProcessesByName($target))
        $live=0
        try {
            foreach ($instance in $instances) {
                # Fail closed if a same-name process is uninspectable.
                if (-not $instance.HasExited) { $live++ }
            }
        }
        finally {
            foreach ($instance in $instances) {
                if ($null -ne $instance) { $instance.Dispose() }
            }
        }
        if ($live -ne 0) {
            Write-Output ('PROCESS_GATE=BLOCKED;SCAN='+$scan+';COUNT='+$live)
            exit 41
        }
        if ($scan -lt $passes) { Start-Sleep -Milliseconds $pauseMs }
    }
    Write-Output ('PROCESS_GATE=ABSENT;SCANS='+$passes)
    exit 0
}
catch {
    Write-Output 'PROCESS_GATE=REJECT_UNVERIFIABLE'
    exit 42
}
";

        private static string Build(string processName, int passes, int pauseMs)
        {
            // No PowerShell metacharacters or arbitrary source interpolation.
            if (String.IsNullOrEmpty(processName) || processName.Length > 80 ||
                !Regex.IsMatch(processName, @"^[A-Za-z0-9][A-Za-z0-9 ._-]*$",
                    RegexOptions.CultureInvariant) ||
                processName.EndsWith(" ", StringComparison.Ordinal) ||
                processName.EndsWith(".", StringComparison.Ordinal) ||
                passes < 2 || passes > 5 || pauseMs < 0 || pauseMs > 2000)
                throw new ArgumentException("Invalid process-gate parameters.");
            return Script.Replace("__PROCESS_NAME__", processName)
                .Replace("__PASSES__", passes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture))
                .Replace("__PAUSE_MS__", pauseMs.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }

        // Fixed product name: future production code MUST use this overload.
        internal static string BuildProductionReadOnlyWorkerGate()
        {
            return Build(ProductionProcessName, ProductionPasses,
                ProductionPauseMilliseconds);
        }

        // Pure, non-executing test seam. Not reachable by CLI or About;
        // a synthetic target cannot grant permission to install anything.
        internal static string BuildSyntheticReadOnlyWorkerGate(
            string syntheticProcessName, int passes, int pauseMs)
        {
            return Build(syntheticProcessName, passes, pauseMs);
        }
    }
}
