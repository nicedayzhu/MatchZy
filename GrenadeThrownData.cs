using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using TeamEnum = SwiftlyS2.Shared.Players.Team;
using SwiftlyS2.Shared.Natives;

namespace MatchZy;
public class GrenadeThrownData
{
    public Vector Position { get; private set; }

    public QAngle Angle { get; private set; }

    public Vector Velocity { get; private set; }

    public Vector PlayerPosition { get; private set; }

    public QAngle PlayerAngle { get; private set; }

    public string Type { get; private set; }

    public DateTime ThrownTime { get; private set; }

    public float Delay { get; set; }

    public UInt16 ItemIndex { get; set; }

    public GrenadeThrownData(Vector nadePosition, QAngle nadeAngle, Vector nadeVelocity, Vector playerPosition, QAngle playerAngle, string grenadeType, DateTime thrownTime, UInt16 itemIndex)
    {
        Position = new Vector(nadePosition.X, nadePosition.Y, nadePosition.Z);
        Angle = new QAngle(nadeAngle.Pitch, nadeAngle.Yaw, nadeAngle.Roll);
        Velocity = new Vector(nadeVelocity.X, nadeVelocity.Y, nadeVelocity.Z);
        PlayerPosition = new Vector(playerPosition.X, playerPosition.Y, playerPosition.Z);
        PlayerAngle = new QAngle(playerAngle.Pitch, playerAngle.Yaw, playerAngle.Roll);
        Type = grenadeType;
        ThrownTime = thrownTime;
        Delay = 0;
        ItemIndex = itemIndex;
    }

    public void LoadPosition(IPlayer player)
    {
        if (player == null || !player.IsValid) return;
        player.RequiredPlayerPawn.Teleport(PlayerPosition, PlayerAngle, new Vector(0, 0, 0));
    }

    public void Throw(IPlayer player)
    {
		// TODO: Implement grenade throwing using SwiftlyS2 EntitySystem
		// This functionality needs to be reimplemented using SwiftlyS2's entity creation APIs
		Console.WriteLine($"[MatchZy] Throw grenade not yet implemented for type: {Type}");
		/*
		var playerPawn = player.RequiredPlayerPawn;
		var team = player.RequiredController.TeamNum == (byte)TeamEnum.CT ? TeamEnum.CT : TeamEnum.T;
		
		switch (Type)
		{
			case "smoke":
			{
				// TODO: Implement using SwiftlyS2 EntitySystem
				break;
			}
			case "molotov":
			{
				// TODO: Implement using SwiftlyS2 EntitySystem
				break;
			}
			case "hegrenade":
			{
				// TODO: Implement using SwiftlyS2 EntitySystem
				break;
			}
			case "decoy":
			{
				// TODO: Implement using SwiftlyS2 EntitySystem
				break;
			}
			case "flash":
			{
				// TODO: Implement using SwiftlyS2 EntitySystem
				break;
			}
			default:
				Console.WriteLine($"[MatchZy] Unknown Grenade: {Type}");
				break;
		}
		*/
    }
}
