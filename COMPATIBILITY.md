# Hardware compatibility

## Currently implemented backends (unchanged in v1.3.8)

| Backend | Status | Notes |
| --- | --- | --- |
| `IBMPmDrv` | Implemented; hardware-tested | **Preferred**: initialized first, with a read-only GET probe; tested on the developer's ThinkPad |
| `EnergyDrv` v1/v2 | **Experimental; not hardware-tested** | First fallback after IBMPmDrv initialization fails; read-only GET `0x32` recognition and SET/GET verification |
| `EnergyDrv` guarded capability variant | **Experimental; not hardware-tested** | Tried only if legacy EnergyDrv is not recognized; read-only capability GET `0x01` must match the supported 3-state signature, followed by status GET `0x22`; SET `0x23` is verified with GET |
| Other Lenovo ACPI / WMI / RGB protocols | Not implemented | Neither EnergyDrv variant guarantees support for every ThinkBook, IdeaPad, Yoga or Legion; no RGB support |

The application does not bundle or install Lenovo drivers. **No EnergyDrv-equipped laptop has yet been tested with this fork.** A recognized protocol response does not prove that the physical keyboard LEDs respond as intended.

## Verified hardware

| Laptop | Machine type / model | OS | Driver | Levels | Verification |
| --- | --- | --- | --- | --- | --- |
| ThinkPad X1 Extreme Gen 2 | `20QVCTO1WW` | Windows 11 | `IBMPmDrv` running | Off / Low / High | Developer-tested through v1.3.8: direct v1.3.7 → v1.3.8 MSI upgrade, verified MSI EXE payload, keyboard lighting, optional camera brightness and exposure restoration, and restart/autostart; earlier v1.3.6 → v1.3.7 and v1.3.4 → v1.3.5 upgrades, input, diagnostics and OSD |

### Community-reported hardware (limited verification)

| Laptop | Report | Details still needed |
| --- | --- | --- |
| ThinkPad P14s Gen 5 AMD | One user reported the application working | Machine type, Windows build, Lenovo driver, tested brightness levels and power transitions |

The developer-verified table remains limited to the X1 Extreme Gen 2. A community report is not equivalent to comprehensive hardware or MSI validation.

## v1.3.8 dependency maintenance (Phase 28)

- **Hardcodet.NotifyIcon.Wpf:** updated from 1.0.8 to 2.0.1; the maintainer tested opening the system-tray menu in the rebuilt EXE.
- **OxyPlot:** Core, Wpf and Wpf.Shared updated from 2.0.0 to 2.2.0. Calibration now uses native `PlotView`/`PlotModel`; incompatible `OxyPlot.Contrib.Wpf` was removed. The maintainer tested opening Calibration and adding, moving and removing points.
- **Accord.NET:** all six package entries remain at 3.8.0. A targeted webcam-exposure restoration fix was tested locally with the camera brightness option. Compatibility with other webcams/exposure drivers is not guaranteed.
- **Hardware backends:** no changes to IBMPmDrv or experimental EnergyDrv. Verified model coverage remains unchanged.
- **Final v1.3.8 acceptance (2 November 2026):** the developer successfully upgraded from installed v1.3.7 using the rebuilt v1.3.8 MSI. The executable extracted from that MSI matched the tested Release EXE (SHA-256 `45510B15D235E5FD352D5BBF38922FD3C83E7E24C51CC0877273829A3406ACF8`). `Assembly.GetTypes()` without a resolver loaded all 119 application types. Keyboard illumination, optional camera-driven screen brightness, exposure restoration, and startup after reboot were reported working. The installed EXE hash was not separately recorded for v1.3.8.

## For a new model / EnergyDrv testers

1. Confirm the original Lenovo driver for your laptop is installed normally. **Do not** install unrelated third-party kernel drivers, disable Windows security or change driver service states just to test this app.
2. Run the application; use **Diagnostics...** to confirm the selected backend. IBMPmDrv is attempted first. EnergyDrv v1/v2 detection is tried before the guarded variant, which requires an exact read-only capability response and recognized brightness status.
3. If EnergyDrv is selected, cautiously check Off / Low / High against the actual keyboard LEDs, then automatic inactivity behavior and Fn+Space. **EnergyDrv has no hardware-validation claim**; stop testing if unexpected behavior occurs.
4. Check built-in keyboard, touchpad/TrackPoint (when present), external input devices (when opted in), lid close/open, and sleep/resume.
5. Report the exact laptop model, Windows build, selected backend, protocol variant shown in logs, and results; attach redacted **Diagnostics...** / **Export log...** output.

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

## v1.3.6 developer verification (1 November 2026)

- **Phase 25C:** replaced diagnostic WMI driver-service discovery with read-only Windows Service Control Manager enumeration; the tested IBMPmDrv path remains the preferred hardware backend.
- **Phase 25E.3:** added startup-only Raw Input enumeration and constructor timing logs. This is diagnostic instrumentation, not a claim that sporadic slow Windows-logon startup has been fixed.
- **Phase 25F rollback:** removed the experimental Task Scheduler logon delay and restored automatic `hardware-report.txt` generation on application startup; manual **Diagnostics...** export remains available.
- **Phase 26:** added experimental EnergyDrv v1/v2 read-only protocol recognition and optional GET/SET backend, attempted only after IBMPmDrv initialization fails. Protocol-level test passed without hardware IOCTLs; **EnergyDrv hardware control has not been tested**.
- Developer reports successful v1.3.6 source build, uninstall/reinstall of a new MSI and functioning IBMPmDrv keyboard backlight on X1 Extreme Gen 2. Diagnostics report IBMPmDrv selected, EnergyDrv absent, and Off/Low/High capability. **No verified cross-driver compatibility or v1.3.5 → v1.3.6 in-place upgrade is claimed**.

## v1.3.7 developer verification (1 November 2026)

- **Phase 27:** added a guarded three-state EnergyDrv protocol variant following the existing v1/v2 fallback. It first queries capabilities (`0x01`), requires the exact supported response `0x00000005`, then uses GET `0x22`. SET `0x23` is permitted only after successful detection and is checked by reading back the resulting state.
- Recognized states are limited to Off / Low / High. Unknown capability responses, unknown states and the legacy v2 Auto mode are rejected rather than guessing hardware commands; the v1.3.6 protocol stays first in the EnergyDrv detection order.
- The standalone protocol test passed on Windows/.NET without sending any hardware IOCTLs. The developer reports a successful **Release rebuild and direct v1.3.6 → v1.3.7 MSI upgrade**, with the application responding normally to keyboard, touchpad and mouse input on a ThinkPad X1 Extreme Gen 2 using IBMPmDrv.
- **Not yet validated:** physical EnergyDrv devices or EnergyDrv Fn+Space/brightness behavior. The v1.3.6 → v1.3.7 in-place MSI upgrade was successful on one ThinkPad; this is not an exhaustive cross-device upgrade matrix.
- No Lenovo Vantage runtime dependency, Lenovo Lighting WMI backend or RGB control was added.

## v1.3.8 MSI regression and camera validation (2 November 2026)

- The pre-fix v1.3.8 EXE failed `Assembly.GetTypes()` with `ReflectionTypeLoadException` because `WebCam.Cam` contained a field typed as `Accord.Video.DirectShow.CameraControlFlags`. The class is inspected by Windows Installer before the application entry point can register its embedded-DLL resolver.
- The fixed EXE stores the saved flag value as `int`, converts it to the enum only when restoring camera exposure, and loads all 119 application types without an assembly resolver.
- A direct v1.3.7 → v1.3.8 MSI upgrade succeeded on the verified ThinkPad, with working keyboard backlight, camera-driven brightness, exposure restoration and Windows restart startup. Testing does not establish compatibility with all webcam exposure drivers, EnergyDrv hardware or every legacy settings combination.

## Known limits

- **EnergyDrv is experimental.** Only recognized legacy v1/v2 replies or the exact guarded three-state capability/status replies are supported. Unknown/Auto responses are rejected without exploratory writes. Other EnergyDrv variants, Lenovo Lighting WMI protocols and RGB control are not claimed.
- An installed driver or compatible laptop family name does **not** guarantee physical keyboard lighting works.
- Startup after Windows logon may still take longer than a normal manual launch; the startup task has **no intentional delay**.
- Brightness levels depend on the selected backend; Off/Low/High for EnergyDrv are not yet physically verified.
- Camera-based screen brightness is optional, disabled by default for new installations, and separate from keyboard control.
- The successful v1.3.7 → v1.3.8 MSI upgrade validates the tested machine and path only; clean installs, uninstall/reinstall and other upgrade paths were not comprehensively retested for v1.3.8.

## Additional validation notes (27 October 2026)

- **Verified machine only:** ThinkPad X1 Extreme Gen 2 (`20QVCTO1WW`), Windows 11, running `IBMPmDrv`; observed Off (0), Low (1), High (2) control, Raw Input handling, external input option, report export and lid-event recovery. These are not claims about other ThinkPad generations.
- **MSI tests:** clean install/reinstall and in-place upgrade of the original `1.0.0` MSI to fork `1.2.0` succeeded on this machine. The original app's missing tray icon was a known upstream symptom. During one upgraded launch the new app initialized its tray icon about 22 seconds after process start; startup delay remains under investigation.
- **Not verified comprehensively:** `EnergyDrv` hardware commands, unavailable-driver behavior across all systems, every suspend/resume sequence, every legacy settings combination, and community-reported models beyond the developer-tested ThinkPad.
- **Historical note (v1.3.0):** a separate v1.2.0 → v1.3.0 MSI upgrade test was not documented. This is retained for test history, not as an instruction to delay the already published v1.3.4 release.
