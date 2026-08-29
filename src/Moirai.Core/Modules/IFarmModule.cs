using Moirai.Core.Intents;
using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public abstract record ModuleDirective;
public sealed record FarmHere : ModuleDirective;
public sealed record MoveToTerritory(ushort TerritoryId) : ModuleDirective;
public sealed record EnsureMinion(uint MinionId) : ModuleDirective;
public sealed record BuyMinion(uint MinionId, uint MinionItemId, int MedalCost) : ModuleDirective;
public sealed record EnsureWatch : ModuleDirective;
public sealed record StopSession(StopReason Reason, string Summary) : ModuleDirective;

public interface IFarmModule
{
    ModuleDirective Next(WorldSnapshot w);
}
