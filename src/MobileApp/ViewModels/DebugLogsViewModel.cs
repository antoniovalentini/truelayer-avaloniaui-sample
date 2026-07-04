using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugLogsViewModel : ViewModelBase
{
    private readonly IDebugLogStore _store;

    public DebugLogsViewModel(IDebugLogStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    public LogLevel?[] AvailableLevels { get; } =
        [null, LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private LogLevel? _levelFilter;

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        var filtered = _store.Snapshot()
            .Where(e => LevelFilter is null || e.Level == LevelFilter)
            .Where(e => string.IsNullOrWhiteSpace(SearchText) || e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .Reverse();
        Entries.AddRange(filtered);
    }

    partial void OnSearchTextChanged(string? value) => Refresh();
    partial void OnLevelFilterChanged(LogLevel? value) => Refresh();
}
