using SuitabilityWriter.GoldenPath.Assertions;
using System.Text.Json;

[TestClass]
[TestCategory("GoldenPath")]
public class GoldenPathBlockedCaseTests : GoldenPathTestBase
{
    [TestMethod]
    public async Task Blocked_case_must_not_proceed_beyond_validation()
    {
        var caseId = "GOLDEN-BLOCKED-001";

        await RunBlockedPipeline(caseId);

        var json = await GetLineageJson(caseId);
        var lineage = JsonDocument.Parse(json).RootElement;

        lineage.MustBeBlocked();
        lineage.MustHaveBlockReason();
        lineage.MustNotGenerateReport();
        lineage.MustNotBeDelivered();
        lineage.MustBeChronologicallyBlocked();
    }
}