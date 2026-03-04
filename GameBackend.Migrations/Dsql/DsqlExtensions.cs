using DbUp.Builder;
using DbUp.Engine.Transactions;
using DbUp.Postgresql;

namespace GameBackend.Migrations.Dsql;

public static class DsqlExtensions
{
    public static UpgradeEngineBuilder DsqlDatabase(
        this SupportedDatabases supportedDatabases,
        IConnectionManager connectionManager,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(supportedDatabases);
        ArgumentNullException.ThrowIfNull(connectionManager);

        var builder = new UpgradeEngineBuilder();
        builder.Configure(configuration => configuration.ConnectionManager = connectionManager);
        builder.Configure(configuration => configuration.ScriptExecutor = new PostgresqlScriptExecutor(
            () => configuration.ConnectionManager,
            () => configuration.Log,
            schema,
            () => configuration.VariablesEnabled,
            configuration.ScriptPreprocessors,
            () => configuration.Journal));
        builder.Configure(configuration => configuration.Journal = new DsqlTableJournal(
            () => configuration.ConnectionManager,
            () => configuration.Log,
            schema,
            "dbup_schema_versions"));
        builder.WithPreprocessor(new PostgresqlPreprocessor());
        return builder;
    }
}
