using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;


namespace MatchZy
{

    public partial class MatchZy
    {
        public void StartSleepMode()
        {
            if (matchStarted) return;
            isSleep = true;
            isPractice = false;
            isDryRun = false;
            isWarmup = false;
            readyAvailable = false;
            matchStarted = false;
            isSideSelectionPhase = false;
            isMatchLive = false;

            string cfgExecPath = EnsureCfgInGameCfgDirectory("sleep.cfg");
            string cfgPath = GetConfigFilePath("sleep.cfg");

            if (File.Exists(cfgPath))
            {
                Logger.LogInformation($"Starting Sleep Mode! Executing Sleep CFG via exec {cfgExecPath} (source: {cfgPath})");
                Core.Engine.ExecuteCommand($"exec {cfgExecPath}");
            }
            else
            {
                Logger.LogInformation($"Starting Sleep Mode! Sleep CFG not found in {cfgPath}, using default CFG!");
                ExecUnpracCommands();
                Core.Engine.ExecuteCommand("""exec gamemode_competitive.cfg;""");
            }
            Logger.LogInformation($"[StartSleepMode] MatchZy deactivated!");
        }

        [Command("sleep", registerRaw: true)]
        public void OnSleepCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (!IsPlayerAdmin(player, "css_sleep", "@css/map", "@custom/prac"))
            {
                SendPlayerNotAdminMessage(player);
                return;
            }

            if (matchStarted)
            {
                // ReplyToUserCommand(player, "Sleep Mode cannot be started when a match has been started!");
                ReplyToUserCommand(player, Localizer["matchzy.sleep.sleepwhenmatchstared"]);
                return;
            }
            StartSleepMode();
        }

    }
}
