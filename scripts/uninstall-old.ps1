#Requires -Version 5.1
<#
.SYNOPSIS
  Remove legacy Bimwright.Rvt v0.3.0-and-earlier plugin installation before upgrading to RvtMcp v0.4.0+.

.DESCRIPTION
  Cleans up addin folders and manifests installed by the legacy `Bimwright` plugin layout.
  Preserves user data (baked tools, journal, firm profiles, debug logs) in `%LOCALAPPDATA%\Bimwright\`
  for migration on next launch of v0.4.0+.

  Run this BEFORE installing v0.4.0 to avoid Revit loading both legacy + new plugins simultaneously.

.PARAMETER WhatIf
  Show what would be removed without actually removing anything.

.PARAMETER Years
  Specific Revit years to clean. Defaults to all detected years 2022-2027.

.EXAMPLE
  pwsh .\uninstall-old.ps1 -WhatIf
  pwsh .\uninstall-old.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [int[]]$Years
)

$ErrorActionPreference = 'Stop'

function Get-InstalledRevitYears {
    $detected = @()
    $root = 'HKLM:\SOFTWARE\Autodesk\Revit'
    if (-not (Test-Path $root)) { return $detected }
    foreach ($year in 2022..2027) {
        if (Test-Path (Join-Path $root "$year")) { $detected += $year }
    }
    return $detected
}

if (-not $Years -or $Years.Count -eq 0) {
    $Years = Get-InstalledRevitYears
    if ($Years.Count -eq 0) {
        $Years = 2022..2027   # try all years even if registry detection fails
    }
}

$removed = @()
$skipped = @()

foreach ($year in $Years) {
    $yearTwo = "{0:D2}" -f ($year - 2000)
    $addinsRoot = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\{0}" -f $year)
    $pluginDir = Join-Path $addinsRoot 'Bimwright'
    $addinPath = Join-Path $addinsRoot "Bimwright.R$yearTwo.addin"

    if (Test-Path $pluginDir) {
        if ($PSCmdlet.ShouldProcess($pluginDir, 'Remove legacy plugin folder')) {
            Remove-Item $pluginDir -Recurse -Force
        }
        $removed += "R$yearTwo plugin folder"
    } else {
        $skipped += "R$yearTwo plugin folder (not present)"
    }

    if (Test-Path $addinPath) {
        if ($PSCmdlet.ShouldProcess($addinPath, 'Remove legacy addin manifest')) {
            Remove-Item $addinPath -Force
        }
        $removed += "R$yearTwo addin manifest"
    } else {
        $skipped += "R$yearTwo addin manifest (not present)"
    }
}

# Server install root (separate from per-Revit-year plugin folders)
$legacyServerRoot = Join-Path $env:LOCALAPPDATA 'Bimwright\rvt\server'
if (Test-Path $legacyServerRoot) {
    if ($PSCmdlet.ShouldProcess($legacyServerRoot, 'Remove legacy server install root')) {
        Remove-Item $legacyServerRoot -Recurse -Force
    }
    $removed += "legacy server install root"
}

# Legacy global .NET tool — try to uninstall (best-effort).
try {
    $toolList = & dotnet tool list -g 2>&1
    if ($LASTEXITCODE -eq 0 -and $toolList -match 'bimwright\.rvt\.server') {
        if ($PSCmdlet.ShouldProcess('Bimwright.Rvt.Server', 'Uninstall legacy .NET global tool')) {
            & dotnet tool uninstall -g Bimwright.Rvt.Server
            if ($LASTEXITCODE -eq 0) { $removed += 'Bimwright.Rvt.Server (.NET tool)' }
        }
    }
} catch {
    Write-Warning "Could not uninstall .NET global tool Bimwright.Rvt.Server: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "=== uninstall-old.ps1 summary ==="
Write-Host ("Removed: {0}" -f ($(if ($removed.Count -gt 0) { $removed -join '; ' } else { 'nothing' })))
if ($skipped.Count -gt 0) {
    Write-Host ("Skipped: {0}" -f ($skipped -join '; '))
}
Write-Host ""
Write-Host "PRESERVED user data (will migrate on next RvtMcp launch):"
Write-Host "  %LOCALAPPDATA%\Bimwright\baked\        (custom baked tools)"
Write-Host "  %LOCALAPPDATA%\Bimwright\journal\      (session history)"
Write-Host "  %LOCALAPPDATA%\Bimwright\firm-profiles\ (firm settings)"
Write-Host "  %LOCALAPPDATA%\Bimwright\*.log         (debug logs)"
