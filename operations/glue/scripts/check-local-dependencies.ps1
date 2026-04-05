param (
    [string]$RootPath = (Get-Location).Path
)

$mapPath = Join-Path $RootPath "operations/glue/config/service-map.json"
if (-not (Test-Path $mapPath)) {
    throw "Service map was not found at $mapPath"
}

$map = Get-Content $mapPath -Raw | ConvertFrom-Json

foreach ($service in $map.services) {
    $uri = "$($service.baseUrl.TrimEnd('/'))$($service.healthPath)"
    Write-Host "Checking $($service.name) at $uri" -ForegroundColor Cyan

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method GET -TimeoutSec 5
        Write-Host "  UP   HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    }
    catch {
        Write-Host "  DOWN $($_.Exception.Message)" -ForegroundColor Yellow
    }
}
