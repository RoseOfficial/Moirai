using System.Numerics;

namespace Moirai.Core.Intents;

public enum CombatMode { Auto, Defensive }

public abstract record Intent;

public sealed record GoTo(Vector3 Destination, bool Fly, float Tolerance) : Intent;
public sealed record MountUp : Intent;
public sealed record Dismount : Intent;
public sealed record Engage(ulong TargetId) : Intent;
public sealed record ClearTarget : Intent;
public sealed record SyncLevel : Intent;
public sealed record InteractWith(ulong ObjectId) : Intent;
public sealed record TeleportTo(uint AetheryteId) : Intent;
public sealed record ChangeZone(ushort TerritoryId) : Intent;
public sealed record SummonMinion(uint MinionId) : Intent;
public sealed record EquipWatch : Intent;
public sealed record AcceptReturn : Intent;
public sealed record SetCombat(bool Enabled, CombatMode Mode) : Intent;
public sealed record StopRun(StopReason Reason) : Intent;
public sealed record Hold(int Milliseconds) : Intent;
public sealed record NoAction : Intent;
