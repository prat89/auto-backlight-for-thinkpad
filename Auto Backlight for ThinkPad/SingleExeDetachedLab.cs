// PHASE30B7D6F_DETACHED_LIFECYCLE_LAB_ONLY_V1
// Intentionally synthetic. The spawned PowerShell program is not elevated,
// does not touch staging, does not launch an installer and cannot modify Git.
// This is NOT the production updater or an authorization mechanism.
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class Phase30B7D6FDetachedLab
    {
        internal const string Switch = "--update-detached-lab";
        private const string MonitorTemplate = "$ErrorActionPreference='Stop'\n$folder=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__DIR_BASE64__'))\n$systemPowerShell=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__PS_BASE64__'))\n$originProcessId=__ORIGIN_PID__\n$syntheticExitCode=__EXIT_CODE__\n$result=Join-Path $folder 'result.txt'\n$temp=Join-Path $folder 'result.tmp'\ntry {\n    $deadline=[DateTime]::UtcNow.AddSeconds(25)\n    $launcherExited=$false\n    while ([DateTime]::UtcNow -lt $deadline) {\n        if ($null -eq (Get-Process -Id $originProcessId -ErrorAction SilentlyContinue)) {\n            $launcherExited=$true\n            break\n        }\n        Start-Sleep -Milliseconds 100\n    }\n    if (-not $launcherExited) { throw 'Launcher did not exit within 25 seconds.' }\n    $childCommand='[Environment]::Exit(' + $syntheticExitCode + ')'\n    $childEncoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childCommand))\n    $psi=New-Object System.Diagnostics.ProcessStartInfo\n    $psi.FileName=$systemPowerShell\n    $psi.Arguments='-NoProfile -NonInteractive -EncodedCommand ' + $childEncoded\n    $psi.UseShellExecute=$false\n    $psi.CreateNoWindow=$true\n    $worker=[System.Diagnostics.Process]::Start($psi)\n    if ($null -eq $worker) { throw 'Fake worker could not start.' }\n    if (-not $worker.WaitForExit(20000)) {\n        try { $worker.Kill() } catch {}\n        throw 'Fake worker did not exit.'\n    }\n    $actualExitCode=[int]$worker.ExitCode\n    $worker.Dispose()\n    $body=\"LAUNCHER_GONE=TRUE`nFAKE_WORKER_EXIT_CODE=$actualExitCode`nMSI_EXECUTED=FALSE`nUAC_ATTEMPTED=FALSE`n\"\n    [IO.File]::WriteAllText($temp,$body,([System.Text.UTF8Encoding]::new($false)))\n    [IO.File]::Move($temp,$result)\n} catch {\n    $body='LAB_ERROR=' + $_.Exception.Message + \"`nMSI_EXECUTED=FALSE`nUAC_ATTEMPTED=FALSE`n\"\n    [IO.File]::WriteAllText($temp,$body,([System.Text.UTF8Encoding]::new($false)))\n    [IO.File]::Move($temp,$result)\n    exit 1\n}\n";

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return identity != null &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        internal static int Run(string[] args)
        {
            if (args == null || args.Length != 3 ||
                !String.Equals(args[0], Switch, StringComparison.Ordinal)) return 10;
            Guid id;
            if (args[1].Length != 32 ||
                !Guid.TryParseExact(args[1], "N", out id) ||
                id == Guid.Empty ||
                !String.Equals(id.ToString("N"), args[1], StringComparison.Ordinal)) return 10;
            int code;
            if (!Int32.TryParse(args[2], out code) ||
                !(code == 0 || code == 3010 || code == 1602 || code == 1603 || code == 1618) ||
                !String.Equals(code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    args[2], StringComparison.Ordinal)) return 10;
            if (IsElevated()) return 13; // Lab runs only in an ordinary user session.

            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(ps) || (File.GetAttributes(ps) & FileAttributes.ReparsePoint) != 0)
                    return 20;
                string downloads = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string root = Path.Combine(downloads, "LenovoBacklightStage", "Phase30B7D6F_DetachedLab");
                if (Directory.Exists(root) &&
                    (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return 21;
                Directory.CreateDirectory(root);
                string folder = Path.Combine(root, "run_" + id.ToString("N"));
                // Refuse reuse; this is a one-time diagnostic, never a production path.
                if (Directory.Exists(folder) || File.Exists(folder)) return 22;
                Directory.CreateDirectory(folder);
                string script = MonitorTemplate
                    .Replace("__DIR_BASE64__", Convert.ToBase64String(Encoding.UTF8.GetBytes(folder)))
                    .Replace("__PS_BASE64__", Convert.ToBase64String(Encoding.UTF8.GetBytes(ps)))
                    .Replace("__ORIGIN_PID__", Process.GetCurrentProcess().Id.ToString(
                        System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("__EXIT_CODE__", code.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var info = new ProcessStartInfo
                {
                    FileName = ps,
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = sys,
                };
                using (Process monitor = Process.Start(info))
                {
                    if (monitor == null) return 23;
                }
                // IMPORTANT: do not wait here; the monitor must outlive this process.
                return 0;
            }
            catch (Exception)
            {
                return 24;
            }
        }
    }
}
