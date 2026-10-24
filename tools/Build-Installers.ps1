#requires -Version 7.0
[CmdletBinding()]
param(
 [string]$Version,
 [Parameter(Mandatory)][string]$InnoCompiler,
 [Parameter(Mandatory)][string]$HeirowInstallerCli,
 [Parameter(Mandatory)][string]$HeirowInstallerLauncher,
 [Parameter(Mandatory)][string]$HeirowUpdater,
 [Parameter(Mandatory)][string]$SigningKey,
 [string]$PackageCache,
 [switch]$PayloadOnly
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(-not $Version){$Version=([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version}
if($Version -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'A four-part release version is required.'}
foreach($inputPath in @($InnoCompiler,$HeirowInstallerCli,$HeirowInstallerLauncher,$HeirowUpdater,$SigningKey)){
 if(-not(Test-Path -LiteralPath $inputPath -PathType Leaf)){throw 'A required build tool or signing input is missing.'}
}
$payloadRoot=Join-Path $repo "artifacts/publish-$Version"
$backendRoot=Join-Path $repo "artifacts/backend-$Version"
$releaseRoot=Join-Path $repo "artifacts/release-$Version"
New-Item -ItemType Directory -Force -Path $payloadRoot,$backendRoot,$releaseRoot|Out-Null
$products=@(
 @{Id='heirowDebug';Name='heirowDebug 2026';Folder='heirowDebug';Project='heirowDebug.Injector/heirowDebug.Injector.csproj';Exe='heirowDebug.exe';InnoId='heirow.heirowDebug';Icon='heirowDebug-Injector.ico'},
 @{Id='heirowDotNetShield';Name='heirowDotNetShield 2026';Folder='heirowDotNetShield';Project='heirowDotNetShield/heirowDotNetShield.csproj';Exe='heirowDotNetShield.exe';InnoId='heirow.heirowDotNetShield';Icon='heirowDotNetShield.ico'},
 @{Id='heirowDebug.ProjectZ';Name='heirowDebug Project-Z';Folder='heirowDebug-ProjectZ';Project='heirowDebug.ProjectZ.Tools/heirowDebug.ProjectZ.Tools.csproj';Exe='heirowDebug.ProjectZ.Tools.exe';InnoId='heirow.heirowDebug.ProjectZ';Icon='heirowDebug-ProjectZ.ico'}
)
$receipts=@()
foreach($product in $products){
 $payload=Join-Path $payloadRoot $product.Folder
 $setup=Join-Path $releaseRoot "$($product.Folder)-$Version-windows-x64-Setup.exe"
 if(Test-Path -LiteralPath $setup){throw 'An immutable setup already exists; use a new release version.'}
 $publishArgs=@('publish',(Join-Path $repo $product.Project),'-c','Release','-r','win-x64','--self-contained','true','-m:1','-nodeReuse:false','-p:UseSharedCompilation=false',"-p:Version=$Version",'-p:DebugType=embedded','-p:CopyOutputSymbolsToPublishDirectory=false','-o',$payload,'--nologo')
 if($PackageCache){$publishArgs+="-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))"}
 & dotnet @publishArgs
 if($LASTEXITCODE -ne 0){throw "Application publish failed: $($product.Id)"}
 if($PayloadOnly){continue}
 $compileArgs=@("/DProductId=$($product.InnoId)","/DProductName=$($product.Name)","/DProductVersion=$Version","/DProductFolder=$($product.Folder)","/DExecutableName=$($product.Exe)","/DBrandIcon=$(Join-Path $repo ('Assets/'+$product.Icon))","/DOutputDirectory=$backendRoot","/DPublishDirectory=$payload",(Join-Path $repo 'packaging/windows.iss'))
 & $InnoCompiler @compileArgs
 if($LASTEXITCODE -ne 0){throw "Original installer backend failed: $($product.Id)"}
 $backend=Join-Path $backendRoot "$($product.Folder)-$Version-windows-x64-Setup.exe"
 $packagePayload=Join-Path $releaseRoot ($product.Id+'-payload')
 if(Test-Path -LiteralPath $packagePayload){throw 'A standalone payload directory already exists.'}
 New-Item -ItemType Directory -Path $packagePayload|Out-Null
 Copy-Item -LiteralPath $backend -Destination (Join-Path $packagePayload 'Application.Setup.exe')
 Copy-Item -LiteralPath $HeirowUpdater -Destination (Join-Path $packagePayload 'heirowUpdater.exe')
 $silent=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART')
 $manifest=[ordered]@{
  formatVersion=1;id=$product.Id;name=$product.Name;version=$Version;publisher='SocketJack';scope='user'
  description='Self-contained Windows application with optional updates.'
  installDirectory=('{LocalAppData}/Programs/heirow/'+$product.Folder);entryPoint=$product.Exe
  homepage='https://github.com/hheiroww/heirowDebug'
  updateUrl=('https://socketjack.com/installers/'+$product.Id+'/update.json')
  features=@(@{id='updates';name='Application updates';description='Check for updates after the application exits.';required=$false;default=$true})
  updateCompanion=@{feature='updates';mode='after-exit';authApplication='heirowinstaller';credentialOrigin='https://socketjack.com';tokenEnvironmentVariable='HEIROWUPDATER_TOKEN';cookieEnvironmentVariable='HEIROWUPDATER_COOKIE'}
  legacyExecutable=@{
   payloadPath='Application.Setup.exe';installArguments=$silent;silentInstallArguments=$silent
   installDirectoryArgument='/DIR={InstallDir}';repairArguments=$silent
   uninstallExecutable='{InstallDir}/unins000.exe';uninstallArguments=$silent;successExitCodes=@(0,3010)
   maintenanceRegistration=@{hive='user';view='registry64';uninstallKey=('Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$product.InnoId+'_is1')}
  }
  payload=@(@{source='Application.Setup.exe';path='Application.Setup.exe'},@{source='heirowUpdater.exe';path='heirowUpdater.exe';feature='updates'})
 }
 $manifestPath=Join-Path $releaseRoot ($product.Id+'-installer.json')
 $manifest|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
 $package=Join-Path $releaseRoot "$($product.Id)-$Version.hinst"
 & $HeirowInstallerCli pack $manifestPath --source $packagePayload --output $package --sign-key ([IO.Path]::GetFullPath($SigningKey))
 if($LASTEXITCODE -ne 0){throw "Signed package build failed: $($product.Id)"}
 & $HeirowInstallerCli bundle $package --launcher ([IO.Path]::GetFullPath($HeirowInstallerLauncher)) --output $setup
 if($LASTEXITCODE -ne 0){throw "Standalone setup build failed: $($product.Id)"}
 & $HeirowInstallerCli verify $setup
 if($LASTEXITCODE -ne 0){throw "Standalone setup verification failed: $($product.Id)"}
 $receipts+=@{id=$product.Id;version=$Version;installer=$setup;package=$package;bytes=(Get-Item -LiteralPath $setup).Length;sha256=(Get-FileHash -LiteralPath $setup).Hash;backendSha256=(Get-FileHash -LiteralPath $backend).Hash;updaterSha256=(Get-FileHash -LiteralPath $HeirowUpdater).Hash}
}
if(-not $PayloadOnly){
 $receipts|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $releaseRoot 'installer-build.json') -Encoding utf8NoBOM
 $receipts|ForEach-Object { "$($_.sha256.ToLowerInvariant())  $([IO.Path]::GetFileName($_.installer))" }|Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
 $receipts
}
