using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Misc;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using Microsoft.Extensions.Logging;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<TeamEnum, bool> teamReadyOverride = new() {
        {TeamEnum.T, false},
        {TeamEnum.CT, false},
        {TeamEnum.Spectator, false}
    };

    public bool allowForceReady = true;

    public bool IsTeamsReady()
    {
        return IsTeamReady((int)TeamEnum.CT) && IsTeamReady((int)TeamEnum.T);
    }

    public bool IsSpectatorsReady()
    {
        return IsTeamReady((int)TeamEnum.Spectator);
    }

    public bool IsTeamReady(int team)
    {
        // if (matchStarted) return true;

        int minPlayers = GetPlayersPerTeam(team);
        int minReady = GetTeamMinReady(team);
        (int playerCount, int readyCount) = GetTeamPlayerCount(team, false);

        Logger.LogInformation($"[IsTeamReady] team: {team} minPlayers:{minPlayers} minReady:{minReady} playerCount:{playerCount} readyCount:{readyCount}");

        if (team == (int)TeamEnum.Spectator && minReady == 0)
        {
            return true;
        }

        if (readyAvailable && playerCount == 0)
        {
            // We cannot ready for veto with no players, regardless of force status or min_players_to_ready.
            return false;
        }

        if (playerCount == readyCount && playerCount >= minPlayers)
        {
            return true;
        }

        // Check if team is forced ready (if implemented)
        // For now, just check ready count
        if (readyCount >= minReady)
        {
            return true;
        }

        return false;
    }

    public int GetPlayersPerTeam(int team)
    {
        if (team == (int)TeamEnum.CT || team == (int)TeamEnum.T) return matchConfig.PlayersPerTeam;
        if (team == (int)TeamEnum.Spectator) return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    public int GetTeamMinReady(int team)
    {
        if (team == (int)TeamEnum.CT || team == (int)TeamEnum.T) return matchConfig.MinPlayersToReady;
        if (team == (int)TeamEnum.Spectator) return matchConfig.MinSpectatorsToReady;
        return 0;
    }

    public (int, int) GetTeamPlayerCount(int team, bool includeCoaches = false)
    {
        int playerCount = 0;
        int readyCount = 0;
        
        // Using Core.PlayerManager to get players in SwiftlyS2
        var allPlayers = Core.PlayerManager.GetAllPlayers();
        foreach (var player in allPlayers)
        {
            if (!player.IsValid) continue;
            if ((int)player.Controller.TeamNum == team) {
                playerCount++;
                if (playerReadyStatus.ContainsKey(player.PlayerID) && playerReadyStatus[player.PlayerID] == true) 
                    readyCount++;
            }
        }
        return (playerCount, readyCount);
    }

    public bool IsTeamForcedReady(TeamEnum team) {
        return teamReadyOverride[team];
    }

    [Command("forceready", registerRaw: true)]
    public void OnForceReadyCommandCommand(ICommandContext context)
    {
        if (context.Sender == null || !context.Sender.IsValid) return;
        
        var player = context.Sender;
        var teamNum = (int)player.Controller.TeamNum;
        
        Logger.LogInformation($"{readyAvailable} {isMatchSetup} {allowForceReady} {player.IsValid}");
        if (!readyAvailable || !isMatchSetup || !allowForceReady || !player.IsValid) return;

        int minReady = GetTeamMinReady(teamNum);
        (int playerCount, int readyCount) = GetTeamPlayerCount(teamNum, false);

        if (playerCount < minReady) 
        {
            context.Reply(Localizer["matchzy.rs.minreadyplayers", minReady]);
            return;
        }

        var allPlayers = Core.PlayerManager.GetAllPlayers();
        foreach (var p in allPlayers)
        {
            if (!p.IsValid) continue;
            if ((int)p.Controller.TeamNum == teamNum) {
                playerReadyStatus[p.PlayerID] = true;
                ReplyToUserCommand(p, Localizer["matchzy.rs.forcereadiedby", player.Controller.PlayerName]);
            }
        }

        teamReadyOverride[(TeamEnum)teamNum] = true;
        // Re‑evaluate whether the match should go live after forcing players ready.
        CheckLiveRequired();
    }
}
