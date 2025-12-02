
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using Team = SwiftlyS2.Shared.Players.Team;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using Microsoft.Extensions.Logging;

namespace MatchZy;
public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event)
    {
        try
        {
            IPlayer? player = @event.UserIdPlayer;

            if (player == null || !player.IsValid) return HookResult.Continue;
            Logger.LogInformation($"[FULL CONNECT] Player ID: {player.PlayerID}, Name: {player.RequiredController.PlayerName} has connected!");

            // Handling whitelisted players
            if (!player.IsFakeClient && !player.RequiredController.IsHLTV)
            {
                var steamId = player.SteamID;

                bool kicked = HandlePlayerWhitelist(player, steamId.ToString());
                if (kicked) return HookResult.Continue;

                if (isMatchSetup || matchModeOnly)
                {
                    TeamEnum team = GetPlayerTeam(player);
                    if (team == TeamEnum.Spectator) // Using Spectator as default check
                    {
                        Logger.LogInformation($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.RequiredController.PlayerName} (NOT ALLOWED!)");
                        PrintToAllChat($"Kicking player {player.RequiredController.PlayerName} - Not a player in this game.");
                        KickPlayer(player);
                        return HookResult.Continue;
                    }
                }
            }

            int playerId = player.PlayerID;
            playerData[playerId] = player;
            connectedPlayers++;
            if (readyAvailable && !matchStarted)
            {
                playerReadyStatus[playerId] = false;
            }
            else
            {
                playerReadyStatus[playerId] = true;
            }
            // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
            // Update: Commenting the below function as it was being called multiple times on map change.
            // UpdatePlayersMap();

            if (readyAvailable && !matchStarted)
            {
                // Start Warmup when first player connect and match is not started.
                if (GetRealPlayersCount() == 1)
                {
                    Logger.LogInformation($"[FULL CONNECT] First player has connected, starting warmup!");
                    ExecUnpracCommands();
                    AutoStart();
                }
            }
            return HookResult.Continue;

        }
        catch (Exception e)
        {
            Logger.LogError($"[EventPlayerConnectFull FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }

    }
    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event)
    {
        try
        {
            IPlayer? player = @event.UserIdPlayer;

            if (player == null || !player.IsValid) return HookResult.Continue;
            int userId = player.PlayerID;

            if (playerReadyStatus.ContainsKey(userId))
            {
                playerReadyStatus.Remove(userId);
                connectedPlayers--;
            }
            playerData.Remove(userId);

            // Update coach checking logic for SwiftlyS2
            if (matchzyTeam1.coach.Contains(player))
            {
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
                // Note: Clan property access may need to be updated based on SwiftlyS2 API
                // player.RequiredController.Clan = "";
            }
            else if (matchzyTeam2.coach.Contains(player))
            {
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
                // Note: Clan property access may need to be updated based on SwiftlyS2 API
                // player.RequiredController.Clan = "";
            }
            noFlashList.Remove(userId);
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Logger.LogError($"[EventPlayerDisconnect FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelRoundHandler(EventCsWinPanelRound @event)
    {
        // EventCsWinPanelRound has stopped firing after Arms Race update, hence we handle knife round winner in EventRoundEnd.

        // Log($"[EventCsWinPanelRound PRE] finalEvent: {@event.FinalEvent}");
        // if (isKnifeRound && matchStarted)
        // {
        //     HandleKnifeWinner(@event);
        // }
        return HookResult.Continue;
    }

    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event)
    {
        try
        {
            HandleMatchEnd();
            // ResetMatch();
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Logger.LogError($"[EventCsWinPanelMatch FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundStartHandler(EventRoundStart @event)
    {
        try
        {
            HandlePostRoundStartEvent(@event);
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Logger.LogError($"[EventRoundStart FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundFreezeEndHandler(EventRoundFreezeEnd @event)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;
            // Update coach handling for SwiftlyS2
            HashSet<IPlayer> coaches = GetAllCoaches();
            foreach (var coach in coaches)
            {
                if (coach == null || !coach.IsValid) continue;
                // If coaches are still left alive after freezetime ends, this code will force them to spectate their team again.
                if (coach.PlayerPawn == null || coach.PlayerPawn.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                // Force coach to spectate their team
                coach.ChangeTeam(TeamEnum.Spectator);
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Logger.LogError($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event) {
        try {
            if (!matchStarted) return HookResult.Continue;
            var recv = @event.UserIdPlayer;
            if (recv == null || !recv.IsValid) return HookResult.Continue;

            // Update coach checking logic for SwiftlyS2
            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv)) {
                TransferCoachBomb(recv);
            }
        } catch (Exception e) {
            Logger.LogError($"[EventPlayerGivenC4 FATAL] An error occured: {e.Message}");
        }
        return HookResult.Continue;
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || entity == null || entity.Entity == null) return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName)) return;

            SchedulerService.DelayBySeconds(0.0f, () => {
                // Cast entity to projectile type using As<> method
                if (entity == null || entity.Entity == null) return;
                // Use As<> method to convert to specific type
                var projectile = entity.As<CBaseCSGrenadeProjectile>();
                if (projectile == null || !projectile.IsValid) return;

                if (!projectile.Thrower.IsValid ||
                    projectile.Thrower.Value == null ||
                    !projectile.Thrower.Value.Controller.IsValid ||
                    projectile.Thrower.Value.Controller.Value == null)
                {
                    return;
                }

                var playerController = projectile.Thrower.Value.Controller.Value;
                var player = Core.PlayerManager.GetPlayer((int)playerController.Index);
                if(player == null || !player.IsValid) return;
                int client = player.PlayerID;
                
                var origin = projectile.CBodyComponent?.SceneNode?.AbsOrigin;
                if (origin == null) return;
                Vector position = new(origin.Value.X, origin.Value.Y, origin.Value.Z);
                var rotation = projectile.CBodyComponent?.SceneNode?.AbsRotation;
                if (rotation == null) return;
                QAngle angle = new(rotation.Value.Pitch, rotation.Value.Yaw, rotation.Value.Roll);
                var velocity = projectile.AbsVelocity;
                Vector velocityVec = new(velocity.X, velocity.Y, velocity.Z);
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                if (!lastGrenadesData.ContainsKey(client)) {
                    lastGrenadesData[client] = new();
                }

                if (!nadeSpecificLastGrenadeData.ContainsKey(client))
                {
                    nadeSpecificLastGrenadeData[client] = new(){};
                }

                var playerPawn = player.RequiredPlayerPawn;
                GrenadeThrownData lastGrenadeThrown = new(
                    position, 
                    angle, 
                    velocityVec, 
                    playerPawn.CBodyComponent!.SceneNode!.AbsOrigin, 
                    playerPawn.EyeAngles,
                    nadeType,
                    DateTime.Now,
                    projectile.ItemIndex
                );

                nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                lastGrenadesData[client].Add(lastGrenadeThrown);

                if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit)
                {
                    lastGrenadesData[client].RemoveAt(0);
                }

                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
                if (smokeColorEnabled && nadeType == "smoke")
                {
                    var smokeProjectile = entity.As<CSmokeGrenadeProjectile>();
                    if (smokeProjectile != null)
                    {
                        var color = GetPlayerTeammateColor(player);
                        smokeProjectile.SmokeColor.X = color.R;
                        smokeProjectile.SmokeColor.Y = color.G;
                        smokeProjectile.SmokeColor.Z = color.B;
                    }
                }
            });
        }
        catch (Exception e)
        {
            Logger.LogError($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}");
        }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event)
    {
        try
        {
            // We do not broadcast the suicide of the coach
            if (!matchStarted) return HookResult.Continue;

            var attacker = Core.PlayerManager.GetPlayer(@event.Attacker);
            var victim = @event.UserIdPlayer;
            if (attacker != null && victim != null && attacker.PlayerID == victim.PlayerID)
            {
                // Update coach checking logic for SwiftlyS2
                if (matchzyTeam1.coach.Contains(attacker) || matchzyTeam2.coach.Contains(attacker))
                {
                    // Note: DontBroadcast is handled by SwiftlyS2 event system
                    // If needed, we can use event cancellation or filtering
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Logger.LogError($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        IPlayer? player = @event.UserIdPlayer;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.EntityID, out var thrownTime)) 
        {
            PrintToPlayerChat(player, Localizer["matchzy.pracc.smoke", player.RequiredController.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.EntityID);
        }
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        IPlayer? player = @event.UserIdPlayer;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.EntityID, out var thrownTime)) 
        {
            PrintToPlayerChat(player, Localizer["matchzy.pracc.flash", player.RequiredController.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.EntityID);
        }
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        IPlayer? player = @event.UserIdPlayer;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.EntityID, out var thrownTime)) 
        {
            PrintToPlayerChat(player, Localizer["matchzy.pracc.grenade", player.RequiredController.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.EntityID);
        }
        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        IPlayer? player = @event.UserIdPlayer;
        if (player == null || !player.IsValid) return HookResult.Continue;
        // EventMolotovDetonate doesn't have EntityID, using UserId as key
        int eventKey = @event.UserId;
        if(lastGrenadeThrownTime.TryGetValue(eventKey, out var thrownTime)) 
        {
            PrintToPlayerChat(player, Localizer["matchzy.pracc.molotov", player.RequiredController.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
        }
        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        IPlayer? player = @event.UserIdPlayer;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.EntityID, out var thrownTime)) 
        {
            PrintToPlayerChat(player, Localizer["matchzy.pracc.decoy", player.RequiredController.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.EntityID);
        }
        return HookResult.Continue;
    }
}
