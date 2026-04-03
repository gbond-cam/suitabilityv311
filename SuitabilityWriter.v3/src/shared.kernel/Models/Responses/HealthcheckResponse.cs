namespace Shared.Kernel.Models.Responses;

public class HealthcheckResponse
{
    public string Service { get; set; } = string.Empty;
    public string Status { get; set; } = "ok";
    public DateTimeOffset CheckedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
