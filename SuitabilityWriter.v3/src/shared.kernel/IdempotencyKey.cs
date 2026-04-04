using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class IdempotencyKey
{
    // Stable hash based on the meaningful identity of the event, excluding TimestampUtc.
    public static string From(LineageRecord record, string? discriminator = null)
    {
        var metadataJson = record.Metadata is null ? string.Empty : JsonSerializer.Serialize(record.Metadata);
        var input =
            $"{record.CaseId}|{record.Stage}|{record.Action}|{record.ArtefactName}|{record.ArtefactVersion}|{record.ArtefactHash}|{record.PerformedBy}|{discriminator ?? string.Empty}|{metadataJson}";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
