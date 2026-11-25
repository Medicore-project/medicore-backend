<#
.SYNOPSIS
  SE3112 Unit Testing & Mocking — one-command demo runner for the Appointment service.

.DESCRIPTION
  Restores, builds and runs the MediCore.Appointment.UnitTests suite, collects Coverlet coverage
  and renders an HTML report with ReportGenerator.

.EXAMPLE
  ./run-tests.ps1                         # everything + coverage report
  ./run-tests.ps1 -Filter Parameterized   # only the [Theory] tests
  ./run-tests.ps1 -Filter Mocking         # only the Moq behaviour-verification tests
  ./run-tests.ps1 -Filter Booking         # one feature (Booking | Lifecycle | Waitlist | Validation | DomainRules)
  ./run-tests.ps1 -Open                   # open the coverage report in the browser afterwards
#>
param(
    [ValidateSet("All", "Parameterized", "Mocking", "Booking", "Lifecycle", "Waitlist", "Validation", "DomainRules")]
    [string]$Filter = "All",
    [switch]$Open
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$results = Join-Path $PSScriptRoot "TestResults"
if (Test-Path $results) { Remove-Item $results -Recurse -Force }

$testFilter = switch ($Filter) {
    "All"           { "Category=Unit" }
    "Parameterized" { "Technique=Parameterized" }
    "Mocking"       { "Technique=Mocking" }
    default         { "Feature=$Filter" }
}

Write-Host "`n== MediCore Appointment - Unit Testing & Mocking (filter: $testFilter) ==`n" -ForegroundColor Cyan

dotnet test UnitTests.csproj `
    --configuration Release `
    --filter $testFilter `
    --settings coverlet.runsettings `
    --collect "XPlat Code Coverage" `
    --results-directory $results `
    --logger "console;verbosity=normal" `
    --logger "trx;LogFileName=unit-tests.trx"
$testExit = $LASTEXITCODE

if (-not (Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue)) {
    Write-Host "`nNo coverage was collected (the build or test run failed) - see the errors above." -ForegroundColor Red
    exit $testExit
}

# Files extracted from a downloaded zip carry Windows' "from the internet" mark, which stops
# `dotnet tool restore` from trusting the tool manifest. Clear it for this folder (no-op otherwise).
Get-ChildItem $PSScriptRoot -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

$restoreOutput = dotnet tool restore 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nCoverage report skipped: could not restore ReportGenerator." -ForegroundColor Yellow
    $restoreOutput | Write-Host
    exit $testExit
}

dotnet tool run reportgenerator `
    "-reports:$results/**/coverage.cobertura.xml" `
    "-targetdir:$results/coverage-report" `
    "-reporttypes:Html;TextSummary;MarkdownSummaryGithub" `
    "-title:MediCore Appointment - Unit Tests" | Out-Null

$summary = Join-Path $results "coverage-report/Summary.txt"
if (-not (Test-Path $summary)) {
    Write-Host "`nCoverage report skipped: ReportGenerator produced no output." -ForegroundColor Yellow
    exit $testExit
}

Write-Host "`n== Coverage summary (Application layer) ==" -ForegroundColor Cyan
Get-Content $summary | Select-Object -First 16

Write-Host "`nHTML report: $results\coverage-report\index.html"
Write-Host "TRX results: $results\unit-tests.trx"
if ($Open) { Start-Process "$results/coverage-report/index.html" }

exit $testExit
