# operational.glue

Operational glue for Suitability Writer local development and service coordination.

## Responsibilities
- Record the local service endpoint map
- Document the end-to-end service handoff order
- Provide lightweight dependency visibility for local runs

## Local service order
1. startup.healthcheck
2. evidence.intake
3. template.routing
4. placeholder.validation
5. suitability.engine
6. report.generator
7. delivery.gate
8. audit.lineage

## Quick commands
```powershell
powershell -ExecutionPolicy Bypass -File .\operational.glue.check.ps1
powershell -ExecutionPolicy Bypass -File .\operations\glue\scripts\show-service-map.ps1
```
