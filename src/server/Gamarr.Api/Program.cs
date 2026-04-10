using System.Text.Json.Serialization;
using Gamarr.Api;
using Gamarr.Api.Configuration;
using Gamarr.Application.Interfaces;
using Gamarr.Domain.Enums;
using Gamarr.Infrastructure;
using Gamarr.Infrastructure.Persistence;
using Gamarr.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<GamarrServerOptions>(builder.Configuration.GetSection("GamarrServer"));

var serverOptions = new GamarrServerOptions();
builder.Configuration.GetSection("GamarrServer").Bind(serverOptions);
serverOptions.ListenUrls = builder.Configuration["ASPNETCORE_URLS"] ?? builder.Configuration["GamarrServer:ListenUrls"] ?? serverOptions.ListenUrls;
serverOptions.PublicServerUrl = builder.Configuration["GAMARR_PUBLIC_SERVER_URL"] ?? builder.Configuration["GamarrServer:PublicServerUrl"] ?? serverOptions.PublicServerUrl;
serverOptions.AgentServerUrl = builder.Configuration["GAMARR_AGENT_SERVER_URL"] ?? builder.Configuration["GamarrServer:AgentServerUrl"] ?? serverOptions.AgentServerUrl;

if (!builder.Configuration.GetValue("GamarrServer:RunAsConsole", serverOptions.RunAsConsole))
{
    builder.Host.UseWindowsService(options => options.ServiceName = "Gamarr Server");
}

builder.WebHost.UseUrls(serverOptions.ListenUrls);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddGamarrInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ProblemDetailsMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var configuration = services.GetRequiredService<IConfiguration>();
    var dbContext = services.GetRequiredService<GamarrDbContext>();
    var applyMigrations = bool.TryParse(configuration["Database:ApplyMigrationsOnStartup"], out var parsed) && parsed;
    if (applyMigrations)
    {
        await dbContext.Database.MigrateAsync();
    }

    var seeder = services.GetRequiredService<DemoDataSeeder>();
    var settingsService = services.GetRequiredService<ISettingsService>();
    await settingsService.EnsureDefaultsAsync(CancellationToken.None);
    await seeder.SeedAsync(CancellationToken.None);

    // Reset any scans that were left Running due to a previous server restart.
    var stuckScans = await dbContext.LibraryScans
        .Include(s => s.LibraryRoot)
        .Where(s => s.State == LibraryScanState.Running)
        .ToListAsync();
    if (stuckScans.Count > 0)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var stuck in stuckScans)
        {
            stuck.State = LibraryScanState.Failed;
            stuck.CompletedAtUtc = now;
            stuck.ErrorMessage = "Scan interrupted by server restart.";
            stuck.Summary = "Scan did not complete — server was restarted during execution.";

            var root = stuck.LibraryRoot;
            if (root is not null && root.LastScanState == LibraryScanState.Running)
            {
                root.LastScanState = LibraryScanState.Failed;
                root.LastScanCompletedAtUtc = now;
                root.LastScanError = "Scan interrupted by server restart.";
                root.UpdatedAtUtc = now;
            }
        }
        await dbContext.SaveChangesAsync();
    }
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
var webDistPath = ResolveWebUiPath();
var hasWebUi = webDistPath is not null && File.Exists(Path.Combine(webDistPath, "index.html"));

app.MapGet("/health/ready", async (GamarrDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect && hasWebUi
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(!canConnect ? "Database unavailable." : "Web UI assets are missing. Build the web app before starting the server.");
});

app.MapControllers();

if (webDistPath is not null)
{
    var fileProvider = new PhysicalFileProvider(webDistPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fileProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fileProvider
    });
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(webDistPath, "index.html"));
    });
}
else
{
    app.Logger.LogWarning("Web UI assets were not found. The API will run without serving the frontend.");
}

app.Run();

static string? ResolveWebUiPath()
{
    var publishedWwwRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    if (Directory.Exists(publishedWwwRoot) && File.Exists(Path.Combine(publishedWwwRoot, "index.html")))
    {
        return publishedWwwRoot;
    }

    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "web", "dist");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }
    }

    return null;
}
