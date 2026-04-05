param ($Request)

. "$PSScriptRoot/../validation/validate-intake.ps1"
. "$PSScriptRoot/../normalisation/normalise-payload.ps1"

Validate-Intake $Request | Out-Null
$normalised = Normalise-Payload $Request

$intakeId = [guid]::NewGuid().ToString()

# Emit lineage event (append-only)
$lineageEvent = @{
  eventType    = "EVIDENCE_INTAKE"
  caseId       = $normalised.caseId
  intakeId     = $intakeId
  evidenceType = $normalised.evidenceType
  timestamp    = (Get-Date -Format o)
}

return @{
  caseId       = $normalised.caseId
  intakeId     = $intakeId
  intakeStatus = "accepted"
}
