using System.Data;
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

        efCoreApi.MapPost("/reset", async (GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);

            try
            {
                await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM player_stats;", cancellationToken);
                await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM players;", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Problem(detail: ex.MessageText, statusCode: 500);
            }
        });

        efCoreApi.MapGet("/players", async (int? limit, GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            if (limit is <= 0 or > 1000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = ["limit must be between 1 and 1000."]
                });
            }

            IQueryable<Player> query = dbContext.Players
                .AsNoTracking()
                .OrderByDescending(player => player.CreatedAt);

            if (limit.HasValue)
            {
                query = query.Take(limit.Value);
            }

            var players = await query
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
            var result = await (
                    from player in dbContext.Players.AsNoTracking()
                    where player.Id == id
                    join stat in dbContext.PlayerStats.AsNoTracking() on player.Id equals stat.PlayerId into statGroup
                    from stat in statGroup.DefaultIfEmpty()
                    select new { Player = player, Stat = stat })
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

                var originalAutoSavepoints = dbContext.Database.AutoSavepointsEnabled;
                dbContext.Database.AutoSavepointsEnabled = false;
                await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);

                try
                {
                    var playerExists = await dbContext.Players
                        .AnyAsync(player => player.Id == id, cancellationToken);

                    if (!playerExists)
                    {
                        await transaction.RollbackAsync(cancellationToken);
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
                    await transaction.CommitAsync(cancellationToken);

                    return Results.Ok(ToPlayerStatResponse(stat));
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }
                catch (DbUpdateException ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.Problem(detail: ex.Message, statusCode: 500);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }
                catch (PostgresException ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.Problem(detail: ex.MessageText, statusCode: 500);
                }
                finally
                {
                    dbContext.Database.AutoSavepointsEnabled = originalAutoSavepoints;
                }
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