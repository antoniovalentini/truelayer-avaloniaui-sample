using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugLogsViewModel : ViewModelBase
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

    private readonly IDebugStore<DebugLogEntry> _store;

    public DebugLogsViewModel(IDebugStore<DebugLogEntry> store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    public LogLevel?[] AvailableLevels { get; } =
        [null, LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private LogLevel? _levelFilter;
    [ObservableProperty] private bool _authOnly;

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        var filtered = _store.Snapshot()
            .Where(e => LevelFilter is null || e.Level == LevelFilter)
            .Where(e => !AuthOnly || AuthCategories.Contains(e.Category))
            .Where(e => string.IsNullOrWhiteSpace(SearchText) || e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .Reverse();
        Entries.AddRange(filtered);
    }

    partial void OnSearchTextChanged(string? value) => Refresh();
    partial void OnLevelFilterChanged(LogLevel? value) => Refresh();
    partial void OnAuthOnlyChanged(bool value) => Refresh();
}
