param (
    [Parameter(Mandatory)]
    [ValidateSet("dev", "test", "prod")]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$SubscriptionId
)

$rootPath = (Resolve-Path (Join-Path $PSScriptRoot "..\")).Path
$targetScript = Join-Path $rootPath "deployment.recheck.ps1"

if (-not (Test-Path $targetScript)) {
    Write-Error "deployment.recheck.ps1 not found at $targetScript"
    exit 1
}

$pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
$exe = if ($pwsh) { $pwsh.Source } else { (Get-Command powershell).Source }

$process = Start-Process `
    -FilePath $exe `
    -ArgumentList @('-ExecutionPolicy', 'Bypass', '-File', $targetScript, '-Environment', $Environment, '-SubscriptionId', $SubscriptionId) `
    -Wait `
    -NoNewWindow `
    -PassThru

exit $process.ExitCode
