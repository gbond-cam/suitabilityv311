using FluentAssertions;
using SuitabilityWriter.GoldenPath.Assertions;
using System.Text.Json;

[TestClass]
[TestCategory("GoldenPath")]
public class GoldenPathTests : GoldenPathTestBase
{
    [TestMethod]
    public async Task Full_advice_lifecycle_is_auditable_and_reconstructable()
    {
        var caseId = "GOLDEN-001";

        await RunFullPipeline(caseId);

        var json = await GetLineageJson(caseId);
        var lineage = JsonDocument.Parse(json).RootElement;

        lineage.MustContainArtefact("ClientEvidence", "v3.1");
        lineage.MustContainArtefact("PlaceholderSeverity", "v2");
        lineage.MustContainArtefact("ISA_Template", "v2.4");

        lineage.MustContainStage("Validation");
        lineage.MustContainStage("Routing");
        lineage.MustContainStage("Generation");
        lineage.MustContainStage("Delivery");

        lineage.MustContainAdviserApproval();
        lineage.MustBeChronologicallyOrdered();
    }
}