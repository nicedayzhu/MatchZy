using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;


namespace MatchZy
{
    public partial class MatchZy
    {
        public async Task SendEventAsync(MatchZyEvent @event)
        {
            try
            {
                if (string.IsNullOrEmpty(matchConfig.RemoteLogURL)) return;

                Logger.LogInformation($"[SendEventAsync] Sending Event: {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} on {matchConfig.RemoteLogURL}");

                using var httpClient = new HttpClient();
                using var jsonContent = new StringContent(JsonSerializer.Serialize(@event, @event.GetType()), Encoding.UTF8, "application/json");

                string jsonString = await jsonContent.ReadAsStringAsync();

                Logger.LogInformation($"[SendEventAsync] SENDING DATA: {jsonString}");

                if (!string.IsNullOrEmpty(matchConfig.RemoteLogHeaderKey) && !string.IsNullOrEmpty(matchConfig.RemoteLogHeaderValue))
                {
                    httpClient.DefaultRequestHeaders.Add(matchConfig.RemoteLogHeaderKey, matchConfig.RemoteLogHeaderValue);
                }

                var httpResponseMessage = await httpClient.PostAsync(matchConfig.RemoteLogURL, jsonContent);

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    Logger.LogInformation($"[SendEventAsync] Sending {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} successful with status code: {httpResponseMessage.StatusCode}");
                }
                else
                {
                    Logger.LogError($"[SendEventAsync] Sending {@event.EventName} for matchId: {liveMatchId} mapNumber: {matchConfig.CurrentMapNumber} failed with status code: {httpResponseMessage.StatusCode}, ResponseContent: {await httpResponseMessage.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"[SendEventAsync FATAL] An error occurred: {e.Message}");
            }
        }
    }
}
