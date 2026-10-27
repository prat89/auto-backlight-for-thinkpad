# Auto Backlight for ThinkPad

> Community-maintained fork of [Parth Patel's original Auto Backlight for ThinkPad](https://github.com/pspatel321/auto-backlight-for-thinkpad), maintained at [prat89/auto-backlight-for-thinkpad](https://github.com/prat89/auto-backlight-for-thinkpad).

A lightweight Windows system-tray application that automatically turns the keyboard backlight on when you use your ThinkPad, and off after an adjustable period of inactivity.

**Status:** Release candidate for application/MSI version `1.2.0` (development milestones referred to as v6.0). This candidate is not yet a published GitHub Release. Keyboard backlight control currently uses Lenovo's **IBMPmDrv** driver. Other Lenovo driver families, including **EnergyDrv**, are **not supported** by this fork.

## Features

- Automatic keyboard backlight control using built-in keyboard, touchpad and TrackPoint activity.
- Optional **React to external input devices** setting for external keyboards and mice.
- Configurable inactivity timeout and keyboard brightness (**Low / High** on hardware that supports both).
- Works alongside the ThinkPad's **Fn+Space** keyboard backlight shortcut.
- Restores appropriate behavior around supported Windows power/display events.
- Optional camera-based automatic **screen brightness** control. **Off by default for new installations**; enabling this feature may activate the camera briefly. Existing saved preferences are retained.
- Hardware diagnostics and **Export diagnostic report...** in the tray menu.
- Error logging and isolation designed to reduce crashes when optional hardware is unavailable.

## Compatibility

**Tested:** Lenovo ThinkPad X1 Extreme Gen 2, model `20QVCTO1WW`, with `IBMPmDrv` installed and running; keyboard backlight levels Off (0), Low (1), High (2).

Other ThinkPads with a compatible `IBMPmDrv` interface **may work**, but have not been verified by this fork's maintainer. An installed Lenovo driver alone does not guarantee compatibility. **ThinkBook, IdeaPad, Yoga and Legion devices using EnergyDrv or other protocols are not supported.**

See [COMPATIBILITY.md](COMPATIBILITY.md) for requirements, diagnostics and troubleshooting.

## Installation

1. Visit [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) and select a release from **this fork**.
2. Download the MSI attached to the release, if one is provided. Read its release notes before installing; development commits are **not** equivalent to tested installer releases.
3. Run the installer. The Windows installer is designed to register the application to start when the user signs in.
4. After launch, locate the application in the Windows notification area (system tray). Open its menu to change settings.

To uninstall, **exit the running application from its tray menu first**, then use **Windows Settings > Apps > Installed apps**. Leaving the application running may delay removal. Settings stored for the current user are retained by the tested uninstall/reinstall procedure. **An in-place upgrade from an older MSI has not yet been verified**; preserve your settings and consult release notes before upgrading.

**Updates:** The legacy automatic download/uninstall/install mechanism from the original app has been disabled in this fork. Check [Releases](https://github.com/prat89/auto-backlight-for-thinkpad/releases) manually. Do not download installers from the original repository expecting them to update this fork.

## Usage

- Enable automatic keyboard backlighting and select the inactivity timeout in the tray menu.
- Select **Low** or **High** brightness, subject to the levels reported by your laptop.
- Enable **React to external input devices** if activity on connected keyboards/mice should also turn on the backlight.
- **Screen brightness via camera** is independent and optional. It is disabled by default on a fresh installation.
- Choose **Export diagnostic report...** to save basic hardware and driver information for troubleshooting. Review a report before sharing it publicly.

## Troubleshooting

1. Confirm `IBMPmDrv` is available on your machine. The app needs a compatible Lenovo power-management driver; it does not install or replace drivers.
2. Check the notification area, including hidden tray icons.
3. Export a report via **Export diagnostic report...** or inspect these files:

   ```text
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\application.log
   %LOCALAPPDATA%\Auto Backlight for ThinkPad\Logs\hardware-report.txt
   ```

4. If the app does not start, note your ThinkPad model, Windows version and driver status when opening an [issue](https://github.com/prat89/auto-backlight-for-thinkpad/issues). Redact sensitive information from diagnostics before sharing.
5. If a camera is missing or access is blocked, leave optional screen brightness automation disabled.

## Build from source

- Windows with **Visual Studio 2026**, the **.NET desktop development** workload, **.NET Framework 4.8 developer/targeting pack**, and **NuGet package restore**.
- Open `Auto Backlight for Thinkpad.sln` in Visual Studio.
- Build the **Auto Backlight for ThinkPad** application project in `Release / Any CPU`.
- The separate **Auto Backlight for ThinkPad Setup** project uses Visual Studio Installer Projects (`.vdproj`). Installing its extension may be required to build the MSI.
- Building the EXE does **not** require building the MSI. Test a new EXE before creating an installer.
- For a release candidate, choose **Release / Any CPU** and **Rebuild** the solution; verify both projects report success and the MSI under `Auto Backlight for ThinkPad Setup/Release/` is newly created.
- The application generates `Settings.cs` from `Settings.tmpl.cs` via `tmplGenerator.py`; edit the template, not the generated file.
- Do not distribute debug builds, old MSI outputs, or development patch scripts as release assets.

The project uses WPF, Windows Raw Input, Windows power notifications and Lenovo's `IBMPmDrv` interface. The optional camera feature uses the existing image/video dependencies. The modular backend architecture is intended to permit future implementations, but currently only `IBMPmDrv` is active.

## Development and releases

See [CHANGELOG.md](CHANGELOG.md) for the history of this fork and planned verification work. The in-repository `v4.x`, `v5.x` and `v6.0 Phase 1–7` labels are **development milestones**, not published product version numbers. The application assembly and current MSI are `1.2.0` (`AssemblyVersion`/`AssemblyFileVersion`: `1.2.0.0`); `AssemblyInformationalVersion` may still contain `1.2.0-dev` until the release identity is finalized. Before tagging a public release, align the informational version and test an upgrade path. GitHub Releases remain authoritative for published builds.

## Credits and license

Originally developed by **Parth Patel** (2019): [original project](https://github.com/pspatel321/auto-backlight-for-thinkpad). This fork is maintained by **prat89** and preserves upstream attribution.

Source license: **Apache License 2.0**. Read [LICENSE](LICENSE) and [NOTICE](NOTICE) for upstream and third-party notices. The `NOTICE` file is intentionally preserved.

## Release verification status (26 October 2026)

- **Tested on ThinkPad X1 Extreme Gen 2:** application startup, keyboard control and activity timeout; lid-close/lid-open behavior and input capture resumed in the observed test cycles. Intermittent suspend/resume behavior remains under observation.
- **MSI `1.2.0`:** Release rebuild completed; clean installation, tray initialization, removal with app closed, reinstall and persistence of previously saved settings were tested successfully.
- **Still required before public release:** in-place upgrade from the previous MSI; verify package metadata and installed executable match; decide and align release version identity; review signing and release assets.
- The MSI is **not digitally signed** in the tested build. Windows may show publisher/security warnings.
- `EnergyDrv` control is unsupported; no claims of compatibility with other Lenovo hardware have been validated.
