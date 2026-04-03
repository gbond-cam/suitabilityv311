public interface ILineageWriter
{
    Task AppendAsync(LineageRecord record);
}