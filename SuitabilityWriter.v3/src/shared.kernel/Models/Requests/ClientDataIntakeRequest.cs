namespace Shared.Kernel.Models.Requests;

public class ClientDataIntakeRequest
{
    public string AdviceScope { get; set; } = string.Empty;
    public bool ConsentConfirmed { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ClientApplicantProfile Applicant { get; set; } = new();
    public ClientHouseholdProfile Household { get; set; } = new();
    public ClientFinancialProfile FinancialProfile { get; set; } = new();
    public ClientRiskAssessment RiskAssessment { get; set; } = new();
    public List<ClientObjective> Objectives { get; set; } = [];
}

public class ClientApplicantProfile
{
    public string GivenName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string Residency { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
}

public class ClientHouseholdProfile
{
    public string MaritalStatus { get; set; } = string.Empty;
    public int Dependants { get; set; }
    public bool VulnerabilityFlag { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class ClientFinancialProfile
{
    public decimal? AnnualIncome { get; set; }
    public decimal? MonthlyDisposableIncome { get; set; }
    public decimal? LiquidAssets { get; set; }
    public decimal? Liabilities { get; set; }
    public decimal? NetWorth { get; set; }
    public int? EmergencyFundMonths { get; set; }
}

public class ClientRiskAssessment
{
    public string RiskProfile { get; set; } = string.Empty;
    public string CapacityForLoss { get; set; } = string.Empty;
    public string KnowledgeAndExperience { get; set; } = string.Empty;
    public string TimeHorizon { get; set; } = string.Empty;
}

public class ClientObjective
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateOnly? TargetDate { get; set; }
    public decimal? TargetAmount { get; set; }
}