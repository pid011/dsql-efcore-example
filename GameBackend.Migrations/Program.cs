using System.Reflection;
using DbUp;
using DbUp.Postgresql;
using GameBackend.DSQL;
using GameBackend.Migrations.Dsql;
using GameBackend.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.AddDsqlNpgsqlDataSource("gamebackenddb");
builder.Services.Configure<DsqlOptions>(builder.Configuration.GetSection(DsqlOptions.SectionName));

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

var upgrader = DeployChanges.To
    .DsqlDatabase(new PostgresqlConnectionManager(dataSource), "public")
    .WithScriptsEmbeddedInAssembly(
        Assembly.GetExecutingAssembly(),
        resourceName => resourceName.Contains(".Migrations.", StringComparison.OrdinalIgnoreCase) &&
                        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
    .WithoutTransaction()
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful)
{
    throw result.Error ?? new InvalidOperationException("GameBackend database migration failed.");
}

Console.WriteLine("GameBackend database migration completed.");
