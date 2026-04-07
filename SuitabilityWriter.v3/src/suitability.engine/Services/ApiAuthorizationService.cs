using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Suitability.Engine.Services;

public interface IApiAuthorizationService
{
    Task<HttpResponseData?> AuthorizeAsync(HttpRequestData req, CancellationToken cancellationToken, params string[] routeSpecificRoleSettingNames);
}

public sealed class ApiAuthorizationService : IApiAuthorizationService
{
    private static readonly StringComparer RoleComparer = StringComparer.OrdinalIgnoreCase;
    private readonly ILogger<ApiAuthorizationService> _logger;

    public ApiAuthorizationService(ILogger<ApiAuthorizationService> logger)
    {
        _logger = logger;
    }

    public async Task<HttpResponseData?> AuthorizeAsync(HttpRequestData req, CancellationToken cancellationToken, params string[] routeSpecificRoleSettingNames)
    {
        if (!RequiresAzureAdAuth())
        {
            return null;
        }

        if (HasTrustedInternalAccess(req))
        {
            _logger.LogDebug("Allowing trusted internal API access for {Path}.", req.Url.AbsolutePath);
            return null;
        }

        var principal = TryReadPrincipal(req);
        if (principal is null)
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            unauthorized.Headers.Add("WWW-Authenticate", "Bearer");
            await unauthorized.WriteStringAsync("Azure AD authentication is required for this API.", cancellationToken);
            return unauthorized;
        }

        var requiredRoles = GetConfiguredRoles("API_ALLOWED_ROLES");
        foreach (var settingName in routeSpecificRoleSettingNames)
        {
            requiredRoles.UnionWith(GetConfiguredRoles(settingName));
        }

        if (requiredRoles.Count > 0 && !principal.Roles.Any(requiredRoles.Contains))
        {
            _logger.LogWarning(
                "Authenticated caller {Caller} is missing a required role for {Path}. Required={RequiredRoles}; Presented={PresentedRoles}",
                principal.Name ?? principal.UserId ?? "unknown",
                req.Url.AbsolutePath,
                string.Join(", ", requiredRoles),
                string.Join(", ", principal.Roles));

            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            await forbidden.WriteStringAsync("The authenticated identity does not have a required role for this API.", cancellationToken);
            return forbidden;
        }

        return null;
    }

    private static bool RequiresAzureAdAuth()
    {
        var configuredValue = Environment.GetEnvironmentVariable("REQUIRE_AAD_AUTH");
        if (!string.IsNullOrWhiteSpace(configuredValue) && bool.TryParse(configuredValue, out var parsed))
        {
            return parsed;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_AUTH_ENABLED")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    }

    private static HashSet<string> GetConfiguredRoles(string settingName)
    {
        var rawValue = Environment.GetEnvironmentVariable(settingName);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(RoleComparer);
    }

    private static AuthenticatedPrincipal? TryReadPrincipal(HttpRequestData req)
    {
        var principalId = TryGetHeaderValue(req, "X-MS-CLIENT-PRINCIPAL-ID");
        var principalName = TryGetHeaderValue(req, "X-MS-CLIENT-PRINCIPAL-NAME");
        var encodedPrincipal = TryGetHeaderValue(req, "X-MS-CLIENT-PRINCIPAL");

        if (string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            return string.IsNullOrWhiteSpace(principalId) && string.IsNullOrWhiteSpace(principalName)
                ? null
                : new AuthenticatedPrincipal(principalId, principalName, "AppService", Array.Empty<string>());
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPrincipal));
            var payload = JsonSerializer.Deserialize<EasyAuthPrincipalPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (payload is null)
            {
                return null;
            }

            var roles = payload.Claims?
                .Where(claim => IsRoleClaim(claim.Type) && !string.IsNullOrWhiteSpace(claim.Value))
                .Select(static claim => claim.Value!)
                .Distinct(RoleComparer)
                .ToArray() ?? Array.Empty<string>();

            return new AuthenticatedPrincipal(
                payload.UserId ?? principalId,
                payload.UserDetails ?? principalName,
                payload.IdentityProvider ?? "AzureAD",
                roles);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasTrustedInternalAccess(HttpRequestData req)
    {
        var expectedSecret = Environment.GetEnvironmentVariable("INTERNAL_API_SHARED_SECRET");
        var presentedSecret = TryGetHeaderValue(req, "X-Internal-Api-Key");

        if (string.IsNullOrWhiteSpace(expectedSecret) || string.IsNullOrWhiteSpace(presentedSecret))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSecret),
            Encoding.UTF8.GetBytes(presentedSecret));
    }

    private static string? TryGetHeaderValue(HttpRequestData req, string headerName)
    {
        return req.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static bool IsRoleClaim(string? claimType)
    {
        return !string.IsNullOrWhiteSpace(claimType) &&
               (string.Equals(claimType, "roles", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claimType, "role", StringComparison.OrdinalIgnoreCase) ||
                claimType.EndsWith("/claims/role", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record AuthenticatedPrincipal(string? UserId, string? Name, string? IdentityProvider, IReadOnlyCollection<string> Roles);

    private sealed class EasyAuthPrincipalPayload
    {
        [JsonPropertyName("identity_provider")]
        public string? IdentityProvider { get; set; }

        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonPropertyName("user_details")]
        public string? UserDetails { get; set; }

        [JsonPropertyName("claims")]
        public List<EasyAuthClaim>? Claims { get; set; }
    }

    private sealed class EasyAuthClaim
    {
        [JsonPropertyName("typ")]
        public string? Type { get; set; }

        [JsonPropertyName("val")]
        public string? Value { get; set; }
    }
}
