using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Nue.SearchCompare;

public sealed partial class SearchMod
{
    const string EquippedPrefix = "nue-equipped-tooltip";
    const string Yellow = "0.909804 0.741176 0.447059";
    const string Green = "0.42 0.76 0.32";
    const string Red = "0.90 0.30 0.24";
    // A measurable prefix reserves space in the native tooltip layout. The decoration replaces it with geometry.
    const string ArrowSpace = "00 ";
    static readonly ConditionalWeakTable<IReadOnlyList<TooltipLine>,InventoryItemView> EquippedByLines = new();

    const string Number = @"[+-]?\d+(?:\.\d+)?";
    static readonly Regex Stat = new(
        $@"^\s*(?<first>{Number})(?:\s*(?<separator>[-/–])\s*(?<second>{Number}))?\s*(?<unit>%|[a-z]+(?:/[a-z]+)?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    sealed record TooltipPair(int Start, IReadOnlyList<TooltipLine> Equipped, string?[] EquippedColours,
        int[] HoveredDirections, int[] EquippedDirections);

    static void RememberComparison(object t, IReadOnlyList<TooltipLine> __result)
    {
        if (Comparisons.TryGetValue(t, out var equipped))
            EquippedByLines.AddOrUpdate(__result, equipped);
    }

    static void CompareTooltipValues(object __instance, List<UiControl> controls, ref IReadOnlyList<TooltipLine> lines,
        string prefix, ref IReadOnlyList<string?>? valueColours, out TooltipPair? __state)
    {
        __state = null;
        if (prefix != "tooltip" || !EquippedByLines.TryGetValue(lines, out var equipped)) return;
        var tip = ToTip.Invoke(null, [equipped, null, null]);
        var last = LastModTooltip.GetValue(__instance);
        IReadOnlyList<TooltipLine> equippedLines;
        try
        {
            equippedLines = (IReadOnlyList<TooltipLine>)ItemLines.Invoke(__instance, [tip])!;
        }
        finally
        {
            LastModTooltip.SetValue(__instance, last);
        }

        var hoveredColours = new string?[lines.Count];
        if (valueColours is not null)
            for (int i = 0; i < Math.Min(valueColours.Count, hoveredColours.Length); i++)
                hoveredColours[i] = valueColours[i];
        var equippedColours = new string?[equippedLines.Count];
        var hoveredRows = lines.ToArray();
        var equippedRows = equippedLines.ToArray();
        var hoveredDirections = new int[hoveredRows.Length];
        var equippedDirections = new int[equippedRows.Length];
        for (int i = 0; i < lines.Count; i++)
        {
            if (hoveredColours[i] is not null || !UncolouredStat(lines[i])) continue;
            for (int j = 0; j < equippedLines.Count; j++)
            {
                if (lines[i].Key != equippedLines[j].Key || !UncolouredStat(equippedLines[j])) continue;
                int order = CompareStat(lines[i].Value, equippedLines[j].Value);
                if (order != 0)
                {
                    bool higher = order > 0;
                    // The arrows show numeric direction. Weight rewards the lower value.
                    bool hoveredBetter = lines[i].Key == "-Weight" ? !higher : higher;
                    hoveredColours[i] = hoveredBetter ? Green : Red;
                    equippedColours[j] = hoveredBetter ? Red : Green;
                    hoveredRows[i] = lines[i] with { Value = ArrowSpace + lines[i].Value };
                    equippedRows[j] = equippedLines[j] with { Value = ArrowSpace + equippedLines[j].Value };
                    hoveredDirections[i] = order;
                    equippedDirections[j] = -order;
                }
                break;
            }
        }
        valueColours = hoveredColours;
        lines = hoveredRows;
        __state = new(controls.Count, equippedRows, equippedColours, hoveredDirections, equippedDirections);
    }

    static bool UncolouredStat(TooltipLine line) =>
        line.Key.Length > 0 && line.Value.Length > 0 && line.ValueColour is null &&
        line.ValueTone is TooltipTone.Default or TooltipTone.Main or TooltipTone.Ink;

    static int CompareStat(string left, string right)
    {
        var a = Stat.Match(left);
        var b = Stat.Match(right);
        if (!a.Success || !b.Success ||
            !a.Groups["unit"].Value.Equals(b.Groups["unit"].Value, StringComparison.OrdinalIgnoreCase)) return 0;

        static double Parse(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
        double first = Parse(a.Groups["first"]), otherFirst = Parse(b.Groups["first"]);
        int order = first.CompareTo(otherFirst);
        string separator = a.Groups["separator"].Value.Replace('–', '-');
        if (separator != b.Groups["separator"].Value.Replace('–', '-')) return 0;
        if (separator.Length == 0) return order;

        double second = Parse(a.Groups["second"]), otherSecond = Parse(b.Groups["second"]);
        if (separator == "/") return second > 0 && otherSecond > 0
            ? (first / second).CompareTo(otherFirst / otherSecond) : 0;
        int end = second.CompareTo(otherSecond);
        return order == 0 ? end : end == 0 || order == end ? order : 0;
    }

    static void PlaceEquippedTooltip(object __instance, List<UiControl> controls, UiPainter painter,
        TooltipKind kind, UiRect? given, Vector2? point, TooltipPair? __state)
    {
        if (__state is not {} pair || pair.Start >= controls.Count) return;
        var equipped = new List<UiControl>();
        PlaceTooltip.Invoke(__instance, [equipped, painter, pair.Equipped, kind, given, EquippedPrefix, point, pair.EquippedColours]);
        if (equipped.Count == 0) return;

        var player = TooltipPlayer.GetValue(__instance)!;
        int width = (int)AccessTools.Field(player.GetType(), "Width").GetValue(player)!;
        int height = (int)AccessTools.Field(player.GetType(), "Height").GetValue(player)!;
        float scale = (float)Property(player, "UiScale");
        float minimum = (bool)Property(player, "TitleOpen") ? 0 : (float)Property(player, "StandardPixels");
        DrawArrows(controls, pair.Start, "tooltip", pair.HoveredDirections, painter, height, minimum, scale);
        DrawArrows(equipped, 0, EquippedPrefix, pair.EquippedDirections, painter, height, minimum, scale);
        float gap = Math.Max(6, 9 * scale), labelHeight = Math.Max(14, 22 * scale);
        float border = Math.Max(1, 2 * scale);
        var left = controls[pair.Start].Bounds;
        var right = equipped[0].Bounds;
        float x = Math.Clamp(left.X, 0, Math.Max(0, width - left.Width - gap - right.Width - border));
        float y = Math.Clamp(left.Y, labelHeight + border,
            Math.Max(labelHeight + border, height - Math.Max(left.Height, right.Height) - border));
        int end = controls.Count;
        for (int i = pair.Start; i < end; i++)
            controls[i] = MoveTooltipControl(controls[i], x - left.X, y - left.Y);

        float rightX = x + left.Width + gap;
        foreach (var control in equipped)
            controls.Add(MoveTooltipControl(control, rightX - right.X, y - right.Y));

        // The Meitou plate includes a transparent margin around its visible frame.
        float margin = equipped[0].Type == "ImageBox" ? 16 * scale : 0;
        var frame = new UiRect(rightX + margin, y + margin, right.Width - 2 * margin, right.Height - 2 * margin);
        var gold = new Vector4(.909804f, .741176f, .447059f, 1);
        var style = new Dictionary<string,string>();
        void Edge(string name, UiRect bounds) => controls.Add(new(EquippedPrefix + "-" + name, "Widget", bounds,
            "", "", "", true, style, gold, Pickable:false));
        Edge("top", new(frame.X - border, frame.Y - border, frame.Width + 2 * border, border));
        Edge("bottom", new(frame.X - border, frame.Y + frame.Height, frame.Width + 2 * border, border));
        Edge("left", new(frame.X - border, frame.Y, border, frame.Height));
        Edge("right", new(frame.X + frame.Width, frame.Y, border, frame.Height));
        controls.Add(new(EquippedPrefix + "-heading", "TextBox",
            new(frame.X, frame.Y - border - labelHeight, frame.Width, labelHeight), "EQUIPPED", "", "", true,
            new Dictionary<string,string> {
                ["FontName"] = "Kenshi_StandardFont_Small", ["TextAlign"] = "Center VCenter", ["TextColour"] = Yellow
            }, default, Pickable:false));
    }

    static void DrawArrows(List<UiControl> controls, int start, string prefix, int[] directions,
        UiPainter painter, int screenHeight, float minimum, float scale)
    {
        string valuePrefix = prefix + "-value-";
        int end = controls.Count;
        for (int i = start; i < end; i++)
        {
            var control = controls[i];
            if (!control.Name.StartsWith(valuePrefix, StringComparison.Ordinal) ||
                !int.TryParse(control.Name.AsSpan(valuePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int row) ||
                (uint)row >= (uint)directions.Length || directions[row] == 0 ||
                !control.Caption.StartsWith(ArrowSpace, StringComparison.Ordinal)) continue;

            string value = control.Caption[ArrowSpace.Length..];
            controls[i] = control with { Caption = value };
            var font = control.Properties["FontName"];
            var size = painter.MeasureText(font, value, screenHeight, 0, minimum);
            float unit = Math.Max(1, MathF.Round(scale));
            float x = control.Bounds.X + control.Bounds.Width - size.Width - Math.Max(2, 3 * scale) - 3.5f * unit;
            float y = control.Bounds.Y + (Math.Min(control.Bounds.Height, size.Height) - 7 * unit) / 2;
            string[] channels = control.Properties["TextColour"].Split(' ');
            var colour = new Vector4(float.Parse(channels[0], CultureInfo.InvariantCulture),
                float.Parse(channels[1], CultureInfo.InvariantCulture), float.Parse(channels[2], CultureInfo.InvariantCulture), 1);
            bool up = directions[row] > 0;
            for (int strip = 0; strip < 4; strip++)
                AddStrip(strip, up ? strip : 6 - strip, 1 + 2 * strip, 1);
            AddStrip(4, up ? 4 : 0, 3, 3);

            void AddStrip(int strip, int top, int width, int height)
            {
                var bounds = new UiRect(x - width * unit / 2, y + top * unit, width * unit, height * unit);
                controls.Add(new(control.Name + "-arrow-" + strip, "Widget", bounds, "", "", "", true,
                    control.Properties, colour, Pickable: false, Clip: control.Clip));
            }
        }
    }

    static UiControl MoveTooltipControl(UiControl control, float dx, float dy)
    {
        UiRect Move(UiRect bounds) => bounds with { X = bounds.X + dx, Y = bounds.Y + dy };
        return control with { Bounds = Move(control.Bounds), Clip = control.Clip is {} clip ? Move(clip) : null };
    }
}
