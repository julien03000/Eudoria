using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Eudoria.Core.Identity.Persistence;

/// <summary>
/// Used by the dotnet-ef tool only (tools/dev/migrate.ps1 and "migrations add"). The connection
/// string comes from the ConnectionStrings__Core environment variable, set in memory by the script;
/// "migrations add" does not connect, so a placeholder is enough there.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? "Host=127.0.0.1";
        DbContextOptions<IdentityDbContext> options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, IdentityModule.ConfigureNpgsql)
            .Options;
        return new IdentityDbContext(options);
    }
}
