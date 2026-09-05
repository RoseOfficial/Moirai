using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Fates;
using Moirai.Core.Model;
using Moirai.Game;
using Moirai.Snapshot;

namespace Moirai.Diagnostics;

// A plain-text dump of what the game reports for every fate in the zone next to how Moirai
// reads it. Users paste it into a bug report; nothing here is needed to run.
public static class DebugReport
{
    public static string Build(Plugin plugin, SnapshotBuilder snapshots)
    {
        var sb = new StringBuilder();
        var director = plugin.Director;
        var currentId = director?.CurrentFate?.Id;
        var snap = snapshots.Build(currentId);
        var lp = Svc.Objects.LocalPlayer;

        sb.AppendLine($"Moirai {Plugin.Version} debug report {DateTimeOffset.UtcNow:u}");
        sb.AppendLine($"zone={Svc.ClientState.TerritoryType} level={lp?.Level.ToString() ?? "?"} synced={snap?.Player.IsLevelSynced.ToString() ?? "?"} pos={Fmt(lp?.Position)}");
        sb.AppendLine($"phase={director?.Phase.ToString() ?? "none"} currentFate={currentId?.ToString() ?? "none"} status='{plugin.LastStatus}'");
        sb.AppendLine($"skipNpcStart={plugin.Config.SkipNpcStartFates} navmesh={plugin.NavmeshReady} combat={plugin.CombatBackendLoaded} combatActive={plugin.CombatBackendActive?.ToString() ?? "unknown"} inCombat={snap?.Player.InCombat.ToString() ?? "?"} snapshot={(snap is null ? "none" : "ok")}");
        sb.AppendLine($"visible ui: {string.Join(", ", GameEx.VisibleAddonNames())}");
        sb.AppendLine();
        sb.AppendLine("fates: game fields | sheet | moirai");

        var count = 0;
        foreach (var fate in Svc.Fates)
        {
            if (fate == null) continue;
            count++;
            sb.Append($"  #{fate.FateId} '{fate.Name}' state={fate.State} start={fate.StartTimeEpoch} dur={fate.Duration} left={fate.TimeRemaining} prog={fate.Progress} handIn={SafeHandIn(fate)} lvl={fate.Level}-{fate.MaxLevel} icon={fate.IconId} pos={Fmt(fate.Position)} r={fate.Radius:0}");

            uint eventItem = 0, turnIn = 0, req = 0, rule = 0, icon = fate.IconId, banner = 0;
            try
            {
                if (fate.GameData.ValueNullable is { } row)
                {
                    eventItem = row.EventItem.RowId;
                    turnIn = row.TurnInEventItem.RowId;
                    req = row.ReqEventItem.RowId;
                    rule = row.Rule;
                    icon = row.Icon;
                    banner = row.ScreenImageAccept.RowId;
                }
                sb.Append($" | rule={rule} sheetIcon={icon} banner={banner} eventItem={eventItem} turnIn={turnIn} req={req}");
            }
            catch { sb.Append(" | sheet=unavailable"); }

            var phase = fate.State switch
            {
                FateState.Preparing => FatePhase.Preparing,
                FateState.Running => FatePhase.Running,
                FateState.Ended => FatePhase.Ended,
                FateState.Failed => FatePhase.Failed,
                _ => (FatePhase?)null,
            };
            var collectItem = eventItem != 0 ? eventItem : turnIn != 0 ? turnIn : req;
            var kind = phase is { } p
                ? FateClassifier.Classify(collectItem, rule, icon, p, fate.StartTimeEpoch, fate.Progress).ToString()
                : "dropped";
            var special = FateClassifier.IsSpecialBoss(FateClassifier.FromSheet(collectItem, rule, icon), banner);
            var unopened = phase is { } q && FateClassifier.IsUnopened(q, fate.StartTimeEpoch, fate.Progress);
            var projected = snap?.FateById(fate.FateId);
            var timeLeft = projected is null ? "" : $" timeLeft={projected.EffectiveTimeLeft}";
            sb.AppendLine($" | kind={kind} special={special} unopened={unopened} inSnapshot={projected is not null}{timeLeft}");
        }
        sb.AppendLine($"  ({count} fates)");

        if (snap is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"objects seen (fate #{currentId?.ToString() ?? "none"}): {snap.Enemies.Count} enemies, {snap.Interactables.Count} interactables");
            foreach (var e in snap.Enemies)
                sb.AppendLine($"  enemy id={e.Id} fate={e.FateId} maxHp={e.MaxHp} onUs={e.IsAttackingPlayer} peel={e.TargetsProtectedFriendly} dist={Vector3.Distance(e.Position, snap.Player.Position):0.0}");
            foreach (var i in snap.Interactables)
                sb.AppendLine($"  {i.Kind} id={i.Id} fate={i.FateId} dist={Vector3.Distance(i.Position, snap.Player.Position):0.0}");
        }
        return sb.ToString();
    }

    private static string SafeHandIn(IFate fate)
    {
        try { return fate.HandInCount.ToString(); }
        catch { return "?"; }
    }

    private static string Fmt(Vector3? v) => v is { } p ? $"{p.X:0.0},{p.Y:0.0},{p.Z:0.0}" : "?";
}
