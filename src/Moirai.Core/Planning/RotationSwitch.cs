namespace Moirai.Core.Planning;

public enum RotationMode { Off, Auto, Manual }

// Which mode commands a combat request needs. The backend changes state on its own: it turns
// itself off after a spell out of combat, on death and on a zone change, and the user may turn
// it on. So what it reports beats what we remember. A mode command while it is already on
// toggles or cycles it instead of switching cleanly, so every change of mode goes through off.
public static class RotationSwitch
{
    public static IReadOnlyList<RotationMode> Plan(RotationMode? last, bool? active, RotationMode want)
    {
        RotationMode? current = active switch
        {
            false => RotationMode.Off,
            true => last is null or RotationMode.Off ? null : last, // on, in a mode we did not set
            null => last,                                          // no report: memory decides
        };
        if (current == want) return [];
        if (want == RotationMode.Off) return [RotationMode.Off];
        return current == RotationMode.Off ? [want] : [RotationMode.Off, want];
    }
}
