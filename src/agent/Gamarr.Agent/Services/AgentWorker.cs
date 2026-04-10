using Gamarr.Agent.Configuration;
using Gamarr.Agent.Models;
using Microsoft.Extensions.Options;

namespace Gamarr.Agent.Services;

public sealed class AgentWorker(
    MachineIdentityStore identityStore,
    GamarrApiClient apiClient,
    IPackageJobExecutor jobExecutor,
    IOptions<GamarrAgentOptions> options,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = await identityStore.GetOrCreateAsync(stoppingToken);
        var machineId = await EnsureRegisteredAsync(identity, stoppingToken);

        var heartbeatInterval = TimeSpan.FromSeconds(options.Value.HeartbeatIntervalSeconds);
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        var lastHeartbeat = DateTimeOffset.MinValue;
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow - lastHeartbeat >= heartbeatInterval)
                {
                    await apiClient.HeartbeatAsync(machineId, BuildHeartbeatRequest(), stoppingToken);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                var nextJob = await apiClient.GetNextJobAsync(machineId, stoppingToken);
                if (nextJob is not null)
                {
                    logger.LogInformation("Discovered job {JobId} for package {PackageName}", nextJob.Id, nextJob.PackageName);
                    var claimed = await apiClient.ClaimJobAsync(nextJob.Id, machineId, stoppingToken);
                    if (claimed)
                    {
                        await ExecuteClaimedJobAsync(machineId, nextJob, stoppingToken);
                        lastHeartbeat = DateTimeOffset.MinValue;
                    }
                }

                await ExecutePendingMountsAsync(machineId, stoppingToken);

                consecutiveFailures = 0;
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                logger.LogWarning(ex, "Agent loop failure {FailureCount}. Retrying.", consecutiveFailures);
                var delay = TimeSpan.FromSeconds(Math.Min(30, 3 * consecutiveFailures));
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task ExecutePendingMountsAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var pendingMount = await apiClient.GetNextMountAsync(machineId, cancellationToken);
        if (pendingMount is not null)
        {
            logger.LogInformation("Executing standalone mount for {ImagePath}", pendingMount.IsoPath);
            try
            {
                var driveLetter = await MockRecipeExecutor.MountStandaloneImageAsync(pendingMount.IsoPath, cancellationToken);
                await apiClient.ReportMountResultAsync(machineId, pendingMount.MountId,
                    new ReportMountResultRequest(driveLetter, null), cancellationToken);
                logger.LogInformation("Image mounted at {DriveLetter}", driveLetter);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Standalone mount failed for {ImagePath}", pendingMount.IsoPath);
                await apiClient.ReportMountResultAsync(machineId, pendingMount.MountId,
                    new ReportMountResultRequest(null, ex.Message), cancellationToken);
            }
        }

        var pendingDismount = await apiClient.GetNextDismountAsync(machineId, cancellationToken);
        if (pendingDismount is not null)
        {
            logger.LogInformation("Executing standalone dismount for {ImagePath}", pendingDismount.IsoPath);
            try
            {
                await MockRecipeExecutor.DismountStandaloneImageAsync(pendingDismount.IsoPath, cancellationToken);
                await apiClient.ConfirmDismountAsync(machineId, pendingDismount.MountId, cancellationToken);
                logger.LogInformation("Image dismounted: {ImagePath}", pendingDismount.IsoPath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Standalone dismount failed for {ImagePath}", pendingDismount.IsoPath);
            }
        }
    }

    private async Task<Guid> EnsureRegisteredAsync(MachineIdentity identity, CancellationToken cancellationToken)
    {
        var response = await apiClient.RegisterAsync(new RegisterMachineRequest(
            identity.StableKey,
            identity.Name,
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            Environment.Is64BitOperatingSystem ? ArchitectureKind.X64 : ArchitectureKind.X86,
            "0.1.0",
            BuildCapabilities()), cancellationToken);

        await identityStore.SaveMachineIdAsync(identity, response.Id, cancellationToken);
        return response.Id;
    }

    private async Task ExecuteClaimedJobAsync(Guid machineId, NextJobResponse nextJob, CancellationToken stoppingToken)
    {
        var lastSequence = 2;

        try
        {
            await jobExecutor.ExecuteAsync(
                machineId,
                nextJob,
                async request =>
                {
                    await apiClient.ReportEventAsync(nextJob.Id, request, stoppingToken);
                    lastSequence = request.SequenceNumber;
                },
                stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed during execution.", nextJob.Id);
            await apiClient.ReportEventAsync(
                nextJob.Id,
                new JobEventRequest(
                    lastSequence + 1,
                    JobState.Failed,
                    $"Execution failed: {ex.Message}",
                    [new JobLogRequest(LogLevelKind.Error, "agent", ex.ToString(), null)]),
                stoppingToken);
        }
    }

    private static HeartbeatRequest BuildHeartbeatRequest() =>
        new(
            MachineStatus.Online,
            "0.1.0",
            BuildCapabilities());

    private static string[] BuildCapabilities()
    {
        var capabilities = new List<string>
        {
            "iso-mount",
            "powershell-execution",
            "validation"
        };

        if (MockRecipeExecutor.FindWinCDEmuExecutable() is not null)
        {
            capabilities.Add("wincdemu");
        }

        if (MockRecipeExecutor.FindSevenZipExecutable() is not null)
        {
            capabilities.Add("7zip");
        }

        return capabilities.ToArray();
    }
}
