using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public abstract record ModuleDirective;
public sealed record FarmHere : ModuleDirective;
public sealed record MoveToTerritory(ushort TerritoryId) : ModuleDirective;
public sealed record StopSession(StopReason Reason, string Summary) : ModuleDirective;

// What the Director tells the module each tick besides the snapshot: how long selection has
// come up empty in this zone (G6), which is the module's cue to move on
public sealed record ModuleContext(long IdleSeconds);

public interface IFarmModule
{
    ModuleDirective Next(WorldSnapshot w, ModuleContext ctx);
}
