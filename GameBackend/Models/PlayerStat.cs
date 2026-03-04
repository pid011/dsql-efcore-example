using System.ComponentModel.DataAnnotations;

namespace GameBackend.Models;

public sealed class PlayerStat
{
    [Key]
    public Guid PlayerId { get; set; }

    public int MatchesPlayed { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Draws { get; set; }

    public int CurrentWinStreak { get; set; }

    public int BestWinStreak { get; set; }

    public int TotalKills { get; set; }

    public int TotalDeaths { get; set; }

    public int TotalAssists { get; set; }

    public int TotalScore { get; set; }

    public int Rating { get; set; } = 1000;

    public int HighestRating { get; set; } = 1000;

    public DateTime? LastMatchAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public decimal WinRate => MatchesPlayed == 0
        ? 0
        : Math.Round((decimal)Wins / MatchesPlayed * 100, 2);

    public decimal KDA => TotalDeaths == 0
        ? TotalKills + TotalAssists
        : Math.Round((decimal)(TotalKills + TotalAssists) / TotalDeaths, 2);
}
