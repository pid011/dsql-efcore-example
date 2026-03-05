using GameBackend.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameBackend;

public static class EFCoreApi
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextPool<GameDbContext>((serviceProvider, dbContextOptionsBuilder) =>
        {
            var dataSource = serviceProvider.GetRequiredService<NpgsqlDataSource>();
            dbContextOptionsBuilder
                .UseNpgsql(dataSource)
                .UseSnakeCaseNamingConvention();
        });
    }

    public static void ConfigureEndpoints(IEndpointRouteBuilder app)
    {
        var efCoreApi = app.MapGroup("/efcore");

        efCoreApi.MapPost("/players", async (CreatePlayerRequest request, GameDbContext dbContext, CancellationToken cancellationToken) =>
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

            return Results.Created($"/efcore/players/{player.Id}", ToPlayerResponse(player));
        });

        efCoreApi.MapGet("/players", async (GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var players = await dbContext.Players
                .AsNoTracking()
                .OrderByDescending(player => player.CreatedAt)
                .Select(player => new PlayerResponse(player.Id, player.Name, player.CreatedAt, player.UpdatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(players);
        });

        efCoreApi.MapGet("/players/{id:guid}", async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var player = await dbContext.Players
                .AsNoTracking()
                .FirstOrDefaultAsync(player => player.Id == id, cancellationToken);

            return player is null ? Results.NotFound() : Results.Ok(ToPlayerResponse(player));
        });

        efCoreApi.MapGet("/players/{id:guid}/profile", async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
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

        efCoreApi.MapPost("/players/{id:guid}/match-results",
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
                    .AnyAsync(player => player.Id == id, cancellationToken);

                if (!playerExists)
                {
                    return Results.NotFound();
                }

                var stat = await dbContext.PlayerStats
                    .FirstOrDefaultAsync(playerStat => playerStat.PlayerId == id, cancellationToken);

                var now = DateTime.UtcNow;

                if (stat is null)
                {
                    stat = new PlayerStat { PlayerId = id, CreatedAt = now };
                    dbContext.PlayerStats.Add(stat);
                }

                stat.MatchesPlayed++;
                stat.TotalKills += request.Kills;
                stat.TotalDeaths += request.Deaths;
                stat.TotalAssists += request.Assists;
                stat.TotalScore += request.Score;
                stat.LastMatchAt = now;
                stat.UpdatedAt = now;

                switch (request.MatchResult)
                {
                    case MatchResult.Win:
                        stat.Wins++;
                        stat.CurrentWinStreak++;
                        if (stat.CurrentWinStreak > stat.BestWinStreak)
                        {
                            stat.BestWinStreak = stat.CurrentWinStreak;
                        }

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

                const int kFactor = 32;
                var actualScore = request.MatchResult switch
                {
                    MatchResult.Win => 1.0,
                    MatchResult.Loss => 0.0,
                    _ => 0.5
                };
                const double expectedScore = 0.5;
                var ratingDelta = (int)Math.Round(kFactor * (actualScore - expectedScore));
                stat.Rating = Math.Max(0, stat.Rating + ratingDelta);

                if (stat.Rating > stat.HighestRating)
                {
                    stat.HighestRating = stat.Rating;
                }

                await dbContext.SaveChangesAsync(cancellationToken);

                return Results.Ok(ToPlayerStatResponse(stat));
            });
    }

    private static PlayerResponse ToPlayerResponse(Player player)
    {
        return new(
            player.Id,
            player.Name,
            player.CreatedAt,
            player.UpdatedAt);
    }

    private static PlayerStatResponse ToPlayerStatResponse(PlayerStat stat)
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
}