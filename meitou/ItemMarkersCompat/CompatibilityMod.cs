using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;

namespace Nue.ItemMarkersCompat;

public sealed class CompatibilityMod : IMod
{
    private const string MarkerPrefix = "ItemMarkers:item-markers:";
    private const string ItemPrefix = "inventory-item-";
    private const string Background = "-background";
    private const string Icon = "-icon";

    private static MethodInfo drawMarkers = null!;
    private static MethodInfo getItemCells = null!;
    private static FieldInfo routerPlayer = null!;

    public void Initialize(ModContext context)
    {
        var avatar = AccessTools.Method(AccessTools.TypeByName("Nue.InventoryAvatar.AvatarMod"), "Controls",
            [typeof(string), typeof(List<UiControl>), typeof(object)]);
        var hook = AccessTools.Method(AccessTools.TypeByName("ItemMarkers.ItemCellHook"), "AfterAddItemCell",
            [typeof(object), typeof(List<UiControl>), typeof(string), typeof(UiRect), typeof(UiRect?)]);
        var router = AccessTools.TypeByName("Meitou.Game.Ui.ScreenRouter");
        var player = AccessTools.TypeByName("Meitou.Game.PlayerInterface");
        var cells = AccessTools.PropertyGetter(player, "ItemCells");
        var field = router is null ? null : AccessTools.GetDeclaredFields(router).SingleOrDefault(f => f.FieldType == player);
        if (avatar is null || hook is null || cells is null || field is null)
        {
            context.Log("NUE Item Markers compatibility needs the NUE inventory avatar and the modern Item Markers cell hook.");
            return;
        }

        drawMarkers = hook;
        getItemCells = cells;
        routerPlayer = field;
        context.Harmony.Patch(avatar,
            prefix: new HarmonyMethod(typeof(CompatibilityMod), nameof(CaptureCells)),
            postfix: new HarmonyMethod(typeof(CompatibilityMod), nameof(PlaceMarkers)));
    }

    private static void CaptureCells(List<UiControl> controls, out Dictionary<string, UiControl>? __state)
    {
        __state = null;
        var marked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in controls)
            if (MarkedCell(control.Name) is { } name) marked.Add(name);
        if (marked.Count == 0) return;

        __state = new(StringComparer.Ordinal);
        foreach (var control in controls)
            if (BackgroundCell(control.Name) is { } name && marked.Contains(name))
                __state[name] = control;
    }

    private static void PlaceMarkers(List<UiControl> controls, object __2, Dictionary<string, UiControl>? __state)
    {
        if (__state is null || __state.Count == 0) return;
        var remaining = new HashSet<UiControl>(controls, ReferenceEqualityComparer.Instance);
        var moved = __state.Where(pair => !remaining.Contains(pair.Value))
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        if (moved.Count == 0) return;

        var player = routerPlayer.GetValue(__2);
        if (player is null || getItemCells.Invoke(player, null) is not { } cells) return;
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
            drawMarkers.Invoke(null, [cells, markers, cell, control.Bounds, control.Clip]);
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
