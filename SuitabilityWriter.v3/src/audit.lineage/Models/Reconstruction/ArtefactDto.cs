using System.Text.Json.Serialization;

public sealed record ArtefactDto
(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash
)
{
    public static ArtefactDto NotApplicable =>
        new("N/A", "N/A", "N/A");
}