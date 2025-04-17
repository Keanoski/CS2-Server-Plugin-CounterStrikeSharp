using CounterStrikeSharp.API;

using CounterStrikeSharp.API.Core;

using CounterStrikeSharp.API.Core.Attributes;

using CounterStrikeSharp.API.Core.Attributes.Registration;

using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static CounterStrikeSharp.API.Core.Listeners;



namespace PluginStatTracking;



[MinimumApiVersion(80)]

public class PluginStatTracking : BasePlugin

{

    public override string ModuleName => "Example: With Database EFCore";

    public override string ModuleVersion => "1.0.0";

    public override string ModuleAuthor => "CounterStrikeSharp & Contributors";

    public override string ModuleDescription => "A plugin that reads and writes from the database.";


    private string _dbPath = null!;
    private CCSGameRules? _gameRules; // used for menu flicker fix                                 
    private string? _currentMapName; // Store the current map name
    public override void Load(bool hotReload)
    {
        _dbPath = Path.Join(ModuleDirectory, "database.db");
        Logger.LogInformation("Loading database from {Path}", _dbPath);

        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnMapStart>(OnMapStartHandler);

        // Ensure database and table exist using Migrations
        // Run in a separate thread to avoid blocking the main thread
        Task.Run(async () =>
        {
            try
            {
                await using var dbContext = new PluginDbContext(_dbPath);
                await dbContext.Database.EnsureCreatedAsync();
                Logger.LogInformation("Database schema ensured.");
            }
            catch (Exception ex)
            {
                // Log any errors during migration
                Logger.LogError(ex, "Error applying database migrations");
            }
        });


        // Listen for 'say' and 'say_team' commands to catch chat messages
        RegisterListener<Listeners.OnMapStart>(name => { /* Can do map specific things here */ });
        AddCommandListener("say", Listener_SayChat);          // Hook public chat
        AddCommandListener("say_team", Listener_SayChat);     // Hook team chat
    }

    private void OnMapStartHandler(string mapName)
    {
        _gameRules = null; // Reset if using for flicker fix
        _currentMapName = mapName; // Store the new map name
        Logger.LogInformation("Map changed to: {MapName}", _currentMapName);

        // Clean up bot stats from previous maps in the background
        _ = Task.Run(async () => await CleanUpOldBotStatsAsync(_currentMapName));
    }

    private async Task CleanUpOldBotStatsAsync(string currentMap)
    {
        if (string.IsNullOrEmpty(currentMap)) return;

        try
        {
            Logger.LogInformation("Attempting to clear old bot stats (not matching '{CurrentMap}') from database...", currentMap);
            await using var dbContext = new PluginDbContext(_dbPath);

            // Find bot stats that are NOT from the current map
            var oldBotStats = await dbContext.BotStats
                .Where(b => b.MapName != currentMap)
                .ToListAsync();

            if (oldBotStats.Any())
            {
                dbContext.BotStats.RemoveRange(oldBotStats);
                int count = await dbContext.SaveChangesAsync();
                Logger.LogInformation("Cleared {Count} old bot stat records from database.", count);
            }
            else
            {
                Logger.LogInformation("No old bot stats found in database to clear.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error clearing old bot stats from database.");
        }
    }

    /// <summary>
    /// retrieves the game rules from the map. This is used to check if the round has restarted.
    /// also nessesary for the menu flicker fix.
    /// </summary>
    private void InitializeGameRules()
    {
        var gameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        _gameRules = gameRulesProxy?.GameRules;
        Logger.LogInformation("GameRules initialized: {GameRules}", _gameRules != null ? "Yes" : "No");
    }

    /// <summary>
    /// Checks if the round has restarted. This is used to check if the menu should be closed.
    /// also nessesary for the menu flicker fix.
    /// </summary>
    private void OnTick()
    {
        if (_gameRules == null)
        {
            InitializeGameRules();
        }
        else
        {
            _gameRules.GameRestart = _gameRules.RestartRoundTime < Server.CurrentTime;
        }
    }


    /// <summary>
    /// Handles player connection events. This is where we add the player to the database if they don't exist.
    /// </summary>
    /// <param name="event"></param>
    /// <param name="info"></param>
    /// <returns></returns>
    [GameEventHandler] HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if (@event.Userid == null) return HookResult.Continue;

        var steamId = @event.Userid.AuthorizedSteamID?.SteamId64;
        var playerName = @event.Userid.PlayerName;
        if (steamId == null) return HookResult.Continue;

        // Run in a separate thread
        Task.Run(async () =>
        {
            try
            {

                await using var dbContext = new PluginDbContext(_dbPath);

                // Check if the player already exists in the database
                var playerRecord = await dbContext.Players.FindAsync(steamId.Value);
                if (playerRecord == null)
                {
                    // Player doesn't exist, add new record
                    playerRecord = new PlayerRecord { SteamId = steamId.Value, PlayerName = playerName, Kills = 0 };
                    dbContext.Players.Add(playerRecord);
                    await dbContext.SaveChangesAsync(); // Persist changes to the database
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error adding player to database for SteamID {SteamId}", steamId);
            }
        });

        return HookResult.Continue;
    }

    //[GameEventHandler]
    //public HookResult OnPlayerKilled(EventPlayerDeath @event, GameEventInfo info)
    //{
    //    if (@event.Attacker == @event.Userid) return HookResult.Continue;

    //    // We know @event.Attacker is not null here because of the 'if' check above.
    //    // Use the null-forgiving operator (!) on @event.Attacker.
    //    var steamId = @event.Attacker!.AuthorizedSteamID?.SteamId64;
    //    var playerName = @event.Attacker.PlayerName;
    //    if (steamId == null) return HookResult.Continue;

    //    // Run in a separate thread
    //    Task.Run(async () =>
    //    {
    //        try
    //        {
    //            await using var dbContext = new PluginDbContext(_dbPath);

    //            var playerRecord = await dbContext.Players.FindAsync(steamId.Value);

    //            if (playerRecord == null)
    //            {
    //                // Player doesn't exist, add new record
    //                playerRecord = new PlayerRecord { SteamId = steamId.Value, PlayerName = playerName, Kills = 1 };
    //                dbContext.Players.Add(playerRecord);
    //            }
    //            else
    //            {
    //                // Player exists, increment kills
    //                playerRecord.Kills++;
    //            }

    //            await dbContext.SaveChangesAsync();
    //        }
    //        catch (Exception ex)
    //        {
    //            Logger.LogError(ex, "Error updating player kills for SteamID {SteamId}", steamId);
    //        }
    //    });

    //    return HookResult.Continue;
    //}

    [GameEventHandler]
    public HookResult OnPlayerKilled(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var steamId = @event.Attacker!.AuthorizedSteamID?.SteamId64;
        var playerName = @event.Attacker.PlayerName;
        if (attacker == null || !attacker.IsValid || attacker == @event.Userid) return HookResult.Continue;

        // --- Handle Bot Attacker ---
        if (attacker.IsBot)
        {
            string botName = attacker.PlayerName ?? "Unknown Bot";
            string? mapName = _currentMapName; // Use the stored map name

            if (string.IsNullOrEmpty(mapName))
            {
                Logger.LogWarning("Current map name not set, cannot record bot kill for {BotName}", botName);
                return HookResult.Continue; // Or handle differently
            }

            // Run DB update in background
            Task.Run(async () =>
            {
                try
                {
                    await using var dbContext = new PluginDbContext(_dbPath);

                    // Find bot record for this specific name on this specific map
                    var botRecord = await dbContext.BotStats
                        .FirstOrDefaultAsync(b => b.BotName == botName && b.MapName == mapName);

                    if (botRecord == null)
                    {
                        // First kill for this bot name on this map
                        botRecord = new BotRecord
                        {
                            BotName = botName,
                            Kills = 1,
                            MapName = mapName,
                            LastUpdated = DateTime.UtcNow
                        };
                        dbContext.BotStats.Add(botRecord);
                        // Logger.LogInformation("Added new bot record for {BotName} on map {MapName}", botName, mapName);
                    }
                    else
                    {
                        // Bot record exists, increment kills
                        botRecord.Kills++;
                        botRecord.LastUpdated = DateTime.UtcNow;
                    }
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error updating bot stats for Bot '{BotName}' on map {MapName}", botName, mapName);
                }
            });
            // Continue processing (e.g., player attacker logic) if needed
        }
        // --- Handle Player Attacker (Keep existing logic) ---
        else if (attacker.AuthorizedSteamID != null)
        {
            
            Task.Run(async () => {
            try
                {
                    await using var dbContext = new PluginDbContext(_dbPath);

                    var playerRecord = await dbContext.Players.FindAsync(steamId);

                    if (playerRecord == null)
                    {
                        // Player doesn't exist, add new record
                        playerRecord = new PlayerRecord { SteamId = (ulong)steamId, PlayerName = playerName, Kills = 1 };
                        dbContext.Players.Add(playerRecord);
                    }
                    else
                    {
                        // Player exists, increment kills
                        playerRecord.Kills++;
                    }

                    await dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error updating player kills for SteamID {SteamId}", steamId);
                }
            });
        }

        return HookResult.Continue;
    }

    [ConsoleCommand("css_botkills", "Shows kill counts for bots on the current map from DB.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBotKillsCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        string? mapName = _currentMapName; // Get current map name

        if (string.IsNullOrEmpty(mapName))
        {
            ReplyToCommand(caller, "Current map name not available to query bot stats.");
            return;
        }

        // Query DB in background, reply on main thread
        Task.Run(async () =>
        {
            List<BotRecord> botRecords = new List<BotRecord>();
            bool dbError = false;
            try
            {
                await using var dbContext = new PluginDbContext(_dbPath);
                botRecords = await dbContext.BotStats
                    .Where(b => b.MapName == mapName) // Filter by current map
                    .OrderByDescending(b => b.Kills)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error retrieving bot stats from database for map {MapName}", mapName);
                dbError = true;
            }

            // Reply on the main thread
            Server.NextFrame(() =>
            {
                if (dbError)
                {
                    ReplyToCommand(caller, "Error retrieving bot stats from database.");
                    return;
                }

                if (!botRecords.Any())
                {
                    ReplyToCommand(caller, $"No bot stats recorded in DB for current map ({mapName}).");
                    return;
                }

                ReplyToCommand(caller, $"--- Bot Kills on {mapName} (from DB) ---");
                foreach (var botRecord in botRecords)
                {
                    ReplyToCommand(caller, $"{botRecord.BotName}: {botRecord.Kills} Kills");
                }
                ReplyToCommand(caller, "--------------------------------------");
            });
        });
    }

    // Keep your ReplyToCommand helper function
    private void ReplyToCommand(CCSPlayerController? caller, string message)
    {
        if (caller == null) { Console.WriteLine(message); }
        else { caller.PrintToChat(message); }
    }



    /// <summary>
    /// get kills for a player using the console command.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="commandInfo"></param>
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
                await using var dbContext = new PluginDbContext(_dbPath);

                var playerRecord = await dbContext.Players
                                            .AsNoTracking() // AsNoTracking for read-only queries for better performance
                                            .FirstOrDefaultAsync(p => p.SteamId == steamId);

                kills = playerRecord?.Kills ?? 0;
            }
            catch (Exception ex)
            {

                Logger.LogError(ex, "Error retrieving player kills for SteamID {SteamId}", steamId);

                Server.NextFrame(() => player.PrintToChat("Error retrieving kills data."));
                return; // Exit the task
            }


            // Print the result - must run on game thread
            Server.NextFrame(() => { player.PrintToChat($"Kills: {kills}"); });
        });
    }

    /// <summary>
    /// Listens for chat commands and opens the menu when the command is recognized.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="commandInfo"></param>
    /// <returns></returns>
    private HookResult Listener_SayChat(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid || player.IsBot)
        {
            return HookResult.Continue;
        }

        string? commandTrigger = (commandInfo.ArgCount > 1) ? commandInfo.GetArg(1)?.Trim() : null;

        // Logger.LogInformation($"[ChatListener] Player: {player.PlayerName}, Arg1: '{commandTrigger}'"); // Optional Debug log

        if (commandTrigger != null && commandTrigger.Equals("!menu", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation($"[ChatListener] '!menu' command attempt recognized for player {player.PlayerName}."); // Optional Debug log

            // --- Open the CenterHtmlMenu ---
            OpenMainMenu(player);

            // --- Stop the original chat command ---
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    /// <summary>
    /// Opens the main menu for the player.
    /// </summary>
    /// <param name="player"></param>
    private void OpenMainMenu(CCSPlayerController player)
    {
        if (player == null || !player.IsValid) return;

        var menu = new CenterHtmlMenu("Main Menu", this);
        menu.AddMenuOption("Show My Kills", HandleMenuSelection);
        //menu.AddMenuOption("Placeholder Option", HandleMenuSelection);


        MenuManager.OpenCenterHtmlMenu(this, player, menu);
        Logger.LogInformation($"[Menu] Opened main menu for {player.PlayerName}");
    }

    /// <summary>
    /// Takes the selected menu option and performs the corresponding action.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="option"></param>
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


    /// <summary>
    /// retrieves player kills from the database and prints it to the player.
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
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
                                           .AsNoTracking() // AsNoTracking for read-only queries for better performance
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