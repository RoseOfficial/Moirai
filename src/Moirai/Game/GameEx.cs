using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Component.GUI;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Moirai.Game;

// The only file that touches game memory directly. Every call is throttled by the executor.
public static unsafe class GameEx
{
    private const uint MountRouletteGeneralAction = 9;
    private const uint JumpGeneralAction = 2;

    public static ushort GetFateId(IGameObject obj)
        => obj.Address == nint.Zero ? (ushort)0 : ((CSGameObject*)obj.Address)->FateId;

    public static uint GetNameplateIcon(IGameObject obj)
        => obj.Address == nint.Zero ? 0u : ((CSGameObject*)obj.Address)->NamePlateIconId;

    public static void MountRoulette()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, MountRouletteGeneralAction);

    // The mount general action toggles dismount when already mounted.
    public static void DismountToggle()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, MountRouletteGeneralAction);

    public static void Jump()
        => ActionManager.Instance()->UseAction(ActionType.GeneralAction, JumpGeneralAction);

    public static void SummonCompanion(uint companionId)
        => ActionManager.Instance()->UseAction(ActionType.Companion, companionId);

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

    // Equip a wrist item straight from the bags (the Yo-kai Watch is wrist-only).
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

    public static bool IsAddonVisible(string name)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible;
    }

    public static bool FireAddonCallback(string name, params int[] values)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
        if (addon == null || !addon->IsVisible) return false;
        var atkValues = stackalloc AtkValue[values.Length];
        for (var i = 0; i < values.Length; i++)
            atkValues[i].SetInt(values[i]);
        addon->FireCallback((uint)values.Length, atkValues, true);
        return true;
    }

    public static bool CloseAddon(string name)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
        if (addon == null || !addon->IsVisible) return false;
        addon->FireCallbackInt(-1);
        return true;
    }

    // Names of every currently visible addon — ground truth for UI debugging.
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

    // Find the index of a menu entry containing the given text (case-insensitive)
    // in a SelectString / SelectIconString popup. Returns -1 when absent.
    public static int FindMenuEntry(string addonName, string contains, out string allEntries)
    {
        allEntries = "";
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(addonName).Address;
        if (addon == null || !addon->IsVisible) return -1;

        var menu = addonName == "SelectIconString"
            ? &((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectIconString*)addon)->PopupMenu.PopupMenu
            : &((FFXIVClientStructs.FFXIV.Client.UI.AddonSelectString*)addon)->PopupMenu.PopupMenu;

        var found = -1;
        var texts = new List<string>();
        for (var i = 0; i < menu->EntryCount; i++)
        {
            var text = Dalamud.Memory.MemoryHelper
                .ReadSeStringNullTerminated((nint)menu->EntryNames[i].Value).TextValue;
            texts.Add(text);
            if (found < 0 && text.Contains(contains, StringComparison.OrdinalIgnoreCase))
                found = i;
        }
        allEntries = string.Join(" | ", texts);
        return found;
    }

    // Confirm a non-gil exchange dialog by clicking its Exchange button (node 18).
    public static bool ClickExchangeConfirm()
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("ShopExchangeItemDialog").Address;
        if (addon == null || !addon->IsVisible) return false;
        var button = addon->GetComponentButtonById(18);
        if (button == null || !button->IsEnabled || !button->AtkResNode->IsVisible()) return false;
        var node = button->AtkComponentBase.OwnerNode->AtkResNode;
        var evt = (AtkEvent*)node.AtkEventManager.Event;
        addon->ReceiveEvent(evt->State.EventType, (int)evt->Param, node.AtkEventManager.Event);
        return true;
    }

    // Advance a Talk dialogue box by synthesizing the click the game expects.
    public static bool ClickTalk()
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("Talk").Address;
        if (addon == null || !addon->IsVisible) return false;

        var evt = stackalloc AtkEvent[1];
        evt[0] = new AtkEvent
        {
            Listener = (AtkEventListener*)addon,
            Target = &AtkStage.Instance()->AtkEventTarget,
            State = new() { StateFlags = (AtkEventStateFlags)132 },
        };
        var data = stackalloc AtkEventData[1];
        for (var i = 0; i < sizeof(AtkEventData); i++)
            ((byte*)data)[i] = 0;

        addon->ReceiveEvent(AtkEventType.MouseDown, 0, evt, data);
        addon->ReceiveEvent(AtkEventType.MouseClick, 0, evt, data);
        addon->ReceiveEvent(AtkEventType.MouseUp, 0, evt, data);
        return true;
    }

    public static bool IsCompanionUnlocked(uint companionId)
    {
        try
        {
            return FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->IsCompanionUnlocked(companionId);
        }
        catch
        {
            return true; // fail open: the summon call itself is a no-op when locked
        }
    }

    // Confirms the topmost SelectYesno with "yes". Used only for the death return prompt.
    public static bool ClickYes()
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName("SelectYesno").Address;
        if (addon == null || !addon->IsVisible) return false;
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
