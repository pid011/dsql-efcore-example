using GameBackend.Models;

namespace GameBackend.Players;

internal static class PlayerStatCalculator
{
    private const int KFactor = 32;
    private const double ExpectedScore = 0.5;

    public static PlayerStat CreateNew(Guid playerId, DateTime now)
    {
        return new PlayerStat
        {
            PlayerId = playerId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static void ApplyMatchResult(PlayerStat stat, GamePlayerResultRequest request, DateTime now)
    {
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

        var actualScore = request.MatchResult switch
        {
            MatchResult.Win => 1.0,
            MatchResult.Loss => 0.0,
            _ => 0.5
        };

        var ratingDelta = (int)Math.Round(KFactor * (actualScore - ExpectedScore));
        stat.Rating = Math.Max(0, stat.Rating + ratingDelta);

        if (stat.Rating > stat.HighestRating)
        {
            stat.HighestRating = stat.Rating;
        }
    }
}
