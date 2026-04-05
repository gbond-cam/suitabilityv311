# startup.healthcheck

Startup invariant validation for Suitability Writer services.

## Responsibilities
- Verify delivery.gate passed
- Verify required environment variables
- Verify required directories
- Fail fast on startup

## Non-Responsibilities
- No runtime health probes
- No liveness checks
- No business logic
