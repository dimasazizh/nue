using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Nue.HudTabs;

public sealed class HudTabsMod : IMod, IUiExtension
{
    public string Id => "nue.hud-tabs";
    static bool showStats;
    internal static bool ShowingStats => showStats;
    const string StatsTab = "NueHudStatsTab";
    const string HealthTab = "NueHudHealthTab";

    static readonly Type Player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
    static readonly Type Panels = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.HudPanels", true)!;
    static readonly FieldInfo Owner = Panels.GetFields(AccessTools.all).Single(f => f.FieldType == Player);
    static readonly FieldInfo Expanded = AccessTools.Field(Player, "InfoExpanded");
    static readonly PropertyInfo BuildingSelection = AccessTools.Property(Player, "BuildingSelection");
    static readonly MethodInfo BuildingFocus = AccessTools.Method(BuildingSelection.PropertyType, "BuildingFocus");
    static readonly MethodInfo BuildingLines = AccessTools.Method(BuildingSelection.PropertyType, "BuildingLines");
    static readonly PropertyInfo ModButtons = AccessTools.Property(BuildingSelection.PropertyType, "ModButtons");
    static readonly MethodInfo ButtonLines = AccessTools.Method(ModButtons.PropertyType, "Lines");
    static readonly PropertyInfo UiScale = AccessTools.Property(Player, "UiScale");
    static readonly FieldInfo Layout = AccessTools.Field(Player, "FaithfulHud");
    static readonly FieldInfo Width = AccessTools.Field(Player, "Width");
    static readonly FieldInfo Height = AccessTools.Field(Player, "Height");
    static readonly Dictionary<string, string> Empty = [];

    public void Initialize(ModContext context)
    {
        context.AddUi(this);
        HarmonyMethod Hook(string name) => new(typeof(HudTabsMod), name);
        context.Harmony.Patch(AccessTools.Method(Player, "ArrangeHud"), prefix: Hook(nameof(Position)), postfix: Hook(nameof(Tabs)));
        context.Harmony.Patch(AccessTools.Method(Panels, "AddHudText"), prefix: Hook(nameof(Expand)), finalizer: Hook(nameof(Restore)));
        context.Harmony.Patch(AccessTools.Method(Player, "AddLines"), prefix: Hook(nameof(FitRows)));
    }

    public void Register(UiRegistration registration)
    {
        registration.RegisterAction("stats", _ => showStats = true);
        registration.RegisterAction("health", _ => showStats = false);
    }

    static void Position(object __instance, UiBindings b, out UiRect? __state)
    {
        __state = null;
        var layout = (MyGuiLayout)Layout.GetValue(__instance)!;
        if (layout.Find("NUE_HealthBackdrop") is not { } backdrop || b.Hidden.Contains("InformationPanel")
            || b.Hidden.Contains("MedicalPanel"))
            return;

        var screen = new UiRect(0, 0, (int)Width.GetValue(__instance)!, (int)Height.GetValue(__instance)!);
        var dock = backdrop.Position.RelativeTo(screen, true);
        var selection = BuildingSelection.GetValue(__instance);
        if (BuildingFocus.Invoke(selection, null) is { } focus)
        {
            var lines = (ITuple)BuildingLines.Invoke(selection, [focus])!;
            int buttons = ((Array)ButtonLines.Invoke(ModButtons.GetValue(selection), [focus])!).Length;
            int meters = 0;
            while (!b.Hidden.Contains($"LifeBar{meters + 1}") && layout.Find($"LifeBar{meters + 1}") != null)
                meters++;
            ArrangeBuilding(b, dock, (float)UiScale.GetValue(__instance)!, ((Array)lines[1]!).Length,
                ((Array)lines[2]!).Length + buttons, ((Array)lines[5]!).Length, meters,
                (bool)Expanded.GetValue(__instance)!);
            return;
        }
        __state = dock;
        ArrangePanels(b, dock, showStats);
    }

    static void ArrangeBuilding(UiBindings b, UiRect dock, float scale, int state, int stats, int extra, int meters, bool expanded)
    {
        float row = 20 * scale, pad = 4 * scale;
        float bottom = dock.Y + dock.Height;
        float cursor = bottom - pad;
        var positions = b.Positions;
        UiRect Above(float height)
        {
            cursor -= height;
            return new(dock.X + pad, cursor, dock.Width - 2 * pad, height);
        }

        // Use the same row spacing as AddHudText, including the visible slice of a scrollable meter list.
        var medical = Above(meters * row);
        if (meters == 0) b.Hidden.Add("MedicalPanelDragAndMove");
        foreach (var name in new[] { "MedicalPanelDragAndMove", "MedicalPanel", "MedicalPanel_Back", "MedicalPanel_Front" })
            positions[name] = medical;
        for (int i = 1; i <= meters; i++)
        {
            float y = medical.Y + (i - 1) * row;
            var bar = new UiRect(medical.X + pad, y + scale, medical.Width * .72f, row - 2 * scale);
            string name = $"LifeBar{i}";
            positions[name] = bar;
            positions[name + "Value"] = new(bar.X + bar.Width, y, medical.Width - bar.Width - 2 * pad, row);
            positions[name + "Tooltip"] = medical with { Y = y, Height = row };
        }
        if (meters > 0) cursor -= pad;
        positions["StatsText"] = Above(stats * row);
        positions["StatsPanel"] = positions["StatsText"];
        positions["ExtendedStatsText"] = Above(expanded ? extra * row : 0);
        if (extra > 0)
        {
            var strip = Above(row);
            positions["ExtendedStatsButton"] = strip with { X = strip.X + strip.Width / 2 - row, Width = row * 2 };
        }
        else b.Hidden.Add("ExtendedStatsButton");
        positions["StateText"] = Above(state * row);
        positions["HealthText"] = positions["StatusPanel"] = Above(row);
        positions["NameText"] = positions["NamePanel"] = Above(row);
        cursor -= pad;
        var group = dock with { Y = cursor, Height = bottom - cursor };
        positions["ExtendedStatsPanel"] = positions["ExtendingPanel"] = positions["InformationPanel"] = group;
        b.Hidden.Add("NUE_HealthBackdrop");
        b.Hidden.Add("BountyPanel");
    }

    static void ArrangePanels(UiBindings b, UiRect dock, bool stats)
    {
        // Keep the summary immediately above the shared slot. All positions here are screen coordinates.
        float unit = dock.Height / 229f;
        var state = dock with { Y = dock.Y - 98 * unit, Height = 98 * unit };
        var status = state with { Y = state.Y - 38 * unit, Height = 38 * unit };
        var name = state with { Y = status.Y - 60 * unit, Height = 60 * unit };
        var bounty = state with { Y = name.Y - 32 * unit, Height = 32 * unit };
        var group = bounty with { Height = dock.Y + dock.Height - bounty.Y };
        var p = b.Positions;
        p["InformationPanel"] = group;
        p["ExtendingPanel"] = group;
        p["BountyPanel"] = bounty;
        p["NamePanel"] = name;
        p["StatusPanel"] = status;
        p["ExtendedStatsPanel"] = state;
        p["StateText"] = Inset(state, 4 * unit);
        b.Hidden.Add("ExtendedStatsButton");
        // This host normally shares the summary background. Tabs() places it inside the stats slot.
        b.Hidden.Add("ExtendedStatsText");
        if (stats)
        {
            b.Hidden.Add("NUE_HealthBackdrop");
            b.Hidden.Add("MedicalPanelDragAndMove");
            p["StatsPanel"] = dock;
            p["StatsText"] = Inset(dock with { Height = dock.Height / 2 }, 3 * unit);
        }
        else b.Hidden.Add("StatsPanel");
    }

    static UiRect Inset(UiRect r, float n) => new(r.X + n, r.Y + n, r.Width - 2 * n, r.Height - 2 * n);

    static void Tabs(List<UiControl> __result, UiRect? __state, UiBindings b)
    {
        if (__state is not { } dock)
        {
            if (b.Positions.ContainsKey("MedicalPanel_Front") && b.Positions.ContainsKey("InformationPanel"))
                for (int i = 0; i < __result.Count; i++)
                {
                    var control = __result[i];
                    if (control.Name == "InformationPanel")
                        __result[i] = control with { Skin = "NUE_PanelBackground" };
                    else if (control.Name is "NamePanel" or "StatusPanel" or "StatsPanel" or "ExtendedStatsPanel")
                        __result[i] = control with { Skin = "PanelEmpty", Pickable = false };
                }
            return;
        }
        float unit = dock.Height / 229f;
        if (showStats)
        {
            var lower = Inset(dock with { Y = dock.Y + dock.Height / 2, Height = dock.Height / 2 }, 3 * unit);
            __result.Add(new("ExtendedStatsText", "Widget", lower, "", "", "PanelEmpty", true, Empty, default,
                Pickable: false, Clip: lower));
        }

        string statsLabel = "STATS", healthLabel = "HEALTH";
#if NUE_UNIFIED
        statsLabel = Nue.SearchCompare.NueText.Get(statsLabel);
        healthLabel = Nue.SearchCompare.NueText.Get(healthLabel);
#endif
        float gap = 2 * unit, width = 30 * unit;
        int statsCells = statsLabel.Length + 2, healthCells = healthLabel.Length + 2;
        float cell = (dock.Height - gap) / (statsCells + healthCells);
        var stats = new UiRect(dock.X + dock.Width + gap, dock.Y, width, cell * statsCells);
        var health = stats with { Y = stats.Y + stats.Height + gap, Height = cell * healthCells };
        AddTab(__result, stats, StatsTab, statsLabel, "stats", "stats", cell, showStats);
        AddTab(__result, health, HealthTab, healthLabel, "health", "medic", cell, !showStats);
    }

    static void AddTab(List<UiControl> controls, UiRect rect, string name, string label, string action, string icon,
        float cell, bool selected)
    {
        var face = new Dictionary<string, string> { ["TextAlign"] = "Center", ["FontName"] = "Kenshi_PaintedTextFont_Medium",
            ["FontPixels"] = Math.Max(1, (int)(Math.Min(cell, rect.Width) * .85f)).ToString(CultureInfo.InvariantCulture) };
        var skin = selected ? new Dictionary<string, string> { ["State"] = "normal_checked" } : Empty;
        controls.Add(new(name, "Button", rect, "", "nue.hud-tabs:" + action, "NUE_ButtonSurface",
            true, skin, default, Pickable: true));
        float y = rect.Y + cell * .4f;
        float iconSize = Math.Min(cell, rect.Width);
        var symbol = new UiRect(rect.X + (rect.Width - iconSize) / 2, y, iconSize, iconSize);
        controls.Add(new(name + "Icon", "Widget", symbol, "", "", "NUE_Icon_" + icon, true, Empty, default, Pickable: false));
        for (int i = 0; i < label.Length; i++)
        {
            var line = rect with { Y = y + (i + 1) * cell, Height = cell };
            controls.Add(new(name + i, "TextBox", line, label[i].ToString(), "", "", true, face, default,
                TextColour: selected ? new(1, .83f, .55f, 1) : new(.89f, .85f, .76f, 1), Pickable: false));
        }
    }

    // Ask the native HUD to populate its extended rows, including equipment modifiers and tooltips.
    static void Expand(object __instance, List<UiControl> controls, out bool? __state)
    {
        __state = null;
        if (!controls.Any(c => c.Name == StatsTab) || !showStats) return;
        var player = Owner.GetValue(__instance);
        __state = (bool)Expanded.GetValue(player)!;
        Expanded.SetValue(player, true);
    }

    static void Restore(object __instance, bool? __state)
    {
        if (__state.HasValue) Expanded.SetValue(Owner.GetValue(__instance), __state.Value);
    }

    static void FitRows(List<UiControl> controls, string panel, object lines, ref float rowHeight,
        ref float? fontPixels, ref float? valuePixels)
    {
        if (panel is not ("StatsText" or "ExtendedStatsText" or "StateText") || lines is not ICollection { Count: > 0 } rows
            || !controls.Any(c => c.Name == StatsTab) || controls.FirstOrDefault(c => c.Name == panel) is not { } host)
            return;
        rowHeight = Math.Min(rowHeight, host.Bounds.Height / rows.Count);
        float cap = Math.Max(8, rowHeight * .85f);
        fontPixels = Math.Min(fontPixels ?? cap, cap);
        valuePixels = Math.Min(valuePixels ?? cap, cap);
    }
}
