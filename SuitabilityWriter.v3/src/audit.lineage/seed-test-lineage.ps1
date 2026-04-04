param(
    [string]$BaseUrl = "http://127.0.0.1:7071",
    [string]$CaseId = "GOLDEN-001"
)

$routeName = 'RecordLineage' + 'Event'
$idPropertyName = 'Ev' + 'entId'
$uri = "$($BaseUrl.TrimEnd('/'))/api/$routeName"

$seedRecords = @(
    @{
        MessageId = "seed-bootstrap-001"
        CaseId = $CaseId
        Stage = "Bootstrap"
        Action = "CaseOpened"
        ArtefactName = "ClientProfile"
        ArtefactVersion = "v1"
        ArtefactHash = "HASH-CLIENT-PROFILE"
        PerformedBy = "system"
        TimestampUtc = "2026-04-03T10:00:00Z"
        Metadata = @{ channel = "local-test"; note = "seed data" }
    },
    @{
        MessageId = "seed-evidence-001"
        CaseId = $CaseId
        Stage = "EvidenceIntake"
        Action = "EvidenceReceived"
        ArtefactName = "ClientEvidence"
        ArtefactVersion = "v3.1"
        ArtefactHash = "HASH-CLIENT-EVIDENCE"
        PerformedBy = "case.worker"
        TimestampUtc = "2026-04-03T10:05:00Z"
        Metadata = @{ channel = "local-test"; document = "fact-find" }
    },
    @{
        MessageId = "seed-approval-001"
        CaseId = $CaseId
        Stage = "AdviceReview"
        Action = "AdviserApproved"
        ArtefactName = "SuitabilityReport"
        ArtefactVersion = "v3.11"
        ArtefactHash = "HASH-SUITABILITY-REPORT"
        PerformedBy = "adviser.uk"
        TimestampUtc = "2026-04-03T10:15:00Z"
        Metadata = @{ decision = "approved" }
    },
    @{
        MessageId = "seed-delivery-001"
        CaseId = $CaseId
        Stage = "Delivery"
        Action = "Delivered"
        ArtefactName = "SuitabilityReport"
        ArtefactVersion = "v3.11"
        ArtefactHash = "HASH-SUITABILITY-REPORT"
        PerformedBy = "delivery.bot"
        TimestampUtc = "2026-04-03T10:20:00Z"
        Metadata = @{ channel = "secure-portal" }
    }
)

$seedRecords | ForEach-Object {
    $json = $_ | ConvertTo-Json -Depth 6 -Compress
    $json = $json.Replace('"MessageId"', ('"' + $idPropertyName + '"'))
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $json
    Write-Host "Seeded $($_.Action): HTTP $([int]$response.StatusCode)"
}

Write-Host "Completed seeding lineage for case '$CaseId'."
