using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("players")] // Optional if class name matches table name convention
public class PlayerRecord
{
    [Key] // Marks steamid as the primary key
    [Column("steamid")] // Maps property to the specific column name
    [DatabaseGenerated(DatabaseGeneratedOption.None)] // Important for non-auto-incrementing keys
    public ulong SteamId { get; set; }

    [Column("kills")]
    public int Kills { get; set; }
}
