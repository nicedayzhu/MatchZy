using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MatchZy
{
    public partial class MatchZy
    {
        public string demoPath = "MatchZy/";
        public string demoNameFormat = "{TIME}_{MATCH_ID}_{MAP}_{TEAM1}_vs_{TEAM2}";
        public string demoUploadURL = "";
        public string demoUploadHeaderKey = "";
        public string demoUploadHeaderValue = "";

        public string activeDemoFile = "";

        public bool isDemoRecording = false;
        public bool isDemoRecordingEnabled = true;

        public void StartDemoRecording()
        {
            if (!isDemoRecordingEnabled)
            {
                Logger.LogInformation("[StartDemoRecording] Demo recording is disabled.");
                return;
            }
            if (isDemoRecording)
            {
                Logger.LogInformation("[StartDemoRecording] Demo recording is already in progress.");
                return;
            }
            string demoFileName = FormatCvarValue(demoNameFormat.Replace(" ", "_")) + ".dem";
            try
            {
                string? directoryPath = Path.GetDirectoryName(Path.Join(Core.CSGODirectory + "/", demoPath));
                if (directoryPath != null)
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }
                string tempDemoPath = demoPath == "" ? demoFileName : demoPath + demoFileName;
                activeDemoFile = tempDemoPath;
                Logger.LogInformation($"[StartDemoRecoding] Starting demo recording, path: {tempDemoPath}");
                Core.Engine.ExecuteCommand($"tv_record {tempDemoPath}");
                isDemoRecording = true;
            }
            catch (Exception ex)
            {
                Logger.LogInformation($"[StartDemoRecording - FATAL] Error: {ex.Message}. Starting demo recording with path. Name: {demoFileName}");
                // This is to avoid demo loss in any case of exception
                Core.Engine.ExecuteCommand($"tv_record {demoFileName}");
                isDemoRecording = true;
            }

        }

        public void StopDemoRecording(float delay, string activeDemoFile, long liveMatchId, int currentMapNumber)
        {
            Logger.LogInformation($"[StopDemoRecording] Going to stop demorecording in {delay}s");
            string demoPath = Path.Join(Core.CSGODirectory + "/", activeDemoFile);
            (int t1score, int t2score) = GetTeamsScore();
            int roundNumber = t1score + t2score;
            SchedulerService.DelayBySeconds(delay, () =>
            {
                if (isDemoRecording)
                {
                    Core.Engine.ExecuteCommand($"tv_stoprecord");
                }
                isDemoRecording = false;
                SchedulerService.DelayBySeconds(15, () =>
                {
                    Task.Run(async () =>
                    {
                        await UploadFileAsync(demoPath, demoUploadURL, demoUploadHeaderKey, demoUploadHeaderValue, liveMatchId, currentMapNumber, roundNumber);
                    });
                });
            });
        }

        public int GetTvDelay()
        {
            var tvEnable = Core.ConVar.Find<bool>("tv_enable");
            if (tvEnable == null || !tvEnable.Value) return 0;

            var tvEnable1 = Core.ConVar.Find<bool>("tv_enable1");
            var tvDelay = Core.ConVar.Find<int>("tv_delay");
            if (tvDelay == null) return 0;
            int tvDelayValue = tvDelay.Value;

            if (tvEnable1 == null || !tvEnable1.Value) return tvDelayValue;
            var tvDelay1 = Core.ConVar.Find<int>("tv_delay1");
            if (tvDelay1 == null) return tvDelayValue;
            int tvDelay1Value = tvDelay1.Value;

            if (tvDelayValue < tvDelay1Value) return tvDelay1Value;
            return tvDelayValue;
        }

        [Command("get5_demo_upload_header_key", registerRaw: true)]
        [CommandAlias("matchzy_demo_upload_header_key", registerRaw: true)]
        public void DemoUploadHeaderKeyCommand(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string header = context.Args[0].Trim();

            if (header != "") demoUploadHeaderKey = header;
        }

        [Command("get5_demo_upload_header_value", registerRaw: true)]
        [CommandAlias("matchzy_demo_upload_header_value", registerRaw: true)]
        public void DemoUploadHeaderValueCommand(ICommandContext context)
        {
            if (context.Sender != null) return;
            if (context.Args.Length < 1) return;
            string headerValue = context.Args[0].Trim();

            if (headerValue != "") demoUploadHeaderValue = headerValue;
        }
    }
}
