// PHASE30B7D6M3F2_BOUNDED_SHUTDOWN_COORDINATOR_V1
// Future production launcher may call RequestAllVerifiedAndWait BEFORE
// spawning a detached MSI worker. No CLI/UI/MSI/UAC integration in 3F.2.
// A successful signal is NOT proof of termination: hold a Process handle,
// wait for real exit, then check again for remaining named instances.
// Crucial future requirement: detached worker MUST independently re-check
// process absence immediately before starting msiexec (race prevention).
using System;
using System.Diagnostics;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class ShutdownCoordinator
    {
        private const string AppProcessName = "Auto Backlight for ThinkPad";
        internal const int DefaultTimeoutMilliseconds = 12000;

        // Read-only method usable in synthetic tests. The process's lifetime
        // is pinned before the wait; an unrelated reused PID cannot pass.
        internal static string WaitForIdentifiedExitReadOnly(
            int pid, long startedUtcTicks, int timeoutMilliseconds)
        {
            if (pid <= 0 || startedUtcTicks <= 0 ||
                timeoutMilliseconds < 0 || timeoutMilliseconds > 30000)
                return "REJECT_INVALID_WAIT_ARGUMENTS";
            try
            {
                using (var candidate = Process.GetProcessById(pid))
                {
                    if (candidate.StartTime.ToUniversalTime().Ticks != startedUtcTicks)
                        return "REJECT_PID_LIFETIME_MISMATCH";
                    return candidate.WaitForExit(timeoutMilliseconds)
                        ? "EXIT_CONFIRMED" : "WAIT_TIMEOUT";
                }
            }
            catch (ArgumentException) { return "REJECT_PID_NOT_FOUND"; }
            catch (Exception) { return "REJECT_PROCESS_UNVERIFIABLE"; }
        }

        // Not currently called by the MSI executor or UI. It is deliberately
        // available only to a caller whose own installed EXE passes the 6M.2
        // trusted Program Files / MSI registration / ACL audit.
        internal static string RequestAllVerifiedAndWait(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds < 1000 || timeoutMilliseconds > 30000)
                return "REJECT_INVALID_TIMEOUT";
            if (TrustedUacBoundary.AuditSelf() != "PASS")
                return "REJECT_UNTRUSTED_CALLER";
            Process[] candidates = null;
            try
            {
                using (var self = Process.GetCurrentProcess())
                {
                    candidates = Process.GetProcessesByName(AppProcessName);
                    int stopped = 0;
                    foreach (var candidate in candidates)
                    {
                        if (candidate.Id == self.Id) continue;
                        // Never signal another user's/session's process or
                        // unidentified process: 3F.1 validates SID, session,
                        // canonical installed path and start-time identity.
                        if (candidate.HasExited) continue;
                        long started = candidate.StartTime.ToUniversalTime().Ticks;
                        string status = CooperativeShutdown
                            .RequestForVerifiedPid(candidate.Id);
                        if (status != "SIGNAL_SENT_WAIT_FOR_EXIT")
                            return "REJECT_SIGNAL_" + status;
                        // Do not infer exit from successful Set(); the kernel
                        // process handle is the actual completion signal.
                        if (!candidate.WaitForExit(timeoutMilliseconds))
                            return "REJECT_SHUTDOWN_TIMEOUT";
                        // The same Process handle cannot be satisfied by PID reuse.
                        if (!candidate.HasExited ||
                            candidate.StartTime.ToUniversalTime().Ticks != started)
                            return "REJECT_UNVERIFIABLE_EXIT";
                        ++stopped;
                    }
                    // Detect fresh/restarted same-name process before returning.
                    using (var check = new RemainingProcessAudit(self.Id))
                    {
                        if (check.AnyRemaining) return "REJECT_REMAINING_INSTANCES";
                    }
                    // Neither result authorizes MSI: the independent worker must
                    // repeat the absence and package checks under admin ACLs.
                    return stopped == 0 ? "NO_TARGETS_OBSERVED" :
                        "ALL_TARGETS_EXITED_" + stopped;
                }
            }
            catch (Exception) { return "REJECT_UNVERIFIABLE_TARGETS"; }
            finally
            {
                if (candidates != null)
                    foreach (var p in candidates) p.Dispose();
            }
        }

        private sealed class RemainingProcessAudit : IDisposable
        {
            private readonly Process[] _processes;
            internal bool AnyRemaining { get; private set; }
            internal RemainingProcessAudit(int callerPid)
            {
                _processes = Process.GetProcessesByName(AppProcessName);
                foreach (var candidate in _processes)
                    if (candidate.Id != callerPid && !candidate.HasExited)
                        AnyRemaining = true;
            }
            public void Dispose()
            {
                foreach (var p in _processes) p.Dispose();
            }
        }
    }
}
