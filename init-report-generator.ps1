param (
    [string]$RootPath = (Get-Location).Path,
    [int]$Port = 7075
)

Write-Host "Initializing report.generator in $RootPath" -ForegroundColor Cyan

$projectPath = Join-Path $RootPath "SuitabilityWriter.v3\src\report.generator"
if (-not (Test-Path $projectPath)) {
    throw "report.generator project was not found at $projectPath"
}

$localSettingsPath = Join-Path $projectPath "local.settings.json"
$localSettingsJson = @'
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
'@
Set-Content -Path $localSettingsPath -Value $localSettingsJson -Encoding utf8
Write-Host "Updated: SuitabilityWriter.v3/src/report.generator/local.settings.json" -ForegroundColor Green

$checkScriptPath = Join-Path $RootPath "report.generator.check.ps1"
$checkScript = @'
param (
    [string]$BaseUrl = "http://127.0.0.1:7075",
    [string]$CaseId = "GOLDEN-001",
    [string]$TemplateId = "SuitabilityTemplate-v3.11",
    [string]$RequestedBy = "adviser.uk"
)

$uri = "$($BaseUrl.TrimEnd('/'))/api/reports/generate"
$payload = @{
    CaseId = $CaseId
    TemplateId = $TemplateId
    RequestedBy = $RequestedBy
} | ConvertTo-Json -Depth 4 -Compress

Write-Host "Checking report generator at $uri" -ForegroundColor Cyan
Write-Host "Payload: $payload" -ForegroundColor DarkGray

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $payload
    Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    if ($response.Content) {
        Write-Host $response.Content
    }
}
catch {
    Write-Host "Report generator check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
'@
Set-Content -Path $checkScriptPath -Value $checkScript -Encoding utf8
Write-Host "Updated: report.generator.check.ps1" -ForegroundColor Green

Push-Location $projectPath
dotnet build .\report.generator.csproj -v minimal
$buildExit = $LASTEXITCODE
Pop-Location

if ($buildExit -ne 0) {
    throw "report.generator build failed with exit code $buildExit"
}

Write-Host "[OK] report.generator initialized successfully on port $Port." -ForegroundColor Cyan
