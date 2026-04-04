using FluentAssertions;
using System.Text.Json;

namespace SuitabilityWriter.GoldenPath.Assertions;

public static class EmergencyOverrideAssertions
{
    public static void MustContainEmergencyOverride(
        this JsonElement lineage)
    {
        var overrides = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == "EmergencyOverrideApplied"
            )
            .ToList();

        overrides.Should().NotBeEmpty(
            "emergency overrides must be explicitly recorded in lineage"
        );
    }

    public static void EmergencyOverrideMustHaveIncidentReference(
        this JsonElement lineage)
    {
        var overrides = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == "EmergencyOverrideApplied"
            );

        var hasIncidentReferences = overrides.All(e =>
        {
            if (!e.TryGetProperty("metadata", out var metadata))
            {
                return false;
            }

            return metadata.TryGetProperty("incidentId", out var incidentId)
                && !string.IsNullOrWhiteSpace(incidentId.GetString());
        });

        hasIncidentReferences.Should().BeTrue(
            "emergency overrides must reference an incident ID");
    }

    public static void EmergencyOverrideMustBeApproved(
        this JsonElement lineage)
    {
        var overrides = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == "EmergencyOverrideApplied"
            );

        var hasApprovals = overrides.All(e =>
        {
            var metadata = e.GetProperty("metadata");
            return metadata.TryGetProperty("approvedBy", out var approver)
                && !string.IsNullOrWhiteSpace(approver.GetString());
        });

        hasApprovals.Should().BeTrue(
            "emergency overrides must record who approved them");
    }

    public static void EmergencyOverrideMustPrecedeDelivery(
        this JsonElement lineage)
    {
        var timeline = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .ToList();

        var overrideIndex = timeline.FindIndex(
            e => e.GetProperty("action").GetString() == "EmergencyOverrideApplied"
        );

        overrideIndex.Should().BeGreaterThanOrEqualTo(0,
            "emergency override must appear in the lineage timeline");

        var deliveryIndex = timeline.FindIndex(
            e => e.GetProperty("action").GetString() == "AdviserApproved" ||
                 e.GetProperty("action").GetString() == "Delivered"
        );

        if (deliveryIndex >= 0)
        {
            overrideIndex.Should().BeLessThan(deliveryIndex,
                "emergency override must occur before delivery or approval");
        }
    }

    public static void EmergencyOverrideMustBeChronologicallyOrdered(
        this JsonElement lineage)
    {
        var timestamps = lineage
            .GetProperty("timeline")
            .EnumerateArray()
            .Select(e => e.GetProperty("timestampUtc").GetDateTimeOffset())
            .ToList();

        timestamps.Should().BeInAscendingOrder(
            "emergency overrides must not disrupt lineage ordering"
        );
    }
}