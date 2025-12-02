using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Database;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Scheduler;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Translation;
using SwiftlyS2.Shared.Players;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using System.Threading;
using System.Reflection;
using System.IO;

namespace MatchZy
{
    [PluginMetadata(
        Id = "matchzy",
        Version = "0.8.15",
        Name = "MatchZy",
        Author = "WD- (https://github.com/shobhit-pathak/)",
        Description = "A plugin for running and managing CS2 practice/pugs/scrims/matches!",
        Website = "https://github.com/shobhit-pathak/MatchZy"
    )]
    public partial class MatchZy : BasePlugin
    {
        // Access services through Core property
        public ICommandService CommandService => Core.Command;
        public IGameEventService GameEventService => Core.GameEvent;
        public IDatabaseService DatabaseService => Core.Database;
        public ISchedulerService SchedulerService => Core.Scheduler;
        public ILogger<MatchZy> Logger => Core.LoggerFactory.CreateLogger<MatchZy>();
        public ILocalizer Localizer => Core.Localizer;

        public MatchZy(ISwiftlyCore core) : base(core)
        {
        }

        // Helper method to access plugin metadata
        private static PluginMetadata? GetMetadata()
        {
            return typeof(MatchZy).GetCustomAttribute<PluginMetadata>();
        }

        // Helper method to get resource file path
        // Resources are copied to the plugin output directory during build
        private string GetResourcePath(string relativePath)
        {
            // Use Core.PluginPath which is the plugin's installation directory
            // Resources are copied to the plugin directory during build
            string pluginDirectory = Core.PluginPath;
            
            // Resources are in the plugin directory
            string resourcePath = Path.Combine(pluginDirectory, "resources", relativePath);
            return resourcePath;
        }

        // Helper method to get config file path (for runtime data, use PluginDataDirectory)
        private string GetConfigPath(string fileName)
        {
            // For runtime config files (like database.json, savednades.json), use PluginDataDirectory
            // These files can be modified at runtime
            return Path.Combine(Core.PluginDataDirectory, fileName);
        }

        // chatPrefix is now computed from metadata, but can be overridden if needed
        private string? _chatPrefix;
        public string chatPrefix
        {
            get => _chatPrefix ?? $"[{ChatColors.Green}{GetMetadata()?.Name ?? "MatchZy"}{ChatColors.Default}]";
            set => _chatPrefix = value;
        }
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        public bool isWarmup = false;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;

        public bool mapReloadRequired = false;

        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
        };

        bool isPauseCommandForTactical = false;

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, IPlayer> playerData = new Dictionary<int, IPlayer>();

        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();

        // Timers
        public CancellationTokenSource? unreadyPlayerMessageTimer = null;
        public CancellationTokenSource? sideSelectionMessageTimer = null;
        public CancellationTokenSource? pausedStateTimer = null;
        public CancellationTokenSource? coachKillTimer = null;
        public CancellationTokenSource? collisionGroupTimer = null;
        public CancellationTokenSource? vetoStateTimer = null;

        // Each message is kept in chat display for ~13 seconds, hence setting default chat timer to 13 seconds.
        // Configurable using matchzy_chat_messages_timer_delay <seconds>
        public int chatTimerDelay = 13;

        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2; // Number of ready players required start the match. If set to 0, all connected players have to ready-up to start the match.
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;

        public bool isPlayOutEnabled = false;

        public bool playerHasTakenDamage = false;

        // User command - action map
        // public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;

        // SQLite/MySQL Database 
        private Database database = new();
    
        public override void Load(bool hotReload) {
            LoadAdmins();

            // Initialize permissions.jsonc configuration (可选，权限也可以完全使用 SwiftlyS2 全局 permissions.jsonc 来管理)
            Core.Configuration
                .InitializeJsonWithModel<PermissionsConfiguration>("permissions.jsonc", "Permissions");

            // Initialize database using SwiftlyS2 DatabaseService
            database.SetLogger(Logger);
            database.SetDatabaseService(DatabaseService);
            database.InitializeDatabase(Core.PluginDataDirectory, Core.CSGODirectory);

            // This sets default config ConVars
            // 使用 cfg/MatchZy/config.cfg 作为模板（如有 data/matchzy/config/config.cfg 则优先生效）
            string configExecPath = EnsureCfgInGameCfgDirectory("config.cfg");
            Logger.LogInformation($"[Load] Executing config CFG via exec {configExecPath}");
            Core.Engine.ExecuteCommand($"exec {configExecPath}");

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

            // Register all event handlers
            RegisterEventHandlers();

            if (!hotReload) {
                AutoStart();
            } else {
                // Plugin should not be reloaded while a match is live (this would messup with the match flags which were set)
                // Only hot-reload the plugin if you are testing something and don't want to restart the server time and again.
                UpdatePlayersMap();
                AutoStart();
            }

            var metadata = GetMetadata();
            if (metadata != null)
            {
                Console.WriteLine($"[{metadata.Name} {metadata.Version} LOADED] {metadata.Name} by {metadata.Author}");
                if (!string.IsNullOrEmpty(metadata.Website))
                {
                    Console.WriteLine($"[{metadata.Name}] Website: {metadata.Website}");
                }
                if (!string.IsNullOrEmpty(metadata.Description))
                {
                    Console.WriteLine($"[{metadata.Name}] {metadata.Description}");
                }
            }
            else
            {
                var fallbackMetadata = GetMetadata();
                Console.WriteLine($"[{fallbackMetadata?.Name ?? "MatchZy"} {fallbackMetadata?.Version ?? "0.8.15"} LOADED] MatchZy by WD- (https://github.com/shobhit-pathak/)");
            }
        }
        
        public override void Unload()
        {
            // Cancel all timers
            unreadyPlayerMessageTimer?.Cancel();
            sideSelectionMessageTimer?.Cancel();
            pausedStateTimer?.Cancel();
            coachKillTimer?.Cancel();
            collisionGroupTimer?.Cancel();
            vetoStateTimer?.Cancel();
            
            // Unhook all event handlers
            // Note: SwiftlyS2 automatically cleans up event handlers when plugin unloads
        }

        private void RegisterEventHandlers()
        {
            // Register game event handlers using SwiftlyS2 GameEventService
            GameEventService.HookPost<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
            GameEventService.HookPost<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            GameEventService.HookPre<EventCsWinPanelRound>(EventCsWinPanelRoundHandler);
            GameEventService.HookPost<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            GameEventService.HookPost<EventRoundStart>(EventRoundStartHandler);
            GameEventService.HookPost<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            GameEventService.HookPost<EventPlayerGivenC4>(EventPlayerGivenC4);
            GameEventService.HookPre<EventPlayerDeath>(EventPlayerDeathPreHandler);
            
            // Register EventPlayerTeam handlers
            GameEventService.HookPre<EventPlayerTeam>((@event) => {
                IPlayer? player = @event.UserIdPlayer;
                if (player == null || !player.IsValid) return HookResult.Continue;

                if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player)) {
                    @event.Silent = true;
                    return HookResult.Handled;
                }
                return HookResult.Continue;
            });

            GameEventService.HookPost<EventPlayerTeam>((@event) => {
                if (!isMatchSetup && !isVeto) return HookResult.Continue;

                IPlayer? player = @event.UserIdPlayer;
                if (player == null || !player.IsValid) return HookResult.Continue;

                if (player.IsFakeClient || player.RequiredController.IsHLTV)
                {
                    return HookResult.Continue;
                }

                TeamEnum playerTeam = GetPlayerTeam(player);
                SwitchPlayerTeam(player, playerTeam);

                return HookResult.Continue;
            });

            // Register EventRoundEnd handlers
            GameEventService.HookPre<EventRoundEnd>((@event) => {
                if (!isKnifeRound) return HookResult.Continue;

                DetermineKnifeWinner();
                @event.Winner = (byte)knifeWinner;
                byte finalEvent = 10;
                if (knifeWinner == 3) {
                    finalEvent = 8;
                } else if (knifeWinner == 2) {
                    finalEvent = 9;
                }
                @event.Reason = finalEvent;
                isSideSelectionPhase = true;
                isKnifeRound = false;
                StartAfterKnifeWarmup();

                return HookResult.Handled;
            });

            GameEventService.HookPost<EventRoundEnd>((@event) => {
                try 
                {
                    if (isDryRun)
                    {
                        StartPracticeMode();
                        isDryRun = false;
                        return HookResult.Continue;
                    }
                    if (!isMatchLive) return HookResult.Continue;
                    HandlePostRoundEndEvent(@event);
                    return HookResult.Continue;
                }
                catch (Exception e)
                {
                    Logger.LogError($"[EventRoundEnd FATAL] An error occurred: {e.Message}");
                    return HookResult.Continue;
                }
            });

            // Register EventPlayerDeath handler
            GameEventService.HookPost<EventPlayerDeath>((@event) => {
                // Setting money back to 16000 when a player dies in warmup
                IPlayer? player = @event.UserIdPlayer;
                if (!isWarmup) return HookResult.Continue;
                if (player == null || !player.IsValid) return HookResult.Continue;
                if (player.RequiredController.InGameMoneyServices != null) 
                    player.RequiredController.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            // Register EventPlayerHurt handler
            GameEventService.HookPost<EventPlayerHurt>((@event) => {
                IPlayer? attacker = @event.Attacker > 0 ? Core.PlayerManager.GetPlayer(@event.Attacker) : null;
                IPlayer? victim = @event.UserIdPlayer;

                if (attacker == null || !attacker.IsValid || victim == null || !victim.IsValid) 
                    return HookResult.Continue;

                if (isPractice && victim.IsFakeClient)
                {
                    int damage = @event.DmgHealth;
                    int postDamageHealth = @event.Health;
                    PrintToPlayerChat(attacker, Localizer["matchzy.pracc.damage", damage, victim.RequiredController.PlayerName, postDamageHealth]);
                    return HookResult.Continue;
                }

                if (!attacker.IsValid || attacker.IsFakeClient && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
                    return HookResult.Continue;
                    
                if (matchStarted && victim.RequiredController.TeamNum != attacker.RequiredController.TeamNum) 
                {
                    int targetId = victim.PlayerID;
                    UpdatePlayerDamageInfo(@event, targetId);
                    if (attacker != victim) playerHasTakenDamage = true;
                }

                return HookResult.Continue;
            });

            // Register EventPlayerBlind handler
            GameEventService.HookPost<EventPlayerBlind>((@event) => {
                IPlayer? player = @event.UserIdPlayer;
                IPlayer? attacker = @event.Attacker > 0 ? Core.PlayerManager.GetPlayer(@event.Attacker) : null;
                if (!isPractice) return HookResult.Continue;

                if (player == null || !player.IsValid || attacker == null || !attacker.IsValid) 
                    return HookResult.Continue;

                if (attacker != null && attacker.IsValid)
                {
                    double roundedBlindDuration = Math.Round(@event.BlindDuration, 2);
                    PrintToPlayerChat(attacker, Localizer["matchzy.pracc.blind", player.RequiredController.PlayerName, roundedBlindDuration]);
                }
                
                int playerUserId = @event.UserId;
                if (noFlashList.Contains(playerUserId))
                {
                    SchedulerService.NextTick(() => KillFlashEffect(player));
                }

                return HookResult.Continue;
            });

            // Register grenade detonate handlers
            GameEventService.HookPost<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            GameEventService.HookPost<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            GameEventService.HookPost<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            GameEventService.HookPost<EventMolotovDetonate>(EventMolotovDetonateHandler);
            GameEventService.HookPost<EventDecoyStarted>(EventDecoyDetonateHandler);

            // Register EventPlayerSpawn handler for practice mode
            GameEventService.HookPost<EventPlayerSpawn>(OnPlayerSpawn);

            // Register entity spawn handler - Note: SwiftlyS2 may handle this differently
            // We'll need to hook into entity system events if available
            // For now, OnEntitySpawnedHandler is called from PracticeMode.cs when needed
        }

        // IsPlayerAdmin and SendPlayerNotAdminMessage are defined in Utility.cs
    }
}
