using Moirai.Core.Model;

namespace Moirai.Core.Modules;

// Farms the zone the run started in. D1: a death's return prompt can land the character at a
// home point in another zone, and a run must not settle for wherever it woke up.
public sealed class SingleZoneModule : IFarmModule
{
    private ushort? _home;

    public ModuleDirective Next(WorldSnapshot w, ModuleContext ctx)
    {
        _home ??= w.TerritoryId;
        return w.TerritoryId == _home ? new FarmHere() : new MoveToTerritory(_home.Value);
    }
}
