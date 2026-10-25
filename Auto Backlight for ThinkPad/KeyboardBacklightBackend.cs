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

    // Only IBMPmDrv is supported in v5.0. Add other backend implementations
    // and probe them here in future releases; do not probe by writing levels.
    public static class KeyboardBacklightBackendFactory
    {
        public static IKeyboardBacklightBackend Create()
        {
            DiagnosticsLog.Info("Backlight backend selection: trying IBMPmDrv");
            // Constructor performs the same read-only hardware initialization
            // as the v4.3 controller. Errors propagate to existing isolation.
            IKeyboardBacklightBackend backend = new Backlight();
            DiagnosticsLog.Info("Backlight backend selected: IBMPmDrv; max level=" + backend.Limit);
            return backend;
        }
    }
}
