namespace Eudoria.Core.Api.Database;

/// <summary>Connection settings of the core's PostgreSQL database (ADR-0002).</summary>
internal sealed class DatabaseOptions
{
    /// <summary>Name of the connection string: configuration key <c>ConnectionStrings:Core</c>
    /// (environment variable <c>ConnectionStrings__Core</c>).</summary>
    public const string ConnectionStringName = "Core";

    public string ConnectionString { get; set; } = string.Empty;
}
