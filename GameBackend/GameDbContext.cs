using GameBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace GameBackend;

public sealed class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerStat> PlayerStats => Set<PlayerStat>();
}
