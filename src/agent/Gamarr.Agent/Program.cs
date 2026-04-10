using System.Runtime.Versioning;
using Gamarr.Agent.Configuration;
using Gamarr.Agent.Services;

[assembly: SupportedOSPlatform("windows")]

return ProgramEntry.Run(args);

static class ProgramEntry
{
    [SupportedOSPlatform("windows")]
    public static int Run(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.Configure<GamarrAgentOptions>(builder.Configuration.GetSection("Gamarr"));
        builder.Services.AddSingleton<MachineIdentityStore>();
        builder.Services.AddSingleton<GamarrApiClient>();
        builder.Services.AddSingleton<IPackageJobExecutor, MockRecipeExecutor>();
        builder.Services.AddHostedService<AgentWorker>();

        if (!builder.Configuration.GetValue("Gamarr:RunAsConsole", true))
        {
            builder.Services.AddWindowsService(options => options.ServiceName = "Gamarr Agent");
        }

        var host = builder.Build();
        host.Run();
        return 0;
    }
}
