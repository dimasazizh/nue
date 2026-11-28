using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Production;

namespace Nue.SearchCompare;

public sealed partial class SearchMod
{
    private const string Overview = "overview", CraftTab = "craft", ResearchTab = "research";
    private static MyGuiScreenHost ScreensOf(object player) => (MyGuiScreenHost)PlayerScreens.GetValue(player)!;
    private static WorldSnapshot SnapshotOf(object player) => ((WorldSession)PlayerSession.GetValue(player)!).CurrentSnapshot;

    private static bool ProductionSearchInput(MyGuiEvent e, object player)
    {
        if (e.Screen != Overview) return false;
        string? key = (e.Widget, e.Action) switch
        {
            (Prefix + CraftTab, "text") or ("NueClear." + CraftTab, ClearAction) => CraftTab,
            (Prefix + ResearchTab, "text") or ("NueClear." + ResearchTab, ClearAction) => ResearchTab,
            _ => null
        };
        if (key is null) return false;
        if (e.Action == ClearAction)
        {
            ScreensOf(player).SetText(Overview, Prefix + key, "");
            Queries.Remove(Prefix + key);
        }
        else
            Queries[Prefix + key] = e.Text;

        (key == CraftTab ? CraftScroll : ResearchScroll).SetValue(Property(player, "Production"), 0f);
        return true;
    }

    private static void CraftBar(object __instance, List<UiControl> controls, out bool __state)
    {
        __state = false;
        var player = ProductionPlayer.GetValue(__instance)!;
        if (!ProductionBar(player, controls, CraftTab, 3, "QueueItemsPanel", "QueueDescriptionPanel1", "Search all items") ||
            CraftStation.GetValue(__instance) is not null || Query(CraftTab).Length == 0) return;

        // The native item list needs a live station even when the query searches all stations.
        var snapshot = SnapshotOf(player);
        if (snapshot.Machines.FirstOrDefault(m => m.Craft is not null && snapshot.Crafts.ContainsKey(m.Site)) is not {} station)
            return;
        CraftStation.SetValue(__instance, station.Site);
        __state = true;
    }

    private static void RestoreCraftStation(object __instance, bool __state)
    {
        if (__state) CraftStation.SetValue(__instance, null);
    }

    private static void ResearchBar(object __instance, List<UiControl> controls) =>
        ProductionBar(ProductionPlayer.GetValue(__instance)!, controls, ResearchTab, 2,
            "BlueprintsAvailablePanel", "ResearchDescriptionPanel1", "Search all blueprints");

    private static bool ProductionBar(object player, List<UiControl> controls, string key, int tab,
        string listName, string descriptionName, string hint)
    {
        var screens = ScreensOf(player);
        if (!screens.IsOpen(Overview) || screens.GetTab(Overview, "TabsMain") != tab ||
            controls.Find(c => c.Type == "Window") is not {} window) return false;
        int index = controls.FindIndex(c => c.Name == listName);
        if (index < 0) return false;

        var list = controls[index];
        var bounds = list.Bounds;
        float scale = (float)Property(player, "UiScale");
        float gap = Math.Max(2, 3 * scale);
        float height = Math.Min(27 * scale, bounds.Height / 5);
        float bottom = controls.Find(c => c.Name == descriptionName) is {} description
            ? description.Bounds.Y - 10 * scale : bounds.Y + bounds.Height;
        float y = Math.Min(bounds.Y + bounds.Height + gap, bottom - height);
        float clearWidth = Math.Min(height * 1.25f, bounds.Width / 4);
        if (height <= 0 || y <= bounds.Y + gap || bounds.Width <= clearWidth + gap) return false;
        if (y - gap < bounds.Y + bounds.Height)
            controls[index] = list with { Bounds = bounds with { Height = y - gap - bounds.Y } };

        var field = new UiRect(bounds.X, y, bounds.Width - clearWidth - gap, height);
        AddStockSearch(controls, screens, Overview, window.Bounds, field, bounds.X + bounds.Width - clearWidth,
            clearWidth, key, hint);
        return true;
    }

    private static IEnumerable<CodeInstruction> SearchAllCrafts(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var op in instructions)
        {
            yield return op;
            if (EnumerableCall<CraftOptionView>(op, nameof(Enumerable.ToList)))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SearchMod), nameof(CraftMatches)));
            }
        }
    }

    private static IEnumerable<CodeInstruction> SearchAllResearch(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var op in instructions)
        {
            if (EnumerableCall<ResearchOption>(op, nameof(Enumerable.Where)))
                op.operand = AccessTools.Method(typeof(SearchMod), nameof(ResearchMatches));
            yield return op;
        }
    }

    private static bool EnumerableCall<T>(CodeInstruction op, string name) =>
        op.operand is MethodInfo { IsGenericMethod: true } method && method.Name == name &&
        method.DeclaringType == typeof(Enumerable) && method.GetGenericArguments()[0] == typeof(T);

    private static List<CraftOptionView> CraftMatches(List<CraftOptionView> local, object production)
    {
        string query = Query(CraftTab);
        if (query.Length == 0) return local;
        var snapshot = SnapshotOf(ProductionPlayer.GetValue(production)!);
        return snapshot.Crafts.Values.SelectMany(rows => rows)
            .Where(row => Match(row.Name, query))
            .OrderByDescending(row => row.Unlocked).ThenBy(row => row.Name, StringComparer.Ordinal)
            .DistinctBy(row => row.Key).ToList();
    }

    private static IEnumerable<ResearchOption> ResearchMatches(IEnumerable<ResearchOption> rows,
        Func<ResearchOption, bool> currentCategory)
    {
        string query = Query(ResearchTab);
        return query.Length == 0 ? rows.Where(currentCategory) : rows.Where(row => Match(row.Name, query));
    }

    private static void RouteCraftResult(object __instance, MyGuiEvent e)
    {
        if (e.Screen != Overview || Query(CraftTab).Length == 0 || !e.Widget.StartsWith("craft-row-", StringComparison.Ordinal) ||
            !TryCraftRecipe(e.Action, out string recipe, out bool queue)) return;
        var stations = SnapshotOf(ProductionPlayer.GetValue(__instance)!).Crafts;
        var current = CraftStation.GetValue(__instance) as string;
        if (FindRecipe(stations, recipe, current, queue) is {} result && result.Station != current)
            CraftStation.SetValue(__instance, result.Station);
    }

    private static bool TryCraftRecipe(string action, out string recipe, out bool queue)
    {
        const string enqueue = "craft-queue:", focus = "craft-focus:";
        queue = action.StartsWith(enqueue, StringComparison.Ordinal);
        recipe = queue ? action[enqueue.Length..] :
            action.StartsWith(focus, StringComparison.Ordinal) ? action[focus.Length..] : "";
        return recipe.Length > 0;
    }

    private static (string Station, CraftOptionView Option)? FindRecipe(
        IReadOnlyDictionary<string, IReadOnlyList<CraftOptionView>> stations, string recipe, string? current, bool unlocked)
    {
        bool Matches(CraftOptionView row) => row.Key == recipe && (!unlocked || row.Unlocked);
        if (current is not null && stations.TryGetValue(current, out var local) && local.FirstOrDefault(Matches) is {} option)
            return (current, option);
        foreach (var (station, rows) in stations)
            if (rows.FirstOrDefault(Matches) is {} match) return (station, match);
        return null;
    }

    private static void CraftIconTooltip(object __instance, List<UiControl> controls)
    {
        var player = TooltipPlayer.GetValue(__instance)!;
        var screens = ScreensOf(player);
        if (!screens.IsOpen(Overview) || screens.GetTab(Overview, "TabsMain") != 3 ||
            PlayerMouse.GetValue(player) is not Vector2 mouse ||
            screens.HitTest(mouse) is not { Screen: Overview } hit ||
            !hit.Control.Name.StartsWith("craft-row-", StringComparison.Ordinal) ||
            !TryCraftRecipe(hit.Control.Action, out string recipe, out bool unlocked)) return;

        var stations = SnapshotOf(player).Crafts;
        var current = CraftStation.GetValue(Property(player, "Production")) as string;
        if (FindRecipe(stations, recipe, current, unlocked) is not {} result || result.Option.Product is not {} item) return;

        int width = (int)PlayerWidth.GetValue(player)!, height = (int)PlayerHeight.GetValue(player)!;
        float size = Math.Clamp(68 * (float)Property(player, "UiScale"), 42, 104);
        const float pad = 6, offset = 14;
        float x = mouse.X + offset + size <= width ? mouse.X + offset : Math.Max(0, mouse.X - offset - size);
        float y = Math.Clamp(mouse.Y + offset, 0, Math.Max(0, height - size));
        var popup = new UiRect(x, y, size, size);
        controls.Add(new("NueCraftIconTooltip", "Widget", popup, "", "", "Kenshi_GenericTextBoxSkin",
            true, new Dictionary<string, string>(), new Vector4(.09f, .07f, .05f, .96f), Pickable: false, Clip: popup));
        float cell = (size - 2 * pad) / Math.Max(1, Math.Max(item.Width, item.Height));
        float iconWidth = Math.Max(1, item.Width) * cell, iconHeight = Math.Max(1, item.Height) * cell;
        var icon = new UiRect(x + (size - iconWidth) / 2, y + (size - iconHeight) / 2, iconWidth, iconHeight);
        DrawItemCell.Invoke(ItemCells.Invoke(player, null), [controls, "NueCraftTooltipItem", icon, item.Record,
            item.Material, item.Name, 1, null, "", item.Index, popup, false, null, item.Colour, item.Rotated, false, ""]);
    }
}
