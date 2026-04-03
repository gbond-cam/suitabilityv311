public interface ILineageRecorder
{
    Task RecordAsync(LineageRecord record);
}
