namespace Gamarr.Agent.Configuration;

public sealed class GamarrAgentOptions
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5000";
    public int HeartbeatIntervalSeconds { get; set; } = 15;
    public int PollIntervalSeconds { get; set; } = 10;
    public bool RunAsConsole { get; set; } = true;
}
