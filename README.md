# Auto Backlight for ThinkPad

> Community-maintained fork of [Parth Patel's original Auto Backlight for ThinkPad](https://github.com/pspatel321/auto-backlight-for-thinkpad), maintained at [prat89/auto-backlight-for-thinkpad](https://github.com/prat89/auto-backlight-for-thinkpad).

A lightweight Windows system-tray application that automatically turns the keyboard backlight on when you use your ThinkPad, and off after an adjustable period of inactivity.

**Status:** Pre-release validation of application/MSI version `1.2.0` (the v6.0 labels denote internal development phases, not a product version). No public GitHub Release is implied. Keyboard backlight control currently uses Lenovo's **IBMPmDrv** driver. Other Lenovo driver families, including **EnergyDrv**, are **not supported** by this fork.

## Features

- Automatic keyboard backlight on supported ThinkPad hardware, including built-in keyboard, touchpad and TrackPoint activity.
- **React to external input devices** detects activity from compatible external keyboards and mice. Enabled by default for new configurations; previously saved preferences are respected.
- Configurable inactivity timeout and **Off / Low / High** backlight states on the verified three-level backend (Off = 0, Low = 1, High = 2). The **High** label replaces the earlier **Full** label without changing the underlying hardware level.
- Cooperates with the laptop's **Fn+Space** shortcut; manual level changes may affect subsequent automatic brightness.
- Improved handling of power/display and lid close/open notifications, with explicit pause/resume and Raw Input capture recovery. Intermittent scenarios are still under observation.
- Optional camera-based automatic **screen brightness** control, independent from keyboard lighting and **off by default on new installations**. Existing saved preferences are respected; enabling it may briefly activate the camera.
- **Diagnostics...** in the tray menu opens the diagnostics/export workflow, alongside application and hardware logs. Review diagnostics before sharing.
- Diagnostic checks for `IBMPmDrv`/`EnergyDrv` service presence; only `IBMPmDrv` performs hardware commands. Hardware-access errors are logged with throttling.
- Improved settings persistence and validation, including compatibility recovery for previously saved settings. Legacy in-app automatic updater remains disabled.

## Compatibility

**Tested:** Lenovo ThinkPad X1 Extreme Gen 2, model `20QVCTO1WW`, with `IBMPmDrv` installed and running; keyboard backlight levels Off (0), Low (1), High (2).

Other ThinkPads with a compatible `IBMPmDrv` interface **may work**, but have not been verified by this fork's maintainer. An installed Lenovo driver alone does not guarantee compatibility. **ThinkBook, IdeaPad, Yoga and Legion devices using EnergyDrv or other protocols are not supported.**

See [COMPATIBILITY.md](COMPATIBILITY.md) for requirements, diagnostics and troubleshooting.

## Installation

1. Visit [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) and select a release from **this fork** (not upstream).
2. Download the MSI asset of that release, if provided. Development commits are not release builds.
3. **Exit any running Auto Backlight for ThinkPad instance before upgrading or uninstalling.** If the tray icon is hidden, check the notification overflow area; use Task Manager if needed.
4. Run the MSI, then allow the application to initialize. The tray icon may appear after a delay; consult the startup log if it never appears.

**Verified MSI scenarios (ThinkPad X1 Extreme Gen 2, 27 October 2026):** clean installation of `1.2.0`; uninstall with the application closed; reinstall with existing user settings retained; and **in-place major upgrade from the original `1.0.0` MSI to `1.2.0`**. During the upgrade, Windows Installer detected and removed the original product and registered the new product. A single `1.2.0` entry remained in Windows Installed apps.

**Settings caveat:** settings retained across a `1.2.0` uninstall/reinstall were tested. The `1.0.0` → `1.2.0` test confirmed that existing saved settings could be read on the upgraded installation, but did not validate every possible legacy preference/value combination. Back up settings before upgrading.

To uninstall, exit the application first, then use **Windows Settings > Apps > Installed apps**. Removing the MSI does not automatically erase the current user's saved registry preferences in the tested scenario.

**Updates:** the original application's automatic download/uninstall/install updater is disabled in this fork. Check the fork's [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) page manually; do not use an original-project MSI as an update to this fork.

## Usage

- Enable automatic keyboard lighting in the tray menu and select an inactivity timeout and **Low** or **High** brightness.
- Use **React to external input devices** to include connected keyboards and mice. On a new configuration it starts enabled; existing settings prevail.
- The optional **Screen brightness via camera** feature is separate from keyboard lighting; it starts disabled for a new configuration.
- Choose **Diagnostics...** in the tray menu to inspect/export diagnostic information. Review and redact local paths, system identifiers and device details before sharing.

## Troubleshooting

1. Confirm that a compatible `IBMPmDrv` driver is installed and running. This program does not ship or install kernel drivers. `EnergyDrv` discovery is informational only.
2. Check Windows' notification area, including hidden icons. Startup can be delayed while Windows reports driver/service status; a visible tray icon is not guaranteed immediately after MSI completion.
3. Open **Diagnostics...** in the tray menu to inspect/export a report, or review:

   ```text
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\application.logg
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\hardware-report.txt
   ```

4. For startup delays, compare timestamps for `Process start`, `Startup checkpoint`, `Backend service` and `Tray icon initialized`. One observed post-upgrade startup took around 22 seconds to initialize the tray icon, with an approximately 20-second gap before the backend-service report; the root cause has **not** yet been confirmed.
5. If keyboard lighting does not resume after opening the lid, collect power event logs (`Power event`, `Power resume recovery`) and note whether external monitors or input devices were connected.
6. If no camera is available or camera access is blocked, leave optional screen brightness automation disabled. When opening an [issue](https://github.com/prat89/auto-backlight-for-thinkpad/issues), specify laptop model, Windows build, driver state and steps to reproduce; redact diagnostics before uploading.

## Build from source

- Windows, **Visual Studio 2026** with the **.NET desktop development** workload, **.NET Framework 4.8 developer/targeting pack** and NuGet restore. The MSI project also needs the **Visual Studio Installer Projects** extension.
- Open `Auto Backlight for Thinkpad.sln`, select **Release / Any CPU**, rebuild the application, then rebuild the separate **Auto Backlight for ThinkPad Setup** project if an MSI is needed.
- `Settings.cs` is **generated** from `Settings.tmpl.cs` using `tmplGenerator.py`; edit the template and rebuild instead of editing generated code. The generator's string comparison warning was fixed (`is` → `==`) during validation.
- Validate installed files and the MSI version independently from the build output. Build artifacts and temporary scripts are not source files and should not be committed.
- Keep staging and backup artifacts **outside this repository**, preferably under `Downloads` and `Documents/LenovoBacklightBackups`. Inspect `git status` before committing; do not commit log files, `.msi` files, `bin/`, `obj/` or local backup directories.
- The Windows application uses WPF, Windows Raw Input, power notifications and the `IBMPmDrv` interface. Optional camera screen-brightness functionality uses the existing project dependencies; other Lenovo backends are not enabled.

## Development and releases

The historical `v4.x`, `v5.x` and `v6.0 Phase 1–8` labels are **internal development milestones**, not public version tags. The compiled application and tested MSI use `1.2.0` (`AssemblyVersion` and `AssemblyFileVersion` of `1.2.0.0`); confirm `AssemblyInformationalVersion` and all public-facing version strings before tagging a release.

See [CHANGELOG.md](CHANGELOG.md) for history and [COMPATIBILITY.md](COMPATIBILITY.md) for verified hardware. A successful MSI upgrade test does not establish compatibility with untested models or every power transition. At the documentation update date (27 October 2026), startup timing diagnostics and final release validation are still pending. The tested MSI is **not digitally signed**; Windows may show publisher/security warnings. GitHub Releases, once published, are authoritative for release assets.

## Credits and license

Originally developed by **Parth Patel** (2019): [original project](https://github.com/pspatel321/auto-backlight-for-thinkpad). This fork is maintained by **prat89** and preserves upstream attribution.

Source license: **Apache License 2.0**. Read [LICENSE](LICENSE) and [NOTICE](NOTICE) for upstream and third-party notices. The `NOTICE` file is intentionally preserved.
