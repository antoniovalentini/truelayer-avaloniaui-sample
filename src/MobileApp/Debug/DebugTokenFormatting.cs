using System;

namespace MobileApp.Debug;

public static class DebugTokenFormatting
{
    public static string GetExpiryStatus(OAuthToken token, DateTimeOffset now)
    {
        var expiresAt = token.IssuedAt + TimeSpan.FromSeconds(token.ExpiresIn);
        var remaining = expiresAt - now;

        return remaining <= TimeSpan.Zero
            ? $"Expired {FormatDuration(-remaining)} ago"
            : $"Expires in {FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{(int)duration.TotalMinutes}m";

    public static string MaskSecret(string value)
    {
        const int visibleSuffixLength = 4;
        const string mask = "••••••••";

        return string.IsNullOrEmpty(value) || value.Length <= visibleSuffixLength
            ? mask
            : mask + value[^visibleSuffixLength..];
    }
}
