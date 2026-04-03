public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}