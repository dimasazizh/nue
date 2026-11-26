#if NUE_UNIFIED
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private const string MarkerPrefix = "ItemMarkers:item-markers:";
    private const string ItemPrefix = "inventory-item-";
    private const string Background = "-background";
    private const string Icon = "-icon";

    private static MethodInfo? drawMarkers;
    private static MethodInfo getItemCells = null!;

    internal static void EnableItemMarkersCompatibility(ModContext context)
    {
        var itemMarkers = AccessTools.TypeByName("ItemMarkers.ItemCellHook");
        if (itemMarkers is null) return;
        var hook = AccessTools.Method(itemMarkers, "AfterAddItemCell",
            [typeof(object), typeof(List<UiControl>), typeof(string), typeof(UiRect), typeof(UiRect?)]);
        var cells = AccessTools.PropertyGetter(Player, "ItemCells");
        if (hook is null || cells is null)
        {
            context.Log("NUE Item Markers compatibility needs the modern Item Markers cell hook.");
            return;
        }

        drawMarkers = hook;
        getItemCells = cells;
    }

    private static Dictionary<string, UiControl>? CaptureMarkedCells(List<UiControl> controls)
    {
        if (drawMarkers is null) return null;
        var marked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls)
            if (MarkedCell(control.Name) is { } name) marked.Add(name);
        if (marked.Count == 0) return null;

        var originalCells = new Dictionary<string, UiControl>(StringComparer.Ordinal);
        foreach (var control in controls)
            if (BackgroundCell(control.Name) is { } name && marked.Contains(name))
                originalCells[name] = control;
        return originalCells;
    }

    private static void PlaceMarkers(List<UiControl> controls, object player, Dictionary<string, UiControl>? originalCells)
    {
        if (originalCells is null || originalCells.Count == 0 || drawMarkers is not {} draw) return;
        var remaining = new HashSet<UiControl>(controls, ReferenceEqualityComparer.Instance);
        var moved = originalCells.Where(pair => !remaining.Contains(pair.Value))
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (moved.Count == 0) return;

        if (getItemCells.Invoke(player, null) is not { } cells) return;
        var result = new List<UiControl>(controls.Count);
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            if (MarkedCell(control.Name) is { } old && moved.Contains(old)) continue;
            result.Add(control);
            if (BackgroundCell(control.Name) is not { } cell || !moved.Contains(cell)) continue;

            // NUE emits a fresh background for each visible copy, including the expanded weapon rack.
            // Reuse the marker mod's sizing, nine-slice settings and badge placement for that copy.
            if (i + 1 < controls.Count && controls[i + 1].Name == cell + Icon)
                result.Add(controls[++i]);
            var markers = new List<UiControl>();
            draw.Invoke(null, [cells, markers, cell, control.Bounds, control.Clip]);
            result.AddRange(markers);
        }

        controls.Clear();
        controls.AddRange(result);
    }

    private static string? MarkedCell(string name)
    {
        if (!name.StartsWith(MarkerPrefix + ItemPrefix, StringComparison.Ordinal)) return null;
        int separator = name.IndexOf(':', MarkerPrefix.Length);
        return separator >= 0 ? name[MarkerPrefix.Length..separator] : null;
    }

    private static string? BackgroundCell(string name) =>
        name.StartsWith(ItemPrefix, StringComparison.Ordinal) && name.EndsWith(Background, StringComparison.Ordinal)
            ? name[..^Background.Length] : null;
}
#endif
