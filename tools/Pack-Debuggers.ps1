[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts\nuget' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
foreach ($project in @('heirowDebug.WPF.vbproj', 'heirowDebug.ProjectZ\heirowDebug.ProjectZ.csproj')) {
    & dotnet pack (Join-Path $repo $project) -c Release -o $OutputDirectory -p:ContinuousIntegrationBuild=true -p:GeneratePackageOnBuild=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Pack failed: $project" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($id in @('heirowDebug.WPF', 'heirowDebug.ProjectZ')) {
    $package = Join-Path $OutputDirectory "$id.$version.nupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $names = @($zip.Entries.FullName)
        if (-not ($names -match "^lib/.+/$([regex]::Escape($id)).dll$")) { throw "Library missing in $id" }
        if ($names -match '(?i)(^|/)(\.vs|obj|bin|artifacts|\.codex)/|\.jsonl$|\.log$|clipboard|\.pfx$|\.pem$') { throw "Forbidden package entry in $id" }
        if (-not ($names -contains 'README.md')) { throw "Readme missing in $id" }
    }
    finally { $zip.Dispose() }
    Get-Item -LiteralPath $package | Select-Object Name, Length
}
