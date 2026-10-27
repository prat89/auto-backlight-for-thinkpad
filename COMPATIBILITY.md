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
| ThinkPad X1 Extreme Gen 2 | `20QVCTO1WW` | Windows 11 | `IBMPmDrv` running | Off / Low / High | Developer-tested: startup, input recognition, backlight, diagnostics and report export |

The compatibility list deliberately does **not** claim support for other ThinkPad models without a real user test.

## For a new model

1. Confirm that the Lenovo `IBMPmDrv` power-management driver is installed and running.
2. Launch the app and verify that it starts without a hardware exception.
3. Test keyboard, touchpad, TrackPoint, and (if enabled) external input devices.
4. Test Low / High, inactivity timeout, lid close/open, sleep/resume, and Fn+Space.
5. Use **Diagnostic report...** to inspect/export diagnostic information, and review/redact it before sharing with maintainers.

If `IBMPmDrv` is absent, do **not** install random third-party kernel drivers or turn off Windows security protections just to try this program.

## Known limits

- EnergyDrv discovery in v5.1 only checks whether a Windows service exists. It does **not** validate EnergyDrv IOCTL compatibility.
- Brightness levels come from the reported backend capabilities and may differ across ThinkPad generations.
- Camera-based screen brightness is optional, disabled by default for new installations and not required for keyboard control.
- Clean installation, uninstall with the app closed, reinstall and preservation of saved settings were tested on the verified model (26 October 2026). **In-place MSI upgrade from upstream 1.0.0 to fork 1.2.0 passed on 27 October 2026**, including removal of the older product. Not every legacy settings value or transition was exhaustively tested.

## Additional validation notes (27 October 2026)

- **Verified machine only:** ThinkPad X1 Extreme Gen 2 (`20QVCTO1WW`), Windows 11, running `IBMPmDrv`; observed Off (0), Low (1), High (2) control, Raw Input handling, external input option, report export and lid-event recovery. These are not claims about other ThinkPad generations.
- **MSI tests:** clean install/reinstall and in-place upgrade of the original `1.0.0` MSI to fork `1.2.0` succeeded on this machine. The original app's missing tray icon was a known upstream symptom. During one upgraded launch the new app initialized its tray icon about 22 seconds after process start; startup delay remains under investigation.
- **Not verified:** `EnergyDrv` hardware commands, unavailable-driver behavior across all systems, every suspend/resume sequence, every legacy settings combination, and models other than the listed ThinkPad.
