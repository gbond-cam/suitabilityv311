param(
    [string]$BaseUrl = "http://127.0.0.1:7071",
    [string]$CaseId = "GOLDEN-001"
)

$routeName = 'RecordLineageEvent'
$uri = "$($BaseUrl.TrimEnd('/'))/api/$routeName"

$seedRecords = @(
    [pscustomobject]@{
        'EventId' = "seed-bootstrap-001"
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
    [pscustomobject]@{
        'EventId' = "seed-evidence-001"
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
    [pscustomobject]@{
        'EventId' = "seed-approval-001"
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
    [pscustomobject]@{
        'EventId' = "seed-delivery-001"
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

foreach ($lineageRecord in $seedRecords) {
    $json = $lineageRecord | ConvertTo-Json -Depth 6 -Compress
    $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -Method POST -ContentType "application/json" -Body $json
    Write-Host "Seeded $($lineageRecord.Action): HTTP $([int]$response.StatusCode)"
}

Write-Host "Completed seeding lineage for case '$CaseId'."
