public sealed class BlobLineageStore :
    ILineageWriter,
    ILineageReader
{
    public Task AppendAsync(LineageRecord record)
    {
        // Append-only write (no overwrite, no update)
        // Storage choice is implementation detail
        return Task.CompletedTask;
    }

    public Task<LineageEnvelope> GetByCaseIdAsync(string caseId)
    {
        // Read-only aggregation
        return Task.FromResult(
            new LineageEnvelope(caseId, Array.Empty<LineageRecord>())
        );
    }
}