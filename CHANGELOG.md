# Changelog

## v1.4.0 — 11 November 2026 (release source prepared; acceptance pending)

### Changes
- Fix checksum normalization for persisted binary settings when assembly version strings change length (for example, `1.3.9.0` to `1.3.14.0`). Retains the original `Settings` registry bytes, checksum validation and existing settings schema; the recovery test is pending.
- Add a genuine **Install updates automatically** option in About, disabled by default and requiring a fresh opt-in. It runs the existing signed and verified MSI pipeline at application startup; Windows UAC is not bypassed.
- Remove redundant success popups after **Export log...** and **Diagnostics...**. Export failures still produce an error message.
- Replace internal historical milestone-style C# identifiers, source comments, private WPF resource keys and human-readable application log prefixes with descriptive names. Preserve worker protocol markers and compile-time MSI safety guards.
- Update assembly/MSI version to v1.4.0 and maintain the existing MSI UpgradeCode for major upgrades.

### Validation and known limitations
- Source changes and .NET Framework 4.8 Release Rebuild were checked during development. Final MSI install, settings recovery/persistence and automatic-install behavior require device-level tests before publication.
- The signed manual updater and application relaunch previously worked in the observed v1.3.13 → v1.3.14 upgrade. This is not a validation of unattended v1.4.0 auto-install.
- Intermittent missing tray icon after Windows logon is not fixed in this release because it could not be reliably reproduced.

## v1.3.10 — 8 November 2026 (candidate; not yet released)

### Updater end-to-end validation release
- Version-only release candidate to test the published v1.3.9 → v1.3.10 signed GitHub MSI update flow.
- No changes to updater execution, shutdown, installer logic, or supported hardware.
- **Validation pending:** public GitHub discovery, signature/SHA-256 checks, protected staging, consent, app shutdown, MSI outcome, settings and Scheduled Task preservation.
- Manual v1.3.8 → v1.3.9 MSI installation was reported to hang while the app was running; installation succeeded on retry after exit and cancellation. Root cause unconfirmed.



## v1.3.9 — 8 November 2026

### Secure GitHub MSI updater
- Added stable-release discovery in About, explicit signed MSI download, local review, protected staging, and an installation approval flow.
- Verify publisher-signed RSA manifest, SHA-256 digest, MSI metadata and installed-product identity; keep untrusted MSI bytes out of privileged execution paths.
- Added a trusted elevation boundary, cooperative shutdown, independent process checks, protected worker and durable installation reporting. Installation requires explicit user consent. Unattended auto-install remains disabled.
- The original upstream update/uninstall workflow remains disabled.

### Local validation
- 36 consolidated integration checks passed, including the Release WPF build and negative security checks.
- A controlled MSI upgrade from verified official v1.3.8 to v1.3.9 RC succeeded on the maintainer's ThinkPad; settings and Scheduled Task survived.
- The maintainer verified the installed RC's tray, keyboard backlight, built-in/external input detection and About window.
- **Limitation:** the complete interactive About → GitHub signed download → MSI execution workflow has not yet been tested end to end against a publicly published newer version. This is not a claim of fully validated unattended updates.
- No additional EnergyDrv hardware compatibility is claimed.

## v1.3.8 — 2 November 2026

### WPF and plotting dependency maintenance (Phases 28B, 28C.3)
- Updated `Hardcodet.NotifyIcon.Wpf` from 1.0.8 to 2.0.1, updating its embedded DLL reference and third-party notice. Tray menu behavior was validated locally in the rebuilt application.
- Updated `OxyPlot.Core`, `OxyPlot.Wpf` and `OxyPlot.Wpf.Shared` from 2.0.0 to 2.2.0. Migrated Calibration from the incompatible `OxyPlot.Contrib.Wpf` control to native `PlotView`/`PlotModel`, removing Contrib and retaining chart point editing.
- The interim OxyPlot 2.1.2 stage and unsuccessful mixed Contrib 2.1.2 / OxyPlot 2.2.0 experiment were development-only; v1.3.8 uses a consistent 2.2.0 runtime without Contrib.

### Camera exposure and compatibility (Phase 28D)
- Corrected restoration of saved webcam exposure settings for optional automatic screen brightness, including a best-effort restore on frame timeout.
- Fixed MSI Error 1001 (`ReflectionTypeLoadException`) during installer type discovery: store the original camera control flags as an `int` rather than a field of type `Accord.Video.DirectShow.CameraControlFlags`. The original flags are converted back to the Accord enum when restoring exposure, avoiding premature assembly resolution during `InstallUtil` reflection. Accord.NET remains embedded in the EXE.
- Retained Accord.NET 3.8.0 (six NuGet package entries) after an audit; no unverified replacement or camera framework upgrade.
- No changes to keyboard-backlight backend selection, experimental EnergyDrv implementation, or user-facing inactivity controls.

### Validation
- Maintainer reports successful Release EXE build and local functional tests for tray, Calibration point interactions and camera-based brightness.
- **Release acceptance passed on the developer-tested ThinkPad X1 Extreme Gen 2 (2 November 2026):** completed a direct MSI upgrade from installed v1.3.7 to v1.3.8 without Error 1001. The final MSI payload EXE matched the validated Release EXE (SHA-256 `45510B15D235E5FD352D5BBF38922FD3C83E7E24C51CC0877273829A3406ACF8`); `Assembly.GetTypes()` without a resolver loaded all 119 application types, including the installer class.
- Maintainer confirmed normal application operation, keyboard backlight, camera-based screen-brightness response to changing light, camera exposure restoration, and startup after a Windows restart. This is a single-device upgrade and functional test, not comprehensive clean-install, uninstall or cross-model coverage.
- EnergyDrv support remains experimental and not validated on physical EnergyDrv hardware.

## v1.3.7 — 1 November 2026

### Guarded experimental EnergyDrv protocol (Phase 27)
- Added a capability-gated alternative EnergyDrv three-state protocol in addition to the existing v1/v2 support. This provides a potential path for Lenovo models using GET `0x22` / SET `0x23`.
- Before activating the new variant, require a **read-only capabilities query** (`0x01`) with exactly the supported response `0x00000005`, then a recognized GET (`0x22`) brightness state (Off / Low / High). Unrecognized capabilities, states and unsupported Auto are rejected, with **no speculative SET during detection**.
- Existing `IBMPmDrv` remains the first-choice backend. Existing EnergyDrv v1/v2 recognition (GET `0x32`) is attempted before the new guarded variant. SET operations are verified by reading back the resulting brightness.
- Credited the protocol research reference in `NOTICE`. No Lenovo Vantage DLLs, new third-party driver, Lenovo Lighting WMI backend or RGB controller were added.

### Build and verification
- Updated assembly/file/informational versions to `1.3.7.0` / `1.3.7` and MSI ProductVersion to `1.3.7`; generated fresh MSI ProductCode and PackageCode while retaining the existing UpgradeCode.
- Standalone Windows/.NET protocol tests passed without accessing hardware, including capability/command mapping, readback decoding and rejection of unknown/Auto states.
- Maintainer reports a successful Release rebuild and direct v1.3.6 → v1.3.7 MSI upgrade, with normal IBMPmDrv keyboard-backlight behavior triggered by built-in keyboard, touchpad and mouse input on ThinkPad X1 Extreme Gen 2.
- **Not verified:** physical EnergyDrv hardware and broader Lenovo model compatibility. A direct v1.3.6 → v1.3.7 MSI upgrade was successfully tested on one ThinkPad, not across all configurations.

## v1.3.6 — 1 November 2026

### Driver detection and startup diagnostics (Phases 25C / 25E.3)
- Replaced diagnostic `Win32_SystemDriver` WMI enumeration with read-only Service Control Manager driver discovery and registry start-mode lookup. The proven IBMPmDrv hardware-control implementation remains unchanged.
- Added startup-only Raw Input enumeration timing, device-count and per-device timing summaries to investigate occasional slow initialization.
- Intermittent slow startup after Windows logon is **not claimed to be fixed**.

### Experimental EnergyDrv v1/v2 backend (Phase 26)
- Added an optional Lenovo `EnergyDrv` backend based on MIT-licensed open-source protocol implementations credited in `NOTICE`.
- **IBMPmDrv remains the first-choice backend**; only when it cannot initialize does the application attempt EnergyDrv.
- EnergyDrv variant selection requires a recognized **read-only GET** response. Unknown/unsupported responses are rejected without an exploratory SET; writes are checked by reading back the resulting level.
- Added polling for external/manual brightness changes on EnergyDrv and updated **Diagnostics...** to report the actually selected backend and a backend-specific read-only probe.
- **Experimental and not tested on actual EnergyDrv hardware.** Protocol-level checks passed without driver IOCTLs, but correct Off/Low/High behavior, Fn+Space interaction and model compatibility require community testing. No blanket ThinkBook/IdeaPad/Yoga/Legion support claim.

### Startup behavior and local regression
- Reverted the temporary Phase 25F 15-second logon delay experiment: the packaged scheduled task starts at logon **without an intentional delay**.
- Restored automatic hardware-report generation at application startup while retaining the manual **Diagnostics...** export.
- Updated assembly version to `1.3.6.0` and MSI ProductVersion to `1.3.6`. Developer reports a successful Release/MSI rebuild and uninstall/reinstall on ThinkPad X1 Extreme Gen 2, with working IBMPmDrv keyboard lighting; local diagnostic report confirms IBMPmDrv selection.
- A v1.3.5 → v1.3.6 **in-place** MSI upgrade, EnergyDrv hardware compatibility and comprehensive cross-device testing have **not** been verified.

## v1.3.5 — 31 October 2026

### Read-only Raw Input diagnostics (Phase 22)
- Added a diagnostic snapshot of Raw Input capture, including rescan/failure counts, last enumeration timestamp, eligible internal/external device counts, and device-added/removed callback counts.
- These counters are diagnostic only: no keystroke contents or device paths are stored in the snapshot. Device callback counts may include enumeration rebuilds; they must not be interpreted as physical hot-plug events.
- Added the Raw Input snapshot to the hardware diagnostics report without changing keyboard input selection, brightness policy or driver-control behavior.

### Startup and device-notification profiling (Phases 22 / 22.1)
- Added monotonic startup checkpoints and elapsed-time breakdowns to application logging and the diagnostics report.
- Added separate counters for actual Windows `WM_INPUT_DEVICE_CHANGE` arrival/removal notifications and notification errors, distinct from enumeration-generated callbacks.
- Added finer timing logs for WMI-based Lenovo service discovery, IBMPmDrv backend construction, Raw Input capture, power tracking and keyboard controller initialization.
- This release adds diagnostic instrumentation; it does **not** claim to resolve intermittent slow startup. Startup time still varies, and further investigation is needed.

### Release packaging and verified upgrade (Phases 23A–23D)
- Set assembly/file version to `1.3.5.0` and MSI ProductVersion to `1.3.5`; changed MSI ProductCode and PackageCode while retaining the existing UpgradeCode for major-upgrade detection.
- Rebuilt the application and MSI; verified source/build metadata and confirmed that the final Release EXE SHA-256 matches the executable extracted from the MSI.
- Successfully tested an in-place Windows Installer upgrade from **v1.3.4 to v1.3.5** on the developer's ThinkPad X1 Extreme Gen 2 (Windows 11): v1.3.5 registered, v1.3.4 removed, installed EXE hash matched the validated MSI payload, and the logon startup task was preserved/recreated correctly.
- Confirmed saved settings could be loaded after the upgrade. The serialized registry `Settings` value changed at the byte level, but manually checked timeout, brightness level, external-device setting and OSD preference were retained. This is **not** a claim of byte-for-byte preservation or exhaustive settings compatibility.
- Passed local regression checks for external Bluetooth mouse and built-in keyboard triggering, inactivity timeout, sleep/resume and startup after Windows restart. Other ThinkPad models and clean v1.3.5 installations have not been comprehensively tested.
- No new hardware backend is introduced: only compatible `IBMPmDrv` is used for backlight control; `EnergyDrv` remains diagnostic-only.

## v1.3.4 — released 30 October 2026

### Backlight and input reliability (Phases 20C / 20C.1)
- Improved Raw Input discovery and rescan for Bluetooth/USB mice and keyboards, including devices connected after launch and devices available after Windows startup.
- Hardened hot-plug handling and device enumeration to avoid crashes on device-change notifications; retained internal keyboard/touchpad/TrackPoint support.
- Fixed a brightness preference synchronization edge case in which automatic changes could be mistaken for the user's chosen brightness level.
- Improved recovery across pause/resume and device changes while retaining Fn+Space behavior and the existing IBMPmDrv backend.

### Driver diagnostics (Phase 20A)
- Added a read-only IBMPmDrv GET probe to the hardware diagnostics report: raw response, recognized signature, decoded current brightness, and maximum level.
- Verified GET at Off (0), Low (1), High (2) and separately exercised SET/GET with restoration on the developer's laptop; the shipped report remains read-only and does not poll the driver continuously.

### Lenovo OSD compatibility (Phase 21)
- Made **Disable on-screen display** available when the Lenovo OSD registry key exists but its `Enable` value is absent.
- Avoided creating `Enable` on application startup. An explicit user toggle can create the missing DWORD (`0` to suppress OSD); toggling back writes `1`.
- Retained handling of valid existing `Enable=0/1` values, with read-back diagnostics and safeguards for invalid registry data.
- Locally verified missing-value behavior and transitions `0 → 1 → 0` on the ThinkPad X1 Extreme Gen 2. This is not comprehensive validation of other Lenovo OSD versions.

### Build, installation and validation
- Released v1.3.4 with updated application and MSI version metadata, retaining the same MSI upgrade family (`UpgradeCode`).
- Validated identical SHA-256 hashes for the local Release EXE and installed Program Files EXE after MSI replacement, and confirmed Bluetooth mouse activation of keyboard backlight after a full Windows reboot.
- Preserved Task Scheduler autostart, legacy user-settings format, optional camera behavior, and IBMPmDrv-only active control.
- The v1.3.4 MSI was installed successfully on the developer’s ThinkPad and the application was reported to work as expected. This is a local installation smoke test, not an exhaustive validation of every upgrade or uninstall scenario. EnergyDrv control remains unimplemented.

## v1.3.1 — 28 October 2026

### Enhanced diagnostics
- Extended the hardware diagnostics report with Lenovo OSD registry configuration, saved preferences, TPOSD process information, TPHKLOAD service state and BIOS information.
- Improved Lenovo OSD application logging, including startup state, requested changes, previous registry values and read-back verification.
- Added diagnostic information to help identify inconsistencies between saved OSD preferences and the actual registry state.

### Application log export
- Added **Export log...** to the main tray menu, directly below Diagnostics.
- Users can export a copy of `application.log` through the standard Windows Save As dialog.
- Preserved the original log file and existing diagnostic report functionality.

### Compatibility
- Preserved existing keyboard and screen brightness controls.
- No changes to the keyboard backlight driver backend or serialized user settings format.
- Continued support for the existing Lenovo OSD preference mechanism.

## v1.3.0 — 27 October 2026

### Modern native WPF tray menu
- Replaced the legacy tray-menu visuals with a coordinated set of custom WPF styles and templates; no FluentWpfCore dependency is required.
- Updated the top-level menu with rounded corners, subtle hover highlighting, vector icons and consistent spacing.
- Restyled the Settings submenu, using compact, neutral checkmarks and a layout that accommodates its existing ComboBox/TextBox controls.
- Preserved existing menu bindings, shortcuts, calibration, diagnostics and keyboard/screen controls.

### Lenovo on-screen display preference
- Added **Disable on-screen display** in Settings to control Lenovo's existing per-user OSD `Enable` setting, when detected.
- Save the user's preference separately from the legacy binary-serialized application settings, preserving backward compatibility.
- Adopt the existing Lenovo OSD state on first run and reapply the stored preference on subsequent starts when necessary.

### Release packaging and validation
- Updated application and MSI version identifiers for v1.3.0.
- At the time of v1.3.0 preparation, no independently confirmed v1.2.0 → v1.3.0 MSI upgrade test was documented.

## v1.2.0 — released 27 October 2026

The `v4.x`/`v5.x` and `v6.0 Phase 1–8` names below refer to internal milestones that contributed to this release.

### Keyboard backlight and input
- Improved handling of built-in keyboard, touchpad and TrackPoint Raw Input, including external keyboards/mice via **React to external input devices** (enabled for new configurations; existing saved choices preserved).
- Renamed displayed maximum brightness from **Full** to **High** without changing the underlying hardware level (`2`). The three levels remain Off (0), Low (1), High (2).
- Improved `Pause`/`Resume` power-event handling and recovery of input capture following lid reopen; successful lid cycles observed, intermittent behavior remains under observation.
- Preserved interaction with the built-in **Fn+Space** shortcut and configurable inactivity timeout.

### Settings and optional components
- Stabilized settings integrity checks/checksum across saves, including recovery of previously stored settings where validated.
- Kept optional camera-based screen brightness disabled by default for new installations while respecting previous preferences.
- Kept the legacy automatic in-app updater disabled; releases are obtained manually from the fork.

### Driver diagnostics and reporting
- Isolated the `IBMPmDrv` backend behind a dedicated interface; `EnergyDrv` service detection remains informational and does not send control commands.
- Added startup, hardware, power-event and exception diagnostics. `IBMPmDrv` failures are logged with rate limiting rather than silently swallowed.
- Expanded the **Diagnostics...** tray workflow, including hardware information and export support.

### Packaging and tested upgrade
- Updated MSI metadata for fork maintainer and repository URL; changed `ProductCode` and `PackageCode` while retaining the existing upgrade family (`UpgradeCode`). MSI and compiled assembly are version `1.2.0`.
- Passed clean MSI install, uninstall (with app closed), reinstall and retention of saved user settings on the tested ThinkPad.
- **Passed in-place MSI upgrade from original `1.0.0` to fork `1.2.0` on 27 October 2026.** The installer detected the old product, executed removal, and left only `1.2.0` registered; application startup and saved-settings loading were observed. Exhaustive validation of every legacy preference is not claimed.
- A post-upgrade startup was observed with approximately 22 seconds before tray initialization, including an approximately 20-second gap preceding backend service reporting; the precise cause is under investigation. No fix is claimed yet.
- v1.2.0 was published; wider hardware validation and startup timing investigation remained open.

## v5.3: documentation and release readiness

- Rewrite README to describe the maintained fork rather than only the original app.
- Document `IBMPmDrv` scope, verified hardware, optional camera and external-device behavior.
- Add compatibility and troubleshooting guidance.
- Explain that legacy automatic MSI updating is disabled and point to fork Releases.
- No C# source, project configuration or installer changes.

## v5.2: fork identity and About

- Updated About/project metadata and links to the maintained fork.
- Disabled the legacy automatic update/install flow pending a safe replacement.
- Introduced an explicit development-version identity (`1.2.0-dev`).

## v5.1: backend discovery

- Added read-only discovery and reporting for `IBMPmDrv` and `EnergyDrv` services.
- Kept `IBMPmDrv` as the only active backlight backend.

## v5.0: modular backend architecture

- Introduced a backend interface/factory and isolated `IBMPmDrv` control from high-level automation.

## v4.3: hardware compatibility and resilience

- Improved brightness-level clamping, backend error handling and hardware capability reporting.

## v4.2: diagnostics export

- Added export of a hardware diagnostic report through the tray menu.
- Fixed the file-save dialog by assigning an explicit owner window.

## v4.1: fault isolation

- Added independent controller initialization diagnostics and a hardware-report file.

## v4.0 and earlier fork improvements

- Added application logging and more defensive driver/Raw Input handling.
- Removed assumptions about specific keyboard/TrackPoint identifiers, including support for `LEN0402`.
- Added optional recognition of external keyboard and mouse activity.
- Made camera initialization optional and improved handling of missing devices.
- Preserved keyboard illumination preferences and improved UI binding behavior.

## Historical upstream

Original project and credits: [pspatel321/auto-backlight-for-thinkpad](https://github.com/pspatel321/auto-backlight-for-thinkpad).

## Outstanding validation and future work (after v1.3.8)

- Continue investigating intermittent slow startup using the Phase 22/22.1 checkpoints; diagnostic instrumentation is not a performance fix.
- Broader failure-mode and hardware regression coverage for driver errors, camera, power transitions and legacy settings variants.
- For each subsequent release, confirm informational version, MSI/EXE versions, release asset hashes, upgrade behavior, packaging and signing decision.
- Obtain real EnergyDrv v1/v2 and guarded-protocol hardware tests and compare results across model families before calling either backend stable. Investigate Lenovo Lighting WMI/Vantage interfaces separately; neither is implemented.
