using FluentAssertions;
using System.Text.Json;

namespace SuitabilityWriter.GoldenPath.Assertions;

public static class SlaTimingAssertions
{
    /// <summary>
    /// Ensures an emergency override results in incident creation within a bounded time.
    /// Example policy: incident created within 15 minutes of override.
    /// </summary>
    public static void EmergencyOverrideMustCreateIncidentWithin(
        this JsonElement lineage,
        TimeSpan maxDelay)
    {
        var (overrideEvent, incidentId) = GetSingleEventWithIncidentId(lineage, "EmergencyOverrideApplied");
        var incidentCreated = GetSingleEventForIncident(lineage, "IncidentCreated", incidentId);

        var overrideTime = overrideEvent.GetProperty("timestampUtc").GetDateTimeOffset();
        var createdTime = incidentCreated.GetProperty("timestampUtc").GetDateTimeOffset();

        (createdTime - overrideTime).Should().BeLessThanOrEqualTo(
            maxDelay,
            $"incident must be created within {maxDelay} of emergency override (incidentId={incidentId})"
        );
    }

    /// <summary>
    /// Ensures the incident is closed within an SLA window (e.g., 7 calendar days ≈ 5 business days).
    /// </summary>
    public static void IncidentMustCloseWithinSla(
        this JsonElement lineage,
        TimeSpan slaWindow)
    {
        var incidentCreated = GetSingleEventWithIncidentId(lineage, "IncidentCreated").eventElement;
        var incidentId = GetSingleEventWithIncidentId(lineage, "IncidentCreated").incidentId;

        var incidentClosed = GetSingleEventForIncident(lineage, "IncidentClosed", incidentId);

        var createdTime = incidentCreated.GetProperty("timestampUtc").GetDateTimeOffset();
        var closedTime = incidentClosed.GetProperty("timestampUtc").GetDateTimeOffset();

        (closedTime - createdTime).Should().BeLessThanOrEqualTo(
            slaWindow,
            $"incident must be closed within SLA window {slaWindow} (incidentId={incidentId})"
        );
    }

    /// <summary>
    /// Optional: ensures escalation warning events exist as SLA approaches breach.
    /// Supports two escalation levels: "Warning" and "Critical".
    /// </summary>
    public static void MustContainSlaEscalationWarnings(
        this JsonElement lineage,
        TimeSpan warnAt,
        TimeSpan criticalAt)
    {
        var (incidentCreated, incidentId) = GetSingleEventWithIncidentId(lineage, "IncidentCreated");
        var createdTime = incidentCreated.GetProperty("timestampUtc").GetDateTimeOffset();

        var warnings = GetEventsForIncident(lineage, "SlaWarningIssued", incidentId);

        warnings.Should().NotBeEmpty("SLA warning events should be recorded for approaching breach");

        // Validate at least one "Warning" and one "Critical"
        warnings.Any(e =>
            e.GetProperty("metadata").GetProperty("level").GetString() == "Warning"
        ).Should().BeTrue("an SLA Warning escalation should be recorded");

        warnings.Any(e =>
            e.GetProperty("metadata").GetProperty("level").GetString() == "Critical"
        ).Should().BeTrue("an SLA Critical escalation should be recorded");

        // Optional timing checks (approximate, based on recorded timestamps)
        var warningEvent = warnings.First(e =>
            e.GetProperty("metadata").GetProperty("level").GetString() == "Warning"
        );
        var criticalEvent = warnings.First(e =>
            e.GetProperty("metadata").GetProperty("level").GetString() == "Critical"
        );

        var warningTime = warningEvent.GetProperty("timestampUtc").GetDateTimeOffset();
        var criticalTime = criticalEvent.GetProperty("timestampUtc").GetDateTimeOffset();

        (warningTime - createdTime).Should().BeGreaterThanOrEqualTo(warnAt,
            $"Warning escalation should occur at or after {warnAt} from incident creation");

        (criticalTime - createdTime).Should().BeGreaterThanOrEqualTo(criticalAt,
            $"Critical escalation should occur at or after {criticalAt} from incident creation");
    }

    // -----------------------
    // Helpers (internal)
    // -----------------------

    private static (JsonElement eventElement, string incidentId) GetSingleEventWithIncidentId(
        JsonElement lineage,
        string action)
    {
        var matches = lineage.GetProperty("timeline")
            .EnumerateArray()
            .Where(e => e.GetProperty("action").GetString() == action)
            .ToList();

        matches.Should().ContainSingle($"exactly one '{action}' event must exist");

        var ev = matches.Single();

        ev.TryGetProperty("metadata", out var meta).Should().BeTrue($"'{action}' must include metadata");
        meta.TryGetProperty("incidentId", out var incidentIdProp).Should().BeTrue($"'{action}' must include metadata.incidentId");

        var incidentId = incidentIdProp.GetString();
        incidentId.Should().NotBeNullOrWhiteSpace($"'{action}' metadata.incidentId must not be empty");

        return (ev, incidentId!);
    }

    private static JsonElement GetSingleEventForIncident(
        JsonElement lineage,
        string action,
        string incidentId)
    {
        var matches = GetEventsForIncident(lineage, action, incidentId);
        matches.Should().ContainSingle($"exactly one '{action}' must exist for incidentId={incidentId}");
        return matches.Single();
    }

    private static List<JsonElement> GetEventsForIncident(
        JsonElement lineage,
        string action,
        string incidentId)
    {
        return lineage.GetProperty("timeline")
            .EnumerateArray()
            .Where(e =>
                e.GetProperty("action").GetString() == action &&
                e.TryGetProperty("metadata", out var m) &&
                m.TryGetProperty("incidentId", out var id) &&
                id.GetString() == incidentId
            )
            .ToList();
    }
}