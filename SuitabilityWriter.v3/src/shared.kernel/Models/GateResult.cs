public sealed record GateResult(
    bool IsAllowed,
    BlockedResult? Blocked
);