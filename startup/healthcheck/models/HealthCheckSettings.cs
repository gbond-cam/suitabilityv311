using System;
using System.Collections.Generic;

namespace Startup.HealthCheck;

public sealed class HealthCheckSettings
{
    public bool RequireDeliveryGate { get; init; }
    public IReadOnlyList<string> RequiredAppSettings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredDirectories { get; init; } = Array.Empty<string>();
}
