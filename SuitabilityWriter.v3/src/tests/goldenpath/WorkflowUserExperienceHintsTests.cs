using FluentAssertions;
using Shared.Kernel.Models.Responses;

[TestClass]
[TestCategory("GoldenPath")]
public class WorkflowUserExperienceHintsTests
{
    [TestMethod]
    public void Apply_sets_upload_prompt_when_workflow_has_not_started()
    {
        var state = new CaseWorkflowStateResponse
        {
            CaseId = "CASE-UX-001",
            Status = "pending",
            Steps =
            [
                new WorkflowStepStateResponse { Step = "data.upload.validation", Status = "pending" },
                new WorkflowStepStateResponse { Step = "suitability.evaluate", Status = "pending" },
                new WorkflowStepStateResponse { Step = "report.generate", Status = "pending" }
            ]
        };

        WorkflowUserExperienceHints.Apply(state);

        state.NextPrompt.Should().Be("Please upload client data first.");
        state.ProgressPercentage.Should().Be(0);
        state.CurrentStage.Should().Be("Awaiting client data");
    }

    [TestMethod]
    public void Apply_sets_evaluation_prompt_when_prerequisites_are_ready()
    {
        var state = new CaseWorkflowStateResponse
        {
            CaseId = "CASE-UX-002",
            Status = "awaiting-action",
            Steps =
            [
                new WorkflowStepStateResponse { Step = "data.upload.validation", Status = "completed" },
                new WorkflowStepStateResponse { Step = "suitability.evaluate", Status = "pending" },
                new WorkflowStepStateResponse { Step = "report.generate", Status = "pending" }
            ]
        };

        WorkflowUserExperienceHints.Apply(state);

        state.NextPrompt.Should().Be("Shall I start the evaluation now?");
        state.ProgressPercentage.Should().Be(34);
        state.CurrentStage.Should().Be("Client data upload");
    }

    [TestMethod]
    public void Apply_sets_secure_download_link_when_report_is_ready()
    {
        var response = new AcceptedResponse
        {
            Operation = "report.generate",
            Status = "completed",
            DownloadUrl = "https://contoso.sharepoint.com/reports/case-1/report.pdf"
        };

        WorkflowUserExperienceHints.Apply(response);

        response.SecureDownloadUrl.Should().Be("https://contoso.sharepoint.com/reports/case-1/report.pdf");
        response.NextPrompt.Should().Be("Your secure report download link is ready.");
        response.ProgressPercentage.Should().Be(100);
    }

    [TestMethod]
    public void Apply_preserves_explicit_stage_progress_and_prompt_for_step_specific_updates()
    {
        var response = new AcceptedResponse
        {
            Operation = "evidence.intake",
            Status = "completed",
            CurrentStage = "Client data upload",
            ProgressPercentage = 34,
            NextPrompt = "Shall I start the evaluation now?"
        };

        WorkflowUserExperienceHints.Apply(response);

        response.CurrentStage.Should().Be("Client data upload");
        response.ProgressPercentage.Should().Be(34);
        response.NextPrompt.Should().Be("Shall I start the evaluation now?");
    }
}
