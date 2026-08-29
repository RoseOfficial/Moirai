using Moirai.Core.Model;

namespace Moirai.Core.Modules;

public sealed class SingleZoneModule : IFarmModule
{
    public ModuleDirective Next(WorldSnapshot w) => new FarmHere();
}
