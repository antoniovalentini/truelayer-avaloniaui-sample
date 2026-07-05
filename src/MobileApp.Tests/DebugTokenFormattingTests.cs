using System;
using MobileApp.Debug;
using Xunit;

namespace MobileApp.Tests;

public class DebugTokenFormattingTests
{
    [Fact]
    public void GetExpiryStatus_WhenNotYetExpired_ReturnsExpiresInMessage()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh", issuedAt);
        var now = issuedAt.AddMinutes(30);

        var result = DebugTokenFormatting.GetExpiryStatus(token, now);

        Assert.Equal("Expires in 30m", result);
    }

    [Fact]
    public void GetExpiryStatus_WhenExpired_ReturnsExpiredMessage()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh", issuedAt);
        var now = issuedAt.AddHours(2);

        var result = DebugTokenFormatting.GetExpiryStatus(token, now);

        Assert.Equal("Expired 1h 0m ago", result);
    }

    [Fact]
    public void GetExpiryStatus_WithDefaultIssuedAt_ReturnsExpired()
    {
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh");

        var result = DebugTokenFormatting.GetExpiryStatus(token, DateTimeOffset.UtcNow);

        Assert.StartsWith("Expired", result);
    }

    [Fact]
    public void MaskSecret_LongValue_KeepsLastFourCharacters()
    {
        var result = DebugTokenFormatting.MaskSecret("abcdef1234567890");

        Assert.Equal("••••••••7890", result);
    }

    [Fact]
    public void MaskSecret_ShortValue_FullyMasked()
    {
        var result = DebugTokenFormatting.MaskSecret("ab");

        Assert.Equal("••••••••", result);
    }
}
