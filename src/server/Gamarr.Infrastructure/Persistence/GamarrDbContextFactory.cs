using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gamarr.Infrastructure.Persistence;

public sealed class GamarrDbContextFactory : IDesignTimeDbContextFactory<GamarrDbContext>
{
    public GamarrDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<GamarrDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("GAMARR_POSTGRES_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamarr;Username=gamarr;Password=gamarr";
        optionsBuilder.UseNpgsql(connectionString);
        return new GamarrDbContext(optionsBuilder.Options);
    }
}
