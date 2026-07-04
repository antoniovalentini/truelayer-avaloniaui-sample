namespace MobileApp;

/// <summary>
/// Shares plain text via the OS share sheet. Only implemented on Android
/// (<c>AndroidShareService</c>, via <c>Intent.ActionSend</c> wrapped in a chooser) — there's no equivalent
/// "share sheet" concept on Desktop, so it has no registration for this interface. Consumers should resolve
/// it as optional (e.g. <c>GetService&lt;IShareService&gt;()</c>) and fall back to a platform-appropriate
/// alternative, such as the file-save dialog the Debug tab's "Share diagnostics" action uses on Desktop.
/// </summary>
public interface IShareService
{
    void ShareText(string subject, string content);
}
