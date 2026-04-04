# Evidence Bundle Acceptance Criteria

_Authoritative acceptance criteria for regulator-, auditor-, and governance-ready evidence bundles._

## Purpose

This document defines the minimum conditions that an exported evidence bundle must satisfy before it can be treated as **fit for review, disclosure, audit, or regulatory response**.

## Acceptance Criteria

### 1. Case integrity
- The bundle **must** reference a unique case identifier.
- The bundle **must** include the relevant decision, recommendation, or review context.
- The bundle **must** be reconstructable to a clear chronology of material events.

### 2. Evidence completeness
- The bundle **must** contain all required artefacts for the case scope.
- Mandatory artefacts **must not** be silently omitted.
- Missing artefacts **must** be explicitly flagged with reason codes.

### 3. Lineage and traceability
- Every artefact **must** be linked to immutable lineage records.
- Hashes, timestamps, and producing service identifiers **must** be present where applicable.
- The bundle **must** support independent reconstruction of the evidence chain.

### 4. Cryptographic assurance
- Integrity hashes **must** validate successfully.
- Required signatures **must** verify successfully against the approved key material.
- If encryption is applied, the envelope **must** conform to the approved schema and decrypt for an authorized recipient.

### 5. Schema and format compliance
- The bundle payload **must** validate against the approved JSON schema version.
- The schema version **must** be declared and traceable in the schema catalog.
- Exported content **must** be machine-readable and reproducible.

### 6. Governance controls
- The bundle **must** reflect the current approved templates and governed artefacts.
- Any override, exception, or emergency path **must** be recorded and attributable.
- The export event **must** be auditable.

### 7. Review readiness
- An auditor or regulator **must** be able to determine:
  - what happened,
  - when it happened,
  - what evidence supports the outcome,
  - and whether integrity controls held throughout.

## Minimum Release Decision

An evidence bundle is accepted only when it is:
- **Complete** for the defined scope,
- **Traceable** to immutable lineage,
- **Cryptographically verifiable**, and
- **Governance-compliant** with the current approved schema and control set.

## Owner

- Evidence System Governance
- Compliance / Risk Oversight
- Internal Audit for independent review
