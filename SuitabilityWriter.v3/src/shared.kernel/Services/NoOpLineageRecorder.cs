public sealed class NoOpLineageRecorder : ILineageRecorder
{
    public Task RecordAsync(LineageRecord record) => Task.CompletedTask;
}
