<#
.SYNOPSIS
    Downloads the DuckDB extensions that are bundled with the plugin.

.DESCRIPTION
    Extensions are fetched from the official DuckDB extension repositories and cached under
    artifacts\extensions\. The build runs this automatically when the cache is empty.
    Extension binaries are tied to an exact DuckDB version, so the version must match the
    engine shipped by DuckDB.NET (see DuckDbVersion in Directory.Build.props).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DuckDbVersion,
    [string] $Platform = 'windows_amd64',
    # Comma-separated "name:repository" pairs; the repository is "core" or "community".
    [string] $Extensions = 'sqlite_scanner:core,avro:core,nanoarrow:community'
)

$repositories = @{
    core      = 'http://extensions.duckdb.org'
    community = 'http://community-extensions.duckdb.org'
}

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$targetDir = Join-Path $PSScriptRoot "..\artifacts\extensions\v$DuckDbVersion\$Platform"
New-Item -ItemType Directory -Force $targetDir | Out-Null

# The build may run this for several target frameworks at once; download one at a time.
$lock = New-Object System.Threading.Mutex($false, 'DuckDbViewer.FetchExtensions')
[void]$lock.WaitOne()
try {

foreach ($entry in $Extensions.Split(',')) {
    $name, $repository = $entry.Trim().Split(':')
    if (-not $repositories.ContainsKey($repository)) { throw "Unknown extension repository '$repository' for '$name'." }
    $target = Join-Path $targetDir "$name.duckdb_extension"
    if (Test-Path $target) {
        Write-Host "$name already cached."
        continue
    }

    $url = "$($repositories[$repository])/v$DuckDbVersion/$Platform/$name.duckdb_extension.gz"
    $archive = "$target.gz"
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing

    $source = [System.IO.File]::OpenRead($archive)
    try {
        $gzip = New-Object System.IO.Compression.GZipStream($source, [System.IO.Compression.CompressionMode]::Decompress)
        $output = [System.IO.File]::Create("$target.tmp")
        try { $gzip.CopyTo($output) } finally { $output.Dispose(); $gzip.Dispose() }
    }
    finally { $source.Dispose() }

    Move-Item "$target.tmp" $target -Force
    Remove-Item $archive -Force
    Write-Host "Cached $target"
}

} finally {
    $lock.ReleaseMutex()
    $lock.Dispose()
}
