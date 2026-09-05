using System.Numerics;

namespace Moirai.Core.Intents;

public enum CombatMode { Auto, Defensive }

// H2/H3: who moves the character; navigation by default, the dodge layer while danger is up
public enum MovementOwner { Navigation, Dodge }

public abstract record Intent;

public sealed record GoTo(Vector3 Destination, bool Fly, float Tolerance) : Intent;
public sealed record StopMoving : Intent; // C13: drop the running path so the next GoTo is issued afresh
public sealed record Jump : Intent;       // C8: part of the ground escape
public sealed record MountUp : Intent;
public sealed record Dismount : Intent;
public sealed record Engage(ulong TargetId) : Intent;
public sealed record ClearTarget : Intent;
public sealed record SyncLevel : Intent;
public sealed record InteractWith(ulong ObjectId) : Intent;
public sealed record ConfirmDialog : Intent; // B4: yes to the open yes/no prompt
public sealed record TeleportTo(uint AetheryteId) : Intent;
public sealed record ChangeZone(ushort TerritoryId) : Intent;
public sealed record SummonCompanion(uint GreensItemId) : Intent;
public sealed record SummonMinion(uint MinionId) : Intent; // E3
public sealed record EquipWatch : Intent;                  // E1
public sealed record AcquireMinion(uint MinionId, uint MinionItemId, int MedalCost) : Intent; // E9: the shell's purchaser
public sealed record SetCompanionStance(uint StanceActionId) : Intent;
public sealed record AcceptReturn : Intent;
public sealed record SetCombat(bool Enabled, CombatMode Mode) : Intent;
public sealed record HandMovementTo(MovementOwner Owner) : Intent;
public sealed record StopRun(StopReason Reason) : Intent;
public sealed record Hold(int Milliseconds) : Intent;
public sealed record NoAction : Intent;
