param (
    [string]$BaseUrl = "http://127.0.0.1:7074",
    [string]$CaseId = "GOLDEN-001",
    [string]$EvidenceType = "fact-find",
    [string]$Source = "secure-portal"
)

$uri = "$($BaseUrl.TrimEnd('/'))/api/evidence/intake"
$payload = @{
    CaseId = $CaseId
    EvidenceType = $EvidenceType
    Source = $Source
} | ConvertTo-Json -Depth 4 -Compress

Write-Host "Checking evidence intake at $uri" -ForegroundColor Cyan
Write-Host "Payload: $payload" -ForegroundColor DarkGray

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $payload
    Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    if ($response.Content) {
        Write-Host $response.Content
    }
}
catch {
    Write-Host "Evidence intake check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
