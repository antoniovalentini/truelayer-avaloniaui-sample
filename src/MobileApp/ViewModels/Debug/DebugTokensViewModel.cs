using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugTokensViewModel : ViewModelBase
{
    private readonly IAuthTokenStorage _storage;

    public DebugTokensViewModel(IAuthTokenStorage storage)
    {
        _storage = storage;
        Refresh();
    }

    public ObservableCollection<DebugTokenDisplay> Tokens { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Tokens.Clear();
        var tokens = _storage.LoadTokens() ?? [];
        var now = DateTimeOffset.UtcNow;
        Tokens.AddRange(tokens.Select(t => new DebugTokenDisplay(
            t.ProviderId,
            DebugTokenFormatting.GetExpiryStatus(t, now),
            t.AccessToken,
            t.RefreshToken)));
    }
}

public sealed partial class DebugTokenDisplay : ObservableObject
{
    private readonly string _accessToken;
    private readonly string _refreshToken;

    public DebugTokenDisplay(string providerId, string expiryStatus, string accessToken, string refreshToken)
    {
        ProviderId = providerId;
        ExpiryStatus = expiryStatus;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    public string ProviderId { get; }
    public string ExpiryStatus { get; }

    [ObservableProperty] private bool _isRevealed;

    public string AccessTokenDisplay => IsRevealed ? _accessToken : DebugTokenFormatting.MaskSecret(_accessToken);
    public string RefreshTokenDisplay => IsRevealed ? _refreshToken : DebugTokenFormatting.MaskSecret(_refreshToken);

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(AccessTokenDisplay));
        OnPropertyChanged(nameof(RefreshTokenDisplay));
    }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;
}
