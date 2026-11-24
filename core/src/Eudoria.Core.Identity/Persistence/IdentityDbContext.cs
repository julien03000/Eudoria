using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Tokens;
using Microsoft.EntityFrameworkCore;

namespace Eudoria.Core.Identity.Persistence;

/// <summary>Database of the Identity module: PostgreSQL schema "identity", owned by this module only.</summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
