# suitability writer v3.11

Local workspace for the `SuitabilityWriter.v3` solution, operational validation scripts, and evidence/governance artefacts.

## Run

Use the existing VS Code task or run:

```powershell
python main.py
```

## Security and secret access

Application secrets are handled through **Managed Identity + Azure Key Vault RBAC**.

- Mapping: `infra/env/keyvault.mapping.json`
- Env contract check: `infra/env/check-env-contracts.ps1`
- Key Vault mapping check: `infra/env/check-keyvault-mapping.ps1`
- RBAC apply script: `infra/env/apply-keyvault-access.ps1`
- Folder guide: `infra/env/README.md`

## Verification

```powershell
powershell -ExecutionPolicy Bypass -File .\infra\env\check-env-contracts.ps1
powershell -ExecutionPolicy Bypass -File .\infra\env\check-keyvault-mapping.ps1
```

## Files

- `main.py` - starter entry point
- `SuitabilityWriter.v3/` - .NET solution and Function Apps
- `infra/env/` - environment contracts, Key Vault mapping, and enforcement scripts
- `.github/copilot-instructions.md` - workspace setup checklist and notes