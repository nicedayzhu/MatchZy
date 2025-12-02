using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;
using ChatColors = SwiftlyS2.Shared.Helper.ChatColors;
using System.Reflection;


namespace MatchZy
{
    public partial class MatchZy
    {

        // Configuration fields - these are regular fields, not ConVars
        // They are set via console commands (e.g., matchzy_* commands)
        // In SwiftlyS2, we use regular fields and handle them via Command handlers
        public bool smokeColorEnabled = false;
        public bool techPauseEnabled = true;
        public string techPausePermission = "";
        public int techPauseDuration = 300;
        public int maxTechPausesAllowed = 2;
        public bool everyoneIsAdmin = false;
        public bool showCreditsOnMatchStart = true;
        public string hostnameFormat = "MatchZy | {TEAM1} vs {TEAM2}";
        public bool enableDamageReport = true;
        public bool stopCommandNoDamage = false;
        public string matchStartMessage = "";

        [Command("matchzy_whitelist_enabled_default", registerRaw: true)]
        public void MatchZyWLConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isWhitelistRequired = bool.TryParse(args, out bool isWhitelistRequiredValue) ? isWhitelistRequiredValue : args != "0" && isWhitelistRequired;
        }
        
        [Command("matchzy_knife_enabled_default", registerRaw: true)]
        public void MatchZyKnifeConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isKnifeRequired = bool.TryParse(args, out bool isKnifeRequiredValue) ? isKnifeRequiredValue : args != "0" && isKnifeRequired;
        }

        [Command("matchzy_playout_enabled_default", registerRaw: true)]
        public void MatchZyPlayoutConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isPlayOutEnabled = bool.TryParse(args, out bool isPlayOutEnabledValue) ? isPlayOutEnabledValue : args != "0" && isPlayOutEnabled;
        }

        [Command("matchzy_save_nades_as_global_enabled", registerRaw: true)]
        public void MatchZySaveNadesAsGlobalConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isSaveNadesAsGlobalEnabled = bool.TryParse(args, out bool isSaveNadesAsGlobalEnabledValue) ? isSaveNadesAsGlobalEnabledValue : args != "0" && isSaveNadesAsGlobalEnabled;
        }

        [Command("matchzy_kick_when_no_match_loaded", registerRaw: true)]
        public void MatchZyMatchModeOnlyConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            matchModeOnly = bool.TryParse(args, out bool matchModeOnlyValue) ? matchModeOnlyValue : args != "0" && matchModeOnly;
        }

        [Command("matchzy_reset_cvars_on_series_end", registerRaw: true)]
        public void MatchZyResetCvarsOnSeriesEndConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            resetCvarsOnSeriesEnd = bool.TryParse(args, out bool resetCvarsOnSeriesEndValue) ? resetCvarsOnSeriesEndValue : args != "0" && resetCvarsOnSeriesEnd;
        }

        [Command("matchzy_minimum_ready_required", registerRaw: true)]
        public void MatchZyMinimumReadyRequired(ICommandContext context)
        {
            if (context.Sender != null) return;
            // Since there is already a console command for this purpose, we will use the same.   
            OnReadyRequiredCommand(context);
        }

        [Command("matchzy_demo_path", registerRaw: true)]
        public void MatchZyDemoPath(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length >= 1)
            {
                string path = context.Args[0];
                if (path[0] == '/' || path[0] == '.' || path[^1] != '/' || path.Contains("//"))
                {
                    Logger.LogInformation($"matchzy_demo_path must end with a slash and must not start with a slash or dot. It will be reset to an empty string! Current value: {demoPath}");
                }
                else
                {
                    demoPath = path;
                }
            }
        }

        [Command("matchzy_demo_name_format", registerRaw: true)]
        public void MatchZyDemoNameFormat(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length >= 1)
            {
                string format = context.Args[0].Trim();

                if (!string.IsNullOrEmpty(format)) 
                {
                    demoNameFormat = format;
                }
            }
        }

        [Command("matchzy_demo_recording_enabled", registerRaw: true)]
        public void MatchZyDemoRecordingEnabled(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isDemoRecordingEnabled = bool.TryParse(args, out bool isDemoRecordingEnabledValue) ? isDemoRecordingEnabledValue : args != "0" && isDemoRecordingEnabled;
        }

        [Command("get5_demo_upload_url", registerRaw: true)]
        [CommandAlias("matchzy_demo_upload_url", registerRaw: true)]
        public void MatchZyDemoUploadURL(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string url = context.Args[0];
            if (url.Trim() == "") return;
            if (!IsValidUrl(url))
            {
                Logger.LogInformation($"[MatchZyDemoUploadURL] Invalid URL: {url}. Please provide a valid URL for uploading the demo!");
                return;
            }
            demoUploadURL = url;
        }

        [Command("matchzy_stop_command_available", registerRaw: true)]
        public void MatchZyStopCommandEnabled(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isStopCommandAvailable = bool.TryParse(args, out bool isStopCommandAvailableValue) ? isStopCommandAvailableValue : args != "0" && isStopCommandAvailable;
        }

        [Command("matchzy_use_pause_command_for_tactical_pause", registerRaw: true)]
        public void MatchZyPauseForTacticalCommand(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            isPauseCommandForTactical = bool.TryParse(args, out bool isPauseCommandForTacticalValue) ? isPauseCommandForTacticalValue : args != "0" && isPauseCommandForTactical;
        }

        [Command("matchzy_pause_after_restore", registerRaw: true)]
        public void MatchZyPauseAfterStopEnabled(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            pauseAfterRoundRestore = bool.TryParse(args, out bool pauseAfterRoundRestoreValue) ? pauseAfterRoundRestoreValue : args != "0" && pauseAfterRoundRestore;
        }

        [Command("matchzy_chat_prefix", registerRaw: true)]
        public void MatchZyChatPrefix(ICommandContext context)
        {
            if (context.Sender != null) return;

            string args = string.Join(" ", context.Args).Trim();

            if (string.IsNullOrEmpty(args))
            {
                var metadata = typeof(MatchZy).GetCustomAttribute<PluginMetadata>();
                chatPrefix = $"[{ChatColors.Green}{metadata?.Name ?? "MatchZy"}{ChatColors.Default}]";
                return;
            }

            args = GetColorTreatedString(args);

            chatPrefix = args;

            Logger.LogInformation($"[MatchZyChatPrefix] chatPrefix: {chatPrefix}");
        }

        [Command("matchzy_admin_chat_prefix", registerRaw: true)]
        public void MatchZyAdminChatPrefix(ICommandContext context)
        {
            if (context.Sender != null) return;

            string args = string.Join(" ", context.Args).Trim();

            if (string.IsNullOrEmpty(args))
            {
                adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";
                return;
            }

            args = GetColorTreatedString(args);

            adminChatPrefix = args;

            Logger.LogInformation($"[MatchZyAdminChatPrefix] adminChatPrefix: {adminChatPrefix}");
        }

        [Command("matchzy_chat_messages_timer_delay", registerRaw: true)]
        public void MatchZyChatMessagesTimerDelay(ICommandContext context)
        {
            if (context.Sender != null) return;

            if (context.Args.Length >= 1)
            {
                string commandArg = context.Args[0];
                if (!string.IsNullOrWhiteSpace(commandArg))
                {
                    if (int.TryParse(commandArg, out int chatTimerDelayValue) && chatTimerDelayValue >= 0)
                    {
                        chatTimerDelay = chatTimerDelayValue;
                    }
                    else
                    {
                        ReplyToUserCommand(context.Sender, Localizer["matchzy.cvars.invalidvalue"]);
                    }
                }
            } else if (context.Args.Length == 0) {
                Logger.LogInformation($"matchzy_chat_messages_timer_delay = {chatTimerDelay}");
            }
        }

        [Command("matchzy_autostart_mode", registerRaw: true)]
        public void MatchZyAutoStartConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            if (int.TryParse(args, out int autoStartModeValue))
            {
                autoStartMode = autoStartModeValue;
            }

        }

        [Command("matchzy_allow_force_ready", registerRaw: true)]
        [CommandAlias("get5_allow_force_ready", registerRaw: true)]
        public void MatchZyAllowForceReadyConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            allowForceReady = bool.TryParse(args, out bool allowForceReadyValue) ? allowForceReadyValue : args != "0" && allowForceReady;
        }

        [Command("matchzy_max_saved_last_grenades", registerRaw: true)]
        public void MatchZyMaxSavedLastGrenadesConvar(ICommandContext context)
        {
            if (context.Sender != null) return;
            string args = string.Join(" ", context.Args);

            if (int.TryParse(args, out int maxLastGrenadesSavedLimitValue))
            {
                maxLastGrenadesSavedLimit = maxLastGrenadesSavedLimitValue;
            }
            else
            {
                context.Reply(Localizer["matchzy.cc.usage", $"matchzy_max_saved_last_grenades <number>"]);
            }
        }

        [Command("get5_remote_backup_url", registerRaw: true)]
        [CommandAlias("matchzy_remote_backup_url", registerRaw: true)]
        public void MatchZyBackupUploadURL(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string url = context.Args[0];
            if (url.Trim() == "") return;
            if (!IsValidUrl(url))
            {
                Logger.LogInformation($"[MatchZyBackupUploadURL] Invalid URL: {url}. Please provide a valid URL for uploading the backup!");
                return;
            }
            backupUploadURL = url;
        }

        [Command("get5_remote_backup_header_key", registerRaw: true)]
        [CommandAlias("matchzy_remote_backup_header_key", registerRaw: true)]
        public void BackupUploadHeaderKeyCommand(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string header = context.Args[0].Trim();

            if (header != "") backupUploadHeaderKey = header;
        }

        [Command("get5_remote_backup_header_value", registerRaw: true)]
        [CommandAlias("matchzy_remote_backup_header_value", registerRaw: true)]
        public void BackupUploadHeaderValueCommand(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string headerValue = context.Args[0].Trim();

            if (headerValue != "") backupUploadHeaderValue = headerValue;
        }

    }
}
