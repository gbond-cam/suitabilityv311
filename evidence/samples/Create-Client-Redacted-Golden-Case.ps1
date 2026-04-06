param (
    [Parameter(Mandatory)]
    [string]$SourceCasePath,

    [string]$OutputPath = "golden-case-redacted/GOLDEN-CASE-REDACTED"
)

Write-Host "🔒 Creating client-redacted Golden Case" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# Copy system evidence verbatim
Copy-Item "$SourceCasePath/01_SYSTEM_EVIDENCE" "$OutputPath" -Recurse

# Copy approved artefacts verbatim (already non-PII)
Copy-Item "$SourceCasePath/03_APPROVED_ARTEFACTS" "$OutputPath" -Recurse

# Copy MI verbatim
Copy-Item "$SourceCasePath/06_MI" "$OutputPath" -Recurse

# Input evidence – manually redacted PDFs
Copy-Item "$SourceCasePath/02_INPUT_EVIDENCE/*_REDACTED.pdf" `
  "$OutputPath/02_INPUT_EVIDENCE" -Force

# Output report – redacted version only
Copy-Item "$SourceCasePath/04_ENGINE_OUTPUT/*REDACTED*" `
  "$OutputPath/04_ENGINE_OUTPUT" -Force

# Lineage – redacted JSON
Copy-Item "$SourceCasePath/05_LINEAGE/*REDACTED*" `
  "$OutputPath/05_LINEAGE" -Force

Copy-Item "$SourceCasePath/00_README.md" "$OutputPath"

Write-Host "✅ Client-redacted Golden Case created" -ForegroundColor Green
