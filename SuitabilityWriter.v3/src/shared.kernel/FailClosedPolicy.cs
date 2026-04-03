public sealed class FailClosedPolicy : IFailClosedPolicy
{
    public bool IsFailClosedEnabled() => true;
}