using GameBackend.Data;
using GameBackend.Extensions;
using GameBackend.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

builder.AddDsqlNpgsqlDataSource("gamebackenddb");
builder.Services.Configure<DsqlOptions>(builder.Configuration.GetSection(DsqlOptions.SectionName));
builder.Services.AddDbContextPool<AppDbContext>((serviceProvider, dbContextOptionsBuilder) =>
{
    var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
    dbContextOptionsBuilder.UseNpgsql(dataSource);
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await Migrate(dbContext);

Console.WriteLine("GameBackend database migration completed.");

static async Task Migrate(AppDbContext dbContext)
{
    var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();
    if (pendingMigrations.Count == 0)
    {
        return;
    }

    var appliedMigrations = (await dbContext.Database.GetAppliedMigrationsAsync()).ToList();
    var migrator = dbContext.GetService<IMigrator>();
    var fromMigration = appliedMigrations.LastOrDefault() ?? "0";

    foreach (var targetMigration in pendingMigrations)
    {
        var script = migrator.GenerateScript(
            fromMigration: fromMigration,
            toMigration: targetMigration,
            options: MigrationsSqlGenerationOptions.NoTransactions);

        if (!string.IsNullOrWhiteSpace(script))
        {
            await dbContext.Database.ExecuteSqlRawAsync(script);
        }

        fromMigration = targetMigration;
    }
}