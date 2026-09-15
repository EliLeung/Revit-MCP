# H2-Revit installer, Apache-2.0. Installs per user; does not edit MCP host configs.
[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$Uninstall, [string]$TestRoot)
$ErrorActionPreference = 'Stop'
$version = '1.0'
$pluginId = '5e077288-82fd-4b2f-9f4e-a1849c38bb00'
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function AtomicWrite([string]$path, [string]$content) {
    $temp = $path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temp, $content, (New-Object Text.UTF8Encoding($false)))
    try {
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temp, $path, $path + '.last-backup') }
        else { [IO.File]::Move($temp, $path) }
    } finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
function AtomicCopy([string]$source, [string]$path) {
    $temp = $path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::Copy($source, $temp)
    try { [IO.File]::Replace($temp, $path, $path + '.last-backup') }
    finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
}
function FindManifest([string]$directory) {
    if (!(Test-Path -LiteralPath $directory)) { return }
    Get-ChildItem -LiteralPath $directory -Filter '*.addin' -File | ForEach-Object {
        try {
            $xml = [xml]([IO.File]::ReadAllText($_.FullName))
            if (@($xml.RevitAddIns.AddIn | Where-Object { ([string]$_.AddInId).Trim('{}') -eq $pluginId }).Count -gt 0) { $_.FullName }
        } catch { throw "Cannot inspect add-in manifest: $($_.Exception.Message)" }
    }
}
if ($TestRoot) {
    $test = [IO.Path]::GetFullPath($TestRoot)
    $dataRoot = Join-Path $test 'LocalAppData\H2-Revit'
    $addins = Join-Path $test 'AppData\Autodesk\Revit\Addins\2026'
} else {
    if ($env:OS -ne 'Windows_NT') { throw 'Windows is required.' }
    if (Get-Process -Name Revit -ErrorAction SilentlyContinue) { throw 'Save your models and close Revit before installing or restoring.' }
    $dataRoot = Join-Path $env:LOCALAPPDATA 'H2-Revit'
    $addins = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2026'
    if (!$Uninstall -and @(FindManifest (Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2026')).Count -gt 0) {
        throw 'A machine-wide rvt-mcp add-in is present. Disable it before installing this per-user edition to avoid duplicate AddInId loading.'
    }
}
$statePath = Join-Path $dataRoot 'install-state.json'
$state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json } else { $null }
if ($Uninstall) {
    if (!$state -or !$state.active) { Write-Output 'H2-Revit is not active. Nothing to restore.'; return }
    $target = [string]$state.target
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($target)) -ne [IO.Path]::GetFullPath($addins)) { throw 'Invalid saved target path.' }
    if (!(Test-Path -LiteralPath $target) -or (Hash $target) -ne $state.installedHash) { throw 'Add-in manifest changed after installation. Restore was stopped to protect newer changes.' }
    if ($state.backup -and (Hash $state.backup) -ne $state.backupHash) { throw 'Backup integrity check failed.' }
    if ($PSCmdlet.ShouldProcess($target, 'Restore previous plugin registration')) {
        if ($state.backup) { AtomicCopy $state.backup $target }
        else { [IO.File]::Move($target, (Join-Path $dataRoot ('uninstalled-' + [guid]::NewGuid().ToString('N') + '.addin'))) }
        $state.active = $false
        AtomicWrite $statePath ($state | ConvertTo-Json -Depth 5)
        Write-Output 'Restored. Package files and backups remain available. Remove any MCP host entry you added separately, then restart Revit.'
    }
    return
}
$payload = Join-Path $PSScriptRoot 'payload'
$files = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw | ConvertFrom-Json
foreach ($item in $files.PSObject.Properties) {
    $candidate = [IO.Path]::GetFullPath((Join-Path $payload $item.Name))
    if (!$candidate.StartsWith([IO.Path]::GetFullPath($payload) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid payload path.' }
    if (!(Test-Path -LiteralPath $candidate) -or (Hash $candidate) -ne $item.Value) { throw "Payload integrity check failed: $($item.Name)" }
}
$destination = Join-Path $dataRoot ('versions\' + $version)
$registrations = @(FindManifest $addins)
if ($registrations.Count -gt 1) { throw 'Multiple rvt-mcp registrations detected. Resolve duplicates before installing.' }
$target = if ($registrations.Count -eq 1) { $registrations[0] } else { Join-Path $addins 'H2-Revit.addin' }
if ($registrations.Count -eq 0 -and (Test-Path -LiteralPath $target)) { throw 'H2-Revit.addin already belongs to another registration. No files were overwritten.' }
if ($state -and $state.active) {
    if ((Hash $target) -ne $state.installedHash) { throw 'Existing registration changed; refusing to overwrite.' }
    foreach ($item in $files.PSObject.Properties) {
        if ((Hash (Join-Path $destination $item.Name)) -ne $item.Value) { throw 'Installed files changed. Restore first or inspect the installation.' }
    }
    Write-Output "H2-Revit $version is already installed."
    return
}
$assembly = [Security.SecurityElement]::Escape((Join-Path $destination 'plugin\RvtMcp.Plugin.dll'))
$registration = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns><AddIn Type="Application">
<Name>H2-Revit</Name><Assembly>$assembly</Assembly>
<FullClassName>RvtMcp.Plugin.App</FullClassName><AddInId>{$pluginId}</AddInId>
<VendorId>H2RV</VendorId><VendorDescription>H2-Revit; enhanced from bimwright/rvt-mcp (Apache-2.0)</VendorDescription>
</AddIn></RevitAddIns>
"@
if ($registrations.Count -eq 1) {
    $existing = [xml]([IO.File]::ReadAllText($target))
    if (@($existing.RevitAddIns.AddIn).Count -ne 1) { throw 'Existing manifest contains other add-ins. Split the registrations before installation.' }
}
Write-Output "Plugin and server: $destination"
Write-Output "Revit registration: $target"
if (!$PSCmdlet.ShouldProcess($target, "Install H2-Revit $version and back up existing registration")) { return }
[IO.Directory]::CreateDirectory($dataRoot) | Out-Null
[IO.Directory]::CreateDirectory($addins) | Out-Null
if (Test-Path -LiteralPath $destination) {
    foreach ($item in $files.PSObject.Properties) {
        if (!(Test-Path -LiteralPath (Join-Path $destination $item.Name)) -or (Hash (Join-Path $destination $item.Name)) -ne $item.Value) { throw 'Version folder differs from package. No files were overwritten.' }
    }
} else {
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    Copy-Item -Path (Join-Path $payload '*') -Destination $destination -Recurse
}
$backup = $null; $backupHash = $null
if (Test-Path -LiteralPath $target) {
    $backupDir = Join-Path $dataRoot ('backups\' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($backupDir) | Out-Null
    $backup = Join-Path $backupDir ([IO.Path]::GetFileName($target))
    [IO.File]::Copy($target, $backup)
    $backupHash = Hash $backup
}
$exe = Join-Path $destination 'server\H2-Revit-MCP.exe'
$commandLiteral = ConvertTo-Json -InputObject $exe -Compress
$snippet = "[mcp_servers.h2-revit]`r`ncommand = " + $commandLiteral + "`r`nargs = [`"--toolsets`", `"all`"]`r`n"
AtomicWrite (Join-Path $dataRoot 'codex-mcp.toml') $snippet
try {
    AtomicWrite $target $registration
    $newState = @{ active = $true; version = $version; target = $target; installedHash = (Hash $target); backup = $backup; backupHash = $backupHash }
    AtomicWrite $statePath ($newState | ConvertTo-Json -Depth 5)
} catch {
    if ($backup) { [IO.File]::Copy($backup, $target, $true) }
    elseif ([IO.File]::Exists($target)) { [IO.File]::Move($target, (Join-Path $dataRoot ('failed-' + [guid]::NewGuid().ToString('N') + '.addin'))) }
    throw
}
Write-Output 'Installed. Open Revit 2026 > Add-Ins > H2-Revit > Connection Manager.'
Write-Output ('Codex configuration snippet: ' + (Join-Path $dataRoot 'codex-mcp.toml'))
Write-Output 'Configure your MCP client using that snippet. Replace an existing rvt-mcp entry instead of running two equivalent entries.'
