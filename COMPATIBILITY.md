# Hardware compatibility

## Currently implemented backend

| Backend | Status | Notes |
| --- | --- | --- |
| `IBMPmDrv` | Implemented | Requires compatible Lenovo power-management driver and ThinkPad hardware |
| `EnergyDrv` | Not implemented | Service discovery is informational only; no EnergyDrv control commands are sent |
| Other ACPI / WMI / RGB backends | Not implemented | No claim of support for Yoga, IdeaPad, ThinkBook or Legion |

## Verified hardware

| Laptop | Machine type / model | OS | Driver | Levels | Verification |
| --- | --- | --- | --- | --- | --- |
| ThinkPad X1 Extreme Gen 2 | `20QVCTO1WW` | Windows 11 | `IBMPmDrv` running | Off / Low / Full | Developer-tested: startup, input recognition, backlight, diagnostics and report export |

The compatibility list deliberately does **not** claim support for other ThinkPad models without a real user test.

## For a new model

1. Confirm that the Lenovo `IBMPmDrv` power-management driver is installed and running.
2. Launch the app and verify that it starts without a hardware exception.
3. Test keyboard, touchpad, TrackPoint, and (if enabled) external input devices.
4. Test Low / Full, timeout, sleep/resume, and Fn+Space.
5. Export the diagnostic report, and review/redact it before sharing with maintainers.

If `IBMPmDrv` is absent, do **not** install random third-party kernel drivers or turn off Windows security protections just to try this program.

## Known limits

- EnergyDrv discovery in v5.1 only checks whether a Windows service exists. It does **not** validate EnergyDrv IOCTL compatibility.
- Brightness levels come from the reported backend capabilities and may differ across ThinkPad generations.
- Camera-based screen brightness is optional, disabled by default for new installations and not required for keyboard control.
- Installer upgrades, unavailable drivers and all sleep/resume combinations require additional hardware validation.
