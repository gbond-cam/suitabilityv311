# Secret Bootstrapping

Secrets are bootstrapped using `bootstrap-keyvault-secrets.ps1`.

## What this does
- Creates missing secrets only
- Uses placeholder values
- Tags secrets for traceability
- Never prints or reads secret values

## What this does NOT do
- Does not rotate secrets
- Does not inject real values
- Does not bypass approval workflows

## Operational rule
Real secret values **MUST** be populated:
- via secure runbook, or
- via privileged ops pipeline,
- **NOT** via application CI.

## Break-glass secret override
This procedure exists for **emergency service restoration only**.

### Preconditions
- Production incident declared
- Incident ID assigned
- Senior approval obtained (e.g. SMF / Director)
- Time-boxed expiry defined

### What happens
- A Key Vault secret is temporarily overridden
- Secret is tagged with incident, approver, timestamp, expiry
- All access is logged by Azure Key Vault

### Controls
- Only secrets declared in `keyvault-mapping.json` may be overridden
- Expiry is mandatory
- Managed Identity access remains unchanged
- `startup.healthcheck` continues to enforce presence

### Post-incident
- Root cause analysis completed
- Secret restored via standard process
- Break-glass tags reviewed
- Evidence retained for audit

### Misuse
Any use outside an active incident is a control breach.
