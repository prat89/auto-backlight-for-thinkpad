# Phase 30B.5A — Update Helper preflight (no installation)

This is a deliberately **non-installing** foundation for Phase 30B.5.
The helper is a standalone .NET Framework console source file, built by the
read-only PowerShell test script **outside the repository**. It is not yet
included in the application's `.sln`, `.csproj`, or setup project.

## Supported modes

- `--selftest` — compare MSI-style versions; does not read installed state.
- `--preflight --msi PATH --sha256 HASH --current X.Y.Z --expected X.Y.Z` —
  validate a Phase 30B.4-staged MSI without executing it.

Preflight enforces a current-user UpdateStaging path with a unique 32-hex folder,
correct MSI filename, a bounded nonempty MSI file, OLE compound-file signature,
SHA-256 match, ProductName, UpgradeCode, ProductCode GUID and ProductVersion.
MSI metadata are read using read-only Windows Installer API calls.
It returns `PASS` for an expected package newer than the supplied current
version, `NOT_NEWER` (exit 3) if equal/older, and `FAIL` (exit 1) otherwise.

**A PASS is not authorization to execute an MSI.** No Authenticode signature
verification, secure elevation, user confirmation, installed-product registry
check, time-of-check/time-of-use mitigation, rollback strategy or process
relaunch exists yet. These are mandatory before enabling installation.

`INSTALL_CAPABILITY=DISABLED` is intentional. The helper never calls
`msiexec.exe`, does not start processes, does not alter Task Scheduler or
registry and does not install/uninstall software.

Do not distribute this source as a completed updater or merge it to master
before later phases are completed and tested.

## Phase 30B.5B — simulated installation plan

The helper now also supports:

- `--plan --msi PATH --sha256 HASH --current X.Y.Z --expected X.Y.Z`:
  runs the **same full read-only MSI preflight** and, only after it passes,
  prints a proposed `msiexec.exe /i <MSI> /norestart /L*v <log>` command.
  It **never executes that command**. In particular, `--current` is supplied
  by the caller and is **not** a verified locally installed version.
- `--simulate-exit CODE`: labels common Windows Installer and UAC codes,
  such as `0`, `3010`, `1602`, `1603`, `1618` and `1223`.

No `Process.Start`, ShellExecute, elevation, uninstall/install, registry write,
Task Scheduler change or application shutdown/relaunch is implemented.
The `PLANNED_ARGUMENTS` output is for audit only; do not copy and run it on
an existing installation. A planning `PASS` is neither consent nor proof that
execution is safe. The unsigned v1.3.8 MSI remains ineligible for unattended
installation.

Before a future executing helper, we still need: verified **actual** installed
product/version (not a supplied argument), independent publisher trust for
unattended installs or explicit user consent for manual test installs,
protection against changes between verification and launch, logging and
result handling, a robust shutdown/relaunch strategy, and a controlled
rollback/Task Scheduler test on an isolated machine.

## Phase 30B.5C — actual installed MSI inventory and install gates

New read-only commands:

- `--inventory`: enumerate installed products by the fixed UpgradeCode using Windows Installer API.
  Accept exactly one *installed* product with the expected ProductName; reject advertised,
  missing and multiple related products. Report `ACTUAL_INSTALLED_VERSION`.
- `--assess --msi PATH --sha256 HASH --expected X.Y.Z`: compare the **actual** registered
  version, not a supplied `--current` value, with the staged MSI. Reuse full hash, path
  and MSI metadata checks. Reject same/older MSI and unchanged ProductCode on upgrades.
- `--execute`: explicitly DENIED (`EXECUTION_POLICY=BLOCKED_BY_DESIGN`, exit 7).

The earlier `--plan` command is retained **for simulations only** and still accepts
`--current` as hypothetical input. **Never use a `--plan` success as permission to install.**

No MSI execution or process launch is present. No registry writes, Task Scheduler changes,
shutdown/relaunch, UAC prompting or installation are performed. Unsigned releases are
NOT eligible for unattended installation. The `--assess` result is a technical preflight
only and does not prove publisher authenticity, control over the MSI while Windows Installer
opens it, or safety of upgrade/rollback custom actions.

The next stage must handle explicit user approval, independent publisher-trust policy,
TOCTOU protections, elevation, MSI logging, post-install verification and rollback tests
on a disposable Windows VM before use on the development ThinkPad.

## Phase 30B.5D — publisher signature and explicit-consent execution gate

This phase introduces `--readiness` and an **intentionally blocked** `--execute` command.
Both use the **actual** installed MSI version from Windows Installer (never a caller-supplied
`--current`), reject a same/older version, and reuse the full Phase 30B.5A SHA-256 +
MSI ProductName/ProductVersion/ProductCode/UpgradeCode preflight.

Release MSI signing is NOT yet configured. The compiled-in publisher-certificate SHA-256
pin is empty, so a newer package cannot pass `TrustedPublisherSignature` at this stage.
The design uses Windows `WinVerifyTrust` under Authenticode policy and verifies the
signer's certificate fingerprint against a pre-established, hard-coded publisher pin.
A hash obtained from GitHub does not independently establish publisher authenticity.

The future explicit-consent path requires a user at a real console to type
`INSTALL X.Y.Z`, and refuses a still-running application or redirected input.
**Even after future signing, execution currently returns
`PHASE30B5D_GATE=PROTECTED_STAGING_NOT_IMPLEMENTED`** because the downloaded MSI is
held in user-writable LocalAppData and could be swapped between validation and elevation.

There is **no Process.Start / msiexec / app shutdown / registry modification** in this
phase. No MSI can be launched by this helper. The `--execute` command is a security
review interface and must NOT be described as an enabled installer. Subsequent work
must implement an elevated trusted staging boundary, publisher signing, actual launch,
logging, post-install verification, and controlled rollback tests on a disposable VM.

Example safe diagnostics (no MSI installation):

```
UpdateHelper.exe --readiness --msi PATH --sha256 SHA --expected 1.3.8
UpdateHelper.exe --execute --msi PATH --sha256 SHA --expected 1.3.8
```

Both return a nonzero status if 1.3.8 is already installed. No automatic update is enabled.
