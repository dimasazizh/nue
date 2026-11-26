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
    const string Higher = "0.42 0.76 0.32";
    const string Lower = "0.90 0.30 0.24";
    static readonly ConditionalWeakTable<IReadOnlyList<TooltipLine>,InventoryItemView> EquippedByLines = new();

    const string Number = @"[+-]?\d+(?:\.\d+)?";
    static readonly Regex Stat = new(
        $@"^\s*(?<first>{Number})(?:\s*(?<separator>[-/–])\s*(?<second>{Number}))?\s*(?<unit>%|[a-z]+(?:/[a-z]+)?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    sealed record TooltipPair(int Start, IReadOnlyList<TooltipLine> Equipped, string?[] EquippedColours);

    static void RememberComparison(object t, IReadOnlyList<TooltipLine> __result)
    {
        if (Comparisons.TryGetValue(t, out var equipped))
            EquippedByLines.AddOrUpdate(__result, equipped);
    }

    static void CompareTooltipValues(object __instance, List<UiControl> controls, IReadOnlyList<TooltipLine> lines,
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
        for (int i = 0; i < lines.Count; i++)
        {
            if (hoveredColours[i] is not null || !UncolouredStat(lines[i])) continue;
            for (int j = 0; j < equippedLines.Count; j++)
            {
                if (lines[i].Key != equippedLines[j].Key || !UncolouredStat(equippedLines[j])) continue;
                int order = CompareStat(lines[i].Value, equippedLines[j].Value);
                if (order != 0)
                {
                    hoveredColours[i] = order > 0 ? Higher : Lower;
                    equippedColours[j] = order > 0 ? Lower : Higher;
                }
                break;
            }
        }
        valueColours = hoveredColours;
        __state = new(controls.Count, equippedLines, equippedColours);
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

    static UiControl MoveTooltipControl(UiControl control, float dx, float dy)
    {
        UiRect Move(UiRect bounds) => bounds with { X = bounds.X + dx, Y = bounds.Y + dy };
        return control with { Bounds = Move(control.Bounds), Clip = control.Clip is {} clip ? Move(clip) : null };
    }
}
