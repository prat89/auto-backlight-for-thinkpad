# Auto Backlight for ThinkPad

> Community-maintained fork of [Parth Patel's original Auto Backlight for ThinkPad](https://github.com/pspatel321/auto-backlight-for-thinkpad), maintained at [prat89/auto-backlight-for-thinkpad](https://github.com/prat89/auto-backlight-for-thinkpad).

A lightweight Windows system-tray application that automatically turns a compatible Lenovo laptop keyboard backlight on when you use the computer, and off after an adjustable period of inactivity.

**Current source version: v1.3.6** (release candidate, 1 November 2026). The latest publicly published installer may still be v1.3.5 until the [GitHub Release](https://github.com/prat89/auto-backlight-for-thinkpad/releases) is created. v1.3.6 improves driver-service discovery and adds **experimental EnergyDrv v1/v2 keyboard-backlight control** as a fallback when IBMPmDrv initialization fails. EnergyDrv control has passed protocol-level tests but **has not been tested on EnergyDrv hardware**. On the developer's ThinkPad, IBMPmDrv still works. Intermittent slow Windows-startup initialization is not claimed to be fixed.

## Features

- Automatic keyboard backlight on supported ThinkPad hardware, including built-in keyboard, touchpad and TrackPoint activity.
- **React to external input devices** detects activity from compatible external keyboards and mice, with improved hot-plug, Bluetooth reconnection and post-restart discovery (tested locally). Enabled by default for new configurations; previously saved preferences are respected.
- Configurable inactivity timeout and **Off / Low / High** backlight states on the verified three-level backend (Off = 0, Low = 1, High = 2). The **High** label replaces the earlier **Full** label without changing the underlying hardware level.
- Cooperates with the laptop's **Fn+Space** shortcut; manual level changes may affect subsequent automatic brightness.
- Improved handling of power/display and lid close/open notifications, with explicit pause/resume and Raw Input capture recovery. Intermittent scenarios are still under observation.
- Optional camera-based automatic **screen brightness** control, independent from keyboard lighting and **off by default on new installations**. Existing saved preferences are respected; enabling it may briefly activate the camera.
- **Diagnostics...** in the tray menu exports hardware and backend information; a hardware report is also written automatically at startup. v1.3.5 added read-only Raw Input counters and startup checkpoints; v1.3.6 adds startup-only Raw Input enumeration timings, SCM driver-service discovery, and reporting for the actually selected backend. These diagnostics do not record keystroke contents or Raw Input device paths; review and redact exported files before sharing.
- **Disable on-screen display** supports compatible Lenovo OSD installations where `HKCU\Software\Lenovo\OnScreenDisplay` exists even when `Enable` is missing. The app does not create a missing `Enable` on startup; a user-initiated toggle can create the DWORD and set `0` (OSD disabled) or `1` (OSD enabled). Its preference is stored separately from legacy settings; behavior depends on the installed Lenovo OSD software.
- **Modern tray menu** (v1.3.0) replaces the legacy menu visuals with native WPF templates, rounded corners, subtle highlighting, icons and compact checkmarks. No additional UI library is required.
- **Driver backends (v1.3.6):** prefers the hardware-tested `IBMPmDrv` interface; if that initialization fails, attempts an experimental `EnergyDrv` v1/v2 backend. EnergyDrv detection uses a read-only GET and rejects unknown responses; writes are followed by a GET verification. No Lenovo kernel drivers are bundled. EnergyDrv has **not** been hardware-validated.
- Improved settings persistence and validation, including compatibility recovery for previously saved settings. Legacy in-app automatic updater remains disabled.

## Compatibility

**Tested:** Lenovo ThinkPad X1 Extreme Gen 2, model `20QVCTO1WW`, with `IBMPmDrv` installed and running; keyboard backlight levels Off (0), Low (1), High (2).

One community user also reported the app working on a **ThinkPad P14s Gen 5 AMD**, without comprehensive independent verification. Other IBMPmDrv-based ThinkPads may work. **Experimental EnergyDrv v1/v2 support** is included for potentially compatible Lenovo models (for example, some ThinkBook/IdeaPad devices), but **no EnergyDrv laptop has been tested with this fork**. Driver presence, family name or a recognized GET response does not guarantee successful physical backlight control. No blanket Yoga/Legion/RGB compatibility is claimed.

See [COMPATIBILITY.md](COMPATIBILITY.md) for requirements, diagnostics and troubleshooting.

## Installation

1. Visit [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) and select a release from **this fork** (not upstream).
2. Download the MSI asset of that release, if provided. Development commits are not release builds.
3. **Exit any running Auto Backlight for ThinkPad instance before upgrading or uninstalling.** If the tray icon is hidden, check the notification overflow area; use Task Manager if needed.
4. Run the MSI, then allow the application to initialize. The tray icon may appear after a delay; consult the startup log if it never appears.

**v1.3.5 upgrade validation (31 October 2026):** the maintainer successfully upgraded the developer-tested ThinkPad X1 Extreme Gen 2 from the published v1.3.4 MSI to v1.3.5. Windows Installer removed the earlier product and registered v1.3.5; the installed EXE matched the validated MSI payload SHA-256; the logon startup task remained functional. Preferences were loaded successfully and checked in the UI, including after a restart and sleep/resume. This is a **single-device upgrade test**, not a guarantee for other models or every legacy configuration.

**Settings caveat:** the serialized `Settings` registry value changed between the baseline and the upgraded installation, so byte-for-byte equality did not pass. The application successfully loaded its preferences and manually checked values (timeout, Low/High, external input and OSD) persisted across upgrade and reboot. Back up important settings before upgrading; not every historical settings combination has been tested.

To uninstall, exit the application first, then use **Windows Settings > Apps > Installed apps**. Removing the MSI does not automatically erase the current user's saved registry preferences in the tested scenario.

**Updates:** the original application's automatic download/uninstall/install updater is disabled in this fork. Check the fork's [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) page manually; do not use an original-project MSI as an update to this fork.

## Usage

- Enable automatic keyboard lighting in the tray menu and select an inactivity timeout and **Low** or **High** brightness.
- Use **React to external input devices** to include connected keyboards and mice. On a new configuration it starts enabled; existing settings prevail.
- Use **Disable on-screen display** to control compatible Lenovo indicators. When the Lenovo OSD registry key exists but `Enable` is absent, startup leaves it absent; explicitly toggling the option can create `Enable=0` (disable) or write `Enable=1` (enable). This does not change physical keyboard backlight control.
- The optional **Screen brightness via camera** feature is separate from keyboard lighting; it starts disabled for a new configuration.
- Choose **Diagnostics...** in the tray menu to inspect/export diagnostic information. Review and redact local paths, system identifiers and device details before sharing.

## Troubleshooting

1. Confirm that the laptop has a supported Lenovo driver interface. `IBMPmDrv` is preferred; experimental `EnergyDrv` v1/v2 is attempted only if IBMPmDrv initialization fails. This app does not install kernel drivers. If EnergyDrv is selected, its actual backlight control is unverified and may not work on your model.
2. Check Windows' notification area, including hidden icons. Startup can be delayed while Windows reports driver/service status; a visible tray icon is not guaranteed immediately after MSI completion.
3. Open **Diagnostics...** in the tray menu to inspect/export a report, or review:

   ```text
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\application.log
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\hardware-report.txt
   ```

4. For startup delays, inspect `Startup timing:`, `Phase25C startup:` (SCM driver discovery), and `Phase25E.3 startup:` (initial Raw Input enumeration) in `application.log`. The older slow WMI service-discovery path was replaced with SCM, but intermittent slow initialization during Windows logon is **not** conclusively fixed. The standard logon task has **no intentional delay**.
5. If keyboard lighting does not resume after opening the lid, collect power event logs (`Power event`, `Power resume recovery`) and note whether external monitors or input devices were connected.
6. If no camera is available or camera access is blocked, leave optional screen brightness automation disabled. When opening an [issue](https://github.com/prat89/auto-backlight-for-thinkpad/issues), specify laptop model, Windows build, driver state and steps to reproduce; redact diagnostics before uploading.

## Build from source

- Windows, **Visual Studio 2026** with the **.NET desktop development** workload, **.NET Framework 4.8 developer/targeting pack** and NuGet restore. The MSI project also needs the **Visual Studio Installer Projects** extension.
- Open `Auto Backlight for Thinkpad.sln`, select **Release / Any CPU**, rebuild the application, then rebuild the separate **Auto Backlight for ThinkPad Setup** project if an MSI is needed.
- `Settings.cs` is **generated** from `Settings.tmpl.cs` using `tmplGenerator.py`; edit the template and rebuild instead of editing generated code. The generator's string comparison warning was fixed (`is` → `==`) during validation.
- Validate installed files and the MSI version independently from the build output. Build artifacts and temporary scripts are not source files and should not be committed.
- Keep staging and backup artifacts **outside this repository**, preferably under `Downloads` and `Documents/LenovoBacklightBackups`. Inspect `git status` before committing; do not commit log files, `.msi` files, `bin/`, `obj/` or local backup directories.
- The Windows application uses WPF, Windows Raw Input, power notifications and a pluggable keyboard-backlight backend (`IBMPmDrv` preferred, experimental `EnergyDrv` v1/v2 fallback). Optional camera screen-brightness functionality uses the existing project dependencies.

## Development and releases

The historical `v4.x`, `v5.x`, `v6.0` and `Phase` labels refer to **internal development milestones**, not public version tags. The current **source version is v1.3.6**; [GitHub Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) is authoritative for published installers. The developer successfully rebuilt and reinstalled the v1.3.6 MSI and confirmed IBMPmDrv lighting, but v1.3.6 has not yet completed a documented in-place upgrade test or EnergyDrv hardware test. Earlier v1.3.5 upgrade/payload validation is recorded in the changelog.

See [CHANGELOG.md](CHANGELOG.md) for historical releases and [COMPATIBILITY.md](COMPATIBILITY.md) for verified hardware and historical MSI tests. Installation on one machine does not establish support across all ThinkPads or power transitions. Earlier MSI builds were not digitally signed; confirm the signing status of each download rather than assuming an authenticated publisher. GitHub Releases are authoritative for published release assets.

## Credits and license

Originally developed by **Parth Patel** (2019): [original project](https://github.com/pspatel321/auto-backlight-for-thinkpad). This fork is maintained by **prat89** and preserves upstream attribution.

Source license: **Apache License 2.0**. Read [LICENSE](LICENSE) and [NOTICE](NOTICE) for upstream and third-party notices, including the MIT-licensed EnergyDrv protocol references. The `NOTICE` file is intentionally preserved.
