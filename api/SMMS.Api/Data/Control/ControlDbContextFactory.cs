using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SMMS.Api.Data.Control;

/// <summary>Used only by EF Core CLI tooling ("dotnet ef ... --context ControlDbContext")
/// to build the control-plane DbContext at design time.</summary>
public class ControlDbContextFactory : IDesignTimeDbContextFactory<ControlDbContext>
{
    public ControlDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration["ControlPlane:ConnectionString"]
            ?? throw new InvalidOperationException(
                "ControlPlane:ConnectionString is not configured for design-time control DbContext creation.");

        var optionsBuilder = new DbContextOptionsBuilder<ControlDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ControlDbContext(optionsBuilder.Options);
    }
}
