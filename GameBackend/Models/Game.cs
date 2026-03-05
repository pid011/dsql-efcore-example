using System.ComponentModel.DataAnnotations;
using GameBackend.Players;

namespace GameBackend.Models;

public sealed class Game
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Status { get; set; } = GameStatus.Created;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
