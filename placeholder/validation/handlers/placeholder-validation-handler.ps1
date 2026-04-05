param (
    [string]$DocumentText
)

$policy = Get-Content "$PSScriptRoot/../policies/placeholder-severity-policy.json" | ConvertFrom-Json
$result = & "$PSScriptRoot/../validators/validate-placeholders.ps1" `
            -DocumentText $DocumentText `
            -SeverityPolicy $policy

$hasBlockers = $result.issues | Where-Object { $_.severity -eq "Block" }

if ($hasBlockers) {
    throw "Placeholder validation failed with BLOCK severity issues"
}

return $result
