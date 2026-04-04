using FluentAssertions;
using System.Text.Json;

namespace SuitabilityWriter.GoldenPath.Assertions;

public static class BlockedCaseAssertions
{
    public static void MustBeBlocked(
        this JsonElement lineage)
    {
        var blockedEvents = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == "Blocked"
            )
            .ToList();

        blockedEvents.Should().NotBeEmpty(
            "a blocked case must record at least one Blocked event"
        );
    }

    public static void MustHaveBlockReason(
        this JsonElement lineage)
    {
        var reasons = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == "Blocked"
            )
            .Select(e =>
                e.GetProperty("metadata")
                 .GetProperty("reasonCode")
                 .GetString()
            )
            .ToList();

        reasons.Should().NotBeEmpty(
            "blocked cases must include a reason code"
        );

        reasons.All(r => !string.IsNullOrWhiteSpace(r))
            .Should().BeTrue(
                "block reason codes must not be empty"
            );
    }

    public static void MustNotBeDelivered(
        this JsonElement lineage)
    {
        var deliveryEvents = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("stage").GetString() == "Delivery" ||
                e.GetProperty("action").GetString() == "AdviserApproved" ||
                e.GetProperty("action").GetString() == "Delivered"
            )
            .ToList();

        deliveryEvents.Should().BeEmpty(
            "blocked cases must never proceed to delivery or adviser approval"
        );
    }

    public static void MustNotGenerateReport(
        this JsonElement lineage)
    {
        var generationEvents = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("stage").GetString() == "Generation" ||
                e.GetProperty("action").GetString() == "ReportGenerated"
            )
            .ToList();

        generationEvents.Should().BeEmpty(
            "blocked cases must never generate a report"
        );
    }

    public static void MustBeChronologicallyBlocked(
        this JsonElement lineage)
    {
        var timeline = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .ToList();

        var blockedIndex = timeline.FindIndex(
            e => e.GetProperty("action").GetString() == "Blocked"
        );

        blockedIndex.Should().BeGreaterThanOrEqualTo(0,
            "a blocked case must record where blocking occurred");

        var afterBlock = timeline.Skip(blockedIndex + 1);

        afterBlock.Should().OnlyContain(e =>
            e.GetProperty("stage").GetString() == "Audit" ||
            e.GetProperty("action").GetString() == "Blocked",
            "no business stages may occur after a block"
        );
    }
}