param (
    [string]$BaseUrl = "http://127.0.0.1:7072",
    [string]$TemplateId = "SuitabilityTemplate-v3.11",
    [string]$CaseId = "GOLDEN-001"
)

$uri = "$($BaseUrl.TrimEnd('/'))/api/placeholders/validate"
$payload = @{
    TemplateId = $TemplateId
    CaseId = $CaseId
    Artefact = @{
        Name = "SuitabilityReport"
        Version = "v3.11"
        Hash = "HASH-SUITABILITY-REPORT"
    }
} | ConvertTo-Json -Depth 5 -Compress

Write-Host "Checking placeholder validation at $uri" -ForegroundColor Cyan
Write-Host "Payload: $payload" -ForegroundColor DarkGray

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $payload
    Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    if ($response.Content) {
        Write-Host $response.Content
    }
}
catch {
    Write-Host "Placeholder validation check failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
