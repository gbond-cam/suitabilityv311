function Validate-Intake {
    param ($Request)

    # Fail-closed validation
    if (-not $Request.caseId) { throw "Missing caseId" }
    if (-not $Request.evidenceType) { throw "Missing evidenceType" }
    if (-not $Request.payload) { throw "Missing payload" }

    return $true
}
