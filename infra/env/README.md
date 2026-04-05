# Key Vault Access – Managed Identity

This folder defines how application secrets are accessed securely.

## Design principles
- No secrets in source control
- No secrets in pipelines
- Managed Identity only
- Least privilege (`Key Vault Secrets User`)

## Flow
1. `keyvault.mapping.json` declares which app needs which secrets
2. Managed Identity is enabled on the Function App
3. `apply-keyvault-access.ps1` grants access via RBAC
4. App reads secrets at runtime via environment variables

## Enforcement
- `check-env-contracts.ps1` → ensures env vars exist
- `check-keyvault-mapping.ps1` → ensures secrets are mapped
- `startup.healthcheck` → fails closed if injection is missing

## Local verification
```powershell
powershell -ExecutionPolicy Bypass -File .\infra\env\check-env-contracts.ps1
powershell -ExecutionPolicy Bypass -File .\infra\env\check-keyvault-mapping.ps1
```

## Azure application
```powershell
powershell -ExecutionPolicy Bypass -File .\infra\env\apply-keyvault-access.ps1 -SubscriptionId "<sub-id>" -ResourceGroup "<rg-name>"
```

## Secret bootstrapping
Secrets are bootstrapped using `..\keyvault\bootstrap-keyvault-secrets.ps1`.

See `infra/keyvault/README.md` for the operational rules and guardrails.
