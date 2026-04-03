public interface ILineageReader
{
    Task<LineageEnvelope> GetByCaseIdAsync(string caseId);
}