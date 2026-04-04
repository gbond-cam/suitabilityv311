public sealed class EncryptedBundle
{
    public string Algorithm { get; init; } = default!;
    public string Recipient { get; init; } = default!;
    public string EncryptedKey { get; init; } = default!;
    public string Iv { get; init; } = default!;
    public string Ciphertext { get; init; } = default!;
    public string Tag { get; init; } = default!;
}
