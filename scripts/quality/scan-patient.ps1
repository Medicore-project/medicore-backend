<#
.SYNOPSIS
  Runs the Patient service code-quality scan: build with analyzers, tests with
  coverage, SonarQube analysis, coverage report and duplication report.

.DESCRIPTION
  Output goes to .quality/ (gitignored):
    .quality/coverage-report/index.html   coverage report (ReportGenerator)
    .quality/jscpd/html/index.html        duplication report (jscpd)
    .quality/build.log                    build output with analyzer warnings
  The SonarQube dashboard is at <SonarUrl>/dashboard?id=<ProjectKey>.

  Start SonarQube first:
    docker compose -f infra/sonarqube/docker-compose.yml up -d

.EXAMPLE
  ./scripts/quality/scan-patient.ps1 -Token squ_xxx
.EXAMPLE
  ./scripts/quality/scan-patient.ps1 -SkipSonar      # coverage + duplication only
#>
param(
    [string]$Token = $env:SONAR_TOKEN,
    [string]$SonarUrl = "http://localhost:9000",
    [string]$ProjectKey = "medicore-patient",
    [switch]$SkipSonar
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $root
$out = Join-Path $root ".quality"
$service = "src/services/patient"
$testProject = "$service/MediCore.Patient.Tests/Tests.csproj"

function Install-ToolIfMissing([string]$command, [string]$package) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        Write-Host "Installing $package..." -ForegroundColor Cyan
        dotnet tool install --global $package | Out-Null
    }
}

if (-not $SkipSonar -and -not $Token) {
    throw "Pass -Token or set SONAR_TOKEN (SonarQube > My Account > Security), or use -SkipSonar."
}

Install-ToolIfMissing "reportgenerator" "dotnet-reportgenerator-globaltool"
if (-not $SkipSonar) { Install-ToolIfMissing "dotnet-sonarscanner" "dotnet-sonarscanner" }

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Path $out | Out-Null

# --- 1. SonarQube begin ---------------------------------------------------------
if (-not $SkipSonar) {
    Write-Host "`n[1/5] SonarQube begin" -ForegroundColor Cyan
    dotnet sonarscanner begin `
        /k:"$ProjectKey" `
        /n:"MediCore Patient Service" `
        /d:sonar.host.url="$SonarUrl" `
        /d:sonar.login="$Token" `
        /d:sonar.scanner.scanAll=false `
        /d:sonar.cs.opencover.reportsPaths=".quality/coverage/**/coverage.opencover.xml" `
        /d:sonar.cs.vstest.reportsPaths=".quality/coverage/**/*.trx" `
        /d:sonar.exclusions="**/bin/**,**/obj/**,**/Migrations/**,src/shared/**" `
        /d:sonar.coverage.exclusions="**/Program.cs,**/*.Infrastructure/**,**/*.Tests/**" `
        /d:sonar.cpd.exclusions="**/Migrations/**,**/*.Tests/**"
    if ($LASTEXITCODE -ne 0) { throw "sonarscanner begin failed" }
}

# --- 2. Build with analyzers (the test project builds Api, Application, Infrastructure)
Write-Host "`n[2/5] Build (Release, analyzers on)" -ForegroundColor Cyan
dotnet build $testProject -c Release -nologo | Tee-Object -FilePath "$out/build.log"
if ($LASTEXITCODE -ne 0) { throw "Build failed - see .quality/build.log" }

# --- 3. Tests with coverage ---------------------------------------------------------
Write-Host "`n[3/5] Tests with coverage" -ForegroundColor Cyan
dotnet test $testProject -c Release --no-build -nologo `
    --collect:"XPlat Code Coverage" `
    --results-directory "$out/coverage" `
    --logger "trx"
$testExit = $LASTEXITCODE

# --- 4. SonarQube end ----------------------------------------------------------------
if (-not $SkipSonar) {
    Write-Host "`n[4/5] SonarQube end (uploading analysis)" -ForegroundColor Cyan
    dotnet sonarscanner end /d:sonar.login="$Token"
    if ($LASTEXITCODE -ne 0) { throw "sonarscanner end failed" }
}

# --- 5. Coverage + duplication reports --------------------------------------------------
Write-Host "`n[5/5] Coverage and duplication reports" -ForegroundColor Cyan
reportgenerator `
    -reports:"$out/coverage/**/coverage.cobertura.xml" `
    -targetdir:"$out/coverage-report" `
    -reporttypes:"Html;TextSummary" | Out-Null
npx --yes jscpd@4.0.5 --config .jscpd.json --silent $service
$dupExit = $LASTEXITCODE

# --- Summary ----------------------------------------------------------------------------
$warnings = Select-String -Path "$out/build.log" -Pattern "services.patient.*warning (CA|IDE)\d+" -AllMatches |
    ForEach-Object { $_.Line -replace '\s*\[.*$', '' } | Sort-Object -Unique
Write-Host "`n========== Patient service code quality summary ==========" -ForegroundColor Green
Get-Content "$out/coverage-report/Summary.txt" |
    Select-String "Line coverage|Branch coverage|Method coverage" | ForEach-Object { "  $($_.Line.Trim())" }
Write-Host "  Analyzer warnings (unique): $($warnings.Count)"
$warnings | ForEach-Object { if ($_ -match 'warning ((CA|IDE)\d+)') { $Matches[1] } } |
    Group-Object | Sort-Object Count -Descending | ForEach-Object { "    $($_.Name): $($_.Count)" }
Write-Host "  Coverage report:    $out\coverage-report\index.html"
Write-Host "  Duplication report: $out\jscpd\html\index.html"
if (-not $SkipSonar) { Write-Host "  SonarQube:          $SonarUrl/dashboard?id=$ProjectKey" }
if ($testExit -ne 0) { Write-Host "  Tests: FAILED (see output above)" -ForegroundColor Red }
if ($dupExit -ne 0) { Write-Host "  Duplication: above the .jscpd.json threshold" -ForegroundColor Red }
exit ([int]($testExit -ne 0 -or $dupExit -ne 0))
