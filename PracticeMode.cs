using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.GameEventDefinitions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using CollisionGroup = SwiftlyS2.Shared.Natives.CollisionGroup;
using System.Reflection;


namespace MatchZy
{
    public class Position
    {

        public Vector PlayerPosition { get; private set; }
        public QAngle PlayerAngle { get; private set; }

        // Copy constructor
        public Position(Position other)
        {
            PlayerPosition = other.PlayerPosition;
            PlayerAngle = other.PlayerAngle;
        }

        public Position(Vector playerPosition, QAngle playerAngle)
        {
            // Create deep copies of the Vector and QAngle objects
            PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
            PlayerAngle = new QAngle(playerAngle.Pitch, playerAngle.Yaw, playerAngle.Roll);
        }

        public void Teleport(IPlayer player)
        {
            if (player.PlayerPawn != null)
            {
                player.Teleport(PlayerPosition, PlayerAngle, new Vector(0, 0, 0));
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            Position otherPosition = (Position)obj;

            return PlayerPosition.X == otherPosition.PlayerPosition.X &&
                PlayerPosition.Y == otherPosition.PlayerPosition.Y &&
                PlayerAngle.Pitch == otherPosition.PlayerAngle.Pitch &&
                PlayerAngle.Yaw == otherPosition.PlayerAngle.Yaw &&
                PlayerAngle.Roll == otherPosition.PlayerAngle.Roll;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + PlayerPosition.X.GetHashCode();
                hash = hash * 23 + PlayerPosition.Y.GetHashCode();
                hash = hash * 23 + PlayerPosition.Z.GetHashCode();
                hash = hash * 23 + PlayerAngle.Pitch.GetHashCode();
                hash = hash * 23 + PlayerAngle.Yaw.GetHashCode();
                hash = hash * 23 + PlayerAngle.Roll.GetHashCode();
                return hash;
            }
        }
    }

    public static class StringSimilarity
    {
        // Dice coefficient function
        public static double DiceCoefficient(string s1, string s2)
        {
            var bigrams1 = GetBigrams(s1);
            var bigrams2 = GetBigrams(s2);

            int intersection = bigrams1.Intersect(bigrams2).Count();
            return (2.0 * intersection) / (bigrams1.Count + bigrams2.Count);
        }

        // Get bigrams function
        private static List<string> GetBigrams(string input)
        {
            var bigrams = new List<string>();
            for (int i = 0; i < input.Length - 1; i++)
            {
                bigrams.Add(input.Substring(i, 2));
            }
            return bigrams;
        }

        /// <summary>
        /// Finds the name from a list of names that is nearest to the input name using the Dice coefficient.
        /// </summary>
        /// <param name="inputName">The input name to match.</param>
        /// <param name="names">The list of names to search from.</param>
        /// <returns>The nearest matching name from the list.</returns>
        public static string FindNearestName(string inputName, List<string> names)
        {
            if (inputName.Length == 1)
            {
                // If input name is a single character, find the name that starts with the same character
                var matchingName = names.FirstOrDefault(name => name.StartsWith(inputName, StringComparison.OrdinalIgnoreCase));
                if (matchingName != null)
                {
                    return matchingName;
                }
            }
            // Otherwise, use the Dice coefficient to find the nearest name
            string nearestName = names.OrderByDescending(name => DiceCoefficient(inputName, name)).FirstOrDefault() ?? inputName;
            return nearestName;
        }
    }

    public partial class MatchZy
    {
        int maxLastGrenadesSavedLimit = 512;
        Dictionary<int, List<GrenadeThrownData>> lastGrenadesData = new();
        Dictionary<int, Dictionary<string, GrenadeThrownData>> nadeSpecificLastGrenadeData = new();
        Dictionary<int, DateTime> lastGrenadeThrownTime = new();
        Dictionary<int, PlayerPracticeTimer> playerTimers = new();
        Dictionary<int, PlayerLocationData> savedPlayerLocationData = new();

        public Dictionary<byte, List<Position>> spawnsData = GetEmptySpawnsData();

        public Dictionary<byte, List<Position>> coachSpawns = GetEmptySpawnsData();

        // Helper method to get savednades.json path (runtime data, use PluginDataDirectory)
        private string GetSavedNadesPath()
        {
            string savednadesPath = Path.Combine(Core.PluginDataDirectory, "savednades.json");
            // Ensure directory exists
            Directory.CreateDirectory(Core.PluginDataDirectory);
            return savednadesPath;
        }

        // This map stores the bots which are being used in prac (probably spawned using .bot). Key is the userid of the bot.
        public Dictionary<int, Dictionary<string, object>> pracUsedBots = new Dictionary<int, Dictionary<string, object>>();

        // Timer is now implemented using SwiftlyS2 scheduler in MatchZy.cs

        public bool isSpawningBot;

        public bool isDryRun = false;

        public List<int> noFlashList = new List<int>();

        public static Dictionary<byte, List<Position>> GetEmptySpawnsData()
        {
            return new Dictionary<byte, List<Position>>
            {
                { (byte)TeamEnum.CT, new List<Position>() },
                { (byte)TeamEnum.T, new List<Position>() }
            };
        }

        public void StartPracticeMode()
        {
            if (matchStarted) return;
            isPractice = true;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;

            string cfgExecPath = EnsureCfgInGameCfgDirectory("prac.cfg");
            string absolutePath = GetConfigFilePath("prac.cfg");

            if (File.Exists(absolutePath))
            {
                Logger.LogInformation($"[StartWarmup] Starting Practice Mode! Executing Practice CFG via exec {cfgExecPath} (source: {absolutePath})");
                Core.Engine.ExecuteCommand($"exec {cfgExecPath}");
            }
            else
            {
                Logger.LogInformation($"[StartWarmup] Starting Practice Mode! Practice CFG not found in {absolutePath}, using default CFG!");
                Core.Engine.ExecuteCommand("""sv_cheats "true"; mp_force_pick_time "0"; bot_quota "0"; sv_showimpacts "1"; mp_limitteams "0"; sv_deadtalk "true"; sv_full_alltalk "true"; sv_ignoregrenaderadio "false"; mp_forcecamera "0"; sv_grenade_trajectory_prac_pipreview "true"; sv_grenade_trajectory_prac_trailtime "3"; sv_infinite_ammo "1"; weapon_auto_cleanup_time "15"; weapon_max_before_cleanup "30"; mp_buy_anywhere "1"; mp_maxmoney "9999999"; mp_startmoney "9999999";""");
                Core.Engine.ExecuteCommand("""mp_weapons_allow_typecount "-1"; mp_death_drop_breachcharge "false"; mp_death_drop_defuser "false"; mp_death_drop_taser "false"; mp_drop_knife_enable "true"; mp_death_drop_grenade "0"; ammo_grenade_limit_total "5"; mp_defuser_allocation "2"; mp_free_armor "2"; mp_ct_default_grenades "weapon_incgrenade weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_ct_default_primary "weapon_m4a1";""");
                Core.Engine.ExecuteCommand("""mp_t_default_grenades "weapon_molotov weapon_hegrenade weapon_smokegrenade weapon_flashbang weapon_decoy"; mp_t_default_primary "weapon_ak47"; mp_warmup_online_enabled "true"; mp_warmup_pausetimer "1"; mp_warmup_start; bot_quota_mode fill; mp_solid_teammates 2; mp_autoteambalance false; mp_teammates_are_enemies false; buddha 1; buddha_ignore_bots 1; buddha_reset_hp 100;""");
            }
            GetSpawns();
            Core.PlayerManager.SendChat($"{chatPrefix} Practice mode loaded!");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Spawns: {ChatColors.Default}.spawn, .ctspawn, .tspawn, .bestspawn, .worstspawn");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Bots: {ChatColors.Default}.bot, .nobots, .crouchbot, .boost, .crouchboost");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Nades: {ChatColors.Default}.loadnade, .savenade, .importnade, .listnades");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Nade Throw: {ChatColors.Default}.rethrow, .throwindex <index>, .lastindex, .delay <number>");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.clear, .fastforward, .last, .back, .solid, .impacts, .traj");
            // On new line to prevent text cutting off
            Core.PlayerManager.SendChat($" {ChatColors.Green}Utility & Toggles: {ChatColors.Default}.savepos, .loadpos");
            Core.PlayerManager.SendChat($" {ChatColors.Green}Sides & Others: {ChatColors.Default}.ct, .t, .spec, .fas, .god, .dryrun, .break, .exitprac");
        }

        public void GetSpawns()
        {
            // Resetting spawn data to avoid any glitches
            spawnsData = GetEmptySpawnsData();

            int minPriority = 1;

            var spawnsct = Core.EntitySystem.GetAllEntitiesByDesignerName<CInfoPlayerCounterterrorist>("info_player_counterterrorist");
            foreach (var spawn in spawnsct)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority < minPriority)
                {
                    minPriority = spawn.Priority;
                }
            }

            foreach (var spawn in spawnsct)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority == minPriority)
                {
                    var origin = spawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
                    var rotation = spawn.CBodyComponent?.SceneNode?.AbsRotation ?? new QAngle(0, 0, 0);
                    spawnsData[(byte)TeamEnum.CT].Add(new Position(origin, rotation));
                }
            }

            var spawnst = Core.EntitySystem.GetAllEntitiesByDesignerName<CInfoPlayerTerrorist>("info_player_terrorist");
            foreach (var spawn in spawnst)
            {
                if (spawn.IsValid && spawn.Enabled && spawn.Priority == minPriority)
                {
                    var origin = spawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
                    var rotation = spawn.CBodyComponent?.SceneNode?.AbsRotation ?? new QAngle(0, 0, 0);
                    spawnsData[(byte)TeamEnum.T].Add(new Position(origin, rotation));
                }
            }

            GetCoachSpawns();
        }

        private void HandleSpawnCommand(IPlayer? player, string commandArg, byte teamNum, string command)
        {
            if (!isPractice || player == null || !player.IsValid) return;
            if (teamNum != (byte)TeamEnum.T && teamNum != (byte)TeamEnum.CT) return;
            if (!string.IsNullOrWhiteSpace(commandArg))
            {
                if (int.TryParse(commandArg, out int spawnNumber) && spawnNumber >= 1)
                {
                    // Adjusting the spawnNumber according to the array index.
                    spawnNumber -= 1;
                    if (spawnsData.ContainsKey(teamNum) && spawnsData[teamNum].Count <= spawnNumber) return;
                    player.Teleport(spawnsData[teamNum][spawnNumber].PlayerPosition, spawnsData[teamNum][spawnNumber].PlayerAngle, new Vector(0, 0, 0));
                    player.SendChat(Localizer["matchzy.pm.movedtospawn", $"{spawnNumber + 1}/{spawnsData[teamNum].Count}"]);
                }
                else
                {
                    player.SendChat(Localizer["matchzy.pm.negativenumber"]);
                    return;
                }
            }
            else
            {
                player.SendChat(Localizer["matchzy.cc.usage", $"!{command} <number>"]);
            }
        }

        private string GetNadeType(string nadeName)
        {
            switch (nadeName)
            {
                case "weapon_flashbang":
                    return "Flash";
                case "weapon_smokegrenade":
                    return "Smoke";
                case "weapon_hegrenade":
                    return "HE";
                case "weapon_decoy":
                    return "Decoy";
                case "weapon_molotov":
                    return "Molly";
                case "weapon_incgrenade":
                    return "Molly";
                default:
                    return "";
            }
        }

        private void HandleSaveNadeCommand(IPlayer? player, string saveNadeName)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Split string into 2 parts
                string[] lineupUserString = saveNadeName.Split(' ');
                string lineupName = lineupUserString[0];
                string lineupDesc = string.Join(" ", lineupUserString, 1, lineupUserString.Length - 1);

                // Get player info: steamid, pos, ang
                string playerSteamID;
                if(isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player!.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                QAngle playerAngle = player!.RequiredPlayerPawn.EyeAngles!;
                Vector playerPos = player.RequiredPlayerPawn.CBodyComponent!.SceneNode!.AbsOrigin;
                string currentMapName = Core.Engine.GlobalVars.MapName;
                string nadeType = GetNadeType(player.RequiredPlayerPawn.WeaponServices!.ActiveWeapon.Value!.DesignerName);

                // Define the file path (use PluginDataDirectory for runtime data)
                string savednadesPath = GetSavedNadesPath();

                // Check if the file exists, if not, create it with an empty JSON object
                if (!File.Exists(savednadesPath))
                {
                    File.WriteAllText(savednadesPath, "{}");
                }

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup name already exists for the given SteamID
                    if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                    {
                        // Check if the lineup already exists on the same map
                        if (savedNadesDict[playerSteamID][lineupName]["Map"] == currentMapName)
                        {
                            // Lineup already exists on the same map, reply to the user and return
                            // ReplyToUserCommand(player, $"Lineup already exists! Please use a different name or use .delnade <nade>");
                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupissaved"]);
                            return;
                        }
                    }

                    // Update or add the new lineup information
                    if (!savedNadesDict.ContainsKey(playerSteamID))
                    {
                        savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                    }

                    savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                    {
                        { "LineupPos", $"{playerPos.X} {playerPos.Y} {playerPos.Z+4}" },
                        { "LineupAng", $"{playerAngle.Pitch} {playerAngle.Yaw} {playerAngle.Roll}" },
                        { "Desc", lineupDesc },
                        { "Map", currentMapName },
                        { "Type", nadeType }
                    };

                    // Serialize the updated dictionary back to JSON
                    string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                    // Write the updated JSON content back to the file
                    File.WriteAllText(savednadesPath, updatedJson);

                    player.SendMessage(MessageType.Chat, $"{chatPrefix} {lineupName} saved successfully!");
                    Core.PlayerManager.SendChat($"{chatPrefix} {player.RequiredController.PlayerName} saved lineup: {lineupName} {playerPos} {playerAngle}");
                }
                catch (JsonException ex)
                {
                    Logger.LogError($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .savenade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".savenade <name>"]);
            }
        }

        private void HandleDeleteNadeCommand(IPlayer? player, string saveNadeName)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            if (!string.IsNullOrWhiteSpace(saveNadeName))
            {
                // Grab player steamid
                string playerSteamID;
                if(isSaveNadesAsGlobalEnabled == false)
                {
                    playerSteamID = player.SteamID.ToString();
                }
                else
                {
                    playerSteamID = "default";
                }

                // Define the file path (use PluginDataDirectory for runtime data)
                string savednadesPath = GetSavedNadesPath();

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    // Check if the lineup exists for the given SteamID and name
                    if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(saveNadeName))
                    {
                        var lineupInfo = savedNadesDict[playerSteamID][saveNadeName];

                        // Check if the lineup is for the current maps
                        if (lineupInfo.ContainsKey("Map") && lineupInfo["Map"] == Core.Engine.GlobalVars.MapName)
                        {
                            // Remove the specified lineup
                            savedNadesDict[playerSteamID].Remove(saveNadeName);

                            // Serialize the updated dictionary back to JSON
                            string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                            // Write the updated JSON content back to the file
                            File.WriteAllText(savednadesPath, updatedJson);

                            // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' deleted successfully.");
                            ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdeletesuccess", saveNadeName]);
                        }
                        else
                        {
                            // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' not found on the current map!");
                            ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfoundonmap", saveNadeName]);
                        }
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Lineup '{saveNadeName}' not found!");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupnotfound", saveNadeName]);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.LogError($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .delnade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".delnade <name>"]);
            }
        }

        private void HandleImportNadeCommand(IPlayer? player, string saveNadeCode)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            if (!string.IsNullOrWhiteSpace(saveNadeCode))
            {
                try
                {
                    // Split the code into parts
                    string[] parts = saveNadeCode.Split(' ');

                    // Check if there are enough parts
                    if (parts.Length == 7)
                    {
                        // Extract name, pos, and ang from the parts
                        string lineupName = parts[0].Trim();
                        string[] posAng = parts.Skip(1).Select(p => p.Replace(",", "")).ToArray(); // Replace ',' with '' for proper parsing

                        // Get player info: steamid
                        string playerSteamID = player.SteamID.ToString();
                        string currentMapName = Core.Engine.GlobalVars.MapName;

                        // Define the file path
                        string savednadesfileName = "MatchZy/savednades.json";
                        string savednadesPath = Path.Join(Core.CSGODirectory + "/cfg", savednadesfileName);

                        // Read existing JSON content
                        string existingJson = File.ReadAllText(savednadesPath);

                        //Console.WriteLine($"Existing JSON Content: {existingJson}");

                        // Deserialize the existing JSON content
                        var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                            ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                        // Check if the lineup name already exists for the given SteamID on the same map
                        if (savedNadesDict.ContainsKey(playerSteamID) && savedNadesDict[playerSteamID].ContainsKey(lineupName))
                        {
                            var existingLineup = savedNadesDict[playerSteamID][lineupName];
                            if (existingLineup.ContainsKey("Map") && existingLineup["Map"] == currentMapName)
                            {
                                // Lineup already exists on the same map, reply to the user and return
                                // ReplyToUserCommand(player, $"Lineup '{lineupName}' already exists! Please use a different name or use .delnade <nade>");
                                ReplyToUserCommand(player, Localizer["matchzy.pm.lineupalreadyexists", lineupName]);
                                return;
                            }
                        }

                        // Update or add the new lineup information
                        if (!savedNadesDict.ContainsKey(playerSteamID))
                        {
                            savedNadesDict[playerSteamID] = new Dictionary<string, Dictionary<string, string>>();
                        }

                        savedNadesDict[playerSteamID][lineupName] = new Dictionary<string, string>
                        {
                            { "LineupPos", $"{posAng[0]} {posAng[1]} {posAng[2]}" },
                            { "LineupAng", $"{posAng[3]} {posAng[4]} {posAng[5]}" },
                            { "Desc", "" },
                            { "Map", currentMapName }
                        };

                        // Serialize the updated dictionary back to JSON
                        string updatedJson = JsonSerializer.Serialize(savedNadesDict, new JsonSerializerOptions { WriteIndented = true });

                        // Write the updated JSON content back to the file
                        File.WriteAllText(savednadesPath, updatedJson);

                        // ReplyToUserCommand(player, $"Lineup '{lineupName}' imported and saved successfully.");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupimportedsuccess"]);
                    }
                    else
                    {
                        // ReplyToUserCommand(player, $"Invalid code format. Please provide a valid code with name, pos, and ang.");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupinvalidcode"]);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.LogError($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Usage: .importnade <code>");
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $".importnade <code>"]);
            }
        }

        private void HandleListNadesCommand(IPlayer? player, string nadeFilter)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            // Define the file path
            string savednadesfileName = "MatchZy/savednades.json";
            string savednadesPath = Path.Join(Core.CSGODirectory + "/cfg", savednadesfileName);

            try
            {
                // Read existing JSON content
                string existingJson = File.ReadAllText(savednadesPath);

                //Console.WriteLine($"Existing JSON Content: {existingJson}");

                // Deserialize the existing JSON content
                var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                    ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                player.SendMessage(MessageType.Chat, $"\x0D-----All Saved Lineups for \x06{Core.Engine.GlobalVars.MapName}\x0D-----");

                // List lineups for the specified player
                ListLineups(player, "default", Core.Engine.GlobalVars.MapName, savedNadesDict, nadeFilter);

                // List lineups for the current player
                ListLineups(player, player.SteamID.ToString(), Core.Engine.GlobalVars.MapName, savedNadesDict, nadeFilter);
            }
            catch (JsonException ex)
            {
                Logger.LogError($"Error handling JSON: {ex.Message}");
                ReplyToUserCommand(player, $"Error handling JSON. Please check the server logs.");
            }
        }

        private void ListLineups(IPlayer player, string steamID, string mapName, Dictionary<string, Dictionary<string, Dictionary<string, string>>> savedNadesDict, string nadeFilter)
        {
            if (savedNadesDict.ContainsKey(steamID))
            {
                foreach (var kvp in savedNadesDict[steamID])
                {
                    // Check if a filter is provided, and if so, apply the filter
                    if ((string.IsNullOrWhiteSpace(nadeFilter) || kvp.Key.Contains(nadeFilter, StringComparison.OrdinalIgnoreCase))
                        && kvp.Value.ContainsKey("Map") && kvp.Value["Map"] == mapName)
                    {
                        // Format and reply with the lineup name
                        ReplyToUserCommand(player, $"\x06[{kvp.Value["Type"]}] \x0D.loadnade \x06{kvp.Key}");
                    }
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"No saved lineups found for the specified SteamID: ({steamID}).");
                ReplyToUserCommand(player, Localizer["matchzy.pm.nosavedlineups", steamID]);

            }
        }

        private void HandleLoadNadeCommand(IPlayer? player, string loadNadeName)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            if (!string.IsNullOrWhiteSpace(loadNadeName))
            {
                // Get player info: steamid
                string playerSteamID = player.SteamID.ToString();

                // Define the file path (use PluginDataDirectory for runtime data)
                string savednadesPath = GetSavedNadesPath();

                try
                {
                    // Read existing JSON content
                    string existingJson = File.ReadAllText(savednadesPath);

                    //Console.WriteLine($"Existing JSON Content: {existingJson}");

                    // Deserialize the existing JSON content
                    var savedNadesDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(existingJson)
                                        ?? new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();

                    bool lineupFound = false;
                    bool lineupOnWrongMap = false;

                    // Check for the lineup in the player's steamID and the fixed steamID
                    foreach (string currentSteamID in new[] { playerSteamID, "default" })
                    {
                        if (savedNadesDict.ContainsKey(currentSteamID))
                        {
                            // Filter nade names based on the current map
                            var nadeNamesOnCurrentMap = savedNadesDict[currentSteamID]
                                .Where(n => n.Value.ContainsKey("Map") && n.Value["Map"] == Core.Engine.GlobalVars.MapName)
                                .Select(n => n.Key)
                                .ToList();

                            // Find the nearest matching name
                            string nearestName = StringSimilarity.FindNearestName(loadNadeName, nadeNamesOnCurrentMap);

                            if (savedNadesDict[currentSteamID].ContainsKey(nearestName))
                            {
                                var lineupInfo = savedNadesDict[currentSteamID][nearestName];

                                // Check if the lineup contains the "Map" key and if it matches the current map
                                if (lineupInfo.ContainsKey("Map") && lineupInfo["Map"] == Core.Engine.GlobalVars.MapName)
                                {
                                    // Extract position and angle from the lineup information
                                    string[] posArray = lineupInfo["LineupPos"].Split(' ');
                                    string[] angArray = lineupInfo["LineupAng"].Split(' ');

                                    // Parse position and angle
                                    Vector loadedPlayerPos = new Vector(float.Parse(posArray[0]), float.Parse(posArray[1]), float.Parse(posArray[2]));
                                    QAngle loadedPlayerAngle = new QAngle(float.Parse(angArray[0]), float.Parse(angArray[1]), float.Parse(angArray[2]));

                                    // Teleport player
                                    player!.RequiredPlayerPawn.Teleport(loadedPlayerPos, loadedPlayerAngle, new Vector(0, 0, 0));

                                    // Change player inv slot
                                    switch (lineupInfo["Type"])
                                    {
                                        case "Flash":
                                            player.ExecuteCommand("slot7");
                                            break;
                                        case "Smoke":
                                            player.ExecuteCommand("slot8");
                                            break;
                                        case "HE":
                                            player.ExecuteCommand("slot6");
                                            break;
                                        case "Decoy":
                                            player.ExecuteCommand("slot9");
                                            break;
                                        case "Molly":
                                            player.ExecuteCommand("slot10");
                                            break;
                                        case "":
                                            player.ExecuteCommand("slot8");
                                            break;
                                    }

                                    // Extract description, if available
                                    string lineupDesc = lineupInfo.ContainsKey("Desc") ? lineupInfo["Desc"] : null;

                                    // Print messages
                                    // ReplyToUserCommand(player, $"Lineup {ChatColors.Green}{nearestName}{ChatColors.Default} loaded successfully!");
                                    ReplyToUserCommand(player, Localizer["matchzy.pm.lineuploadedsuccess", nearestName]);

                                    if (!string.IsNullOrWhiteSpace(lineupDesc))
                                    {
                                        player.SendCenter($"{lineupDesc}");
                                        // ReplyToUserCommand(player, $"Description: {ChatColors.Green}{lineupDesc}{ChatColors.Default}");
                                        ReplyToUserCommand(player, Localizer["matchzy.pm.lineupdesc", lineupDesc]);
                                    }

                                    lineupFound = true;
                                    break;
                                }
                                else
                                {
                                    // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{nearestName}{ChatColor.Default} not found on the current map!");
                                    ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfoundonmap", nearestName]);
                                    lineupOnWrongMap = true;
                                }
                            }
                        }
                    }

                    if (!lineupFound && !lineupOnWrongMap)
                    {
                        // Lineup not found
                        // ReplyToUserCommand(player, $"Nade {ChatColor.Green}{loadNadeName}{ChatColor.Default} not found!");
                        ReplyToUserCommand(player, Localizer["matchzy.pm.nadenotfound", loadNadeName]);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.LogError($"Error handling JSON: {ex.Message}");
                }
            }
            else
            {
                // ReplyToUserCommand(player, $"Nade not found! Usage: .loadnade <name>");
                ReplyToUserCommand(player, Localizer["matchzy.pm.loadnadenotfound"]);
            }
        }

        public void ShowSpawnBeam(Position spawn, SwiftlyS2.Shared.Natives.Color color)
        {
            CBeam? beam = Core.EntitySystem.CreateEntity<CBeam>();
            if (beam == null)
            {
                Logger.LogError($"Failed to create beam for the spawn");
                return;
            }

            beam.LifeState = 1;
            beam.Width = 5;
            beam.Render = color;

            beam.EndPos.X = spawn.PlayerPosition.X;
            beam.EndPos.Y = spawn.PlayerPosition.Y;
            beam.EndPos.Z = spawn.PlayerPosition.Z + 100.0f;

            beam.Teleport(spawn.PlayerPosition, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            beam.DispatchSpawn();
        }

        public void RemoveSpawnBeams()
        {
            var beams = Core.EntitySystem.GetAllEntitiesByDesignerName<CBeam>("beam");
            foreach (var beam in beams)
            {
                if (beam == null) continue;
                beam?.AcceptInput("Kill", "");
            }
        }

        /// <summary>
        /// Toggles god mode (invincibility) for the player in practice mode.
        /// </summary>
        [Command("matchzy_god", registerRaw: true)]
        // Note: "god" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnGodCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            
            var player = context.Sender;
            var pawn = player.PlayerPawn;
            if (pawn == null) return;
            
            int currentHP = pawn.Health;
            
            if(currentHP > 100)
            {
                pawn.Health = 100;
                context.Reply($"God is {Localizer["matchzy.cc.disabled"]}");
                return;
            }
            else
            {
                pawn.Health = 2147483647; // max 32bit int
                context.Reply($"God is {Localizer["matchzy.cc.enabled"]}");
                return;
            }
        }

        /// <summary>
        /// Starts practice mode. Requires @css/map or @custom/prac permission.
        /// </summary>
        [Command("prac", registerRaw: true, permission: "@css/map")]
        [CommandAlias("tactics", registerRaw: true)]
        public void OnPracCommand(ICommandContext context)
        {
            if (!IsPlayerAdmin(context.Sender, "css_prac", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(context.Sender);
                return;
            }

            if (matchStarted)
            {
                context.Reply(Localizer["matchzy.pm.pracmatchstarted"]);
                return;
            }
    
            // if (isPractice)
            // {
            //     StartMatchMode();
            //     return;
            // }

            StartPracticeMode();
        }

        /// <summary>
        /// Starts dry run mode in practice. Requires @css/map or @custom/prac permission.
        /// </summary>
        [Command("dry", registerRaw: true, permission: "@css/map")]
        [CommandAlias("dryrun", registerRaw: true)]
        public void OnDryRunCommand(ICommandContext context)
        {
            if (!IsPlayerAdmin(context.Sender, "css_prac", "@css/map", "@custom/prac")) {
                SendPlayerNotAdminMessage(context.Sender);
                return;
            }
            if (matchStarted)
            {
                context.Reply(Localizer["matchzy.pm.dryrunmatchstarted"]);
                return;
            }
            if (!isPractice)
            {
                context.Reply(Localizer["matchzy.pm.dryrunnopractice"]);
                return;
            }

            Core.Engine.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
            noFlashList = new();

            ExecUnpracCommands();
            ExecDryRunCFG();

            isDryRun = true;
        }

        /// <summary>
        /// Teleports player to a specific spawn position in practice mode.
        /// Usage: !spawn &lt;number&gt;
        /// </summary>
        /// <summary>
        /// Teleports player to a specific spawn position in practice mode.
        /// Usage: !spawn &lt;number&gt;
        /// </summary>
        [Command("spawn", registerRaw: true)]
        public void OnSpawnCommand(ICommandContext context)
        {
            if (!isPractice) return;
            if (context.Sender == null || !context.Sender.IsValid) return;
            
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            var player = context.Sender;
            if (player.PlayerPawn == null) return;

            if (context.Args.Length >= 1)
            {
                string commandArg = context.Args[0];
                HandleSpawnCommand(player, commandArg, (byte)player.Controller.TeamNum, "spawn");
            }
            else
            {
                context.Reply(Localizer["matchzy.cc.usage", $"!spawn <round>"]);
            }
        }

        /// <summary>
        /// Teleports player to a specific CT spawn position in practice mode.
        /// Usage: !ctspawn &lt;number&gt;
        /// </summary>
        /// <summary>
        /// Teleports player to a specific CT spawn position in practice mode.
        /// Usage: !ctspawn &lt;number&gt;
        /// </summary>
        [Command("ctspawn", registerRaw: true)]
        public void OnCtSpawnCommand(ICommandContext context)
        {
            if (!isPractice) return;
            if (context.Sender == null || !context.Sender.IsValid) return;
            
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            var player = context.Sender;
            if (player.PlayerPawn == null) return;

            if (context.Args.Length >= 1)
            {
                string commandArg = context.Args[0];
                HandleSpawnCommand(player, commandArg, (byte)TeamEnum.CT, "ctspawn");
            }
            else
            {
                context.Reply(Localizer["matchzy.cc.usage", $"!ctspawn <round>"]);
            }
        }

        /// <summary>
        /// Teleports player to a specific T spawn position in practice mode.
        /// Usage: !tspawn &lt;number&gt;
        /// </summary>
        [Command("tspawn", registerRaw: true)]
        public void OnTSpawnCommand(ICommandContext context)
        {
            if (!isPractice) return;
            if (context.Sender == null || !context.Sender.IsValid) return;
            
            // Checking if any of the Position List is empty
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            var player = context.Sender;
            if (player.PlayerPawn == null) return;

            if (context.Args.Length >= 1)
            {
                string commandArg = context.Args[0];
                HandleSpawnCommand(player, commandArg, (byte)TeamEnum.T, "tspawn");
            }
            else
            {
                context.Reply(Localizer["matchzy.cc.usage", $"!tspawn <round>"]);
            }
        }

        [Command("bot", registerRaw: true)]
        public void OnBotCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            AddBot(context.Sender, false);
        }

        /// <summary>
        /// Spawns a crouching bot for boost practice in practice mode.
        /// </summary>
        [Command("cbot", registerRaw: true)]
        [CommandAlias("crouchbot", registerRaw: true)]
        public void OnCrouchBotCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            AddBot(context.Sender, true);
        }

        [Command("boost", registerRaw: true)]
        public void OnBoostBotCommand(ICommandContext context)
        {
            if (!isPractice) return;
            if (context.Sender == null || !context.Sender.IsValid) return;
            AddBot(context.Sender, false);
            SchedulerService.DelayBySeconds(0.2f, () => ElevatePlayer(context.Sender));
        }

        [Command("crouchboost", registerRaw: true)]
        public void OnCrouchBoostBotCommand(ICommandContext context)
        {
            if (!isPractice) return;
            if (context.Sender == null || !context.Sender.IsValid) return;
            AddBot(context.Sender, true);
            SchedulerService.DelayBySeconds(0.2f, () => ElevatePlayer(context.Sender));
        }

        private void AddBot(IPlayer? player, bool crouch)
        {
            try
            {
                if (!isPractice || player == null || !player.IsValid) return;
                var movementService = player.RequiredPlayerPawn.MovementServices;

                if (movementService != null && movementService.DuckAmount == 1)
                {
                    // Player was crouching while using .bot command
                    crouch = true;
                }
                isSpawningBot = true;
                // !bot/.bot command is made using a lot of workarounds, as there is no direct way to create a bot entity and spawn it in CSSharp
                // Hence there can be some issues with this approach. This will be revamped when we will be able to fake clients.
                if (player.RequiredController.TeamNum == (byte)TeamEnum.CT)
                {
                    Core.Engine.ExecuteCommand("bot_join_team T");
                    Core.Engine.ExecuteCommand("bot_add_t");
                }
                else if (player.RequiredController.TeamNum == (byte)TeamEnum.T)
                {
                    Core.Engine.ExecuteCommand("bot_join_team CT");
                    Core.Engine.ExecuteCommand("bot_add_ct");
                }
                
                // Once bot is added, we teleport it to the requested position
                SchedulerService.DelayBySeconds(0.1f, () => SpawnBot(player, crouch));
                Core.Engine.ExecuteCommand("bot_stop 1");
                Core.Engine.ExecuteCommand("bot_freeze 1");
                Core.Engine.ExecuteCommand("bot_zombie 1");
            }
            catch (JsonException ex)
            {
                Logger.LogInformation($"[AddBot - FATAL] Error: {ex.Message}");
            }
        }

        private void SpawnBot(IPlayer botOwner, bool crouch)
        {
            try 
            {
                if (botOwner == null || !botOwner.IsValid) return;
                var playerEntities = Core.PlayerManager.GetAllPlayers();
                bool unusedBotFound = false;
                foreach (var tempPlayer in playerEntities)
                {
                    if (tempPlayer == null || !tempPlayer.IsValid) continue;
                    if (!tempPlayer.IsFakeClient || tempPlayer.RequiredController.IsHLTV) continue;
                    int playerId = tempPlayer.PlayerID;
                    if (!pracUsedBots.ContainsKey(playerId) && unusedBotFound)
                    {
                        Logger.LogInformation($"UNUSED BOT FOUND: {playerId} EXECUTING: kickid {playerId}");
                        // Kicking the unused bot. We have to do this because bot_add_t/bot_add_ct may add multiple bots but we need only 1, so we kick the remaining unused ones
                        Core.Engine.ExecuteCommand($"kickid {tempPlayer.PlayerID}");
                        continue;
                    }
                    if (pracUsedBots.ContainsKey(playerId))
                    {
                        continue;
                    }
                    pracUsedBots[playerId] = new Dictionary<string, object>();

                    var origin = botOwner.RequiredPlayerPawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
                    var rotation = botOwner.RequiredPlayerPawn.CBodyComponent?.SceneNode?.AbsRotation ?? new QAngle(0, 0, 0);
                    Position botOwnerPosition = new Position(origin, rotation);
                    // Add key-value pairs to the inner dictionary
                    pracUsedBots[playerId]["controller"] = tempPlayer;
                    pracUsedBots[playerId]["position"] = botOwnerPosition;
                    pracUsedBots[playerId]["owner"] = botOwner;
                    pracUsedBots[playerId]["crouchstate"] = crouch;

                    if (crouch)
                    {
                        var movementService = tempPlayer.RequiredPlayerPawn.MovementServices;
                        if (movementService != null)
                        {
                            SchedulerService.DelayBySeconds(0.1f, () => movementService.DuckAmount = 1);
                            SchedulerService.DelayBySeconds(0.2f, () => {
                                if (tempPlayer.RequiredPlayerPawn.Bot != null)
                                    tempPlayer.RequiredPlayerPawn.Bot.IsCrouching = true;
                            });
                        }
                    }

                    tempPlayer.RequiredPlayerPawn.Teleport(botOwnerPosition.PlayerPosition, botOwnerPosition.PlayerAngle, new Vector(0, 0, 0));
                    TemporarilyDisableCollisions(botOwner, tempPlayer);
                    unusedBotFound = true;
                }
                if (!unusedBotFound) {
                    Core.PlayerManager.SendChat($"{chatPrefix} Cannot add bots, the team is full! Use .nobots to remove the current bots.");
                }

                isSpawningBot = false;
            }
            catch (JsonException ex)
            {
                Logger.LogInformation($"[SpawnBot - FATAL] Error: {ex.Message}");
            }
        }

        public void TemporarilyDisableCollisions(IPlayer p1, IPlayer p2)
        {
            Logger.LogInformation($"[TemporarilyDisableCollisions] Disabling {p1.RequiredController.PlayerName} {p2.RequiredController.PlayerName}");
            // Reference collision code: https://github.com/Source2ZE/CS2Fixes/blob/f009e399ff23a81915e5a2b2afda20da2ba93ada/src/events.cpp#L150
            byte debrisGroup = (byte)CollisionGroup.Debris;
            p1.RequiredPlayerPawn.Collision.CollisionAttribute.CollisionGroup = debrisGroup;
            p1.RequiredPlayerPawn.Collision.CollisionGroup = debrisGroup;
            p2.RequiredPlayerPawn.Collision.CollisionAttribute.CollisionGroup = debrisGroup;
            p2.RequiredPlayerPawn.Collision.CollisionGroup = debrisGroup;
            // TODO: call CollisionRulesChanged
            var p1p = p1.RequiredPlayerPawn;
            var p2p = p2.RequiredPlayerPawn;
            collisionGroupTimer?.Cancel();
            collisionGroupTimer = SchedulerService.RepeatBySeconds(0.1f, () =>
            {
                if (collisionGroupTimer?.Token.IsCancellationRequested == true)
                {
                    return;
                }
                if (!p1.IsValid || !p2.IsValid)
                {
                    Logger.LogInformation($"player handle invalid p1 {p1.IsValid} p2 {p2.IsValid}");
                    return;
                }

                if (!DoPlayersCollide(p1, p2))
                {
                    // Once they no longer collide 
                    byte playerMovementGroup = (byte)CollisionGroup.PlayerMovement;
                    p1p.Collision.CollisionAttribute.CollisionGroup = playerMovementGroup;
                    p1p.Collision.CollisionGroup = playerMovementGroup;
                    p2p.Collision.CollisionAttribute.CollisionGroup = playerMovementGroup;
                    p2p.Collision.CollisionGroup = playerMovementGroup;
                    // TODO: call CollisionRulesChanged
                    collisionGroupTimer?.Cancel();
                }
            });
        }

        public bool DoPlayersCollide(IPlayer p1, IPlayer p2)
        {
            Vector p1min, p1max, p2min, p2max;
            var p1pawn = p1.RequiredPlayerPawn;
            var p2pawn = p2.RequiredPlayerPawn;
            var p1pos = p1pawn.AbsOrigin;
            var p2pos = p2pawn.AbsOrigin;
            p1min = p1pawn.Collision.Mins + (p1pos ?? new Vector(0, 0, 0));
            p1max = p1pawn.Collision.Maxs + (p1pos ?? new Vector(0, 0, 0));
            p2min = p2pawn.Collision.Mins + (p2pos ?? new Vector(0, 0, 0));
            p2max = p2pawn.Collision.Maxs + (p2pos ?? new Vector(0, 0, 0));

            return p1min.X <= p2max.X && p1max.X >= p2min.X &&
                    p1min.Y <= p2max.Y && p1max.Y >= p2min.Y &&
                    p1min.Z <= p2max.Z && p1max.Z >= p2min.Z;
        }

        private static void ElevatePlayer(IPlayer? player)
        {
            if (player == null || !player.IsValid) return;
            var pawn = player.RequiredPlayerPawn;
            var origin = pawn.CBodyComponent!.SceneNode!.AbsOrigin;
            player.RequiredPlayerPawn.Teleport(new Vector(origin.X, origin.Y, origin.Z + 80.0f), pawn.EyeAngles!, new Vector(0, 0, 0));
        }

        public HookResult OnPlayerSpawn(EventPlayerSpawn @event)
        {
            IPlayer? player = @event.UserIdPlayer;
            if (player == null || !player.IsValid) return HookResult.Continue;

            // disable noclip on spawn -- all no clipping functionality is handled by the plugin!
            // Movement adjustments are consistent with cs2-noclip.
            var pawn = player.RequiredPlayerPawn;
            if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) {
                pawn.MoveType = MoveType_t.MOVETYPE_WALK;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_WALK;
                // Note: In SwiftlyS2, property changes are automatically synchronized, SetStateChanged may not be needed
            }

            // Update coach checking logic for SwiftlyS2
            if (matchStarted && (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player)))
            {
                if (player.RequiredController.InGameMoneyServices != null)
                {
                    player.RequiredController.InGameMoneyServices.Account = 0;
                }
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
                pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
                
                return HookResult.Continue;
            }

            // Respawing a bot where it was actually spawned during practice session
            if (isPractice && player.IsValid && player.IsFakeClient)
            {
                int playerId = player.PlayerID;
                if (pracUsedBots.ContainsKey(playerId))
                {
                    if (pracUsedBots.ContainsKey(playerId) && pracUsedBots[playerId]["position"] is Position botPosition)
                    {
                        player.RequiredPlayerPawn.Teleport(botPosition.PlayerPosition, botPosition.PlayerAngle, new Vector(0, 0, 0));
                        bool isCrouched = (bool)pracUsedBots[playerId]["crouchstate"];
                        if (isCrouched)
                        {
                            player.RequiredPlayerPawn.Flags |= 0x2; // FL_DUCKING flag
                            SchedulerService.DelayBySeconds(0.1f, () => player.RequiredPlayerPawn.MovementServices.DuckAmount = 1);
                            SchedulerService.DelayBySeconds(0.2f, () => {
                                if (player.RequiredPlayerPawn.Bot != null)
                                    player.RequiredPlayerPawn.Bot.IsCrouching = true;
                            });
                        }
                        if (pracUsedBots[playerId]["owner"] is IPlayer botOwner) {
                            SchedulerService.DelayBySeconds(0.2f, () => TemporarilyDisableCollisions(botOwner, player));
                        } 
                    }
                }
                else if (!isSpawningBot && !player.RequiredController.IsHLTV)
                {
                    // Bot has been spawned, but we didn't spawn it, so kick it.
                    // This most often happens when a player changes team with bot_quota_mode set to fill
                    // Extra bots from bot_add are already handled in SpawnBot
                    // Delay this for a few seconds to prevent crashes
                    Logger.LogInformation($"Kicking bot {player.RequiredController.PlayerName} due to erroneous spawning");
                    SchedulerService.DelayBySeconds(2.5f, () =>
                    {
                        Core.Engine.ExecuteCommand($"bot_kick {player.RequiredController.PlayerName}");
                    });
                }
            }

            return HookResult.Continue;
        }

        /// <summary>
        /// Removes all bots in practice mode.
        /// </summary>
        [Command("nobots", registerRaw: true)]
        public void OnNoBotsCommand(ICommandContext context)
        {
            if (!isPractice) return;
            Core.Engine.ExecuteCommand("bot_kick");
            pracUsedBots = new Dictionary<int, Dictionary<string, object>>();
        }

        /// <summary>
        /// Toggles friendly fire in practice mode.
        /// </summary>
        [Command("ff", registerRaw: true)]
        public void OnFFCommand(ICommandContext context)
        {
            if (!isPractice) return;

            Dictionary<int, MoveType_t> preFastForwardMoveTypes = new();

            // Using Core.PlayerManager for SwiftlyS2
            var allPlayers = Core.PlayerManager.GetAllPlayers();
            foreach (var player in allPlayers) {
                if(!player.IsValid || !player.RequiredController.PawnIsAlive) continue;
                var pawn = player.RequiredPlayerPawn;
                preFastForwardMoveTypes[player.PlayerID] = pawn.MoveType;
                pawn.MoveType = MoveType_t.MOVETYPE_NONE;
            }

            Core.PlayerManager.SendChat($"{chatPrefix} Fastforwarding 20 seconds!");
            Core.Engine.ExecuteCommand("host_timescale 10");
            SchedulerService.DelayBySeconds(20.0f, () => {
                ResetFastForward(preFastForwardMoveTypes);
            });
        }

        /// <summary>
        /// Fast forwards time by 20 seconds in practice mode.
        /// </summary>
        [Command("fastforward", registerRaw: true)]
        public void OnFastForwardCommand(ICommandContext context)
        {
            OnFFCommand(context);
        }

        public void ResetFastForward(Dictionary<int, MoveType_t> preFastForwardMoveTypes) {
            if (!isPractice) return;
            Core.Engine.ExecuteCommand("host_timescale 1");
            // Using Core.PlayerManager for SwiftlyS2
            var allPlayers = Core.PlayerManager.GetAllPlayers();
            foreach (var player in allPlayers) {
                if(!player.IsValid || !player.RequiredController.PawnIsAlive) continue;
                if (preFastForwardMoveTypes.ContainsKey(player.PlayerID))
                {
                    var pawn = player.RequiredPlayerPawn;
                    pawn.MoveType = preFastForwardMoveTypes[player.PlayerID];
                    player.PlayerPawn.MoveType = preFastForwardMoveTypes[player.PlayerID];
                }
            }
        }

        /// <summary>
        /// Clears all grenades and entities in practice mode.
        /// </summary>
        [Command("matchzy_clear", registerRaw: true)]
        // Note: "clear" alias removed to avoid conflict with SwiftlyS2/system commands
        public void OnClearCommand(ICommandContext context)
        {
            RemoveGrenadeEntities();
        }

        /// <summary>
        /// Switches player to spectator team in practice mode.
        /// </summary>
        [Command("spec", registerRaw: true)]
        public void OnSpecCommand(ICommandContext context) {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            SideSwitchCommand(context.Sender, TeamEnum.Spectator);
        }

        [Command("fas", registerRaw: true)]
        [CommandAlias("watchme", registerRaw: true)]
        public void OnFASCommand(ICommandContext context) {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            // SideSwitchCommand(context.Sender, TeamEnum.None); // TeamEnum.None doesn't exist, using Spectator instead
            SideSwitchCommand(context.Sender, TeamEnum.Spectator);
        }

        /// <summary>
        /// Toggles no-flash (immunity to flashbangs) in practice mode.
        /// </summary>
        [Command("noblind", registerRaw: true)]
        [CommandAlias("noflash", registerRaw: true)]
        public void OnNoFlashCommand(ICommandContext context) {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            int playerId = context.Sender.PlayerID;

            if (noFlashList.Contains(playerId))
            {
                noFlashList.Remove(playerId);
                context.Reply("Disabled noflash.");
            } else {
                noFlashList.Add(playerId);
                context.Reply("Enabled noflash. Use .noflash again to disable.");
                // Using NextTick as NextFrame equivalent in SwiftlyS2
                SchedulerService.NextTick(() => KillFlashEffect(context.Sender));
            }

        }

        /// <summary>
        /// Breaks all breakable props in practice mode.
        /// </summary>
        [Command("break", registerRaw: true)]
        public void OnBreakCommand(ICommandContext context)
        {
            if (!isPractice) return;
            var entities = Core.EntitySystem.GetAllEntitiesByDesignerName<CBreakable>("prop_dynamic")
                .Concat(Core.EntitySystem.GetAllEntitiesByDesignerName<CBreakable>("func_breakable"));
            foreach (var entity in entities)
            {
                entity.AcceptInput("Break", "");
            }
        }

        public void KillFlashEffect(IPlayer player) {
            var playerPawn = player.PlayerPawn;
            if (playerPawn == null) return;
            Logger.LogInformation($"[KillFlashEffect] Killing flash effect for player: {player.Controller.PlayerName}");
            playerPawn.FlashMaxAlpha = 0.5f;
        }

        // TeamEnum.Spectator is used as a special value to mean force all other players to spectator
        private void SideSwitchCommand(IPlayer player, TeamEnum team) {
          if (team != TeamEnum.Spectator) {
            if((int)player.Controller.TeamNum == (int)TeamEnum.Spectator) {
              player.SendChat(Localizer["matchzy.pm.spectatorbroken"]);
              return;
            }
            player.ChangeTeam(team);
            return;
          }
          var allPlayers = Core.PlayerManager.GetAllPlayers();
          foreach (var x in allPlayers) { 
              if(x.IsValid && !x.IsFakeClient && x.PlayerID != player.PlayerID) {
                x.ChangeTeam(TeamEnum.Spectator);
              }
            }
        }

        public void RemoveGrenadeEntities()
        {
            if (!isPractice) return;
            var smokes = Core.EntitySystem.GetAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("smokegrenade_projectile");
            foreach (var entity in smokes)
            {
                entity?.AcceptInput("Kill", "");
            }
            var mollys = Core.EntitySystem.GetAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("molotov_projectile");
            foreach (var entity in mollys)
            {
                entity?.AcceptInput("Kill", "");
            }
            var inferno = Core.EntitySystem.GetAllEntitiesByDesignerName<CSmokeGrenadeProjectile>("inferno");
            foreach (var entity in inferno)
            {
                entity?.AcceptInput("Kill", "");
            }
        }

        public void ExecDryRunCFG()
        {
            string cfgPath = GetConfigFilePath("dryrun.cfg");
    
            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(cfgPath)) {
                Logger.LogInformation($"[ExecDryRunCFG] Starting Dryrun! Executing Dryrun CFG from {cfgPath}");
                Core.Engine.ExecuteCommand($"exec {cfgPath}");
                Core.Engine.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Logger.LogInformation($"[ExecDryRunCFG] Starting Dryrun! Dryrun CFG not found in {cfgPath}, using default CFG!");
                Core.Engine.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 600;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Core.Engine.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 6;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 16000;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_win_panel_display_time 3;spec_freeze_deathanim_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 3;mp_team_timeout_ot_max 1;mp_team_timeout_ot_add_each 1;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
        }

        public void ExecUnpracCommands() {
            Core.Engine.ExecuteCommand("sv_cheats false;sv_grenade_trajectory_prac_pipreview false;sv_grenade_trajectory_prac_trailtime 0; mp_ct_default_grenades \"\"; mp_ct_default_primary \"\"; mp_t_default_grenades\"\"; mp_t_default_primary\"\"; mp_teammates_are_enemies false;");
            Core.Engine.ExecuteCommand("mp_death_drop_breachcharge true; mp_death_drop_defuser true; mp_death_drop_taser true; mp_drop_knife_enable false; mp_death_drop_grenade 2; ammo_grenade_limit_total 4; mp_defuser_allocation 0; sv_infinite_ammo 0; mp_force_pick_time 15");
        }

        public bool IsValidPositionForLastGrenade(IPlayer player, int position)
        {
            int playerId = player.PlayerID;
            if (!lastGrenadesData.ContainsKey(playerId) || lastGrenadesData[playerId].Count <= 0)
            {
                player.SendChat(Localizer["matchzy.pm.nothrownnades"]);
                return false;
            }

            if (lastGrenadesData[playerId].Count < position)
            {
                player.SendChat(Localizer["matchzy.pm.grenadehistory", $"{lastGrenadesData[playerId].Count}"]);
                return false;
            }

            return true;
        }

        public void RethrowSpecificNade(IPlayer player, string nadeType)
        {
            if (!isPractice || player == null || !player.IsValid) return;
            int playerId = player.PlayerID;
            if (!nadeSpecificLastGrenadeData.ContainsKey(playerId) || !nadeSpecificLastGrenadeData[playerId].ContainsKey(nadeType))
            {
                player.SendChat(Localizer["matchzy.pm.nothrownnadestype", nadeType]);
                return;
            }
            GrenadeThrownData grenadeThrown = nadeSpecificLastGrenadeData[playerId][nadeType];
            SchedulerService.DelayBySeconds(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
        }

        public void HandleBackCommand(IPlayer player, string number)
        {
            if (!isPractice || player == null || !player.IsValid) return;
            int playerId = player.PlayerID;
            if (!string.IsNullOrWhiteSpace(number))
            {
                if (int.TryParse(number, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        lastGrenadesData[playerId][positionNumber].LoadPosition(player);
                        player.SendChat(Localizer["matchzy.pm.tptogrenade", $"{positionNumber + 1}/{lastGrenadesData[playerId].Count}"]);
                    }
                }
                else
                {
                    player.SendChat(Localizer["matchzy.pm.backinvalidvalue"]);
                    return;
                }
            }
            else
            {
                int thrownCount = lastGrenadesData.ContainsKey(playerId) ? lastGrenadesData[playerId].Count : 0;
                player.SendChat(Localizer["matchzy.pm.backtonumber", thrownCount]);
            }
        }

        public void HandleThrowIndexCommand(IPlayer player, string argString)
        {
            if (!isPractice || player == null || !player.IsValid) return;
            int playerId = player.PlayerID;

            if (string.IsNullOrEmpty(argString))
            {
                int thrownCount = lastGrenadesData.ContainsKey(playerId) ? lastGrenadesData[playerId].Count : 0;
                player.SendChat(Localizer["matchzy.pm.throwindextonumber", thrownCount]);
                return;
            }

            string[] argsList = argString.Split();

            foreach (string arg in argsList)
            {
                if (int.TryParse(arg, out int positionNumber) && positionNumber >= 1)
                {
                    if (IsValidPositionForLastGrenade(player, positionNumber))
                    {
                        positionNumber -= 1;
                        GrenadeThrownData grenadeThrown = lastGrenadesData[playerId][positionNumber];
                        SchedulerService.DelayBySeconds(grenadeThrown.Delay, () => grenadeThrown.Throw(player));
                        player.SendChat(Localizer["matchzy.pm.throwgrenadehistory", $"{positionNumber + 1}/{lastGrenadesData[playerId].Count}"]);
                    }
                }
                else
                {
                    player.SendChat(Localizer["matchzy.pm.backnegativenumber", arg]);
                }
            }
        }

        public void HandleDelayCommand(IPlayer player, string delay)
        {
            if (!isPractice || player == null || !player.IsValid) return;

            int playerId = player.PlayerID;
            if (string.IsNullOrWhiteSpace(delay))
            {
                player.SendChat(Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
                return;
            }
            
            if (float.TryParse(delay, out float delayInSeconds) && delayInSeconds > 0)
            {
                if (IsValidPositionForLastGrenade(player, 0))
                {
                    lastGrenadesData[playerId].Last().Delay = delayInSeconds;
                    player.SendChat(Localizer["matchzy.pm.delaygrenade", $"{delayInSeconds:0.00}", $"{lastGrenadesData[playerId].Count}"]);
                }
            }
            else
            {
                player.SendChat(Localizer["matchzy.pm.delayvalidnumber"]);
                return;
            }
        }

        public void DisplayPracticeTimerCenter(int playerId)
        {
            // TODO: Update to use Core.PlayerManager
            var player = Core.PlayerManager.GetPlayer(playerId);
            if (player == null || !player.IsValid || !playerTimers.ContainsKey(playerId)) return;
            playerTimers[playerId].DisplayTimerCenter(player);
        }

        [Command("throw", registerRaw: true)]
        [CommandAlias("rethrow", registerRaw: true)]
        public void OnRethrowCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            int playerId = context.Sender.PlayerID;
            if (!lastGrenadesData.ContainsKey(playerId) || lastGrenadesData[playerId].Count <= 0)
            {
                context.Reply(Localizer["matchzy.pm.notthrownnade"]);
                return;
            }
            GrenadeThrownData lastGrenade = lastGrenadesData[playerId].Last();
            SchedulerService.DelayBySeconds(lastGrenade.Delay, () => lastGrenade.Throw(context.Sender));
        }

        /// <summary>
        /// Saves the current player position in practice mode.
        /// </summary>
        [Command("savepos", registerRaw: true)]
        public void OnSavePosCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            
            int playerId = context.Sender.PlayerID;
            var pawn = context.Sender.RequiredPlayerPawn;
            var origin = pawn.CBodyComponent?.SceneNode?.AbsOrigin ?? new Vector(0, 0, 0);
            Vector position = new(origin.X, origin.Y, origin.Z);
            QAngle angle = pawn.EyeAngles;
            
            savedPlayerLocationData[playerId] = new PlayerLocationData(position, angle);
            Logger.LogInformation($"[SavePos] Saved position for PlayerID {playerId}, Position: {position}, Angle: {angle}!");
            context.Reply(Localizer["matchzy.pm.savepos"]);
        }

        /// <summary>
        /// Loads the saved player position in practice mode.
        /// </summary>
        [Command("loadpos", registerRaw: true)]
        public void OnLoadPosCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            
            int playerId = context.Sender.PlayerID;
            if (!savedPlayerLocationData.TryGetValue(playerId, out var playerLocationData))
            {
                context.Reply(Localizer["matchzy.pm.notsavedpos"]);
                return;
            }
            
            Logger.LogInformation($"[LoadPos] LoadPos position for PlayerID {playerId}, Position: {playerLocationData.Position}, Angles: {playerLocationData.Angle}!");
            playerLocationData.LoadPosition(context.Sender);
            context.Reply(Localizer["matchzy.pm.loadpos"]);
        }

        /// <summary>
        /// Rethrows the last thrown smoke grenade in practice mode.
        /// </summary>
        [Command("throwsmoke", registerRaw: true)]
        [CommandAlias("rethrowsmoke", registerRaw: true)]
        public void OnRethrowSmokeCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            RethrowSpecificNade(context.Sender, "smoke");
        }

        /// <summary>
        /// Rethrows the last thrown flashbang in practice mode.
        /// </summary>
        [Command("throwflash", registerRaw: true)]
        [CommandAlias("rethrowflash", registerRaw: true)]
        public void OnRethrowFlashCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            RethrowSpecificNade(context.Sender, "flash");
        }

        /// <summary>
        /// Rethrows the last thrown HE grenade in practice mode.
        /// </summary>
        [Command("throwgrenade", registerRaw: true)]
        [CommandAlias("rethrowgrenade", registerRaw: true)]
        [CommandAlias("thrownade", registerRaw: true)]
        [CommandAlias("rethrownade", registerRaw: true)]
        public void OnRethrowGrenadeCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            RethrowSpecificNade(context.Sender, "hegrenade");
        }

        /// <summary>
        /// Rethrows the last thrown molotov in practice mode.
        /// </summary>
        [Command("throwmolotov", registerRaw: true)]
        [CommandAlias("rethrowmolotov", registerRaw: true)]
        public void OnRethrowMolotovCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            RethrowSpecificNade(context.Sender, "molotov");
        }

        /// <summary>
        /// Rethrows the last thrown decoy in practice mode.
        /// </summary>
        [Command("throwdecoy", registerRaw: true)]
        [CommandAlias("rethrowdecoy", registerRaw: true)]
        public void OnRethrowDecoyCommand(ICommandContext context)
        {
            if (context.Sender == null || !context.Sender.IsValid) return;
            RethrowSpecificNade(context.Sender, "decoy");
        }

        /// <summary>
        /// Teleports to the last thrown grenade position in practice mode.
        /// </summary>
        [Command("last", registerRaw: true)]
        public void OnLastCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            int playerId = context.Sender.PlayerID;
            if (!lastGrenadesData.ContainsKey(playerId) || lastGrenadesData[playerId].Count <= 0)
            {
                context.Reply(Localizer["matchzy.pm.notthrownnade"]);
                return;
            }
            lastGrenadesData[playerId].Last().LoadPosition(context.Sender);
        }

        /// <summary>
        /// Teleports back to a previous grenade position in practice mode.
        /// Usage: !back &lt;number&gt;
        /// </summary>
        [Command("back", registerRaw: true)]
        public void OnBackCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            if (context.Args.Length >= 1) 
            {
                string commandArg = context.Args[0];
                HandleBackCommand(context.Sender, commandArg);
            }
            else 
            {
                int playerId = context.Sender.PlayerID;
                int thrownCount = lastGrenadesData.ContainsKey(playerId) ? lastGrenadesData[playerId].Count : 0;
                context.Reply(Localizer["matchzy.pm.backtonumber", thrownCount]);
            }      
        }

        /// <summary>
        /// Rethrows a grenade from history by index in practice mode.
        /// Usage: !throwidx &lt;number&gt;
        /// </summary>
        [Command("throwidx", registerRaw: true)]
        [CommandAlias("throwindex", registerRaw: true)]
        public void OnThrowIndexCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            if (context.Args.Length >= 1) 
            {
                HandleThrowIndexCommand(context.Sender, string.Join(" ", context.Args));
            }
            else 
            {
                int playerId = context.Sender.PlayerID;
                int thrownCount = lastGrenadesData.ContainsKey(playerId) ? lastGrenadesData[playerId].Count : 0;
                context.Reply(Localizer["matchzy.pm.throwindextonumber", thrownCount]);
            }      
        }

        /// <summary>
        /// Displays the index of the last thrown grenade in practice mode.
        /// </summary>
        [Command("lastindex", registerRaw: true)]
        public void OnLastIndexCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            if (IsValidPositionForLastGrenade(context.Sender, 1))
            {
                int playerId = context.Sender.PlayerID;
                context.Reply(Localizer["matchzy.pm.indexlastgrenade", $"{lastGrenadesData[playerId].Count}"]);
            } 
        }

        /// <summary>
        /// Sets delay for the last thrown grenade in practice mode.
        /// Usage: !delay &lt;seconds&gt;
        /// </summary>
        [Command("delay", registerRaw: true)]
        public void OnDelayCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            if (context.Args.Length >= 1) 
            {
                HandleDelayCommand(context.Sender, context.Args[0]);
            }
            else 
            {
                context.Reply(Localizer["matchzy.cc.usage", $"!delay <delay_in_seconds>"]);
            }      
        }

        /// <summary>
        /// Starts/stops a practice timer in practice mode.
        /// </summary>
        [Command("timer", registerRaw: true)]
        public void OnTimerCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            int playerId = context.Sender.PlayerID;
            if (playerTimers.ContainsKey(playerId))
            {
                playerTimers[playerId].KillTimer();
                double timerResult = playerTimers[playerId].GetTimerResult();
                if (context.Sender != null)
                {
                    context.Sender.SendCenter($"Timer: {timerResult}s");
                }
                context.Reply($"Timer stopped! Result: {timerResult}s");
                playerTimers.Remove(playerId);
            }
            else
            {
                var timer = new PlayerPracticeTimer(PracticeTimerType.Immediate)
                {
                    StartTime = DateTime.Now
                };
                timer.Timer = SchedulerService.RepeatBySeconds(0.1f, () => {
                    if (timer.Timer?.Token.IsCancellationRequested == true) return;
                    DisplayPracticeTimerCenter(playerId);
                });
                playerTimers[playerId] = timer;
                context.Reply("Timer started! Use !timer to stop it.");
            }
        }

        [Command("sn", registerRaw: true)]
        [CommandAlias("savenade", registerRaw: true)]
        public void OnSaveNadeCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            HandleSaveNadeCommand(context.Sender, string.Join(" ", context.Args));
        }

        [Command("ln", registerRaw: true)]
        [CommandAlias("loadnade", registerRaw: true)]
        public void OnLoadNadeCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            HandleLoadNadeCommand(context.Sender, string.Join(" ", context.Args));
        }

        [Command("lin", registerRaw: true)]
        [CommandAlias("listnades", registerRaw: true)]
        public void OnListNadesCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            HandleListNadesCommand(context.Sender, string.Join(" ", context.Args));
        }

        [Command("importnade", registerRaw: true)]
        [CommandAlias("in", registerRaw: true)]
        public void OnImportNadeCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            HandleImportNadeCommand(context.Sender, string.Join(" ", context.Args));
        }

        [Command("deletenade", registerRaw: true)]
        [CommandAlias("delnade", registerRaw: true)]
        [CommandAlias("dn", registerRaw: true)]
        public void OnDeleteNadeCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            HandleDeleteNadeCommand(context.Sender, string.Join(" ", context.Args));
        }

        [Command("solid", registerRaw: true)]
        public void OnSolidCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            var solidConVar = Core.ConVar.Find<int>("mp_solid_teammates");
            if (solidConVar == null) return;
            int solidValue = solidConVar.Value;

            int newSolidValue = (solidValue == 0 || solidValue == 1) ? 2 : 1;

            solidConVar.Value = newSolidValue;

            Core.PlayerManager.SendChat($"mp_solid_teammates is now set to {newSolidValue}");
        }

        [Command("impacts", registerRaw: true)]
        public void OnImpactsCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            var impactConVar = Core.ConVar.Find<int>("sv_showimpacts");
            if (impactConVar == null) return;
            int impactValue = impactConVar.Value;

            int newImpactValue = 1 - impactValue;

            Core.Engine.ExecuteCommand($"sv_showimpacts {newImpactValue}");

            Core.PlayerManager.SendChat($"sv_showimpacts is now set to {newImpactValue}");
        }

        [Command("traj", registerRaw: true)]
        [CommandAlias("pip", registerRaw: true)]
        public void OnTrajCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;

            var trajConVar = Core.ConVar.Find<bool>("sv_grenade_trajectory_prac_pipreview");
            if (trajConVar == null) return;
            bool trajValue = trajConVar.Value;

            Core.Engine.ExecuteCommand($"sv_grenade_trajectory_prac_pipreview {!trajValue}");

            Core.PlayerManager.SendChat($"sv_grenade_trajectory_prac_pipreview is now set to {!trajValue}");
        }

        [Command("bestspawn", registerRaw: true)]
        public void OnBestSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToBestSpawn(context.Sender, (byte)context.Sender.RequiredController.TeamNum);
        }

        [Command("worstspawn", registerRaw: true)]
        public void OnWorstSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToWorstSpawn(context.Sender, (byte)context.Sender.RequiredController.TeamNum);
        }

        [Command("bestctspawn", registerRaw: true)]
        public void OnBestCTSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToBestSpawn(context.Sender, (byte)TeamEnum.CT);
        }

        [Command("worstctspawn", registerRaw: true)]
        public void OnWorstCTSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToWorstSpawn(context.Sender, (byte)TeamEnum.CT);
        }

        [Command("besttspawn", registerRaw: true)]
        public void OnBestTSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToBestSpawn(context.Sender, (byte)TeamEnum.T);
        }

        [Command("worsttspawn", registerRaw: true)]
        public void OnWorstTSpawnCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            TeleportPlayerToWorstSpawn(context.Sender, (byte)TeamEnum.T);
        }

        [Command("showspawns", registerRaw: true)]
        public void OnShowSpawnsCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            RemoveSpawnBeams();
            if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
            foreach (Position spawn in spawnsData[(byte)TeamEnum.CT])
            {
                ShowSpawnBeam(spawn, new SwiftlyS2.Shared.Natives.Color(0, 0, 255, 255));
            }
            foreach (Position spawn in spawnsData[(byte)TeamEnum.T])
            {
                ShowSpawnBeam(spawn, new SwiftlyS2.Shared.Natives.Color(255, 165, 0, 255));
            }
        }

        [Command("hidespawns", registerRaw: true)]
        public void OnHideSpawnsCommand(ICommandContext context)
        {
            if (!isPractice || context.Sender == null || !context.Sender.IsValid) return;
            RemoveSpawnBeams();
        }

        public void TeleportPlayerToBestSpawn(IPlayer player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns)) return;
            Vector playerPosition = player.RequiredPlayerPawn.CBodyComponent!.SceneNode!.AbsOrigin;
            int closestIndex = -1;
            double minDistance = double.MaxValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = index;
                }
            }
            player.RequiredPlayerPawn.Teleport(teamSpawns[closestIndex].PlayerPosition, teamSpawns[closestIndex].PlayerAngle, new Vector(0, 0, 0));
        }

        public void TeleportPlayerToWorstSpawn(IPlayer player, byte teamNum)
        {
            if (!spawnsData.TryGetValue(teamNum, out List<Position>? teamSpawns)) return;
            Vector playerPosition = player.RequiredPlayerPawn.CBodyComponent!.SceneNode!.AbsOrigin;
            int farthestIndex = -1;
            double maxDistance = double.MinValue;
            for (int index = 0; index < teamSpawns.Count; index++)
            {
                Vector spawnPosition = teamSpawns[index].PlayerPosition;
                Vector diff = playerPosition - spawnPosition;
                float distance = diff.Length();
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthestIndex = index;
                }
            }
            player.RequiredPlayerPawn.Teleport(teamSpawns[farthestIndex].PlayerPosition, teamSpawns[farthestIndex].PlayerAngle, new Vector(0, 0, 0));
        }

        // Note: timer2 implementation is deferred until OnPlayerRunCmd is available in SwiftlyS2
        // Using OnTick would be an alternative, but it would be very expensive and not worth it
        // [ConsoleCommand("css_timer2", "Starts a timer, use .timer2 again to stop it.")]
        // public void OnTimer2Command(CCSPlayerController? player, CommandInfo command)
        // {
        //     if (!isPractice || !IsPlayerValid(player)) return;
        //     int userId = player!.UserId!.Value;
        //     if (playerTimers.ContainsKey(userId))
        //     {
        //         PrintToPlayerChat(player, $"Timer stopped! Result: {playerTimers[userId].GetTimerResult()}s");
        //         playerTimers[userId].KillTimer();
        //         playerTimers.Remove(userId);
        //     }
        //     else
        //     {
        //         playerTimers[userId] = new PlayerPracticeTimer(PracticeTimerType.OnMovement);
        //         PrintToPlayerChat(player, $"When you start moving a timer will run until you stop moving.");
        //     }
        // }
    }
}
