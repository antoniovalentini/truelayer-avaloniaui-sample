using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MobileApp.Models;

namespace MobileApp;

public interface IAuthTokenStorage
{
    OAuthToken[]? LoadTokens();
    Task StoreTokens(OAuthToken[] token);
    Task<T?> Load<T>(string fileName);
    Task Store<T>(string fileName, T blob);
    Task ExportSettings(Stream outputStream);
    Task ImportSettings(Stream inputStream);
    IReadOnlyList<StorageFileSnapshot> InspectManagedFiles();
}

public class AuthTokenStorage(ILogger<AuthTokenStorage> logger) : IAuthTokenStorage
{
    // TODO: use platform specific paths
    private static string BasePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "TrueLayerMobile/");
    private static string SecretsFolderPath => Path.Combine(BasePath, "secrets/");

    private readonly JsonSerializerOptions _jsonSerializerOptions = new() { WriteIndented = true };

    public OAuthToken[]? LoadTokens()
    {
        try
        {
            var filePath = Path.Combine(SecretsFolderPath, "settings.json");
            logger.LogInformation("Getting token from {FilePath}", filePath);

            if (!File.Exists(filePath)) return null;

            var data = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<OAuthToken[]>(data);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to read token from storage: {Message}", e.Message);
            return null;
        }
    }

    public async Task StoreTokens(OAuthToken[] token)
    {
        try
        {
            var filePath = Path.Combine(SecretsFolderPath, "settings.json");
            logger.LogInformation("Storing token to {FolderPath}", filePath);
            var serialized = JsonSerializer.Serialize(token, _jsonSerializerOptions);

            if (!string.IsNullOrWhiteSpace(SecretsFolderPath))
                Directory.CreateDirectory(SecretsFolderPath);

            await File.WriteAllTextAsync(filePath, serialized).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to store new token: {Message}", e.Message);
        }
    }

    public async Task<T?> Load<T>(string fileName)
    {
        try
        {
            var filePath = Path.Combine(BasePath, fileName);
            logger.LogInformation("Loading blob from {FolderPath}", filePath);

            // TODO: this is to avoid an exception if the beneficiaries files was never created, find a better way to do it
            if (fileName == "beneficiaries.json" && !File.Exists(filePath))
            {
                await File.WriteAllTextAsync(filePath, "[]").ConfigureAwait(false);
                logger.LogInformation("Created empty beneficiaries file at {FolderPath}", filePath);
            }

            var data = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(data, _jsonSerializerOptions);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to load blob: {Message}", e.Message);
            return default;
        }
    }

    public async Task Store<T>(string fileName, T blob)
    {
        try
        {
            var filePath = Path.Combine(BasePath, fileName);
            logger.LogInformation("Storing blob to {FolderPath}", filePath);
            var serialized = JsonSerializer.Serialize(blob, _jsonSerializerOptions);
            await File.WriteAllTextAsync(filePath, serialized).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to store blob: {Message}", e.Message);
        }
    }

    public async Task ExportSettings(Stream outputStream)
    {
        var tokens = LoadTokens() ?? [];
        var beneficiaries = await Load<List<BeneficiaryModel>>("beneficiaries.json") ?? [];
        var backup = new SettingsBackup(1, tokens, beneficiaries);
        await JsonSerializer.SerializeAsync(outputStream, backup, _jsonSerializerOptions);
    }

    public async Task ImportSettings(Stream inputStream)
    {
        var backup = await JsonSerializer.DeserializeAsync<SettingsBackup>(inputStream, _jsonSerializerOptions)
            ?? throw new InvalidDataException("Invalid backup file");

        // TODO:
        // either surface exceptions from Store/StoreTokens so the caller can react,
        // or write both to a temporary location and rename atomically,
        // or at minimum check return state and set a distinct error StatusMessage.
        await StoreTokens(backup.Tokens);
        await Store("beneficiaries.json", backup.Beneficiaries);
    }

    public IReadOnlyList<StorageFileSnapshot> InspectManagedFiles()
    {
        return
        [
            Inspect("settings.json", Path.Combine(SecretsFolderPath, "settings.json")),
            Inspect("beneficiaries.json", Path.Combine(BasePath, "beneficiaries.json")),
        ];

        static StorageFileSnapshot Inspect(string fileName, string fullPath)
        {
            var info = new FileInfo(fullPath);
            return info.Exists
                ? new StorageFileSnapshot(fileName, fullPath, true, info.Length, info.LastWriteTimeUtc)
                : new StorageFileSnapshot(fileName, fullPath, false, 0, null);
        }
    }

    private record SettingsBackup(int Version, OAuthToken[] Tokens, List<BeneficiaryModel> Beneficiaries);
}

public record OAuthToken(string ProviderId, string AccessToken, string TokenType, long ExpiresIn, string RefreshToken, DateTimeOffset IssuedAt = default);

public sealed record StorageFileSnapshot(string FileName, string FullPath, bool Exists, long SizeBytes, DateTimeOffset? LastModifiedUtc);
