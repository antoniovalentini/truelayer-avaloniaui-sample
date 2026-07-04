using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugAuthLogsViewModel : ViewModelBase
{
    // MobileApp.Android/.Desktop types can't be referenced from this shared project
    // (dependency runs the other way), so their ILogger<T> category names
    // (== typeof(T).FullName) are hardcoded here.
    private static readonly HashSet<string> AuthCategories =
    [
        typeof(AuthManager).FullName!,
        "MobileApp.Android.MainActivity",
        "MobileApp.Android.AndroidRedirectManager",
        "MobileApp.Desktop.DesktopRedirectManager",
    ];

    private readonly IDebugLogStore _store;

    public DebugAuthLogsViewModel(IDebugLogStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        Entries.AddRange(_store.Snapshot().Where(e => AuthCategories.Contains(e.Category)).Reverse());
    }
}
