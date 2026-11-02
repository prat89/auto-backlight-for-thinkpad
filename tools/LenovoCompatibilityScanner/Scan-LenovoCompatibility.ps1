# Phase 29C.1 -- Lenovo Keyboard Backlight Compatibility Scanner
# Windows PowerShell 5.1. Metadata-only discovery; NEVER invokes firmware methods or opens drivers.
# Output is written to Downloads\LenovoBacklightStage outside the Git repository.
[CmdletBinding()]
param(
    [switch]$NoBios
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Clean-Value {
    param([object]$Value, [int]$MaxLength = 120)
    if ($null -eq $Value) { return 'unavailable' }
    $valueString = ([string]$Value) -replace '[\x00-\x1f\x7f]', ' '
    $valueString = $valueString.Trim()
    if ($valueString.Length -gt $MaxLength) { $valueString = $valueString.Substring(0, $MaxLength) }
    if (-not $valueString) { return 'unavailable' }
    return $valueString
}

function Find-GuidTokens {
    param([object]$Qualifiers)
    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($qualifier in @($Qualifiers)) {
        if ($null -eq $qualifier) { continue }
        if ([string]$qualifier.Name -notmatch '^(?i:Guid|UUID|WmiGuid)$') { continue }
        foreach ($guidToken in [regex]::Matches([string]$qualifier.Value, '(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')) {
            $g = $guidToken.Value.ToUpperInvariant()
            if (-not $result.Contains($g)) { $result.Add($g) }
        }
    }
    return $result.ToArray()
}

$homeDir = [Environment]::GetFolderPath('UserProfile')
if (-not $homeDir) { throw 'Cannot determine user profile directory.' }
$downloads = Join-Path $homeDir 'Downloads'
$stageRoot = Join-Path $downloads 'LenovoBacklightStage\Phase29C_CompatibilityScanner'
$knownRepo = Join-Path $homeDir 'Documents\GitHub\auto-backlight-for-thinkpad'
$stagePath = [IO.Path]::GetFullPath($stageRoot).TrimEnd('\') + '\'
$repoPath = [IO.Path]::GetFullPath($knownRepo).TrimEnd('\') + '\'
if ($stagePath.StartsWith($repoPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to write scanner output into the application repository.'
}
foreach ($commandName in @('Get-CimClass', 'Get-CimInstance', 'ConvertTo-Json')) {
    if (-not (Get-Command -Name $commandName -ErrorAction SilentlyContinue)) {
        throw ('Required Windows PowerShell command unavailable: ' + $commandName)
    }
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')
$runDir = Join-Path $stageRoot ('run_' + $stamp)
if (Test-Path -LiteralPath $runDir) { throw 'Refusing to overwrite an existing run directory.' }
New-Item -ItemType Directory -Path $runDir -ErrorAction Stop | Out-Null

$warnings = New-Object 'System.Collections.Generic.List[string]'
$scans = New-Object 'System.Collections.Generic.List[object]'
$classMatches = New-Object 'System.Collections.Generic.List[object]'
$targets = @(
    '8C5B9127-ECD4-4657-980F-851019F99CA5',
    '887B54E3-DDDC-4B2C-8B88-68A26A8835D0'
)
$device = [ordered]@{ Manufacturer = 'unavailable'; Model = 'unavailable'; BIOSVersion = 'unavailable'; OSVersion = 'unavailable' }

# Read only these named properties. Never retrieve serials, hostnames, users, MACs, disk IDs or instance paths.
try {
    $cs = Get-CimInstance -Namespace 'root\cimv2' -ClassName 'Win32_ComputerSystem' -Property Manufacturer,Model -ErrorAction Stop
    if ($cs) {
        $device.Manufacturer = Clean-Value $cs.Manufacturer
        $device.Model = Clean-Value $cs.Model
    }
} catch { $warnings.Add('Device model metadata unavailable (' + $_.Exception.GetType().Name + ').') }
if (-not $NoBios) {
    try {
        $bios = Get-CimInstance -Namespace 'root\cimv2' -ClassName 'Win32_BIOS' -Property SMBIOSBIOSVersion -ErrorAction Stop
        if ($bios) { $device.BIOSVersion = Clean-Value $bios.SMBIOSBIOSVersion }
    } catch { $warnings.Add('BIOS version unavailable (' + $_.Exception.GetType().Name + ').') }
} else { $device.BIOSVersion = 'omitted-by-user' }
try {
    $os = Get-CimInstance -Namespace 'root\cimv2' -ClassName 'Win32_OperatingSystem' -Property Version -ErrorAction Stop
    if ($os) { $device.OSVersion = Clean-Value $os.Version }
} catch { $warnings.Add('Windows version unavailable (' + $_.Exception.GetType().Name + ').') }

# Win32_SystemDriver is service metadata, not a handle to the device. Do not record executable paths.
$driverNames = @('IBMPmDrv', 'EnergyDrv')
$drivers = New-Object 'System.Collections.Generic.List[object]'
foreach ($driverName in $driverNames) {
    $entry = [ordered]@{ Name = $driverName; Observation = 'unknown'; State = $null; StartMode = $null }
    try {
        $items = @(Get-CimInstance -Namespace 'root\cimv2' -ClassName 'Win32_SystemDriver' -Filter ("Name='" + $driverName + "'") -ErrorAction Stop)
        $found = @($items | Where-Object { $null -ne $_ -and [string]$_.Name -ieq $driverName })
        if ($found.Count -gt 0) {
            $entry.Observation = 'service-present'
            $entry.State = Clean-Value $found[0].State 40
            $entry.StartMode = Clean-Value $found[0].StartMode 40
        } else { $entry.Observation = 'not-observed' }
    } catch {
        $entry.Observation = 'query-error'
        $warnings.Add('Unable to inspect ' + $driverName + ' service metadata (' + $_.Exception.GetType().Name + ').')
    }
    $drivers.Add($entry)
}

# Search core namespaces and a bounded set of additional Lenovo-named namespaces.
# __NAMESPACE is used exclusively to enumerate namespace names; it does not invoke firmware APIs.
$namespaces = New-Object 'System.Collections.Generic.List[string]'
$namespaces.Add('root\wmi')
$namespaces.Add('root\cimv2')
$vendorNamespacePattern = '(?i)^(lenovo|legion|gamezone|lighting|backlight)'
$maxAdditionalNamespaces = 8
foreach ($baseNamespace in @('root', 'root\wmi', 'root\cimv2')) {
    try {
        $children = @(Get-CimInstance -Namespace $baseNamespace -ClassName '__NAMESPACE' -ErrorAction Stop)
        foreach ($child in $children) {
            if ($null -eq $child) { continue }
            $childName = [string]$child.Name
            if ($childName -notmatch $vendorNamespacePattern) { continue }
            $candidate = $baseNamespace + '\' + $childName
            if (-not $namespaces.Contains($candidate) -and $namespaces.Count -lt (2 + $maxAdditionalNamespaces)) {
                $namespaces.Add($candidate)
            }
        }
    } catch {
        $warnings.Add('Namespace discovery unavailable under ' + $baseNamespace + ' (' + $_.Exception.GetType().Name + ').')
    }
}
# Look one level below additional vendor namespaces, with a hard cap.
$vendorRoots = @($namespaces.ToArray() | Where-Object { $_ -notin @('root\wmi', 'root\cimv2') })
foreach ($baseNamespace in $vendorRoots) {
    if ($namespaces.Count -ge (2 + $maxAdditionalNamespaces)) { break }
    try {
        $children = @(Get-CimInstance -Namespace $baseNamespace -ClassName '__NAMESPACE' -ErrorAction Stop)
        foreach ($child in $children) {
            if ($null -eq $child) { continue }
            $childName = [string]$child.Name
            if ($childName -notmatch $vendorNamespacePattern) { continue }
            $candidate = $baseNamespace + '\' + $childName
            if (-not $namespaces.Contains($candidate) -and $namespaces.Count -lt (2 + $maxAdditionalNamespaces)) {
                $namespaces.Add($candidate)
            }
        }
    } catch {
        $warnings.Add('Nested namespace discovery unavailable under ' + $baseNamespace + ' (' + $_.Exception.GetType().Name + ').')
    }
}

$namePattern = '(?i)lenovo|legion|gamezone|keyboard.{0,16}light|backlight|lighting|lamparray'
$methodPattern = '(?i)keyboard.{0,16}light|backlight|lightingfeature|lamparray'
foreach ($ns in $namespaces) {
    $seen = 0
    $count = 0
    try {
        $classes = @(Get-CimClass -Namespace $ns -ErrorAction Stop)
        foreach ($c in $classes) {
            if ($null -eq $c) { continue }
            $seen += 1
            $className = [string]$c.CimClassName
            $classGuids = @(Find-GuidTokens $c.CimClassQualifiers)
            $guidHit = $false
            foreach ($g in $classGuids) { if ($targets -contains $g) { $guidHit = $true; break } }
            $allMethods = @($c.CimClassMethods)
            $methodHit = $false
            foreach ($m in $allMethods) {
                if ($null -ne $m -and [string]$m.Name -match $methodPattern) { $methodHit = $true; break }
            }
            if (-not ($guidHit -or ($className -match $namePattern) -or $methodHit)) { continue }
            $count += 1
            $methodMetadata = New-Object 'System.Collections.Generic.List[object]'
            foreach ($m in $allMethods) {
                if ($null -eq $m) { continue }
                $methodId = $null
                foreach ($q in @($m.Qualifiers)) {
                    if ($null -ne $q -and [string]$q.Name -match '^(?i:WmiMethodId|ID)$') {
                        $methodId = Clean-Value $q.Value 40
                    }
                }
                # Only method names and method IDs; no parameters, values or WMI invocations.
                $methodMetadata.Add([ordered]@{ Name = Clean-Value $m.Name 120; WmiMethodId = $methodId })
            }
            $classMatches.Add([ordered]@{
                Namespace = $ns
                ClassName = Clean-Value $className 200
                Guids = @($classGuids)
                TargetGuidMatch = [bool]$guidHit
                MethodMetadataOnly = @($methodMetadata.ToArray())
            })
        }
        $scans.Add([ordered]@{ Namespace = $ns; Status = 'OK'; EnumeratedClasses = $seen; MatchingClasses = $count })
    } catch {
        $scans.Add([ordered]@{ Namespace = $ns; Status = 'ERROR'; EnumeratedClasses = $seen; MatchingClasses = $count })
        $warnings.Add('Unable to enumerate class metadata under ' + $ns + ' (' + $_.Exception.GetType().Name + ').')
    }
}

$detectedGuid = @{}
foreach ($g in $targets) { $detectedGuid[$g] = $false }
foreach ($m in $classMatches) {
    foreach ($g in $m.Guids) { if ($targets -contains $g) { $detectedGuid[$g] = $true } }
}
$report = [ordered]@{
    Schema = 'LenovoCompatibilityScanner-v1'
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Safety = 'Read-only system/driver-service/CIM class metadata. No firmware methods, driver handles, IOCTL, registry modification or uploads.'
    Device = $device
    Drivers = @($drivers.ToArray())
    TargetGuids = $targets
    Scans = @($scans.ToArray())
    Matches = @($classMatches.ToArray())
    Warnings = @($warnings.ToArray())
    HardwareCompatibilityConfirmed = $false
}
$jsonFile = Join-Path $runDir 'LenovoCompatibilityReport.json'
$summaryFile = Join-Path $runDir 'LenovoCompatibilitySummary.txt'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFile -Encoding UTF8

$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add('LENOVO_COMPATIBILITY_SCAN=COMPLETE')
$lines.Add('SCHEMA=LenovoCompatibilityScanner-v1')
$lines.Add('MODE=READ_ONLY_METADATA; NO HARDWARE COMPATIBILITY CONFIRMATION')
$lines.Add('MODEL=' + $device.Manufacturer + ' / ' + $device.Model)
$lines.Add('BIOS=' + $device.BIOSVersion)
$lines.Add('WINDOWS=' + $device.OSVersion)
foreach ($driver in $drivers) {
    $lines.Add('DRIVER=' + $driver.Name + ';OBSERVATION=' + $driver.Observation + ';STATE=' + $driver.State + ';START_MODE=' + $driver.StartMode)
}
foreach ($s in $scans) {
    $lines.Add('WMI_NAMESPACE=' + $s.Namespace + ';STATUS=' + $s.Status + ';CLASSES=' + $s.EnumeratedClasses + ';MATCHES=' + $s.MatchingClasses)
}
foreach ($g in $targets) { $lines.Add('TARGET_GUID=' + $g + ';OBSERVED=' + $detectedGuid[$g]) }
$lines.Add('TOTAL_WMI_MATCHES=' + $classMatches.Count)
foreach ($m in $classMatches) {
    if ($m.TargetGuidMatch -or ($m.ClassName -match $namePattern)) {
        $lines.Add('WMI_CLASS=' + $m.Namespace + '/' + $m.ClassName + ';GUIDS=' + ($m.Guids -join ','))
    }
}
foreach ($w in $warnings) { $lines.Add('WARNING=' + $w) }
$lines.Add('NOTE=Driver service presence or WMI class presence does NOT prove keyboard backlight compatibility.')
$lines.Add('PRIVACY=Check JSON before posting: model, BIOS, OS and WMI metadata may identify a specific device type.')
$lines | Set-Content -LiteralPath $summaryFile -Encoding UTF8

$lines | ForEach-Object { Write-Output $_ }
Write-Output ('JSON_REPORT=' + $jsonFile)
Write-Output ('SUMMARY=' + $summaryFile)
Write-Output 'No repository files or device settings were changed. No report was uploaded.'
