# placeholder.validation

Responsible for validating unresolved placeholders in advice templates.

## Responsibilities
- Detect unresolved {{placeholders}}
- Apply severity-based policy
- Fail closed on Block severity
- Emit append-only validation lineage

## Non-Responsibilities
- No document generation
- No cryptographic signing
- No business suitability logic
