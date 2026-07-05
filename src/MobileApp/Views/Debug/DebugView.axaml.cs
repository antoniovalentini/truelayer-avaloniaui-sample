using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using MobileApp.ViewModels;

namespace MobileApp.Views;

public partial class DebugView : UserControl
{
    public DebugView()
    {
        InitializeComponent();
        ShareButton.Click += ShareButton_Click;
    }

    private DebugViewModel ViewModel => (DebugViewModel)DataContext!;

    private async void ShareButton_Click(object? sender, RoutedEventArgs e)
    {
        var bundle = ViewModel.BuildDiagnosticsBundle();
        var shareService = App.Instance.Services.GetService<IShareService>();

        if (shareService is not null)
        {
            shareService.ShareText("TrueMobile diagnostics", bundle);
            return;
        }

        await SaveToFileAsync(bundle);
    }

    private async Task SaveToFileAsync(string bundle)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Diagnostics",
            SuggestedFileName = "truemobile-diagnostics.txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(bundle);
    }
}
