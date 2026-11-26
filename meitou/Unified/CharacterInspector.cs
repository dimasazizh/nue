using System.Globalization;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Items;

namespace Nue.Unified;

internal static class CharacterInspector
{
    private static GameDatabase? data;

    internal static void Enable(GameDatabase database) => data = database;

    private readonly record struct Protection(float Coverage, float Cut, float Blunt, float Harpoon);

    internal static void AddSummary(List<UiControl> controls, CharacterSnapshot character, UiRect portrait, UiRect window)
    {
        if (data is not {} database || character.ItemGrid is not {} grid) return;
        var parts = character.HealthParts.Select(part => part.Id).ToArray();
        if (parts.Length == 0 && character.Appearance?.Race is {} race)
            parts = RaceData.From(race, database).Anatomy.Select(entry => entry.Part.StringId).ToArray();
        if (parts.Length == 0) return;

        var armour = new List<ArmourPiece>();
        foreach (var item in grid.Items)
        {
            if (!InventoryCommands.IsEquipmentSection(item.Section) ||
                database.Find(item.Record) is not { Type: FcsRecordType.Armour } record) continue;

            var piece = ArmourData.From(record);
            armour.Add(ArmourPiece.Create(piece, ArmourPiece.QualityOf(piece, item.Level), ArmourPiece.Graded(piece, item.Level)));
        }
        var estimate = Estimate(parts, armour);
        var rows = new (string Label, string Value)[]
        {
            ("EST. ARMOUR PROTECTION", ""),
            ("Coverage", estimate.Coverage.ToString("P0")),
            ("Cut resistance", estimate.Cut.ToString("P0")),
            ("Blunt resistance", estimate.Blunt.ToString("P0")),
            ("Harpoon resistance", $"{estimate.Harpoon:0.#} pts")
        };

        float pad = Math.Clamp(portrait.Width * .025f, 2, 8);
        float y = portrait.Y + portrait.Height + pad;
        float width = portrait.Width - 2 * pad;
        float height = (window.Y + window.Height - y - pad) / rows.Length;
        if (width <= 0 || height <= 0) return;
        float rowHeight = Math.Min(height, width * .073f);
        string font = Math.Max(1, (int)MathF.Floor(Math.Min(rowHeight * .76f, width / 14)))
            .ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < rows.Length; i++)
        {
            var row = new UiRect(portrait.X + pad, y + i * rowHeight, width, rowHeight);
            var (label, value) = rows[i];
            AddText(controls, $"label-{i}", i == 0 ? row : row with { Width = width * .72f }, label,
                font, "Left VCenter", i == 0 ? "0.909804 0.741176 0.447059" : null);
            if (value.Length > 0)
                AddText(controls, $"value-{i}", row with { X = row.X + width * .73f, Width = width * .27f },
                    value, font, "Right VCenter");
        }
    }

    private static Protection Estimate(string[] parts, List<ArmourPiece> armour)
    {
        float coverage = 0, cut = 0, blunt = 0, harpoon = 0;
        foreach (string part in parts)
        {
            float uncovered = 1, cutRemaining = 1, bluntRemaining = 1;
            foreach (var piece in armour)
            {
                float chance = Math.Clamp(piece.Data.CoverageOf(part), 0, 100) / 100f;
                // Each layer rolls coverage independently. Cut and blunt stack on damage remaining.
                uncovered *= 1 - chance;
                cutRemaining *= 1 - chance * piece.CutDefence;
                bluntRemaining *= 1 - chance * piece.BluntDefence;
                harpoon += chance * piece.PierceDefence;
            }
            coverage += 1 - uncovered;
            cut += 1 - cutRemaining;
            blunt += 1 - bluntRemaining;
        }
        return new(coverage / parts.Length, cut / parts.Length, blunt / parts.Length, harpoon / parts.Length);
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
        controls.Add(new("NueArmourSummary-" + name, "TextBox", bounds, caption, "", "", true,
            style, default, Pickable: false, Clip: bounds));
    }
}
