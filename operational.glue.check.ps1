param (
    [string]$RootPath = (Get-Location).Path
)

& (Join-Path $RootPath "operations/glue/scripts/check-local-dependencies.ps1") -RootPath $RootPath
