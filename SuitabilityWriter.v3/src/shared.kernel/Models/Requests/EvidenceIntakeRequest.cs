namespace Shared.Kernel.Models.Requests;

public class EvidenceIntakeRequest
{
    public string CaseId { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public SharePointEvidenceReference? SharePoint { get; set; }
    public EvidenceIntakeOptions Options { get; set; } = new();
}

public class SharePointEvidenceReference
{
    public string SiteId { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public bool IncludeChildren { get; set; }
}

public class EvidenceIntakeOptions
{
    public string[] AllowedExtensions { get; set; } = [];
    public int? MaxFileCount { get; set; }
    public int? MaxFileSizeMb { get; set; }
}
