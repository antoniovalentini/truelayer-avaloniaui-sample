using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using MobileApp.ViewModels;

namespace MobileApp;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // Resolves by CLR namespace + type name, not file path — Views/ViewModels can live in
        // feature subfolders (e.g. Views/Debug, ViewModels/Debug) while keeping the flat
        // MobileApp.Views / MobileApp.ViewModels namespace this lookup depends on.
        var name = param.GetType().FullName!
            .Replace("ViewModel", "View", StringComparison.Ordinal)
            .Replace("Design.", string.Empty, StringComparison.Ordinal) // namespace
            .Replace("Design", string.Empty, StringComparison.Ordinal); // design prefix
        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase or ObservableObject;
    }
}
