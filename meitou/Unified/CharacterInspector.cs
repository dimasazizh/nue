using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;
using Nue.SearchCompare;

namespace Nue.Unified;

internal static partial class CharacterInspector
{
    internal const string ActionPrefix = "nue-inspector:";
    private const string Gold = "0.909804 0.741176 0.447059";
    private const int PartsPerPage = 8, GearPerPage = 2;
    private static readonly Type Player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
    internal static readonly System.Reflection.FieldInfo Mouse = AccessTools.Field(Player, "Mouse");
    private static readonly System.Reflection.MethodInfo ItemCells = AccessTools.PropertyGetter(Player, "ItemCells");
    private static readonly System.Reflection.MethodInfo AddItemCell = AccessTools.Method(ItemCells.ReturnType, "AddItemCell");
    private static GameDatabase? data;
    private static string silhouette = "";

    internal static bool Enabled => data is not null;

    internal static void Enable(ModContext context)
    {
        data = context.Data;
        silhouette = UiPainter.IconTexture(Path.Combine(context.Folder, "gui", "gfx", "nue_body.png"));
    }

    internal sealed class View
    {
        private CharacterId character = CharacterId.None;
        internal string? Part;
        internal int Page;
        internal bool ShowRadar;
        internal readonly int[] RadarColours = [0, 1, 2];

        internal void Use(CharacterId id)
        {
            if (character == id) return;
            character = id;
            Part = null;
            Page = 0;
            ShowRadar = false;
        }

        internal void Act(string action)
        {
            switch (action)
            {
                case "figure-mode": ShowRadar = !ShowRadar; break;
                case "back": Part = null; Page = 0; break;
                case "previous": Page = Math.Max(0, Page - 1); break;
                case "next": Page++; break;
                default:
                    if (action.StartsWith("chart-colour:", StringComparison.Ordinal) &&
                        int.TryParse(action.AsSpan("chart-colour:".Length), out int metric) &&
                        (uint)metric < RadarColours.Length)
                    {
                        RadarColours[metric] = (RadarColours[metric] + 1) % ArmourRadarImage.Palette.Length;
                        break;
                    }
                    if (!action.StartsWith("part:", StringComparison.Ordinal)) break;
                    Part = action[5..];
                    Page = 0;
                    break;
            }
        }
    }

    private static readonly Dictionary<string, UiRect> BodyAreas = new()
    {
        ["head"] = new(37, 0, 22, 26),
        ["chest"] = new(34, 26, 28, 24),
        ["stomach"] = new(34, 50, 28, 19),
        ["right arm"] = new(15, 26, 19, 55),
        ["left arm"] = new(62, 26, 19, 55),
        ["right leg"] = new(28, 69, 20, 59),
        ["left leg"] = new(48, 69, 20, 59)
    };

    private sealed record Part(string Id, string Name, string Shape);
    private sealed record Gear(InventoryItemView Item, ArmourPiece Armour);
    private readonly record struct Protection(float Coverage, float Cut, float Blunt, float Harpoon);

    internal static void Draw(List<UiControl> controls, object player, CharacterSnapshot character, UiRect area,
        View view, string? hoveredAction)
    {
        if (data is not {} database || character.ItemGrid is not {} grid || area.Width <= 0 || area.Height <= 0) return;
        IEnumerable<(string Id, string Name)> anatomy = character.HealthParts.Select(part => (part.Id, part.Name));
        if (character.HealthParts.Count == 0 && character.Appearance?.Race is {} race)
            anatomy = RaceData.From(race, database).Anatomy.Select(entry => (entry.Part.StringId, entry.Part.Name));
        var parts = anatomy.DistinctBy(part => part.Id).Select(part => new Part(part.Id, part.Name, Shape(database, part.Id))).ToArray();

        var gear = new List<Gear>();
        foreach (var item in grid.Items)
        {
            if (!InventoryCommands.IsEquipmentSection(item.Section) ||
                database.Find(item.Record) is not { Type: FcsRecordType.Armour } record) continue;

            var piece = ArmourData.From(record);
            gear.Add(new(item, ArmourPiece.Create(piece, ArmourPiece.QualityOf(piece, item.Level), ArmourPiece.Graded(piece, item.Level))));
        }

        var selected = parts.FirstOrDefault(part => part.Id == view.Part);
        var estimates = parts.ToDictionary(part => part.Id, part => Estimate(part.Id, gear), StringComparer.Ordinal);
        float pad = Math.Clamp(area.Width * .025f, 2, 8);
        float x = area.X + pad, width = area.Width - 2 * pad;
        if (width <= 0 || area.Height <= 2 * pad) return;
        float figureSpace = (area.Height - 2 * pad) * .43f;
        float switchHeight = Math.Min(figureSpace * .13f, width * .075f);
        string switchFont = Math.Max(1, (int)(switchHeight * .68f)).ToString(CultureInfo.InvariantCulture);
        var switchBounds = new UiRect(x + width * .27f, area.Y + pad, width * .46f, switchHeight);
        AddButton(controls, switchBounds, view.ShowRadar ? "BODY FIGURE" : "ARMOUR CHART", "figure-mode", switchFont,
            new() { ["TextColour"] = Gold });
        var visual = new UiRect(x, switchBounds.Y + switchHeight + pad, width, figureSpace - switchHeight - pad);
        if (view.ShowRadar)
            DrawRadar(controls, parts, estimates, visual, view.RadarColours);
        else
        {
            float unit = Math.Min(visual.Height / 128, width / 96);
            var figure = new UiRect(x + (width - 96 * unit) / 2, visual.Y + (visual.Height - 128 * unit) / 2,
                96 * unit, 128 * unit);
            DrawFigure(controls, character, parts, estimates, selected, hoveredAction, figure, visual, unit);
        }

        float y = visual.Y + visual.Height + visual.Height * .105f + 2 * pad;
        float rowHeight = Math.Min(width * .074f, (area.Y + area.Height - y - pad) / 11);
        if (rowHeight <= 0) return;
        string font = Math.Max(1, (int)MathF.Floor(rowHeight * .76f)).ToString(CultureInfo.InvariantCulture);
        UiRect Row(int row) => new(x, y + row * rowHeight, width, rowHeight);
        void Text(int row, string label, string value = "", bool heading = false)
        {
            var bounds = Row(row);
            AddText(controls, $"label-{row}", value.Length == 0 ? bounds : bounds with { Width = width * .66f },
                label, font, "Left VCenter", heading ? Gold : null);
            if (value.Length > 0)
                AddText(controls, $"value-{row}", bounds with { X = x + width * .68f, Width = width * .32f },
                    value, font, "Right VCenter");
        }

        var estimate = selected is null ? Average(estimates.Values) : estimates[selected.Id];
        Text(0, selected is null ? "EST. ARMOUR PROTECTION" : NueText.Get(selected.Name).ToUpperInvariant(), heading: true);
        if (parts.Length == 0)
        {
            Text(1, "No body-part data available.");
            return;
        }
        Text(1, "Coverage", estimate.Coverage.ToString("P0"));
        Text(2, "Cut resistance", estimate.Cut.ToString("P0"));
        Text(3, "Blunt resistance", estimate.Blunt.ToString("P0"));
        Text(4, "Harpoon resistance", $"{estimate.Harpoon:0.#} pts");

        if (selected is null)
        {
            Text(5, "BODY PARTS", heading: true);
            int pages = PreparePage(view, parts.Length, PartsPerPage);
            for (int i = 0; i < PartsPerPage && view.Page * PartsPerPage + i < parts.Length; i++)
            {
                var part = parts[view.Page * PartsPerPage + i];
                var row = Row(6 + i / 2);
                float half = (width - pad) / 2;
                var button = row with { X = x + (i % 2) * (half + pad), Width = half, Height = rowHeight * .94f };
                AddButton(controls, button, part.Name, "part:" + part.Id, font, Tooltip(part, estimates[part.Id]));
            }
            Navigation(controls, view, Row(10), font, pages, back: false);
            return;
        }

        Text(5, "ARMOUR ON THIS PART", heading: true);
        var covering = gear.Where(item => item.Armour.Data.CoverageOf(selected.Id) > 0).ToArray();
        if (covering.Length == 0) Text(6, "No armour covers this part.");
        int gearPages = PreparePage(view, covering.Length, GearPerPage);
        for (int i = 0; i < GearPerPage && view.Page * GearPerPage + i < covering.Length; i++)
            DrawGear(controls, player, Row(6 + i * 2), font, area, selected, covering[view.Page * GearPerPage + i], i);
        Navigation(controls, view, Row(10), font, gearPages, back: true);
    }

    private static string Shape(GameDatabase database, string id)
    {
        if (database.Find(id) is not {} record) return "";
        var template = BodyPartTemplate.From(record);
        return (template.Type, template.Side) switch
        {
            (BodyPartType.Head, _) => "head",
            (BodyPartType.Arm, BodySide.Left) => "left arm",
            (BodyPartType.Arm, BodySide.Right) => "right arm",
            (BodyPartType.Leg, BodySide.Left) => "left leg",
            (BodyPartType.Leg, BodySide.Right) => "right leg",
            (BodyPartType.Torso, _) when id == ArmourData.ChestPartId => "chest",
            _ => record.Name.Trim().ToLowerInvariant()
        };
    }

    private static Protection Estimate(string part, List<Gear> gear)
    {
        float uncovered = 1, cutRemaining = 1, bluntRemaining = 1, harpoon = 0;
        foreach (var item in gear)
        {
            var piece = item.Armour;
            float chance = Math.Clamp(piece.Data.CoverageOf(part), 0, 100) / 100f;
            // Coverage rolls are independent, while cut and blunt stack on damage remaining.
            uncovered *= 1 - chance;
            cutRemaining *= 1 - chance * piece.CutDefence;
            bluntRemaining *= 1 - chance * piece.BluntDefence;
            harpoon += chance * piece.PierceDefence;
        }
        return new(1 - uncovered, 1 - cutRemaining, 1 - bluntRemaining, harpoon);
    }

    private static Protection Average(ICollection<Protection> estimates) => estimates.Count == 0 ? default :
        new(estimates.Average(p => p.Coverage), estimates.Average(p => p.Cut), estimates.Average(p => p.Blunt), estimates.Average(p => p.Harpoon));

    private static void DrawFigure(List<UiControl> controls, CharacterSnapshot character, Part[] parts,
        Dictionary<string, Protection> estimates, Part? selected, string? hoveredAction,
        UiRect figure, UiRect clip, float unit)
    {
        if (character.Animal || !parts.Any(part => BodyAreas.ContainsKey(part.Shape))) return;
        Image("body", clip, null);
        foreach (var part in parts)
        {
            if (!BodyAreas.TryGetValue(part.Shape, out var region)) continue;
            var bounds = new UiRect(figure.X + region.X * unit, figure.Y + region.Y * unit, region.Width * unit, region.Height * unit);
            var estimate = estimates[part.Id];
            string action = ActionPrefix + "part:" + part.Id;
            bool highlighted = selected?.Id == part.Id || hoveredAction == action;
            var tint = highlighted ? "1 0.82 0.46 1" : estimate.Coverage switch
            {
                >= .75f => "0.56 0.89 0.49 1",
                >= .40f => "0.91 0.75 0.44 1",
                > 0 => "0.94 0.53 0.40 1",
                _ => null
            };
            if (tint is not null) Image("tint-" + part.Id, clip.Intersect(bounds), tint);
            controls.Add(new(ActionPrefix + "body-" + part.Id, "Button", bounds, "", action, "", true,
                Tooltip(part, estimate), default, Pickable: true, Clip: clip));
        }

        void Image(string name, UiRect bounds, string? tint)
        {
            var style = new Dictionary<string, string> { ["ImageTexture"] = silhouette };
            if (tint is not null) style["Colour"] = tint;
            controls.Add(new(ActionPrefix + name, "ImageBox", figure, "", "", "", true,
                style, default, Pickable: false, Clip: bounds));
        }
    }

    private static Dictionary<string, string> Tooltip(Part part, Protection p) =>
        UiTooltips.RowProperties([(NueText.Get(part.Name), ""), (NueText.Get("Coverage"), p.Coverage.ToString("P0")),
            (NueText.Get("Cut"), p.Cut.ToString("P0")), (NueText.Get("Blunt"), p.Blunt.ToString("P0")),
            (NueText.Get("Harpoon"), $"{p.Harpoon:0.#} pts")]);

    private static void DrawGear(List<UiControl> controls, object player, UiRect row, string font, UiRect clip,
        Part part, Gear gear, int index)
    {
        var item = gear.Item;
        var armour = gear.Armour;
        int coverage = Math.Clamp(armour.Data.CoverageOf(part.Id), 0, 100);
        float side = Math.Min(row.Height * 1.8f, row.Width * .18f);
        var icon = new UiRect(row.X, row.Y, side, side);
        AddItemCell.Invoke(ItemCells.Invoke(player, null), [controls, ActionPrefix + "item-" + index, icon, item.Record,
            item.Material, item.Name, 1, null, "nue-inspector-item", item.Index, clip, false, null, item.Colour, false, false, item.ModTag]);
        float inset = side + 3;
        var text = row with { X = row.X + inset, Width = row.Width - inset };
        AddText(controls, $"gear-name-{index}", text, $"{item.Name} · {coverage}%", font, "Left VCenter", Gold);
        string resistances = $"C {armour.CutDefence:P0} · B {armour.BluntDefence:P0} · H {armour.PierceDefence:0.#} pts";
        AddText(controls, $"gear-resist-{index}", text with { Y = text.Y + text.Height }, resistances, font, "Left VCenter");
        var details = UiTooltips.RowProperties([(item.Name, ""), (NueText.Get("Coverage"), $"{coverage}%"),
            (NueText.Get("Cut"), armour.CutDefence.ToString("P0")), (NueText.Get("Blunt"), armour.BluntDefence.ToString("P0")),
            (NueText.Get("Harpoon"), $"{armour.PierceDefence:0.#} pts")]);
        controls.Add(new(ActionPrefix + "gear-tip-" + index, "Widget", row with { Height = row.Height * 2 },
            "", "", "", true, details, default, Pickable: true, Clip: clip));
    }

    private static int PreparePage(View view, int count, int size)
    {
        int pages = Math.Max(1, (count + size - 1) / size);
        view.Page = Math.Clamp(view.Page, 0, pages - 1);
        return pages;
    }

    private static void Navigation(List<UiControl> controls, View view, UiRect bounds, string font, int pages, bool back)
    {
        float step = bounds.Width / 4;
        UiRect Slot(int index) => bounds with { X = bounds.X + step * index, Width = step * .94f };
        if (back) AddButton(controls, Slot(0), "BACK", "back", font);
        if (pages <= 1) return;
        if (view.Page > 0) AddButton(controls, Slot(1), "PREV", "previous", font);
        AddText(controls, "page", Slot(2), $"{view.Page + 1}/{pages}", font, "Center VCenter");
        if (view.Page + 1 < pages) AddButton(controls, Slot(3), "NEXT", "next", font);
    }

    private static void AddButton(List<UiControl> controls, UiRect bounds, string caption, string action, string font,
        Dictionary<string, string>? style = null)
    {
        style ??= new();
        style["TextAlign"] = "Center VCenter";
        style["FontName"] = "Kenshi_StandardFont_Small";
        style["FontPixels"] = font;
        controls.Add(new(ActionPrefix + action, "Button", bounds, NueText.Get(caption), ActionPrefix + action,
            "Kenshi_Button1Skin", true, style, default, Pickable: true, Clip: bounds));
    }

    private static void AddText(List<UiControl> controls, string name, UiRect bounds, string caption,
        string font, string align, string? colour = null)
    {
        var style = new Dictionary<string, string>
        {
            ["TextAlign"] = align,
            ["FontName"] = "Kenshi_StandardFont_Small",
            ["FontPixels"] = font
        };
        if (colour is not null) style["TextColour"] = colour;
        controls.Add(new(ActionPrefix + name, "TextBox", bounds, NueText.Get(caption), "", "", true,
            style, default, Pickable: false, Clip: bounds));
    }
}
