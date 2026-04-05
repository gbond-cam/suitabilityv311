function Normalise-Payload {
    param ($Request)

    # Canonical normalisation layer
    $normalised = @{
      caseId       = $Request.caseId.Trim()
      evidenceType = $Request.evidenceType
      submittedAt  = (Get-Date $Request.submittedAt -Format o)
      payload      = $Request.payload
    }

    return $normalised
}
