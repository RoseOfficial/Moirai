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

    // C16: whether the character may take off here is the game's own verdict, the same check it
    // runs when the mounted player jumps: the zone's currents or its unlock quest, and the mount's
    // own flying condition. It is only answered from the saddle; on foot the flag the game sets for
    // the zone at load stands in, for display and for the leg that has not mounted yet.
    public static bool CanFlyHere(bool mounted, bool flying)
    {
        if (flying) return true;
        if (mounted) return Control.CanFly;
        var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        return ps != null && ps->CanFly;
    }

    // Diagnostics: the game's reason when the mounted character may not take off
    public static string FlightStatus()
    {
        try { return Control.GetFlightAllowedStatus().ToString(); }
        catch { return "?"; }
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

    // §8: minions are companions; summoning one is an action of its own type
    public static void SummonMinion(uint companionId)
        => ActionManager.Instance()->UseAction(ActionType.Companion, companionId);

    public static bool IsCompanionUnlocked(uint companionId)
    {
        try { return FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->IsCompanionUnlocked(companionId); }
        catch { return true; } // fail open: the summon itself is a no-op when locked
    }

    // E1: judged by the equipped container, not by counting the item in the bags
    public static bool IsItemEquipped(uint itemId)
    {
        var container = InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems);
        if (container == null) return false;
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot != null && slot->ItemId == itemId) return true;
        }
        return false;
    }

    private const ushort WristEquipSlot = 10;

    // E1: the Yo-kai Watch is wrist-only; move it from the bags into the wrist slot
    public static bool EquipWristItem(uint itemId)
    {
        var im = InventoryManager.Instance();
        ReadOnlySpan<InventoryType> bags =
            [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
        foreach (var bag in bags)
        {
            var container = im->GetInventoryContainer(bag);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId != itemId) continue;
                im->MoveItemSlot(bag, (ushort)i, InventoryType.EquippedItems, WristEquipSlot, true);
                return true;
            }
        }
        return false;
    }

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

    public static bool AddonReady(string name) => ReadyAddon(name) != null;

    // E9: the game's own callback surface for a window: a list of ints, the way the field scripts
    // drive menus and exchange windows (an entry index, or 0 / row / count for a purchase)
    public static bool FireAddonCallback(string name, params int[] values)
    {
        var addon = ReadyAddon(name);
        if (addon == null) return false;
        var atk = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
            atk[i].SetInt(values[i]);
        addon->FireCallback((uint)values.Length, atk, false);
        return true;
    }

    public static bool CloseAddon(string name)
    {
        var addon = ReadyAddon(name);
        if (addon == null) return false;
        addon->Close(true);
        return true;
    }

    // Diagnostics: every value a window carries, so a report pins menu entries and shop rows
    public static List<string> AddonValues(string name)
    {
        var lines = new List<string>();
        var addon = ReadyAddon(name);
        if (addon == null) return lines;
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var v = addon->AtkValues[i];
            string text;
            try { text = v.GetValueAsString(); }
            catch { text = "?"; }
            if (!string.IsNullOrEmpty(text)) lines.Add($"[{i}] {v.Type} {text}");
        }
        return lines;
    }

    // E9: the first event NPC in view whose base id is one of the given
    public static IGameObject? FindNpc(IReadOnlySet<uint> baseIds)
    {
        foreach (var obj in Svc.Objects)
            if (obj is { ObjectKind: Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc } && baseIds.Contains(obj.BaseId))
                return obj;
        return null;
    }

    // E9: an NPC's placement from the Level sheet (type 8 rows link to ENpcBase), for walking to
    // one that is not in view yet; null when the sheet has none
    public static (ushort Territory, System.Numerics.Vector3 Position)? NpcPlacement(IReadOnlySet<uint> baseIds)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Level>();
        if (sheet is null) return null;
        foreach (var row in sheet)
        {
            if (row.Type != 8 || !baseIds.Contains(row.Object.RowId)) continue;
            return ((ushort)row.Territory.RowId, new System.Numerics.Vector3(row.X, row.Y, row.Z));
        }
        return null;
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
