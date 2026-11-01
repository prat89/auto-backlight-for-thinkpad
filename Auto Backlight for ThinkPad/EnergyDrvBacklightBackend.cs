/*
 * EnergyDrv backend for Lenovo ThinkBooks / IdeaPads.
 * Protocol based on the MIT-licensed SaltwaterC and r00bertos1 projects;
 * both variants are selected by READ-ONLY GET. See NOTICE.
 * Hardware-specific functionality is not validated without an EnergyDrv laptop.
 */
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace Auto_Backlight_for_ThinkPad
{
    internal sealed class EnergyDrvBacklightBackend : IKeyboardBacklightBackend
    {
        private const string DevicePath = @"\\.\EnergyDrv";
        private const uint GenericReadWrite = 0xC0000000u;
        private const uint ShareReadWrite = 0x00000003u;
        private const uint OpenExisting = 3u;
        private readonly DispatcherTimer _monitor;
        private readonly string _variant;
        private readonly bool _guarded;
        private readonly uint _guard;
        private DateTime _lastMonitorErrorUtc = DateTime.MinValue;

        public event BacklightEventHandler Changed;
        public int Limit { get { return 2; } }
        public int State { get; private set; }
        public bool Enabled
        {
            get { return _monitor.IsEnabled; }
            set { if (value) Start(); else Stop(); }
        }

        public EnergyDrvBacklightBackend()
        {
            // Keep the v1.3.6 EnergyDrv GET(0x32) path first; no probing SET.
            int level = -1;
            string variant = null;
            uint raw = 0;
            try
            {
                raw = SendGet(EnergyDrvProtocol.GetCommand);
                EnergyDrvProtocol.TryDecode(raw, out level, out variant);
                // v2 Auto is a known state but intentionally unsupported.
                // Do not attempt to bypass this by switching to another protocol.
                if (raw == 0x00010007u)
                    throw new NotSupportedException("EnergyDrv v2 Auto mode is unsupported; no SET attempted.");
            }
            catch (Win32Exception ex)
            {
                DiagnosticsLog.Info("EnergyDrv legacy GET unavailable; guarded read-only capability probe follows (Win32 " + ex.NativeErrorCode + ")");
            }
            catch (InvalidOperationException ex)
            {
                DiagnosticsLog.Info("EnergyDrv legacy GET incomplete; guarded read-only capability probe follows (" + ex.GetType().Name + ")");
            }
            if (variant == null)
            {
                // Capability GET(0x01) MUST confirm two nonzero brightness levels.
                uint capability = SendGet(EnergyDrvProtocol.GuardedCapabilityCommand, 4);
                uint guard;
                if (!EnergyDrvProtocol.TryGuardedCapability(capability, out guard))
                    throw new NotSupportedException("EnergyDrv guarded capability unsupported: 0x" + capability.ToString("X8") + "; no SET attempted.");
                raw = SendGet(EnergyDrvProtocol.GuardedStatusCommand(guard), 4);
                if (!EnergyDrvProtocol.TryDecodeGuardedStatus(raw, out level))
                    throw new NotSupportedException("EnergyDrv guarded status unsupported: 0x" + raw.ToString("X8") + "; no SET attempted.");
                _guarded = true;
                _guard = guard;
                variant = "guarded-0x22";
            }
            _variant = variant;
            State = level;
            _monitor = new DispatcherTimer(DispatcherPriority.Background);
            _monitor.Interval = TimeSpan.FromMilliseconds(750);
            _monitor.Tick += MonitorTick;
            DiagnosticsLog.Info("EnergyDrv read-only probe matched " + variant + "; raw=0x" +
                raw.ToString("X8") + "; level=" + level);
        }

        public void Start() { _monitor.Start(); }
        public void Stop() { _monitor.Stop(); }

        private uint ReadRawState()
        {
            return _guarded
                ? SendGet(EnergyDrvProtocol.GuardedStatusCommand(_guard), 4)
                : SendGet(EnergyDrvProtocol.GetCommand);
        }

        private bool TryDecodeSelected(uint raw, out int level)
        {
            if (_guarded) return EnergyDrvProtocol.TryDecodeGuardedStatus(raw, out level);
            string variant;
            bool known = EnergyDrvProtocol.TryDecode(raw, out level, out variant);
            return known && variant == _variant;
        }

        public int Read()
        {
            uint raw = ReadRawState();
            int level;
            if (!TryDecodeSelected(raw, out level))
                throw new InvalidOperationException("EnergyDrv GET returned unsupported/mismatched status: 0x" +
                    raw.ToString("X8") + " (expected " + _variant + ")");
            State = level;
            return level;
        }

        public void Write(int level)
        {
            uint command = _guarded
                ? EnergyDrvProtocol.GuardedSetCommand(_guard, level)
                : EnergyDrvProtocol.SetCommand(level);
            SendCommand(command, false, _guarded ? 4 : 16);
            // Verify SET with a fresh GET. A failed read is treated as a failed write.
            Thread.Sleep(40);
            int actual = Read();
            if (actual != level)
                throw new InvalidOperationException("EnergyDrv SET verification mismatch: requested " +
                    level + ", got " + actual);
        }

        internal string ReadOnlyDriverProbe()
        {
            try
            {
                uint raw = ReadRawState();
                int level;
                bool supported = TryDecodeSelected(raw, out level);
                return "GET operation: succeeded" + Environment.NewLine +
                    "Raw GET response: 0x" + raw.ToString("X8") + Environment.NewLine +
                    "Recognized EnergyDrv variant: " + (supported ? _variant : "no") + Environment.NewLine +
                    "Decoded brightness level: " + (supported ? level.ToString() : "unavailable") + Environment.NewLine +
                    "SET verification: not performed (read-only diagnostics)";
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("EnergyDrv read-only diagnostics failed", ex);
                return "GET operation: failed (" + ex.GetType().Name + ")" + Environment.NewLine +
                    "SET verification: not performed (read-only diagnostics)";
            }
        }

        private void MonitorTick(object sender, EventArgs e)
        {
            try
            {
                int previous = State;
                int current = Read();
                if (current != previous)
                    Changed?.Invoke(this, new BacklightEventArgs(current));
            }
            catch (Exception ex)
            {
                DateTime now = DateTime.UtcNow;
                if (now - _lastMonitorErrorUtc >= TimeSpan.FromSeconds(30))
                {
                    _lastMonitorErrorUtc = now;
                    DiagnosticsLog.Error("EnergyDrv external backlight-change polling failed (throttled)", ex);
                }
            }
        }

        private static uint SendGet(uint command, int outputLength = 16)
        {
            byte[] output;
            int returned;
            SendCommand(command, true, outputLength, out output, out returned);
            if (returned < 4)
                throw new InvalidOperationException("EnergyDrv GET returned fewer than 4 bytes.");
            return BitConverter.ToUInt32(output, 0);
        }

        private static void SendCommand(uint input, bool isGet, int outputLength)
        {
            byte[] ignored;
            int returned;
            SendCommand(input, isGet, outputLength, out ignored, out returned);
        }

        private static void SendCommand(uint input, bool isGet, int outputLength, out byte[] output, out int returned)
        {
            using (SafeFileHandle handle = CreateFile(DevicePath, GenericReadWrite, ShareReadWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open EnergyDrv");
                byte[] data = BitConverter.GetBytes(input);
                output = new byte[outputLength];
                if (!DeviceIoControl(handle, EnergyDrvProtocol.Ioctl, data, data.Length,
                    output, output.Length, out returned, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "EnergyDrv DeviceIoControl " + (isGet ? "GET" : "SET") + " failed");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle handle, uint code,
            byte[] input, int inputLength, [Out] byte[] output, int outputLength,
            out int bytesReturned, IntPtr overlapped);
    }
}
