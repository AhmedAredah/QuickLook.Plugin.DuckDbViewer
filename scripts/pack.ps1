<#
.SYNOPSIS
    Builds the plugin and packages it as a .qlplugin file under artifacts\.

.DESCRIPTION
    A .qlplugin is a zip archive whose root contains the plugin assembly, its dependencies
    and QuickLook.Plugin.Metadata.config. QuickLook installs it when the file is previewed.

.PARAMETER Version
    Version to stamp into the assembly and the plugin metadata. Defaults to the version in
    Directory.Build.props.

.PARAMETER Lite
    Leaves the DuckDB extensions (SQLite, Avro, Arrow) out of the package. The plugin then
    downloads each from the official DuckDB extension repositories the first time a file of
    that format is previewed.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Lite
)

$ErrorActionPreference = 'Stop'

$pluginName = 'QuickLook.Plugin.DuckDbViewer'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot "src\$pluginName"
$buildOutput = Join-Path $project 'bin\Release'
$artifacts = Join-Path $repoRoot 'artifacts'
$staging = Join-Path $artifacts 'package'

# Start clean so files from an earlier configuration cannot leak into the package.
foreach ($dir in $buildOutput, $staging) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

$buildArgs = @('build', $project, '-c', 'Release', '--nologo', '-v', 'q')
if ($Version) { $buildArgs += "-p:Version=$Version" }
if ($Lite) { $buildArgs += '-p:BundleExtensions=false' }
dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $buildOutput '*') $staging -Recurse -Exclude '*.pdb'

$metadata = [xml](Get-Content (Join-Path $staging 'QuickLook.Plugin.Metadata.config'))
$packageVersion = $metadata.Metadata.Version
$suffix = if ($Lite) { '-lite' } else { '' }
$package = Join-Path $artifacts "$pluginName-$packageVersion$suffix.qlplugin"

if (Test-Path $package) { Remove-Item $package -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $staging, $package, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $staging -Recurse -Force

$sizeMb = [math]::Round((Get-Item $package).Length / 1MB, 1)
Write-Host "Created $package ($sizeMb MB)"
