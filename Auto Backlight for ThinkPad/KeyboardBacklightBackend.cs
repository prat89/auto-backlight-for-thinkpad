/*
 * Copyright 2019 Parth Patel
 * Licensed under the Apache License, Version 2.0 (the "License");
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Additional backend abstraction for the community-maintained fork.
 */
using System;

namespace Auto_Backlight_for_ThinkPad
{
    // Hardware-independent interface for the keyboard controller.
    public interface IKeyboardBacklightBackend
    {
        event BacklightEventHandler Changed;
        bool Enabled { get; set; }
        int State { get; }
        int Limit { get; }
        void Start();
        void Stop();
        int Read();
        void Write(int level);
    }

    // IBMPmDrv is preferred; EnergyDrv v1/v2 GET-only fallback is optional.
    public static class KeyboardBacklightBackendFactory
    {
        // Current selection is set only after successful read-only backend probing.
        internal static string ActiveBackendName { get; private set; } = "unavailable";
        private static IKeyboardBacklightBackend _selectedBackend;

        internal static string ReadOnlySelectedBackendProbe()
        {
            EnergyDrvBacklightBackend energy = _selectedBackend as EnergyDrvBacklightBackend;
            if (energy != null) return energy.ReadOnlyDriverProbe();
            return Backlight.ReadOnlyDriverProbe();
        }
        public static IKeyboardBacklightBackend Create()
        {
            // Diagnostic probe: Windows service state only; no IOCTL or write.
            var phase221Clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                foreach (string line in BackendDetection.GetReportLines())
                    DiagnosticsLog.Info(line);
            }
            finally
            {
                DiagnosticsLog.Info("Phase25C startup: SCM service discovery elapsed=" +
                    phase221Clock.ElapsedMilliseconds + "ms");
            }
            DiagnosticsLog.Info("Backlight backend selection: trying IBMPmDrv");
            // IBMPmDrv construction retains the proven ThinkPad GET-only probe.
            phase221Clock.Restart();
            IKeyboardBacklightBackend backend;
            try
            {
                // Preserve the existing, hardware-tested ThinkPad path first.
                // Only if it fails do a read-only EnergyDrv GET to choose v1 or v2.
                try
                {
                    backend = new Backlight();
                    ActiveBackendName = "IBMPmDrv";
                }
                catch (Exception ibmError)
                {
                    DiagnosticsLog.Error("IBMPmDrv initialization failed; testing EnergyDrv GET-only fallback", ibmError);
                    try
                    {
                        backend = new EnergyDrvBacklightBackend();
                        ActiveBackendName = "EnergyDrv";
                    }
                    catch (Exception energyError)
                    {
                        DiagnosticsLog.Error("EnergyDrv GET-only fallback rejected", energyError);
                        throw new InvalidOperationException("No supported Lenovo keyboard backlight backend found", energyError);
                    }
                }
            }
            finally
            {
                DiagnosticsLog.Info("Phase22.1 startup: IBMPmDrv backend construction elapsed=" +
                    phase221Clock.ElapsedMilliseconds + "ms");
            }
            DiagnosticsLog.Info("Backlight backend selected: " + ActiveBackendName + "; max level=" + backend.Limit);
            _selectedBackend = backend;
            return backend;
        }
    }
}
