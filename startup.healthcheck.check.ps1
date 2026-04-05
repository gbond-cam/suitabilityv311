param (
    [string]$BaseUrl = "http://127.0.0.1:7076"
)

$uri = "$($BaseUrl.TrimEnd('/'))/api/startup/healthcheck"

Write-Host "Checking startup healthcheck at $uri" -ForegroundColor Cyan

try {
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method GET
    Write-Host "HTTP $([int]$response.StatusCode)" -ForegroundColor Green
    if ($response.Content) {
        Write-Host $response.Content
    }
}
catch {
    Write-Host "Startup healthcheck check failed: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        Write-Host $reader.ReadToEnd()
    }
    exit 1
}
