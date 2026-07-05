using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using OneOf;
using TrueLayer;
using TrueLayer.Auth;
using TrueLayer.Data;
using TrueLayer.Data.Model;
using TrueLayer.Mandates;
using TrueLayer.MerchantAccounts;
using TrueLayer.Payments;
using TrueLayer.Payments.Model;
using TrueLayer.Payments.Model.AuthorizationFlow;
using TrueLayer.PaymentsProviders;
using TrueLayer.Payouts;

namespace MobileApp.Fakes;

public class FakeTrueLayerClient : ITrueLayerClient
{
    public IAuthApi Auth => new FakeAuthApi();
    public IPaymentsApi Payments { get; } = new FakePaymentsApi();
    public IDataApi Data { get; } = new FakeDataApi();
    public IPaymentsProvidersApi PaymentsProviders => throw new NotImplementedException();
    public IPayoutsApi Payouts => throw new NotImplementedException();
    public IMerchantAccountsApi MerchantAccounts => throw new NotImplementedException();
    public IMandatesApi Mandates => throw new NotImplementedException();
}

public class FakeAuthApi : IAuthApi
{
    private readonly ExchangeCodeResponse _fakeExchangeCodeResponse = new()
    {
        AccessToken = new JwtSecurityTokenHandler().CreateJwtSecurityToken().RawData,
        RefreshToken = "fake-refresh-token",
        ExpiresIn = 3600,
        TokenType = "Bearer",
    };

    public ValueTask<ApiResponse<GetAuthTokenResponse>> GetAuthToken(GetAuthTokenRequest authTokenRequest, CancellationToken cancellationToken = new())
    {
        return new ValueTask<ApiResponse<GetAuthTokenResponse>>(new ApiResponse<GetAuthTokenResponse>(HttpStatusCode.OK, "fake-trace-id"));
    }

    public async ValueTask<ApiResponse<ExchangeCodeResponse>> ExchangeCode(ExchangeCodeRequest exchangeCodeRequest, CancellationToken cancellationToken = new())
    {
        await Task.CompletedTask;
        return new ApiResponse<ExchangeCodeResponse>(_fakeExchangeCodeResponse, HttpStatusCode.OK, "fake-trace-id");
    }

    public async ValueTask<ApiResponse<ExchangeCodeResponse>> RefreshToken(string refreshToken, CancellationToken cancellationToken = new())
    {
        await Task.Delay(500, cancellationToken);
        return new ApiResponse<ExchangeCodeResponse>(_fakeExchangeCodeResponse, HttpStatusCode.OK, "fake-trace-id");
    }
}

public class FakePaymentsApi : IPaymentsApi
{
    public Task<ApiResponse<OneOf<CreatePaymentResponse.AuthorizationRequired, CreatePaymentResponse.Authorized, CreatePaymentResponse.Failed, CreatePaymentResponse.Authorizing>>>
        CreatePayment(CreatePaymentRequest paymentRequest, string? idempotencyKey, CancellationToken cancellationToken = new())
    {
        var data = new CreatePaymentResponse.AuthorizationRequired
        {
            Id = "test-payment-id",
            ResourceToken = "test-resource-token",
            Status = "AuthorizationRequired",
            User = new PaymentUserResponse("user-id"),
        };
        return Task.FromResult(
            new ApiResponse<OneOf<CreatePaymentResponse.AuthorizationRequired, CreatePaymentResponse.Authorized, CreatePaymentResponse.Failed, CreatePaymentResponse.Authorizing>>(
                data, HttpStatusCode.OK, "test-trace-id"));
    }

    public string CreateHostedPaymentPageLink(string paymentId, string paymentToken, Uri returnUri) =>
        new Uri("https://example.com/hosted-payment-page?paymentId=" + paymentId + "&token=" + paymentToken).ToString();

    public Task<ApiResponse<OneOf<GetPaymentResponse.AuthorizationRequired, GetPaymentResponse.Authorizing, GetPaymentResponse.Authorized, GetPaymentResponse.Executed, GetPaymentResponse.Settled, GetPaymentResponse.Failed, GetPaymentResponse.AttemptFailed>>> GetPayment(string id, CancellationToken cancellationToken = new())
    {
        throw new NotImplementedException();
    }

    public Task<ApiResponse<OneOf<AuthorizationFlowResponse.AuthorizationFlowAuthorizing, AuthorizationFlowResponse.AuthorizationFlowAuthorizationFailed>>> StartAuthorizationFlow(string paymentId, string? idempotencyKey, StartAuthorizationFlowRequest request,
        CancellationToken cancellationToken = new())
    {
        throw new NotImplementedException();
    }

    public Task<ApiResponse<CreatePaymentRefundResponse>> CreatePaymentRefund(string paymentId, string? idempotencyKey, CreatePaymentRefundRequest request,
        CancellationToken cancellationToken = new())
    {
        throw new NotImplementedException();
    }

    public Task<ApiResponse<ListPaymentRefundsResponse>> ListPaymentRefunds(string paymentId, CancellationToken cancellationToken = new())
    {
        throw new NotImplementedException();
    }

    public Task<ApiResponse<OneOf<RefundPending, RefundAuthorized, RefundExecuted, RefundFailed>>> GetPaymentRefund(string paymentId, string refundId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<ApiResponse> CancelPayment(string paymentId, string? idempotencyKey,
        CancellationToken cancellationToken = new())
    {
        throw new NotImplementedException();
    }
}

public class FakeDataApi : IDataApi
{
    public async Task<ApiResponse<GetAccountsResponse>> GetAccounts(string accessToken, CancellationToken cancellationToken = new())
    {
        await Task.CompletedTask;
        var accounts = new GetAccountsResponse
        {
            Results = _accounts,
        };
        return new ApiResponse<GetAccountsResponse>(accounts, HttpStatusCode.OK, "fake-trace-id");
    }

    public async Task<ApiResponse<GetAccountBalanceResponse>> GetAccountBalance(string accountId, string accessToken, CancellationToken cancellationToken = new())
    {
        await Task.CompletedTask;
        var response = new GetAccountBalanceResponse
        {
            Results =
            [
                accountId switch
                {
                    AccountId1 => new DataAccountBalance
                    {
                        Currency = "GBP",
                        Available = 1000.00m,
                        Current = 1200.00m,
                        Overdraft = -200.00m,
                        UpdateTimestamp = "2023-10-01T12:00:00Z"
                    },
                    AccountId2 => new DataAccountBalance
                    {
                        Currency = "EUR",
                        Available = 500.00m,
                        Current = 600.00m,
                        Overdraft = 0.00m,
                        UpdateTimestamp = "2023-10-01T12:00:00Z"
                    },
                    _ => throw new ArgumentException("Invalid account ID", nameof(accountId))
                }
            ],
        };
        return new ApiResponse<GetAccountBalanceResponse>(response, HttpStatusCode.OK, "fake-trace-id");
    }

    private const string AccountId1 = "account-123";
    private const string AccountId2 = "account-456";
    private readonly DataAccount[] _accounts =
        [
            new()
            {
                UpdateTimestamp = "2023-10-01T12:00:00Z",
                AccountId = AccountId1,
                AccountType = "personal",
                DisplayName = "John Doe's Account",
                Currency = "GBP",
                AccountNumber = new DataAccountNumber
                {
                    Iban = "GB29NWBK60161331926819",
                    Number = "12345678",
                    SortCode = "601613",
                    SwiftBic = "NWBKGB2L"
                },
                Provider = new DataProvider
                {
                    ProviderId = "provider-123"
                }
            },
            new()
            {
                UpdateTimestamp = "2023-10-01T12:00:00Z",
                AccountId = AccountId2,
                AccountType = "savings",
                DisplayName = "Jane Doe's Savings Account",
                Currency = "EUR",
                AccountNumber = new DataAccountNumber
                {
                    Iban = "IE29AIBK93115212345678",
                    Number = "87654321",
                    SortCode = "931152",
                    SwiftBic = "AIBKIE2D"
                },
                Provider = new DataProvider
                {
                    ProviderId = "provider-456"
                }
            }
        ];
}
