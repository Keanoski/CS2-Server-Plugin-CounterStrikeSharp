using CounterStrikeSharp.API;

using CounterStrikeSharp.API.Core;

using CounterStrikeSharp.API.Core.Attributes;

using CounterStrikeSharp.API.Core.Attributes.Registration;

using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
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
    // --- NEW: Dictionary to track last menu open time per player ---
    private readonly Dictionary<ulong, DateTime> _lastMenuOpenTime = new();
    private readonly TimeSpan _menuOpenCooldown = TimeSpan.FromMilliseconds(500); // Cooldown of 0.5 seconds

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

        // --- Register Listeners ---
        // Listen for 'say' and 'say_team' commands to catch chat messages
        RegisterListener<Listeners.OnMapStart>(name => { /* Can do map specific things here */ });
        AddCommandListener("say", Listener_SayChat);          // Hook public chat
        AddCommandListener("say_team", Listener_SayChat);     // Hook team chat
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

    // --- Updated Listener ---
    private HookResult Listener_SayChat(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid || player.IsBot)
        {
            return HookResult.Continue;
        }

        // Using GetArg is generally preferred now
        string? commandTrigger = (commandInfo.ArgCount > 1) ? commandInfo.GetArg(1)?.Trim() : null;

        // Log received command attempt (optional, good for debug)
        // Logger.LogInformation($"[ChatListener] Player: {player.PlayerName}, Arg1: '{commandTrigger}'");

        if (commandTrigger != null && commandTrigger.Equals("!menu", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation($"[ChatListener] '!menu' command attempt recognized for player {player.PlayerName}.");

            // --- Check Cooldown ---
            ulong steamId = player.SteamID; // Use SteamID which is reliable
            if (_lastMenuOpenTime.TryGetValue(steamId, out DateTime lastOpen) && DateTime.UtcNow < lastOpen + _menuOpenCooldown)
            {
                Logger.LogInformation($"[Cooldown] Menu open throttled for player {player.PlayerName} (SteamID: {steamId}).");
                // Return Handled to prevent the "!menu" text appearing if throttled.
                return HookResult.Handled;
            }

            // --- Update Last Open Time ---
            _lastMenuOpenTime[steamId] = DateTime.UtcNow;
            Logger.LogInformation($"[Cooldown] Updated last menu open time for player {player.PlayerName} (SteamID: {steamId}).");


            // --- Open the Menu ---
            OpenMainMenu(player); // Call your existing menu function

            // --- VERY IMPORTANT: Stop the original chat command ---
            // If we successfully handle '!menu', stop the "say" or "say_team" command
            // from proceeding further (e.g., prevent showing "!menu" in chat).
            return HookResult.Handled;
        }

        // If it wasn't our command, let the chat message proceed normally
        return HookResult.Continue;
    }

    // --- Updated Method to Create and Open the Menu (No changes needed here for flicker) ---
    private void OpenMainMenu(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return;

        var menu = new CenterHtmlMenu("Main Menu");
        menu.AddMenuOption("Show My Kills", HandleMenuSelection);
        menu.AddMenuOption("Placeholder Option 2", HandleMenuSelection);
        menu.AddMenuOption("Close Menu", HandleMenuSelection);

        MenuManager.OpenCenterHtmlMenu(this, player, menu);
        Logger.LogInformation($"[Menu] Opened main menu for {player.PlayerName}");
    }

    // --- Menu Selection Handler (Remains the same - adding logging) ---
    private void HandleMenuSelection(CCSPlayerController player, ChatMenuOption option)
    {
        if (player == null || !player.IsValid) return;

        Logger.LogInformation($"[Menu] Player {player.PlayerName} selected option: {option.Text}"); // Log selection

        switch (option.Text)
        {
            case "Show My Kills":
                _ = ShowPlayerKillsAsync(player);
                break;
            // ... other cases
            default:
                Logger.LogWarning($"[Menu] Unhandled menu option selected by {player.PlayerName}: {option.Text}");
                break;
        }
    }

    // Other methods (Load, OnPlayerConnect, OnPlayerKilled, ShowPlayerKillsAsync, DBContext, etc.) remain the same...


    // --- REFACTORED: Async Method to Get and Show Kills ---
    private async Task ShowPlayerKillsAsync(CCSPlayerController player)
    {
        if (player == null || player.AuthorizedSteamID == null)
        {
            // This case should ideally not happen if called correctly, but check anyway
            Logger.LogWarning("ShowPlayerKillsAsync called with invalid player object.");
            return;
        }

        var steamId = player.AuthorizedSteamID.SteamId64;
        int kills = 0;
        bool errorOccurred = false;

        try
        {
            await using var dbContext = new PluginDbContext(_dbPath);
            var playerRecord = await dbContext.Players
                                           .AsNoTracking() // Good for read-only
                                           .FirstOrDefaultAsync(p => p.SteamId == steamId);
            kills = playerRecord?.Kills ?? 0;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving player kills for SteamID {SteamId}", steamId);
            errorOccurred = true;
        }

        // IMPORTANT: Interaction with player (PrintToChat) must happen on the main game thread.
        // Use Server.NextFrame or AddTimer to schedule it.
        Server.NextFrame(() =>
        {
            // Double check player validity *again* before printing, as they might disconnect
            // between the async operation finishing and the next frame executing.
            if (player == null || !player.IsValid) return;

            if (errorOccurred)
            {
                player.PrintToChat("Sorry, there was an error retrieving your kills data.");
            }
            else
            {
                player.PrintToChat($"Your Kills: {kills}");
            }
        });
    }

}