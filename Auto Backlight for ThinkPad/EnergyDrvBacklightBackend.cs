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
            // No SET occurs during discovery. A recognized GET is mandatory.
            uint raw = SendGet();
            int level;
            string variant;
            if (!EnergyDrvProtocol.TryDecode(raw, out level, out variant))
                throw new NotSupportedException("Unrecognized EnergyDrv GET status 0x" + raw.ToString("X8") +
                    "; no write attempted (Auto mode and unknown firmware are unsupported).");
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

        public int Read()
        {
            int level;
            string variant;
            uint raw = SendGet();
            if (!EnergyDrvProtocol.TryDecode(raw, out level, out variant) || variant != _variant)
                throw new InvalidOperationException("EnergyDrv GET returned unsupported/mismatched status: 0x" +
                    raw.ToString("X8") + " (expected " + _variant + ")");
            State = level;
            return level;
        }

        public void Write(int level)
        {
            uint command = EnergyDrvProtocol.SetCommand(level);
            SendCommand(command, false);
            // SaltwaterC verifies a SET after a short delay; don't assume success.
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
                uint raw = SendGet();
                int level;
                string variant;
                bool supported = EnergyDrvProtocol.TryDecode(raw, out level, out variant);
                return "GET operation: succeeded" + Environment.NewLine +
                    "Raw GET response: 0x" + raw.ToString("X8") + Environment.NewLine +
                    "Recognized EnergyDrv variant: " + (supported ? variant : "no") + Environment.NewLine +
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

        private static uint SendGet()
        {
            byte[] output;
            int returned;
            SendCommand(EnergyDrvProtocol.GetCommand, true, out output, out returned);
            if (returned < 4)
                throw new InvalidOperationException("EnergyDrv GET returned fewer than 4 bytes.");
            return BitConverter.ToUInt32(output, 0);
        }

        private static void SendCommand(uint input, bool isGet)
        {
            byte[] ignored;
            int returned;
            SendCommand(input, isGet, out ignored, out returned);
        }

        private static void SendCommand(uint input, bool isGet, out byte[] output, out int returned)
        {
            using (SafeFileHandle handle = CreateFile(DevicePath, GenericReadWrite, ShareReadWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open EnergyDrv");
                byte[] data = BitConverter.GetBytes(input);
                output = new byte[16]; // Required by known EnergyDrv implementations.
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
