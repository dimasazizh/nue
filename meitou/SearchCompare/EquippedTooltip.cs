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
    static readonly ConditionalWeakTable<IReadOnlyList<TooltipLine>,GearComparisonTarget> EquippedByLines = new();

    const string Number = @"[+−-]?\d+(?:\.\d+)?";
    static readonly Regex Stat = new(
        $@"^\s*(?<first>{Number})(?:\s*(?<separator>[-/–])\s*(?<second>{Number}))?\s*(?<unit>%|[a-z]+(?:/[a-z]+)?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    sealed record TooltipPair(int Start, IReadOnlyList<TooltipLine> Equipped, string?[] EquippedColours,
        int[] HoveredDirections, int[] EquippedDirections, string? HoveredName, string? EquippedName,
        TooltipTone HoveredGrade, TooltipTone EquippedGrade);
    enum RowKind { None, Stat, Skill, Weather }

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
        var tip = ToTip.Invoke(null, [equipped.Item, null, null]);
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
        for (int i = 0; i < hoveredRows.Length; i++)
        {
            var row = lines[i];
            var category = ComparisonKind(row);
            if (hoveredColours[i] is not null || category == RowKind.None) continue;
            int j = Array.FindIndex(equippedRows, candidate =>
                candidate.Key == row.Key && ComparisonKind(candidate) == category);
            if (j >= 0)
            {
                int order = CompareStat(row.Value, equippedLines[j].Value);
                Mark(hoveredRows, hoveredColours, hoveredDirections, i, order);
                Mark(equippedRows, equippedColours, equippedDirections, j, -order);
            }
            else if (category is RowKind.Skill or RowKind.Weather)
                Mark(hoveredRows, hoveredColours, hoveredDirections, i, AgainstNeutral(row.Value));
        }
        for (int j = 0; j < equippedLines.Count; j++)
        {
            var row = equippedLines[j];
            var category = ComparisonKind(row);
            if (category is (RowKind.Skill or RowKind.Weather) &&
                !lines.Any(candidate => candidate.Key == row.Key && ComparisonKind(candidate) == category))
                Mark(equippedRows, equippedColours, equippedDirections, j, AgainstNeutral(row.Value));
        }
        valueColours = hoveredColours;
        lines = hoveredRows;
        __state = new(controls.Count, equippedRows, equippedColours, hoveredDirections, equippedDirections,
            equipped.HoveredName, equipped.EquippedName, GradeOf(hoveredRows), GradeOf(equippedRows));
    }

    static TooltipTone GradeOf(IEnumerable<TooltipLine> rows) =>
        rows.Select(row => row.KeyTone).FirstOrDefault(tone => GradeBorder(tone).HasValue);

    // Match ItemGrade0–ItemGrade4 in the NUE palette.
    static Vector4? GradeBorder(TooltipTone grade) => grade switch
    {
        TooltipTone.Grade0 => Rgb(0x787878),
        TooltipTone.Grade1 => Rgb(0xD9CCBF),
        TooltipTone.Grade2 => Rgb(0x4099B3),
        TooltipTone.Grade3 => Rgb(0x66FC80),
        TooltipTone.Grade4 => Rgb(0xFCEB0D),
        _ => null
    };

    static Vector4 Rgb(int hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1);

    static RowKind ComparisonKind(TooltipLine line)
    {
        if (line.Key.Length == 0 || line.Value.Length == 0 || line.ValueColour is not null) return RowKind.None;
        // Native armour details colour the skill and weather rows before comparison.
        if (line.Key.StartsWith('-'))
        {
            if (line.KeyTone == TooltipTone.Title && line.ValueTone is TooltipTone.Good or TooltipTone.Bad)
                return RowKind.Skill;
            if (line.KeyTone == TooltipTone.Good && line.ValueTone == TooltipTone.Good && line.Value.EndsWith('%'))
                return RowKind.Weather;
        }
        return line.ValueTone is TooltipTone.Default or TooltipTone.Main or TooltipTone.Ink ? RowKind.Stat : RowKind.None;
    }

    static void Mark(TooltipLine[] rows, string?[] colours, int[] directions, int index, int order)
    {
        if (order == 0 || directions[index] != 0) return;
        var row = rows[index];
        bool better = row.Key == "-Weight" ? order < 0 : order > 0;
        colours[index] = better ? Green : Red;
        rows[index] = row with { Value = ArrowSpace + row.Value };
        directions[index] = order;
    }

    static int AgainstNeutral(string value)
    {
        var match = Stat.Match(value);
        if (!match.Success || match.Groups["separator"].Success) return 0;
        string unit = match.Groups["unit"].Value;
        return CompareStat(value, (unit.Equals("x", StringComparison.OrdinalIgnoreCase) ? "1" : "0") + unit);
    }

    static int CompareStat(string left, string right)
    {
        var a = Stat.Match(left);
        var b = Stat.Match(right);
        if (!a.Success || !b.Success ||
            !a.Groups["unit"].Value.Equals(b.Groups["unit"].Value, StringComparison.OrdinalIgnoreCase)) return 0;

        static double Parse(Group g) => double.Parse(g.Value.Replace('−', '-'), CultureInfo.InvariantCulture);
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
        const float gap = 4;
        float labelHeight = Math.Max(14, 22 * scale);
        float border = Math.Max(1, 2 * scale);
        var leftPlate = controls[pair.Start];
        var rightPlate = equipped[0];
        var left = leftPlate.Bounds;
        var right = rightPlate.Bounds;
        // The plate image contains a transparent margin. Space the visible frame edges.
        float leftInset = leftPlate.Type == "ImageBox" ? 16 * scale : 0;
        float rightInset = rightPlate.Type == "ImageBox" ? 16 * scale : 0;
        var hoveredBorder = GradeBorder(pair.HoveredGrade);
        float leftBorder = hoveredBorder.HasValue ? border : 0;
        float rightOffset = left.Width - leftInset + leftBorder + gap - rightInset + border;
        float rightTop = leftInset - leftBorder - rightInset + border;
        float pairWidth = rightOffset + right.Width - rightInset + border;
        float pairHeight = Math.Max(left.Height - leftInset + leftBorder, rightTop + right.Height - rightInset + border);
        float x = Math.Clamp(left.X, 0, Math.Max(0, width - pairWidth));
        float y = Math.Clamp(left.Y, labelHeight + border,
            Math.Max(labelHeight + border, height - pairHeight));
        int end = controls.Count;
        for (int i = pair.Start; i < end; i++)
            controls[i] = MoveTooltipControl(controls[i], x - left.X, y - left.Y);

        float rightX = x + rightOffset;
        float rightY = y + rightTop;
        foreach (var control in equipped)
            controls.Add(MoveTooltipControl(control, rightX - right.X, rightY - right.Y));

        var frame = new UiRect(rightX + rightInset, rightY + rightInset,
            right.Width - 2 * rightInset, right.Height - 2 * rightInset);
        var hoveredFrame = new UiRect(x + leftInset, y + leftInset, left.Width - 2 * leftInset, left.Height - 2 * leftInset);
        var style = new Dictionary<string,string>();
        if (hoveredBorder is {} colour)
            Frame("tooltip", hoveredFrame, colour);
        Frame(EquippedPrefix, frame, GradeBorder(pair.EquippedGrade) ?? Rgb(0xE8BD72));
        if (pair.HoveredName is {} hoveredName)
            Heading("tooltip-heading", hoveredFrame.X, hoveredFrame.Y - leftBorder, hoveredFrame.Width, hoveredName);
        Heading(EquippedPrefix + "-heading", frame.X, frame.Y - border, frame.Width,
            pair.EquippedName ?? NueText.Get("EQUIPPED"));

        void Frame(string prefix, UiRect rect, Vector4 tint)
        {
            void Edge(string name, UiRect bounds) => controls.Add(new(prefix + "-grade-" + name, "Widget", bounds,
                "", "", "", true, style, tint, Pickable: false));
            Edge("top", new(rect.X - border, rect.Y - border, rect.Width + 2 * border, border));
            Edge("bottom", new(rect.X - border, rect.Y + rect.Height, rect.Width + 2 * border, border));
            Edge("left", new(rect.X - border, rect.Y, border, rect.Height));
            Edge("right", new(rect.X + rect.Width, rect.Y, border, rect.Height));
        }

        void Heading(string name, float leftEdge, float top, float frameWidth, string caption) =>
            controls.Add(new(name, "TextBox", new(leftEdge, top - labelHeight, frameWidth, labelHeight), caption, "", "", true,
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
