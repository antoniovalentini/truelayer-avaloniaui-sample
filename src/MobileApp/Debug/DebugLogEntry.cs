using System;
using Microsoft.Extensions.Logging;

namespace MobileApp.Debug;

public sealed record DebugLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);
