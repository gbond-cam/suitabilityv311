namespace Shared.Kernel.Models.Responses;

public class ValidationProblemResponse
{
    public string Message { get; set; } = "The request is invalid.";
    public string Prompt { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = [];
}