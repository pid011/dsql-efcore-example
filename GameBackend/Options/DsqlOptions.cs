namespace GameBackend.Options;

public sealed class DsqlOptions
{
    public const string SectionName = "Dsql";

    public string ClusterEndpoint { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public int TokenRefreshMinutes { get; set; } = 1;

    public int TokenExpiryMinutes { get; set; } = 15;

    public int TokenRefreshBufferMinutes { get; set; } = 2;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int ConnectionTimeoutSeconds { get; set; } = 15;

    public int ConnectionLifetimeSeconds { get; set; } = 3300;

    public int KeepAliveSeconds { get; set; } = 30;

    public int MaxPoolSize { get; set; } = 100;

    public int MinPoolSize { get; set; }
}
