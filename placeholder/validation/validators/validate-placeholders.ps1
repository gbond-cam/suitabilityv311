param (
    [string]$DocumentText,
    [object]$SeverityPolicy
)

$regex = "\{\{\s*[^}]+\s*\}\}"
$matches = [regex]::Matches($DocumentText, $regex)

$issues = @()

foreach ($m in $matches) {
    $severity = "Info"

    foreach ($rule in $SeverityPolicy.rules) {
        if ($m.Value -like "*$($rule.pattern)*") {
            $severity = $rule.severity
        }
    }

    $issues += @{
        placeholder = $m.Value
        severity    = $severity
        message     = "Unresolved placeholder detected"
    }
}

return @{
    issues = $issues
}
