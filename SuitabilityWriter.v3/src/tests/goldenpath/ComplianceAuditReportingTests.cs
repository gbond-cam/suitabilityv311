using FluentAssertions;

[TestClass]
[TestCategory("GoldenPath")]
public class ComplianceAuditReportingTests : GoldenPathTestBase
{
    [TestMethod]
    public async Task Regulatory_audit_report_must_include_immutable_archive_metadata()
    {
        var caseId = "GOLDEN-001";

        await RunFullPipeline(caseId);

        var json = await GetComplianceAuditReportJson(caseId);

        json.Should().Contain("\"complianceStatus\"");
        json.Should().Contain("\"regulatoryChecks\"");
        json.Should().Contain("\"immutableStorage\"");
        json.Should().Contain("\"retentionUntilUtc\"");
        json.Should().Contain("ReportGenerated");
    }
}
