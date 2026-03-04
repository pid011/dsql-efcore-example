using System.Text.Json.Serialization;
using GameBackend;
using GameBackend.DSQL;
using GameBackend.Models;
using GameBackend.Options;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.AddServiceDefaults();
builder.AddDsqlNpgsqlDataSource("gamebackenddb");
builder.Services.Configure<DsqlOptions>(builder.Configuration.GetSection(DsqlOptions.SectionName));
builder.Services.AddDbContextPool<GameDbContext>((serviceProvider, dbContextOptionsBuilder) =>
{
    var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
    dbContextOptionsBuilder
        .UseNpgsql(dataSource)
        .UseSnakeCaseNamingConvention();
});
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "GameBackend API");
    });
}

app.UseHttpsRedirection();

app.MapPost("/players", async (CreatePlayerRequest request, GameDbContext dbContext, CancellationToken cancellationToken) =>
{
    var trimmedName = request.Name.Trim();
    if (trimmedName.Length is 0 or > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["Name must be between 1 and 100 characters."]
        });
    }

    var now = DateTime.UtcNow;
    var player = new Player
    {
        Name = trimmedName,
        CreatedAt = now,
        UpdatedAt = now
    };

    dbContext.Players.Add(player);

    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_players_name" })
    {
        return Results.Conflict(new { message = "Name is already in use." });
    }
    catch (DbUpdateException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }

    return Results.Created($"/players/{player.Id}", ToPlayerResponse(player));
});

app.MapGet("/players", async (GameDbContext dbContext, CancellationToken cancellationToken) =>
{
    var players = await dbContext.Players
        .AsNoTracking()
        .OrderByDescending(player => player.CreatedAt)
        .Select(player => new PlayerResponse(player.Id, player.Name, player.CreatedAt, player.UpdatedAt))
        .ToListAsync(cancellationToken);

    return Results.Ok(players);
});

app.MapGet("/players/{id:guid}", async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
{
    var player = await dbContext.Players
        .AsNoTracking()
        .FirstOrDefaultAsync(player => player.Id == id, cancellationToken);

    return player is null ? Results.NotFound() : Results.Ok(ToPlayerResponse(player));
});

app.MapGet("/players/{id:guid}/profile", async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
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



app.MapPost("/players/{id:guid}/match-results",
    async (Guid id, SubmitMatchResultRequest request, GameDbContext dbContext, CancellationToken cancellationToken) =>
    {
        if (!Enum.IsDefined(request.MatchResult))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["matchResult"] = ["matchResult must be one of Win, Loss, or Draw."]
            });
        }

        var playerExists = await dbContext.Players
            .AnyAsync(p => p.Id == id, cancellationToken);

        if (!playerExists)
        {
            return Results.NotFound();
        }

        var stat = await dbContext.PlayerStats
            .FirstOrDefaultAsync(s => s.PlayerId == id, cancellationToken);

        var now = DateTime.UtcNow;

        if (stat is null)
        {
            stat = new PlayerStat { PlayerId = id, CreatedAt = now };
            dbContext.PlayerStats.Add(stat);
        }

        // Update basic stats
        stat.MatchesPlayed++;
        stat.TotalKills += request.Kills;
        stat.TotalDeaths += request.Deaths;
        stat.TotalAssists += request.Assists;
        stat.TotalScore += request.Score;
        stat.LastMatchAt = now;
        stat.UpdatedAt = now;

        // Win/Loss/Draw and streak
        switch (request.MatchResult)
        {
            case MatchResult.Win:
                stat.Wins++;
                stat.CurrentWinStreak++;
                if (stat.CurrentWinStreak > stat.BestWinStreak)
                    stat.BestWinStreak = stat.CurrentWinStreak;
                break;
            case MatchResult.Loss:
                stat.Losses++;
                stat.CurrentWinStreak = 0;
                break;
            case MatchResult.Draw:
                stat.Draws++;
                stat.CurrentWinStreak = 0;
                break;
        }

        // Elo rating calculation (K=32, simplified with opponent rating = own rating)
        const int kFactor = 32;
        double actualScore = request.MatchResult switch
        {
            MatchResult.Win => 1.0,
            MatchResult.Loss => 0.0,
            _ => 0.5
        };
        // Opponent rating = own rating, so expected = 0.5
        const double expectedScore = 0.5;
        int ratingDelta = (int)Math.Round(kFactor * (actualScore - expectedScore));
        stat.Rating = Math.Max(0, stat.Rating + ratingDelta);

        if (stat.Rating > stat.HighestRating)
            stat.HighestRating = stat.Rating;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToPlayerStatResponse(stat));
    });

app.Run();

static PlayerResponse ToPlayerResponse(Player player)
{
    return new(
    player.Id,
    player.Name,
    player.CreatedAt,
    player.UpdatedAt);
}

static PlayerStatResponse ToPlayerStatResponse(PlayerStat stat)
{
    return new(
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
    stat.KDA,
    stat.CreatedAt,
    stat.UpdatedAt);
}

record CreatePlayerRequest(
    string Name);

record PlayerResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt);

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

record PlayerProfileResponse(
    PlayerResponse Player,
    PlayerStatResponse? Stat);

enum MatchResult { Win, Loss, Draw }

record SubmitMatchResultRequest(
    MatchResult MatchResult,
    int Kills,
    int Deaths,
    int Assists,
    int Score);