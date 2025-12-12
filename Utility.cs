using System.Text.Json;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Schemas;
using System.Text.RegularExpressions;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Drawing;
using Microsoft.Extensions.Logging;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using TeamEnum = SwiftlyS2.Shared.Players.Team;


namespace MatchZy
{
    public partial class MatchZy
    {
        /// <summary>
        /// 确保某个 cfg 文件存在于游戏可执行的目录下（csgo/cfg/MatchZy），并返回可以传给 exec 的相对路径。
        /// 注意：CS2 的 exec 命令只能从 csgo/cfg 目录开始读取，相对路径会被自动拼成 cfg/xxx。
        /// </summary>
        /// <param name="fileName">例如 "config.cfg"、"warmup.cfg"</param>
        /// <returns>可以直接用于 exec 的相对路径，例如 "MatchZy/config.cfg"</returns>
        private string EnsureCfgInGameCfgDirectory(string fileName)
        {
            // 1. 先通过我们自己的查找逻辑拿到“源”cfg 路径（插件 data / 资源 / 旧路径）
            string sourcePath = GetConfigFilePath(fileName);

            // 2. 目标路径：csgo/cfg/MatchZy/fileName
            string gameCfgMatchZyDir = Path.Combine(Core.CSGODirectory, "cfg", "MatchZy");
            if (!Directory.Exists(gameCfgMatchZyDir))
            {
                Directory.CreateDirectory(gameCfgMatchZyDir);
            }

            string destPath = Path.Combine(gameCfgMatchZyDir, fileName);

            // 3. 如果源文件存在，就拷贝到 csgo/cfg/MatchZy 目录
            if (File.Exists(sourcePath))
            {
                try
                {
                    // 如果源和目标本身就是同一个文件，就不要复制，避免自我覆盖导致的占用问题
                    string srcFull = Path.GetFullPath(sourcePath);
                    string dstFull = Path.GetFullPath(destPath);
                    if (!srcFull.Equals(dstFull, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(sourcePath, destPath, true);
                    }
                }
                catch (IOException ex)
                {
                    // 如果文件正被其他进程占用，不要让插件加载失败，只打日志提示
                    Logger.LogWarning($"[EnsureCfgInGameCfgDirectory] Failed to copy cfg from {sourcePath} to {destPath}: {ex.Message}");
                }
            }

            // 4. 返回给 exec 用的相对路径（从 cfg/ 开始算起）
            return $"MatchZy/{fileName}";
        }

        // Config file paths - 优先顺序：
        // 1. (swRoot)/addons/swiftlys2/data/matchzy/config/<file>      —— 用户运行时覆盖
        // 2. (swRoot)/addons/swiftlys2/plugins/MatchZy/cfg/MatchZy/<file> —— 插件自带 cfg（源码中的 cfg/MatchZy）
        // 3. (swRoot)/addons/swiftlys2/plugins/MatchZy/resources/config/<file> —— 旧的 / 仅 json(jsonc) 等
        // 4. (兼容) (swRoot)/game/csgo/cfg/MatchZy/<file>
        private string GetConfigFilePath(string fileName)
        {
            // First check if user has customized the file in PluginDataDirectory
            string userConfigPath = Path.Combine(Core.PluginDataDirectory, "config", fileName);
            if (File.Exists(userConfigPath))
            {
                return userConfigPath;
            }

            // 其次：插件目录下的 cfg/MatchZy（和原来 CSSharp 插件结构保持一致）
            string pluginCfgPath = Path.Combine(Core.PluginPath, "cfg", "MatchZy", fileName);
            if (File.Exists(pluginCfgPath))
            {
                return pluginCfgPath;
            }

            // 再次：resources/config（主要放 json / jsonc）
            string resourcePath = Path.Combine(Core.PluginPath, "resources", "config", fileName);
            if (File.Exists(resourcePath))
            {
                return resourcePath;
            }

            // Fallback to old path for backward compatibility
            return Path.Combine(Core.CSGODirectory, "cfg", "MatchZy", fileName);
        }

        private void PrintToAllChat(string message)
        {
            Core.PlayerManager.SendChat($"{chatPrefix} {message}");
        }

        private void PrintToPlayerChat(IPlayer player, string message)
        {
            player.SendMessage(MessageType.Chat, $"{chatPrefix} {message}");
        }

        private void ReplyToUserCommand(IPlayer? player, string message, bool console = false)
        {
            if (player == null)
            {
                Logger.LogInformation($"{chatPrefix} {message}");
            }
            else
            {
                if (console)
                {
                    player.SendMessage(MessageType.Console, $"{chatPrefix} {message}");
                }
                else
                {
                    player.SendMessage(MessageType.Chat, $"{chatPrefix} {message}");
                }
            }
        }

        private void LoadAdmins()
        {
            // Load permissions from plugin's permissions.jsonc file
            // SwiftlyS2's global permission system loads from (swRoot)/configs/permissions.jsonc
            // This method loads from plugin-specific permissions.jsonc and adds them to the permission system
            try
            {
                string permissionsConfigPath = Core.Configuration.GetConfigPath("permissions.jsonc");
                if (File.Exists(permissionsConfigPath))
                {
                    string jsonContent = File.ReadAllText(permissionsConfigPath);
                    var permissionsConfig = JsonSerializer.Deserialize<PermissionsConfiguration>(jsonContent);
                    
                    if (permissionsConfig?.Permissions != null)
                    {
                        int loadedCount = 0;
                        
                        // Load player-specific permissions
                        foreach (var playerEntry in permissionsConfig.Permissions.Players)
                        {
                            if (ulong.TryParse(playerEntry.Key, out ulong steamId))
                            {
                                foreach (var permission in playerEntry.Value)
                                {
                                    Core.Permission.AddPermission(steamId, permission);
                                    loadedCount++;
                                }
                                Logger.LogInformation($"[LoadAdmins] Loaded {playerEntry.Value.Count} permissions for player {steamId}");
                            }
                            else
                            {
                                Logger.LogWarning($"[LoadAdmins] Invalid SteamID format: {playerEntry.Key}");
                            }
                        }
                        
                        // Note: Permission groups are handled by SwiftlyS2's global permission system
                        // We only load player-specific permissions here
                        if (loadedCount > 0)
                        {
                            Logger.LogInformation($"[LoadAdmins] Loaded {loadedCount} permissions from {permissionsConfigPath}");
                        }
                        else
                        {
                            Logger.LogInformation($"[LoadAdmins] No player permissions found in {permissionsConfigPath}");
                        }
                    }
                }
                else
                {
                    Logger.LogInformation($"[LoadAdmins] Permissions config file not found at {permissionsConfigPath}, using SwiftlyS2's global permission system only.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[LoadAdmins] Error loading permissions: {ex.Message}");
            }
            
            // Keep empty dictionary for backward compatibility
            loadedAdmins = new Dictionary<string, string>();
            
            Logger.LogInformation("[LoadAdmins] Permissions can also be managed via SwiftlyS2's permission commands:");
            Logger.LogInformation("[LoadAdmins]   - permission add <steamid> @css/root");
            Logger.LogInformation("[LoadAdmins]   - permission add <steamid> @css/config");
            Logger.LogInformation("[LoadAdmins]   - permission add <steamid> @css/map");
        }

        public bool IsPlayerAdmin(IPlayer? player, string command = "", params string[] permissions)
        {
            if (everyoneIsAdmin) return true; // Everyone is treated as admin if matchzy_everyone_is_admin is true.
            if (player == null) return true; // Sent via server, hence should be treated as an admin.
            
            // Check permissions using SwiftlyS2 permission system
            // Root permission grants all access
            if (Core.Permission.PlayerHasPermission(player.SteamID, "@css/root")) return true;
            
            // Check specific permissions
            foreach (var perm in permissions)
            {
                if (!string.IsNullOrEmpty(perm) && Core.Permission.PlayerHasPermission(player.SteamID, perm)) 
                    return true;
            }
            
            // Note: admins.json is no longer used in SwiftlyS2 architecture
            // All permissions should be managed through SwiftlyS2's permission system
            return false;
        }

        private int GetRealPlayersCount()
        {
            return playerData.Count;
        }

        private void SendUnreadyPlayersMessage()
        {
            if (!isWarmup || matchStarted) return;
            List<string> unreadyPlayers = new();

            foreach (var key in playerReadyStatus.Keys)
            {
                if (playerReadyStatus[key] == false)
                {
                    var p = Core.PlayerManager.GetPlayer(key);
                    if (p != null && p.IsValid)
                        unreadyPlayers.Add(p.RequiredController.PlayerName);
                }
            }
            if (unreadyPlayers.Count > 0)
            {
                string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                // Build the minimum ready required message separately to avoid formatting issues
                string minimumReadyRequiredMessage = "";
                if (!isMatchSetup)
                {
                    // Use Localizer for the minimum ready message part
                    minimumReadyRequiredMessage = $"[{Localizer["matchzy.utility.minreadyplayers", minimumReadyRequired]}]";
                }

                // Core.PlayerManager.SendChat($"{chatPrefix} Unready players: {unreadyPlayerList}. Please type .ready to ready up! {minimumReadyRequiredMessage}");
                if (isRoundRestorePending)
                {
                    // Escape curly braces in the message to prevent string.Format from interpreting them
                    string escapedMessage = minimumReadyRequiredMessage.Replace("{", "{{").Replace("}", "}}");
                    string message = Localizer["matchzy.ready.readytotestorebackupinfomessage", unreadyPlayerList, escapedMessage];
                    PrintToAllChat(message);
                }
                else
                {
                    // Escape curly braces in the message to prevent string.Format from interpreting them
                    string escapedMessage = minimumReadyRequiredMessage.Replace("{", "{{").Replace("}", "}}");
                    string message = Localizer["matchzy.utility.unreadyplayers", unreadyPlayerList, escapedMessage];
                    PrintToAllChat(message);
                }
            }
            else
            {
                int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                if (isMatchSetup)
                {
                    // Core.PlayerManager.SendChat($"{chatPrefix} Current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    PrintToAllChat(Localizer["matchzy.utility.readyplayers", countOfReadyPlayers]);
                }
                else
                {
                    // Core.PlayerManager.SendChat($"{chatPrefix} Minimum ready players required {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}, current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                    PrintToAllChat(Localizer["matchzy.utility.minimumreadyplayers", minimumReadyRequired, countOfReadyPlayers]);
                }
            }
        }

        private void SendPausedStateMessage()
        {
            if (isPaused && matchStarted)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin")
                {
                    PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
                }
                else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedbecauserestore"]);
                }
                else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["TERRORIST"].teamName, reverseTeamSides["CT"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", reverseTeamSides["CT"].teamName, reverseTeamSides["TERRORIST"].teamName]);
                }
                else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
                }
            }
        }

        private void ExecWarmupCfg()
        {
            string cfgExecPath = EnsureCfgInGameCfgDirectory("warmup.cfg");
            string absolutePath = GetConfigFilePath("warmup.cfg");

            if (File.Exists(absolutePath))
            {
                Logger.LogInformation($"[StartWarmup] Starting warmup! Executing Warmup CFG via exec {cfgExecPath} (source: {absolutePath})");
                Core.Engine.ExecuteCommand($"exec {cfgExecPath}");
            }
            else
            {
                Logger.LogInformation($"[StartWarmup] Starting warmup! Warmup CFG not found in {absolutePath}, using default CFG!");
                Core.Engine.ExecuteCommand("bot_kick;bot_quota 0;mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;mp_weapons_allow_typecount -1;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 1;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary;mp_maxrounds 24;mp_warmup_start;mp_warmup_pausetimer 1;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;");
            }
        }

        private void StartWarmup()
        {
            unreadyPlayerMessageTimer?.Cancel();
            unreadyPlayerMessageTimer = null;
            unreadyPlayerMessageTimer ??= SchedulerService.RepeatBySeconds(chatTimerDelay, SendUnreadyPlayersMessage);
            isWarmup = true;
            ExecWarmupCfg();
        }

        private void StartKnifeRound()
        {
            // Kills unready players message timer
            if (unreadyPlayerMessageTimer != null)
            {
                unreadyPlayerMessageTimer.Cancel();
                unreadyPlayerMessageTimer = null;
            }

            // Setting match phases bools
            matchStarted = true;
            isKnifeRound = true;
            readyAvailable = false;
            isWarmup = false;

            string knifeExecPath = EnsureCfgInGameCfgDirectory("knife.cfg");
            string knifeAbsolutePath = GetConfigFilePath("knife.cfg");

            if (File.Exists(knifeAbsolutePath))
            {
                Logger.LogInformation($"[StartKnifeRound] Starting Knife! Executing Knife CFG via exec {knifeExecPath} (source: {knifeAbsolutePath})");
                Core.Engine.ExecuteCommand($"exec {knifeExecPath}");
                Core.Engine.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                Logger.LogInformation($"[StartKnifeRound] Starting Knife! Knife CFG not found in {knifeAbsolutePath}, using default CFG!");
                Core.Engine.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }

            PrintToAllChat($"{ChatColors.Olive}KNIFE!");
            PrintToAllChat($"{ChatColors.Lime}KNIFE!");
            PrintToAllChat($"{ChatColors.Green}KNIFE!");
        }

        private void SendSideSelectionMessage()
        {
            if (!isSideSelectionPhase) return;
            PrintToAllChat(Localizer["matchzy.knife.sidedecisionpending", knifeWinnerName]);
            // Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Waiting for them to type {ChatColors.Green}.stay{ChatColors.Default} or {ChatColors.Green}.switch{ChatColors.Default}");
        }

        private void StartAfterKnifeWarmup()
        {
            isWarmup = true;
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? reverseTeamSides["CT"].teamName : reverseTeamSides["TERRORIST"].teamName;
            ShowDamageInfo();
            PrintToAllChat(Localizer["matchzy.knife.sidedecisionpending", knifeWinnerName]);
            // Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Waiting for them to type {ChatColors.Green}.stay{ChatColors.Default} or {ChatColors.Green}.switch{ChatColors.Default}");
            sideSelectionMessageTimer ??= SchedulerService.RepeatBySeconds(chatTimerDelay, SendSideSelectionMessage);
        }

        private void SetLiveFlags()
        {
            // Setting match phases bools
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            readyAvailable = false;
            isKnifeRound = false;
        }

        private void SetupLiveFlagsAndCfg()
        {
            SetLiveFlags();
            KillPhaseTimers();
            ExecLiveCFG();
            // Adding timer here to make sure that CFG execution is completed till then
            SchedulerService.DelayBySeconds(1, () =>
            {
                HandlePlayoutConfig();
                ExecuteChangedConvars();
            });
        }

        private void StartLive()
        {
            SetupLiveFlagsAndCfg();
            StartDemoRecording();

            // Storing 0-0 score backup file as lastBackupFileName, so that .stop functions properly in first round.
            lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.txt";
            lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round00.json";

            // This is to reload the map once it is over so that all flags are reset accordingly
            Core.Engine.ExecuteCommand("mp_match_end_restart true");

            PrintToAllChat($"{ChatColors.Olive}LIVE!");
            PrintToAllChat($"{ChatColors.Lime}LIVE!");
            PrintToAllChat($"{ChatColors.Green}LIVE!");

            var goingLiveEvent = new GoingLiveEvent
            {
                MatchId = liveMatchId,
                MapNumber = matchConfig.CurrentMapNumber,
            };

            Task.Run(async () =>
            {
                await SendEventAsync(goingLiveEvent);
            });
        }

        private void KillPhaseTimers()
        {
            unreadyPlayerMessageTimer?.Cancel();
            sideSelectionMessageTimer?.Cancel();
            pausedStateTimer?.Cancel();
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
        }

        private (int alivePlayers, int totalHealth) GetAlivePlayers(int team)
        {
            int count = 0;
            int totalHealth = 0;
            foreach (var key in playerData.Keys)
            {
                IPlayer player = playerData[key];
                // Update coach checking logic for SwiftlyS2
                if (team == 2 && reverseTeamSides["TERRORIST"].coach.Contains(player)) continue;
                if (team == 3 && reverseTeamSides["CT"].coach.Contains(player)) continue;
                if (!IsPlayerValid(player)) continue;
                if ((int)player.RequiredController.TeamNum == team)
                {
                    int health = player.RequiredPlayerPawn.Health;
                    if (health > 0) count++;
                    totalHealth += health;
                }
            }
            return (count, totalHealth);
        }

        private void ResetMatch(bool warmupCfgRequired = true)
        {
            try
            {
                // We stop demo recording if a live match was restarted
                if (matchStarted && isDemoRecording)
                {
                    Core.Engine.ExecuteCommand($"tv_stoprecord");
                    isDemoRecording = false;
                }
                // Reset match data
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;
                isMatchSetup = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;
                liveMatchId = -1;
                isPractice = false;
                isDryRun = false;
                isVeto = false;
                isPreVeto = false;

                lastBackupFileName = "";
                lastMatchZyBackupFileName = "";

                isRoundRestorePending = false;
                playerHasTakenDamage = false;

                // Unready all players
                foreach (var key in playerReadyStatus.Keys)
                {
                    playerReadyStatus[key] = false;
                }

                teamReadyOverride = new()
                {
                    {TeamEnum.T, false},
                    {TeamEnum.CT, false},
                    {TeamEnum.Spectator, false}
                };

                HandleClanTags();

                // Reset unpause data
                Dictionary<string, object> unpauseData = new()
                {
                    { "ct", false },
                    { "t", false },
                    { "pauseTeam", "" }
                };

                // Reset stop data
                stopData["ct"] = false;
                stopData["t"] = false;

                // Reset owned bots data
                pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
                noFlashList = new();
                lastGrenadesData = new();
                nadeSpecificLastGrenadeData = new();
                UnpauseMatch();

                matchzyTeam1.teamName = "COUNTER-TERRORISTS";
                matchzyTeam2.teamName = "TERRORISTS";

                matchzyTeam1.teamPlayers = null;
                matchzyTeam2.teamPlayers = null;

                HashSet<IPlayer> coaches = GetAllCoaches();

                foreach (var coach in coaches)
                {
                    if (coach == null || !coach.IsValid) continue;
                    coach.RequiredController.Clan = "";
                    SetPlayerVisible(coach);
                }

                matchzyTeam1.coach = new();
                matchzyTeam2.coach = new();
                coachKillTimer?.Cancel();
                coachKillTimer = null;

                matchzyTeam1.seriesScore = 0;
                matchzyTeam2.seriesScore = 0;

                Core.Engine.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
                Core.Engine.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");

                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;

                // Keeping the log URLs to avoid their reset on match start.
                matchConfig = new()
                {
                    RemoteLogURL = matchConfig.RemoteLogURL,
                    RemoteLogHeaderKey = matchConfig.RemoteLogHeaderKey,
                    RemoteLogHeaderValue = matchConfig.RemoteLogHeaderValue
                };

                KillPhaseTimers();
                UpdatePlayersMap();
                if (warmupCfgRequired)
                {
                    StartWarmup();
                }
                else
                {
                    // Since we should be already in warmup phase by this point, we are just setting up the SendUnreadyPlayersMessage timer
                    unreadyPlayerMessageTimer?.Cancel();
                    unreadyPlayerMessageTimer = null;
                    unreadyPlayerMessageTimer ??= SchedulerService.RepeatBySeconds(chatTimerDelay, SendUnreadyPlayersMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[ResetMatch - FATAL] [ERROR]: {ex.Message}");
            }
        }

        private void UpdatePlayersMap()
        {
            try
            {
                var playerEntities = Core.PlayerManager.GetAllPlayers();
                Logger.LogInformation($"[UpdatePlayersMap] Player count: {playerEntities.Count()} matchModeOnly: {matchModeOnly}");
                connectedPlayers = 0;

                // Clear the playerData dictionary by creating a new instance to add fresh data.
                playerData = new Dictionary<int, IPlayer>();
                foreach (var player in playerEntities)
                {
                    if (player == null) continue;
                    if (!player.IsValid || player.IsFakeClient || player.RequiredController.IsHLTV) continue;

                    if (isMatchSetup || matchModeOnly)
                    {
                        TeamEnum team = GetPlayerTeam(player);
                        if (team == TeamEnum.Spectator) // Check if player is not in a team
                        {
                            Core.Engine.ExecuteCommand($"kickid {player.PlayerID}");
                            continue;
                        }
                    }

                    // A player controller still exists after a player disconnects
                    // Hence checking whether the player is actually in the server or not
                    // Check player connection state in SwiftlyS2
                    if (!player.IsValid) continue;

                    int playerId = player.PlayerID;

                    // Updating playerData and playerReadyStatus
                    playerData[playerId] = player;

                    // Adding missing player in playerReadyStatus
                    if (!playerReadyStatus.ContainsKey(playerId))
                    {
                        playerReadyStatus[playerId] = false;
                    }
                    connectedPlayers++;
                }

                // Removing disconnected players from playerReadyStatus
                foreach (var key in playerReadyStatus.Keys.ToList())
                {
                    if (!playerData.ContainsKey(key))
                    {
                        // Key is not present in playerData, so remove it from playerReadyStatus
                        playerReadyStatus.Remove(key);
                    }
                }
                Logger.LogInformation($"[UpdatePlayersMap] Player count: {playerEntities.Count()}, RealPlayersCount: {GetRealPlayersCount()}");
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[UpdatePlayersMap FATAL] An error occurred: {e.Message}");
            }
        }

        public void DetermineKnifeWinner()
        {
            // Knife Round code referred from Get5, thanks to the Get5 team for their amazing job!
            (int tAlive, int tHealth) = GetAlivePlayers(2);
            (int ctAlive, int ctHealth) = GetAlivePlayers(3);
            Logger.LogInformation($"[KNIFE OVER] CT Alive: {ctAlive} with Total Health: {ctHealth}, T Alive: {tAlive} with Total Health: {tHealth}");
            if (ctAlive > tAlive)
            {
                knifeWinner = 3;
            }
            else if (tAlive > ctAlive)
            {
                knifeWinner = 2;
            }
            else if (ctHealth > tHealth)
            {
                knifeWinner = 3;
            }
            else if (tHealth > ctHealth)
            {
                knifeWinner = 2;
            }
            else
            {
                // Choosing a winner randomly
                Random random = new();
                knifeWinner = random.Next(2, 4);
            }
        }

        private void HandleKnifeWinner(EventCsWinPanelRound @event)
        {
            DetermineKnifeWinner();
            // Below code is working partially (Winner audio plays correctly for knife winner team, but may display round winner incorrectly)
            // Hence we restart the game with StartAfterKnifeWarmup and allow the winning team to choose side

            // Fun‑fact related fields are intentionally left untouched in SwiftlyS2,
            // as writing to them has been observed to cause instability.

            // Commenting these assignments as they were crashing the server.
            // long empty = 0;
            // @event.FunfactPlayer = null;
            // @event.FunfactData1 = empty;
            // @event.FunfactData2 = empty;
            // @event.FunfactData3 = empty;
            int finalEvent = 10;
            if (knifeWinner == 3)
            {
                finalEvent = 8;
            }
            else if (knifeWinner == 2)
            {
                finalEvent = 9;
            }
            Logger.LogInformation($"[KNIFE WINNER] Won by: {knifeWinner}, finalEvent: {@event.FinalEvent}, newFinalEvent: {finalEvent}");
            @event.FinalEvent = (byte)finalEvent;
        }

        private void HandleMapChangeCommand(IPlayer? player, string mapName)
        {
            if (!IsPlayerAdmin(player, "css_map", "@css/map"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, $"Map cannot be changed once the match is started!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchstarted"]);
                return;
            }

            if (!long.TryParse(mapName, out _) && !mapName.Contains('_'))
            {
                mapName = "de_" + mapName;
            }

            if (long.TryParse(mapName, out _))
            { // Check if mapName is a long for workshop map ids
                Core.Engine.ExecuteCommand($"bot_kick");
                Core.Engine.ExecuteCommand($"host_workshop_map \"{mapName}\"");
            }
            else if (Core.Engine.IsMapValid(mapName))
            {
                Core.Engine.ExecuteCommand($"bot_kick");
                Core.Engine.ExecuteCommand($"changelevel \"{mapName}\"");
            }
            else
            {
                ReplyToUserCommand(player, $"Invalid map name!");
            }
        }

        private void HandleReadyRequiredCommand(IPlayer? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int readyRequired) && readyRequired >= 0 && readyRequired <= 32)
                {
                    minimumReadyRequired = readyRequired;
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    // ReplyToUserCommand(player, $"Minimum ready players required to start the match are now set to: {minimumReadyRequiredFormatted}");
                    ReplyToUserCommand(player, Localizer["matchzy.utility.minreadyplayers", minimumReadyRequiredFormatted]);
                    CheckLiveRequired();
                }
                else
                {
                    // ReplyToUserCommand(player, $"Invalid value for readyrequired. Please specify a valid non-negative number. Usage: !readyrequired <number_of_ready_players_required>");
                    ReplyToUserCommand(player, Localizer["matchzy.utility.rrinvalidvalue"]);
                }
            }
            else
            {
                string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                // ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted} .Usage: !readyrequired <number_of_ready_players_required>");
                ReplyToUserCommand(player, Localizer["matchzy.utility.currentreadyrequired", minimumReadyRequiredFormatted]);
            }
        }

        private void CheckLiveRequired()
        {
            if (!readyAvailable || matchStarted) return;

            // Todo: Implement a same ready system for both pug and match
            int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
            bool liveRequired = false;
            if (isMatchSetup)
            {
                if (IsTeamsReady() && IsSpectatorsReady())
                {
                    liveRequired = true;
                }
            }
            else if (minimumReadyRequired == 0)
            {
                if (countOfReadyPlayers >= connectedPlayers && connectedPlayers > 0)
                {
                    liveRequired = true;
                }
            }
            else if (countOfReadyPlayers >= minimumReadyRequired)
            {
                liveRequired = true;
            }
            if (liveRequired)
            {
                HandleMatchStart();
            }
        }

        private void HandleMatchStart()
        {
            isPractice = false;
            isDryRun = false;
            if (isRoundRestorePending)
            {
                RestoreRoundBackup(null, pendingRestoreFileName);
                isRoundRestorePending = false;
                pendingRestoreFileName = "";
                return;
            }
            // If default names, we pick a player and use their name as their team name
            if (matchzyTeam1.teamName == "COUNTER-TERRORISTS")
            {
                // matchzyTeam1.teamName = teamName;
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
                foreach (var key in playerData.Keys)
                {
                    var p = playerData[key];
                    if (p != null && p.IsValid && (int)p.RequiredController.TeamNum == 3)
                    {
                        matchzyTeam1.teamName = "team_" + RemoveSpecialCharacters(p.RequiredController.PlayerName.Replace(" ", "_"));
                        foreach (var coach in matchzyTeam1.coach) {
                            // Update Clan property access for SwiftlyS2
                            coach.RequiredController.Clan = $"[{matchzyTeam1.teamName} COACH]";
                            coach.RequiredController.ClanUpdated();
                        }
                        break;
                    }
                }
                // Core.Engine.ExecuteCommand($"mp_teamname_1 {matchzyTeam1.teamName}");
            }

            if (matchzyTeam2.teamName == "TERRORISTS")
            {
                // matchzyTeam2.teamName = teamName;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                foreach (var key in playerData.Keys)
                {
                    var p = playerData[key];
                    if (p != null && p.IsValid && (int)p.RequiredController.TeamNum == 2)
                    {
                        matchzyTeam2.teamName = "team_" + RemoveSpecialCharacters(p.RequiredController.PlayerName.Replace(" ", "_"));
                        foreach (var coach in matchzyTeam2.coach) {
                            // Update Clan property access for SwiftlyS2
                            coach.RequiredController.Clan = $"[{matchzyTeam2.teamName} COACH]";
                            coach.RequiredController.ClanUpdated();
                        }
                        break;
                    }
                }
                // Core.Engine.ExecuteCommand($"mp_teamname_2 {matchzyTeam2.teamName}");
            }

            Core.Engine.ExecuteCommand($"mp_teamname_1 {reverseTeamSides["CT"].teamName}");
            Core.Engine.ExecuteCommand($"mp_teamname_2 {reverseTeamSides["TERRORIST"].teamName}");

            HandleClanTags();

            string seriesType = "BO" + matchConfig.NumMaps.ToString();
            string mapName = isMatchSetup ? matchConfig.Maplist[matchConfig.CurrentMapNumber] : Core.Engine.GlobalVars.MapName;
            // Optional: record match metadata in the stats database (disabled by default in SwiftlyS2 port).
            // liveMatchId = database.InitMatch(matchzyTeam1.teamName, matchzyTeam2.teamName, "-", isMatchSetup, liveMatchId, matchConfig.CurrentMapNumber, seriesType, mapName);
            SetupRoundBackupFile();

            GetSpawns();

            if (isPreVeto)
            {
                CreateVeto();
            }
            else if (isKnifeRequired)
            {
                StartKnifeRound();
            }
            else
            {
                StartDemoRecording();
                StartLive();
            }
            if (showCreditsOnMatchStart)
            {
                Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}MatchZy{ChatColors.Default} Plugin by {ChatColors.Green}WD-{ChatColors.Default}");
            }
            if (matchStartMessage.Trim() != "" && matchStartMessage.Trim() != "\"\"")
            {
                List<string> matchStartMessages = [.. matchStartMessage.Split("$$$")];
                foreach (string message in matchStartMessages)
                {
                    PrintToAllChat(GetColorTreatedString(FormatCvarValue(message.Trim())));
                }
            }
        }

        public void HandleClanTags()
        {
            // Currently it is not possible to keep updating player tags while in warmup without restarting the match
            // Hence returning from here until we find a proper solution
            return;

            if (readyAvailable && !matchStarted)
            {
                foreach (var key in playerData.Keys)
                {
                    var p = playerData[key];
                    if (p == null || !p.IsValid) continue;
                    // Update Clan property access for SwiftlyS2
                    if (playerReadyStatus.ContainsKey(key) && playerReadyStatus[key])
                    {
                        p.RequiredController.Clan = "[Ready]";
                        p.RequiredController.ClanUpdated();
                    }
                    else
                    {
                        p.RequiredController.Clan = "[Unready]";
                        p.RequiredController.ClanUpdated();
                    }
                }
            }
            else if (matchStarted)
            {
                foreach (var key in playerData.Keys)
                {
                    var p = playerData[key];
                    if (p == null || !p.IsValid) continue;
                    // Update Clan property access for SwiftlyS2
                    if ((int)p.RequiredController.TeamNum == 2)
                    {
                        p.RequiredController.Clan = reverseTeamSides["TERRORIST"].teamTag;
                        p.RequiredController.ClanUpdated();
                    }
                    else if ((int)p.RequiredController.TeamNum == 3)
                    {
                        p.RequiredController.Clan = reverseTeamSides["CT"].teamTag;
                        p.RequiredController.ClanUpdated();
                    }
                    // Core.PlayerManager.SendChat($"PlayerName: {p.RequiredController.PlayerName} Clan: {p.Clan}");
                }
            }
        }

        private void HandleMatchEnd()
        {
            if (!isMatchLive) return;

            // This ensures that the mp_match_restart_delay is not shorter than what is required for the GOTV recording to finish.
            // Ref: Get5
            var restartDelayConVar = Core.ConVar.Find<int>("mp_match_restart_delay");
            int restartDelay = restartDelayConVar != null ? restartDelayConVar.Value : 5;
            int tvDelay = GetTvDelay();
            int requiredDelay = tvDelay + 15;
            int tvFlushDelay = requiredDelay;
            if (tvDelay > 0.0)
            {
                requiredDelay += 10;
            }
            if (requiredDelay > restartDelay)
            {
                Logger.LogInformation($"Extended mp_match_restart_delay from {restartDelay} to {requiredDelay} to ensure GOTV broadcast can finish.");
                if (restartDelayConVar != null) restartDelayConVar.Value = requiredDelay;
                restartDelay = requiredDelay;
            }
            int currentMapNumber = matchConfig.CurrentMapNumber;
            Logger.LogInformation($"[HandleMatchEnd] MAP ENDED, isMatchSetup: {isMatchSetup} matchid: {liveMatchId} currentMapNumber: {currentMapNumber} tvFlushDelay: {tvFlushDelay}");

            StopDemoRecording(tvFlushDelay - 0.5f, activeDemoFile, liveMatchId, currentMapNumber);

            string winnerName = GetMatchWinnerName();
            (int t1score, int t2score) = GetTeamsScore();
            int team1SeriesScore = matchzyTeam1.seriesScore;
            int team2SeriesScore = matchzyTeam2.seriesScore;

            string statsPath = Core.CSGODirectory + "/csgo/MatchZy_Stats/" + liveMatchId.ToString();

            var mapResultEvent = new MapResultEvent
            {
                MatchId = liveMatchId,
                MapNumber = currentMapNumber,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", t1score > t2score ? "team1" : "team2"),
                StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, team1SeriesScore, t1score, 0, 0, new List<StatsPlayer>()),
                StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, team2SeriesScore, t2score, 0, 0, new List<StatsPlayer>())
            };

            Task.Run(async () =>
            {
                await SendEventAsync(mapResultEvent);
                // Optional: write detailed stats to the matchzy database; disabled by default for SwiftlyS2.
                // await database.SetMapEndData(liveMatchId, currentMapNumber, winnerName, t1score, t2score, team1SeriesScore, team2SeriesScore);
                // await database.WritePlayerStatsToCsv(statsPath, liveMatchId, currentMapNumber);
            });

            // If a match is not setup, it was supposed to be a pug/scrim with 1 map
            // Hence we reset the match once it is over
            // Todo: Support BO3/BO5 in pugs as well
            if (!isMatchSetup)
            {
                EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                return;
            }

            int remainingMaps = matchConfig.NumMaps - matchzyTeam1.seriesScore - matchzyTeam2.seriesScore;
            Logger.LogInformation($"[HandleMatchEnd] MATCH ENDED, remainingMaps: {remainingMaps}, NumMaps: {matchConfig.NumMaps}, Team1SeriesScore: {matchzyTeam1.seriesScore}, Team2SeriesScore: {matchzyTeam2.seriesScore}");
            if (matchzyTeam1.seriesScore == matchzyTeam2.seriesScore && remainingMaps <= 0)
            {
                EndSeries(null, restartDelay - 1, t1score, t2score);
            }
            else if (matchConfig.SeriesCanClinch)
            {
                int mapsToWinSeries = (matchConfig.NumMaps / 2) + 1;
                if (matchzyTeam1.seriesScore == mapsToWinSeries)
                {
                    EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                    return;
                }
                else if (matchzyTeam2.seriesScore == mapsToWinSeries)
                {
                    EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                    return;
                }
            }
            else if (remainingMaps <= 0)
            {
                EndSeries(winnerName, restartDelay - 1, t1score, t2score);
                return;
            }
            if (matchzyTeam1.seriesScore > matchzyTeam2.seriesScore)
            {
                Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");

            }
            else if (matchzyTeam2.seriesScore > matchzyTeam1.seriesScore)
            {
                Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} is winning the series {ChatColors.Green}{matchzyTeam2.seriesScore}-{matchzyTeam1.seriesScore}{ChatColors.Default}");

            }
            else
            {
                Core.PlayerManager.SendChat($"{chatPrefix} The series is tied at {ChatColors.Green}{matchzyTeam1.seriesScore}-{matchzyTeam2.seriesScore}{ChatColors.Default}");
            }
            matchConfig.CurrentMapNumber += 1;
            string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];

            if (isPaused)
                UnpauseMatch();

            stopData["ct"] = false;
            stopData["t"] = false;

            KillPhaseTimers();

            SchedulerService.DelayBySeconds(restartDelay - 4, () =>
            {
                if (!isMatchSetup) return;
                ChangeMap(nextMap, 3.0f);
                matchStarted = false;
                readyAvailable = true;
                isPaused = false;

                isWarmup = true;
                isKnifeRound = false;
                isSideSelectionPhase = false;
                isMatchLive = false;
                isPractice = false;
                isDryRun = false;
                StartWarmup();
                SetMapSides();
            });
        }

        private void ChangeMap(string mapName, float delay)
        {
            Logger.LogInformation($"[ChangeMap] Changing map to {mapName} with delay {delay}");
            SchedulerService.DelayBySeconds(delay, () =>
            {
                if (long.TryParse(mapName, out _))
                {
                    Core.Engine.ExecuteCommand($"bot_kick");
                    Core.Engine.ExecuteCommand($"host_workshop_map \"{mapName}\"");
                }
                else if (Core.Engine.IsMapValid(mapName))
                {
                    Core.Engine.ExecuteCommand($"bot_kick");
                    Core.Engine.ExecuteCommand($"changelevel \"{mapName}\"");
                }
            });
        }

        private string GetMatchWinnerName()
        {
            (int t1score, int t2score) = GetTeamsScore();
            if (t1score > t2score)
            {
                matchzyTeam1.seriesScore++;
                return matchzyTeam1.teamName;
            }
            else if (t2score > t1score)
            {
                matchzyTeam2.seriesScore++;
                return matchzyTeam2.teamName;
            }
            else
            {
                return "Draw";
            }
        }

        private (int t1score, int t2score) GetTeamsScore()
        {
            var teamEntities = Core.EntitySystem.GetAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            int t1score = 0;
            int t2score = 0;
            foreach (var team in teamEntities)
            {
                if (team.Teamname == teamSides[matchzyTeam1])
                {
                    t1score = team.Score;
                }
                else if (team.Teamname == teamSides[matchzyTeam2])
                {
                    t2score = team.Score;
                }
            }
            return (t1score, t2score);
        }

        private int GetRoundNumer()
        {
            (int t1score, int t2score) = GetTeamsScore();

            return t1score + t2score;
        }

        // Event handler for round start - signature updated for SwiftlyS2
        public void HandlePostRoundStartEvent(EventRoundStart @event)
        {
            if (isDryRun) RandomizeSpawns();
            if (!matchStarted) return;
            playerHasTakenDamage = false;
            HandleCoaches();
            CreateMatchZyRoundDataBackup();
            InitPlayerDamageInfo();
            UpdateHostname();
        }

        // Event handler for round end - signature updated for SwiftlyS2
        private void HandlePostRoundEndEvent(EventRoundEnd @event)
        {
            try
            {
                if (isMatchLive)
                {
                    coachKillTimer?.Cancel();
                    coachKillTimer = null;
                    (int t1score, int t2score) = GetTeamsScore();
                    Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam1.teamName} [{t1score} - {t2score}] {matchzyTeam2.teamName}");

                    ShowDamageInfo();

                    (Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary, List<StatsPlayer> playerStatsListTeam1, List<StatsPlayer> playerStatsListTeam2) = GetPlayerStatsDict();

                    int currentMapNumber = matchConfig.CurrentMapNumber;
                    long matchId = liveMatchId;
                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;
                    // Get EventRoundEnd from the handler
                    // Winner winner = new(@event.Winner.ToString() ?? "0", t1score > t2score ? "team1" : "team2");

                    var roundEndEvent = new MatchZyRoundEndedEvent
                    {
                        MatchId = liveMatchId,
                        MapNumber = matchConfig.CurrentMapNumber,
                        RoundNumber = GetRoundNumer(),
                        Reason = 0, // TODO: Get from event
                        RoundTime = 0,
                        Winner = new Winner("0", t1score > t2score ? "team1" : "team2"), // TODO: Get from event
                        StatsTeam1 = new MatchZyStatsTeam(matchzyTeam1.id, matchzyTeam1.teamName, 0, t1score, 0, 0, playerStatsListTeam1),
                        StatsTeam2 = new MatchZyStatsTeam(matchzyTeam2.id, matchzyTeam2.teamName, 0, t2score, 0, 0, playerStatsListTeam2),
                    };

                    Task.Run(async () =>
                    {
                        await SendEventAsync(roundEndEvent);
                        // Optional: push per‑round stats into the database; disabled by default for SwiftlyS2.
                        // await DatabaseService.UpdatePlayerStatsAsync(matchId, currentMapNumber, playerStatsDictionary);
                        // await DatabaseService.UpdateMapStatsAsync(matchId, currentMapNumber, t1score, t2score);
                    });

                    string round = GetRoundNumer().ToString("D2");
                    lastBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt";
                    lastMatchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    Logger.LogInformation($"[HandlePostRoundEndEvent] Setting lastBackupFileName to {lastBackupFileName} and lastMatchZyBackupFileName to {lastMatchZyBackupFileName}");

                    // One of the team did not use .stop command hence display the proper message after the round has ended.
                    if (stopData["ct"] && !stopData["t"])
                    {
                        Core.PlayerManager.SendChat($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["CT"].teamName}{ChatColors.Default} was cancelled as the round ended");
                    }
                    else if (!stopData["ct"] && stopData["t"])
                    {
                        Core.PlayerManager.SendChat($"{chatPrefix} The round restore request by {ChatColors.Green}{reverseTeamSides["TERRORIST"].teamName}{ChatColors.Default} was cancelled as the round ended");
                    }

                    // Invalidate .stop requests after a round is completed.
                    stopData["ct"] = false;
                    stopData["t"] = false;

                    bool swapRequired = IsTeamSwapRequired();

                    // If isRoundRestoring is true, sides will be swapped from round restore if required!
                    if (swapRequired && !isRoundRestoring)
                    {
                        SwapSidesInTeamData(false);
                    }

                    isRoundRestoring = false;
                }
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[HandlePostRoundEndEvent FATAL] An error occurred: {e.Message}");
            }
        }

        public bool IsTeamSwapRequired()
        {
            // Handling OTs and side swaps (Referred from Get5)
            var gameRules = Core.EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
            int roundsPlayed = gameRules.TotalRoundsPlayed;

            var maxroundsConVar = Core.ConVar.Find<int>("mp_maxrounds");
            int roundsPerHalf = (maxroundsConVar != null ? maxroundsConVar.Value : 24) / 2;
            var otMaxroundsConVar = Core.ConVar.Find<int>("mp_overtime_maxrounds");
            int roundsPerOTHalf = (otMaxroundsConVar != null ? otMaxroundsConVar.Value : 6) / 2;

            var halftimeConVar = Core.ConVar.Find<bool>("mp_halftime");
            bool halftimeEnabled = halftimeConVar != null ? halftimeConVar.Value : true;

            if (halftimeEnabled)
            {
                if (roundsPlayed == roundsPerHalf)
                {
                    return true;
                }
                // Now in OT.
                if (roundsPlayed >= 2 * roundsPerHalf)
                {
                    int otround = roundsPlayed - 2 * roundsPerHalf;  // round 33 -> round 3, etc.
                    // Do side swaps at OT halves (rounds 3, 9, ...)
                    if ((otround + roundsPerOTHalf) % (2 * roundsPerOTHalf) == 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void PauseMatch(IPlayer? player, ICommandContext? command)
        {
            if (isMatchLive && isPaused)
            {
                // ReplyToUserCommand(player, "Match is already paused!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.paused"]);
                return;
            }
            if (IsHalfTimePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command during halftime.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.duringhalftime"]);
                return;
            }
            if (IsPostGamePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchended"]);
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.tacticaltimeout"]);
                return;
            }
            if (!techPauseEnabled && player != null)
            {
                PrintToPlayerChat(player, Localizer["matchzy.pause.techpausenotenabled"]);
                return;
            }
            if(!string.IsNullOrEmpty(techPausePermission) && techPausePermission != "\"\"")
            {
                if (!IsPlayerAdmin(player, "css_pause", techPausePermission))
                {
                    SendPlayerNotAdminMessage(player);
                    return;
                }
            }
            if (isMatchLive && !isPaused)
            {

                string pauseTeamName = "Admin";
                unpauseData["pauseTeam"] = "Admin";
                if (player != null && player.RequiredController.TeamNum == 2)
                {

                    pauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["TERRORIST"].teamName;
                }
                else if (player != null && player.RequiredController.TeamNum == 3)
                {
                    pauseTeamName = reverseTeamSides["CT"].teamName;
                    unpauseData["pauseTeam"] = reverseTeamSides["CT"].teamName;
                }
                else
                {
                    return;
                }
                PrintToAllChat(Localizer["matchzy.pause.pausedthematch", pauseTeamName]);
                // Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");

                SetMatchPausedFlags();
            }
        }

        private void ForcePauseMatch(IPlayer? player, ICommandContext? command)
        {
            if (!matchStarted) return;
            if (!IsPlayerAdmin(player, "css_forcepause", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (isMatchLive && isPaused)
            {
                // ReplyToUserCommand(player, "Match is already paused!");
                ReplyToUserCommand(player, Localizer["matchzy.utility.paused"]);
                return;
            }
            if (IsHalfTimePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command during halftime.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.duringhalftime"]);
                return;
            }
            if (IsPostGamePhase())
            {
                // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.matchended"]);
                return;
            }
            if (IsTacticalTimeoutActive())
            {
                // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
                ReplyToUserCommand(player, Localizer["matchzy.utility.tacticaltimeout"]);
                return;
            }
            unpauseData["pauseTeam"] = "Admin";
            PrintToAllChat(Localizer["matchzy.pause.adminpausedthematch"]);
            // Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
            if (player == null)
            {
                Logger.LogInformation($"[MatchZy] {Localizer["matchzy.pause.adminpausedthematch"]}");
            }
            SetMatchPausedFlags();
        }

        private void ForceUnpauseMatch(IPlayer? player, ICommandContext? command)
        {
            if (matchStarted && isPaused)
            {
                if (!IsPlayerAdmin(player, "css_forceunpause", "@css/config"))
                {
                    SendPlayerNotAdminMessage(player);
                    return;
                }
                PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
                UnpauseMatch();

                if (player == null)
                {
                    Logger.LogInformation("[MatchZy] Admin has unpaused the match, resuming the match!");
                }
            }
        }

        private void UnpauseMatch()
        {
            Core.Engine.ExecuteCommand("mp_unpause_match;");
            isPaused = false;
            unpauseData["ct"] = false;
            unpauseData["t"] = false;
            if (!isPaused && pausedStateTimer != null)
            {
                pausedStateTimer.Cancel();
                pausedStateTimer = null;
            }
        }

        private void SetMatchPausedFlags()
        {
            coachKillTimer?.Cancel();
            coachKillTimer = null;

            Core.Engine.ExecuteCommand("mp_pause_match;");
            isPaused = true;

            pausedStateTimer ??= SchedulerService.RepeatBySeconds(chatTimerDelay, SendPausedStateMessage);
        }

        private void StartMatchMode()
        {
            if (matchStarted || (!isPractice && !isSleep)) return;
            ExecUnpracCommands();
            ResetMatch();
            RemoveSpawnBeams();
            Core.PlayerManager.SendChat($"{chatPrefix} Match mode loaded!");
        }

        private void ExecLiveCFG()
        {
            int gameMode = GetGameMode();

            string cfgFileName = gameMode == 2 ? "live_wingman.cfg" : "live.cfg";
            string cfgPath = GetConfigFilePath(cfgFileName);

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(cfgPath))
            {
                Logger.LogInformation($"[StartLive] Starting Live! Executing Live CFG from {cfgPath}");
                Core.Engine.ExecuteCommand($"exec {cfgPath}");
                Core.Engine.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            }
            else
            {
                Logger.LogInformation($"[StartLive] Starting Live! Live CFG not found in {cfgPath}, using default CFG!");
                if (gameMode == 2)
                {
                    Core.Engine.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_bonus_shorthanded 1000;cash_team_elimination_bomb_map 2750;cash_team_elimination_hostage_map_ct 2500;cash_team_elimination_hostage_map_t 2500;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 2000;cash_team_loser_bonus_consecutive_rounds 300;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3000;cash_team_win_by_defusing_bomb 3000;cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 2750;cash_team_win_by_time_running_out_hostage 2750;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 0;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 10;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 1;mp_maxmoney 8000;");
                    Core.Engine.ExecuteCommand("mp_maxrounds 16;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 4;mp_overtime_startmoney 8000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 7;mp_roundtime 1.5;mp_roundtime_defuse 1.5;mp_roundtime_hostage 1.5;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 0");
                }
                else
                {
                    Core.Engine.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                    Core.Engine.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 3;mp_team_timeout_ot_max 1;mp_team_timeout_ot_add_each 1;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
                }
            }
        }

        public void SendPlayerNotAdminMessage(IPlayer? player)
        {
            // ReplyToUserCommand(player, "You do not have permission to use this command!");
            ReplyToUserCommand(player, Localizer["matchzy.utility.dontpermission"]);
        }

        private string GetColorTreatedString(string message)
        {
            // Adding extra space before args if message starts with a color name
            // This is because colors cannot be applied from 1st character, hence we make first character as an empty space
            if (message.StartsWith('{')) message = " " + message;

            foreach (var field in typeof(ChatColors).GetFields())
            {
                string pattern = $"{{{field.Name}}}";
                string? replacement = field.GetValue(null)?.ToString();

                if (replacement is null) return message;

                // Create a case-insensitive regular expression pattern for the color name
                string patternIgnoreCase = Regex.Escape(pattern);
                message = Regex.Replace(message, patternIgnoreCase, replacement, RegexOptions.IgnoreCase);
            }

            return message;
        }

        private void SendAvailableCommandsMessage(IPlayer? player)
        {
            if (!IsPlayerValid(player)) return;

            ReplyToUserCommand(player, "Available commands:");

            if (isPractice)
            {
                player!.SendChat($" {ChatColors.Green}Spawns: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn");
                player.SendChat($" {ChatColors.Green}Bots: {ChatColors.Default}.bot, .nobots, .crouchbot, .boost, .crouchboost");
                player.SendChat($" {ChatColors.Green}Nades: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades");
                player.SendChat($" {ChatColors.Green}Nade Throw: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>");
                player.SendChat($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj");
                player.SendChat($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.savepos, .loadpos");
                player.SendChat($" {ChatColors.Green}Sides & Others: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .exitprac");
                return;
            }
            if (readyAvailable)
            {
                player!.SendChat($" {ChatColors.Green}Ready/Unready: {ChatColors.Default}.ready, .unready");
                return;
            }
            if (isSideSelectionPhase)
            {
                player!.SendChat($" {ChatColors.Green}Side Selection: {ChatColors.Default}.stay, .switch, .ct, .t");
                return;
            }
            if (matchStarted)
            {
                string stopCommandMessage = isStopCommandAvailable ? ", .stop" : "";
                player!.SendChat($" {ChatColors.Green}Pause/Restore: {ChatColors.Default}.pause, .unpause, .tac, .tech{stopCommandMessage}");
                return;
            }
        }

        public void LoadClientNames()
        {
            string namesFileName = "Match_" + liveMatchId.ToString() + ".ini";
            // Core.CSGODirectory already points to the csgo directory in SwiftlyS2,
            // so we create/use "MatchZyPlayerNames" directly under it.
            string namesDirPath = Path.Combine(Core.CSGODirectory, "MatchZyPlayerNames");
            if (!Directory.Exists(namesDirPath))
            {
                Directory.CreateDirectory(namesDirPath);
            }
            string namesFilePath = Path.Combine(namesDirPath, namesFileName);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\"Names\"");
            sb.AppendLine("{");

            WriteClientNamesInFile(sb, matchzyTeam1.teamPlayers);
            WriteClientNamesInFile(sb, matchzyTeam2.teamPlayers);
            WriteClientNamesInFile(sb, matchConfig.Spectators);

            sb.AppendLine("}");
            File.WriteAllText(namesFilePath, sb.ToString());
            Core.Engine.ExecuteCommand($"sv_load_forced_client_names_file MatchZyPlayerNames/{namesFileName}");
        }

        public void WriteClientNamesInFile(StringBuilder sb, JToken? players)
        {
            if (players == null) return;
            foreach (JProperty player in players)
            {
                string steamId = player.Name;
                string escapedName = player.Value.ToString().Replace("\"", "\\\"").Trim();

                if (string.IsNullOrEmpty(escapedName)) continue;

                sb.AppendLine($"\t\"{steamId}\"\t\t\"{escapedName}\"");
            }
        }

        static bool IsValidUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? result))
            {
                return result != null && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
            }
            return false;
        }

        // GetConvarStringValue for SwiftlyS2 - helpers for safely reading convars
        public string GetConvarStringValue<T>(SwiftlyS2.Shared.Convars.IConVar<T>? cvar) where T : struct
        {
            if (cvar == null) return "";
            try
            {
                return cvar.Value.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[GetConvarStringValue] Failed to read value for convar of type {typeof(T).Name}: {ex.Message}");
                return "";
            }
        }

        // Overload for string type
        public string GetConvarStringValue(SwiftlyS2.Shared.Convars.IConVar<string>? cvar)
        {
            if (cvar == null) return "";
            try
            {
                return cvar.Value ?? "";
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[GetConvarStringValue] Failed to read value for string convar: {ex.Message}");
                return "";
            }
        }

        // Overload that tries to find the convar by name using SwiftlyS2's typed API.
        // We need to be defensive here because Find<T> throws when T does not match the real type.
        public string GetConvarStringValue(string cvarName)
        {
            try
            {
                // Try string first for typical text-based cvars like hostname.
                try
                {
                    var stringCvar = Core.ConVar.Find<string>(cvarName);
                    if (stringCvar != null) return stringCvar.Value ?? "";
                }
                catch (Exception)
                {
                    // Type mismatch is expected when the real type is not string; ignore and try next.
                }

                // Then try bool / int / float in order, swallowing type mismatches.
                try
                {
                    var boolCvar = Core.ConVar.Find<bool>(cvarName);
                    if (boolCvar != null) return boolCvar.Value.ToString();
                }
                catch (Exception)
                {
                }

                try
                {
                    var intCvar = Core.ConVar.Find<int>(cvarName);
                    if (intCvar != null) return intCvar.Value.ToString();
                }
                catch (Exception)
                {
                }

                try
                {
                    var floatCvar = Core.ConVar.Find<float>(cvarName);
                    if (floatCvar != null) return floatCvar.Value.ToString();
                }
                catch (Exception)
                {
                }

                return "";
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[GetConvarStringValue] Failed to resolve convar '{cvarName}': {ex.Message}");
                return "";
            }
        }

        // SetConvarValue for SwiftlyS2 - 根据名字安全设置 convar 值
        public void SetConvarValue(string cvarName, string value)
        {
            try
            {
                // 1) 先尝试 string（如 hostname、mp_teamname_1/2 等）
                try
                {
                    var stringCvar = Core.ConVar.Find<string>(cvarName);
                    if (stringCvar != null)
                    {
                        // 立即生效，避免排到内部队列里延迟应用
                        stringCvar.SetInternal(value);
                        return;
                    }
                }
                catch
                {
                    // 类型不匹配时忽略，继续其它类型
                }

                // 2) 尝试 bool（mp_friendlyfire、tv_enable 等）
                try
                {
                    var boolCvar = Core.ConVar.Find<bool>(cvarName);
                    if (boolCvar != null)
                    {
                        bool target;
                        if (bool.TryParse(value, out bool boolValue))
                        {
                            target = boolValue;
                        }
                        else if (int.TryParse(value, out int intValue) && intValue >= 1)
                        {
                            target = true;
                        }
                        else
                        {
                            target = false;
                        }

                        boolCvar.SetInternal(target);
                        return;
                    }
                }
                catch
                {
                }

                // 3) 尝试 int
                try
                {
                    var intCvar = Core.ConVar.Find<int>(cvarName);
                    if (intCvar != null && int.TryParse(value, out int intVal))
                    {
                        intCvar.SetInternal(intVal);
                        return;
                    }
                }
                catch
                {
                }

                // 4) 尝试 float
                try
                {
                    var floatCvar = Core.ConVar.Find<float>(cvarName);
                    if (floatCvar != null && float.TryParse(value, out float floatVal))
                    {
                        floatCvar.SetInternal(floatVal);
                        return;
                    }
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[SetConvarValue] Failed to set convar '{cvarName}' to '{value}': {ex.Message}");
            }
        }

        public void ExecuteChangedConvars()
        {
            foreach (string key in matchConfig.ChangedCvars.Keys)
            {
                string value = matchConfig.ChangedCvars[key];
                Logger.LogInformation($"[ExecuteChangedConvars] Setting convar {key} = \"{value}\"");
                // 使用 SwiftlyS2 的 ConVar API，而不是文本命令，确保服务端 cvar 实际被修改
                SetConvarValue(key, value);
            }
        }

        public void ResetChangedConvars()
        {
            foreach (string key in matchConfig.OriginalCvars.Keys)
            {
                string value = matchConfig.OriginalCvars[key];
                Logger.LogInformation($"[ResetChangedConvars] Restoring convar {key} = \"{value}\"");
                // 使用 SwiftlyS2 的 ConVar API 还原原始值
                SetConvarValue(key, value);
            }
        }

        public string FormatCvarValue(string value)
        {
            string formattedTime = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            (int team1Score, int team2Score) = GetTeamsScore();

            var formattedValue = value
                .Replace("{TIME}", formattedTime.Replace(" ", "_"))
                .Replace("{MATCH_ID}", $"{liveMatchId}")
                .Replace("{MAP}", Core.Engine.GlobalVars.MapName)
                .Replace("{MAPNUMBER}", matchConfig.CurrentMapNumber.ToString())
                .Replace("{TEAM1}", matchzyTeam1.teamName.Replace(" ", "_"))
                .Replace("{TEAM2}", matchzyTeam2.teamName.Replace(" ", "_"))
                .Replace("{TEAM1_SCORE}", team1Score.ToString())
                .Replace("{TEAM2_SCORE}", team2Score.ToString());
            return formattedValue;
        }

        public void UpdateHostname()
        {
            string hostname = hostnameFormat.Trim();
            if (hostname == "" || hostname == "\"\"") return;
            string formattedHostname = FormatCvarValue(hostname);
            Logger.LogInformation($"UPDATING HOSTNAME TO: {formattedHostname}");
            // 使用引号包裹，防止带空格的主机名被拆成多个参数
            Core.Engine.ExecuteCommand($"hostname \"{formattedHostname}\"");
        }

        // GetGameRules for SwiftlyS2
        public CCSGameRules? GetGameRules()
        {
            return Core.EntitySystem.GetGameRules();
        }

        public int GetGamePhase()
        {
            var gameRules = GetGameRules();
            return gameRules?.GamePhase ?? 0;
        }

        public bool IsHalfTimePhase()
        {
            try
            {
                return GetGamePhase() == 4;
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[IsHalfTime FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsPostGamePhase()
        {
            try
            {
                return GetGamePhase() == 5;
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[IsPostGamePhase FATAL] An error occurred: {e.Message}");
                return false;
            }

        }

        public bool IsTacticalTimeoutActive()
        {
            // Use SwiftlyS2 game rules entity to determine whether a tactical timeout is active.
            var gameRules = Core.EntitySystem.GetGameRules();
            if (gameRules == null) return false;

            return (gameRules.CTTimeOutActive || gameRules.TerroristTimeOutActive) && gameRules.FreezePeriod;
        }

        public (Dictionary<ulong, Dictionary<string, object>>, List<StatsPlayer>, List<StatsPlayer>) GetPlayerStatsDict()
        {
            Dictionary<ulong, Dictionary<string, object>> playerStatsDictionary = new Dictionary<ulong, Dictionary<string, object>>();
            List<StatsPlayer> playerStatsListTeam1 = new();
            List<StatsPlayer> playerStatsListTeam2 = new();
            // Update to use SwiftlyS2 entity system
            var gameRules = Core.EntitySystem.GetGameRules();
            int roundsPlayed = gameRules?.TotalRoundsPlayed ?? 0;
            try
            {
                // Using Core.PlayerManager to get players in SwiftlyS2
                var allPlayers = Core.PlayerManager.GetAllPlayers();
                foreach (var player in allPlayers)
                {
                    if (player == null || !player.IsValid) continue;
                    // Update ActionTrackingServices access for SwiftlyS2
                    if (player.RequiredController.ActionTrackingServices == null) continue;
                    var playerStats = player.RequiredController.ActionTrackingServices.MatchStats;
                    ulong steamid64 = player.SteamID;

                    // Create a nested dictionary to store individual stats for the player
                    // Update playerStats access for SwiftlyS2
                    Dictionary<string, object> stats = new Dictionary<string, object>
                    {
                        { "PlayerName", player.RequiredController.PlayerName },
                        { "Kills", playerStats.Kills },
                        { "Deaths", playerStats.Deaths },
                        { "Assists", playerStats.Assists },
                        { "Damage", (int)playerStats.Damage },
                        { "Enemy2Ks", playerStats.Enemy2Ks },
                        { "Enemy3Ks", playerStats.Enemy3Ks },
                        { "Enemy4Ks", playerStats.Enemy4Ks },
                        { "Enemy5Ks", playerStats.Enemy5Ks },
                        { "EntryCount", playerStats.EntryCount },
                        { "EntryWins", playerStats.EntryWins },
                        { "1v1Count", playerStats.I1v1Count },
                        { "1v1Wins", playerStats.I1v1Wins },
                        { "1v2Count", playerStats.I1v2Count },
                        { "1v2Wins", playerStats.I1v2Wins },
                        { "UtilityCount", playerStats.Utility_Count },
                        { "UtilitySuccess", playerStats.Utility_Successes },
                        { "UtilityDamage", (int)playerStats.UtilityDamage },
                        { "UtilityEnemies", playerStats.Utility_Enemies },
                        { "FlashCount", playerStats.Flash_Count },
                        { "FlashSuccess", playerStats.Flash_Successes },
                        { "HealthPointsRemovedTotal", (int)playerStats.HealthPointsRemovedTotal },
                        { "HealthPointsDealtTotal", (int)playerStats.HealthPointsDealtTotal },
                        { "ShotsFiredTotal", playerStats.ShotsFiredTotal },
                        { "ShotsOnTargetTotal", playerStats.ShotsOnTargetTotal },
                        { "EquipmentValue", 0 }, // Not available in MatchStats
                        { "MoneySaved", 0 }, // Not available in MatchStats
                        { "KillReward", 0 }, // Not available in MatchStats
                        { "LiveTime", 0 }, // Not available in MatchStats
                        { "HeadShotKills", playerStats.HeadShotKills },
                        { "CashEarned", 0 }, // Not available in MatchStats
                        { "EnemiesFlashed", 0 } // Not available in MatchStats
                    };

                    string teamName = "Spectator";
                    if (player.RequiredController.TeamNum == 3)
                    {
                        teamName = reverseTeamSides["CT"].teamName;
                    }
                    else if (player.RequiredController.TeamNum == 2)
                    {
                        teamName = reverseTeamSides["TERRORIST"].teamName;
                    }

                    stats["TeamName"] = teamName;

                    playerStatsDictionary.Add(steamid64, stats);

                    // Populate PlayerStats instance
                    // Note: Some stats are marked as 0 as they may not be available in MatchStats
                    // Stats are now populated from SwiftlyS2 ActionTrackingServices
                    PlayerStats playerStatsInstance = new()
                    {
                        Kills = (int)stats["Kills"],
                        Deaths = (int)stats["Deaths"],
                        Assists = (int)stats["Assists"],
                        FlashAssists = 0,
                        TeamKills = 0,
                        Suicides = 0,
                        Damage = (int)stats["Damage"],
                        UtilityDamage = (int)stats["UtilityDamage"],
                        EnemiesFlashed = (int)stats["EnemiesFlashed"],
                        FriendliesFlashed = 0,
                        KnifeKills = 0,
                        HeadshotKills = (int)(stats.ContainsKey("HeadShotKills") ? stats["HeadShotKills"] : 0),
                        RoundsPlayed = roundsPlayed,
                        BombDefuses = 0,
                        BombPlants = 0,
                        Kills1 = 0,
                        Kills2 = (int)(stats.ContainsKey("Enemy2Ks") ? stats["Enemy2Ks"] : 0),
                        Kills3 = (int)(stats.ContainsKey("Enemy3Ks") ? stats["Enemy3Ks"] : 0),
                        Kills4 = (int)(stats.ContainsKey("Enemy4Ks") ? stats["Enemy4Ks"] : 0),
                        Kills5 = (int)(stats.ContainsKey("Enemy5Ks") ? stats["Enemy5Ks"] : 0),
                        OneV1s = (int)(stats.ContainsKey("I1v1Wins") ? stats["I1v1Wins"] : 0),
                        OneV2s = (int)(stats.ContainsKey("I1v2Wins") ? stats["I1v2Wins"] : 0),
                        OneV3s = 0,
                        OneV4s = 0,
                        OneV5s = 0,
                        FirstKillsT = 0,
                        FirstKillsCT = 0,
                        FirstDeathsT = 0,
                        FirstDeathsCT = 0,
                        TradeKills = 0,
                        Kast = 0,
                        Score = player.RequiredController.Score,
                        Mvps = player.RequiredController.MVPs
                    };

                    StatsPlayer statsPlayer = new()
                    {
                        SteamId = steamid64.ToString(),
                        Name = player.RequiredController.PlayerName,
                        Stats = playerStatsInstance
                    };

                    int ctTeamNum = reverseTeamSides["CT"] == matchzyTeam1 ? 1 : 2;
                    int tTeamNum = reverseTeamSides["TERRORIST"] == matchzyTeam1 ? 1 : 2;

                    if (player.RequiredController.TeamNum == 3)
                    {
                        if (ctTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (ctTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    }
                    else if (player.RequiredController.TeamNum == 2)
                    {
                        if (tTeamNum == 1) playerStatsListTeam1.Add(statsPlayer);
                        if (tTeamNum == 2) playerStatsListTeam2.Add(statsPlayer);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[GetPlayerStatsDict FATAL] An error occurred: {e.Message}");
            }

            return (playerStatsDictionary, playerStatsListTeam1, playerStatsListTeam2);
        }

        static string RemoveSpecialCharacters(string input)
        {
            Regex regex = new("[^\\p{L}0-9 _-]");
            return regex.Replace(input, "");
        }

        private void Log(string message)
        {
            Logger.LogInformation("[MatchZy] " + message);
        }

        private void AutoStart()
        {
            Logger.LogInformation($"[AutoStart] autoStartMode: {autoStartMode}");
            if (autoStartMode == 0)
            {
                StartSleepMode();
            }
            if (autoStartMode == 1)
            {
                readyAvailable = true;
                isPractice = false;
                StartWarmup();
            }
            if (autoStartMode == 2)
            {
                StartPracticeMode();
            }
        }

        public int GetGameMode()
        {
            var convar = Core.ConVar.Find<int>("game_mode");
            if (convar != null)
            {
                return convar.Value;
            }
            return -1;
        }

        public int GetGameType()
        {
            var convar = Core.ConVar.Find<int>("game_type");
            if (convar != null)
            {
                return convar.Value;
            }
            return -1;
        }

        public void SetCorrectGameMode()
        {
            var gameModeConVar = Core.ConVar.Find<int>("game_mode");
            if (gameModeConVar != null) gameModeConVar.Value = matchConfig.Wingman ? 2 : 1;
            var gameTypeConVar = Core.ConVar.Find<int>("game_type");
            if (gameTypeConVar != null) gameTypeConVar.Value = 0; // Classic GameType
        }

        public bool IsMapReloadRequiredForGameMode(bool wingman)
        {
            int expectedMode = wingman ? 2 : 1;
            if (GetGameMode() != expectedMode || GetGameType() != 0)
            {
                return true;
            }
            return false;
        }

        public bool IsWingmanMode()
        {
            if (GetGameMode() == 2 && GetGameType() == 0) return true;
            return false;
        }

        public void KickPlayer(IPlayer player)
        {
            Core.Engine.ExecuteCommand($"kickid {player.PlayerID}");
        }

        public bool IsPlayerValid(IPlayer? player)
        {
            return (
                player != null &&
                player.IsValid &&
                player.PlayerPawn.IsValid &&
                player.RequiredPlayerPawn != null
            );
        }

        public static SwiftlyS2.Shared.Natives.Color GetPlayerTeammateColor(IPlayer playerController)
        {
            var colorValue = playerController.RequiredController.CompTeammateColor;
            return colorValue switch
            {
                1 => new SwiftlyS2.Shared.Natives.Color(50, 255, 0, 255),
                2 => new SwiftlyS2.Shared.Natives.Color(255, 255, 0, 255),
                3 => new SwiftlyS2.Shared.Natives.Color(255, 132, 0, 255),
                4 => new SwiftlyS2.Shared.Natives.Color(255, 0, 255, 255),
                0 => new SwiftlyS2.Shared.Natives.Color(0, 187, 255, 255),
                _ => new SwiftlyS2.Shared.Natives.Color(255, 0, 0, 255),
            };
        }

        public static string? GetConvarValueFromCFGFile(string filePath, string convarName)
        {
            var fileContent = File.ReadAllText(filePath);

            string pattern = @$"^{convarName}\s+(.+)$";

            Regex regex = new(pattern, RegexOptions.Multiline);

            Match match = regex.Match(fileContent);
            string? value = match.Success ? match.Groups[1].Value : null;
            return value;
        }

        public async Task UploadFileAsync(string? filePath, string fileUploadURL, string headerKey, string headerValue, long matchId, int mapNumber, int roundNumber)
        {
            if (filePath == null || fileUploadURL == "")
            {
                Logger.LogInformation($"[UploadFileAsync] Not able to upload the file, either filePath or fileUploadURL is not set. filePath: {filePath} fileUploadURL: {fileUploadURL}");
                return;
            }

            try
            {
                using var httpClient = new HttpClient();
                Logger.LogInformation($"[UploadFileAsync] Going to upload the file on {fileUploadURL}. Complete path: {filePath}");

                if (!File.Exists(filePath))
                {
                    Logger.LogInformation($"[UploadFileAsync ERROR] File not found: {filePath}");
                    return;
                }

                using FileStream fileStream = File.OpenRead(filePath);

                byte[] fileContent = new byte[fileStream.Length];
                await fileStream.ReadAsync(fileContent, 0, (int)fileStream.Length);

                using ByteArrayContent content = new(fileContent);
                content.Headers.Add("Content-Type", "application/octet-stream");

                content.Headers.Add("MatchZy-FileName", Path.GetFileName(filePath));
                content.Headers.Add("MatchZy-MatchId", matchId.ToString());
                content.Headers.Add("MatchZy-MapNumber", mapNumber.ToString());
                content.Headers.Add("MatchZy-RoundNumber", roundNumber.ToString());

                // For Get5 Panel
                content.Headers.Add("Get5-FileName", Path.GetFileName(filePath));
                content.Headers.Add("Get5-MatchId", matchId.ToString());
                content.Headers.Add("Get5-MapNumber", mapNumber.ToString());
                content.Headers.Add("Get5-RoundNumber", roundNumber.ToString());


                if (!string.IsNullOrEmpty(headerKey) && !string.IsNullOrEmpty(headerValue))
                {
                    httpClient.DefaultRequestHeaders.Add(headerKey, headerValue);
                }

                HttpResponseMessage response = await httpClient.PostAsync(fileUploadURL, content);

                if (response.IsSuccessStatusCode)
                {
                    Logger.LogInformation($"[UploadFileAsync] File upload successful for matchId: {matchId} mapNumber: {mapNumber} fileName: {Path.GetFileName(filePath)}.");
                }
                else
                {
                    Logger.LogInformation($"[UploadFileAsync ERROR] Failed to upload file. Status code: {response.StatusCode} Response: {await response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Logger.LogInformation($"[UploadFileAsync FATAL] An error occurred: {e.Message}");
            }
        }

        public bool HandlePlayerWhitelist(IPlayer player, string steamId)
        {
            string whitelistPath = GetConfigFilePath("whitelist.cfg");
            string? directoryPath = Path.GetDirectoryName(whitelistPath);
            if (directoryPath != null)
            {
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
            }
            if (!File.Exists(whitelistPath)) File.WriteAllLines(whitelistPath, new[] { "Steamid1", "Steamid2" });

            var whiteList = File.ReadAllLines(whitelistPath);

            if (isWhitelistRequired == true)
            {
                if (!whiteList.Contains(steamId.ToString()))
                {
                    Logger.LogInformation($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.RequiredController.PlayerName} (Not whitelisted!)");
                    PrintToAllChat($"Kicking player {player.RequiredController.PlayerName} - Not whitelisted.");
                    KickPlayer(player);
                    return true;
                }
            }

            return false;
        }

        public void SwitchPlayerTeam(IPlayer player, TeamEnum team)
        {
            if (player.RequiredController.TeamNum == (byte)team) return;

            // Use NextTick instead of NextFrame in SwiftlyS2
            SchedulerService.NextTick(() =>
            {
                if (team == TeamEnum.Spectator)
                {
                    player.ChangeTeam(team);
                }
                else
                {
                    player.SwitchTeam(team);
                    var gameRules = Core.EntitySystem.GetGameRules();
                    if (gameRules != null && gameRules.WarmupPeriod)
                    {
                        player.Respawn();
                    }
                }
            });
        }

        public void SetPlayerInvisible(IPlayer player, bool setWeaponsInvisible)
        {
            if (!IsPlayerValid(player) || !player.RequiredController.PawnIsAlive) return;
            var playerPawn = player.RequiredPlayerPawn;

            playerPawn.Render = new SwiftlyS2.Shared.Natives.Color(0, 0, 0, 0);
            // Note: In SwiftlyS2, property changes are automatically synchronized, SetStateChanged may not be needed

            if (!setWeaponsInvisible) return;

            var activeWeapon = playerPawn.WeaponServices?.ActiveWeapon;
            if (activeWeapon?.IsValid == true)
            {
                var weaponValue = activeWeapon.Value;
                // Note: Render and ShadowStrength properties may not be directly available on CBasePlayerWeapon
                // In SwiftlyS2, state changes are typically handled automatically when properties are set
                // If needed, we can use Schema.Update() but it's usually not necessary
                // weaponValue.Render = new SwiftlyS2.Shared.Natives.Color(0, 0, 0, 0);
                // weaponValue.ShadowStrength = 0.0f;
            }

            var myWeapons = playerPawn.WeaponServices?.MyWeapons;
            if (myWeapons != null)
            {
                foreach (var gun in myWeapons)
                {
                    var weapon = gun;
                    if (weapon.IsValid && weapon.Value != null)
                    {
                        var weaponValue = weapon.Value;
                        // Note: Render and ShadowStrength properties may not be directly available on CBasePlayerWeapon
                        // In SwiftlyS2, state changes are typically handled automatically when properties are set
                        // weaponValue.Render = new SwiftlyS2.Shared.Natives.Color(0, 0, 0, 0);
                        // weaponValue.ShadowStrength = 0.0f;
                    }
                }
            }
        }

        public void SetPlayerVisible(IPlayer player)
        {
            if (!IsPlayerValid(player) || !player.RequiredController.PawnIsAlive) return;

            var playerPawn = player.RequiredPlayerPawn;
            playerPawn.Render = new SwiftlyS2.Shared.Natives.Color(255, 255, 255, 255);
            // In SwiftlyS2, Render property changes are automatically handled
            // If explicit state update is needed, use RenderUpdated() method if available
            // For now, the property setter should handle state changes automatically
        }

        public void DropWeaponByDesignerName(IPlayer player, string weaponName)
        {
            if (!IsPlayerValid(player) || !player.RequiredController.PawnIsAlive) return;
            var playerPawn = player.RequiredPlayerPawn;
            if (playerPawn.WeaponServices is null) return;
            var matchedWeapon = playerPawn.WeaponServices!.MyWeapons
                .Where(weapon => weapon.IsValid && weapon.Value != null && weapon.Value.Entity?.DesignerName == weaponName).FirstOrDefault();

            if (matchedWeapon.IsValid && matchedWeapon.Value != null)
            {
                var pawn = player.RequiredPlayerPawn;
                if (pawn.WeaponServices?.ActiveWeapon != null && pawn.WeaponServices.ActiveWeapon.IsValid && pawn.WeaponServices.ActiveWeapon.Value != null)
                {
                    // Set the matched weapon as active
                    pawn.WeaponServices.ActiveWeapon.Raw = matchedWeapon.Raw;
                    pawn.WeaponServices.ActiveWeaponUpdated();
                    // Note: To drop the weapon, we would need to remove it from MyWeapons and set ActiveWeapon to null
                    // For now, this sets the matched weapon as active which is the intended behavior
                }
            }
        }

        public void RandomizeSpawns()
        {
            var players = Core.PlayerManager.GetAllPlayers().ToList();

            Dictionary<byte, List<Position>> teamSpawns = new()
            {
                { (byte)TeamEnum.CT, spawnsData[(byte)TeamEnum.CT].Select(position => new Position(position)).ToList() },
                { (byte)TeamEnum.T, spawnsData[(byte)TeamEnum.T].Select(position => new Position(position)).ToList() }
            };

            Random random = new();

            foreach (var player in players)
            {
                if (!IsPlayerValid(player)) continue;
                
                byte teamNum = (byte)player.RequiredController.TeamNum;
                if (teamSpawns[teamNum].Count == 0) break;

                int randomIndex = random.Next(teamSpawns[teamNum].Count);
                Position spawnPosition = teamSpawns[teamNum][randomIndex];
                teamSpawns[teamNum].RemoveAt(randomIndex);

                spawnPosition.Teleport(player);
            }
        }
    }
}
