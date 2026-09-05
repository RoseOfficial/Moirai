using Moirai.Core.Intents;
using Moirai.Core.Modules;

namespace Moirai.Tests;

public class ZoneRotationModuleTests
{
    private static ZoneRotationModule Sut(params ushort[] zones)
        => new(new RotationConfig { Zones = zones, QuietSeconds = 120, MoveTimeoutSeconds = 60 });

    private static ModuleContext Idle(long seconds) => new(IdleSeconds: seconds);

    [Fact] // G1: the starting zone is the target when it is listed; otherwise the first listed zone
    public void G1_targets_the_starting_zone_when_listed_else_the_first()
    {
        Assert.IsType<FarmHere>(Sut(140, 141).Next(TestData.World(territory: 141), Idle(0)));

        var elsewhere = Sut(140, 141).Next(TestData.World(territory: 129), Idle(0));
        Assert.Equal(140, Assert.IsType<MoveToTerritory>(elsewhere).TerritoryId);
    }

    [Fact] // G2: a quiet zone is left for the next one in the list, wrapping around
    public void G2_quiet_period_advances_and_wraps()
    {
        var m = Sut(140, 141);
        Assert.IsType<FarmHere>(m.Next(TestData.World(now: 1000, territory: 140), Idle(119)));
        Assert.Equal(141, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1001, territory: 140), Idle(120))).TerritoryId);

        Assert.IsType<FarmHere>(m.Next(TestData.World(now: 1030, territory: 141), Idle(0)));
        Assert.Equal(140, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1200, territory: 141), Idle(120))).TerritoryId);
    }

    [Fact] // G3: a single listed zone is a pin: quiet or not, it stays
    public void G3_single_zone_list_never_moves_on()
    {
        var m = Sut(140);
        Assert.IsType<FarmHere>(m.Next(TestData.World(territory: 140), Idle(9_999)));
        Assert.Equal(140, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(territory: 129), Idle(0))).TerritoryId);
    }

    [Fact] // G4: a zone change that never lands is given up on and the next zone tried
    public void G4_zone_change_that_never_lands_skips_the_zone()
    {
        var m = Sut(140, 141, 146);
        m.Next(TestData.World(now: 1000, territory: 140), Idle(0));
        Assert.Equal(141, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1000, territory: 140), Idle(120))).TerritoryId);
        Assert.Equal(141, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1059, territory: 140), Idle(179))).TerritoryId);
        Assert.Equal(146, Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1060, territory: 140), Idle(180))).TerritoryId);
    }

    [Fact] // G5: every listed zone failing stops the run with a reason; arriving anywhere clears the count
    public void G5_all_zones_unreachable_stops_the_run()
    {
        var m = Sut(140, 141);
        m.Next(TestData.World(now: 1000, territory: 129), Idle(0));                       // heading for 140
        Assert.IsType<MoveToTerritory>(m.Next(TestData.World(now: 1060, territory: 129), Idle(0))); // 140 failed: try 141
        var stop = Assert.IsType<StopSession>(m.Next(TestData.World(now: 1120, territory: 129), Idle(0)));
        Assert.Equal(StopReason.ZonesUnreachable, stop.Reason);
    }
}
