using Testcontainers.PostgreSql;

namespace Eudoria.Core.Api.Tests.Infrastructure;

/// <summary>
/// A disposable PostgreSQL server in Docker, created once for every test of the
/// <see cref="PostgresCollectionDefinition"/> collection and destroyed afterwards (ADR-0002).
/// Requires Docker Desktop to be running.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same exact version as tools/dev/docker-compose.yml.
    private const string Image = "postgres:18.7";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                "Could not start the test PostgreSQL container. Is Docker Desktop running?", exception);
        }
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
