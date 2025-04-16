using CounterStrikeSharp.API;

using CounterStrikeSharp.API.Core;

using CounterStrikeSharp.API.Core.Attributes;

using CounterStrikeSharp.API.Core.Attributes.Registration;

using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;



namespace StopSound;



[MinimumApiVersion(80)]

public class StopSound : BasePlugin

{

    public override string ModuleName => "Example: With Database EFCore";

    public override string ModuleVersion => "1.0.0";

    public override string ModuleAuthor => "CounterStrikeSharp & Contributors";

    public override string ModuleDescription => "A plugin that reads and writes from the database.";




    private string _dbPath = null!;


    public override void Load(bool hotReload)
    {
        _dbPath = Path.Join(ModuleDirectory, "database.db");
        Logger.LogInformation("Loading database from {Path}", _dbPath);

        // Ensure database and table exist using Migrations
        // Run in a separate thread to avoid blocking the main thread
        Task.Run(async () =>
        {
            try
            {
                await using var dbContext = new PluginDbContext(_dbPath);
                await dbContext.Database.EnsureCreatedAsync(); // Use this line
                Logger.LogInformation("Database schema ensured.");
            }
            catch (Exception ex)
            {
                // Log any errors during migration
                Logger.LogError(ex, "Error applying database migrations"); // Changed log message slightly
            }
        });
    }

    [GameEventHandler] HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid == null) return HookResult.Continue;

        var steamId = @event.Userid.AuthorizedSteamID?.SteamId64;
        if (steamId == null) return HookResult.Continue;

        // Run in a separate thread
        Task.Run(async () =>
        {
            try
            {
                // Create a DbContext instance for this operation
                await using var dbContext = new PluginDbContext(_dbPath);

                // Check if the player already exists in the database
                var playerRecord = await dbContext.Players.FindAsync(steamId.Value);
                if (playerRecord == null)
                {
                    // Player doesn't exist, add new record
                    playerRecord = new PlayerRecord { SteamId = steamId.Value, Kills = 0 };
                    dbContext.Players.Add(playerRecord);
                    await dbContext.SaveChangesAsync(); // Persist changes to the database
                }
            }
            catch (Exception ex)
            {
                // It's CRUCIAL to log errors in background tasks
                Logger.LogError(ex, "Error adding player to database for SteamID {SteamId}", steamId);
            }
        });

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerKilled(EventPlayerDeath @event, GameEventInfo info)
    {
        if (@event.Attacker == @event.Userid) return HookResult.Continue;

        var steamId = @event.Attacker.AuthorizedSteamID?.SteamId64;
        if (steamId == null) return HookResult.Continue;

        // Run in a separate thread
        Task.Run(async () =>
        {
            try
            {
                // Create a DbContext instance for this operation
                await using var dbContext = new PluginDbContext(_dbPath);

                var playerRecord = await dbContext.Players.FindAsync(steamId.Value);

                if (playerRecord == null)
                {
                    // Player doesn't exist, add new record
                    playerRecord = new PlayerRecord { SteamId = steamId.Value, Kills = 1 };
                    dbContext.Players.Add(playerRecord);
                }
                else
                {
                    // Player exists, increment kills
                    playerRecord.Kills++;
                    // No need for dbContext.Players.Update(playerRecord); EF Core tracks changes
                }

                await dbContext.SaveChangesAsync(); // Persist changes to the database
            }
            catch (Exception ex)
            {
                // It's CRUCIAL to log errors in background tasks
                Logger.LogError(ex, "Error updating player kills for SteamID {SteamId}", steamId);
            }
        });

        return HookResult.Continue;
    }




    [ConsoleCommand("css_kills", "Get count of kills for a player")]
    public void OnKillsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || player.AuthorizedSteamID == null) return;

        var steamId = player.AuthorizedSteamID.SteamId64;

        // Run in a separate thread
        Task.Run(async () =>
        {
            int kills = 0;
            try
            {
                // Create a DbContext instance for this operation
                await using var dbContext = new PluginDbContext(_dbPath);

                var playerRecord = await dbContext.Players
                                            .AsNoTracking() // Use AsNoTracking for read-only queries for better performance
                                            .FirstOrDefaultAsync(p => p.SteamId == steamId);

                kills = playerRecord?.Kills ?? 0;
            }
            catch (Exception ex)
            {
                // It's CRUCIAL to log errors in background tasks
                Logger.LogError(ex, "Error retrieving player kills for SteamID {SteamId}", steamId);
                // Optionally inform the player about the error
                Server.NextFrame(() => player.PrintToChat("Error retrieving kills data."));
                return; // Exit the task
            }


            // Print the result - must run on game thread
            Server.NextFrame(() => { player.PrintToChat($"Kills: {kills}"); });
        });
    }

}