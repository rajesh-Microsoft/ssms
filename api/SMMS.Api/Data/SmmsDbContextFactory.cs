using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SMMS.Api.Data;

/// <summary>
/// Used only by EF Core CLI tooling ("dotnet ef migrations add" / "dotnet ef database update")
/// to construct a DbContext outside of ASP.NET Core's request pipeline, where no tenant has been
/// resolved yet. Picks the first configured tenant's connection string purely for schema
/// generation — the resulting migrations apply identically to every tenant database at runtime,
/// since all tenants share the exact same schema.
/// </summary>
public class SmmsDbContextFactory : IDesignTimeDbContextFactory<SmmsDbContext>
{
    public SmmsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetSection("Tenants").GetChildren().FirstOrDefault()?["ConnectionString"]
            ?? throw new InvalidOperationException(
                "No tenant connection string found in configuration for design-time DbContext creation. " +
                "Ensure appsettings.json has at least one entry under \"Tenants\".");

        var optionsBuilder = new DbContextOptionsBuilder<SmmsDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new SmmsDbContext(optionsBuilder.Options);
    }
}
