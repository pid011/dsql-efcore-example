using DbUp.Engine.Output;
using DbUp.Engine.Transactions;
using DbUp.Postgresql;

namespace GameBackend.Migrations.Dsql;

public sealed class DsqlTableJournal(
    Func<IConnectionManager> connectionManager,
    Func<IUpgradeLog> logger,
    string? schema,
    string tableName)
    : PostgresqlTableJournal(connectionManager, logger, schema, tableName)
{
    protected override string GetInsertJournalEntrySql(string scriptName, string applied)
    {
        return $"""
                INSERT INTO {FqSchemaTableName} (schemaversionsid, scriptname, applied)
                SELECT COALESCE(MAX(schemaversionsid), 0) + 1, {scriptName}, {applied}
                FROM {FqSchemaTableName}
                """;
    }

    protected override string GetJournalEntriesSql()
    {
        return $"SELECT scriptname FROM {FqSchemaTableName} ORDER BY scriptname";
    }

    protected override string CreateSchemaTableSql(string quotedPrimaryKeyName)
    {
        return $"""
                CREATE TABLE {FqSchemaTableName}
                (
                    schemaversionsid integer NOT NULL,
                    scriptname character varying(255) NOT NULL,
                    applied timestamp with time zone NOT NULL,
                    CONSTRAINT {quotedPrimaryKeyName} PRIMARY KEY (schemaversionsid)
                )
                """;
    }
}
