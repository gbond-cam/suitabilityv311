# Suitability.Engine.FunctionApp

Orchestrates the advice case processing pipeline:
- pulls evidence
- validates input
- routes template
- enforces placeholder validation
- generates report
- emits audit lineage

This app is orchestration only: business logic lives in services, and evidential integrity lives in audit.lineage.
