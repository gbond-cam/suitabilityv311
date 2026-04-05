using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

public sealed class JsonFileRoutingPolicyStore : IRoutingPolicyStore
{
    public JsonDocument LoadPolicy()
    {
        var configuredPath = Environment.GetEnvironmentVariable("ROUTING_POLICY_PATH") ?? "Policies/routing-policy.json";
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Environment.CurrentDirectory, configuredPath);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Routing policy file not found: {path}");

        var json = File.ReadAllText(path);
        return JsonDocument.Parse(json);
    }
}
