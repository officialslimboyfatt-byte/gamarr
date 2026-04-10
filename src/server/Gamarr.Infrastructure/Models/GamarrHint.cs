namespace Gamarr.Infrastructure.Models;

public sealed class GamarrHint
{
    public string? Name { get; init; }
    public string? InstallerPath { get; init; }
    public string? InstallerFamily { get; init; }
    public string? SilentArgs { get; init; }
    public string? LaunchPath { get; init; }
    public string? UninstallPath { get; init; }
    public string? UninstallArgs { get; init; }
    public GamarrHintDetection? InstallDetection { get; init; }
}

public sealed class GamarrHintDetection
{
    public string Type { get; init; } = "FileExists";
    public string Value { get; init; } = string.Empty;
}
