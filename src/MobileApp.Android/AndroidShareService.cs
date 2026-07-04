using AndroidContent = Android.Content;

namespace MobileApp.Android;

public class AndroidShareService : IShareService
{
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
