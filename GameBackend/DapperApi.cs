using System.Data;
using Dapper;
using GameBackend.Models;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace GameBackend;

public static class DapperApi
{
    static DapperApi()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public static void ConfigureEndpoints(IEndpointRouteBuilder app)
    {
        var dapperApi = app.MapGroup("/dapper");

        dapperApi.MapPost("/players", async (CreatePlayerRequest request, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
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

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            try
            {
                const string sql = """
                                   INSERT INTO players (id, name, created_at, updated_at)
                                   VALUES (@Id, @Name, @CreatedAt, @UpdatedAt);
                                   """;

                await connection.ExecuteAsync(new CommandDefinition(sql, player, cancellationToken: cancellationToken));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation && ex.ConstraintName == "ux_players_name")
            {
                return Results.Conflict(new { message = "Name is already in use." });
            }
            catch (PostgresException ex)
            {
                return Results.Problem(detail: ex.MessageText, statusCode: 500);
            }

            return Results.Created($"/dapper/players/{player.Id}", ToPlayerResponse(player));
        });

        dapperApi.MapPost("/reset", async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);

            try
            {
                await connection.ExecuteAsync(new CommandDefinition("DELETE FROM player_stats;", transaction: transaction, cancellationToken: cancellationToken));
                await connection.ExecuteAsync(new CommandDefinition("DELETE FROM players;", transaction: transaction, cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return Results.NoContent();
            }
            catch (PostgresException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Results.Problem(detail: ex.MessageText, statusCode: 500);
            }
        });

        dapperApi.MapGet("/players", async (int? limit, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            if (limit is <= 0 or > 1000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["limit"] = ["limit must be between 1 and 1000."]
                });
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = """
                               SELECT id, name, created_at, updated_at
                               FROM players
                               ORDER BY created_at DESC;
                               """;

            const string sqlWithLimit = """
                                        SELECT id, name, created_at, updated_at
                                        FROM players
                                        ORDER BY created_at DESC
                                        LIMIT @limit;
                                        """;

            var players = limit.HasValue
                ? await connection.QueryAsync<Player>(new CommandDefinition(sqlWithLimit, new { limit = limit.Value }, cancellationToken: cancellationToken))
                : await connection.QueryAsync<Player>(new CommandDefinition(sql, cancellationToken: cancellationToken));

            return Results.Ok(players.Select(ToPlayerResponse));
        });

        dapperApi.MapGet("/players/{id:guid}", async (Guid id, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = """
                               SELECT id, name, created_at, updated_at
                               FROM players
                               WHERE id = @id;
                               """;

            var player = await connection.QuerySingleOrDefaultAsync<Player>(
                new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

            return player is null ? Results.NotFound() : Results.Ok(ToPlayerResponse(player));
        });

        dapperApi.MapGet("/players/{id:guid}/profile", async (Guid id, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = """
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

            var profile = (await connection.QueryAsync<Player, PlayerStat, PlayerProfileResponse>(
                new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken),
                (player, stat) =>
                {
                    var statResponse = stat.PlayerId == Guid.Empty
                        ? null
                        : ToPlayerStatResponse(stat);

                    return new PlayerProfileResponse(ToPlayerResponse(player), statResponse);
                },
                splitOn: "player_id")).SingleOrDefault();

            if (profile is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(profile);
        });

        dapperApi.MapPost("/players/{id:guid}/match-results", async (Guid id, SubmitMatchResultRequest request, NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        {
            if (!Enum.IsDefined(request.MatchResult))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["matchResult"] = ["matchResult must be one of Win, Loss, or Draw."]
                });
            }

            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Snapshot, cancellationToken);

            try
            {
                const string playerExistsSql = """
                                               SELECT 1
                                               FROM players
                                               WHERE id = @id
                                               LIMIT 1;
                                               """;

                var playerExists = await connection.ExecuteScalarAsync<int?>(
                    new CommandDefinition(playerExistsSql, new { id }, transaction, cancellationToken: cancellationToken));

                if (!playerExists.HasValue)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Results.NotFound();
                }

                const string statSql = """
                                       SELECT player_id, matches_played, wins, losses, draws,
                                              current_win_streak, best_win_streak,
                                              total_kills, total_deaths, total_assists, total_score,
                                              rating, highest_rating, last_match_at,
                                              created_at, updated_at
                                       FROM player_stats
                                       WHERE player_id = @id;
                                       """;

                var stat = await connection.QuerySingleOrDefaultAsync<PlayerStat>(
                    new CommandDefinition(statSql, new { id }, transaction, cancellationToken: cancellationToken));

                var now = DateTime.UtcNow;
                var isNewStat = stat is null;

                stat ??= new PlayerStat
                {
                    PlayerId = id,
                    CreatedAt = now,
                    UpdatedAt = now
                };

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

                if (isNewStat)
                {
                    const string insertSql = """
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
                                                 @CreatedAt, @UpdatedAt);
                                             """;

                    await connection.ExecuteAsync(new CommandDefinition(insertSql, stat, transaction, cancellationToken: cancellationToken));
                }
                else
                {
                    const string updateSql = """
                                             UPDATE player_stats
                                             SET matches_played = @MatchesPlayed,
                                                 wins = @Wins,
                                                 losses = @Losses,
                                                 draws = @Draws,
                                                 current_win_streak = @CurrentWinStreak,
                                                 best_win_streak = @BestWinStreak,
                                                 total_kills = @TotalKills,
                                                 total_deaths = @TotalDeaths,
                                                 total_assists = @TotalAssists,
                                                 total_score = @TotalScore,
                                                 rating = @Rating,
                                                 highest_rating = @HighestRating,
                                                 last_match_at = @LastMatchAt,
                                                 updated_at = @UpdatedAt
                                             WHERE player_id = @PlayerId;
                                             """;

                    await connection.ExecuteAsync(new CommandDefinition(updateSql, stat, transaction, cancellationToken: cancellationToken));
                }

                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(ToPlayerStatResponse(stat));
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