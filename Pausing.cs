using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using TeamEnum = SwiftlyS2.Shared.Players.Team;

namespace MatchZy;

public partial class MatchZy
{
    public Dictionary<Team, int> technicalPauseUsed = new();
    public int lastTechPauseDuration = 0;

    // TODO: Update to use ICommandContext
    public void TechPause(IPlayer? player, ICommandContext? command)
    {
        // Tech Pause is WIP
        return;

        if (!isMatchLive) return;

        // Treating .tech command as .forcepause if it is used via server console.
        if (player == null)
        {
            ForcePauseMatch(player, command);
            return;
        }

        if (isPaused)
        {
            // ReplyToUserCommand(player, "Match is already paused!");
            ReplyToUserCommand(player, Localizer["matchzy.pause.ispaused"]);
            return;
        }
        if (IsHalfTimePhase())
        {
            // ReplyToUserCommand(player, "You cannot use this command during halftime.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.duringhalftime"]); ;
            return;
        }
        if (IsPostGamePhase())
        {
            // ReplyToUserCommand(player, "You cannot use this command after the game has ended.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.matchended"]);
            return;
        }
        if (IsTacticalTimeoutActive())
        {
            // ReplyToUserCommand(player, "You cannot use this command when tactical timeout is active.");
            ReplyToUserCommand(player, Localizer["matchzy.pause.tacticaltimeout"]);
            return;
        }

        if (player.RequiredController.TeamNum == (byte)TeamEnum.Spectator) return;

        if (!techPauseEnabled && player != null)
        {
            PrintToPlayerChat(player, Localizer["matchzy.ready.techpausenotenabled"]);
            return;
        }

        if (maxTechPausesAllowed <= 0) return;

        Team playerTeam = (player!.RequiredController.TeamNum == (byte)TeamEnum.CT) ? reverseTeamSides["CT"] : reverseTeamSides["TERRORIST"];
        if (technicalPauseUsed[playerTeam] >= maxTechPausesAllowed)
        {
            PrintToPlayerChat(player, Localizer["matchzy.pause.notechpauseleft", playerTeam.teamName]);
            return;
        }
    }
}