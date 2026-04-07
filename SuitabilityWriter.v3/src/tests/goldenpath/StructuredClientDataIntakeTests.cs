using FluentAssertions;
using Shared.Kernel.Models.Requests;
using Shared.Kernel.Validation;

[TestClass]
[TestCategory("GoldenPath")]
public class StructuredClientDataIntakeTests
{
    [TestMethod]
    public void Validate_returns_errors_when_client_intake_is_missing()
    {
        var request = new SuitabilityEvaluationRequest
        {
            CaseId = "CASE-001"
        };

        var errors = SuitabilityEvaluationRequestValidator.Validate(request);

        errors.Should().Contain(error => error.Contains("Please upload client data first", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Validate_accepts_complete_structured_client_intake()
    {
        var request = new SuitabilityEvaluationRequest
        {
            CaseId = "CASE-002",
            ClientData = new ClientDataIntakeRequest
            {
                AdviceScope = "Retirement",
                ConsentConfirmed = true,
                Applicant = new ClientApplicantProfile
                {
                    GivenName = "Jane",
                    FamilyName = "Doe",
                    DateOfBirth = new DateOnly(1985, 1, 1),
                    Residency = "UK",
                    EmploymentStatus = "Employed"
                },
                FinancialProfile = new ClientFinancialProfile
                {
                    AnnualIncome = 65000m,
                    MonthlyDisposableIncome = 1500m,
                    LiquidAssets = 25000m,
                    EmergencyFundMonths = 6
                },
                RiskAssessment = new ClientRiskAssessment
                {
                    RiskProfile = "Balanced",
                    CapacityForLoss = "Moderate",
                    KnowledgeAndExperience = "Intermediate",
                    TimeHorizon = "10+ years"
                },
                Objectives =
                [
                    new ClientObjective
                    {
                        Category = "Retirement",
                        Description = "Retire at age 65",
                        Priority = "High",
                        TargetAmount = 500000m
                    }
                ]
            }
        };

        var errors = SuitabilityEvaluationRequestValidator.Validate(request);

        errors.Should().BeEmpty();
    }
}