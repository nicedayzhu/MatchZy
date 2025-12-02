using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System.Text.Json;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using Microsoft.Extensions.Logging;

namespace MatchZy;

public partial class MatchZy
{

    // coachKillTimer is defined in MatchZy.cs and uses SwiftlyS2's scheduler timers.

    public HashSet<IPlayer> GetAllCoaches()
    {
        HashSet<IPlayer> coaches = new(matchzyTeam1.coach);
        coaches.UnionWith(matchzyTeam2.coach);

        return coaches;
    }

    public void HandleCoachCommand(IPlayer? player, string side)
    {
        if (player == null || !player.IsValid) return;
        if (isPractice)
        {
            player.SendMessage(MessageType.Chat, "Coach command can only be used in match mode!");
            return;
        }
        if (IsWingmanMode())
        {
            player.SendMessage(MessageType.Chat, "Coach command cannot be used in wingman!");
            return;
        }

        side = side.Trim().ToLower();

        if (side != "t" && side != "ct")
        {
            player.SendMessage(MessageType.Chat, "Usage: .coach t or .coach ct");
            return;
        }

        if (matchzyTeam1.coach.Contains(player) || matchzyTeam2.coach.Contains(player))
        {
            player.SendMessage(MessageType.Chat, "You are already coaching a team!");
            return;
        }

        Team matchZyCoachTeam;

        if (side == "t")
        {
            matchZyCoachTeam = reverseTeamSides["TERRORIST"];
        }
        else if (side == "ct")
        {
            matchZyCoachTeam = reverseTeamSides["CT"];
        }
        else
        {
            return;
        }

        // if (matchZyCoachTeam.coach != null) {
        //     ReplyToUserCommand(player, "Coach slot for this team has been already taken!");
        //     return;
        // }

        matchZyCoachTeam.coach.Add(player!);
        player!.RequiredController.Clan = $"[{matchZyCoachTeam.teamName} COACH]";
        if (player.RequiredController.InGameMoneyServices != null) player.RequiredController.InGameMoneyServices.Account = 0;
        ReplyToUserCommand(player, $"You are now coaching {matchZyCoachTeam.teamName}! Use .uncoach to stop coaching");
        PrintToAllChat($"{ChatColors.Green}{player.RequiredController.PlayerName}{ChatColors.Default} is now coaching {ChatColors.Green}{matchZyCoachTeam.teamName}{ChatColors.Default}!");
    }

    public void HandleCoaches()
    {
        coachKillTimer?.Cancel();
        coachKillTimer = null;
        HashSet<IPlayer> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        if (spawnsData.Values.Any(list => list.Count == 0)) GetSpawns();
        if (coachSpawns.Count == 0 || 
            coachSpawns[(byte)TeamEnum.CT].Count == 0 || 
            coachSpawns[(byte)TeamEnum.T].Count == 0)
        {
            Logger.LogInformation($"[HandleCoaches] No coach spawns found, player positions will not be swapped!");
            return;
        }

        var freezeTimeConVar = Core.ConVar.Find<int>("mp_freezetime");
        int freezeTime = freezeTimeConVar != null ? freezeTimeConVar.Value : 2;
        freezeTime = freezeTime > 2 ? freezeTime: 2;
        if (coachKillTimer == null)
        {
            coachKillTimer = SchedulerService.DelayBySeconds(freezeTime - 1f, () => KillCoaches());
        }

        Random random = new();
        foreach (IPlayer coach in coaches)
        {
            if (!coach.IsValid) continue;
            Team coachTeam = matchzyTeam1.coach.Contains(coach) ? matchzyTeam1 : matchzyTeam2;
            int coachTeamNum = teamSides[coachTeam] == "CT" ? 3 : 2;
            if (coach.RequiredController.InGameMoneyServices != null)
            {
                coach.RequiredController.InGameMoneyServices.Account = 0;
            }

            SchedulerService.DelayBySeconds(0.5f, () => HandleCoachTeam(coach));

            // SwiftlyS2 does not currently expose the same ActionTrackingServices API here,
            // so per‑coach match stats are left unchanged.

            SetPlayerInvisible(player: coach, setWeaponsInvisible: false);
            // Stopping the coaches from moving, so that they don't block the players.
            var coachPawn = coach.RequiredPlayerPawn;
            coachPawn.MoveType = MoveType_t.MOVETYPE_NONE;
            coachPawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;

            List<Position> coachTeamSpawns = coachSpawns[coach.RequiredController.TeamNum];
            Position coachPosition = new(coachPawn.CBodyComponent!.SceneNode!.AbsOrigin, coachPawn.CBodyComponent!.SceneNode!.AbsRotation);

            // Picking a random position for the coach (from coachSpawns) to teleport them.
            Position newPosition = coachTeamSpawns[random.Next(0, coachTeamSpawns.Count)];

            // Elevating coach before dropping the C4 to prevent it going inside the ground.
            SchedulerService.DelayBySeconds(0.05f, () =>
            {
                HandleCoachWeapons(coach);
                coach.RequiredPlayerPawn.Teleport(newPosition.PlayerPosition, newPosition.PlayerAngle, new Vector(0, 0, 0));
            });

        }

        var players = Core.PlayerManager.GetAllPlayers();
        HashSet<Position> occupiedSpawns = new();
        HashSet<IPlayer> incorrectSpawnedPlayers = new();

        // We will loop on the players 2 times, first loop is to get all the players who are on a non-competitive spawn, and to get all the non-occupied competitive spawn.
        // In the next loop, we will teleport the non-competitive spawned players to an available competitive spawn.

        foreach (IPlayer player in players)
        {
            if (!player.IsValid || coaches.Contains(player)) continue;

            List<Position> teamPositions = spawnsData[player.RequiredController.TeamNum];
            var playerPawn = player.RequiredPlayerPawn;
            Position playerPosition = new(playerPawn.CBodyComponent!.SceneNode!.AbsOrigin, playerPawn.CBodyComponent!.SceneNode!.AbsRotation);
            bool isCompetitiveSpawn = false;
            foreach (Position position in teamPositions)
            {
                if (position.Equals(playerPosition))
                {
                    occupiedSpawns.Add(position);
                    isCompetitiveSpawn = true;
                    break;
                }
            }
            if (isCompetitiveSpawn) continue;

            // The player is not on a competitive spawn, we will put them on one in the next loop.
            incorrectSpawnedPlayers.Add(player);
        }

        foreach (IPlayer player in incorrectSpawnedPlayers)
        {
            if (!player.IsValid || coaches.Contains(player)) continue;

            List<Position> teamPositions = spawnsData[player.RequiredController.TeamNum];
            var playerPawn = player.RequiredPlayerPawn;
            Position playerPosition = new(playerPawn.CBodyComponent!.SceneNode!.AbsOrigin, playerPawn.CBodyComponent!.SceneNode!.AbsRotation);
            foreach (Position position in teamPositions)
            {
                if (occupiedSpawns.Contains(position)) continue;
                occupiedSpawns.Add(position);
                SchedulerService.DelayBySeconds(0.1f, () =>
                {
                    player.RequiredPlayerPawn.Teleport(position.PlayerPosition, position.PlayerAngle, new Vector(0, 0, 0));
                });
                break;
            }
        }
    }

    private void HandleCoachWeapons(IPlayer coach)
    {
        if (!IsPlayerValid(coach)) return;
        // SwiftlyS2 does not currently expose a safe, generic API for stripping all
        // weapons from a player entity, and low‑level hacks have been observed to be unstable.
        // For now we rely on setting coach money to 0 and teleporting them away from play.
    }

    /// <summary>
    /// Transfers bomb from coach to first available non-coach terrorist.
    /// </summary> 
    public void TransferCoachBomb(IPlayer coach) {
        if (coach.RequiredController.TeamNum != (int)TeamEnum.T) return; // can't have bomb

        // find bomb and new target
        var coachPawn = coach.RequiredPlayerPawn;
        var bomb = coachPawn.WeaponServices!.MyWeapons
            .Where(w => w.IsValid && w.Value != null && w.Value.Entity?.DesignerName == "weapon_c4")
            .FirstOrDefault();
        if (!bomb.IsValid || bomb.Value == null) return; // should never trigger

        var target = Core.PlayerManager.GetAllPlayers()
            .FirstOrDefault(
                p => IsPlayerValid(p)
                && !reverseTeamSides["TERRORIST"].coach.Contains(p)
                && p.RequiredController.TeamNum == (int)TeamEnum.T
                && p.RequiredController.PawnIsAlive
            );
        if (!IsPlayerValid(target)) return; // should never trigger

        // transfer bomb
        Logger.LogInformation($"[EventPlayerGivenC4 INFO] Transferred bomb from {coach.RequiredController.PlayerName} (Coach) to {target.RequiredController.PlayerName}.");
        if (bomb.IsValid && bomb.Value != null)
        {
            bomb.Value.AcceptInput("Kill", "");
        }
        // In SwiftlyS2 we give items via engine commands instead of GiveNamedItem.
        Core.Engine.ExecuteCommand($"give {target.PlayerID} weapon_c4");
    }

    public TeamEnum GetCoachTeam(IPlayer coach)
    {
        if (matchzyTeam1.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam1] == "CT")
            {
                return TeamEnum.CT;
            }
            else if (teamSides[matchzyTeam1] == "TERRORIST")
            {
                return TeamEnum.T;
            }
        }
        if (matchzyTeam2.coach.Contains(coach))
        {
            if (teamSides[matchzyTeam2] == "CT")
            {
                return TeamEnum.CT;
            }
            else if (teamSides[matchzyTeam2] == "TERRORIST")
            {
                return TeamEnum.T;
            }
        }
        return TeamEnum.Spectator;
    }

    private void HandleCoachTeam(IPlayer playerController)
    {
        TeamEnum oldTeam = GetCoachTeam(playerController);
        if (playerController.RequiredController.TeamNum != (byte)oldTeam)
        {
            playerController.ChangeTeam(TeamEnum.Spectator);
            SchedulerService.DelayBySeconds(0.01f, () => playerController.ChangeTeam(oldTeam));
        }
        if (playerController.RequiredController.InGameMoneyServices != null) playerController.RequiredController.InGameMoneyServices.Account = 0;
    }

    private void KillCoaches()
    {
        if (isPaused || IsTacticalTimeoutActive()) return;
        HashSet<IPlayer> coaches = GetAllCoaches();
        if (IsWingmanMode() || coaches.Count == 0) return;
        string suicidePenalty = GetConvarStringValue("mp_suicide_penalty");
        string specFreezeTime = GetConvarStringValue("spec_freeze_time");
        string specFreezeTimeLock = GetConvarStringValue("spec_freeze_time_lock");
        string specFreezeDeathanim = GetConvarStringValue("spec_freeze_deathanim_time");
        Core.Engine.ExecuteCommand("mp_suicide_penalty 0;spec_freeze_time 0; spec_freeze_time_lock 0; spec_freeze_deathanim_time 0;");

        foreach (var coach in coaches)
        {
            if (!IsPlayerValid(coach)) continue;
            if (isPaused || IsTacticalTimeoutActive()) continue;

            var coachPawn = coach.RequiredPlayerPawn;
            Position coachPosition = new(coachPawn.CBodyComponent!.SceneNode!.AbsOrigin, coachPawn.CBodyComponent!.SceneNode!.AbsRotation);
            coachPawn.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
            coachPawn.CommitSuicide(explode: false, force: true);
        }
        Core.Engine.ExecuteCommand($"mp_suicide_penalty {suicidePenalty}; spec_freeze_time {specFreezeTime}; spec_freeze_time_lock {specFreezeTimeLock}; spec_freeze_deathanim_time {specFreezeDeathanim};");
    }

    private void GetCoachSpawns()
    {
        coachSpawns = GetEmptySpawnsData();
        try
        {
            // Get spawns file from resources
            string? pluginDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Logger.LogError("[GetCoachSpawns] Failed to get plugin directory");
                return;
            }
            
            string spawnsConfigPath = Path.Combine(pluginDirectory, "resources", "spawns", "coach", $"{Core.Engine.GlobalVars.MapName}.json");
            if (!File.Exists(spawnsConfigPath))
            {
                Logger.LogWarning($"[GetCoachSpawns] Spawns config not found at {spawnsConfigPath}");
                return;
            }
            
            string spawnsConfig = File.ReadAllText(spawnsConfigPath);

            var jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(spawnsConfig);
            if (jsonDictionary is null) return;
            foreach (var entry in jsonDictionary)
            {
                byte team = byte.Parse(entry.Key);
                List<Position> positionList = new();

                foreach (var positionData in entry.Value)
                {
                    string[] vectorArray = positionData["Vector"].Split(' ');
                    string[] angleArray = positionData["QAngle"].Split(' ');

                    // Parse position and angle
                    Vector vector = new(float.Parse(vectorArray[0]), float.Parse(vectorArray[1]), float.Parse(vectorArray[2]));
                    QAngle qAngle = new(float.Parse(angleArray[0]), float.Parse(angleArray[1]), float.Parse(angleArray[2]));

                    Position position = new(vector, qAngle);

                    positionList.Add(position);
                }
                coachSpawns[team] =  positionList;
            }
            Logger.LogInformation($"[GetCoachSpawns] Loaded {coachSpawns.Count} coach spawns");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GetCoachSpawns - FATAL] Error getting coach spawns. [ERROR]: {ex.Message}");
        }
    }
}
