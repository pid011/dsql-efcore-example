using GameBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace GameBackend.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerStat> PlayerStats => Set<PlayerStat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PlayerStat>()
            .HasIndex(stat => stat.PlayerId)
            .IsUnique();
    }
}
