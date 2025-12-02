using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;

namespace MatchZy;

public class PlayerLocationData
{
    public Vector Position { get; set; }
    public QAngle Angle { get; set; }

    public PlayerLocationData(Vector position, QAngle angle)
    {
        this.Position = position;
        this.Angle = angle;
    }
    
    public void LoadPosition(IPlayer player)
    {
        if (player == null || !player.IsValid) return;
        player.RequiredPlayerPawn.Teleport(Position, Angle, new Vector(0, 0, 0));
    }
}