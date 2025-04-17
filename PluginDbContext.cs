using Microsoft.EntityFrameworkCore;

public class PluginDbContext : DbContext
{
    private readonly string _dbPath;
    public PluginDbContext(string dbPath) { _dbPath = dbPath; }

    // Existing DbSet for players
    public DbSet<PlayerRecord> Players { get; set; } = null!;

    // --- NEW: DbSet for bots ---
    public DbSet<BotRecord> BotStats { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Increase command timeout slightly if needed, though usually not necessary for SQLite
        optionsBuilder.UseSqlite($"Data Source={_dbPath}", opt => opt.CommandTimeout(60));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Existing configuration for PlayerRecord
        modelBuilder.Entity<PlayerRecord>().HasKey(p => p.SteamId);

        // --- NEW: Configuration for BotRecord (Optional but good practice) ---
        modelBuilder.Entity<BotRecord>(entity =>
        {
            entity.HasKey(b => b.Id); // Define primary key
            // Add an index on MapName and BotName for faster lookups
            entity.HasIndex(b => new { b.MapName, b.BotName }).IsUnique(false); // Not unique as same bot name might appear later
        });
    }
}