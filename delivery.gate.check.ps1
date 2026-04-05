param (
    [string]$BaseUrl = "http://127.0.0.1:7073",
    [string]$CaseId = "GOLDEN-001",
    [string]$Stage = "Delivery",
    [string]$DecisionBy = "adviser.uk"
)

$uri = "$($BaseUrl.TrimEnd('/'))/api/delivery/gate"
$payload = @{
    CaseId = $CaseId
    Stage = $Stage
    DecisionBy = $DecisionBy
} | ConvertTo-Json -Depth 4 -Compress

Write-Host "Checking delivery gate at $uri" -ForegroundColor Cyan
Write-Host "Payload: $payload" -ForegroundColor DarkGray

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $payload
    Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    if ($response.Content) {
        Write-Host $response.Content
    }
}
catch {
    Write-Host "Delivery gate check failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        Write-Host $reader.ReadToEnd()
    }
    exit 1
}
