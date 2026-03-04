using System.ComponentModel.DataAnnotations;

namespace GameBackend.Models;

public sealed class Player
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
