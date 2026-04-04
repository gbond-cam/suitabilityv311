using System.Text.Json.Serialization;

public sealed record MetricsDto
(
    [property: JsonPropertyName("totalEvents")] int TotalEvents,
    [property: JsonPropertyName("timeToDelivery")] string? TimeToDelivery,
    [property: JsonPropertyName("timeToBlock")] string? TimeToBlock
);