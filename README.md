# Auto Backlight for ThinkPad

> Community-maintained fork of [Parth Patel's original Auto Backlight for ThinkPad](https://github.com/pspatel321/auto-backlight-for-thinkpad), maintained at [prat89/auto-backlight-for-thinkpad](https://github.com/prat89/auto-backlight-for-thinkpad).

A lightweight Windows system-tray application that automatically turns a compatible Lenovo laptop keyboard backlight on when you use the computer, and off after an adjustable period of inactivity.

**Latest stable source version: v1.3.8 (validated 2 November 2026).** This maintenance release updates the WPF tray dependency to Hardcodet.NotifyIcon.Wpf 2.0.1, migrates Calibration to native OxyPlot 2.2.0 `PlotView`/`PlotModel` (removing OxyPlot.Contrib.Wpf), improves webcam exposure restoration, and fixes MSI Error 1001 caused by early Accord.NET type resolution during installation. Accord.NET remains at 3.8.0; the preferred IBMPmDrv and experimental EnergyDrv backends are unchanged. The developer successfully upgraded from v1.3.7 using the final v1.3.8 MSI and verified keyboard illumination, camera brightness, exposure restoration and startup after a Windows restart. See [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) for **published** installers.

## Features

- Automatic keyboard backlight on supported ThinkPad hardware, including built-in keyboard, touchpad and TrackPoint activity.
- **React to external input devices** detects activity from compatible external keyboards and mice, with improved hot-plug, Bluetooth reconnection and post-restart discovery (tested locally). Enabled by default for new configurations; previously saved preferences are respected.
- Configurable inactivity timeout and **Off / Low / High** backlight states on the verified three-level backend (Off = 0, Low = 1, High = 2). The **High** label replaces the earlier **Full** label without changing the underlying hardware level.
- Cooperates with the laptop's **Fn+Space** shortcut; manual level changes may affect subsequent automatic brightness.
- Improved handling of power/display and lid close/open notifications, with explicit pause/resume and Raw Input capture recovery. Intermittent scenarios are still under observation.
- Optional camera-based automatic **screen brightness** control, independent from keyboard lighting and **off by default for new configurations**. v1.3.8 fixes restoring saved camera exposure settings, including the timeout path. The maintainer tested the camera feature locally; exposure behavior can differ with other drivers. Accord.NET 3.8.0 remains unchanged.
- **Diagnostics...** in the tray menu exports hardware and backend information; a hardware report is also written automatically at startup. v1.3.5 added read-only Raw Input counters and startup checkpoints; v1.3.6 adds startup-only Raw Input enumeration timings, SCM driver-service discovery, and reporting for the actually selected backend. These diagnostics do not record keystroke contents or Raw Input device paths; review and redact exported files before sharing.
- **Disable on-screen display** supports compatible Lenovo OSD installations where `HKCU\Software\Lenovo\OnScreenDisplay` exists even when `Enable` is missing. The app does not create a missing `Enable` on startup; a user-initiated toggle can create the DWORD and set `0` (OSD disabled) or `1` (OSD enabled). Its preference is stored separately from legacy settings; behavior depends on the installed Lenovo OSD software.
- **Modern tray menu** (v1.3.0), with Hardcodet.NotifyIcon.Wpf updated to 2.0.1 in v1.3.8. The context menu was verified to open on the maintainer's development EXE; other DPI and Windows configurations are not yet exhaustively tested.
- **Driver backends (unchanged in v1.3.8):** `IBMPmDrv` is preferred; experimental `EnergyDrv` v1/v2 and the guarded capability-based variant remain fallback options. EnergyDrv has not been verified on physical EnergyDrv hardware. No Lenovo drivers or Vantage DLLs are bundled.
- Improved settings persistence and validation, including compatibility recovery for previously saved settings. Legacy in-app automatic updater remains disabled.

## Compatibility

**Tested:** Lenovo ThinkPad X1 Extreme Gen 2, model `20QVCTO1WW`, with `IBMPmDrv` installed and running; keyboard backlight levels Off (0), Low (1), High (2).

One community user also reported the app working on a **ThinkPad P14s Gen 5 AMD**, without comprehensive independent verification. Other IBMPmDrv-based ThinkPads may work. **EnergyDrv v1/v2 and the new capability-gated variant are experimental**, potentially relevant to some Lenovo models beyond ThinkPads. **No EnergyDrv laptop has been tested with this fork**. Driver presence, model family or a recognized GET response does not guarantee physical backlight control. No blanket ThinkBook/IdeaPad/Yoga/Legion or RGB compatibility is claimed.

See [COMPATIBILITY.md](COMPATIBILITY.md) for requirements, diagnostics and troubleshooting.

## Installation

1. Visit [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) and select a release from **this fork** (not upstream).
2. Download the MSI asset of that release, if provided. Development commits are not release builds.
3. **Exit any running Auto Backlight for ThinkPad instance before upgrading or uninstalling.** If the tray icon is hidden, check the notification overflow area; use Task Manager if needed.
4. Run the MSI, then allow the application to initialize. The tray icon may appear after a delay; consult the startup log if it never appears.

**v1.3.5 upgrade validation (31 October 2026):** the maintainer successfully upgraded the developer-tested ThinkPad X1 Extreme Gen 2 from the published v1.3.4 MSI to v1.3.5. Windows Installer removed the earlier product and registered v1.3.5; the installed EXE matched the validated MSI payload SHA-256; the logon startup task remained functional. Preferences were loaded successfully and checked in the UI, including after a restart and sleep/resume. This is a **single-device upgrade test**, not a guarantee for other models or every legacy configuration.

**Settings caveat:** the serialized `Settings` registry value changed between the baseline and the upgraded installation, so byte-for-byte equality did not pass. The application successfully loaded its preferences and manually checked values (timeout, Low/High, external input and OSD) persisted across upgrade and reboot. Back up important settings before upgrading; not every historical settings combination has been tested.

**v1.3.7 upgrade validation (1 November 2026):** the maintainer upgraded directly from installed **v1.3.6 to v1.3.7 using the MSI** on a ThinkPad X1 Extreme Gen 2. The updated application started and keyboard backlight activation via keyboard, touchpad and mouse worked normally using IBMPmDrv. This is one-device functional validation; installed-file hash, detailed settings-retention and full reboot/suspend upgrade matrices were not independently checked for this upgrade.

To uninstall, exit the application first, then use **Windows Settings > Apps > Installed apps**. Removing the MSI does not automatically erase the current user's saved registry preferences in the tested scenario.

**Updates:** the original application's automatic download/uninstall/install updater is disabled in this fork. Check the fork's [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) page manually; do not use an original-project MSI as an update to this fork.

**v1.3.8 validated upgrade (2 November 2026):** The maintainer successfully completed a v1.3.7 → v1.3.8 MSI upgrade on the ThinkPad X1 Extreme Gen 2. The EXE extracted from the final MSI matched the tested Release EXE (SHA-256 `45510B15D235E5FD352D5BBF38922FD3C83E7E24C51CC0877273829A3406ACF8`), and all 119 application types loaded during resolver-free .NET reflection. The application, keyboard lighting, camera-controlled screen brightness, exposure restoration and startup after Windows restart were confirmed working. This does not claim comprehensive clean-install or cross-device validation; the installed EXE hash was not separately recorded for this upgrade.

## Usage

- Enable automatic keyboard lighting in the tray menu and select an inactivity timeout and **Low** or **High** brightness.
- Use **React to external input devices** to include connected keyboards and mice. On a new configuration it starts enabled; existing settings prevail.
- Use **Disable on-screen display** to control compatible Lenovo indicators. When the Lenovo OSD registry key exists but `Enable` is absent, startup leaves it absent; explicitly toggling the option can create `Enable=0` (disable) or write `Enable=1` (enable). This does not change physical keyboard backlight control.
- The optional **Screen brightness via camera** feature is separate from keyboard lighting; it starts disabled for a new configuration.
- Choose **Diagnostics...** in the tray menu to inspect/export diagnostic information. Review and redact local paths, system identifiers and device details before sharing.

## Troubleshooting

1. Confirm that the laptop has a supported Lenovo driver interface. `IBMPmDrv` is preferred; experimental `EnergyDrv` v1/v2 and the guarded three-state variant are fallback options. This app does not install kernel drivers. EnergyDrv hardware compatibility remains unverified; if selected, stop testing if brightness behavior is unexpected.
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
- The Windows application uses WPF, Windows Raw Input, power notifications and pluggable keyboard-backlight backends (`IBMPmDrv` preferred; experimental `EnergyDrv` v1/v2 plus guarded capability-based fallback). Optional camera screen-brightness functionality uses the existing project dependencies.

## Development and releases

The historical `v4.x`, `v5.x`, `v6.0` and `Phase` labels are internal development milestones, not public tags. The latest locally validated stable source version is **v1.3.8**. See [GitHub Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) for actual published installers. This release updates the tray dependency and Calibration plotting stack, and fixes camera exposure restoration; it does not expand the experimental EnergyDrv hardware compatibility claim.

See [CHANGELOG.md](CHANGELOG.md) for historical releases and [COMPATIBILITY.md](COMPATIBILITY.md) for verified hardware and historical MSI tests. Installation on one machine does not establish support across all ThinkPads or power transitions. Earlier MSI builds were not digitally signed; confirm the signing status of each download rather than assuming an authenticated publisher. GitHub Releases are authoritative for published release assets.

## Credits and license

Originally developed by **Parth Patel** (2019): [original project](https://github.com/pspatel321/auto-backlight-for-thinkpad). This fork is maintained by **prat89** and preserves upstream attribution.

Source license: **Apache License 2.0**. Read [LICENSE](LICENSE) and [NOTICE](NOTICE) for upstream and third-party notices, including the MIT-licensed EnergyDrv protocol references. The `NOTICE` file is intentionally preserved.
