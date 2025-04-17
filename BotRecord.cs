using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("bot_stats")] 
public class BotRecord
{
    [Key] 
    public int Id { get; set; }

    [Column("bot_name")]
    [MaxLength(64)] 
    public required string BotName { get; set; }

    [Column("kills")]
    public int Kills { get; set; }

    [Column("map_name")]
    [MaxLength(64)] 
    public required string MapName { get; set; }

    [Column("last_updated")]
    public DateTime LastUpdated { get; set; }
}