using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Schemas;
using System.Text.RegularExpressions;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using Microsoft.Extensions.Logging;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;

namespace MatchZy
{
    public partial class MatchZy
    {
        /// <summary>
        /// Toggles the whitelist requirement for matches.
        /// Requires @css/config permission.
        /// </summary>
        [Command("whitelist", registerRaw: true, permission: "@css/config")]
        [CommandAlias("wl", registerRaw: true)]
        public void OnWLCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_whitelist", "@css/config"))
            {
                isWhitelistRequired = !isWhitelistRequired;
                string WLStatus = isWhitelistRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player == null)
                {
                    //ReplyToUserCommand(player, $"Whitelist is now {WLStatus}!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.wl", WLStatus]);
                }
                else
                {
                    //player.PrintToChat($"{chatPrefix} Whitelist is now {ChatColors.Green}{WLStatus}{ChatColors.Default}!");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.wl", WLStatus]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        /// <summary>
        /// Toggles saving grenades as global (shared across all maps).
        /// Requires @css/config permission.
        /// </summary>
        [Command("save_nades_as_global", registerRaw: true, permission: "@css/config")]
        [CommandAlias("globalnades", registerRaw: true)]
        public void OnSaveNadesAsGlobalCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_save_nades_as_global", "@css/config"))
            {
                isSaveNadesAsGlobalEnabled = !isSaveNadesAsGlobalEnabled;
                string GlobalNadesStatus = isSaveNadesAsGlobalEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player == null)
                {
                    //ReplyToUserCommand(player, $"Saving/Loading Lineups Globally is now {GlobalNadesStatus}!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.globalnades", GlobalNadesStatus]);
                }
                else
                {
                    //player.PrintToChat($"{chatPrefix} Saving/Loading Lineups Globally is now {ChatColors.Green}{GlobalNadesStatus}{ChatColors.Default}!");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.globalnades", GlobalNadesStatus]);

                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        /// <summary>
        /// Marks the player as ready for the match.
        /// </summary>
        [Command("ready", registerRaw: true)]
        [CommandAlias("r", registerRaw: true)]
        public void OnPlayerReady(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null) return;
            Logger.LogInformation($"[!ready command] Sent by: {player.PlayerID} readyAvailable: {readyAvailable} matchStarted: {matchStarted}");
            if (readyAvailable && !matchStarted)
            {
                if (player.IsValid)
                {
                    if (!playerReadyStatus.ContainsKey(player.PlayerID))
                    {
                        playerReadyStatus[player.PlayerID] = false;
                    }
                    if (playerReadyStatus[player.PlayerID])
                    {
                        // player.PrintToChat($"{chatPrefix} You are already ready!");
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedready"]);
                    }
                    else
                    {
                        playerReadyStatus[player.PlayerID] = true;
                        // player.PrintToChat($"{chatPrefix} {Localizer["matchzy.youareready"]}");
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedready"]);
                    }
                    CheckLiveRequired();
                    HandleClanTags();
                }
            }
        }

        /// <summary>
        /// Marks the player as not ready for the match.
        /// </summary>
        [Command("unready", registerRaw: true)]
        [CommandAlias("ur", registerRaw: true)]
        [CommandAlias("notready", registerRaw: true)]
        public void OnPlayerUnReady(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null) return;
            Logger.LogInformation($"[!unready command] {player.PlayerID}");
            if (readyAvailable && !matchStarted)
            {
                if (player.IsValid)
                {
                    if (!playerReadyStatus.ContainsKey(player.PlayerID))
                    {
                        playerReadyStatus[player.PlayerID] = false;
                    }
                    if (!playerReadyStatus[player.PlayerID])
                    {
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedunready"]);
                    }
                    else
                    {
                        playerReadyStatus[player.PlayerID] = false;
                        PrintToPlayerChat(player, Localizer["matchzy.ready.markedunready"]);
                    }
                    HandleClanTags();
                }
            }
        }

        [Command("stay", registerRaw: true)]
        public void OnTeamStay(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null || !isSideSelectionPhase) return;

            Logger.LogInformation($"[!stay command] {player.PlayerID}, TeamNum: {player.Controller.TeamNum}, knifeWinner: {knifeWinner}, isSideSelectionPhase: {isSideSelectionPhase}");
            if (player.Controller.TeamNum == knifeWinner)
            {
                PrintToAllChat(Localizer["matchzy.knife.decidedtostay", knifeWinnerName]);
                // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has decided to stay!");
                StartLive();
            }
        }

        [Command("switch", registerRaw: true)]
        [CommandAlias("swap", registerRaw: true)]
        public void OnTeamSwitch(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null || !isSideSelectionPhase) return;

            Logger.LogInformation($"[!switch command] {player.PlayerID}, TeamNum: {player.Controller.TeamNum}, knifeWinner: {knifeWinner}, isSideSelectionPhase: {isSideSelectionPhase}");

            if (player.Controller.TeamNum == knifeWinner)
            {
                Core.Engine.ExecuteCommand("mp_swapteams;");
                SwapSidesInTeamData(true);
                PrintToAllChat(Localizer["matchzy.knife.decidedtoswitch", knifeWinnerName]);
                // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} has decided to switch!");
                StartLive();
            }
        }

        [Command("t", registerRaw: true)]
        public void OnTCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null || !player.IsValid) return;
            if (isVeto) {
                HandleSideChoice(TeamEnum.T, player.PlayerID);
                return;
            }

            if (isSideSelectionPhase && player.Controller.TeamNum == knifeWinner) {
                if (player.Controller.TeamNum == (byte)TeamEnum.T) {
                    OnTeamStay(context);
                } else {
                    OnTeamSwitch(context);
                }
            }

            if (!isPractice) return;
            SideSwitchCommand(player, TeamEnum.T);
        }

        [Command("ct", registerRaw: true)]
        public void OnCTCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null || !player.IsValid) return;
            if (isVeto) {
                HandleSideChoice(TeamEnum.CT, player.PlayerID);
                return;
            }

            if (isSideSelectionPhase && player.Controller.TeamNum == knifeWinner) {
                if (player.Controller.TeamNum == (byte)TeamEnum.CT) {
                    OnTeamStay(context);
                } else {
                    OnTeamSwitch(context);
                }
                return;
            }

            if (!isPractice) return;
            SideSwitchCommand(player, TeamEnum.CT);
        }

        /// <summary>
        /// Initiates a technical pause. Requires appropriate permissions.
        /// </summary>
        [Command("tech", registerRaw: true)]
        public void OnTechCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            PauseMatch(player, context);
        }

        /// <summary>
        /// Pauses the match. Can be used for tactical or technical pauses depending on configuration.
        /// </summary>
        [Command("matchzy_pause", registerRaw: true)]
        // Note: "pause" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnPauseCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (isPauseCommandForTactical)
            {
                OnTacCommand(context);
            }
            else
            {
                PauseMatch(player, context);
            }
        }

        [Command("fp", registerRaw: true)]
        [CommandAlias("forcepause", registerRaw: true)]
        [CommandAlias("sm_pause", registerRaw: true)]
        public void OnForcePauseCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            ForcePauseMatch(player, context);
        }

        [Command("fup", registerRaw: true)]
        [CommandAlias("forceunpause", registerRaw: true)]
        [CommandAlias("sm_unpause", registerRaw: true)]
        public void OnForceUnpauseCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            ForceUnpauseMatch(player, context);
        }

        [Command("matchzy_unpause", registerRaw: true)]
        // Note: "unpause" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnUnpauseCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (isMatchLive && isPaused)
            {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin" && player != null)
                {
                    PrintToPlayerChat(player, Localizer["matchzy.pause.onlyadmincanunpause"]);
                    return;
                }

                string unpauseTeamName = "Admin";
                string remainingUnpauseTeam = "Admin";
                if (player?.Controller.TeamNum == 2)
                {
                    unpauseTeamName = reverseTeamSides["TERRORIST"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["CT"].teamName;
                    if (!(bool)unpauseData["t"])
                    {
                        unpauseData["t"] = true;
                    }

                }
                else if (player?.Controller.TeamNum == 3)
                {
                    unpauseTeamName = reverseTeamSides["CT"].teamName;
                    remainingUnpauseTeam = reverseTeamSides["TERRORIST"].teamName;
                    if (!(bool)unpauseData["ct"])
                    {
                        unpauseData["ct"] = true;
                    }
                }
                else
                {
                    return;
                }
                if ((bool)unpauseData["t"] && (bool)unpauseData["ct"])
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamsunpausedthematch"]);
                    Core.Engine.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                }
                else if (unpauseTeamName == "Admin")
                {
                    PrintToAllChat(Localizer["matchzy.pause.adminunpausedthematch"]);
                    Core.Engine.ExecuteCommand("mp_unpause_match;");
                    isPaused = false;
                    unpauseData["ct"] = false;
                    unpauseData["t"] = false;
                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.pause.teamwantstounpause", unpauseTeamName, remainingUnpauseTeam]);
                    // Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{unpauseTeamName}{ChatColors.Default} wants to unpause the match. {ChatColors.Green}{remainingUnpauseTeam}{ChatColors.Default}, please write !unpause to confirm.");
                }
                if (!isPaused && pausedStateTimer != null)
                {
                    pausedStateTimer.Cancel();
                    pausedStateTimer = null;
                }
            }
        }

        /// <summary>
        /// Initiates a tactical pause. Requires appropriate permissions.
        /// </summary>
        [Command("tac", registerRaw: true)]
        public void OnTacCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null) return;

            if (matchStarted && isMatchLive)
            {
                Logger.LogInformation($"[.tac command sent via chat] Sent by: {player.PlayerID}, connectedPlayers: {connectedPlayers}");
                if (isPaused)
                {
                    // ReplyToUserCommand(player, "Match is already paused, cannot start a tactical timeout!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.matchpaused"]);
                    return;
                }
                var gameRules = Core.EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
                if (player.Controller.TeamNum == 2)
                {
                    if (gameRules.TerroristTimeOuts > 0)
                    {
                        Core.Engine.ExecuteCommand("timeout_terrorist_start");
                    }
                    else
                    {
                        // ReplyToUserCommand(player, "You do not have any tactical timeouts left!");
                        ReplyToUserCommand(player, Localizer["matchzy.cc.nomorepauses"]);
                    }
                }
                else if (player.Controller.TeamNum == 3)
                {
                    if (gameRules.CTTimeOuts > 0)
                    {
                        Core.Engine.ExecuteCommand("timeout_ct_start");
                    }
                    else
                    {
                        // ReplyToUserCommand(player, "You do not have any tactical timeouts left!");
                        ReplyToUserCommand(player, Localizer["matchzy.cc.nomorepauses"]);
                    }
                }
            }
        }

        [Command("skipveto", registerRaw: true)]
        [CommandAlias("sv", registerRaw: true)]
        public void OnSkipVetoCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_skipveto", "@css/config"))
            {
                if (matchStarted)
                {
                    if (player == null)
                    {
                        // ReplyToUserCommand(player, $"Skip veto command cannot be used if match has already started!");
                        ReplyToUserCommand(player, Localizer["matchzy.cc.skipvetomatchstarted"]);
                    }
                    else
                    {
                        // player.PrintToChat($"{chatPrefix} Skip veto command cannot be used if match has already started!");
                        PrintToPlayerChat(player, Localizer["matchzy.cc.skipvetomatchstarted"]);
                    }
                }
                else
                {
                    SkipVeto();
                    if (player == null)
                    {
                        // ReplyToUserCommand(player, $"Veto phase has been cancelled!");
                        ReplyToUserCommand(player, Localizer["matchzy.cc.skipveto"]);
                    }
                    else
                    {
                        // player.PrintToChat($"{chatPrefix} Veto phase has been cancelled!");
                        PrintToPlayerChat(player, Localizer["matchzy.cc.skipveto"]);
                    }
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        /// <summary>
        /// Toggles knife round requirement. Requires @css/config permission.
        /// </summary>
        [Command("roundknife", registerRaw: true, permission: "@css/config")]
        [CommandAlias("rk", registerRaw: true)]
        public void OnKnifeCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_roundknife", "@css/config"))
            {
                isKnifeRequired = !isKnifeRequired;
                string knifeStatus = isKnifeRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player == null)
                {
                    // ReplyToUserCommand(player, $"Knife round is now {knifeStatus}!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.roundknife", knifeStatus]);
                }
                else
                {
                    // player.PrintToChat($"{chatPrefix} Knife round is now {ChatColors.Green}{knifeStatus}{ChatColors.Default}!");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.roundknife", knifeStatus]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("readyrequired", registerRaw: true)]
        public void OnReadyRequiredCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_readyrequired", "@css/config"))
            {
                if (context.Args.Length >= 1)
                {
                    string commandArg = context.Args[0];
                    HandleReadyRequiredCommand(player, commandArg);
                }
                else
                {
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    // ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted}. Usage: !readyrequired <number_of_ready_players_required>");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.minreadyrequired", minimumReadyRequiredFormatted]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("settings", registerRaw: true)]
        public void OnMatchSettingsCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null) return;

            if (IsPlayerAdmin(player, "css_settings", "@css/config"))
            {
                string knifeStatus = isKnifeRequired ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                string playoutStatus = isPlayOutEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                // player.PrintToChat($"{chatPrefix} Current Settings:");
                PrintToPlayerChat(player, Localizer["matchzy.cc.currentsettings"]);
                // player.PrintToChat($"{chatPrefix} Knife: {ChatColors.Green}{knifeStatus}{ChatColors.Default}");
                PrintToPlayerChat(player, Localizer["matchzy.cc.knifestatus", knifeStatus]);
                if (isMatchSetup)
                {
                    // player.PrintToChat($"{chatPrefix} Minimum Ready Players Required (Per Team): {ChatColors.Green}{matchConfig.MinPlayersToReady}{ChatColors.Default}");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyplayersperteam", matchConfig.MinPlayersToReady]);
                    // player.PrintToChat($"{chatPrefix} Minimum Ready Spectators Required: {ChatColors.Green}{matchConfig.MinSpectatorsToReady}{ChatColors.Default}");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyspecs", matchConfig.MinSpectatorsToReady]);
                }
                else
                {
                    // player.PrintToChat($"{chatPrefix} Minimum Ready Required: {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.minreadyplayers", minimumReadyRequired]);
                }
                // player.PrintToChat($"{chatPrefix} Playout: {ChatColors.Green}{playoutStatus}{ChatColors.Default}");
                PrintToPlayerChat(player, Localizer["matchzy.cc.playoutstatus", playoutStatus]);
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("endmatch", registerRaw: true)]
        [CommandAlias("get5_endmatch", registerRaw: true)]
        [CommandAlias("forceend", registerRaw: true)]
        public void OnEndMatchCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_endmatch", "@css/config"))
            {
                if (!isPractice)
                {
                    // Server.PrintToChatAll($"{chatPrefix} An admin force-ended the match.");
                    PrintToAllChat(Localizer["matchzy.cc.endmatch"]);
                    ResetMatch();
                }
                else
                {
                    // ReplyToUserCommand(player, "Practice mode is active, cannot end the match.");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.endmatchispracc"]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        /// <summary>
        /// Restarts the match. Requires @css/config permission.
        /// </summary>
        [Command("matchzy_restart", registerRaw: true, permission: "@css/config")]
        // Note: "restart" alias removed to avoid conflict with SwiftlyS2/system commands
        [CommandAlias("rr", registerRaw: true)]
        public void OnRestartMatchCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_restart", "@css/config"))
            {
                if (!isPractice)
                {
                    ResetMatch();
                }
                else
                {
                    // ReplyToUserCommand(player, "Practice mode is active, cannot restart the match.");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.rrispracc"]);
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("matchzy_map", registerRaw: true)]
        // Note: "map" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnChangeMapCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (context.Args.Length < 1) return;
            var mapName = context.Args[0];
            HandleMapChangeCommand(player, mapName);
        }

        [Command("rmap", registerRaw: true)]
        private void OnMapReloadCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;

            if (!IsPlayerAdmin(player))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            string currentMapName = Core.Engine.GlobalVars.MapName;
            if (long.TryParse(currentMapName, out _))
            { // Check if mapName is a long for workshop map ids
                Core.Engine.ExecuteCommand($"bot_kick");
                Core.Engine.ExecuteCommand($"host_workshop_map \"{currentMapName}\"");
            }
            else
            {
                if (Core.Engine.IsMapValid(currentMapName))
                {
                Core.Engine.ExecuteCommand($"bot_kick");
                Core.Engine.ExecuteCommand($"changelevel \"{currentMapName}\"");
            }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.cc.invalidmap"]);
                }
            }
        }

        [Command("start", registerRaw: true)]
        [CommandAlias("force", registerRaw: true)]
        [CommandAlias("forcestart", registerRaw: true)]
        public void OnStartCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_start", "@css/config"))
            {
                if (isPractice)
                {
                    // ReplyToUserCommand(player, "Cannot start a match while in practice mode. Please use .exitprac command to exit practice mode first!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.startisprac"]);
                    return;
                }
                if (matchStarted)
                {
                    //ReplyToUserCommand(player, "Start command cannot be used if match is already started! If you want to unpause, please use .unpause");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.startmatchstarted"]);
                }
                else
                {
                    //Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has started the game!");
                    PrintToAllChat(Localizer["matchzy.cc.gamestarted"]);
                    HandleMatchStart();
                }
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        /// <summary>
        /// Sends an admin chat message. Requires @css/chat permission.
        /// </summary>
        [Command("asay", registerRaw: true, permission: "@css/chat")]
        public void OnAdminSay(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null)
            {
                Core.PlayerManager.SendChat($"{adminChatPrefix} {string.Join(" ", context.Args)}");
                return;
            }
            if (!IsPlayerAdmin(player, "css_asay", "@css/chat"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            string message = string.Join(" ", context.Args);
            Core.PlayerManager.SendChat($"{adminChatPrefix} {message}");
        }

        [Command("reload_admins", registerRaw: true)]
        public void OnReloadAdmins(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "reload_admins", "@css/config"))
            {
                LoadAdmins();
                UpdatePlayersMap();
            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("match", registerRaw: true)]
        public void OnMatchCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (!IsPlayerAdmin(player, "css_match", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, "MatchZy is already in match mode!");
                ReplyToUserCommand(player, Localizer["matchzy.cc.match"]);
                return;
            }

            StartMatchMode();
        }

        [Command("exitprac", registerRaw: true)]
        public void OnExitPracCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (!IsPlayerAdmin(player, "css_exitprac", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                //ReplyToUserCommand(player, "MatchZy is already in match mode!");
                ReplyToUserCommand(player, Localizer["matchzy.cc.exitprac"]);
                return;
            }

            StartMatchMode();
        }

        [Command("matchzy_rcon", registerRaw: true)]
        // Note: "rcon" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnRconCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (!IsPlayerAdmin(player, "css_rcon", "@css/rcon"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            Core.Engine.ExecuteCommand(string.Join(" ", context.Args));
            // ReplyToUserCommand(player, "Command sent successfully!");
            ReplyToUserCommand(player, Localizer["matchzy.cc.rcon"]);

        }

        [Command("matchzy_help", registerRaw: true)]
        // Note: "help" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnHelpCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            SendAvailableCommandsMessage(player);
        }

        /// <summary>
        /// Toggles playout mode. Requires @css/config permission.
        /// </summary>
        [Command("playout", registerRaw: true, permission: "@css/config")]
        public void OnPlayoutCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (IsPlayerAdmin(player, "css_playout", "@css/config"))
            {
                isPlayOutEnabled = !isPlayOutEnabled;
                string playoutStatus = isPlayOutEnabled ? Localizer["matchzy.cc.enabled"] : Localizer["matchzy.cc.disabled"];
                if (player == null)
                {
                    // ReplyToUserCommand(player, $"Playout is now {playoutStatus}!");
                    ReplyToUserCommand(player, Localizer["matchzy.cc.playout", playoutStatus]);
                }
                else
                {
                    // player.PrintToChat($"{chatPrefix} Playout is now {ChatColors.Green}{playoutStatus}{ChatColors.Default}!");
                    PrintToPlayerChat(player, Localizer["matchzy.cc.playout", playoutStatus]);
                }

                HandlePlayoutConfig();

            }
            else
            {
                SendPlayerNotAdminMessage(player);
            }
        }

        [Command("version", registerRaw: true)]
        public void OnVersionCommand(ICommandContext context)
        {
            string steamInfFilePath = Path.Combine(Core.CSGODirectory, "steam.inf");

            if (!File.Exists(steamInfFilePath))
            {
                context.Reply("Unable to locate steam.inf file!");
                return;
            }
            var steamInfContent = File.ReadAllText(steamInfFilePath);

            Regex regex = new(@"ServerVersion=(\d+)");
            Match match = regex.Match(steamInfContent);

            // Extract the version number
            string? serverVersion = match.Success ? match.Groups[1].Value : null;

            // Currently returning only server version to show server status as available on Get5
            context.Reply((serverVersion != null) ? $"Protocol version {serverVersion} [{serverVersion}/{serverVersion}]" : "Unable to get server version");
        }

        // Overrides noclip console command. Perform the changes on server side.
        public HookResult OnConsoleNoClip(ICommandContext context, IPlayer? player) {
            if (player == null || !player.IsValid || player.Controller.TeamNum == (byte)TeamEnum.Spectator)
                return HookResult.Stop;
            var cheatsConVar = Core.ConVar.Find<bool>("sv_cheats");
            bool cheatsEnabled = cheatsConVar != null ? cheatsConVar.Value : false;
            if (!cheatsEnabled) {
                return HookResult.Stop;
            }

            // inspired by cs2-noclip
            var pawn = player.RequiredPlayerPawn;
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                // Note: In SwiftlyS2, property changes are automatically synchronized, SetStateChanged may not be needed
            } else {
                pawn.MoveType = MoveType_t.MOVETYPE_NOCLIP;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_OBSERVER;
                // Note: In SwiftlyS2, property changes are automatically synchronized, SetStateChanged may not be needed
            }

            return HookResult.Stop;
        }
    }
}
