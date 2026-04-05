param (
    [string]$RootPath = (Get-Location).Path
)

$mapPath = Join-Path $RootPath "operations/glue/config/service-map.json"
if (-not (Test-Path $mapPath)) {
    throw "Service map was not found at $mapPath"
}

$map = Get-Content $mapPath -Raw | ConvertFrom-Json
$map.services | Select-Object name, baseUrl, healthPath, purpose | Format-Table -AutoSize
