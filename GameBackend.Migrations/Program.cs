using GameBackend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

// DSQL 경로는 임시 비활성화 상태입니다.
// AppHost에서 주입되는 외부 PostgreSQL 연결 문자열을 사용합니다.
var connectionString = builder.Configuration.GetConnectionString("gamebackenddb")
    ?? throw new InvalidOperationException("ConnectionStrings:gamebackenddb 설정이 필요합니다.");

var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    MaxPoolSize = 10
};

builder.Services.AddDbContextPool<AppDbContext>(dbContextOptionsBuilder =>
{
    dbContextOptionsBuilder.UseNpgsql(connectionStringBuilder.ConnectionString);
});

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await dbContext.Database.MigrateAsync();

Console.WriteLine("GameBackend database migration completed.");
