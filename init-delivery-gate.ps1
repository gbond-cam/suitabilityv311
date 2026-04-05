param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing delivery.gate structure in $RootPath" -ForegroundColor Cyan

function New-FileIfMissing {
    param (
        [string]$Path,
        [string]$Content
    )

    $fullPath = Join-Path $RootPath $Path
    $dir = Split-Path $fullPath -Parent

    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if (-not (Test-Path $fullPath)) {
        $Content | Out-File -FilePath $fullPath -Encoding utf8
        Write-Host "Created: $Path" -ForegroundColor Green
    }
    else {
        Write-Host "Exists : $Path" -ForegroundColor Yellow
    }
}

# ------------------------------------------------------------------
# GOVERNANCE
# ------------------------------------------------------------------

$governanceFiles = @{
    "docs/governance/Evidence-System-Overview.md" = "# Evidence System Overview`n`nTODO: Describe purpose, scope, and regulatory intent."
    "docs/governance/Evidence-Controls-Summary.md" = "# Evidence Controls Summary`n`nTODO: High-level control summary for FCA review."
    "docs/governance/Schema-Governance.md" = "# Schema Governance`n`nTODO: Versioning, immutability, approval rules."
    "docs/governance/Cryptographic-Controls.md" = "# Cryptographic Controls`n`nTODO: Hashing, signing, timestamping, encryption."
    "docs/governance/Key-Management-and-Revocation.md" = "# Key Management and Revocation`n`nTODO: Key lifecycle, revocation, rotation."
    "docs/governance/Evidence-Bundle-Acceptance-Criteria.md" = "# Evidence Bundle Acceptance Criteria`n`nTODO: MUST / FAIL rules."
    "docs/governance/Quarterly-Evidence-System-MI-Template.md" = "# Quarterly Evidence System MI`n`nTODO: Populate per quarter."
    "docs/governance/Regulator-QA-Pack.md" = "# Regulator Q&A Pack`n`nTODO: FCA anticipated questions and answers."
    "docs/governance/FCA-One-Page-Briefing.md" = "# FCA One-Page Briefing`n`nTODO: Supervisor pre-read."
}

# ------------------------------------------------------------------
# ARCHITECTURE
# ------------------------------------------------------------------

$architectureFiles = @{
    "docs/architecture/Evidence-Lifecycle.md" = "# Evidence Lifecycle`n`nTODO: Intake -> validation -> bundle -> verification."
    "docs/architecture/Threat-Model.md" = "# Threat Model`n`nTODO: Threats, mitigations, residual risk."
    "docs/architecture/Evidence-Flow-Diagram.svg" = "<!-- SVG placeholder -->"
    "docs/architecture/Evidence-Flow-Diagram-FCA.png" = ""
    "docs/architecture/Evidence-Flow-Diagram-Portrait.svg" = "<!-- SVG placeholder -->"
    "docs/architecture/Evidence-Flow-Diagram-Portrait-FCA.png" = ""
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait.svg" = "<!-- SVG placeholder -->"
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait-FCA.png" = ""
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait-SYSC.svg" = "<!-- SVG placeholder -->"
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait-SYSC-FCA.png" = ""
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait-SYSC-CD.svg" = "<!-- SVG placeholder -->"
    "docs/architecture/Evidence-Flow-Diagram-Board-Portrait-SYSC-CD-FCA.png" = ""
}

# ------------------------------------------------------------------
# VERIFICATION
# ------------------------------------------------------------------

$verificationFiles = @{
    "docs/verification/VERIFY.md" = "# VERIFY`n`nTODO: Offline verification steps."
    "docs/verification/Auditor-Decryption-Guide.md" = "# Auditor Decryption Guide`n`nTODO: How to decrypt and verify bundles."
}

# ------------------------------------------------------------------
# SYSTEM & CONTROLS
# ------------------------------------------------------------------

$systemControlFiles = @{
    "governance/system-controls/System-and-Control-Plan.docx" = ""
    "governance/system-controls/Board-Minutes-or-Resolution.pdf" = ""
}

# ------------------------------------------------------------------
# OPERATIONAL EVIDENCE
# ------------------------------------------------------------------

$evidenceFiles = @{
    "evidence/samples/Case-0001-Evidence-Bundle.zip" = ""
    "evidence/samples/Case-0001-Reconstruction.json" = "{ }"
    "evidence/samples/Case-0001-Verification-Output.txt" = "TODO: Verification results"
    "evidence/samples/Case-0001-Acceptance-Checklist.md" = "# Acceptance Checklist`n`nTODO: Sign-off against criteria."
}

# ------------------------------------------------------------------
# MANAGEMENT INFORMATION
# ------------------------------------------------------------------

$miFiles = @{
    "mi/quarterly/Evidence-System-MI-Q1.md" = "# Evidence System MI - Q1`n`nTODO: Populate using approved template."
}

# ------------------------------------------------------------------
# EXECUTION
# ------------------------------------------------------------------

$allFiles = @{}
$allFiles += $governanceFiles
$allFiles += $architectureFiles
$allFiles += $verificationFiles
$allFiles += $systemControlFiles
$allFiles += $evidenceFiles
$allFiles += $miFiles

foreach ($file in $allFiles.GetEnumerator()) {
    New-FileIfMissing -Path $file.Key -Content $file.Value
}

Write-Host "`n[OK] delivery.gate structure initialized successfully." -ForegroundColor Cyan
