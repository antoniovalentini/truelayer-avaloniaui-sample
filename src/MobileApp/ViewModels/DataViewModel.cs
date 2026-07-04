using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MobileApp.Models;
using TrueLayer;
using TrueLayer.Auth;

namespace MobileApp.ViewModels;

public partial class DataViewModel : ViewModelBase
{
    private readonly ITrueLayerClient _tlClient;
    private readonly IOptionsFactory<TrueLayerOptions> _options;
    private readonly IAuthTokenStorage _tokenStorage;
    private readonly IMessenger _messenger;
    private readonly IRedirectManager _redirectManager;
    private readonly ILogger<DataViewModel> _logger;

    private readonly string[] _scopes =
    [
        "accounts",
        "balance",
        "cards",
        "direct_debits",
        "info",
        "offline_access",
        "standing_orders",
        "transactions"
    ];

    protected List<OAuthToken> Tokens = [];

    public DataViewModel(
        ITrueLayerClient tlClient,
        IOptionsFactory<TrueLayerOptions> options,
        IAuthTokenStorage tokenStorage,
        IMessenger messenger,
        IRedirectManager redirectManager,
        ILogger<DataViewModel> logger)
    {
        _tlClient = tlClient;
        _options = options;
        _tokenStorage = tokenStorage;
        _messenger = messenger;
        _redirectManager = redirectManager;
        _logger = logger;

        Errors.CollectionChanged += OnErrorsCollectionChanged;

        messenger.Register<DataViewModel, CallbackReceivedMessage>(this, (__, message) =>
        {
            if (message.Args.QueryParams.TryGetValue("code", out var code))
            {
                _ = ExchangeCode(code);
                return;
            }

            if (!message.Args.QueryParams.TryGetValue("error", out var error)) return;
            _logger.LogWarning("Authentication error: {Error}", error);
            Errors.Add($"Error during authentication: {error}");
        });

        messenger.Register<DataViewModel, AccountRemovedMessage>(this, (__, message) =>
        {
            var token = Tokens.FirstOrDefault(t => t.ProviderId == message.ProviderId);
            if (token is null) return;

            _logger.LogInformation("Removing token for tokens list: {ProviderId}", message.ProviderId);
            Tokens.Remove(token);
            _ = GetAccountsAsync();
        });

        messenger.Register<DataViewModel, SettingsRestoredMessage>(this, (_, _) =>
        {
            var tokens = _tokenStorage.LoadTokens();
            if (tokens is null || tokens.Length == 0)
            {
                _logger.LogInformation("Token is null");
                return;
            }

            Tokens.Clear();
            Tokens.AddRange(tokens);

            _ = RefreshTokenAsync();
        });

        var tokens = _tokenStorage.LoadTokens();
        if (tokens is null || tokens.Length == 0)
        {
            _logger.LogInformation("Token is null");
            return;
        }

        Tokens.AddRange(tokens);

        _ = RefreshTokenAsync();
    }

    private async Task ExchangeCode(string code)
    {
        using var activity = App.Source.StartActivity();
        activity?.SetTag("av.operation", "exchange-code");

        if (string.IsNullOrWhiteSpace(code))
        {
            Errors.Add("Code cannot be empty.");
            return;
        }

        var response = await _tlClient.Auth.ExchangeCode(new ExchangeCodeRequest
        {
            Code = code,
            RedirectUri = _redirectManager.RedirectUri,
        });

        if (!response.IsSuccessful)
        {
            Errors.Add($"Error exchanging code: {response.StatusCode} - {Helpers.ExtractErrors(response.Problem?.Errors)} - {response.TraceId}");
            return;
        }

        await AddTokenAsync(response.Data);

        _ = GetAccountsAsync();
    }

    [ObservableProperty] private bool _loading;

    [RelayCommand]
    private async Task RefreshTokenAsync()
    {
        using var activity = App.Source.StartActivity();
        activity?.SetTag("av.operation", "refresh-token");

        Balances.Clear();
        Loading = true;
        var responses = new List<ExchangeCodeResponse>();
        var invalidTokens = new List<OAuthToken>();
        // snapshot the list before iterating
        // so the enumerator is over a private copy and mutations to Tokens mid-await cannot affect it.
        foreach (var oAuthToken in Tokens.ToList())
        {
            var response = await _tlClient.Auth.RefreshToken(oAuthToken.RefreshToken);

            if (!response.IsSuccessful)
            {
                Errors.Add($"Error refreshing token for {oAuthToken.ProviderId}: {response.StatusCode} - {Helpers.ExtractErrors(response.Problem?.Errors)} - {response.TraceId}");
                _logger.LogError("Error refreshing token for {ProviderId}: {StatusCode} - {Errors} - {TraceId}", oAuthToken.ProviderId, response.StatusCode, Helpers.ExtractErrors(response.Problem?.Errors), response.TraceId);
                invalidTokens.Add(oAuthToken);
                continue;
            }
            responses.Add(response.Data);
        }

        foreach (var invalidToken in invalidTokens)
        {
            _logger.LogInformation("Deleting invalid token for provider: {ProviderId}", invalidToken.ProviderId);
            Tokens.Remove(invalidToken);
        }
        foreach (var exchangeCodeResponse in responses)
        {
            await AddTokenAsync(exchangeCodeResponse);
        }

        await GetAccountsAsync();
        Loading = false;
    }

    private async Task AddTokenAsync(ExchangeCodeResponse response)
    {
        if (new JwtSecurityTokenHandler().ReadToken(response.AccessToken) is not JwtSecurityToken jtw)
        {
            _logger.LogError("Invalid JWT token.");
            return;
        }

        var providerId = jtw.Claims.FirstOrDefault(c => c.Type == "connector_id")?.Value ?? "unknown-provider";

        if (Tokens.FirstOrDefault(t => t.ProviderId == providerId) is { } existingToken)
        {
            _logger.LogInformation("Updating existing token for provider: {ProviderId}", providerId);
            Tokens.Remove(existingToken);
        }
        else
        {
            _messenger.Send(new DataProviderAddedMessage(providerId, providerId));
            _logger.LogInformation("Adding new token for provider: {ProviderId}", providerId);
        }

        Tokens.Add(new OAuthToken(
            providerId,
            response.AccessToken,
            response.TokenType,
            response.ExpiresIn,
            response.RefreshToken));

        await _tokenStorage.StoreTokens(Tokens.ToArray());
    }

    private bool HasAccessToken()
    {
        return Tokens is { Count: > 0 };
    }

    [RelayCommand(CanExecute = nameof(HasAccessToken))]
    private async Task GetAccountsAsync()
    {
        Balances.Clear();
        Loading = true;
        foreach (var oAuthToken in Tokens)
        {
            var response = await _tlClient.Data.GetAccounts(oAuthToken.AccessToken);
            if (!response.IsSuccessful)
            {
                _logger.LogWarning("Error retrieving accounts for {ProviderId}: {StatusCode} - {Errors} - {TraceId}", oAuthToken.ProviderId, response.StatusCode, Helpers.ExtractErrors(response.Problem?.Errors), response.TraceId);
                Errors.Add(response.StatusCode == HttpStatusCode.Unauthorized
                    ? $"Error retrieving accounts for {oAuthToken.ProviderId}: {HttpStatusCode.Unauthorized}. Try refreshing the tokens - {response.TraceId}"
                    : $"Error retrieving accounts for {oAuthToken.ProviderId}: {response.StatusCode} - {Helpers.ExtractErrors(response.Problem?.Errors)} - {response.TraceId}");
                continue;
            }

            _logger.LogInformation("Accounts retrieved successfully.");
            foreach (var account in response.Data?.Results ?? [])
            {
                _logger.LogInformation("Account ID: {AccountId}, Type: {AccountType}, Currency: {Currency}", account.AccountId, account.AccountType, account.Currency);
                await GetAccountBalanceAsync(account.AccountId, account.AccountNumber.Iban, account.Provider.ProviderId, oAuthToken.AccessToken);
            }
        }
        Loading = false;
    }

    private static Bitmap DefaultBankLogo => new(AssetLoader.Open(new Uri("avares://MobileApp/Assets/default-bank-logo.jpg")));
    private static Bitmap Logos(string providerId) => providerId switch
    {
        "xs2a-redsys-bbva-it" => new Bitmap(AssetLoader.Open(new Uri("avares://MobileApp/Assets/xs2a-redsys-bbva-it.png"))),
        "xs2a-ing-it" => new Bitmap(AssetLoader.Open(new Uri("avares://MobileApp/Assets/xs2a-ing-it.png"))),
        "ob-revolut-it" => new Bitmap(AssetLoader.Open(new Uri("avares://MobileApp/Assets/ob-revolut-it.png"))),
        _ => DefaultBankLogo,
    };

    private async Task GetAccountBalanceAsync(string accountId, string iban, string providerId, string accessToken)
    {
        var response = await _tlClient.Data.GetAccountBalance(accountId, accessToken);
        if (response.IsSuccessful)
        {
            _logger.LogInformation("Balance for account {AccountId} gathered successfully.", accountId);
            foreach (var balance in response.Data?.Results ?? [])
            {
                var currency = Helpers.GetCurrencySymbol(balance.Currency);
                Balances.Add(new ProviderBalance(accountId, iban, $"{currency} {balance.Available}", $"{currency} {balance.Current}", $"{currency} {balance.Overdraft}", Logos(providerId)));
            }
        }
        else
        {
            _logger.LogWarning("Error retrieving balance for account {AccountId}: {StatusCode} - {Errors} - {TraceId}", accountId, response.StatusCode, Helpers.ExtractErrors(response.Problem?.Errors), response.TraceId);
            Errors.Add($"Error retrieving balance for account {accountId}: {response.StatusCode} - {Helpers.ExtractErrors(response.Problem?.Errors)} - {response.TraceId}\n\n");
        }
    }

    public ObservableCollection<string> Errors { get; } = [];
    [ObservableProperty] private bool _hasErrors;
    private void OnErrorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasErrors = Errors.Count != 0;
        if (e.OldItems?.Count >= e.NewItems?.Count) return;
        _ = ClearErrorAsync(5);
    }

    private async Task ClearErrorAsync(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        if (Errors.Count == 0) return;
        Errors.RemoveAt(0);
    }

    public ObservableCollection<ProviderBalance> Balances { get; set; } = [];

    [RelayCommand]
    private void OpenAuthPage()
    {
        var authUri = new Uri(
            "https://auth.t7r.dev/" +
            "?response_type=code" +
            $"&client_id={_options.Create("TrueLayer").ClientId}" +
            $"&scope={string.Join("%20", _scopes)}" +
            $"&redirect_uri={_redirectManager.RedirectUri}" +
            "&providers=all-all-all");
        _logger.LogInformation("Auth URI: {AuthUri}", authUri.AbsoluteUri);

        _redirectManager.NavigateToRedirectUri(authUri);
    }
}
