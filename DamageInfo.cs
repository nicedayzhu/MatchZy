using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.GameEventDefinitions;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using Microsoft.Extensions.Logging;


namespace MatchZy
{

    public partial class MatchZy
    {

        private void InitPlayerDamageInfo()
        {
            foreach (var key in playerData.Keys) {
                var attacker = playerData[key];
                if (attacker == null || !attacker.IsValid) continue;
                if (attacker.IsFakeClient) continue;
                int attackerId = key;
                foreach (var key2 in playerData.Keys) {
                    if (key == key2) continue;
                    var target = playerData[key2];
                    if (target == null || !target.IsValid || target.IsFakeClient) continue;
                    if ((int)attacker.RequiredController.TeamNum == (int)target.RequiredController.TeamNum) continue;
                    if ((int)attacker.RequiredController.TeamNum == 2) {
                        if ((int)target.RequiredController.TeamNum != 3) continue;
                        int targetId = key2;
                        if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                            playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

                        if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                            attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();
                    } else if ((int)attacker.RequiredController.TeamNum == 3) {
                        if ((int)target.RequiredController.TeamNum != 2) continue;
                        int targetId = key2;
                        if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
                            playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

                        if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
                            attackerInfo[targetId] = targetInfo = new DamagePlayerInfo(); 
                    }
                }
            }
        }

		public Dictionary<int, Dictionary<int, DamagePlayerInfo>> playerDamageInfo = new Dictionary<int, Dictionary<int, DamagePlayerInfo>>();
		private void UpdatePlayerDamageInfo(EventPlayerHurt @event, int targetId)
		{
            IPlayer? attacker = Core.PlayerManager.GetPlayer(@event.Attacker);

            if (attacker == null || !attacker.IsValid) return;
			int attackerId = attacker.PlayerID;
			if (!playerDamageInfo.TryGetValue(attackerId, out var attackerInfo))
				playerDamageInfo[attackerId] = attackerInfo = new Dictionary<int, DamagePlayerInfo>();

			if (!attackerInfo.TryGetValue(targetId, out var targetInfo))
				attackerInfo[targetId] = targetInfo = new DamagePlayerInfo();

			targetInfo.DamageHP += @event.DmgHealth;
			targetInfo.Hits++;
		}

        private void ShowDamageInfo()
        {
            if (!enableDamageReport) return;
            try
            {
                HashSet<(int, int)> processedPairs = new HashSet<(int, int)>();

                foreach (var entry in playerDamageInfo)
                {
                    int attackerId = entry.Key;
                    foreach (var (targetId, targetEntry) in entry.Value)
                    {
                        if (processedPairs.Contains((attackerId, targetId)) || processedPairs.Contains((targetId, attackerId)))
                            continue;

                        // Access and use the damage information as needed.
                        int damageGiven = targetEntry.DamageHP;
                        int hitsGiven = targetEntry.Hits;
                        int damageTaken = 0;
                        int hitsTaken = 0;

                        if (playerDamageInfo.TryGetValue(targetId, out var targetInfo) && targetInfo.TryGetValue(attackerId, out var takenInfo))
                        {
                            damageTaken = takenInfo.DamageHP;
                            hitsTaken = takenInfo.Hits;
                        }

                        if (!playerData.ContainsKey(attackerId) || !playerData.ContainsKey(targetId)) continue;

                        var attackerController = playerData[attackerId];
                        var targetController = playerData[targetId];

                        if (attackerController != null && targetController != null)
                        {
                            if (!attackerController.IsValid || !targetController.IsValid) continue;
                            var attackerPawn = attackerController.PlayerPawn;
                            var targetPawn = targetController.PlayerPawn;
                            if (attackerPawn == null || targetPawn == null) continue;

                            int attackerHP = attackerPawn.Health < 0 ? 0 : attackerPawn.Health;
                            string attackerName = attackerController.RequiredController.PlayerName;

                            int targetHP = targetPawn.Health < 0 ? 0 : targetPawn.Health;
                            string targetName = targetController.RequiredController.PlayerName;

                            PrintToPlayerChat(attackerController, $"{ChatColors.Green}To: [{damageGiven} / {hitsGiven} hits] From: [{damageTaken} / {hitsTaken} hits] - {targetName} - ({targetHP} hp){ChatColors.Default}");
                            PrintToPlayerChat(targetController, $"{ChatColors.Green}To: [{damageTaken} / {hitsTaken} hits] From: [{damageGiven} / {hitsGiven} hits] - {attackerName} - ({attackerHP} hp){ChatColors.Default}");
                        }

                        // Mark this pair as processed to avoid duplicates.
                        processedPairs.Add((attackerId, targetId));
                    }
                }
                playerDamageInfo.Clear();
            }
            catch (Exception e)
            {
                Logger.LogError($"[ShowDamageInfo FATAL] An error occurred: {e.Message}");
            }

        }
    }

	public class DamagePlayerInfo
	{
		public int DamageHP { get; set; } = 0;
		public int Hits { get; set; } = 0;
	}
}
