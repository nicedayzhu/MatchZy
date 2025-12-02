using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Commands;
using System;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using Microsoft.Extensions.Logging;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;

namespace MatchZy
{
    public partial class MatchZy
    {
        public bool isPreVeto = false;
        public bool isVeto = false;
        public int warningsPrinted = 0;
        public int vetoCountdownTime = 5; // In Seconds

        public bool mapChangePending = false;
        // vetoStateTimer is now defined in MatchZy.cs
        public Dictionary<string, int> vetoCaptains = new(){
            {"team1", -1},
            {"team2", -1}
        };

        public TeamEnum lastVetoTeam = TeamEnum.None; // Using None as default

        public void CreateVeto()
        {
            SwapPlayersToTeams();
            vetoCaptains["team1"] = GetTeamCaptain("team1");
            vetoCaptains["team2"] = GetTeamCaptain("team2");
            // Todo: Implement pauseOnVeto CVAR
            // if (pauseOnVeto) {
            //     Core.Engine.ExecuteCommand("mp_pause_match");
            //     isPaused = true;
            //     unpauseData["pauseTeam"] = "Admin";
            // }
            Core.Engine.ExecuteCommand("mp_warmup_end");
            Core.Engine.ExecuteCommand("mp_pause_match");
            isPaused = true;
            unpauseData["pauseTeam"] = "Admin";
            vetoStateTimer?.Cancel();
            vetoStateTimer = SchedulerService.RepeatBySeconds(1, () => {
                if (vetoStateTimer?.Token.IsCancellationRequested == true) return;
                VetoCountdown();
            });
            isVeto = true;
            readyAvailable = false;
            isWarmup = false;
            KillPhaseTimers();
        }

        public void VetoCountdown()
        {
            if (!isVeto)
            {
                warningsPrinted = 0;
                vetoStateTimer?.Cancel();
                vetoStateTimer = null;
                return;
            }
            if (warningsPrinted >= vetoCountdownTime)
            {
                int team1Captain = vetoCaptains["team1"];
                int team2Captain = vetoCaptains["team2"];
                warningsPrinted = 0;
                var team1CaptainPlayer = Core.PlayerManager.GetPlayer(team1Captain);
                var team2CaptainPlayer = Core.PlayerManager.GetPlayer(team2Captain);
                if (team1CaptainPlayer == null || !team1CaptainPlayer.IsValid || team2CaptainPlayer == null || !team2CaptainPlayer.IsValid)
                {
                    AbortVeto();
                    vetoStateTimer = null;
                    return;
                }
                Core.PlayerManager.SendChat($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default}: {ChatColors.Green}{team1CaptainPlayer.RequiredController.PlayerName}{ChatColors.Default}");
                Core.PlayerManager.SendChat($"{chatPrefix} Captain for {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default}: {ChatColors.Green}{team2CaptainPlayer.RequiredController.PlayerName}{ChatColors.Default}");

                HandleVetoStep();
                vetoStateTimer?.Cancel();
                vetoStateTimer = null;
                return;
            }
            warningsPrinted++;
            int secondsRemaining = vetoCountdownTime - warningsPrinted + 1;
            Core.PlayerManager.SendChat($"{chatPrefix} Map selection commencing in {secondsRemaining}");
        }

        public void HandleVetoStep()
        {
            // As long as sides are not set for a map, either give side pick or auto-decide sides and recursively call this.
            if (matchConfig.MapSides.Count < matchConfig.Maplist.Count)
            {
                if (matchConfig.MatchSideType == "standard")
                {
                    TeamEnum otherMatchTeam = lastVetoTeam;
                    if (lastVetoTeam == TeamEnum.T) otherMatchTeam = TeamEnum.CT;
                    else if (lastVetoTeam == TeamEnum.CT) otherMatchTeam = TeamEnum.T;
                    PromptForSideSelectionInChat(otherMatchTeam);
                }
                else
                {
                    HandleAutomaticSideSelection();
                    HandleVetoStep();
                }
            }
            else if (matchConfig.NumMaps > matchConfig.Maplist.Count)
            {
                if (matchConfig.MapsLeftInVetoPool.Count == 1)
                {
                    // Only 1 map left in the pool, add it by deduction and determine knife logic.
                    string mapName = matchConfig.MapsLeftInVetoPool[0];
                    PickMap(mapName, 0);
                    HandleAutomaticSideSelection();
                    FinishVeto();
                }
                else
                {
                    // More than 1 map in the pool and not all maps are picked; present choices as determine by config.
                    PromptForMapSelectionInChat(GetCurrentMapSelectionOption());
                }
            }
            else
            {
                FinishVeto();
            }
        }

        public void PromptForMapSelectionInChat(string option) {
            string action = "";
            int client = -1;
            string stepMessage = "";
            switch (option) 
            {
                case "team1_ban":
                    action = $"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} must now {ChatColors.Red}BAN{ChatColors.Default} a map.";
                    client = vetoCaptains["team1"];
                    stepMessage = $"Use .ban <map> to ban a map";
                    break;
                case "team2_ban":
                    action = $"{ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} must now {ChatColors.Red}BAN{ChatColors.Default} a map.";
                    client = vetoCaptains["team2"];
                    stepMessage = $"Use .ban <map> to ban a map";
                    break;                                                       
                case "team1_pick":
                    action = $"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} must now {ChatColors.Green}PICK{ChatColors.Default} a map to play as map {matchConfig.Maplist.Count + 1}.";
                    client = vetoCaptains["team1"];
                    stepMessage = $"Use .pick <map> to pick a map.";
                    break;
                case "team2_pick":
                    action = $"{ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} must now {ChatColors.Green}PICK{ChatColors.Default} a map to play as map {matchConfig.Maplist.Count + 1}.";
                    client = vetoCaptains["team2"];
                    stepMessage = $"Use .pick <map> to pick a map.";
                    break;
            }
            var captainPlayer = Core.PlayerManager.GetPlayer(client);
            if (captainPlayer == null || !captainPlayer.IsValid)
            {
                Logger.LogInformation($"[PromptForMapSelectionInChat] Invalid captain found with ID: {client}");
                return;
            }
            Core.PlayerManager.SendChat($"{chatPrefix} {action}");

            string mapListAsString = string.Join(", ", matchConfig.MapsLeftInVetoPool);
            Core.PlayerManager.SendChat($"{chatPrefix} Remaining Maps: {mapListAsString}");

            captainPlayer.SendMessage(MessageType.Chat, $"{chatPrefix} {stepMessage}");
        }

        [Command("pick", registerRaw: true)]
        public void OnPickMapCommand(ICommandContext context) {
            if (context.Sender == null || !context.Sender.IsValid) return;
            if (context.Args.Length < 1) return;
            string mapArg = context.Args[0];
            HandeMapPickCommand(context.Sender, mapArg);
        }

        [Command("ban", registerRaw: true)]
        public void OnBanMapCommand(ICommandContext context) { 
            if (context.Sender == null || !context.Sender.IsValid) return;
            if (context.Args.Length < 1) return;
            string mapArg = context.Args[0];
            HandeMapBanCommand(context.Sender, mapArg);
        }

        public void HandeMapBanCommand(IPlayer player, string map)
        {
            if (!isVeto || SidePickPending() || player == null || map == null) return;

            int playerTeam = (int)player.RequiredController.TeamNum;
            string currentTeamToBan;
            switch (GetCurrentMapSelectionOption()) {
                case "team1_ban":
                    currentTeamToBan = "team1";
                    break;
                case "team2_ban":
                    currentTeamToBan = "team2";
                    break;
                case "invalid":
                case "team1_pick":
                case "team2_pick":
                default:
                    return;
            }

            if (player.PlayerID != vetoCaptains[currentTeamToBan]) return;

            if (!BanMap(map, playerTeam)) {
                PrintToPlayerChat(player, $"{map} is not a valid map.");
            } else {
                HandleVetoStep();
            }
        }

        public void HandeMapPickCommand(IPlayer player, string map)
        {
            if (!isVeto || SidePickPending() || player == null || !player.IsValid || map == null) return;

            int playerTeam = (int)player.RequiredController.TeamNum;
            string currentTeamToPick;
            switch (GetCurrentMapSelectionOption()) 
            {
                case "team1_pick":
                    currentTeamToPick = "team1";
                    break;
                case "team2_pick":
                    currentTeamToPick = "team2";
                    break;
                case "invalid":
                case "team1_ban":
                case "team2_ban":
                default:
                    return;
            }

            if (player.PlayerID != vetoCaptains[currentTeamToPick]) return;

            if (!PickMap(map, playerTeam)) {
                PrintToPlayerChat(player, $"{map} is not a valid map.");
            } else {
                HandleVetoStep();
            }
        }


        public bool PickMap(string mapName, int team) {
            (bool mapRemoved, string mapRemovedName) = RemoveMapFromMapPool(mapName);

            if (!mapRemoved) return false;

            Team matchzyTeam = matchzyTeam1;

            if (team != 0) {
                matchzyTeam = (team == 2) ? reverseTeamSides["TERRORIST"] : reverseTeamSides["CT"];
                Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} picked {ChatColors.Green}{mapRemovedName}{ChatColors.Default} as map {matchConfig.Maplist.Count + 1}");
            }

            matchConfig.Maplist.Add(mapRemovedName);

            var mapPickedEvent = new MatchZyMapPickedEvent
            {
                MatchId = liveMatchId,
                MapName = mapRemovedName,
                MapNumber = matchConfig.Maplist.Count,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
            };

            Task.Run(async () => {
                await SendEventAsync(mapPickedEvent);
            });

            lastVetoTeam = (TeamEnum)team;

            return true;
        }

        public bool BanMap(string mapName, int team) 
        {
            (bool mapRemoved, string mapRemovedName) = RemoveMapFromMapPool(mapName);

            if (!mapRemoved) return false;

            Team matchzyTeam = matchzyTeam1;

            if (team != 0) {
                matchzyTeam = (team == 2) ? reverseTeamSides["TERRORIST"] : reverseTeamSides["CT"];
                Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} banned {ChatColors.LightRed}{mapRemovedName}{ChatColors.Default}");
            }

            var mapMapVetoedEvent = new MatchZyMapVetoedEvent
            {
                MatchId = liveMatchId,
                MapName = mapRemovedName,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
            };

            Task.Run(async () => {
                await SendEventAsync(mapMapVetoedEvent);
            });

            lastVetoTeam = (TeamEnum)team;

            return true;
        }

        public void AbortVeto()
        {
            // Todo: Add AbortVeto() when captain is disconnecting in-between veto
            Core.PlayerManager.SendChat($"{chatPrefix} A team captain left during map selection. Map selection is paused.");
            Core.PlayerManager.SendChat($"{chatPrefix} Type .ready when you are ready to resume map selection.");
            isPreVeto = true;
            isVeto = false;
            if (isPaused)
            {
                UnpauseMatch();
            }
            vetoCaptains = new(){
                {"team1", -1},
                {"team2", -1}
            };
            foreach (var key in playerReadyStatus.Keys) {
                playerReadyStatus[key] = false;
            }
            readyAvailable = true;
            isWarmup = true;
            StartWarmup();
        }

        public void FinishVeto() 
        {
            Core.PlayerManager.SendChat($"{chatPrefix} The maps have been decided:");
            matchConfig.MapsLeftInVetoPool.Clear();

            if (isPaused) {
                UnpauseMatch();
            }

            // If a team has a map advantage, don't print that map.
            int mapNumber = matchConfig.CurrentMapNumber;

            for (int i = mapNumber; i < matchConfig.Maplist.Count; i++) {
                Core.PlayerManager.SendChat($"{chatPrefix} Map {i + 1 - mapNumber}: {matchConfig.Maplist[i]}.");
            }

            string currentMapName = Core.Engine.GlobalVars.MapName;
            string mapToPlay = matchConfig.Maplist[0];

            // In case the sides don't match after selection, we check it here before writing the backup.
            // Also required if the map doesn't need to change.
            SetMapSides();
            ExecuteChangedConvars();
            foreach (var key in playerReadyStatus.Keys) {
                playerReadyStatus[key] = false;
            }

            if (IsMapReloadRequiredForGameMode(matchConfig.Wingman) || mapReloadRequired || currentMapName != mapToPlay) {

                SetCorrectGameMode();
                float delay = 7.0f;
                mapChangePending = true;
                // Todo: Implement displayGotvVeto cvar
                // if (displayGotvVeto) {
                //     delay += GetTvDelay();
                // }
                SchedulerService.DelayBySeconds(delay, () => {
                    string nextMap = matchConfig.Maplist[matchConfig.CurrentMapNumber];
                    ChangeMap(nextMap, 3);
                });
            }
            isWarmup = true;
            readyAvailable = true;
            isPreVeto = false;
            isVeto = false;
            StartWarmup();
        }


        public int GetTeamCaptain(string team)
        {
            Team matchzyTeam = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            int teamSide = teamSides[matchzyTeam] == "CT" ? 3 : 2;
            var allPlayers = Core.PlayerManager.GetAllPlayers();
            foreach (var player in allPlayers)
            {
                if (!player.IsValid || player.IsFakeClient) continue;
                if ((int)player.RequiredController.TeamNum == teamSide) return player.PlayerID;
            }

            return -1;
        }

        public void SwapPlayersToTeams()
        {
            var allPlayers = Core.PlayerManager.GetAllPlayers();
            foreach (var player in allPlayers)
            {
                if (!player.IsValid || player.IsFakeClient) continue;
                player.SwitchTeam(GetPlayerTeam(player));
            }
        }

        public string GetCurrentMapSelectionOption()
        {
            // Number of banned maps must be: original pool - (current pool + picked);
            // 7 - (4 + 2) = 1; if 4 are left and 2 were picked, 1 must have been banned.
            int mapsBanned = matchConfig.MapsPool.Count - (matchConfig.MapsLeftInVetoPool.Count + matchConfig.Maplist.Count);
            int index = matchConfig.Maplist.Count + mapsBanned;
            if (index > matchConfig.MapBanOrder.Count - 1)
            {
                return "invalid";
            }
            return matchConfig.MapBanOrder[index];
        }
        public bool SidePickPending() {
            return matchConfig.MapSides.Count < matchConfig.Maplist.Count && matchConfig.MatchSideType == "standard";
        }

        public void HandleAutomaticSideSelection() {
            if (matchConfig.MatchSideType == "random") {
                matchConfig.MapSides.Add(new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t");
            } else {
                matchConfig.MapSides.Add(matchConfig.MatchSideType == "never_knife" ? "team1_ct" : "knife");
            }
        }

        public (bool, string) RemoveMapFromMapPool(string mapName) {
            string mapRemoved = "";
            int eraseIndex = -1;
            // First check if we have a single match with a substring.
            if (mapName.Length >= 4) {
                for (int i = 0; i < matchConfig.MapsLeftInVetoPool.Count; i++) {
                    mapRemoved = matchConfig.MapsLeftInVetoPool[i];
                    if (mapRemoved.IndexOf(mapName, StringComparison.OrdinalIgnoreCase) > -1) {
                        if (eraseIndex >= 0) {
                            eraseIndex = -1;  // If more than one match, reset and break.
                            break;
                        }
                        eraseIndex = i;
                    }
                }
            }
            // If no match or more than one match on substring, restart, this time only matching the full string.
            if (eraseIndex == -1) {
                for (int i = 0; i < matchConfig.MapsLeftInVetoPool.Count; i++) {
                    mapRemoved = matchConfig.MapsLeftInVetoPool[i];
                    if (mapRemoved == mapName) {
                        eraseIndex = i;
                        break;
                    }
                }
            }
            if (eraseIndex >= 0) {
                mapRemoved = matchConfig.MapsLeftInVetoPool[eraseIndex];
                matchConfig.MapsLeftInVetoPool.RemoveAt(eraseIndex);
                return (true, mapRemoved);
            }
            if (mapName.IndexOf("cobble", StringComparison.OrdinalIgnoreCase) > -1) {
                // Because Cobblestone is the only map that's actually misspelled, we re-run the code if the input contained
                // "cobble" but there was no match, this time using "cbble" instead, which will match de_cbble.
                return RemoveMapFromMapPool("cbble");
            }
            return (false, mapRemoved);
        }
        public void PromptForSideSelectionInChat(TeamEnum team) {
            string mapName = matchConfig.Maplist[^1];
            Team matchzyTeam = (team == TeamEnum.CT) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
            string teamString = (matchzyTeam == matchzyTeam1) ? "team1" : "team2";
            
            Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} must now pick a side to play on {ChatColors.Green}{mapName}{ChatColors.Default}");

            int client = vetoCaptains[teamString];
            var captainPlayer = Core.PlayerManager.GetPlayer(client);
            if (captainPlayer == null || !captainPlayer.IsValid) return;

            captainPlayer.SendMessage(MessageType.Chat, $"{chatPrefix} Use .ct or .t to pick a side");
        }

        public bool ValidateMapBanLogic() 
        {
            int numberOfPicks = 0;
            string option;
            for (int i = 0; i < matchConfig.MapBanOrder.Count; i++) {
                option = matchConfig.MapBanOrder[i];
                if (option == "team1_pick" || option == "team2_pick") {
                    numberOfPicks++;
                }
                if (numberOfPicks == matchConfig.NumMaps || i == matchConfig.MapsPool.Count - 2) {
                    break;
                }
            }

            // Example: In a Bo3, at least 2 of the options must be picks to avoid randomly selecting map order of remaining maps.
            if (matchConfig.NumMaps > 1 && numberOfPicks < matchConfig.NumMaps - 1) {
                Logger.LogInformation($"[ValidateMapBanLogic] In a series of {matchConfig.NumMaps} maps, at least {matchConfig.NumMaps - 1} veto options must be picks. Found {numberOfPicks} pick(s).");
                return false;
            }

            if (matchConfig.MapsPool.Count - 1 != matchConfig.MapBanOrder.Count && numberOfPicks != matchConfig.NumMaps) {
                // Example: Map pool of 7 requires 6 picks/bans *unless* we have picks for all maps.
                Logger.LogInformation($"[ValidateMapBanLogic] The number of maps in the pool {matchConfig.MapsPool.Count} must be one larger than the number of map picks/bans {matchConfig.MapBanOrder.Count}, unless the number of picks {numberOfPicks} matches the series length {matchConfig.NumMaps}.");
                return false;
            }

            return true;
        }

        public void HandleSideChoice(TeamEnum side, int client) {
            if (!SidePickPending()) {
                // No side selection is done by players in this case.
                return;
            }
            Team team = matchzyTeam1;

            if (lastVetoTeam == TeamEnum.T) team = reverseTeamSides["CT"];
            else if (lastVetoTeam == TeamEnum.CT) team = reverseTeamSides["TERRORIST"];

            string pickingTeam = (team == matchzyTeam1) ? "team1" : "team2";

            if (client != vetoCaptains[pickingTeam]) {
                // Only captain can select a side.
                return;
            }
            PickSide(side, pickingTeam);
            HandleVetoStep();
        }

        public void PickSide(TeamEnum side, string team) {
            if (side == TeamEnum.CT) {
                matchConfig.MapSides.Add(team == "team1" ? "team1_ct" : "team1_t");
            } else {
                matchConfig.MapSides.Add(team == "team1" ? "team1_t" : "team1_ct");
            }

            int mapNumber = matchConfig.Maplist.Count - 1;

            string mapName = matchConfig.Maplist[mapNumber];

            string sideFormatted = (side == TeamEnum.CT) ? "CT" : "T";

            Team matchzyTeam = (team == "team1") ? matchzyTeam1 : matchzyTeam2;

            Core.PlayerManager.SendChat($"{chatPrefix} {ChatColors.Green}{matchzyTeam.teamName}{ChatColors.Default} elected to start as {ChatColors.Green}{sideFormatted}{ChatColors.Default} on {ChatColors.Green}{mapName}{ChatColors.Default}.");

            var sidePickedEvent = new MatchZySidePickedEvent
            {
                MatchId = liveMatchId,
                MapName = mapName,
                MapNumber = matchConfig.Maplist.Count,
                Team = (matchzyTeam == matchzyTeam1) ? "team1" : "team2",
                Side = sideFormatted.ToLower()
            };
            Task.Run(async () => {
                await SendEventAsync(sidePickedEvent);
            });
        }

        public void GenerateDefaultVetoSetup()
        {
            Team startingVetoTeam = matchzyTeam1;
            if (lastVetoTeam == TeamEnum.CT)
            {
                if (reverseTeamSides["CT"] == matchzyTeam1) startingVetoTeam = matchzyTeam2;
                if (reverseTeamSides["CT"] == matchzyTeam2) startingVetoTeam = matchzyTeam1;
            }
            else if (lastVetoTeam == TeamEnum.T)
            {
                if (reverseTeamSides["TERRORIST"] == matchzyTeam1) startingVetoTeam = matchzyTeam2;
                if (reverseTeamSides["TERRORIST"] == matchzyTeam2) startingVetoTeam = matchzyTeam1;
            }
            switch (matchConfig.NumMaps)
            {
                case 1:
                    int numberOfBans = matchConfig.MapsPool.Count - 1;  // Last map either played by default or ignored.
                    for (int i = 0; i < numberOfBans; i++)
                    {
                        matchConfig.MapBanOrder.Add(
                        i % 2 == 0
                            ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                            : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                    }
                    break;

                case 2:
                    if (matchConfig.MapsPool.Count < 5)
                    {
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_pick"
                                                                        : "team2_pick");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_pick"
                                                                        : "team1_pick");
                    }
                    else
                    {
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_ban"
                                                                        : "team2_ban");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_ban"
                                                                        : "team1_ban");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team1_pick"
                                                                        : "team2_pick");
                        matchConfig.MapBanOrder.Add(startingVetoTeam == matchzyTeam1 ? "team2_pick"
                                                                        : "team1_pick");
                    }
                    break;

                default:
                    // Bo3 with 7 maps as an example.
                    // For this to work, a Bo3 requires a map pool of at least 5.
                    if (matchConfig.MapsPool.Count >= matchConfig.NumMaps + 2)
                    {  // 7 >= 3 + 2
                        int numberOfPicks = matchConfig.NumMaps - 1;    // 2 picks in a Bo3
                        // Determine how many bans before we start picking (may be 0):
                        int numberOfStartBans = matchConfig.MapsPool.Count - (matchConfig.NumMaps + 2);  // 7 - (3 + 2) = 2
                        if (numberOfStartBans > 0)
                        {                                          // == 2
                            for (int i = 0; i < numberOfStartBans; i++)
                            {
                                matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                    ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                                    : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                            }
                        }

                        // After the initial bans, add the picks:
                        for (int i = 0; i < numberOfPicks; i++)
                        {
                            matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                ? (startingVetoTeam == matchzyTeam1 ? "team1_pick" : "team2_pick")
                                : (startingVetoTeam == matchzyTeam1 ? "team2_pick" : "team1_pick"));
                        }

                        // Determine how many bans to append to the end (may be 0):
                        int numberOfEndBans = matchConfig.MapsPool.Count - 1 - numberOfPicks - numberOfStartBans;  // 7 - 2 - 2 - 1 = 2
                        if (numberOfEndBans > 0)
                        {                                                     // == 2
                            for (int i = 0; i < numberOfEndBans; i++)
                            {
                                matchConfig.MapBanOrder.Add(
                                matchConfig.MapBanOrder.Count % 2 == 0
                                    ? (startingVetoTeam == matchzyTeam1 ? "team1_ban" : "team2_ban")
                                    : (startingVetoTeam == matchzyTeam1 ? "team2_ban" : "team1_ban"));
                            }
                        }
                    }
                    else
                    {
                        // else we just alternate picks and ignore the last map.
                        for (int i = 0; i < matchConfig.NumMaps; i++)
                        {
                            matchConfig.MapBanOrder.Add(
                                i % 2 == 0
                                ? (startingVetoTeam == matchzyTeam1 ? "team1_pick" : "team2_pick")
                                : (startingVetoTeam == matchzyTeam1 ? "team2_pick" : "team1_pick"));
                        }
                    }
                    break;
            }
        }

        public void SkipVeto()
        {
            isWarmup = true;
            readyAvailable = true;
            isPreVeto = false;
            isVeto = false;
            StartWarmup();
        }
    }
}

