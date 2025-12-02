using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using Newtonsoft.Json.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MatchZy
{

    public class Team 
    {
        [JsonPropertyName("id")]
        public string id = "";

        [JsonPropertyName("teamname")]
        public required string teamName;

        [JsonPropertyName("teamflag")]
        public string teamFlag = "";

        [JsonPropertyName("teamtag")]
        public string teamTag = "";

        [JsonPropertyName("teamplayers")]
        public JToken? teamPlayers;

        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public HashSet<IPlayer> coach = [];

        [JsonPropertyName("seriesscore")]
        public int seriesScore = 0;
    }

    public partial class MatchZy
    {
        [Command("coach", registerRaw: true)]
        public void OnCoachCommand(ICommandContext context) 
        {
            IPlayer? player = context.Sender;
            HandleCoachCommand(player, string.Join(" ", context.Args));
        }

        [Command("uncoach", registerRaw: true)]
        public void OnUnCoachCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player == null || !player.IsValid) return;
            if (isPractice) {
                ReplyToUserCommand(player, "Uncoach command can only be used in match mode!");
                return;
            }

            if (matchzyTeam1.coach.Contains(player)) {
                player.RequiredController.Clan = "";
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else if (matchzyTeam2.coach.Contains(player)) {
                player.RequiredController.Clan = "";
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else {
                ReplyToUserCommand(player, "You are not coaching any team!");
                return;
            }

            if (player.RequiredController.InGameMoneyServices != null) player.RequiredController.InGameMoneyServices.Account = 0;

            ReplyToUserCommand(player, "You are now not coaching any team!");
        }

        [Command("matchzy_addplayer", registerRaw: true)]
        [CommandAlias("get5_addplayer", registerRaw: true)]
        public void OnAddPlayerCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player != null) return;
            if (!isMatchSetup) {
                context.Reply("No match is setup!");
                return;
            }
            if (IsHalfTimePhase())
            {
                context.Reply("Cannot add players during halftime. Please wait until the next round starts.");
                return;
            }
            if (context.Args.Length < 3)
            {
                context.Reply("Usage: matchzy_addplayer <steam64> <team> \"<name>\"");
                return; 
            }

            string playerSteamId = context.Args[0];
            string playerTeam = context.Args[1];
            string playerName = context.Args[2];
            bool success;
            if (playerTeam == "team1")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam1.teamPlayers);
            } else if (playerTeam == "team2")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchzyTeam2.teamPlayers);
            } else if (playerTeam == "spec")
            {
                success = AddPlayerToTeam(playerSteamId, playerName, matchConfig.Spectators);
            } else 
            {
                context.Reply("Unknown team: must be one of team1, team2, spec");
                return; 
            }
            if (!success)
            {
                context.Reply($"Failed to add player {playerName} to {playerTeam}. They may already be on a team or you provided an invalid Steam ID.");
                return;
            }
            context.Reply($"Player {playerName} added to {playerTeam} successfully!");
        }

        [Command("matchzy_removeplayer", registerRaw: true)]
        [CommandAlias("get5_removeplayer", registerRaw: true)]
        public void OnRemovePlayerCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player != null) return;
            if (!isMatchSetup) {
                context.Reply("No match is setup!");
                return;
            }
            if (IsHalfTimePhase())
            {
                context.Reply("Cannot remove players during halftime. Please wait until the next round starts.");
                return;
            }

            if (context.Args.Length < 1) {
                context.Reply("Usage: matchzy_removeplayer <steam64>");
                return;
            }

            string arg = context.Args[0];

            if (!ulong.TryParse(arg, out ulong steamId))
            {
                context.Reply($"Invalid Steam64");
            }

            bool success = RemovePlayerFromTeam(steamId.ToString());
            if (success)
            {
                context.Reply($"Successfully removed player {steamId}");
                // Find player by SteamID using SwiftlyS2 API
                IPlayer? removedPlayer = Core.PlayerManager.GetAllPlayers()
                    .FirstOrDefault(p => p.SteamID == steamId);
                if (IsPlayerValid(removedPlayer))
                {
                    Logger?.LogInformation($"Kicking player {removedPlayer!.RequiredController.PlayerName} - Not a player in this game (removed).");
                    PrintToAllChat($"Kicking player {removedPlayer!.RequiredController.PlayerName} - Not a player in this game.");
                    KickPlayer(removedPlayer);
                }
            }
            else
            {
                context.Reply($"Player {steamId} not found in any team or the Steam ID was invalid.");
            }
        }

        public bool AddPlayerToTeam(string steamId, string name, JToken? team)
        {
            if (matchzyTeam1.teamPlayers != null && matchzyTeam1.teamPlayers[steamId] != null) return false;
            if (matchzyTeam2.teamPlayers != null && matchzyTeam2.teamPlayers[steamId] != null) return false;
            if (matchConfig.Spectators != null && matchConfig.Spectators[steamId] != null) return false;

            if (team is JObject jObjectTeam)
            {
                jObjectTeam.Add(steamId, name);
                LoadClientNames();
                return true;
            }
            else if (team is JArray jArrayTeam)
            {
                jArrayTeam.Add(name);
                LoadClientNames();
                return true;
            }
            return false;
        }

        public bool RemovePlayerFromTeam(string steamId)
        {
            List<JToken?> teams = [matchzyTeam1.teamPlayers, matchzyTeam2.teamPlayers, matchConfig.Spectators];

            foreach (var team in teams)
            {
                if (team is null) continue;
                if (team is JObject jObjectTeam)
                {
                    jObjectTeam.Remove(steamId);
                    return true;
                }
                else if (team is JArray jArrayTeam)
                {
                    jArrayTeam.Remove(steamId);
                    return true;
                }
            }
            return false;
        }
    }
}
