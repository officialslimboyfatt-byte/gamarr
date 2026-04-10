using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gamarr.Application.Contracts;
using Gamarr.Application.Interfaces;

namespace Gamarr.Infrastructure.Services;

public interface ILibraryMetadataProvider
{
    Task<MetadataSearchResult> SearchMatchesAsync(string title, CancellationToken cancellationToken);
}

public sealed class LibraryMetadataProvider(
    HttpClient httpClient,
    ISettingsService settingsService) : ILibraryMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static string? _igdbAccessToken;
    private static DateTimeOffset _igdbAccessTokenExpiryUtc = DateTimeOffset.MinValue;
    private static readonly SemaphoreSlim IgdbTokenLock = new(1, 1);

    public async Task<MetadataSearchResult> SearchMatchesAsync(string title, CancellationToken cancellationToken)
    {
        var trimmedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle))
        {
            return new MetadataSearchResult(Array.Empty<LibraryMetadataMatch>(), Array.Empty<MetadataProviderSearchDiagnostic>());
        }

        var settings = await settingsService.GetMetadataRuntimeAsync(cancellationToken);

        var igdbEnabled = settings.IgdbEnabled &&
                          !string.IsNullOrWhiteSpace(settings.IgdbClientId) &&
                          !string.IsNullOrWhiteSpace(settings.IgdbClientSecret);

        var providerResults = new List<MetadataProviderSearchResult>();

        if (settings.PreferIgdb && igdbEnabled)
        {
            providerResults.Add(await SearchIgdbAsync(trimmedTitle, settings, cancellationToken));
            if (settings.UseSteamFallback)
            {
                providerResults.Add(await SearchSteamAsync(trimmedTitle, cancellationToken));
            }
        }
        else
        {
            if (settings.UseSteamFallback)
            {
                providerResults.Add(await SearchSteamAsync(trimmedTitle, cancellationToken));
            }

            if (igdbEnabled)
            {
                providerResults.Add(await SearchIgdbAsync(trimmedTitle, settings, cancellationToken));
            }
        }

        var matches = providerResults
            .SelectMany(result => result.Matches)
            .Where(match => !string.IsNullOrWhiteSpace(match.Title))
            .GroupBy(match => $"{match.Provider}:{match.Key}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(15)
            .ToArray();

        var diagnostics = providerResults
            .Select(result => new MetadataProviderSearchDiagnostic(
                result.Provider,
                result.Status,
                result.Matches.Count,
                result.Summary,
                result.Matches.Select(match => match.Title).Take(5).ToArray()))
            .ToArray();

        return new MetadataSearchResult(matches, diagnostics);
    }

    private async Task<MetadataProviderSearchResult> SearchSteamAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var encodedTitle = Uri.EscapeDataString(title);
            var search = await httpClient.GetFromJsonAsync<List<SteamSearchApp>>(
                $"https://steamcommunity.com/actions/SearchApps/{encodedTitle}",
                cancellationToken);

            var apps = search?
                .Where(x => x.AppId > 0 && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.AppId)
                .Select(x => x.First())
                .Take(5)
                .ToArray();

            if (apps is null || apps.Length == 0)
            {
                return new MetadataProviderSearchResult("Steam", "NoResults", Array.Empty<LibraryMetadataMatch>(), "Steam returned no search results.");
            }

            var matches = new List<LibraryMetadataMatch>();
            foreach (var app in apps)
            {
                var details = await httpClient.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={app.AppId}",
                    cancellationToken);

                using var document = JsonDocument.Parse(details);
                if (!document.RootElement.TryGetProperty(app.AppId.ToString(CultureInfo.InvariantCulture), out var appNode) ||
                    !appNode.TryGetProperty("success", out var successNode) ||
                    !successNode.GetBoolean() ||
                    !appNode.TryGetProperty("data", out var dataNode))
                {
                    continue;
                }

                var developers = dataNode.TryGetProperty("developers", out var developerNode) && developerNode.ValueKind == JsonValueKind.Array
                    ? developerNode.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
                    : Array.Empty<string>();

                var genres = dataNode.TryGetProperty("genres", out var genresNode) && genresNode.ValueKind == JsonValueKind.Array
                    ? genresNode.EnumerateArray()
                        .Select(x => x.TryGetProperty("description", out var descriptionNode) ? descriptionNode.GetString() : null)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Cast<string>()
                        .ToArray()
                    : Array.Empty<string>();

                var releaseYear = TryParseSteamReleaseYear(dataNode);
                matches.Add(new LibraryMetadataMatch(
                    "Steam",
                    app.AppId.ToString(CultureInfo.InvariantCulture),
                    dataNode.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? app.Name : app.Name,
                    DecodeStoreText(dataNode.TryGetProperty("short_description", out var descriptionNode) ? descriptionNode.GetString() ?? string.Empty : string.Empty),
                    developers.FirstOrDefault() ?? string.Empty,
                    releaseYear,
                    dataNode.TryGetProperty("header_image", out var coverNode) ? coverNode.GetString() : null,
                    dataNode.TryGetProperty("background_raw", out var backdropNode) ? backdropNode.GetString() : null,
                    genres,
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    $"https://store.steampowered.com/app/{app.AppId}/"));
            }

            return new MetadataProviderSearchResult(
                "Steam",
                matches.Count == 0 ? "NoResults" : "Success",
                matches,
                matches.Count == 0 ? "Steam search results did not yield usable store metadata." : $"Steam returned {matches.Count} candidate(s).");
        }
        catch (Exception exception)
        {
            return new MetadataProviderSearchResult("Steam", "Error", Array.Empty<LibraryMetadataMatch>(), $"Steam metadata lookup failed: {SummarizeException(exception)}");
        }
    }

    private async Task<MetadataProviderSearchResult> SearchIgdbAsync(string title, MetadataSettingsRuntime settings, CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetIgdbAccessTokenAsync(settings, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new MetadataProviderSearchResult("IGDB", "Error", Array.Empty<LibraryMetadataMatch>(), "IGDB authentication did not return an access token.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", settings.IgdbClientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                $"search \"{EscapeIgdbString(title)}\"; fields name,summary,first_release_date,version_parent,cover.image_id,genres.name,themes.name,platforms.name,screenshots.image_id,artworks.image_id,involved_companies.developer,involved_companies.company.name; where version_parent = null; limit 10;",
                Encoding.UTF8,
                "text/plain");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var games = await JsonSerializer.DeserializeAsync<List<IgdbGame>>(contentStream, JsonOptions, cancellationToken) ?? [];
            var matches = games
                .Where(game => !string.IsNullOrWhiteSpace(game.Name))
                .Select(game => new LibraryMetadataMatch(
                    "IGDB",
                    game.Id.ToString(CultureInfo.InvariantCulture),
                    game.Name ?? string.Empty,
                    DecodeStoreText(game.Summary ?? string.Empty),
                    ResolveIgdbStudio(game),
                    TryParseIgdbReleaseYear(game.FirstReleaseDate),
                    BuildIgdbImageUrl(game.Cover?.ImageId, "t_cover_big"),
                    BuildIgdbImageUrl(game.Artworks?.FirstOrDefault()?.ImageId ?? game.Screenshots?.FirstOrDefault()?.ImageId, "t_1080p"),
                    game.Genres?.Select(genre => genre.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray() ?? Array.Empty<string>(),
                    game.Themes?.Select(theme => theme.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray() ?? Array.Empty<string>(),
                    game.Platforms?.Select(platform => platform.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray() ?? Array.Empty<string>(),
                    game.Screenshots?.Select(screenshot => BuildIgdbImageUrl(screenshot.ImageId, "t_screenshot_big")).Where(url => !string.IsNullOrWhiteSpace(url)).Cast<string>().ToArray() ?? Array.Empty<string>(),
                    $"https://www.igdb.com/games/{SlugifyIgdbName(game.Name)}"))
                .ToArray();

            return new MetadataProviderSearchResult(
                "IGDB",
                matches.Length == 0 ? "NoResults" : "Success",
                matches,
                matches.Length == 0 ? "IGDB returned no usable search results." : $"IGDB returned {matches.Length} candidate(s) with edition filtering enabled.");
        }
        catch (Exception exception)
        {
            return new MetadataProviderSearchResult("IGDB", "Error", Array.Empty<LibraryMetadataMatch>(), $"IGDB metadata lookup failed: {SummarizeException(exception)}");
        }
    }

    private async Task<string?> GetIgdbAccessTokenAsync(MetadataSettingsRuntime settings, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _igdbAccessTokenExpiryUtc && !string.IsNullOrWhiteSpace(_igdbAccessToken))
        {
            return _igdbAccessToken;
        }

        await IgdbTokenLock.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow < _igdbAccessTokenExpiryUtc && !string.IsNullOrWhiteSpace(_igdbAccessToken))
            {
                return _igdbAccessToken;
            }

            using var response = await httpClient.PostAsync(
                $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(settings.IgdbClientId ?? string.Empty)}&client_secret={Uri.EscapeDataString(settings.IgdbClientSecret ?? string.Empty)}&grant_type=client_credentials",
                content: null,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<TwitchTokenResponse>(contentStream, JsonOptions, cancellationToken);
            _igdbAccessToken = payload?.AccessToken;
            _igdbAccessTokenExpiryUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, (payload?.ExpiresIn ?? 300) - 60));
            return _igdbAccessToken;
        }
        catch (Exception exception)
        {
            _igdbAccessToken = null;
            _igdbAccessTokenExpiryUtc = DateTimeOffset.MinValue;
            _ = exception;
            return null;
        }
        finally
        {
            IgdbTokenLock.Release();
        }
    }

    private static int? TryParseSteamReleaseYear(JsonElement dataNode)
    {
        if (!dataNode.TryGetProperty("release_date", out var releaseDateNode) ||
            !releaseDateNode.TryGetProperty("date", out var dateNode))
        {
            return null;
        }

        var raw = dateNode.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var yearText = new string(raw.Where(char.IsDigit).TakeLast(4).ToArray());
        return int.TryParse(yearText, out var year) ? year : null;
    }

    private static int? TryParseIgdbReleaseYear(long? unixSeconds) =>
        unixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).UtcDateTime.Year
            : null;

    private static string ResolveIgdbStudio(IgdbGame game) =>
        game.InvolvedCompanies?
            .FirstOrDefault(company => company.Developer && !string.IsNullOrWhiteSpace(company.Company?.Name))
            ?.Company?.Name
        ?? game.InvolvedCompanies?
            .FirstOrDefault(company => !string.IsNullOrWhiteSpace(company.Company?.Name))
            ?.Company?.Name
        ?? string.Empty;

    private static string? BuildIgdbImageUrl(string? imageId, string size)
    {
        if (string.IsNullOrWhiteSpace(imageId))
        {
            return null;
        }

        return $"https://images.igdb.com/igdb/image/upload/{size}/{imageId}.jpg";
    }

    private static string EscapeIgdbString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string DecodeStoreText(string value) => WebUtility.HtmlDecode(value).Trim();

    private static string SlugifyIgdbName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    private static string SummarizeException(Exception exception)
    {
        var message = exception.Message;
        if (exception.InnerException is not null && !string.IsNullOrWhiteSpace(exception.InnerException.Message))
        {
            message = $"{message} | {exception.InnerException.Message}";
        }

        return message.Length <= 240 ? message : message[..240];
    }

    private sealed record SteamSearchApp(int AppId, string Name);

    private sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }

    private sealed class IgdbGame
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; init; }

        [JsonPropertyName("cover")]
        public IgdbImage? Cover { get; init; }

        [JsonPropertyName("genres")]
        public IReadOnlyCollection<IgdbGenre>? Genres { get; init; }

        [JsonPropertyName("themes")]
        public IReadOnlyCollection<IgdbTheme>? Themes { get; init; }

        [JsonPropertyName("platforms")]
        public IReadOnlyCollection<IgdbPlatform>? Platforms { get; init; }

        [JsonPropertyName("screenshots")]
        public IReadOnlyCollection<IgdbImage>? Screenshots { get; init; }

        [JsonPropertyName("artworks")]
        public IReadOnlyCollection<IgdbImage>? Artworks { get; init; }

        [JsonPropertyName("involved_companies")]
        public IReadOnlyCollection<IgdbInvolvedCompany>? InvolvedCompanies { get; init; }
    }

    private sealed class IgdbImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("image_id")]
        public string? ImageId { get; init; }
    }

    private sealed class IgdbGenre
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class IgdbTheme
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class IgdbPlatform
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private sealed class IgdbInvolvedCompany
    {
        [JsonPropertyName("developer")]
        public bool Developer { get; init; }

        [JsonPropertyName("company")]
        public IgdbCompany? Company { get; init; }
    }

    private sealed class IgdbCompany
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }
}

public sealed record LibraryMetadataMatch(
    string Provider,
    string Key,
    string Title,
    string Description,
    string Studio,
    int? ReleaseYear,
    string? CoverImagePath,
    string? BackdropImagePath,
    IReadOnlyCollection<string> Genres,
    IReadOnlyCollection<string> Themes,
    IReadOnlyCollection<string> Platforms,
    IReadOnlyCollection<string> ScreenshotImageUrls,
    string? SourceUrl);

public sealed record MetadataSearchResult(
    IReadOnlyCollection<LibraryMetadataMatch> Matches,
    IReadOnlyCollection<MetadataProviderSearchDiagnostic> Diagnostics);

public sealed record MetadataProviderSearchDiagnostic(
    string Provider,
    string Status,
    int CandidateCount,
    string Summary,
    IReadOnlyCollection<string> TopTitles);

internal sealed record MetadataProviderSearchResult(
    string Provider,
    string Status,
    IReadOnlyCollection<LibraryMetadataMatch> Matches,
    string Summary);
