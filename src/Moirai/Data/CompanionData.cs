namespace Moirai.Data;

// Curated companion knowledge: item and BuddyAction row ids. Names are for display only.
public static class CompanionData
{
    public const uint GysahlGreensItemId = 4868;

    public sealed record Stance(uint ActionId, string Name);

    // BuddyAction rows: 3 Follow, 4 Free Stance, 5 Defender, 6 Attacker, 7 Healer.
    // Action id 0 tells the planner to leave whatever stance the player set.
    public static IReadOnlyList<Stance> Stances { get; } =
    [
        new(0, "Leave as is"),
        new(5, "Defender"),
        new(6, "Attacker"),
        new(7, "Healer"),
        new(4, "Free Stance"),
    ];

    public static string StanceName(uint actionId)
    {
        foreach (var s in Stances)
            if (s.ActionId == actionId) return s.Name;
        return $"Stance {actionId}";
    }
}
