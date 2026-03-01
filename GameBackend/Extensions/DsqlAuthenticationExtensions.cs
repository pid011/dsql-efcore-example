using Amazon;
using GameBackend.Infrastructure;
using GameBackend.Options;
using Npgsql;

namespace GameBackend.Extensions;

public static class DsqlAuthenticationExtensions
{
    public static void AddDsqlNpgsqlDataSource(this IHostApplicationBuilder builder, string connectionName)
    {
        var dsqlOptions = builder.Configuration.GetSection(DsqlOptions.SectionName).Get<DsqlOptions>() ?? new DsqlOptions();

        // AWS 서명 시 클라이언트 시간이 서버 시간보다 빠를 경우를 대비해 시간을 1분 늦춥니다.
        AWSConfigs.ManualClockCorrection = TimeSpan.FromMinutes(-1);

        string? clusterEndpoint = builder.Configuration["GAMEBACKEND_DSQL_CLUSTER_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            var connectionString = builder.Configuration.GetConnectionString(connectionName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                clusterEndpoint = new NpgsqlConnectionStringBuilder(connectionString).Host;
            }
        }

        var isDsqlEndpoint = !string.IsNullOrWhiteSpace(clusterEndpoint) &&
                             clusterEndpoint.Contains("dsql", StringComparison.OrdinalIgnoreCase);
        var tokenProvider = isDsqlEndpoint
            ? new DsqlAuthTokenProvider(clusterEndpoint!, dsqlOptions)
            : null;

        builder.AddNpgsqlDataSource(connectionName,
            settings =>
            {
                settings.DisableHealthChecks = isDsqlEndpoint;

                if (!isDsqlEndpoint)
                {
                    return;
                }

                // DSQL + IAM password provider 조합에서는 Password를 직접 주입하지 않습니다.
                var connectionBuilder = new NpgsqlConnectionStringBuilder
                {
                    Host = clusterEndpoint,
                    Database = "postgres",
                    Username = "admin",
                    SslMode = SslMode.VerifyFull,
                    Timeout = Math.Max(dsqlOptions.ConnectionTimeoutSeconds, 1),
                    CommandTimeout = Math.Max(dsqlOptions.CommandTimeoutSeconds, 1),
                    ConnectionLifetime = Math.Max(dsqlOptions.ConnectionLifetimeSeconds, 1),
                    KeepAlive = Math.Max(dsqlOptions.KeepAliveSeconds, 0),
                    MaxPoolSize = Math.Max(dsqlOptions.MaxPoolSize, 1),
                    MinPoolSize = Math.Max(dsqlOptions.MinPoolSize, 0)
                };
                settings.ConnectionString = connectionBuilder.ConnectionString;
            },
            configureDataSourceBuilder: dataSourceBuilder =>
            {
                var host = dataSourceBuilder.ConnectionStringBuilder.Host;
                if (string.IsNullOrWhiteSpace(host) || !host.Contains("dsql", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // DSQL은 DISCARD ALL 명령을 지원하지 않습니다.
                dataSourceBuilder.ConnectionStringBuilder.NoResetOnClose = true;

                dataSourceBuilder.UsePeriodicPasswordProvider(
                    async (_, cancellationToken) =>
                    {
                        if (tokenProvider is null)
                        {
                            throw new InvalidOperationException("DSQL 토큰 공급자가 구성되지 않았습니다.");
                        }

                        return await tokenProvider.GetTokenAsync(cancellationToken);
                    },
                    TimeSpan.FromMinutes(Math.Max(dsqlOptions.TokenRefreshMinutes, 1)),
                    TimeSpan.FromSeconds(60));
            });
    }
}
