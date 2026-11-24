using System.Collections;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Nue.HudTabs;

public sealed class HudTabsMod : IMod, IUiExtension
{
    public string Id => "nue.hud-tabs";
    static bool showStats;
    const string StatsTab = "NueHudStatsTab";
    const string HealthTab = "NueHudHealthTab";

    static readonly Type Player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
    static readonly Type Panels = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.HudPanels", true)!;
    static readonly FieldInfo Owner = Panels.GetFields(AccessTools.all).Single(f => f.FieldType == Player);
    static readonly FieldInfo Expanded = AccessTools.Field(Player, "InfoExpanded");
    static readonly PropertyInfo BuildingSelection = AccessTools.Property(Player, "BuildingSelection");
    static readonly MethodInfo BuildingFocus = AccessTools.Method(BuildingSelection.PropertyType, "BuildingFocus");
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
            || b.Hidden.Contains("MedicalPanel") || BuildingFocus.Invoke(BuildingSelection.GetValue(__instance), null) != null)
            return;

        var screen = new UiRect(0, 0, (int)Width.GetValue(__instance)!, (int)Height.GetValue(__instance)!);
        var dock = backdrop.Position.RelativeTo(screen, true);
        __state = dock;
        ArrangePanels(b, dock, showStats);
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

    static void Tabs(List<UiControl> __result, UiRect? __state)
    {
        if (__state is not { } dock) return;
        float unit = dock.Height / 229f;
        if (showStats)
        {
            var lower = Inset(dock with { Y = dock.Y + dock.Height / 2, Height = dock.Height / 2 }, 3 * unit);
            __result.Add(new("ExtendedStatsText", "Widget", lower, "", "", "PanelEmpty", true, Empty, default,
                Pickable: false, Clip: lower));
        }

        float gap = 2 * unit, width = 30 * unit;
        float cell = (dock.Height - gap) / 15; // Seven cells for STATS and eight for HEALTH, including padding.
        var stats = new UiRect(dock.X + dock.Width + gap, dock.Y, width, cell * 7);
        var health = stats with { Y = stats.Y + stats.Height + gap, Height = cell * 8 };
        AddTab(__result, stats, StatsTab, "STATS", "stats", cell, showStats);
        AddTab(__result, health, HealthTab, "HEALTH", "medic", cell, !showStats);
    }

    static void AddTab(List<UiControl> controls, UiRect rect, string name, string label, string icon, float cell, bool selected)
    {
        var face = new Dictionary<string, string> { ["TextAlign"] = "Center", ["FontName"] = "Kenshi_PaintedTextFont_Medium",
            ["FontPixels"] = Math.Max(8, (int)(cell * .85f)).ToString(CultureInfo.InvariantCulture) };
        var skin = selected ? new Dictionary<string, string> { ["State"] = "normal_checked" } : Empty;
        controls.Add(new(name, "Button", rect, "", "nue.hud-tabs:" + label.ToLowerInvariant(), "NUE_ButtonSurface",
            true, skin, default, Pickable: true));
        float y = rect.Y + cell * .4f;
        var symbol = new UiRect(rect.X + (rect.Width - cell) / 2, y, cell, cell);
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
