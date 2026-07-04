using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugNetworkViewModel : ViewModelBase
{
    private readonly IDebugNetworkStore _store;

    public DebugNetworkViewModel(IDebugNetworkStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugNetworkEntry> Entries { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        Entries.AddRange(_store.Snapshot().Reverse());
    }
}
