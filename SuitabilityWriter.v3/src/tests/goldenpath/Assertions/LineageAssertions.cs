using FluentAssertions;
using System.Text.Json;

namespace SuitabilityWriter.GoldenPath.Assertions;

public static class LineageAssertions
{
    public static void MustContainArtefact(
        this JsonElement lineage,
        string artefactName,
        string artefactVersion)
    {
        var matches = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.TryGetProperty("artefact", out var a) &&
                a.GetProperty("name").GetString() == artefactName &&
                a.GetProperty("version").GetString() == artefactVersion
            )
            .ToList();

        matches.Should().NotBeEmpty(
            $"artefact '{artefactName}' version '{artefactVersion}' must appear in lineage"
        );

        matches.All(e =>
            !string.IsNullOrWhiteSpace(
                e.GetProperty("artefact").GetProperty("hash").GetString()
            )
        ).Should().BeTrue(
            "all artefacts must have a cryptographic hash recorded"
        );
    }

    public static void MustContainStage(
        this JsonElement lineage,
        string stage)
    {
        var exists = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Any(e =>
                e.GetProperty("stage").GetString() == stage
            );

        exists.Should().BeTrue(
            $"lineage must contain stage '{stage}'"
        );
    }

    public static void MustContainAdviserApproval(
        this JsonElement lineage)
    {
        var approvals = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("stage").GetString() == "Delivery" &&
                e.GetProperty("action").GetString() == "AdviserApproved"
            )
            .ToList();

        approvals.Should().ContainSingle(
            "exactly one adviser approval must be recorded"
        );

        approvals.Single()
            .GetProperty("performedBy")
            .GetString()
            .Should().NotBeNullOrWhiteSpace(
                "adviser identity must be recorded"
            );
    }

    public static void MustBeChronologicallyOrdered(
        this JsonElement lineage)
    {
        var timestamps = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Select(e => e.GetProperty("timestampUtc").GetDateTimeOffset())
            .ToList();

        timestamps.Should().BeInAscendingOrder(
            "lineage must represent an immutable timeline"
        );
    }
}