using AndroidContent = Android.Content;

namespace MobileApp.Android;

public class AndroidShareService : IShareService
{
    /// <summary>
    /// Shares plain text via the Android share sheet (<see cref="AndroidContent.Intent.ActionSend"/> wrapped in
    /// a chooser), so the user can pick Gmail, Messages, Slack, etc. Used by the Debug tab's "Share diagnostics"
    /// action; Desktop has no implementation of <see cref="IShareService"/> and falls back to a file-save dialog.
    /// </summary>
    /// <param name="subject">Passed as <see cref="AndroidContent.Intent.ExtraSubject"/> and as the chooser title.</param>
    /// <param name="content">Passed as <see cref="AndroidContent.Intent.ExtraText"/>.</param>
    public void ShareText(string subject, string content)
    {
        var intent = new AndroidContent.Intent(AndroidContent.Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(AndroidContent.Intent.ExtraSubject, subject);
        intent.PutExtra(AndroidContent.Intent.ExtraText, content);

        var chooser = AndroidContent.Intent.CreateChooser(intent, subject);
        chooser?.AddFlags(AndroidContent.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(chooser);
    }
}
