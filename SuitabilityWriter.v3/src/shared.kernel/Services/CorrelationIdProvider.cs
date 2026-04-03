public sealed class CorrelationIdProvider : ICorrelationIdProvider
{
    public string Get() => Guid.NewGuid().ToString("N");
}