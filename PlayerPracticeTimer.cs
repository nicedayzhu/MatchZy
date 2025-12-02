using SwiftlyS2.Shared.Players;
using System.Threading;

namespace MatchZy;

public enum PracticeTimerType
{
    OnMovement,
    Immediate
}

public class PlayerPracticeTimer
{
    public DateTime StartTime { get; set; }

    public PracticeTimerType TimerType { get; set; }

    public CancellationTokenSource? Timer { get; set; }

    public PlayerPracticeTimer(PracticeTimerType timerType)
    {
        TimerType = timerType;
    }

    public void DisplayTimerCenter(IPlayer player)
    {
        player.SendCenter($"Timer: {GetTimerResult()}s");
    }

    public double GetTimerResult()
    {
        double totalSeconds = (DateTime.Now - StartTime).TotalSeconds;
        return Math.Round(totalSeconds, 2);
    }

    public void KillTimer()
    {
        Timer?.Cancel();
        Timer = null;
    }
}
