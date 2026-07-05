using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugNetworkViewModel : ViewModelBase
{
    private readonly IDebugStore<DebugNetworkEntry> _store;

    public DebugNetworkViewModel(IDebugStore<DebugNetworkEntry> store)
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
