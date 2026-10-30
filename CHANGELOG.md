# Changelog

## v1.3.4 — 30 October 2026 (release candidate; publish after MSI validation)

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
- Updated developer build/MSI versions between v1.3.1 and this release candidate; v1.3.4 release metadata and a freshly built MSI must be verified before publication.
- Validated identical SHA-256 hashes for the local Release EXE and installed Program Files EXE after MSI replacement, and confirmed Bluetooth mouse activation of keyboard backlight after a full Windows reboot.
- Preserved Task Scheduler autostart, legacy user-settings format, optional camera behavior, and IBMPmDrv-only active control.
- No change to EnergyDrv support. A successful local test does not establish general hardware compatibility or validate the final v1.3.4 MSI upgrade.

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
- Updated application and MSI version identifiers to v1.3.0; verify compiled metadata and release asset checksum before publishing.
- The v1.2.0 → v1.3.0 MSI upgrade must be tested before publishing; local builds and interactive UI tests alone do not establish upgrade compatibility.

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
- The public v1.2.0 release was published; wider hardware validation and startup timing investigation remain open.

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

## Outstanding validation and future work

- Isolate and measure intermittent startup delay before backend service reporting and tray initialization.
- Broader failure-mode and hardware regression coverage for driver errors, camera, power transitions and legacy settings variants.
- For each subsequent release, confirm informational version, MSI/EXE versions, release asset hashes, upgrade behavior, packaging and signing decision.
- Additional Lenovo driver families, including `EnergyDrv`, remain deferred pending protocol and hardware validation.
