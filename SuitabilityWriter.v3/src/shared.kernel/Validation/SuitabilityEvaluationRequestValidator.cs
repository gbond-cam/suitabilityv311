using Shared.Kernel.Models.Requests;

namespace Shared.Kernel.Validation;

public static class SuitabilityEvaluationRequestValidator
{
    public static IReadOnlyList<string> Validate(SuitabilityEvaluationRequest? request)
    {
        var errors = new List<string>();

        if (request is null)
        {
            errors.Add("A valid suitability evaluation payload is required.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(request.CaseId))
        {
            errors.Add("CaseId is required.");
        }

        if (request.ClientData is null)
        {
            errors.Add("Please upload client data first. A structured fact-find, at least one client objective, and a risk assessment are required before evaluation can start.");
            return errors;
        }

        var clientData = request.ClientData;
        var applicant = clientData.Applicant ?? new ClientApplicantProfile();
        var financialProfile = clientData.FinancialProfile ?? new ClientFinancialProfile();
        var riskAssessment = clientData.RiskAssessment ?? new ClientRiskAssessment();
        var objectives = clientData.Objectives ?? [];

        if (string.IsNullOrWhiteSpace(applicant.GivenName) || string.IsNullOrWhiteSpace(applicant.FamilyName))
        {
            errors.Add("Client name is required in the fact-find.");
        }

        if (applicant.DateOfBirth is null)
        {
            errors.Add("Client date of birth is required in the fact-find.");
        }

        if (string.IsNullOrWhiteSpace(request.AdviceScope) && string.IsNullOrWhiteSpace(clientData.AdviceScope))
        {
            errors.Add("Advice scope is required.");
        }

        if (objectives.Count == 0 || objectives.All(objective => string.IsNullOrWhiteSpace(objective.Description)))
        {
            errors.Add("At least one client objective is required.");
        }
        else if (objectives.Any(objective => string.IsNullOrWhiteSpace(objective.Priority)))
        {
            errors.Add("Each client objective must include a priority.");
        }

        if (financialProfile.AnnualIncome is null && financialProfile.MonthlyDisposableIncome is null)
        {
            errors.Add("Financial fact-find must include annual income or monthly disposable income.");
        }

        if (string.IsNullOrWhiteSpace(request.RiskProfile) && string.IsNullOrWhiteSpace(riskAssessment.RiskProfile))
        {
            errors.Add("A risk profile is required before evaluation can start.");
        }

        if (string.IsNullOrWhiteSpace(riskAssessment.CapacityForLoss))
        {
            errors.Add("Capacity for loss is required in the risk assessment.");
        }

        if (string.IsNullOrWhiteSpace(riskAssessment.KnowledgeAndExperience))
        {
            errors.Add("Knowledge and experience is required in the risk assessment.");
        }

        if (string.IsNullOrWhiteSpace(riskAssessment.TimeHorizon))
        {
            errors.Add("Investment time horizon is required in the risk assessment.");
        }

        if (!clientData.ConsentConfirmed)
        {
            errors.Add("Client consent must be confirmed before evaluation can start.");
        }

        return errors;
    }

    public static void ThrowIfInvalid(SuitabilityEvaluationRequest? request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }
    }
}