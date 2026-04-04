using SuitabilityWriter.GoldenPath.Assertions;
using System.Text.Json;

[TestClass]
[TestCategory("GoldenPath")]
public class GoldenPathEmergencyOverrideTests : GoldenPathTestBase
{
    [TestMethod]
    public async Task Emergency_override_must_be_fully_auditable()
    {
        var caseId = "GOLDEN-EMERGENCY-001";

        await RunPipelineWithEmergencyOverride(caseId);

        var json = await GetLineageJson(caseId);
        var lineage = JsonDocument.Parse(json).RootElement;

        lineage.MustContainEmergencyOverride();
        lineage.EmergencyOverrideMustHaveIncidentReference();
        lineage.EmergencyOverrideMustBeApproved();
        lineage.EmergencyOverrideMustPrecedeDelivery();
        lineage.EmergencyOverrideMustBeChronologicallyOrdered();
    }

    [TestMethod]
    public async Task Emergency_override_must_meet_SLA_timings()
    {
        var caseId = "GOLDEN-EMERGENCY-001";

        await RunPipelineWithEmergencyOverride(caseId);

        var json = await GetLineageJson(caseId);
        var lineage = JsonDocument.Parse(json).RootElement;

        lineage.EmergencyOverrideMustCreateIncidentWithin(TimeSpan.FromMinutes(15));
        lineage.IncidentMustCloseWithinSla(TimeSpan.FromDays(7));
        lineage.MustContainSlaEscalationWarnings(
            warnAt: TimeSpan.FromDays(4),
            criticalAt: TimeSpan.FromDays(6));
    }
}