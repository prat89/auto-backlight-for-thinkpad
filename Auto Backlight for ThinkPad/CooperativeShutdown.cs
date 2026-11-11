// Cooperative shutdown protocol for application update handoff.
// Future installed application can voluntarily exit on a same-account request.
// NO installer launch, command-line route, UAC, task mutation, or forced kill.
// The future privileged worker must verify the target product, process path,
// owner SID, session and process lifetime BEFORE signaling this endpoint.
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;

namespace Auto_Backlight_for_ThinkPad
{
    internal static class CooperativeShutdown
    {
        private static readonly object Sync = new object();
        private static EventWaitHandle _event;
        private static RegisteredWaitHandle _registration;
        private static int _requested;
        private const string Prefix = @"Local\ABTP.UpgradeExit.v1.";

        internal static string EventName(string sid, int session, int pid, long startedUtcTicks)
        {
            if (String.IsNullOrEmpty(sid) ||
                !Regex.IsMatch(sid, @"^S-1-[0-9]+(?:-[0-9]+)+$", RegexOptions.CultureInvariant) ||
                session < 0 || pid <= 0 || startedUtcTicks <= 0)
                throw new ArgumentException("Invalid process identity for update event.");
            return Prefix + sid + "." + session + "." + pid + "." + startedUtcTicks;
        }

        private static string MySid()
        {
            using (var id = WindowsIdentity.GetCurrent())
            {
                if (id == null || id.User == null)
                    throw new UnauthorizedAccessException("Windows SID unavailable.");
                return id.User.Value;
            }
        }

        // Called ONLY by the regular WPF application during Startup.
        internal static bool StartForApp(Action requestShutdown)
        {
            if (requestShutdown == null) throw new ArgumentNullException("requestShutdown");
            lock (Sync)
            {
                if (_event != null || _registration != null) return false;
                using (var self = Process.GetCurrentProcess())
                {
                    var sid = new SecurityIdentifier(MySid());
                    string name = EventName(sid.Value, self.SessionId, self.Id,
                        self.StartTime.ToUniversalTime().Ticks);
                    var security = new EventWaitHandleSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.SetOwner(sid);
                    security.AddAccessRule(new EventWaitHandleAccessRule(
                        sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                    bool created;
                    var handle = new EventWaitHandle(false, EventResetMode.ManualReset,
                        name, out created, security);
                    if (!created)
                    {
                        // Pre-created or duplicate event is not accepted.
                        handle.Dispose();
                        return false;
                    }
                    try
                    {
                        Interlocked.Exchange(ref _requested, 0);
                        _registration = ThreadPool.RegisterWaitForSingleObject(
                            handle,
                            (state, timedOut) =>
                            {
                                if (!timedOut && Interlocked.Exchange(ref _requested, 1) == 0)
                                {
                                    try { requestShutdown(); }
                                    catch (Exception ex)
                                    {
                                        DiagnosticsLog.Error("Cooperative update shutdown failed", ex);
                                    }
                                }
                            },
                            null, Timeout.Infinite, true);
                        _event = handle;
                        return true;
                    }
                    catch
                    {
                        handle.Dispose();
                        _registration = null;
                        throw;
                    }
                }
            }
        }

        // Called by the WPF Exit handler, before other resources are released.
        internal static void StopForApp()
        {
            lock (Sync)
            {
                if (_registration != null)
                {
                    _registration.Unregister(null);
                    _registration = null;
                }
                if (_event != null)
                {
                    _event.Dispose();
                    _event = null;
                }
            }
        }

        // Candidate helper for the future PRODUCTION worker; it is NOT exposed
        // as CLI or wired to any installation path in 6M.3F.1. A successful
        // signal is never treated as proof that the process actually exited.
        internal static string RequestForVerifiedPid(int pid)
        {
            if (pid <= 0) return "REJECT_INVALID_PID";
            if (TrustedUacBoundary.AuditSelf() != "PASS")
                return "REJECT_UNTRUSTED_CALLER";
            try
            {
                string sid = MySid();
                string expected = TrustedUacBoundary.ExpectedInstalledExe();
                using (var self = Process.GetCurrentProcess())
                using (var candidate = Process.GetProcessById(pid))
                {
                    if (pid == self.Id || candidate.SessionId != self.SessionId)
                        return "REJECT_PROCESS_IDENTITY";
                    long ticks = candidate.StartTime.ToUniversalTime().Ticks;
                    string actual = null, ownerSid = null;
                    int count = 0;
                    using (var lookup = new ManagementObjectSearcher(
                        "SELECT * FROM Win32_Process WHERE ProcessId=" + pid))
                    using (var results = lookup.Get())
                    {
                        foreach (ManagementObject item in results)
                        {
                            using (item)
                            {
                                ++count;
                                actual = Convert.ToString(item["ExecutablePath"]);
                                var owner = item.InvokeMethod("GetOwnerSid", null, null);
                                if (owner == null || Convert.ToInt32(owner["ReturnValue"]) != 0)
                                    return "REJECT_PROCESS_IDENTITY";
                                ownerSid = Convert.ToString(owner["Sid"]);
                            }
                        }
                    }
                    if (count != 1 || String.IsNullOrWhiteSpace(actual) ||
                        !String.Equals(Path.GetFullPath(actual), expected,
                            StringComparison.OrdinalIgnoreCase) ||
                        !String.Equals(ownerSid, sid, StringComparison.Ordinal))
                        return "REJECT_PROCESS_IDENTITY";
                    if (candidate.HasExited ||
                        candidate.StartTime.ToUniversalTime().Ticks != ticks)
                        return "REJECT_PROCESS_IDENTITY";
                    string name = EventName(sid, candidate.SessionId, pid, ticks);
                    try
                    {
                        using (var endpoint = EventWaitHandle.OpenExisting(
                            name, EventWaitHandleRights.Modify))
                        {
                            if (!endpoint.Set()) return "REJECT_SIGNAL_FAILED";
                        }
                        return "SIGNAL_SENT_WAIT_FOR_EXIT";
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        return "NOT_SUPPORTED_BY_TARGET";
                    }
                }
            }
            catch (Exception)
            {
                return "REJECT_UNVERIFIABLE_PROCESS";
            }
        }
    }
}
