using System.Data;
using Dapper;
using GameBackend.Models;
using GameBackend.Players;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace GameBackend;

public static class DapperApi
{
    private const string InsertPlayerSql =
        """
        INSERT INTO players (id, name, created_at, updated_at)
        VALUES (@Id, @Name, @CreatedAt, @UpdatedAt);
        """;

    private const string InsertGameSql =
        """
        INSERT INTO games (id, status, started_at, ended_at, created_at, updated_at)
        VALUES (@Id, @Status, @StartedAt, @EndedAt, @CreatedAt, @UpdatedAt);
        """;

    private const string GetGameByIdSql =
        """
        SELECT id, status, started_at, ended_at, created_at, updated_at
        FROM games
        WHERE id = @id
        LIMIT 1;
        """;

    private const string FinalizeGameSql =
        """
        UPDATE games
        SET status = @status,
            ended_at = @endedAt,
            updated_at = @updatedAt
        WHERE id = @id
          AND ended_at IS NULL;
        """;

    private const string SelectExistingPlayerIdsSql =
        """
        SELECT id
        FROM players
        WHERE id = ANY(@playerIds);
        """;

    private const string SelectExistingPlayerStatsSql =
        """
        SELECT player_id, matches_played, wins, losses, draws,
               current_win_streak, best_win_streak,
               total_kills, total_deaths, total_assists, total_score,
               rating, highest_rating, last_match_at,
               created_at, updated_at
        FROM player_stats
        WHERE player_id = ANY(@playerIds);
        """;

    private const string UpsertPlayerStatSql =
        """
        INSERT INTO player_stats (
            player_id, matches_played, wins, losses, draws,
            current_win_streak, best_win_streak,
            total_kills, total_deaths, total_assists, total_score,
            rating, highest_rating, last_match_at,
            created_at, updated_at)
        VALUES (
            @PlayerId, @MatchesPlayed, @Wins, @Losses, @Draws,
            @CurrentWinStreak, @BestWinStreak,
            @TotalKills, @TotalDeaths, @TotalAssists, @TotalScore,
            @Rating, @HighestRating, @LastMatchAt,
            @CreatedAt, @UpdatedAt)
        ON CONFLICT (player_id)
        DO UPDATE SET
            matches_played = EXCLUDED.matches_played,
            wins = EXCLUDED.wins,
            losses = EXCLUDED.losses,
            draws = EXCLUDED.draws,
            current_win_streak = EXCLUDED.current_win_streak,
            best_win_streak = EXCLUDED.best_win_streak,
            total_kills = EXCLUDED.total_kills,
            total_deaths = EXCLUDED.total_deaths,
            total_assists = EXCLUDED.total_assists,
            total_score = EXCLUDED.total_score,
            rating = EXCLUDED.rating,
            highest_rating = EXCLUDED.highest_rating,
            last_match_at = EXCLUDED.last_match_at,
            updated_at = EXCLUDED.updated_at;
        """;

    private const string DeletePlayerStatsBatchSql =
        """
        DELETE FROM player_stats
        WHERE player_id IN (
            SELECT player_id
            FROM player_stats
            ORDER BY player_id
            LIMIT @batchSize
        );
        """;

    private const string DeletePlayersBatchSql =
        """
        DELETE FROM players
        WHERE id IN (
            SELECT id
            FROM players
            ORDER BY id
            LIMIT @batchSize
        );
        """;

    private const string DeleteGamesBatchSql =
        """
        DELETE FROM games
        WHERE id IN (
            SELECT id
            FROM games
            ORDER BY id
            LIMIT @batchSize
        );
        """;

    private const string ListPlayersSql =
        """
        SELECT id, name, created_at, updated_at
        FROM players
        ORDER BY created_at DESC, id DESC
        LIMIT @limit;
        """;

    private const string ListPlayersWithCursorSql =
        """
        SELECT id, name, created_at, updated_at
        FROM players
        WHERE created_at < @beforeCreatedAt
           OR (created_at = @beforeCreatedAt AND id < @beforeId)
        ORDER BY created_at DESC, id DESC
        LIMIT @limit;
        """;

    private const string GetPlayerByIdSql =
        """
        SELECT id, name, created_at, updated_at
        FROM players
        WHERE id = @id;
        """;

    private const string GetPlayerProfileSql =
        """
        SELECT p.id, p.name, p.created_at, p.updated_at,
               s.player_id, s.matches_played, s.wins, s.losses, s.draws,
               s.current_win_streak, s.best_win_streak,
               s.total_kills, s.total_deaths, s.total_assists, s.total_score,
               s.rating, s.highest_rating, s.last_match_at,
               s.created_at, s.updated_at
        FROM players p
        LEFT JOIN player_stats s ON s.player_id = p.id
        WHERE p.id = @id;
        """;

    static DapperApi()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public static void ConfigureEndpoints(IEndpointRouteBuilder app)
    {
        var dapperApi = app.MapGroup("/dapper");

        dapperApi.MapPost("/players",
            async (CreatePlayerRequest request, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
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

                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

                try
                {
                    await connection.ExecuteAsync(new CommandDefinition(InsertPlayerSql, player,
                        cancellationToken: cancellationToken));
                }
                catch (PostgresException ex) when (PostgresErrorClassifier.IsUniquePlayerNameViolation(ex))
                {
                    return Results.Conflict(new { message = "Name is already in use." });
                }
                catch (PostgresException ex)
                {
                    return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
                }

                return Results.Created($"/dapper/players/{player.Id}", PlayerResponseMapper.ToPlayerResponse(player));
            });

        dapperApi.MapPost("/game/create", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            var now = DateTime.UtcNow;
            var game = new Game
            {
                Status = GameStatus.Created,
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(InsertGameSql, game,
                    cancellationToken: cancellationToken));
            }
            catch (PostgresException ex)
            {
                return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
            }

            return Results.Created($"/dapper/game/{game.Id}",
                new CreateGameResponse(PlayerResponseMapper.ToGameResponse(game)));
        });

        dapperApi.MapPost("/game/end",
            async (EndGameRequest request, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
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
                        await using var connection = await dataSource.OpenConnectionAsync(retryToken);
                        await using var transaction =
                            await connection.BeginTransactionAsync(IsolationLevel.Snapshot, retryToken);

                        try
                        {
                            var game = await connection.QuerySingleOrDefaultAsync<Game>(
                                new CommandDefinition(GetGameByIdSql, new { id = request.GameId }, transaction,
                                    cancellationToken: retryToken));

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

                            var existingPlayerIds = (await connection.QueryAsync<Guid>(
                                    new CommandDefinition(SelectExistingPlayerIdsSql, new { playerIds }, transaction,
                                        cancellationToken: retryToken)))
                                .ToHashSet();

                            if (existingPlayerIds.Count != playerIds.Length)
                            {
                                var missingPlayerIds = playerIds
                                    .Where(playerId => !existingPlayerIds.Contains(playerId))
                                    .Select(playerId => playerId.ToString())
                                    .ToArray();

                                await transaction.RollbackAsync(retryToken);
                                return Results.ValidationProblem(new Dictionary<string, string[]>
                                {
                                    ["results.playerId"] =
                                        [$"Unknown playerId values: {string.Join(", ", missingPlayerIds)}"]
                                });
                            }

                            var existingStats = (await connection.QueryAsync<PlayerStat>(
                                    new CommandDefinition(SelectExistingPlayerStatsSql, new { playerIds }, transaction,
                                        cancellationToken: retryToken)))
                                .ToDictionary(stat => stat.PlayerId);

                            var now = DateTime.UtcNow;
                            var updatedStats = new Dictionary<Guid, PlayerStat>(playerIds.Length);

                            foreach (var result in results)
                            {
                                if (!existingStats.TryGetValue(result.PlayerId, out var stat))
                                {
                                    stat = PlayerStatCalculator.CreateNew(result.PlayerId, now);
                                    existingStats[result.PlayerId] = stat;
                                }

                                PlayerStatCalculator.ApplyMatchResult(stat, result, now);
                                updatedStats[result.PlayerId] = stat;
                            }

                            foreach (var stat in updatedStats.Values)
                            {
                                await connection.ExecuteAsync(
                                    new CommandDefinition(UpsertPlayerStatSql, stat, transaction,
                                        cancellationToken: retryToken));
                            }

                            var finalized = await connection.ExecuteAsync(
                                new CommandDefinition(
                                    FinalizeGameSql,
                                    new
                                    {
                                        id = request.GameId, status = GameStatus.Ended, endedAt = now, updatedAt = now
                                    },
                                    transaction,
                                    cancellationToken: retryToken));

                            if (finalized == 0)
                            {
                                await transaction.RollbackAsync(retryToken);
                                return Results.Conflict(new { message = "Game is already ended." });
                            }

                            game.Status = GameStatus.Ended;
                            game.EndedAt = now;
                            game.UpdatedAt = now;

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
                            throw;
                        }
                        catch (PostgresException ex)
                        {
                            await transaction.RollbackAsync(retryToken);
                            return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex),
                                statusCode: 500);
                        }
                    }, cancellationToken);
                }
                catch (Exception ex) when (PostgresErrorClassifier.IsSerializationFailure(ex))
                {
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }
            });
        dapperApi.MapPost("/reset", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            try
            {
                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => connection.ExecuteAsync(new CommandDefinition(
                        DeletePlayerStatsBatchSql,
                        new { batchSize = PlayerApiConstants.ResetDeleteBatchSize },
                        cancellationToken: ct)),
                    cancellationToken);

                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => connection.ExecuteAsync(new CommandDefinition(
                        DeletePlayersBatchSql,
                        new { batchSize = PlayerApiConstants.ResetDeleteBatchSize },
                        cancellationToken: ct)),
                    cancellationToken);

                await BatchDeleteExecutor.ExecuteUntilEmptyAsync(
                    ct => connection.ExecuteAsync(new CommandDefinition(
                        DeleteGamesBatchSql,
                        new { batchSize = PlayerApiConstants.ResetDeleteBatchSize },
                        cancellationToken: ct)),
                    cancellationToken);

                return Results.NoContent();
            }
            catch (PostgresException ex)
            {
                return Results.Problem(detail: PostgresErrorClassifier.ToProblemDetail(ex), statusCode: 500);
            }
        });

        dapperApi.MapGet("/players",
            async (int? limit, DateTime? beforeCreatedAt, Guid? beforeId, NpgsqlDataSource dataSource,
                CancellationToken cancellationToken) =>
            {
                var validationErrors =
                    PlayerRequestValidator.ValidateListRequest(limit, beforeCreatedAt, beforeId,
                        out var effectiveLimit);
                if (validationErrors is not null)
                {
                    return Results.ValidationProblem(validationErrors);
                }

                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

                var players = beforeCreatedAt.HasValue
                    ? await connection.QueryAsync<Player>(new CommandDefinition(
                        ListPlayersWithCursorSql,
                        new
                        {
                            limit = effectiveLimit,
                            beforeCreatedAt = beforeCreatedAt.Value,
                            beforeId = beforeId!.Value
                        },
                        cancellationToken: cancellationToken))
                    : await connection.QueryAsync<Player>(new CommandDefinition(
                        ListPlayersSql,
                        new { limit = effectiveLimit },
                        cancellationToken: cancellationToken));

                return Results.Ok(players.Select(PlayerResponseMapper.ToPlayerResponse));
            });

        dapperApi.MapGet("/players/{id:guid}",
            async (Guid id, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

                var player = await connection.QuerySingleOrDefaultAsync<Player>(
                    new CommandDefinition(GetPlayerByIdSql, new { id }, cancellationToken: cancellationToken));

                return player is null
                    ? Results.NotFound()
                    : Results.Ok(PlayerResponseMapper.ToPlayerResponse(player));
            });

        dapperApi.MapGet("/players/{id:guid}/profile",
            async (Guid id, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

                var profile = (await connection.QueryAsync<Player, PlayerStat, PlayerProfileResponse>(
                    new CommandDefinition(GetPlayerProfileSql, new { id }, cancellationToken: cancellationToken),
                    (player, stat) => PlayerResponseMapper.ToPlayerProfileResponse(
                        player,
                        stat.PlayerId == Guid.Empty ? null : stat),
                    splitOn: "player_id")).SingleOrDefault();

                if (profile is null)
                {
                    return Results.NotFound();
                }

                return Results.Ok(profile);
            });
    }
}

