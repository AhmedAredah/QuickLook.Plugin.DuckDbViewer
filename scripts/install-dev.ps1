<#
.SYNOPSIS
    Builds the plugin and installs it into the local QuickLook for manual testing.

.DESCRIPTION
    QuickLook keeps plugin DLLs loaded, so it is stopped before the files are replaced and
    started again afterwards. Works with the Microsoft Store and the MSI/portable editions.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'

$pluginName = 'QuickLook.Plugin.DuckDbViewer'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $repoRoot "src\$pluginName"

dotnet build $project -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# Locate the QuickLook user data folder (see the wiki: "Differences Between Distributions").
$storePackage = Get-AppxPackage -Name '*PaddyXu.QuickLook*' | Select-Object -First 1
$dataDir = if ($storePackage) {
    Join-Path $env:LOCALAPPDATA "Packages\$($storePackage.PackageFamilyName)\LocalCache\Roaming\pooi.moe\QuickLook"
} else {
    Join-Path $env:APPDATA 'pooi.moe\QuickLook'
}
$target = Join-Path $dataDir "QuickLook.Plugin\$pluginName"

$running = Get-Process -Name QuickLook -ErrorAction SilentlyContinue
$exePath = $running | Select-Object -First 1 -ExpandProperty Path
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# Native libraries stay locked for a moment after the process is gone.
for ($attempt = 1; (Test-Path $target); $attempt++) {
    try {
        Remove-Item $target -Recurse -Force
    } catch {
        if ($attempt -ge 20) { throw }
        Start-Sleep -Milliseconds 500
    }
}
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $project "bin\$Configuration\*") $target -Recurse -Exclude '*.pdb'
Write-Host "Installed to $target"

if ($NoRestart) { return }

if ($storePackage) {
    Start-Process "shell:AppsFolder\$($storePackage.PackageFamilyName)!Main"
} elseif ($exePath) {
    Start-Process $exePath
} else {
    Write-Host 'QuickLook was not running; start it manually to load the plugin.'
}
