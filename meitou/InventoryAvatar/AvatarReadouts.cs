using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Simulation;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private const string ToggleVitals = "nue-avatar-vitals";
    private static readonly Type HudPanels = GameType("Ui.HudPanels");
    private static readonly System.Reflection.MethodInfo HudStatsLines = AccessTools.Method(HudPanels, "StatsLines"),
        HudExtendedLines = AccessTools.Method(HudPanels, "ExtendedLines"),
        HudHealthWord = AccessTools.Method(HudPanels, "HealthWord", [typeof(CharacterSnapshot)]),
        AddHudLines = AccessTools.Method(Player, "AddLines"),
        StatLabelColour = AccessTools.PropertyGetter(Player, "StatLabelColour"),
        SpeedUnit = AccessTools.PropertyGetter(Player, "SpeedUnit");
    private static readonly System.Reflection.FieldInfo WorldSessionField = AccessTools.Field(Player, "Session");
    private static readonly Dictionary<string, string> ReadoutFace = new()
        { ["TextAlign"] = "Left VCenter", ["FontName"] = "Kenshi_StandardFont_Medium" };
    private static readonly Dictionary<string, string> ReadoutValueFace = new(ReadoutFace)
        { ["TextAlign"] = "Right VCenter" };

    private static bool AvatarShowsHealth(PreviewState view)
    {
        bool mainShowsStats = false;
#if NUE_UNIFIED
        mainShowsStats = Nue.HudTabs.HudTabsMod.ShowingStats;
#endif
        if (view.mainHudShowsStats != mainShowsStats)
        {
            view.mainHudShowsStats = mainShowsStats;
            view.showHealthOverride = null;
        }
        return view.showHealthOverride ?? mainShowsStats;
    }

    private static void AddAvatarReadouts(List<UiControl> controls, object player, CharacterSnapshot owner)
    {
        if (controls.Find(c => c.Name == "NueAvatarMoney") is {} money &&
            WorldSessionField.GetValue(player) is WorldSession session)
        {
            ReadoutText(controls, "NueMoneyLabel", money.Bounds, "Money", ReadoutFace);
            ReadoutText(controls, "NueMoneyValue", money.Bounds,
                "c." + session.World.Player.Money.ToString("N0", CultureInfo.InvariantCulture), ReadoutValueFace);
        }

        if (controls.Find(c => c.Name == "NueAvatarVitals") is not {} panel) return;
        var area = panel.Bounds;
        float headingHeight = area.Height * .16f;
        bool health = AvatarShowsHealth(active);
        var heading = area with { Height = headingHeight };
        var title = heading with { Width = heading.Width * .34f };
        controls.Add(new("NueVitalsHeading", "Button", title, health ? "HEALTH" : "STATS",
            ToggleVitals, "", true, ReadoutFace, default, TextColour: new(1, .83f, .55f, 1),
            Pickable: true, Clip: title));
        if (owner.Hud is not {} hud) return;
        if (owner.Body.HasValue && HudHealthWord.Invoke(null, [owner]) is ValueTuple<string, string, string> statusWord)
            ReadoutText(controls, "NueVitalsStatus",
                heading with { X = heading.X + title.Width, Width = heading.Width - title.Width },
                statusWord.Item1, ReadoutValueFace);
        var body = area with { Y = area.Y + headingHeight, Height = area.Height - headingHeight };
        float gap = area.Width * .04f;
        var left = body with { Width = (area.Width - gap) / 2 };
        var right = left with { X = left.X + left.Width + gap };
        if (!health)
        {
            var stats = (Array)HudStatsLines.Invoke(null, [owner, StatLabelColour.Invoke(player, null)])!;
            var extended = (Array)HudExtendedLines.Invoke(null, [owner, SpeedUnit.Invoke(player, null)])!;
            float row = Math.Min(body.Height / Math.Max(1, Math.Max(stats.Length, extended.Length)), headingHeight);
            AddStatsColumn("NueAvatarStats", left, stats, row);
            AddStatsColumn("NueAvatarExtendedStats", right, extended, row);
            return;
        }

        var bars = new List<(string Name, float Value, float Maximum)>
            { (hud.Robot ? "Oil" : "Blood", hud.Blood, hud.BloodCapacity) };
        bars.AddRange(owner.HealthParts.Select(p => (p.Name, p.Flesh - p.Stun, p.Maximum)));
        if (!hud.Robot && owner.Body is {} status) bars.Add(("Hunger", status.Hunger * 100, 300));
        int rows = (bars.Count + 1) / 2;
        float rowHeight = Math.Min(body.Height / rows, headingHeight);
        for (int i = 0; i < bars.Count; i++)
        {
            var (name, value, maximum) = bars[i];
            var column = i < rows ? left : right;
            var row = column with { Y = column.Y + i % rows * rowHeight, Height = rowHeight - 2 };
            string key = "NueAvatarHealth" + i;
            controls.Add(new(key, "Widget", row, "", "", "", true, ReadoutFace,
                new(.13f, .10f, .08f, .85f), Pickable: false, Clip: row));
            float fill = maximum > 0 ? Math.Clamp(value / maximum, 0, 1) : 0;
            if (fill > 0)
                controls.Add(new(key + "Fill", "Widget", row with { Width = row.Width * fill },
                    "", "", "", true, ReadoutFace, new(.33f, .43f, .23f, .85f), Pickable: false, Clip: row));
            float inset = Math.Min(row.Width * .08f, rowHeight * .24f);
            var text = row with { X = row.X + inset, Width = row.Width - 2 * inset };
            ReadoutText(controls, key + "Name", text, name, ReadoutFace);
            ReadoutText(controls, key + "Value", text, value.ToString("0", CultureInfo.InvariantCulture), ReadoutValueFace);
        }

        void AddStatsColumn(string name, UiRect bounds, Array lines, float row)
        {
            controls.Add(new(name, "Widget", bounds, "", "", "PanelEmpty", true, ReadoutFace,
                default, Pickable: false, Clip: bounds));
            float font = Math.Max(8, row * .7f);
            AddHudLines.Invoke(player, [controls, name, lines, row, font, "Kenshi_StandardFont_Medium",
                "Kenshi_StandardFont_Medium", font, 0f]);
        }
    }

    private static void ReadoutText(List<UiControl> controls, string name, UiRect bounds, string text,
        Dictionary<string, string> face, Vector4? colour = null) =>
        controls.Add(new(name, "TextBox", bounds, text, "", "", true, face, default,
            TextColour: colour ?? new(.89f, .85f, .76f, 1), Pickable: false, Clip: bounds));
}
