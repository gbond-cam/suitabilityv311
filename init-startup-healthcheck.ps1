param (
    [string]$RootPath = (Get-Location).Path
)

Write-Host "Initializing startup.healthcheck structure in $RootPath" -ForegroundColor Cyan

function New-FileIfMissing {
    param (
        [string]$Path,
        [string]$Content
    )

    $fullPath = Join-Path $RootPath $Path
    $dir = Split-Path $fullPath -Parent

    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    if (-not (Test-Path $fullPath)) {
        $Content | Out-File -FilePath $fullPath -Encoding utf8
        Write-Host "Created: $Path" -ForegroundColor Green
    }
    else {
        Write-Host "Exists : $Path" -ForegroundColor Yellow
    }
}


# ------------------------------------------------------------------
# STARTUP.HEALTHCHECK STRUCTURE
# ------------------------------------------------------------------

$healthcheckFiles = @{
# --------------------------------------------------
# CONFIG
# --------------------------------------------------
"startup/healthcheck/config/healthcheck-settings.json" = @'
{
  "requireDeliveryGate": true,
  "requiredAppSettings": [
    "FUNCTIONS_WORKER_RUNTIME",
    "AzureWebJobsStorage",
    "APP_VERSION"
  ],
  "requiredDirectories": [
    "docs/governance",
    "evidence/samples",
    "mi/quarterly"
  ]
}
'@

# --------------------------------------------------
# CORE HEALTHCHECK
# --------------------------------------------------
"startup/healthcheck/core/StartupHealthCheck.cs" = @'
using System;
using System.IO;

namespace Startup.HealthCheck;

public sealed class StartupHealthCheck
{
    private readonly HealthCheckSettings _settings;
    private readonly string _rootPath;

    public StartupHealthCheck(HealthCheckSettings settings, string? rootPath = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Environment.CurrentDirectory
            : rootPath;
    }

    public void Run()
    {
        CheckAppSettings();
        CheckDirectories();
        CheckDeliveryGate();
    }

    private void CheckAppSettings()
    {
        foreach (var setting in _settings.RequiredAppSettings)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(setting)))
                throw new InvalidOperationException($"Missing required app setting: {setting}");
        }
    }

    private void CheckDirectories()
    {
        foreach (var dir in _settings.RequiredDirectories)
        {
            var fullPath = Path.IsPathRooted(dir) ? dir : Path.Combine(_rootPath, dir);
            if (!Directory.Exists(fullPath))
                throw new InvalidOperationException($"Required directory missing: {fullPath}");
        }
    }

    private void CheckDeliveryGate()
    {
        if (!_settings.RequireDeliveryGate) return;

        var deliveryGatePath = Path.Combine(_rootPath, "delivery.gate.passed");
        if (!File.Exists(deliveryGatePath))
            throw new InvalidOperationException($"delivery.gate has not been satisfied: {deliveryGatePath}");
    }
}
'@

# --------------------------------------------------
# SETTINGS MODEL
# --------------------------------------------------
"startup/healthcheck/models/HealthCheckSettings.cs" = @'
using System;
using System.Collections.Generic;

namespace Startup.HealthCheck;

public sealed class HealthCheckSettings
{
    public bool RequireDeliveryGate { get; init; }
    public IReadOnlyList<string> RequiredAppSettings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredDirectories { get; init; } = Array.Empty<string>();
}
'@

# --------------------------------------------------
# BOOTSTRAP EXTENSION
# --------------------------------------------------
"startup/healthcheck/extensions/StartupHealthCheckExtensions.cs" = @'
using Microsoft.Extensions.DependencyInjection;

namespace Startup.HealthCheck;

public static class StartupHealthCheckExtensions
{
    public static IServiceCollection AddStartupHealthCheck(
        this IServiceCollection services,
        HealthCheckSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<StartupHealthCheck>();
        return services;
    }
}
'@

# --------------------------------------------------
# LINEAGE
# --------------------------------------------------
"startup/healthcheck/lineage/emit-startup-healthcheck-lineage.json" = @'
{
  "eventType": "STARTUP_HEALTHCHECK",
  "description": "Startup dependency and invariant validation executed",
  "requiredFields": [
    "serviceName",
    "status",
    "timestamp"
  ]
}
'@

# --------------------------------------------------
# README
# --------------------------------------------------
"startup/healthcheck/README.md" = @'
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
'@
}

# ------------------------------------------------------------------
# EXECUTION
# ------------------------------------------------------------------

foreach ($file in $healthcheckFiles.GetEnumerator()) {
    New-FileIfMissing -Path $file.Key -Content $file.Value
}

Write-Host "`n[OK] startup.healthcheck structure initialized successfully." -ForegroundColor Cyan

