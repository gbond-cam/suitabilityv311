using System.Text.Json;

namespace Template.Routing.FunctionApp.Services;

public interface IRoutingPolicyStore
{
    JsonDocument LoadPolicy();
}
