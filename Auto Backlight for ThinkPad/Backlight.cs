/*
 * Copyright 2019 Parth Patel
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Auto_Backlight_for_ThinkPad
{
    /// <summary>
    /// Event args used for <see cref="BacklightEventHandler"/>
    /// </summary>
    public class BacklightEventArgs : EventArgs
    {
        /// <summary>
        /// The new state of the backlight after the event (ex. 0/1/2)
        /// </summary>
        public int State { get; }

        public BacklightEventArgs(int state)
        {
            State = state;
        }
    }
    /// <summary>
    /// Event handler for processing a change in backlight state as in <see cref="Backlight.Changed"/>
    /// </summary>
    /// <param name="sender"><see cref="Backlight"/> object responsible for raising the event</param>
    /// <param name="e">Event args containing the new state</param>
    public delegate void BacklightEventHandler(object sender, BacklightEventArgs e);

    /// <summary>
    /// Friendly read/write access to keyboard backlight
    /// </summary>
    public class Backlight : IKeyboardBacklightBackend
    {
        /// <summary>
        /// Raised when the backlight state has been changed by keyboard or by <see cref="Write(int)"/>
        /// </summary>
        public event BacklightEventHandler Changed;
        /// <summary>
        /// Begin background processing
        /// </summary>
        public void Start() => Enabled = true;
        /// <summary>
        /// End background processing
        /// </summary>
        public void Stop() => Enabled = false;
        /// <summary>
        /// Get/set the enabled state, same as Start()/Stop()
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    if (value)
                    {
                        _finish.Reset();
                        _loopThread = new Thread(_MonitorThread);
                        _loopThread.Start();
                    }
                    else
                    {
                        _finish.Set();
                        _loopThread.Join();
                    }
                }
            }
        }
        /// <summary>
        /// Get the last known state (ex. 0/1/2) of the backlight recorded by last <see cref="Read">/<see cref="Write(int)"/>.
        /// </summary>
        public int State { get; private set; }
        /// <summary>
        /// Maximum brightness level supported by this keyboard (ex. 2 --> 0/1/2 supported states)
        /// </summary>
        public int Limit { get; private set; }
        /// <summary>
        /// Update backlight with a new state
        /// </summary>
        /// <param name="st">The state to be written (ex. 0/1/2) </param>
        public void Write(int st)
        {
            int safeLevel = _clamp(st);
            if (!_SetKeyboardBackLightStatus(safeLevel))
            {
                DiagnosticsLog.Info("Keyboard backlight write failed (IBMPMDRV)");
                throw new Exception("Error accessing Keyboard driver");
            }
            State = safeLevel;
        }
        /// <summary>
        /// Query backlight for cuurent state
        /// </summary>
        /// <returns>The state read from keyboard, range 0 to Limit</returns>
        public int Read()
        {
            int st;
            if (!_GetKeyboardBackLightStatus(out st))
                throw new Exception("Error accessing Keyboard driver");
            State = _clamp(st);
            return State;
        }
        /// <summary>
        /// Create a new instance, automatically queries for <see cref="State"/> and <see cref="Limit"/>
        /// </summary>
        public Backlight()
        {
            int st, lm;
            if (!_GetKeyboardBackLightStatus(out st))
                throw new Exception("Error accessing Keyboard driver");
            if (!_GetKeyboardBackLightLevel(out lm))
                throw new Exception("Error accessing Keyboard driver");
            State = st;
            Limit = lm;
            DiagnosticsLog.Info("IBMPMDRV keyboard backlight detected; reported maximum level=" + Limit);

            // Background monitor thread setup
            _dispatcher = Dispatcher.CurrentDispatcher;
            _finish = new EventWaitHandle(false, EventResetMode.ManualReset);
        }

        private bool _enabled = false;
        private EventWaitHandle _finish;
        private Thread _loopThread;
        private Dispatcher _dispatcher;

        private void _MonitorThread()
        {
            try
            {
            using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset))
            {
                using (var notifyKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\IBMPMSVC\Parameters\Notification"))
                {
                    if (notifyKey == null)
                    {
                        DiagnosticsLog.Info("IBMPMSVC registry notification key missing; monitor disabled");
                        return;
                    }
                    object initialValue = notifyKey.GetValue(null);
                    if (!(initialValue is int))
                    {
                        DiagnosticsLog.Info("IBMPMSVC notification value invalid; monitor disabled");
                        return;
                    }
                    uint val = (uint)(int)initialValue;
                    int ret = RegNotifyChangeKeyValue(notifyKey.Handle.DangerousGetHandle(), false, 4, ev.SafeWaitHandle.DangerousGetHandle(), true);
                    if (ret != 0)
                        throw new Exception("RegNotifyChangeKeyValue failed");

                    while (true)
                    {
                        int which = WaitHandle.WaitAny(new WaitHandle[] { ev, _finish });
                        if (which == 1) break;

                        // Capture value, re-register for notification event
                        uint oldVal = val;
                        val = (uint)(int)notifyKey.GetValue(null);
                        ret = RegNotifyChangeKeyValue(notifyKey.Handle.DangerousGetHandle(), false, 4, ev.SafeWaitHandle.DangerousGetHandle(), true);
                        if (ret != 0)
                            throw new Exception("RegNotifyChangeKeyValue failed");

                        // Check notification reason, bit.17 should flip; invoke event on parent thread
                        if (((oldVal ^ val) >> 17) == 1)
                            _dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    int currentLevel = Read();
                                    Changed?.Invoke(this, new BacklightEventArgs(currentLevel));
                                }
                                catch (Exception ex)
                                {
                                    DiagnosticsLog.Error("Keyboard backlight change event failed", ex);
                                }
                            });
                    }
                }
            }
            }
            catch (Exception ex)
            {
                DiagnosticsLog.Error("Registry notification monitoring failed", ex);
            }
        }
        private int _clamp(int st)
        {
            if (st < 0) st = 0;
            if (st > Limit) st = Limit;
            return st;
        }
        private bool _GetKeyboardBackLightStatus(out int Status)
        {
            try
            {
                int code = _CallPmService(2238080, 0);
                if ((code & 0x0050000) == 0x0050000)
                {
                    Status = code & 0xF;
                    return true;
                }
                else throw new Exception("Backlight hw not ready");
            }
            catch (Exception)
            {
                Status = 0;
                return false;
            }
        }
        private bool _GetKeyboardBackLightLevel(out int Level)
        {
            try
            {
                int code = _CallPmService(2238080, 0);
                if ((code & 0x0050000) == 0x0050000)
                {
                    Level = (code >> 8) & 0xF;
                    return true;
                }
                else throw new Exception("Backlight hw not ready");
            }
            catch (Exception)
            {
                Level = 0;
                return false;
            }
        }
        private bool _SetKeyboardBackLightStatus(int Status)
        {
            try
            {
                int code = _CallPmService(2238080, 0);
                if ((code & 0x0050000) == 0x0050000)
                {
                    int arg = ((code & 0x00200000) != 0 ? 0x100 : 0) | (code & 0xF0) | Status;
                    int ret = _CallPmService(2238084, arg);
                    return ret == 0;
                }
                else throw new Exception("Backlight hw not ready");
            }
            catch (Exception)
            {
                return false;
            }
        }
        private int _CallPmService(uint code, int input)
        {
            IntPtr h = CreateFile(@"\\.\IBMPmDrv", 0x80000000, 1, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == new IntPtr(-1) || h == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Opening IBMPmDrv failed");
            try
            {
                uint bytesRet;
                NativeOverlapped overlapped = default;
                byte[] inp = BitConverter.GetBytes(input);
                byte[] outp = new byte[sizeof(int)];
                bool ok = DeviceIoControl(h, code, inp, (uint)inp.Length, outp, (uint)outp.Length,
                    out bytesRet, ref overlapped);
                if (!ok)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "IBMPmDrv DeviceIoControl failed");
                if (bytesRet < sizeof(int))
                    throw new InvalidOperationException("Incomplete IBMPmDrv response");
                return BitConverter.ToInt32(outp, 0);
            }
            finally
            {
                if (!CloseHandle(h))
                    DiagnosticsLog.Info("Warning: CloseHandle failed; Win32=" + Marshal.GetLastWin32Error());
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3), In] byte[] lpInBuffer, uint nInBufferSize, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 6), Out] byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, ref NativeOverlapped lpOverlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree, uint notifyFilter, IntPtr hEvent, bool asynchronous);
    }
}