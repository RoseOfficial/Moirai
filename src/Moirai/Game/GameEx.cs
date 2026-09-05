using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Moirai.Core.Model;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Moirai.Game;

// The only file that touches game memory directly. Every call is throttled by the executor.
public static unsafe class GameEx
{
    private const uint MountRouletteGeneralAction = 9;
    private const uint JumpGeneralAction = 2;
    private const uint BlizzardAction = 142; // a hostile-only spell: castable on exactly the things the game calls enemies

    public static ushort GetFateId(IGameObject obj)
        => obj.Address == nint.Zero ? (ushort)0 : ((CSGameObject*)obj.Address)->FateId;

    // The game's own hostility test. Sub-kind cannot tell a fate's captors from its captives:
    // both are battle NPCs of the same kind, and only one of them may be attacked.
    public static bool IsHostile(IGameObject obj)
        => obj.Address != nint.Zero
           && ActionManager.CanUseActionOnTarget(BlizzardAction, (CSGameObject*)obj.Address);

    public static uint GetNameplateIcon(IGameObject obj)
        => obj.Address == nint.Zero ? 0u : ((CSGameObject*)obj.Address)->NamePlateIconId;

    public static void MountRoulette()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, MountRouletteGeneralAction);

    // The mount general action toggles dismount when already mounted.
    public static void DismountToggle()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, MountRouletteGeneralAction);

    public static void Jump()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, JumpGeneralAction);

    // C12/C15: mounting is legal where the territory allows it and the player owns a mount at all
    public static bool CanMountIn(ushort territoryId)
    {
        var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRowOrDefault(territoryId);
        if (row is not { Mount: true }) return false;
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        return ps != null && ps->NumOwnedMounts > 0;
    }

    // C16: flight needs the zone's aether currents, every one of them attuned
    public static bool CanFlyIn(ushort territoryId)
    {
        var row = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRowOrDefault(territoryId);
        var set = row?.AetherCurrentCompFlgSet.RowId ?? 0;
        if (set == 0) return false;
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        return ps != null && ps->IsAetherCurrentZoneComplete(set);
    }

    public static void LevelSyncIfNeeded()
    {
        var fm = FateManager.Instance();
        if (fm == null) return;
        if (fm->SyncedFateId == 0)
            fm->LevelSync();
    }

    public static void InteractWith(IGameObject obj)
    {
        if (obj.Address == nint.Zero) return;
        TargetSystem.Instance()->InteractWithObject((CSGameObject*)obj.Address, false);
    }

    public static int ItemCount(uint itemId)
        => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    public static void UseItem(uint itemId)
        => ActionManager.Instance()->UseAction(ActionType.Item, itemId, extraParam: 65535);

    // Companion commands are BuddyAction rows (stances, follow, withdraw).
    public static void SetBuddyAction(uint buddyActionId)
        => ActionManager.Instance()->UseAction(ActionType.BuddyAction, buddyActionId);

    // Seconds left on the summoned chocobo companion; 0 when it is not out.
    public static int CompanionTimeLeftSeconds()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui == null ? 0 : (int)MathF.Max(0f, ui->Buddy.CompanionInfo.TimeLeft);
    }

    // BuddyAction row id of the companion's active stance; 0 when unknown.
    public static uint CompanionStanceId()
    {
        var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
        return ui == null ? 0u : ui->Buddy.CompanionInfo.ActiveCommand;
    }

    // Names of every currently visible addon: ground truth for UI debugging.
    public static List<string> VisibleAddonNames()
    {
        var names = new List<string>();
        var manager = &AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager;
        var list = &manager->AllLoadedUnitsList;
        for (var i = 0; i < list->Count; i++)
        {
            var addon = list->Entries[i].Value;
            if (addon != null && addon->IsVisible)
                names.Add(addon->NameString);
        }
        return names;
    }

    // An addon that is on screen and finished loading: during its open and close animations it is
    // visible while its buttons are still null, so visibility alone is not enough to act on.
    private static AtkUnitBase* ReadyAddon(string name)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible && addon->IsReady ? addon : null;
    }

    // The dialog open on screen, for the snapshot: a prompt sits on top of a Talk window
    public static DialogKind DialogOpen()
    {
        if (ReadyAddon("SelectYesno") != null) return DialogKind.YesNo;
        if (ReadyAddon("Request") != null) return DialogKind.Request;
        if (ReadyAddon("Talk") != null) return DialogKind.Talk;
        return DialogKind.None;
    }

    // Confirms the open SelectYesno with "yes": the death return prompt and the fate-start prompt (B4)
    public static bool ClickYes()
    {
        var addon = ReadyAddon("SelectYesno");
        if (addon == null) return false;
        addon->FireCallbackInt(0);
        return true;
    }
}

public static class Throttle
{
    private static readonly Dictionary<string, long> Next = [];

    public static bool Try(string key, long ms)
    {
        var now = Environment.TickCount64;
        if (Next.TryGetValue(key, out var at) && now < at) return false;
        Next[key] = now + ms;
        return true;
    }
}
