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
