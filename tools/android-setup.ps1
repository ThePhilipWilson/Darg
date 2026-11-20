<#
.SYNOPSIS
  Installs what building Darg for Android needs, without admin (AND-0003). Run once; it skips what's there.

.DESCRIPTION
  The system dotnet (C:\Program Files\dotnet) can't take the android workload without elevation, so this puts
  a dotnet of its own, with the workload, and an Android SDK under one folder (D:\Android, or DARG_ANDROID_ROOT):

    <root>\dotnet   .NET 10 SDK + the android workload
    <root>\sdk      Android SDK: platform-tools, the platform and build-tools the workload asks for
    <root>\avd      (with -Emulator) an emulator image and an AVD named darg

  tools/android.ps1 uses that dotnet and SDK. Java comes from JAVA_HOME (JDK 17 or 21).

.EXAMPLE
  pwsh -File tools/android-setup.ps1            # dotnet, workload, SDK
  pwsh -File tools/android-setup.ps1 -Emulator  # and an emulator to try it on with no phone
#>
param([switch]$Emulator)
$ErrorActionPreference = 'Stop'

$root = if ($env:DARG_ANDROID_ROOT) { $env:DARG_ANDROID_ROOT } else { 'D:\Android' }
$dotnetDir = Join-Path $root 'dotnet'
$sdkDir = Join-Path $root 'sdk'
$dotnet = Join-Path $dotnetDir 'dotnet.exe'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Darg.Android\Darg.Android.csproj'
New-Item -ItemType Directory -Force $root, $sdkDir | Out-Null
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (-not (Test-Path -LiteralPath $dotnet)) {
    Write-Host "== .NET 10 SDK into $dotnetDir"
    $installer = Join-Path $root 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 10.0 -InstallDir $dotnetDir -NoPath
}
# A dotnet run from its own folder still looks for workloads in the system one unless told where its root is.
$env:DOTNET_ROOT = $dotnetDir
$env:DOTNET_MULTILEVEL_LOOKUP = '0'

$workloads = & $dotnet workload list 2>&1 | Out-String
if ($workloads -notmatch '(?m)^\s*android\s') {
    Write-Host "== android workload"
    & $dotnet workload install android --skip-sign-check
    if ($LASTEXITCODE) { throw "workload install failed ($LASTEXITCODE)" }
}

Write-Host "== Android SDK into $sdkDir (what the workload asks for)"
& $dotnet build $project -t:InstallAndroidDependencies -f net10.0-android "-p:AndroidSdkDirectory=$sdkDir" -p:AcceptAndroidSDKLicenses=True -v:minimal
if ($LASTEXITCODE) { throw "InstallAndroidDependencies failed ($LASTEXITCODE)" }

if ($Emulator) {
    $sdkmanager = Get-ChildItem -LiteralPath $sdkDir -Recurse -Filter sdkmanager.bat -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sdkmanager) { throw "no sdkmanager.bat under $sdkDir" }
    $image = 'system-images;android-36;google_apis;x86_64'
    Write-Host "== emulator and $image"
    ("y`n" * 20) |& $sdkmanager.FullName "--sdk_root=$sdkDir" 'emulator' $image | Select-Object -Last 3
    $env:ANDROID_AVD_HOME = Join-Path $root 'avd'
    New-Item -ItemType Directory -Force $env:ANDROID_AVD_HOME | Out-Null
    if (-not (Test-Path -LiteralPath (Join-Path $env:ANDROID_AVD_HOME 'darg.avd'))) {
        $avdmanager = Join-Path $sdkmanager.DirectoryName 'avdmanager.bat'
        'no' | & $avdmanager create avd -n darg -k $image -d pixel_7 --force
    }
}

Write-Host "Done. Build and install with: pwsh -File tools/android.ps1"
