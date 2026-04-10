using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gamarr.Agent.Configuration;
using Gamarr.Agent.Models;
using Microsoft.Extensions.Options;

namespace Gamarr.Agent.Services;

public sealed class GamarrApiClient(IOptions<GamarrAgentOptions> options)
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri(options.Value.ServerBaseUrl) };
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<MachineResponse> RegisterAsync(RegisterMachineRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/machines/register", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MachineResponse>(_jsonOptions, cancellationToken))!;
    }

    public async Task HeartbeatAsync(Guid machineId, HeartbeatRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/machines/{machineId}/heartbeat", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<NextJobResponse?> GetNextJobAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/agents/{machineId}/next-job", new { }, _jsonOptions, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NextJobResponse>(_jsonOptions, cancellationToken);
    }

    public async Task<bool> ClaimJobAsync(Guid jobId, Guid machineId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/jobs/{jobId}/claim", new ClaimJobRequest(machineId), _jsonOptions, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task ReportEventAsync(Guid jobId, JobEventRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/jobs/{jobId}/events", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<NextMountResponse?> GetNextMountAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/api/machines/{machineId}/next-mount", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NextMountResponse>(_jsonOptions, cancellationToken);
    }

    public async Task<NextMountResponse?> GetNextDismountAsync(Guid machineId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"/api/machines/{machineId}/next-dismount", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NextMountResponse>(_jsonOptions, cancellationToken);
    }

    public async Task ReportMountResultAsync(Guid machineId, Guid mountId, ReportMountResultRequest request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/machines/{machineId}/mounts/{mountId}/result", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ConfirmDismountAsync(Guid machineId, Guid mountId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/machines/{machineId}/mounts/{mountId}/dismounted", new { }, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
