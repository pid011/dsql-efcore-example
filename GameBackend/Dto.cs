namespace GameBackend;

public record CreatePlayerRequest(
    string Name);

public record PlayerResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record PlayerStatResponse(
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

public record PlayerProfileResponse(
    PlayerResponse Player,
    PlayerStatResponse? Stat);

public enum MatchResult
{
    Win,
    Loss,
    Draw
}

public record SubmitMatchResultRequest(
    MatchResult MatchResult,
    int Kills,
    int Deaths,
    int Assists,
    int Score);