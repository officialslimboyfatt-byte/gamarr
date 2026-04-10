namespace Gamarr.Infrastructure.Services;

public sealed class MetadataProviderOptions
{
    public bool PreferIgdb { get; set; } = true;
    public string? IgdbClientId { get; set; }
    public string? IgdbClientSecret { get; set; }
}
