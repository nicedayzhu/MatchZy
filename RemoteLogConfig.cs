using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Microsoft.Extensions.Logging;

namespace MatchZy
{
    public partial class MatchZy
    {
        [Command("get5_remote_log_url", registerRaw: true)]
        [CommandAlias("matchzy_remote_log_url", registerRaw: true)]
        public void RemoteLogURLCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player != null) return;
            if (context.Args.Length < 1) return;
            string url = context.Args[0];

            if (!IsValidUrl(url))
            {
                Logger.LogError($"[RemoteLogURLCommand] Invalid URL: {url}. Please provide a valid URL!");
                return;
            }

            matchConfig.RemoteLogURL = url;
        }

        [Command("get5_remote_log_header_key", registerRaw: true)]
        [CommandAlias("matchzy_remote_log_header_key", registerRaw: true)]
        public void RemoteLogHeaderKeyCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player != null) return;
            if (context.Args.Length < 1) return;
            string header = context.Args[0].Trim();

            if (header != "") matchConfig.RemoteLogHeaderKey = header;
        }

        [Command("get5_remote_log_header_value", registerRaw: true)]
        [CommandAlias("matchzy_remote_log_header_value", registerRaw: true)]
        public void RemoteLogHeaderValueCommand(ICommandContext context)
        {
            IPlayer? player = context.Sender;
            if (player != null) return;
            if (context.Args.Length < 1) return;
            string headerValue = context.Args[0].Trim();

            if (headerValue != "") matchConfig.RemoteLogHeaderValue = headerValue;
        }
    }
}
