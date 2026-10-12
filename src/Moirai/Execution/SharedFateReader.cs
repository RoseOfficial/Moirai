using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Moirai.Core.Model;
using Moirai.Game;

namespace Moirai.Execution;

// S4/S5: reads the Dawntrail zones' Shared FATE ranks. The game keeps them only in the Shared FATE
// window's agent and fills it when the window opens, so a read opens the window, gives the agent a
// moment, copies the zones, and closes it again. One attempt either lands with the zones or counts
// as a failure; the planner decides when to ask again. A zone whose rank never arrives (one the
// character has not reached) is left out of the read, and the planner takes it as rank 0.
public sealed unsafe class SharedFateReader(IReadOnlyList<ushort> zones) : IShellTask
{
    public const string Addon = "FateProgress";
    private const long AttemptTimeoutMs = 10_000;
    private const long SettleMs = 1_500; // the agent fills a moment after the window opens
    private const long CloseForceMs = 2_000; // then the window is closed outright
    private const long PartialAfterMs = 5_000; // the zones fill one by one; a zone still missing then is one the character lacks

    private enum Step { Idle, Opening, Reading, Closing }

    private Step _step = Step.Idle;
    private long _startedMs;
    private long _openedMs;
    private long _closingMs;
    private List<ZoneRank>? _captured;

    public SharedFateRanks? Result { get; private set; }
    public bool IsActive => _step != Step.Idle;
    public string Status => "reading Shared FATE ranks";
    public string DebugState => $"step={_step} reads={Result?.Reads ?? 0} failures={Result?.Failures ?? 0}";

    public void Begin()
    {
        if (IsActive) return;
        _step = Step.Opening;
        _startedMs = Environment.TickCount64;
        _captured = null;
    }

    // A new run reads afresh (S4: a read at the start)
    public void NewSession()
    {
        Reset();
        Result = null;
    }

    // A pause, a stop or a duty drops the read under way; it counts as nothing
    public void Reset()
    {
        if (_step != Step.Idle && GameEx.AddonVisible(Addon)) GameEx.FireAddonCallback(Addon, true, -1);
        _step = Step.Idle;
    }

    public void Tick()
    {
        if (!IsActive) return;
        var now = Environment.TickCount64;
        if (now - _startedMs > AttemptTimeoutMs)
        {
            // ranks copied are a read that landed, even when the window would not close
            Finish(landed: _captured is not null);
            return;
        }

        switch (_step)
        {
            case Step.Opening:
                if (GameEx.AddonReady(Addon))
                {
                    _openedMs = now;
                    _step = Step.Reading;
                    break;
                }
                if (Throttle.Try("moirai.sharedfate.open", 1500)) GameEx.OpenSharedFate();
                break;

            case Step.Reading:
                if (now - _openedMs < SettleMs) break;
                // every listed zone, or what the agent has once it has had its time; a read taken
                // halfway once showed a zone the character had ranked as rank 0
                if (ReadAgent() is not { } read) break; // not filled yet; the attempt's timeout bounds the wait
                if (read.Count < zones.Count && now - _openedMs < PartialAfterMs) break;
                _captured = read;
                _closingMs = now;
                _step = Step.Closing;
                break;

            case Step.Closing:
                if (!GameEx.AddonVisible(Addon))
                {
                    Finish(landed: true);
                    break;
                }
                // the window's cancel callback first; a window that ignores it is closed outright
                if (now - _closingMs > CloseForceMs)
                {
                    if (Throttle.Try("moirai.sharedfate.closeforce", 500)) GameEx.CloseAddon(Addon);
                }
                else if (Throttle.Try("moirai.sharedfate.close", 500))
                {
                    GameEx.FireAddonCallback(Addon, true, -1);
                }
                break;
        }
    }

    private void Finish(bool landed)
    {
        if (GameEx.AddonVisible(Addon)) GameEx.CloseAddon(Addon);
        Result = landed
            ? new SharedFateRanks(_captured!, (Result?.Reads ?? 0) + 1, 0)
            : new SharedFateRanks(Result?.Zones ?? [], Result?.Reads ?? 0, (Result?.Failures ?? 0) + 1);
        _step = Step.Idle;
        if (!landed)
            Svc.Chat.Print("[Moirai] The Shared FATE window could not be read; trying again in a minute.");
    }

    // The listed zones whose ranks have arrived (RankRead), or null while none has. A zone still to
    // arrive is left out; the planner reads a missing zone as rank 0 (S2) without predicting a rank-up.
    private List<ZoneRank>? ReadAgent()
    {
        var agent = AgentFateProgress.Instance();
        if (agent == null) return null;
        var carried = new List<AgentZone>();
        foreach (ref var tab in agent->Tabs)
            foreach (ref var z in tab.Zones)
                carried.Add(new AgentZone((ushort)z.TerritoryTypeId, z.CurrentRank, z.FateProgress));
        var arrived = RankRead.Arrived(carried, zones, FatesForNextRank);
        return arrived.Count == 0 ? null : arrived;
    }

    // FateProgressUI: the FATEs each rank-up takes in a zone, as increments (6, 20, 40 in Dawntrail);
    // read from the sheet rather than the agent, whose own count is not verified
    private static int FatesForNextRank(ushort territory, int rank)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.FateProgressUI>();
        if (sheet is null) return 0;
        foreach (var row in sheet)
            if (row.Location.RowId == territory)
                return rank switch { 1 => row.ReqFatesToRank2, 2 => row.ReqFatesToRank3, 3 => row.ReqFatesToRank4, _ => 0 };
        return 0;
    }

    // Diagnostics: every zone the agent carries, raw, so a report shows what the window said
    public static List<string> AgentLines()
    {
        var lines = new List<string>();
        var agent = AgentFateProgress.Instance();
        if (agent == null) return lines;
        foreach (ref var tab in agent->Tabs)
            foreach (ref var z in tab.Zones)
                if (z.TerritoryTypeId != 0)
                    lines.Add($"{z.TerritoryTypeId} rank={z.CurrentRank}/{z.MaxRank} progress={z.FateProgress} needed={z.NeededFates} text='{z.ProgressText}'");
        return lines;
    }
}
