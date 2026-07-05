using System;

namespace MobileApp.Debug;

public sealed record DebugNetworkEntry(
    DateTimeOffset Timestamp,
    string Method,
    string Uri,
    int? StatusCode,
    long DurationMs,
    string? TraceId,
    string? Error);
