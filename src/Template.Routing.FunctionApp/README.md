# template.routing

Routes a case evidence payload to the correct approved suitability template.

## Responsibilities
- Apply auditable routing rules (policy-driven)
- Produce deterministic output: templateFileName, templateVersion, reason
- Fail closed only if required inputs are missing (otherwise safe default route)

## Non-responsibilities
- No template retrieval (that is approved artefact catalog / SharePoint)
- No placeholder validation (placeholder.validation)
- No report generation (report.generator)
- No cryptographic signing (audit.lineage / evidence bundle export)
