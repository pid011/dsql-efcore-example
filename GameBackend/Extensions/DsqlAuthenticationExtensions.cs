using GameBackend.Infrastructure;
using GameBackend.Options;
using Npgsql;

namespace GameBackend.Extensions;

public static class DsqlAuthenticationExtensions
{
    public static void AddDsqlNpgsqlDataSource(this IHostApplicationBuilder builder, string connectionName)
    {
        var dsqlOptions = builder.Configuration.GetSection(DsqlOptions.SectionName).Get<DsqlOptions>() ?? new DsqlOptions();
        var clusterEndpoint = ResolveClusterEndpoint(builder.Configuration, dsqlOptions);

        if (string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            throw new InvalidOperationException(
                "DSQL 클러스터 엔드포인트가 없습니다. AppHost DSQL 참조(AWS:Resources:GameBackendDsqlClusterEndpoint) 또는 Dsql:ClusterEndpoint를 설정해 주세요.");
        }

        if (!clusterEndpoint.Contains("dsql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{clusterEndpoint}'는 DSQL 엔드포인트 형식이 아닙니다.");
        }

        var tokenProvider = new DsqlAuthTokenProvider(clusterEndpoint, dsqlOptions);

        builder.AddNpgsqlDataSource(connectionName,
            settings =>
            {
                settings.DisableHealthChecks = true;

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
                // DSQL은 DISCARD ALL 명령을 지원하지 않습니다.
                dataSourceBuilder.ConnectionStringBuilder.NoResetOnClose = true;

                dataSourceBuilder.UsePeriodicPasswordProvider(
                    async (_, cancellationToken) => await tokenProvider.GetTokenAsync(cancellationToken),
                    TimeSpan.FromMinutes(Math.Max(dsqlOptions.TokenRefreshMinutes, 1)),
                    TimeSpan.FromSeconds(60));
            });
    }

    private static string? ResolveClusterEndpoint(IConfiguration configuration, DsqlOptions dsqlOptions)
    {
        var clusterEndpoint = configuration["GAMEBACKEND_DSQL_CLUSTER_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return clusterEndpoint;
        }

        clusterEndpoint = configuration["AWS:Resources:GameBackendDsqlClusterEndpoint"];
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return clusterEndpoint;
        }

        clusterEndpoint = configuration["AWS:Resources:gamebackend-dsql-cluster:GameBackendDsqlClusterEndpoint"];
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return clusterEndpoint;
        }

        clusterEndpoint = configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("AWS:Resources:", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Key.EndsWith(":GameBackendDsqlClusterEndpoint", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return clusterEndpoint;
        }

        clusterEndpoint = configuration.AsEnumerable()
            .Where(x => x.Key.StartsWith("AWS:Resources:", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) &&
                                 x.Contains(".dsql.", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(clusterEndpoint))
        {
            return clusterEndpoint;
        }

        return dsqlOptions.ClusterEndpoint;
    }
}
