using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("players")] 
public class PlayerRecord
{
    [Key] 
    [Column("steamid")] 
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // Important for non-auto-incrementing keys
    public ulong SteamId { get; set; }

    [Column("playername")] 
    public string? PlayerName { get; set; }

    [Column("kills")]
    public int Kills { get; set; }
}
