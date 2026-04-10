using Gamarr.Application.Interfaces;
using Gamarr.Application.Services;
using Gamarr.Infrastructure.Persistence;
using Gamarr.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gamarr.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGamarrInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Gamarr")
            ?? configuration["Database:ConnectionString"]
            ?? configuration["GAMARR_POSTGRES_CONNECTION"]
            ?? "Host=localhost;Port=5432;Database=gamarr;Username=gamarr;Password=gamarr";

        services.AddDbContext<GamarrDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IPackageService, PackageService>();
        services.AddScoped<IMachineService, MachineService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<ILibraryService, LibraryService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<ISystemService, SystemService>();
        services.AddScoped<INormalizationService, NormalizationService>();
        services.AddScoped<ILibraryMetadataProvider>(serviceProvider =>
            new LibraryMetadataProvider(
                new HttpClient(),
                serviceProvider.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IJobDispatchPublisher, RabbitMqJobDispatchPublisher>();
        services.AddHostedService<NormalizationWorker>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<QuickInstallService>();
        services.AddScoped<MountCommandService>();
        services.AddScoped<MachinePrerequisiteService>();
        return services;
    }
}
