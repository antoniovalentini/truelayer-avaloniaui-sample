using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;
using MobileApp.Models;

namespace MobileApp.ViewModels;

public partial class DebugStorageViewModel : ViewModelBase
{
    private readonly IAuthTokenStorage _storage;
    private static readonly JsonSerializerOptions PrettyPrint = new() { WriteIndented = true };

    public DebugStorageViewModel(IAuthTokenStorage storage)
    {
        _storage = storage;
        Refresh();
    }

    public ObservableCollection<StorageFileSnapshot> Files { get; } = [];

    [ObservableProperty] private string? _previewContent;

    [RelayCommand]
    private void Refresh()
    {
        Files.Clear();
        Files.AddRange(_storage.InspectManagedFiles());
    }

    [RelayCommand]
    private async Task PreviewAsync(StorageFileSnapshot file)
    {
        if (!file.Exists)
        {
            PreviewContent = "(file not found)";
            return;
        }

        PreviewContent = file.FileName switch
        {
            "settings.json" => BuildMaskedTokensPreview(),
            "beneficiaries.json" => await BuildBeneficiariesPreviewAsync(),
            _ => "(no preview available)",
        };
    }

    private string BuildMaskedTokensPreview()
    {
        var tokens = _storage.LoadTokens() ?? [];
        var masked = tokens.Select(t => new
        {
            t.ProviderId,
            AccessToken = DebugTokenFormatting.MaskSecret(t.AccessToken),
            RefreshToken = DebugTokenFormatting.MaskSecret(t.RefreshToken),
            t.TokenType,
            t.ExpiresIn,
            t.IssuedAt,
        }).ToList();
        return JsonSerializer.Serialize(masked, PrettyPrint);
    }

    private async Task<string> BuildBeneficiariesPreviewAsync()
    {
        var beneficiaries = await _storage.Load<List<BeneficiaryModel>>("beneficiaries.json") ?? [];
        return JsonSerializer.Serialize(beneficiaries, PrettyPrint);
    }
}
