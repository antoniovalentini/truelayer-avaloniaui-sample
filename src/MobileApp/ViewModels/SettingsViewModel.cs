using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MobileApp.Models;

namespace MobileApp.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAuthTokenStorage _storage;
    private readonly IMessenger _messenger;

    public SettingsViewModel(IAuthTokenStorage storage, IMessenger messenger)
    {
        _storage = storage;
        _messenger = messenger;
        var tokens = storage.LoadTokens();
        if (tokens is null) return;
        Accounts.AddRange(tokens);
    }

    public string Version { get; } = (Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown")
        .Split('+')[0];

    public ObservableCollection<OAuthToken> Accounts { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    private async Task RemoveAccount(string providerId)
    {
        var account = Accounts.FirstOrDefault(a => a.ProviderId == providerId);
        if (account is null) return;
        Accounts.Remove(account);
        await _storage.StoreTokens(Accounts.ToArray());
        _messenger.Send(new AccountRemovedMessage(providerId));
    }

    [RelayCommand]
    private void RefreshAccounts()
    {
        var tokens = _storage.LoadTokens();
        if (tokens is null) return;
        Accounts.Clear();
        Accounts.AddRange(tokens);
    }

    public async Task ExportSettingsAsync(Stream outputStream)
    {
        try
        {
            await _storage.ExportSettings(outputStream);
            StatusMessage = "Settings exported successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    public async Task ImportSettingsAsync(Stream inputStream)
    {
        try
        {
            await _storage.ImportSettings(inputStream);
            RefreshAccounts();
            StatusMessage = "Settings imported successfully.";
            _messenger.Send(new SettingsRestoredMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }
}
