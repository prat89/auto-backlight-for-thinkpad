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
| ThinkPad X1 Extreme Gen 2 | `20QVCTO1WW` | Windows 11 | `IBMPmDrv` running | Off / Low / High | Developer-tested: v1.3.4 → v1.3.5 MSI upgrade, internal/Bluetooth input, inactivity timeout, Raw Input diagnostics, Lenovo OSD preference, sleep/resume and restart/autostart |

### Community-reported hardware (limited verification)

| Laptop | Report | Details still needed |
| --- | --- | --- |
| ThinkPad P14s Gen 5 AMD | One user reported the application working | Machine type, Windows build, Lenovo driver, tested brightness levels and power transitions |

The developer-verified table remains limited to the X1 Extreme Gen 2. A community report is not equivalent to comprehensive hardware or MSI validation.

## For a new model

1. Confirm that the Lenovo `IBMPmDrv` power-management driver is installed and running.
2. Launch the app and verify that it starts without a hardware exception.
3. Test keyboard, touchpad, TrackPoint, and (if enabled) external input devices.
4. Test Low / High, inactivity timeout, lid close/open, sleep/resume, and Fn+Space.
5. Use **Diagnostics...** to inspect/export diagnostic information, and review/redact it before sharing with maintainers.

If `IBMPmDrv` is absent, do **not** install random third-party kernel drivers or turn off Windows security protections just to try this program.

## Lenovo OSD option (v1.3.4)

- **Disable on-screen display** is available when `HKCU\Software\Lenovo\OnScreenDisplay` exists and `Enable` is either a valid DWORD (`0` or `1`) or absent.
- A missing `Enable` is **not created automatically on startup**. An explicit toggle can create `Enable=0` (`REG_DWORD`) to suppress Lenovo OSD; toggling the option off writes `Enable=1`.
- If the key is absent or the existing value is invalid, the app does not silently overwrite it.
- The option controls Lenovo's visual indicators, not the keyboard backlight hardware or the app's inactivity control. The preference is stored separately from legacy serialized settings.
- On 30 October 2026, the maintainer tested the missing-value path and observed registry transitions `0 → 1 → 0` on X1 Extreme Gen 2. A ThinkPad P14s Gen 5 AMD community tester reported success after manually creating `Enable`; this is not yet validation of the missing-value path on that machine.
- Support depends on the actual Lenovo OSD implementation. The key or DWORD alone does not guarantee the Lenovo overlay will respond.

## v1.3.5 developer verification (31 October 2026)

- **Phase 22:** read-only Raw Input report counts enumerations, eligible devices and device-added/removed callbacks. Callback totals also include enumeration rebuilds; they do not necessarily represent actual device arrivals/removals.
- **Phase 22.1:** separate `WM_INPUT_DEVICE_CHANGE` arrival/removal/error counters and additional startup timing logs for WMI service discovery, backend construction, input capture and keyboard controller setup. The profiling is diagnostic-only; slow startup remains under investigation.
- **Phase 23A–23C:** application and MSI version metadata checked; rebuilt EXE matched the EXE extracted from the v1.3.5 MSI byte-for-byte (SHA-256). MSI ProductCode/PackageCode changed; the UpgradeCode remained unchanged.
- **Phase 23D:** in-place MSI update from installed v1.3.4 to v1.3.5 passed on X1 Extreme Gen 2. The old product was no longer installed, the v1.3.5 EXE hash matched the verified MSI payload, and the Task Scheduler logon task pointed to the installed application.
- The binary `Settings` registry value was not byte-identical to the pre-upgrade baseline. Post-upgrade logs showed saved values loading successfully, and the maintainer verified timeout, brightness level, external-input and OSD preferences before/after restart. This supports **functional preference retention on the tested machine**, not exhaustive format-level compatibility.
- Passed local regression checks for built-in keyboard and external Bluetooth mouse activity, inactivity timeout, sleep/resume, and automatic launch/settings retention after Windows restart. No comprehensive cross-model, clean-v1.3.5-install or every-settings-combination test has been performed.

## Recent developer validation (30 October 2026)

- **Phase 20A:** read-only IBMPmDrv brightness GET probe reported valid values for Off, Low and High on X1 Extreme Gen 2. Experimental SET/GET tests restored the initial setting; they are not part of the diagnostics workflow.
- **Phase 20C.1:** verified external Bluetooth mouse triggering of keyboard lighting after full Windows reboot with the new EXE installed through MSI; the installed EXE matched the local Release EXE by SHA-256. Hot-plug and resume checks were also performed locally.
- **Phase 21:** missing OSD `Enable` was not created on app startup; explicit user interactions created and toggled the DWORD. Existing OSD behavior was retained.
- **v1.3.4 released:** the maintainer reports successful local MSI installation and normal application operation on the verified ThinkPad. A complete v1.3.4 major-upgrade/uninstall regression matrix is not documented. Broader device coverage, EnergyDrv control, other Lenovo OSD revisions and exhaustive legacy settings/power-transition testing remain unverified.

## Known limits

- EnergyDrv discovery in v5.1 only checks whether a Windows service exists. It does **not** validate EnergyDrv IOCTL compatibility.
- Brightness levels come from the reported backend capabilities and may differ across ThinkPad generations.
- Camera-based screen brightness is optional, disabled by default for new installations and not required for keyboard control.
- Historical MSI verification: clean installation, uninstall with the app closed, reinstall and retention of saved settings were tested in October 2026. An in-place MSI upgrade from upstream 1.0.0 to fork 1.2.0 also passed on 27 October 2026. Those tests do not establish comprehensive upgrade coverage for v1.3.4.

## Additional validation notes (27 October 2026)

- **Verified machine only:** ThinkPad X1 Extreme Gen 2 (`20QVCTO1WW`), Windows 11, running `IBMPmDrv`; observed Off (0), Low (1), High (2) control, Raw Input handling, external input option, report export and lid-event recovery. These are not claims about other ThinkPad generations.
- **MSI tests:** clean install/reinstall and in-place upgrade of the original `1.0.0` MSI to fork `1.2.0` succeeded on this machine. The original app's missing tray icon was a known upstream symptom. During one upgraded launch the new app initialized its tray icon about 22 seconds after process start; startup delay remains under investigation.
- **Not verified comprehensively:** `EnergyDrv` hardware commands, unavailable-driver behavior across all systems, every suspend/resume sequence, every legacy settings combination, and community-reported models beyond the developer-tested ThinkPad.
- **Historical note (v1.3.0):** a separate v1.2.0 → v1.3.0 MSI upgrade test was not documented. This is retained for test history, not as an instruction to delay the already published v1.3.4 release.
