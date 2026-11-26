using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Nue.Unified;

internal static class SkillsLayout
{
    private const string Athletics = "Skills3Panel-heading-4";
    private const int PieSize = 24, PiesPerRow = 10;
    private static readonly Assembly Game = typeof(IMod).Assembly;
    private static readonly Type Stats = Game.GetType("Meitou.Game.Ui.StatsWindow", true)!;
    private static readonly Type Player = Game.GetType("Meitou.Game.PlayerInterface", true)!;
    private static readonly FieldInfo StatsPlayer = AccessTools.GetDeclaredFields(Stats).Single(field => field.FieldType == Player);
    private static readonly MethodInfo Selected = AccessTools.Method(Player, "Selected");
    private static readonly MethodInfo UiScale = AccessTools.PropertyGetter(Player, "UiScale");
    private static readonly string[] HeadingNames =
    [
        "Skills1Panel-heading-0", "Skills2Panel-heading-0", "Skills2Panel-heading-4",
        "Skills3Panel-heading-0", Athletics, "Skills4Panel-heading-0", "Skills4Panel-heading-4"
    ];
    private static readonly string[] HeadingIcons = ["weapons", "combat", "ranged", "thievery", "athletics", "sciences", "trades"];
    private static string pieTexture = "";

    internal static void Install(ModContext context)
    {
        var atlas = Path.Combine(context.Folder, "gui", "gfx", "nue_skill_xp.png");
        if (File.Exists(atlas)) pieTexture = UiPainter.IconTexture(atlas);
        context.Harmony.Patch(AccessTools.Method(Stats, "AddStatsContent"),
            postfix: new HarmonyMethod(typeof(SkillsLayout), nameof(Arrange)));
    }

    private static void Arrange(object __instance, List<UiControl> controls)
    {
        if (controls.Find(c => c.Name == "NueSkillsFrame") is not {} frame) return;
        var player = StatsPlayer.GetValue(__instance)!;
        float scale = (float)UiScale.Invoke(player, null)!;
        var lead = ((IReadOnlyList<CharacterSnapshot>)Selected.Invoke(player, null)!).FirstOrDefault();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var columns = new List<int>[] { [], [], [], [] };
        var leftMods = new List<int>();
        var rightMods = new List<int>();
        for (int i = 0; i < controls.Count; i++)
        {
            var name = controls[i].Name;
            if (!name.StartsWith("Skills", StringComparison.Ordinal) || name.Length <= 12 ||
                name[6] is < '1' or > '4' || !name.AsSpan(7).StartsWith("Panel-", StringComparison.Ordinal)) continue;
            if (!indices.TryAdd(name, i)) continue;
            if (name.Contains("-value-", StringComparison.Ordinal)) continue;
            int column = name[6] - '1';
            if (name.Contains("-mod-label-", StringComparison.Ordinal))
                (column < 2 ? leftMods : rightMods).Add(i);
            else
                columns[column].Add(i);
        }

        if (!indices.TryGetValue(Athletics, out int athletics) || columns[0].Count == 0 || columns[3].Count == 0) return;
        int split = columns[2].IndexOf(athletics);
        if (split < 0) return;
        int[] left = [..columns[0], ..columns[1], ..columns[2].Skip(split), ..leftMods];
        int[] right = [..columns[2].Take(split), ..columns[3], ..rightMods];
        var area = frame.Bounds;
        float margin = 5 * scale;
        float row = Math.Min(20 * scale, (area.Height - 2 * margin) / Math.Max(left.Length, right.Length));
        if (row <= 0 || area.Width <= 0) return;
        var clip = area.Intersect(frame.Clip ?? area);
        PlaceColumn(left, area.X + area.Width * .066f, area.X + area.Width * .362f);
        PlaceColumn(right, area.X + area.Width * .560f, area.X + area.Width * .856f);

        void PlaceColumn(int[] rows, float x, float oldValueRight)
        {
            float y = area.Y + margin;
            float valueRight = oldValueRight - Math.Min(40 * scale, (oldValueRight - x) * .22f);
            float valueWidth = Math.Min(32 * scale, (valueRight - x) * .22f);
            float gap = 4 * scale;
            foreach (int index in rows)
            {
                var label = controls[index];
                int heading = Array.IndexOf(HeadingNames, label.Name);
                if (heading >= 0)
                {
                    controls[index] = Move(label, new(x, y, oldValueRight - x, row), clip, row * .89f);
                    int iconIndex = controls.FindIndex(c => c.Skin == "NUE_Stats_" + HeadingIcons[heading]);
                    if (iconIndex >= 0)
                    {
                        float side = Math.Min(row * 1.24f, area.Width * .045f);
                        var icon = controls[iconIndex];
                        controls[iconIndex] = icon with { Bounds = new(x - side - gap, y + (row - side) / 2, side, side), Clip = clip };
                    }
                }
                else if (indices.TryGetValue(label.Name.Replace("-label-", "-value-", StringComparison.Ordinal), out int number))
                {
                    float labelWidth = Math.Max(0, valueRight - valueWidth - gap - x);
                    controls[index] = Move(label, new(x, y, labelWidth, row), clip, row * .77f);
                    var value = controls[number];
                    controls[number] = Move(value, new(valueRight - valueWidth, y, valueWidth, row), clip, row * .77f);
                    AddPie(controls, lead, value, valueRight + gap, y, row, clip);
                }
                y += row;
            }
        }
    }

    private static UiControl Move(UiControl control, UiRect bounds, UiRect clip, float fontSize)
    {
        var style = new Dictionary<string, string>(control.Properties)
        {
            ["FontPixels"] = Math.Max(1, (int)MathF.Floor(fontSize)).ToString(CultureInfo.InvariantCulture)
        };
        style.Remove("WordWrap");
        return control with { Bounds = bounds, Clip = clip, Properties = style };
    }

    private static void AddPie(List<UiControl> controls, CharacterSnapshot? lead, UiControl value,
        float x, float y, float row, UiRect clip)
    {
        if (lead is null || pieTexture.Length == 0 || !value.Action.StartsWith("stat:", StringComparison.Ordinal)) return;
        var id = value.Action.AsSpan(5);
        bool mod = id.StartsWith("mod:", StringComparison.Ordinal);
        if (!int.TryParse(mod ? id[4..] : id, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return;
        float level = mod ? lead.ModSkills.ElementAtOrDefault(index) : lead.AllSkills.ElementAtOrDefault(index);
        if (!float.IsFinite(level)) return;
        int progress = (int)MathF.Floor((level - MathF.Floor(level)) * 100);
        if (progress == 0) return;
        float side = row * .76f;
        var style = new Dictionary<string, string>
        {
            ["ImageTexture"] = pieTexture,
            ["ImageRect"] = FormattableString.Invariant($"{(progress % PiesPerRow) * PieSize} {(progress / PiesPerRow) * PieSize} {PieSize} {PieSize}")
        };
        controls.Add(new(value.Name + "-xp", "ImageBox", new(x, y + (row - side) / 2, side, side),
            "", "", "", true, style, default, Pickable: false, Clip: clip));
    }
}
