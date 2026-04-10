using System.Text.Json;
using Gamarr.Application.Contracts;
using Gamarr.Application.Exceptions;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Entities;
using Gamarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gamarr.Infrastructure.Services;

public sealed class SettingsService(
    GamarrDbContext dbContext,
    IConfiguration configuration) : ISettingsService
{
    private const string MetadataKey = "metadata";
    private const string MediaKey = "media";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        await GetMetadataDocumentAsync(cancellationToken);
        await GetMediaDocumentAsync(cancellationToken);
    }

    public async Task<SettingsResponse> GetAsync(CancellationToken cancellationToken)
        => new(await GetMetadataAsync(cancellationToken), await GetMediaAsync(cancellationToken), await GetNetworkAsync(cancellationToken));

    public async Task<MetadataSettingsResponse> GetMetadataAsync(CancellationToken cancellationToken)
    {
        var document = await GetMetadataDocumentAsync(cancellationToken);
        return ToMetadataResponse(document);
    }

    public async Task<MediaManagementSettingsResponse> GetMediaAsync(CancellationToken cancellationToken)
    {
        var document = await GetMediaDocumentAsync(cancellationToken);
        return ToMediaResponse(document);
    }

    public Task<NetworkSettingsResponse> GetNetworkAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var machineName = Environment.MachineName;
        var apiListenUrls = configuration["Network:ApiListenUrls"]
            ?? configuration["ASPNETCORE_URLS"]
            ?? configuration["GamarrServer:ListenUrls"]
            ?? "http://localhost:5000";
        var webListenHost = configuration["Network:WebListenHost"]
            ?? configuration["GAMARR_WEB_HOST"]
            ?? "served-by-api";
        var publicServerUrl = configuration["Network:PublicServerUrl"]
            ?? configuration["GAMARR_PUBLIC_SERVER_URL"]
            ?? configuration["GamarrServer:PublicServerUrl"]
            ?? $"http://{machineName}:5000";
        var agentServerUrl = configuration["Network:AgentServerUrl"]
            ?? configuration["GAMARR_AGENT_SERVER_URL"]
            ?? configuration["GamarrServer:AgentServerUrl"]
            ?? publicServerUrl;

        if (string.IsNullOrWhiteSpace(publicServerUrl) || string.Equals(publicServerUrl, "http://", StringComparison.OrdinalIgnoreCase))
        {
            publicServerUrl = $"http://{machineName}:5000";
        }

        if (string.IsNullOrWhiteSpace(agentServerUrl) || string.Equals(agentServerUrl, "http://", StringComparison.OrdinalIgnoreCase))
        {
            agentServerUrl = publicServerUrl;
        }

        var lanEnabled = apiListenUrls.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                         apiListenUrls.Contains("[::]", StringComparison.OrdinalIgnoreCase) ||
                         (!publicServerUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) &&
                          !publicServerUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase));

        var summary = lanEnabled
            ? $"LAN mode enabled. Remote agents should use {agentServerUrl}."
            : "Local-only mode. API/web are still using loopback-style access.";

        return Task.FromResult(new NetworkSettingsResponse(
            publicServerUrl,
            agentServerUrl,
            apiListenUrls,
            webListenHost,
            lanEnabled,
            summary));
    }

    public async Task<MetadataSettingsRuntime> GetMetadataRuntimeAsync(CancellationToken cancellationToken)
    {
        var document = await GetMetadataDocumentAsync(cancellationToken);
        return new MetadataSettingsRuntime(
            document.PreferIgdb,
            document.IgdbEnabled,
            document.IgdbClientId,
            Decrypt(document.ProtectedIgdbClientSecret),
            document.UseSteamFallback,
            document.AutoImportThreshold,
            document.ReviewThreshold);
    }

    public async Task<MediaManagementSettingsRuntime> GetMediaRuntimeAsync(CancellationToken cancellationToken)
    {
        var document = await GetMediaDocumentAsync(cancellationToken);
        return new MediaManagementSettingsRuntime(
            document.DefaultLibraryRootPath,
            document.NormalizedAssetRootPath,
            document.AutoScanOnRootCreate,
            document.AutoNormalizeOnImport,
            document.AutoImportHighConfidenceMatches,
            document.IncludePatterns,
            document.ExcludePatterns);
    }

    public async Task<MetadataSettingsResponse> UpdateMetadataAsync(UpdateMetadataSettingsRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateMetadataSettings(request);

        var document = await GetMetadataDocumentAsync(cancellationToken);
        document.PreferIgdb = request.PreferIgdb;
        document.IgdbEnabled = request.IgdbEnabled;
        document.IgdbClientId = request.IgdbClientId?.Trim();
        document.UseSteamFallback = request.UseSteamFallback;
        document.AutoImportThreshold = request.AutoImportThreshold;
        document.ReviewThreshold = request.ReviewThreshold;

        if (request.ClearIgdbClientSecret)
        {
            document.ProtectedIgdbClientSecret = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.IgdbClientSecret))
        {
            document.ProtectedIgdbClientSecret = Encrypt(request.IgdbClientSecret.Trim());
        }

        await SaveAsync(MetadataKey, document, cancellationToken);
        return ToMetadataResponse(document);
    }

    public async Task<MediaManagementSettingsResponse> UpdateMediaAsync(UpdateMediaManagementSettingsRequest request, CancellationToken cancellationToken)
    {
        ValidationHelpers.ValidateMediaManagementSettings(request);

        var document = await GetMediaDocumentAsync(cancellationToken);
        document.DefaultLibraryRootPath = request.DefaultLibraryRootPath?.Trim();
        document.NormalizedAssetRootPath = request.NormalizedAssetRootPath?.Trim();
        document.AutoScanOnRootCreate = request.AutoScanOnRootCreate;
        document.AutoNormalizeOnImport = request.AutoNormalizeOnImport;
        document.AutoImportHighConfidenceMatches = request.AutoImportHighConfidenceMatches;
        document.IncludePatterns = NormalizePatterns(request.IncludePatterns);
        document.ExcludePatterns = NormalizePatterns(request.ExcludePatterns);

        await SaveAsync(MediaKey, document, cancellationToken);
        return ToMediaResponse(document);
    }

    private async Task<MetadataSettingsDocument> GetMetadataDocumentAsync(CancellationToken cancellationToken)
        => await GetOrCreateAsync(
            MetadataKey,
            BuildDefaultMetadataDocument(),
            cancellationToken);

    private async Task<MediaManagementSettingsDocument> GetMediaDocumentAsync(CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultMediaDocument();
        var document = await GetOrCreateAsync(
            MediaKey,
            defaults,
            cancellationToken);

        var updated = false;
        if (string.IsNullOrWhiteSpace(document.NormalizedAssetRootPath))
        {
            document.NormalizedAssetRootPath = defaults.NormalizedAssetRootPath;
            updated = true;
        }

        if (!document.AutoNormalizeOnImport)
        {
            document.AutoNormalizeOnImport = defaults.AutoNormalizeOnImport;
            updated = true;
        }

        if (updated)
        {
            await SaveAsync(MediaKey, document, cancellationToken);
        }

        return document;
    }

    private async Task<TDocument> GetOrCreateAsync<TDocument>(string key, TDocument defaultValue, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (entity is null)
        {
            entity = new SystemSetting
            {
                Key = key,
                JsonValue = JsonSerializer.Serialize(defaultValue, JsonOptions),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.SystemSettings.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return defaultValue;
        }

        var deserialized = JsonSerializer.Deserialize<TDocument>(entity.JsonValue, JsonOptions);
        if (deserialized is not null)
        {
            return deserialized;
        }

        entity.JsonValue = JsonSerializer.Serialize(defaultValue, JsonOptions);
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return defaultValue;
    }

    private async Task SaveAsync<TDocument>(string key, TDocument document, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SystemSettings.FirstAsync(x => x.Key == key, cancellationToken);
        entity.JsonValue = JsonSerializer.Serialize(document, JsonOptions);
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private MetadataSettingsDocument BuildDefaultMetadataDocument()
    {
        var preferIgdb = bool.TryParse(configuration["Metadata:PreferIgdb"], out var parsedPreferIgdb)
            ? parsedPreferIgdb
            : true;

        var clientId = configuration["Metadata:IgdbClientId"]?.Trim();
        var secret = configuration["Metadata:IgdbClientSecret"]?.Trim();

        return new MetadataSettingsDocument
        {
            PreferIgdb = preferIgdb,
            IgdbEnabled = true,
            IgdbClientId = clientId,
            ProtectedIgdbClientSecret = string.IsNullOrWhiteSpace(secret) ? null : Encrypt(secret),
            UseSteamFallback = true,
            AutoImportThreshold = 0.97d,
            ReviewThreshold = 0.72d
        };
    }

    private static MediaManagementSettingsDocument BuildDefaultMediaDocument() =>
        new()
        {
            DefaultLibraryRootPath = @"E:\Games",
            NormalizedAssetRootPath = @"E:\DEV\Spool\.normalized-assets",
            AutoScanOnRootCreate = false,
            AutoNormalizeOnImport = true,
            AutoImportHighConfidenceMatches = true,
            IncludePatterns = [],
            ExcludePatterns = []
        };

    private static MetadataSettingsResponse ToMetadataResponse(MetadataSettingsDocument document)
    {
        var hasSecret = !string.IsNullOrWhiteSpace(document.ProtectedIgdbClientSecret);
        var configured = document.IgdbEnabled &&
                         !string.IsNullOrWhiteSpace(document.IgdbClientId) &&
                         hasSecret;

        var providerStatus = !document.IgdbEnabled
            ? "IGDB disabled. Steam fallback remains available."
            : configured
                ? $"IGDB configured. Provider order: {(document.PreferIgdb ? "IGDB -> Steam -> Local" : "Steam -> IGDB -> Local")}."
                : "IGDB is enabled but incomplete. Add both client id and secret to use it.";

        return new MetadataSettingsResponse(
            document.PreferIgdb,
            document.IgdbEnabled,
            document.IgdbClientId,
            hasSecret,
            configured,
            document.UseSteamFallback,
            document.AutoImportThreshold,
            document.ReviewThreshold,
            providerStatus);
    }

    private static MediaManagementSettingsResponse ToMediaResponse(MediaManagementSettingsDocument document)
        => new(
            document.DefaultLibraryRootPath,
            document.NormalizedAssetRootPath,
            document.AutoScanOnRootCreate,
            document.AutoNormalizeOnImport,
            document.AutoImportHighConfidenceMatches,
            document.IncludePatterns,
            document.ExcludePatterns,
            "One-click install is limited to normalized portable folders, normalized MSI installs, and manually reviewed plans. All other sources stay Review Required.");

    private static string Encrypt(string value) => value;

    private static string? Decrypt(string? value) => value;

    private static string[] NormalizePatterns(IReadOnlyCollection<string> patterns)
        => patterns
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed class MetadataSettingsDocument
    {
        public bool PreferIgdb { get; set; } = true;
        public bool IgdbEnabled { get; set; } = true;
        public string? IgdbClientId { get; set; }
        public string? ProtectedIgdbClientSecret { get; set; }
        public bool UseSteamFallback { get; set; } = true;
        public double AutoImportThreshold { get; set; } = 0.97d;
        public double ReviewThreshold { get; set; } = 0.72d;
    }

    private sealed class MediaManagementSettingsDocument
    {
        public string? DefaultLibraryRootPath { get; set; }
        public string? NormalizedAssetRootPath { get; set; }
        public bool AutoScanOnRootCreate { get; set; }
        public bool AutoNormalizeOnImport { get; set; } = true;
        public bool AutoImportHighConfidenceMatches { get; set; } = true;
        public string[] IncludePatterns { get; set; } = [];
        public string[] ExcludePatterns { get; set; } = [];
    }
}
