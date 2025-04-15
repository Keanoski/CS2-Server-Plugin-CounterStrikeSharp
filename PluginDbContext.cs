using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // If you want EF Core logging

public class PluginDbContext : DbContext
{
    public DbSet<PlayerRecord> Players { get; set; } = null!; // Represents the players table

    private readonly string _dbPath;

    // Constructor to pass the database path
    public PluginDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Configure EF Core to use SQLite
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");

        // Optional: Add logging (requires Microsoft.Extensions.Logging)
        // optionsBuilder.UseLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
        // optionsBuilder.EnableSensitiveDataLogging(); // Use only for debugging
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the model if needed (e.g., specific constraints, indexes)
        // For simple cases like this, attributes on the PlayerRecord class are often sufficient.
        base.OnModelCreating(modelBuilder);
    }
}