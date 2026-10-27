# Changelog

## Unreleased — v6.0 development phases / 1.2.0 release candidate (26 October 2026)

- Standardized backlight terminology to **Off / Low / High**.
- Stabilized settings checksum handling and recovery of previously saved settings.
- Improved power-notification pause/resume handling and input-capture recovery after lid reopen; behavior remains under observation for intermittent failures.
- Added rate-limited diagnostics around `IBMPmDrv` failures without changing hardware command semantics.
- Prepared Visual Studio MSI metadata: product version `1.2.0`, maintainer attribution and repository URL, new package/product codes while preserving the upgrade family code.
- Tested Release rebuild, clean MSI install, normal app/tray startup, uninstall with the app closed, reinstall and saved-settings persistence.
- **Not tested:** in-place MSI upgrade from an older installed version. This candidate is not yet a published release.

Development milestones below refer to changes made in this fork. They are **not** promises that every milestone has been published as a GitHub Release.

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

## Planned (not yet implemented)

- Failure-mode regression tests for driver, camera, sleep/resume and reinstall/upgrade scenarios.
- Performance measurements and UI/notification-area refinement.
- In-place MSI upgrade validation; packaging metadata and release asset checks.
- Additional driver families such as `EnergyDrv` are deferred pending hardware validation.
