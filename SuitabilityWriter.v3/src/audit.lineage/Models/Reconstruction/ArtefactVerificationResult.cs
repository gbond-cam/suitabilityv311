public sealed record ArtefactVerificationResult(
    string Name,
    string Version,
    string ExpectedHash,
    string? ActualHash,
    string Status,
    string? Message)
{
    public static ArtefactVerificationResult Verified(
        string name, string version, string expected) =>
        new(name, version, expected, expected, "Verified", null);

    public static ArtefactVerificationResult Mismatch(
        string name, string version, string expected, string actual) =>
        new(name, version, expected, actual, "Mismatch",
            "Computed hash does not match lineage");

    public static ArtefactVerificationResult NotVerified(
        string name, string version, string expected, string reason) =>
        new(name, version, expected, null, "NotVerified", reason);
}