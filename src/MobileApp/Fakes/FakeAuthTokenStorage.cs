using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MobileApp.Fakes;

public class FakeAuthTokenStorage : IAuthTokenStorage
{
    public OAuthToken[] LoadTokens() =>
        [
            new("Fake Bank 1", "fake-access-token", "fake-token-type", 3600, "0000000000000000000000000000000000000000000000000000000000000000"),
            new("Fake Bank 2", "fake-access-token", "fake-token-type", 3600, "0000000000000000000000000000000000000000000000000000000000000000"),
            new("Fake Bank 3", "fake-access-token", "fake-token-type", 3600, "0000000000000000000000000000000000000000000000000000000000000000"),
            new("Fake Bank 4", "fake-access-token", "fake-token-type", 3600, "0000000000000000000000000000000000000000000000000000000000000000"),
        ];

    public Task StoreTokens(OAuthToken[] tokens) => Task.CompletedTask;
    public Task<T?> Load<T>(string fileName) => Task.FromResult(default(T));
    public Task Store<T>(string fileName, T blob) => Task.CompletedTask;
    public Task ExportSettings(Stream outputStream) => Task.CompletedTask;
    public Task ImportSettings(Stream inputStream) => Task.CompletedTask;
    public IReadOnlyList<StorageFileSnapshot> InspectManagedFiles() => [];
}
