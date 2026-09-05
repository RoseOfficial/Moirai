using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public abstract record ModuleDirective;
public sealed record FarmHere : ModuleDirective;
public sealed record MoveToTerritory(ushort TerritoryId) : ModuleDirective;
public sealed record StopSession(StopReason Reason, string Summary) : ModuleDirective;
public sealed record EnsureMinion(uint MinionId) : ModuleDirective; // E3: summon this yokai's minion
public sealed record EnsureWatch : ModuleDirective;                 // E1: wear the Yo-kai Watch

// What the Director tells the module each tick besides the snapshot: how long selection has
// come up empty in this zone (G6), which is the module's cue to move on, and whether this is a
// settled moment (not mid-leg, mounted, in combat, or casting) for a summon or an equip
public sealed record ModuleContext(long IdleSeconds, bool Settled = false);

public interface IFarmModule
{
    ModuleDirective Next(WorldSnapshot w, ModuleContext ctx);
}
