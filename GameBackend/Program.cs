using GameBackend.Data;
using GameBackend.Extensions;
using GameBackend.Models;
using GameBackend.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddDsqlNpgsqlDataSource("gamebackenddb");
builder.Services.Configure<DsqlOptions>(builder.Configuration.GetSection(DsqlOptions.SectionName));
builder.Services.AddDbContextPool<AppDbContext>((serviceProvider, dbContextOptionsBuilder) =>
{
    var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
    dbContextOptionsBuilder.UseNpgsql(dataSource);
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/players", async (CreatePlayerRequest request, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var trimmedName = request.Name.Trim();
    if (trimmedName.Length is 0 or > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Name은 1자 이상 100자 이하여야 합니다."]
        });
    }

    var now = DateTime.UtcNow;
    var player = new Player
    {
        Id = Guid.NewGuid(),
        Name = trimmedName,
        CreatedAt = now,
        UpdatedAt = now
    };

    dbContext.Players.Add(player);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/players/{player.Id}", ToPlayerResponse(player));
});

app.MapGet("/players", async (AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var players = await dbContext.Players
        .AsNoTracking()
        .OrderByDescending(player => player.CreatedAt)
        .Select(player => new PlayerResponse(player.Id, player.Name, player.CreatedAt, player.UpdatedAt))
        .ToListAsync(cancellationToken);

    return Results.Ok(players);
});

app.MapGet("/players/{id:guid}", async (Guid id, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var player = await dbContext.Players
        .AsNoTracking()
        .FirstOrDefaultAsync(player => player.Id == id, cancellationToken);

    return player is null ? Results.NotFound() : Results.Ok(ToPlayerResponse(player));
});

app.MapGet("/players/{id:guid}/profile", async (Guid id, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var result = await dbContext.Players
        .AsNoTracking()
        .Where(player => player.Id == id)
        .GroupJoin(
            dbContext.PlayerStats.AsNoTracking(),
            player => player.Id,
            stat => stat.PlayerId,
            (player, stats) => new { Player = player, Stat = stats.FirstOrDefault() })
        .FirstOrDefaultAsync(cancellationToken);

    if (result is null)
    {
        return Results.NotFound();
    }

    var response = new PlayerProfileResponse(
        ToPlayerResponse(result.Player),
        result.Stat is null ? null : ToPlayerStatResponse(result.Stat));
    return Results.Ok(response);
});

app.MapPut("/players/{id:guid}/stats", async (Guid id, UpdatePlayerStatRequest request, AppDbContext dbContext, CancellationToken cancellationToken) =>
{
    var playerExists = await dbContext.Players
        .AsNoTracking()
        .AnyAsync(current => current.Id == id, cancellationToken);

    if (!playerExists)
    {
        return Results.NotFound();
    }

    var validationErrors = new Dictionary<string, string[]>();

    if (request.MatchesPlayed < 0 ||
        request.Wins < 0 ||
        request.Losses < 0 ||
        request.Draws < 0 ||
        request.CurrentWinStreak < 0 ||
        request.BestWinStreak < 0 ||
        request.TotalKills < 0 ||
        request.TotalDeaths < 0 ||
        request.TotalAssists < 0 ||
        request.TotalScore < 0 ||
        request.Rating < 0 ||
        request.HighestRating < 0)
    {
        validationErrors["stats"] = ["수치 데이터는 0 이상이어야 합니다."];
    }

    if (request.Wins + request.Losses + request.Draws > request.MatchesPlayed)
    {
        validationErrors["results"] = ["Wins + Losses + Draws는 MatchesPlayed를 초과할 수 없습니다."];
    }

    if (request.HighestRating < request.Rating)
    {
        validationErrors["rating"] = ["HighestRating은 Rating 이상이어야 합니다."];
    }

    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var now = DateTime.UtcNow;
    var playerStat = await dbContext.PlayerStats
        .FirstOrDefaultAsync(current => current.PlayerId == id, cancellationToken);

    if (playerStat is null)
    {
        playerStat = new PlayerStat
        {
            PlayerId = id,
            CreatedAt = now
        };

        dbContext.PlayerStats.Add(playerStat);
    }

    playerStat.MatchesPlayed = request.MatchesPlayed;
    playerStat.Wins = request.Wins;
    playerStat.Losses = request.Losses;
    playerStat.Draws = request.Draws;
    playerStat.CurrentWinStreak = request.CurrentWinStreak;
    playerStat.BestWinStreak = request.BestWinStreak;
    playerStat.TotalKills = request.TotalKills;
    playerStat.TotalDeaths = request.TotalDeaths;
    playerStat.TotalAssists = request.TotalAssists;
    playerStat.TotalScore = request.TotalScore;
    playerStat.Rating = request.Rating;
    playerStat.HighestRating = request.HighestRating;
    playerStat.LastMatchAt = request.LastMatchAt;
    playerStat.UpdatedAt = now;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(ToPlayerStatResponse(playerStat));
});

app.Run();

static PlayerResponse ToPlayerResponse(Player player) =>
    new(player.Id, player.Name, player.CreatedAt, player.UpdatedAt);

static PlayerStatResponse ToPlayerStatResponse(PlayerStat stat) =>
    new(
        stat.PlayerId,
        stat.MatchesPlayed,
        stat.Wins,
        stat.Losses,
        stat.Draws,
        stat.CurrentWinStreak,
        stat.BestWinStreak,
        stat.TotalKills,
        stat.TotalDeaths,
        stat.TotalAssists,
        stat.TotalScore,
        stat.Rating,
        stat.HighestRating,
        stat.LastMatchAt,
        stat.WinRate,
        stat.Kda,
        stat.CreatedAt,
        stat.UpdatedAt);

record CreatePlayerRequest(string Name);

record PlayerResponse(Guid Id, string Name, DateTime CreatedAt, DateTime UpdatedAt);

record PlayerStatResponse(
    Guid PlayerId,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    int CurrentWinStreak,
    int BestWinStreak,
    int TotalKills,
    int TotalDeaths,
    int TotalAssists,
    int TotalScore,
    int Rating,
    int HighestRating,
    DateTime? LastMatchAt,
    decimal WinRate,
    decimal Kda,
    DateTime CreatedAt,
    DateTime UpdatedAt);

record PlayerProfileResponse(PlayerResponse Player, PlayerStatResponse? Stat);

record UpdatePlayerStatRequest(
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    int CurrentWinStreak,
    int BestWinStreak,
    int TotalKills,
    int TotalDeaths,
    int TotalAssists,
    int TotalScore,
    int Rating,
    int HighestRating,
    DateTime? LastMatchAt);

