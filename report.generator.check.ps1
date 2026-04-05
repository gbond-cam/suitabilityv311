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
