using System.Data;
using GameBackend.Models;
using GameBackend.Players;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GameBackend;

public static class EFCoreApi
{
    private const string DeletePlayerStatsBatchSql =
        """
        DELETE FROM player_stats
        WHERE player_id IN (
            SELECT player_id
            FROM player_stats
            ORDER BY player_id
            LIMIT {0}
        );
        """;

    private const string DeletePlayersBatchSql =
        """
        DELETE FROM players
        WHERE id IN (
            SELECT id
            FROM players
            ORDER BY id
            LIMIT {0}
        );
        """;

    private const string DeleteGamesBatchSql =
        """
        DELETE FROM games
        WHERE id IN (
            SELECT id
            FROM games
            ORDER BY id
            LIMIT {0}
        );
        """;

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

        efCoreApi.MapPost("/players",
            async (CreatePlayerRequest request, GameDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var validationErrors = PlayerRequestValidator.ValidatePlayerName(request.Name, out var trimmedName);
                if (validationErrors is not null)
                {
                    return Results.ValidationProblem(validationErrors);
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
                catch (Exception ex) when (PostgresErrorClassifier.IsUniquePlayerNameViolation(ex))
                {
                    return Results.Conflict(new { message = "Name is already in use." });
                }
                catch (Exception ex) when (ex is DbUpdateException or PostgresException)
                {
                    return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
                }

                return Results.Created($"/efcore/players/{player.Id}", PlayerResponseMapper.ToPlayerResponse(player));
            });

        efCoreApi.MapPost("/game/create", async (GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            var game = new Game
            {
                Status = GameStatus.Created,
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.Games.Add(game);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is DbUpdateException or PostgresException)
            {
                return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
            }

            return Results.Created($"/efcore/game/{game.Id}",
                new CreateGameResponse(PlayerResponseMapper.ToGameResponse(game)));
        });

        efCoreApi.MapPost("/game/end",
            async (EndGameRequest request, GameDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var validationErrors = PlayerRequestValidator.ValidateEndGameRequest(request);
                if (validationErrors is not null)
                {
                    return Results.ValidationProblem(validationErrors);
                }

                var results = request.Results!;
                var playerIds = results.Select(result => result.PlayerId).Distinct().ToArray();

                try
                {
                    return await SerializationRetryPolicy.ExecuteAsync(async retryToken =>
                    {
                        var originalAutoSavepoints = dbContext.Database.AutoSavepointsEnabled;
                        dbContext.Database.AutoSavepointsEnabled = false;
                        await using var transaction =
                            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Snapshot, retryToken);

                        try
                        {
                            var game = await dbContext.Games
                                .FirstOrDefaultAsync(candidate => candidate.Id == request.GameId, retryToken);

                            if (game is null)
                            {
                                await transaction.RollbackAsync(retryToken);
                                return Results.NotFound();
                            }

                            if (game.EndedAt.HasValue || string.Equals(game.Status, GameStatus.Ended,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await transaction.RollbackAsync(retryToken);
                                return Results.Conflict(new { message = "Game is already ended." });
                            }

                            var existingPlayerIds = await dbContext.Players
                                .AsNoTracking()
                                .Where(player => playerIds.Contains(player.Id))
                                .Select(player => player.Id)
                                .ToListAsync(retryToken);

                            if (existingPlayerIds.Count != playerIds.Length)
                            {
                                var existingPlayerSet = existingPlayerIds.ToHashSet();
                                var missingPlayerIds = playerIds
                                    .Where(playerId => !existingPlayerSet.Contains(playerId))
                                    .Select(playerId => playerId.ToString())
                                    .ToArray();

                                await transaction.RollbackAsync(retryToken);
                                return Results.ValidationProblem(new Dictionary<string, string[]>
                                {
                                    ["results.playerId"] =
                                        [$"Unknown playerId values: {string.Join(", ", missingPlayerIds)}"]
                                });
                            }

                            var statsByPlayerId = await dbContext.PlayerStats
                                .Where(stat => playerIds.Contains(stat.PlayerId))
                                .ToDictionaryAsync(stat => stat.PlayerId, retryToken);

                            var now = DateTime.UtcNow;
                            var updatedStats = new Dictionary<Guid, PlayerStat>(playerIds.Length);

                            foreach (var result in results)
                            {
                                if (!statsByPlayerId.TryGetValue(result.PlayerId, out var stat))
                                {
                                    stat = PlayerStatCalculator.CreateNew(result.PlayerId, now);
                                    dbContext.PlayerStats.Add(stat);
                                    statsByPlayerId[result.PlayerId] = stat;
                                }

                                PlayerStatCalculator.ApplyMatchResult(stat, result, now);
                                updatedStats[result.PlayerId] = stat;
                            }

                            game.Status = GameStatus.Ended;
                            game.EndedAt = now;
                            game.UpdatedAt = now;

                            await dbContext.SaveChangesAsync(retryToken);
                            await transaction.CommitAsync(retryToken);

                            var response = new EndGameResponse(
                                PlayerResponseMapper.ToGameResponse(game),
                                updatedStats.Values
                                    .OrderBy(stat => stat.PlayerId)
                                    .Select(PlayerResponseMapper.ToPlayerStatResponse)
                                    .ToArray());

                            return Results.Ok(response);
                        }
                        catch (Exception ex) when (PostgresErrorClassifier.IsSerializationFailure(ex))
                        {
                            await transaction.RollbackAsync(retryToken);
                            dbContext.ChangeTracker.Clear();
                            throw;
                        }
                        catch (Exception ex) when (ex is DbUpdateException or PostgresException)
                        {
                            await transaction.RollbackAsync(retryToken);
                            return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex),
                                statusCode: 500);
                        }
                        finally
                        {
                            dbContext.Database.AutoSavepointsEnabled = originalAutoSavepoints;
                        }
                    }, cancellationToken);
                }
                catch (Exception ex) when (PostgresErrorClassifier.IsSerializationFailure(ex))
                {
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }
            });

        efCoreApi.MapPost("/reset", async (GameDbContext dbContext, CancellationToken cancellationToken) =>
        {
            try
            {
                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => dbContext.Database.ExecuteSqlRawAsync(
                        DeletePlayerStatsBatchSql,
                        [PlayerApiConstants.ResetDeleteBatchSize],
                        ct),
                    cancellationToken);

                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => dbContext.Database.ExecuteSqlRawAsync(
                        DeletePlayersBatchSql,
                        [PlayerApiConstants.ResetDeleteBatchSize],
                        ct),
                    cancellationToken);

                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => dbContext.Database.ExecuteSqlRawAsync(
                        DeleteGamesBatchSql,
                        [PlayerApiConstants.ResetDeleteBatchSize],
                        ct),
                    cancellationToken);

                return Results.NoContent();
            }
            catch (PostgresException ex)
            {
                return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
            }
        });

        efCoreApi.MapGet("/players",
            async (int? limit, DateTime? beforeCreatedAt, Guid? beforeId, GameDbContext dbContext,
                CancellationToken cancellationToken) =>
            {
                var validationErrors =
                    PlayerRequestValidator.ValidateListRequest(limit, beforeCreatedAt, beforeId,
                        out var effectiveLimit);
                if (validationErrors is not null)
                {
                    return Results.ValidationProblem(validationErrors);
                }

                List<PlayerResponse> players;

                if (beforeCreatedAt.HasValue)
                {
                    players = await dbContext.Players
                        .AsNoTracking()
                        .Where(player => player.CreatedAt < beforeCreatedAt.Value
                                         || (player.CreatedAt == beforeCreatedAt.Value
                                             && player.Id < beforeId!.Value))
                        .OrderByDescending(player => player.CreatedAt)
                        .ThenByDescending(player => player.Id)
                        .Take(effectiveLimit)
                        .Select(player => new PlayerResponse(
                            player.Id,
                            player.Name,
                            player.CreatedAt,
                            player.UpdatedAt))
                        .ToListAsync(cancellationToken);
                }
                else
                {
                    players = await dbContext.Players
                        .AsNoTracking()
                        .OrderByDescending(player => player.CreatedAt)
                        .ThenByDescending(player => player.Id)
                        .Take(effectiveLimit)
                        .Select(player => new PlayerResponse(
                            player.Id,
                            player.Name,
                            player.CreatedAt,
                            player.UpdatedAt))
                        .ToListAsync(cancellationToken);
                }

                return Results.Ok(players);
            });

        efCoreApi.MapGet("/players/{id:guid}",
            async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var player = await dbContext.Players
                    .AsNoTracking()
                    .Where(player => player.Id == id)
                    .Select(player => new PlayerResponse(
                        player.Id,
                        player.Name,
                        player.CreatedAt,
                        player.UpdatedAt))
                    .FirstOrDefaultAsync(cancellationToken);

                return player is null ? Results.NotFound() : Results.Ok(player);
            });

        efCoreApi.MapGet("/players/{id:guid}/profile",
            async (Guid id, GameDbContext dbContext, CancellationToken cancellationToken) =>
            {
                var profile = await (
                        from player in dbContext.Players.AsNoTracking()
                        where player.Id == id
                        join stat in dbContext.PlayerStats.AsNoTracking() on player.Id equals stat.PlayerId into statGroup
                        from stat in statGroup.DefaultIfEmpty()
                        select new { player, stat })
                    .FirstOrDefaultAsync(cancellationToken);

                if (profile is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(PlayerResponseMapper.ToPlayerProfileResponse(profile.player, profile.stat));
            });
    }
}
