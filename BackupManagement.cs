using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;


namespace MatchZy
{
    public partial class MatchZy
    {
        public bool isStopCommandAvailable = true;
        public bool pauseAfterRoundRestore = true;
        public string lastBackupFileName = "";
        public string lastMatchZyBackupFileName = "";

        public bool isRoundRestoring = false;
        public bool isRoundRestorePending = false;
        public string pendingRestoreFileName = "";

        public Dictionary<string, bool> stopData = new()
        {
            { "ct", false },
            { "t", false }
        };

        public string backupUploadURL = "";
        public string backupUploadHeaderKey = "";
        public string backupUploadHeaderValue = "";


        public void SetupRoundBackupFile()
        {
            string backupFilePrefix = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}";
            Core.Engine.ExecuteCommand($"mp_backup_round_file {backupFilePrefix}");
        }
        /// <summary>
        /// Stops the current round and prepares for restore. Requires @css/config permission.
        /// </summary>
        [Command("matchzy_stop", registerRaw: true, permission: "@css/config")]
        // Note: "stop" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnStopCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            var player = context.Sender;

            Logger.LogInformation($"[!stop command] Sent by: {player.PlayerID}, TeamNum: {player.RequiredController.TeamNum}, connectedPlayers: {connectedPlayers}");
            if (isStopCommandAvailable && isMatchLive)
            {
                if (IsHalfTimePhase())
                {
                    context.Reply(Localizer["matchzy.backup.stopduringhalftime"]);
                    return;
                }
                if (IsPostGamePhase())
                {
                    context.Reply(Localizer["matchzy.backup.stopmatchended"]);
                    return;
                }
                if (IsTacticalTimeoutActive())
                {
                    context.Reply(Localizer["matchzy.backup.stoptacticaltimeout"]);
                    return;
                }
                if (playerHasTakenDamage && stopCommandNoDamage)
                {
                    context.Reply(Localizer["matchzy.restore.stopcommandrequiresnodamage"]);
                    return;
                }
                string stopTeamName = "";
                string remainingStopTeam = "";
                if ((int)player.RequiredController.TeamNum == 2)
                {
                    stopTeamName = reverseTeamSides["TERRORIST"].teamName;
                    remainingStopTeam = reverseTeamSides["CT"].teamName;
                    if (!stopData["t"])
                    {
                        stopData["t"] = true;
                    }

                }
                else if ((int)player.RequiredController.TeamNum == 3)
                {
                    stopTeamName = reverseTeamSides["CT"].teamName;
                    remainingStopTeam = reverseTeamSides["TERRORIST"].teamName;
                    if (!stopData["ct"])
                    {
                        stopData["ct"] = true;
                    }
                }
                else
                {
                    return;
                }
                if (stopData["t"] && stopData["ct"])
                {
                    if (lastMatchZyBackupFileName != "")
                    {
                        RestoreRoundBackup(player, lastMatchZyBackupFileName);
                    }
                    else
                    {
                        // This should not happen, lastMatchZyBackupFileName should not be empty in a live game!
                        Logger.LogInformation($"[OnStopCommand] lastMatchZyBackupFileName not found, unable to restore round!");
                    }

                }
                else
                {
                    PrintToAllChat(Localizer["matchzy.restore.teamwantstorestore", stopTeamName, remainingStopTeam]);
                }
            }
        }

        /// <summary>
        /// Restores the game to a specific round. Requires @css/config permission.
        /// Usage: !restore &lt;round&gt;
        /// </summary>
        [Command("restore", registerRaw: true, permission: "@css/config")]
        public void OnRestoreCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            var player = context.Sender;
            
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (context.Args.Length >= 1)
            {
                string commandArg = context.Args[0];
                HandleRestoreCommand(player, commandArg);
            }
            else
            {
                context.Reply(Localizer["matchzy.cc.usage", "!restore <round>"]);
            }
        }

        private void HandleRestoreCommand(IPlayer? player, string commandArg)
        {
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (!isMatchLive) return;

            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int roundNumber) && roundNumber >= 0)
                {
                    string round = roundNumber.ToString("D2");
                    string requiredBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                    RestoreRoundBackup(player, requiredBackupFileName);
                }
                else
                {
                    // ReplyToUserCommand(player, $"Invalid value for restore command. Please specify a valid non-negative number. Usage: !restore <round>");
                    player.SendMessage(MessageType.Chat, Localizer["matchzy.backup.restoreinvalidvalue"]);
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: !restore <round>");
                player.SendMessage(MessageType.Chat, Localizer["matchzy.cc.usage", "!restore <round>"]);
            }
        }
        public static string ExtractJsonFileName(string input)
        {
           
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            
            if (!input.Contains('\\') && !input.Contains('/'))
            {
                // If no directory separators are found, return the input as-is
                return input;
            }

            // Find the index of ".json" in the input
            int jsonIndex = input.IndexOf(".json", StringComparison.OrdinalIgnoreCase);
            if (jsonIndex != -1)
            {
               
                int startIndex = input.LastIndexOfAny(new[] { '\\', '/' }, jsonIndex);

               
                if (startIndex >= 0)
                {
                   
                    int length = jsonIndex - startIndex + 5;

                  
                    if (length > 0 && startIndex + 1 + length <= input.Length)
                    {
                        string fileName = input.Substring(startIndex + 1, length);
                        return fileName;
                    }
                }
            }

            return string.Empty;
        }



        private void RestoreRoundBackup(IPlayer? player, string fileName)
        {
            if (player != null)
            {
                if (IsHalfTimePhase())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.restoreduringhalftime"]);
                    return;
                }
                if (IsPostGamePhase())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.restorematchended"]);
                    return;
                }
                if (IsTacticalTimeoutActive())
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.restoretacticaltimeout"]);
                    return;
                }
            }
            string backupFolder = Path.Combine(Core.CSGODirectory, "MatchZyDataBackup");
     
            string filePath = Path.Combine(backupFolder, fileName);
 
            if (!File.Exists(filePath))
            {
                if (player != null)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.backup.restoredoesntexist", fileName]);
                }
                Logger.LogError($"[RestoreRoundBackup FATAL] Required backup data file does not exist! File: {filePath}");
                return;
            }

            var gameRules = GetGameRules();
            bool liveSetupRequired = false;

            // We set active timeouts to false so that timeout does not start after the round has been restored.
            // This is to prevent any buggish behaviour with timeouts (like incorrect timeout used showing, or force-unpausing the match once timeout ends)
            var gameRulesForTimeout = Core.EntitySystem.GetGameRules();
            if (gameRulesForTimeout != null)
            {
                gameRulesForTimeout.CTTimeOutActive = false;
                gameRulesForTimeout.TerroristTimeOutActive = false;
            }

            // Core.Engine.ExecuteCommand($"mp_backup_restore_load_file {fileName}");

            Dictionary<string, string> backupData = new();
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        JsonSerializerOptions options = new()
                        {
                            AllowTrailingCommas = true,
                        };
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                    }
                    else
                    {
                        // Handle the case where the JSON content is empty or null
                        backupData = new();
                    }
                }

                isRoundRestoring = true;

                // MatchID is set first to avoid generating a new one.
                if (backupData.TryGetValue("matchid", out var matchId))
                {
                    liveMatchId = long.Parse(matchId);
                }
                if (backupData.TryGetValue("match_loaded", out var matchLoaded))
                {
                    isMatchSetup = bool.Parse(matchLoaded);
                }
                if (backupData.TryGetValue("match_config", out var matchConfigValue))
                {
                    matchConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MatchConfig>(matchConfigValue)!;
                    SetupRoundBackupFile();
                }
                if (backupData.TryGetValue("team1", out var team1config))
                {
                    matchzyTeam1 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team1config)!;
                }
                if (backupData.TryGetValue("team2", out var team2config))
                {
                    matchzyTeam2 = Newtonsoft.Json.JsonConvert.DeserializeObject<Team>(team2config)!;
                }
                if (backupData.TryGetValue("team1_side", out var team1Side))
                {

                    if (team1Side == "CT")
                    {
                        teamSides[matchzyTeam1] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam2;
                        // SwapSidesInTeamData(false);
                    }
                    else if (team1Side == "TERRORIST")
                    {
                        teamSides[matchzyTeam1] = "TERRORIST";
                        reverseTeamSides["TERRORIST"] = matchzyTeam1;
                        teamSides[matchzyTeam2] = "CT";
                        reverseTeamSides["CT"] = matchzyTeam2;
                        // SwapSidesInTeamData(false);
                    }
                }
                if (backupData.TryGetValue("map_name", out var map_name))
                {
                    if (map_name != Core.Engine.GlobalVars.MapName)
                    {
                        ChangeMap(map_name, 0);
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        // Returning from here, backup will be restored again once the map is changed.
                        return;
                    }
                }

                // This is done after checking map_name so that we load the correct map first
                var gameRulesForWarmup = GetGameRules();
                if (gameRulesForWarmup != null && gameRulesForWarmup.WarmupPeriod)
                {
                    if (!isRoundRestorePending)
                    {
                        isRoundRestorePending = true;
                        pendingRestoreFileName = fileName;
                        PrintToAllChat(Localizer["matchzy.restore.loadedsuccessfully", fileName]);
                        return;
                    }
                    else
                    {
                        liveSetupRequired = true;
                    }
                }
                var gameRulesForTimeouts = GetGameRules();
                if (gameRulesForTimeouts != null)
                {
                    if (backupData.TryGetValue("TerroristTimeOuts", out var terroristTimeouts))
                    {
                        gameRulesForTimeouts.TerroristTimeOuts = int.Parse(terroristTimeouts);
                    }
                    if (backupData.TryGetValue("CTTimeOuts", out var ctTimeouts))
                    {
                        gameRulesForTimeouts.CTTimeOuts = int.Parse(ctTimeouts);
                    }
                }
                if (backupData.TryGetValue("valve_backup", out var valveBackup))
                {
                    string tempFileName = fileName.Replace(".json", ".txt");
                    if (backupData.TryGetValue("round", out var roundNumber))
                    {
                        tempFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{roundNumber}.txt";
                    }
                    string tempFilePath = Path.Combine(Core.CSGODirectory, "csgo", tempFileName);


                    if (!File.Exists(tempFilePath))
                    {
                        File.WriteAllText(tempFilePath, valveBackup);
                    }
                    int restoreTimer = liveSetupRequired ? 2 : 0;
                    if (liveSetupRequired)
                    {
                        Logger.LogInformation($"Game was in warmup, setting up Live!");
                        SetupLiveFlagsAndCfg();
                    }
                    SchedulerService.DelayBySeconds(restoreTimer, () => {
                        string backupFileName = Path.GetFileName(tempFilePath);

                        Core.Engine.ExecuteCommand($"mp_backup_restore_load_file {backupFileName}");
                        StartDemoRecording();
                    });
                    SchedulerService.DelayBySeconds(5, () => File.Delete(tempFilePath));
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[RestoreRoundBackup FATAL] An error occurred: {e.Message}");
                return;
            }

            PrintToAllChat(Localizer["matchzy.restore.restoredsuccessfully", fileName]);
            if (pauseAfterRoundRestore)
            {
                Core.Engine.ExecuteCommand("mp_pause_match;");
                stopData["ct"] = false;
                stopData["t"] = false;
                isPaused = true;
                unpauseData["pauseTeam"] = "RoundRestore";
                if (pausedStateTimer == null)
                {
                    pausedStateTimer = SchedulerService.RepeatBySeconds(chatTimerDelay, SendPausedStateMessage);
                }
            }
        }

        public void CreateMatchZyRoundDataBackup()
        {
            Logger.LogInformation($"[CreateMatchZyRoundDataBackup] isRoundRestoring: {isRoundRestoring} isMatchLive: {isMatchLive}");
            if (!isMatchLive || isRoundRestoring) return;
            try
            {
                (int t1score, int t2score) = GetTeamsScore();
                int roundNumber = t1score + t2score;
                string round = roundNumber.ToString("D2");
                string matchZyBackupFileName = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.json";
                string filePath = Path.Combine(Core.CSGODirectory, "csgo", "MatchZyDataBackup", matchZyBackupFileName);

                string? directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath != null && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // Using SwiftlyS2 entity system
                var gameRules = GetGameRules();
                string lastBackupFilePath = $"matchzy_{liveMatchId}_{matchConfig.CurrentMapNumber}_round{round}.txt"; ;
                bool lastBackupExists = File.Exists(Path.Combine(Core.CSGODirectory, "csgo", lastBackupFilePath));
                lastBackupFilePath = Path.Combine(Core.CSGODirectory, "csgo", lastBackupFilePath);

                string valveBackupContent = lastBackupExists ? File.ReadAllText(lastBackupFilePath) : "";

                Dictionary<string, string> roundData = new()
                    {
                        { "matchid", liveMatchId.ToString() },
                        { "timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                        { "map_name", Core.Engine.GlobalVars.MapName },
                        { "mapnumber", matchConfig.CurrentMapNumber.ToString() },
                        { "round", round },
                        { "team1", GetTeamConfig("team1") },
                        { "team2", GetTeamConfig("team2") },
                        { "team1_name", matchzyTeam1.teamName },
                        { "team1_flag", matchzyTeam1.teamFlag },
                        { "team1_tag", matchzyTeam1.teamTag },
                        { "team1_side", teamSides[matchzyTeam1] },
                        { "team2_name", matchzyTeam2.teamName },
                        { "team2_flag", matchzyTeam2.teamFlag },
                        { "team2_tag", matchzyTeam2.teamTag },
                        { "team2_side", teamSides[matchzyTeam2] },
                        { "team1_score", t1score.ToString() },
                        { "team2_score", t2score.ToString() },
                        { "team1_series_score", matchzyTeam1.seriesScore.ToString() },
                        { "team2_series_score", matchzyTeam2.seriesScore.ToString() },
                        { "TerroristTimeOuts", gameRules?.TerroristTimeOuts.ToString() ?? "0" },
                        { "CTTimeOuts", gameRules?.CTTimeOuts.ToString() ?? "0" },
                        { "match_loaded", isMatchSetup.ToString() },
                        { "match_config", GetMatchConfig() },
                        { "valve_backup", valveBackupContent }
                    };
                JsonSerializerOptions options = new()
                {
                    WriteIndented = true,
                };
                string defaultJson = JsonSerializer.Serialize(roundData, options);

                File.WriteAllText(filePath, defaultJson);

                Task.Run(async () => {
                    await UploadFileAsync(filePath, backupUploadURL, backupUploadHeaderKey, backupUploadHeaderValue, liveMatchId, matchConfig.CurrentMapNumber, roundNumber);
                });

            }
            catch (Exception e)
            {
                Logger.LogError($"[CreateMatchZyRoundDataBackup FATAL] Error creating the JSON file: {e.Message}");
            }
        }

        public List<string> GetBackups(string matchID)
        {
            string backupDir = Path.Combine(Core.CSGODirectory, "csgo", "MatchZyDataBackup");


            if (!Directory.Exists(backupDir))
            {
                return [];
            }

            var directoryInfo = new DirectoryInfo(backupDir);
            var files = directoryInfo.GetFiles();

            var pattern = $"matchzy_{matchID}_";
            var backups = new List<string>();

            foreach (var file in files)
            {
                if (file.Name.Contains(pattern))
                {
                    backups.Add(file.FullName);
                }
            }

            backups.Sort((x, y) => string.Compare(y, x, StringComparison.Ordinal));
            return backups;
        }

        public string GetBackupInfo(string filePath)
        {
            string info = "";
            if (!File.Exists(filePath))
            {
                return "";
            }

            Dictionary<string, string> backupData = new();
            try
            {
                using (StreamReader fileReader = File.OpenText(filePath))
                {
                    string jsonContent = fileReader.ReadToEnd();
                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        return "";
                    }
                    else
                    {
                        JsonSerializerOptions options = new()
                        {
                            AllowTrailingCommas = true,
                        };
                        backupData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();

                    }
                }

                info = $"{filePath.Split("/")[^1]} {backupData["timestamp"]} {backupData["team1_name"]} {backupData["team2_name"]} {backupData["map_name"]} {backupData["team1_score"]} {backupData["team2_score"]}";

            }
            catch (Exception e)
            {
                Logger.LogError($"[GetBackupInfo FATAL] An error occurred: {e.Message}");
                return "";
            }

            return info;
        }

        public string GetMatchConfig()
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(matchConfig);
        }

        public string GetTeamConfig(string team)
        {
            Team teamConfig = team == "team1" ? matchzyTeam1 : matchzyTeam2;
            return Newtonsoft.Json.JsonConvert.SerializeObject(teamConfig);
        }

        [Command("get5_loadbackup", registerRaw: true)]
        [CommandAlias("matchzy_loadbackup", registerRaw: true)]
        public void OnLoadBackupCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            var player = context.Sender;
            
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            
            if (context.Args.Length < 1)
            {
                context.Reply("Usage: !loadbackup <backup_file_name>");
                return;
            }
            
            var fileName = ExtractJsonFileName(string.Join(" ", context.Args));

            RestoreRoundBackup(player, fileName);
        }

        [Command("get5_loadbackup_url", registerRaw: true)]
        [CommandAlias("matchzy_loadbackup_url", registerRaw: true)]
        public void LoadBackupFromURL(ICommandContext context)
        {
            if (context.Sender != null) return; // Only allow from server console

            if (context.Args.Length < 1)
            {
                Logger.LogInformation("[LoadBackupFromURL] Usage: loadbackup_url <url> [headerName] [headerValue]");
                return;
            }

            string url = context.Args[0];

            string headerName = context.Args.Length > 2 ? context.Args[1] : "";
            string headerValue = context.Args.Length > 2 ? context.Args[2] : "";

            Logger.LogInformation($"[LoadBackupFromURL] Backup Restore request received with URL: {url} headerName: {headerName} and headerValue: {headerValue}");

            if (!IsValidUrl(url))
            {
                Logger.LogError($"[LoadBackupFromURL] Invalid URL: {url}. Please provide a valid URL to load the backup!");
                return;
            }
            try
            {
                HttpClient httpClient = new();
                if (headerName != "")
                {
                    httpClient.DefaultRequestHeaders.Add(headerName, headerValue);
                }
                HttpResponseMessage response = httpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    string jsonData = response.Content.ReadAsStringAsync().Result;
                    Logger.LogInformation($"[LoadBackupFromURL] Received following data: {jsonData}");
                    string fileName = Guid.NewGuid().ToString() + ".json";
                    string filePath = Path.Combine(Core.CSGODirectory, "MatchZyDataBackup", fileName);

                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null && !Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                    File.WriteAllText(filePath, jsonData);
                    Logger.LogInformation($"[LoadBackupFromURL] Data saved to: {filePath}");

                    RestoreRoundBackup(null, fileName);
                }
                else
                {
                    Logger.LogError($"[LoadBackupFromURL] HTTP request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[LoadBackupFromURL - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        [Command("get5_listbackups", registerRaw: true)]
        [CommandAlias("matchzy_listbackups", registerRaw: true)]
        public void OnListBackupCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            var player = context.Sender;
            
            if (!IsPlayerAdmin(player, "css_restore", "@css/config"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }
            var matchId = context.Args.Length >= 1 ? context.Args[0] : liveMatchId.ToString();
            List<string> backups = GetBackups(matchId);

            if (backups.Count == 0)
            {
                context.Reply("Found no backup files matching the provided parameters.");
            }

            foreach (string backup in backups)
            {
                string backupInfo = GetBackupInfo(backup);
                if (backupInfo != "")
                {
                    context.Reply(backupInfo);
                }
                else
                {
                    context.Reply(backup);
                }
            }
        }
    }
}
