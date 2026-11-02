---
name: Lenovo keyboard backlight compatibility report
about: Volunteer metadata-only report to help study Lenovo keyboard backlight interfaces
title: "[Compatibility] Lenovo model (replace this text)"
---

## Laptop model

- **Model / family:** (e.g. Legion 5, ThinkBook 14; do not include serial numbers)
- **Keyboard backlight:** White / RGB / Unknown
- **Windows version:** (optional)

## Scanner result

1. Download `tools/LenovoCompatibilityScanner/Scan-LenovoCompatibility.ps1` from this repository.
2. Read the [scanner instructions](https://github.com/prat89/auto-backlight-for-thinkpad/blob/master/tools/LenovoCompatibilityScanner/README.md).
3. Run the script in a **normal**, non-elevated Windows PowerShell 5.1 session.
4. Read **both** `LenovoCompatibilityReport.json` and `LenovoCompatibilitySummary.txt` before posting.
5. Paste a **sanitized summary** below; attach the JSON only if you explicitly agree to share its contents.

```text
Paste the reviewed summary here (remove anything you do not wish to publish).
```

**Optional JSON attachment:** Drag and drop the reviewed `LenovoCompatibilityReport.json` here if GitHub accepts the file type; otherwise omit it or use a sanitized code block. Do not post a link exposing files from your local machine.

## Additional information (optional)

- Does Fn+Space change the backlight? (Yes / No / Unknown)
- Any software currently controlling keyboard lighting? (Optional; no serial numbers or account details)

> The scanner reads metadata only. Service presence or WMI methods do **not** confirm compatibility. Do **not** run experimental SET commands, modify BIOS settings, or enable untested protocols as part of this issue.
