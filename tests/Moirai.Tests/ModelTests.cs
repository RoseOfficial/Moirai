using Moirai.Core.Model;

namespace Moirai.Tests;

public class ModelTests
{
    [Fact] // A8: unopened NPC fates assume a 900 s budget
    public void A8_effective_time_defaults_to_900_when_unopened()
    {
        var f = TestData.Fate(startTimeEpoch: 0, timeRemaining: 0);
        Assert.Equal(900, f.EffectiveTimeLeft);
    }

    [Fact]
    public void Effective_time_uses_remaining_when_opened()
    {
        var f = TestData.Fate(startTimeEpoch: 1_000, timeRemaining: 321);
        Assert.Equal(321, f.EffectiveTimeLeft);
    }

    [Fact]
    public void World_item_count_is_zero_when_absent()
    {
        var w = TestData.World();
        Assert.Equal(0, w.CountOf(12345));
    }
}
