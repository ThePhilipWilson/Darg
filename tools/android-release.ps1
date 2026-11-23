<#
.SYNOPSIS
  Builds a signed release APK (or AAB) of the Android app, to copy to a phone and install without adb (AND-0009).

.DESCRIPTION
  Signs with a keystore kept OUTSIDE the repo, in <DARG_ANDROID_ROOT>\keys (default D:\Android\keys): the first run
  makes darg-release.keystore there with a random password in darg-release.pass. Back that folder up: an update can
  only be installed over an earlier copy if both were signed with the same key, and a lost key means uninstalling the
  app (and re-pairing) to move to a new one. Never commit either file.

  Writes D:\Android\release\darg-<version>.apk (or .aab with -Aab), checks its signature with apksigner and prints
  where it is. To install: copy the APK to the phone (USB, cloud drive, a message to yourself), open it, and allow
  "install unknown apps" for the app that opened it when Android asks. Then pair it: on the PC, Settings' Phone part
  shows a QR; scan it from the app's Connect screen (or with the camera) (docs/REMOTE.md).

  Needs tools/android-setup.ps1 run once. -Version sets ApplicationDisplayVersion; the version code is the build
  time in minutes since 2026-01-01, so every build can update the one before it.

.EXAMPLE
  pwsh -File tools/android-release.ps1
  pwsh -File tools/android-release.ps1 -Aab -Version 0.2
#>
param(
    [string]$Version = '0.1',
    [switch]$Aab
)
$ErrorActionPreference = 'Stop'

$root = if ($env:DARG_ANDROID_ROOT) { $env:DARG_ANDROID_ROOT } else { 'D:\Android' }
$dotnet = Join-Path $root 'dotnet\dotnet.exe'
$sdk = Join-Path $root 'sdk'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\Darg.Android\Darg.Android.csproj'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "No Android toolchain at ${root}: run pwsh -File tools/android-setup.ps1 first." }
$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:\nuget\packages' }
$env:ANDROID_HOME = $sdk

# -- the signing key, made once and kept out of the repo
$keys = Join-Path $root 'keys'
$store = Join-Path $keys 'darg-release.keystore'
$passFile = Join-Path $keys 'darg-release.pass'
$alias = 'darg'
if (-not (Test-Path -LiteralPath $store)) {
    $keytool = (Get-Command keytool -ErrorAction Stop).Source
    New-Item -ItemType Directory -Force $keys | Out-Null
    $pass = -join ((48..57) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    Set-Content -LiteralPath $passFile -Value $pass -NoNewline
    & $keytool -genkeypair -v -keystore $store -alias $alias -keyalg RSA -keysize 4096 -validity 10000 `
        -storepass $pass -keypass $pass -dname 'CN=Darg, O=ThePhilipWilson' 2>&1 | Out-Null
    if ($LASTEXITCODE) { Remove-Item -LiteralPath $store, $passFile -ErrorAction SilentlyContinue; throw 'keytool failed' }
    Write-Host "== made the signing key in $keys : back that folder up, and never commit it"
}
$env:DARG_ANDROID_KEYSTORE = $store
$env:DARG_ANDROID_KEYSTORE_ALIAS = $alias
$env:DARG_ANDROID_KEYSTORE_PASS = (Get-Content -LiteralPath $passFile -Raw).Trim()

# -- build
$format = if ($Aab) { 'aab' } else { 'apk' }
$code = [int]((Get-Date) - [datetime]'2026-01-01').TotalMinutes
Write-Host "== building Release $format $Version (code $code)"
& $dotnet publish $project -f net10.0-android -c Release "-p:AndroidSdkDirectory=$sdk" "-p:AndroidPackageFormat=$format" `
    "-p:ApplicationDisplayVersion=$Version" "-p:ApplicationVersion=$code" -v:minimal -nologo
if ($LASTEXITCODE) { throw "build failed ($LASTEXITCODE)" }

$built = Get-ChildItem (Join-Path $repo 'src\Darg.Android\bin\Release\net10.0-android') -Recurse -Filter "uk.co.philipwilson.darg*-Signed.$format" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $built) { throw "No signed $format came out of the build." }
$outDir = Join-Path $root 'release'
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir "darg-$Version.$format"
Copy-Item -LiteralPath $built.FullName -Destination $out -Force

# -- prove it's signed with our key, not the debug key
if (-not $Aab) {
    $apksigner = Join-Path (Get-ChildItem (Join-Path $sdk 'build-tools') | Sort-Object Name -Descending | Select-Object -First 1).FullName 'apksigner.bat'
    $cert = & $apksigner verify --print-certs -v $out 2>&1
    if ($LASTEXITCODE) { throw "apksigner says the APK isn't validly signed:`n$cert" }
    if (($cert -join "`n") -match 'Android Debug') { throw 'The APK was signed with the debug key, not the release keystore.' }
    Write-Host ($cert | Select-String 'Signer #1 certificate (DN|SHA-256)' | ForEach-Object { $_.Line })
}
Write-Host "Done: $out ($([math]::Round((Get-Item -LiteralPath $out).Length / 1MB, 1)) MB)"
