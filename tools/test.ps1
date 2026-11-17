# Darg's whole test suite (WOR-0127): builds everything (so a XAML or app compile error fails too), then runs
#   unit tests         tests/Darg.Core.Tests      Core, no UI
#   integration tests  tests/Darg.Desktop.Tests   the real app headless on a throwaway instance, fake claude
# The integration tests leave a folder per test in .shots/integration/<test>: its instance, board files,
# fake-claude.jsonl (what each agent was started with and sent) and PNGs of what the window showed.
# Exits non-zero if anything fails. Needs python on PATH (DARG_TEST_PYTHON to name another) for the fake claude.
#   pwsh -File tools/test.ps1                     everything
#   pwsh -File tools/test.ps1 -Unit               Core only (seconds)
#   pwsh -File tools/test.ps1 -Integration -Filter SendBack
param([switch]$Unit, [switch]$Integration, [string]$Filter)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/..").Path
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'D:/nuget/packages' }
$both = -not $Unit -and -not $Integration

dotnet build "$repo/Darg.sln" -v quiet -nologo
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

$failed = @()
function Invoke-Suite([string]$name, [string]$project) {
    Write-Host "`n== $name ==" -ForegroundColor Cyan
    $a = @('test', "$repo/tests/$project", '--no-build', '-nologo')
    if ($Filter) { $a += @('--filter', "FullyQualifiedName~$Filter") }
    & dotnet @a
    if ($LASTEXITCODE -ne 0) { $script:failed += $name }
}
if ($both -or $Unit) { Invoke-Suite 'unit' 'Darg.Core.Tests' }
if ($both -or $Integration) {
    Invoke-Suite 'integration' 'Darg.Desktop.Tests'
    Write-Host "Integration folders and pictures: $repo/.shots/integration"
}

if ($failed.Count) { Write-Host "`nFAILED: $($failed -join ', ')" -ForegroundColor Red; exit 1 }
Write-Host "`nAll passed." -ForegroundColor Green
