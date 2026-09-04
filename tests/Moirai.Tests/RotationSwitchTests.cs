using Moirai.Core.Planning;

namespace Moirai.Tests;

// The combat backend changes state on its own: it switches itself off after a spell out of
// combat, on death and on a zone change, and the user may switch it on. What it reports beats
// what we remember, and a mode command while it is already on toggles or cycles it, so every
// transition between modes goes through off.
public class RotationSwitchTests
{
    [Fact] // the backend switched itself off: our memory is stale, so the next request must turn it on again
    public void Backend_off_on_its_own_is_switched_back_on()
        => Assert.Equal(new[] { RotationMode.Auto },
            RotationSwitch.Plan(last: RotationMode.Auto, active: false, want: RotationMode.Auto));

    [Fact] // already in the wanted mode: nothing is sent (a repeat would cycle or toggle it)
    public void Already_in_wanted_mode_sends_nothing()
        => Assert.Empty(RotationSwitch.Plan(last: RotationMode.Auto, active: true, want: RotationMode.Auto));

    [Fact] // a mode change while on goes through off, so the backend never toggles or cycles
    public void Mode_change_while_on_goes_through_off()
        => Assert.Equal(new[] { RotationMode.Off, RotationMode.Manual },
            RotationSwitch.Plan(last: RotationMode.Auto, active: true, want: RotationMode.Manual));

    [Fact] // on in a mode we did not set (the user turned it on): a clean transition through off
    public void Unknown_mode_while_on_goes_through_off()
        => Assert.Equal(new[] { RotationMode.Off, RotationMode.Auto },
            RotationSwitch.Plan(last: null, active: true, want: RotationMode.Auto));

    [Fact] // the backend reports off: no off command is needed whatever we remember
    public void Off_when_backend_reports_off_sends_nothing()
        => Assert.Empty(RotationSwitch.Plan(last: RotationMode.Auto, active: false, want: RotationMode.Off));

    [Fact]
    public void Off_when_on_sends_off()
        => Assert.Equal(new[] { RotationMode.Off },
            RotationSwitch.Plan(last: RotationMode.Manual, active: true, want: RotationMode.Off));

    [Fact] // no state report (older backend): memory decides, as before
    public void Without_a_state_report_memory_decides()
    {
        Assert.Empty(RotationSwitch.Plan(last: RotationMode.Auto, active: null, want: RotationMode.Auto));
        Assert.Equal(new[] { RotationMode.Auto }, RotationSwitch.Plan(last: RotationMode.Off, active: null, want: RotationMode.Auto));
        Assert.Equal(new[] { RotationMode.Off, RotationMode.Manual }, RotationSwitch.Plan(last: RotationMode.Auto, active: null, want: RotationMode.Manual));
    }

    [Fact] // no state report and no memory: off is sent to be safe, a mode goes through off
    public void Without_a_state_report_or_memory_plays_safe()
    {
        Assert.Equal(new[] { RotationMode.Off }, RotationSwitch.Plan(last: null, active: null, want: RotationMode.Off));
        Assert.Equal(new[] { RotationMode.Off, RotationMode.Auto }, RotationSwitch.Plan(last: null, active: null, want: RotationMode.Auto));
    }
}
