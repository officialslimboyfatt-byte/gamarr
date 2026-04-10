namespace Gamarr.Api.Configuration;

public sealed class GamarrServerOptions
{
    public bool RunAsConsole { get; set; } = true;
    public string ListenUrls { get; set; } = "http://localhost:5000";
    public string PublicServerUrl { get; set; } = "http://localhost:5000";
    public string AgentServerUrl { get; set; } = "http://localhost:5000";
}
