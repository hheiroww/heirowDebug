[CmdletBinding()]
param(
    [string]$IsccPath = 'ISCC.exe',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts\release-$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$payloadRoot = Join-Path $repo "artifacts\publish-$version"
New-Item -ItemType Directory -Force $OutputDirectory, $payloadRoot | Out-Null
$compiler = (Get-Command $IsccPath -ErrorAction Stop).Source
$products = @(
    @{ Folder='heirowDebug'; Name='heirowDebug 2026'; Id='heirow.heirowDebug'; Project='heirowDebug.Injector\heirowDebug.Injector.csproj'; Exe='heirowDebug.exe'; Icon='heirowDebug-Injector.ico' },
    @{ Folder='heirowDotNetShield'; Name='heirowDotNetShield 2026'; Id='heirow.heirowDotNetShield'; Project='heirowDotNetShield\heirowDotNetShield.csproj'; Exe='heirowDotNetShield.exe'; Icon='heirowDotNetShield.ico' },
    @{ Folder='heirowDebug-ProjectZ'; Name='heirowDebug Project-Z Sample'; Id='heirow.heirowDebug.ProjectZ'; Project='heirowDebug.Project-z\heirowDebug.Project-z.vbproj'; Exe='heirowDebug.ProjectZ.exe'; Icon='heirowDebug-ProjectZ.ico' }
)
foreach ($product in $products) {
    $publish = Join-Path $payloadRoot $product.Folder
    # Never mix a previous publish with the release being built.
    if (Test-Path -LiteralPath $publish) {
        throw "Publish directory already exists: $publish. Choose a fresh version or archive that directory before rebuilding."
    }
    & dotnet publish (Join-Path $repo $product.Project) -c Release -r win-x64 --self-contained true -o $publish -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($product.Name)" }
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $publish
    & $compiler "/DProductId=$($product.Id)" "/DProductName=$($product.Name)" "/DProductFolder=$($product.Folder)" "/DProductVersion=$version" "/DExecutableName=$($product.Exe)" "/DBrandIcon=$(Join-Path $repo "Assets\$($product.Icon)")" "/DPublishDirectory=$publish" "/DOutputDirectory=$OutputDirectory" (Join-Path $repo 'packaging\windows.iss')
    if ($LASTEXITCODE -ne 0) { throw "Installer failed: $($product.Name)" }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $OutputDirectory "$($product.Folder)-$version-windows-x64.zip") -CompressionLevel Optimal
}
# The DLL downloads are intentionally unmodified assemblies from the same publish.
$debuggerPayload = Join-Path $payloadRoot 'heirowDebug'
foreach ($dll in @('heirowDebug.WPF.dll', 'heirowDebug.Injector.Hook.dll')) {
    Copy-Item -LiteralPath (Join-Path $debuggerPayload $dll) -Destination $OutputDirectory
}
& dotnet pack (Join-Path $repo 'heirowDebug.WPF.vbproj') -c Release -o $OutputDirectory -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Library packaging failed' }
$checksums = Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums)
Get-ChildItem -LiteralPath $OutputDirectory -File | Select-Object Name,Length
