namespace Gamarr.Domain.Entities;

public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;
    public string JsonValue { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
