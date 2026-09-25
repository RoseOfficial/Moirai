using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public abstract record ModuleDirective;
public sealed record FarmHere : ModuleDirective;
public sealed record MoveToTerritory(ushort TerritoryId) : ModuleDirective;
public sealed record StopSession(StopReason Reason, string Summary) : ModuleDirective;
public sealed record EnsureMinion(uint MinionId) : ModuleDirective; // E3: summon this yokai's minion
public sealed record EnsureWatch : ModuleDirective;                 // E1: wear the Yo-kai Watch
public sealed record BuyMinion(uint MinionId, uint MinionItemId, int MedalCost) : ModuleDirective; // E9
public sealed record ReadRanks : ModuleDirective;                     // S4: read the Shared FATE ranks
public sealed record GoShopping(GemstoneTrip Trip) : ModuleDirective; // S6: a trip to a gemstone trader
public sealed record EnsureLearned(uint ItemId) : ModuleDirective;    // S14: learn a collectible in the bags

// What the Director tells the module each tick besides the snapshot: how long selection has
// come up empty in this zone (G6), which is the module's cue to move on, whether this is a
// settled moment (not mid-leg, mounted, in combat, or casting) for a summon or an equip, the
// FATEs the session has completed (S4 puts each down to its zone), and whether no fate is under
// way (S6/S15: a trip or a read is taken only then)
public sealed record ModuleContext(long IdleSeconds, bool Settled = false, int Completed = 0, bool BetweenFates = false);

public interface IFarmModule
{
    ModuleDirective Next(WorldSnapshot w, ModuleContext ctx);
}
