using FluentAssertions;
using System.IO.Compression;
using System.Text.Json;

namespace SuitabilityWriter.GoldenPath.Assertions;

public static class EvidenceBundleAssertions
{
    public static void MustEmbedAllVerifiableArtefacts(
        this byte[] zipBytes)
    {
        using var zipStream = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        // Read manifest.json
        var manifestEntry = zip.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull("manifest.json must exist in evidence bundle");

        using var manifestStream = manifestEntry!.Open();
        var manifest = JsonDocument.Parse(manifestStream).RootElement;

        // Get embedding section
        manifest.TryGetProperty("embedding", out var embedding)
            .Should().BeTrue("manifest must include an 'embedding' section");

        foreach (var item in embedding.EnumerateArray())
        {
            var status = item.GetProperty("status").GetString();

            // Only enforce on verifiable artefacts
            if (status == "Embedded")
            {
                var zipPath = item.GetProperty("zipPath").GetString();
                zipPath.Should().NotBeNullOrWhiteSpace();

                zip.GetEntry(zipPath!)
                   .Should().NotBeNull($"artefact binary '{zipPath}' must be present in ZIP");
            }
        }
    }

    public static void MustNotEmbedArtefactsWithHashMismatch(
        this byte[] zipBytes)
    {
        using var zipStream = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var manifestEntry = zip.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();

        using var manifestStream = manifestEntry!.Open();
        var manifest = JsonDocument.Parse(manifestStream).RootElement;

        if (!manifest.TryGetProperty("embedding", out var embedding))
            return;

        foreach (var item in embedding.EnumerateArray())
        {
            var status = item.GetProperty("status").GetString();

            if (status == "Mismatch")
            {
                item.TryGetProperty("zipPath", out _)
                    .Should().BeFalse("artefacts with hash mismatch must not be embedded");
            }
        }
    }
}