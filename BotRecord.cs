using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("bot_stats")] // Name of the new table
public class BotRecord
{
    [Key] // Auto-incrementing primary key for the record itself
    public int Id { get; set; }

    [Column("bot_name")]
    [MaxLength(64)] // Define a max length for the name
    public required string BotName { get; set; }

    [Column("kills")]
    public int Kills { get; set; }

    [Column("map_name")]
    [MaxLength(64)] // Define a max length for the map name
    public required string MapName { get; set; }

    [Column("last_updated")]
    public DateTime LastUpdated { get; set; }
}