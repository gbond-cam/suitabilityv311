using FluentAssertions;
using SuitabilityWriter.GoldenPath.Assertions;
using System.IO.Compression;

[TestClass]
[TestCategory("GoldenPath")]
public class GoldenPathEvidenceBundleTests : GoldenPathTestBase
{
    [TestMethod]
    public async Task Evidence_bundle_must_embed_all_verified_artefact_binaries()
    {
        var caseId = "GOLDEN-001";

        await RunFullPipeline(caseId);

        var zipBytes = await GetEvidenceBundleZip(caseId);

        zipBytes.Should().NotBeNull();
        zipBytes.Length.Should().BeGreaterThan(0);
        zipBytes.MustEmbedAllVerifiableArtefacts();
        zipBytes.MustNotEmbedArtefactsWithHashMismatch();
    }

    [TestMethod]
    public async Task Evidence_bundle_must_include_revocation_list()
    {
        var zip = await GetEvidenceBundleZip("GOLDEN-001");

        using var z = new ZipArchive(new MemoryStream(zip));
        z.GetEntry("signature/revocation-list.json")
            .Should().NotBeNull("revocation list must be embedded for offline verification");
    }

    [TestMethod]
    public async Task Evidence_bundle_must_include_public_key()
    {
        var zipBytes = await GetEvidenceBundleZip("GOLDEN-001");

        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        zip.GetEntry("public-key.pem")
            .Should().NotBeNull("public verification requires the public signing key");
    }

    [TestMethod]
    public async Task Evidence_bundle_must_be_encrypted()
    {
        using var response = await GetEvidenceBundleResponse("GOLDEN-001");

        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/json");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("encryptedKey");
        json.Should().Contain("ciphertext");
    }

    [TestMethod]
    public async Task Encrypted_bundle_must_include_schema_and_version()
    {
        var envelope = await GetEncryptedEnvelope("GOLDEN-001");

        envelope.Schema.Should().Be("com.consilium.evidence.envelope");
        envelope.SchemaVersion.Should().Be("1.0.0");
    }

    [TestMethod]
    public async Task Encrypted_bundle_must_validate_against_schema()
    {
        var json = await GetEncryptedEnvelopeJson("GOLDEN-001");

        JsonSchemaValidator.Validate(
            json,
            "schemas/encrypted-bundle-envelope.schema.json");
    }

    [TestMethod]
    public async Task Export_must_fail_if_signing_key_revoked()
    {
        await RevokeSigningKeyInKeyVault("zip-signing-key-v1");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => GetEvidenceBundleZip("GOLDEN-001"));
    }
}