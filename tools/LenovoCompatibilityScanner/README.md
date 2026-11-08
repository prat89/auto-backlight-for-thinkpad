# Lenovo Keyboard Backlight Compatibility Scanner

**Experimental, metadata-only scanner. Not a hardware compatibility test.**

This optional tool is part of the [Auto Backlight for ThinkPad](https://github.com/prat89/auto-backlight-for-thinkpad) compatibility research. It helps identify potentially relevant Lenovo firmware interfaces on Windows laptops (ThinkPad, ThinkBook, IdeaPad, Yoga, Legion, LOQ), without installing the main application.

## For volunteers

1. Download **`Scan-LenovoCompatibility.ps1`** from this project. Review the script if desired.
2. Open **Windows PowerShell 5.1** normally (administrator privileges are not required).
3. Change directory to the downloaded script, for example:

   ```powershell
   cd "$env:USERPROFILE\Downloads"
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Scan-LenovoCompatibility.ps1"
   ```

   To omit the BIOS version from the report, add `-NoBios` after the filename.

4. The script writes two files to a timestamped directory **outside the application repository**:

   `Downloads\LenovoBacklightStage\Phase29C_CompatibilityScanner\run_...\`

   - `LenovoCompatibilityReport.json` — structured report (share this only after reviewing it).
   - `LenovoCompatibilitySummary.txt` — human-readable summary.

5. Open and **review both files** before sharing; their model, BIOS version and firmware metadata can be distinctive. Attach the JSON to a GitHub issue *only if you choose to*. Nothing is automatically uploaded.

**No Python installation is required for volunteers.** The Python report analyzer is an optional maintainer tool.

## What it reads

- Lenovo device manufacturer and model, OS version, optionally BIOS version.
- Service metadata (name, state and start mode only) for `IBMPmDrv` and `EnergyDrv` via Windows `Win32_SystemDriver`.
- WMI class definitions, matching GUIDs, method *names* and their declared IDs in `root\wmi`, `root\cimv2`, and a limited number of vendor-named namespaces, when available.
- Known candidate interfaces:
  - `8C5B9127-ECD4-4657-980F-851019F99CA5` — legacy Legion `BA` WMI interface.
  - `887B54E3-DDDC-4B2C-8B88-68A26A8835D0` — Lenovo Gamezone WMI interface.

## What it never does

The scanner **never** invokes a WMI firmware method (including `GetKeyboardLight`), opens driver device handles, issues IOCTL commands, changes keyboard brightness, modifies BIOS/registry/services, installs drivers, asks for elevation, or sends any data to the internet. It only writes local report files.

It deliberately excludes usernames, computer names, serial numbers, user SIDs, device IDs, driver binary paths and MAC addresses. It does **not** enumerate WMI *instance values* except selected system descriptors and service states. WMI namespace enumeration reads namespace names, not firmware methods.

Security note: as with any script, only run a version obtained from a trusted source, and inspect it before execution. Avoid pasting scripts from unknown sources into an elevated console.

## Understanding results

- `service-present` means a named driver service exists; **not** that its device is accessible or keyboard-lighting protocol works.
- `not-observed` means the service/interface was not found *within this scan*, not that your laptop cannot support it.
- A WMI class or method declaration **does not** establish hardware support or permission to invoke it.
- WMI namespace discovery is selective and bounded; the scanner does not exhaustively search every possible vendor namespace or HID interface.
- `HardwareCompatibilityConfirmed` is always `false`.
- WMI query errors and access restrictions are reported as warnings without embedding exception messages or local filesystem paths.

## Maintainer workflow

The optional analyzer only reads local JSON and prints a summary:

```powershell
py -3 .\Analyze-LenovoCompatibilityReports.py "C:\path\to\LenovoCompatibilityReport.json"
```

Multiple JSON paths can be supplied in the same invocation. The analyzer performs basic schema and identifying-data checks, but **manual review is still required** before publishing data. It does not execute any hardware operations or create files.

## Research references

- [Microsoft: Get-CimClass](https://learn.microsoft.com/en-us/powershell/module/cimcmdlets/get-cimclass)
- [Microsoft: Win32_SystemDriver](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-systemdriver)
- [Linux kernel: Lenovo WMI Gamezone](https://www.kernel.org/doc/html/next/wmi/devices/lenovo-wmi-gamezone.html)
- [LenovoLegionLinux](https://github.com/johnfanv2/LenovoLegionLinux)

## Project status

This is an **experimental, read-only volunteer-research tool**, included in the v1.3.9 source repository under `tools/LenovoCompatibilityScanner/`. It is **not bundled in the v1.3.9 MSI installer**, and it does not establish additional Lenovo model compatibility. The initial ThinkPad X1 Extreme Gen 2 scan is a research baseline only; Legion/Gamezone interfaces have not been validated on other laptops. The phase-labelled output directory shown above is the existing scanner script's output path, not a release or hardware-compatibility claim.
