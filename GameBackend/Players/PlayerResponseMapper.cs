using GameBackend.Models;

namespace GameBackend.Players;

internal static class PlayerResponseMapper
{
    public static PlayerResponse ToPlayerResponse(Player player)
    {
        return new PlayerResponse(
            player.Id,
            player.Name,
            player.CreatedAt,
            player.UpdatedAt);
    }

    public static PlayerStatResponse ToPlayerStatResponse(PlayerStat stat)
    {
        return new PlayerStatResponse(
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

    public static PlayerProfileResponse ToPlayerProfileResponse(Player player, PlayerStat? stat)
    {
        return new PlayerProfileResponse(
            ToPlayerResponse(player),
            stat is null ? null : ToPlayerStatResponse(stat));
    }

    public static GameResponse ToGameResponse(Game game)
    {
        return new GameResponse(
            game.Id,
            game.Status,
            game.StartedAt,
            game.EndedAt,
            game.CreatedAt,
            game.UpdatedAt);
    }
}
