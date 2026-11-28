using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Nue.SearchCompare;

namespace Nue.Unified;

internal static partial class CharacterInspector
{
    private static readonly string[] RadarParts =
        ["head", "chest", "right arm", "right leg", "left leg", "left arm", "stomach"];

    private static void DrawRadar(List<UiControl> controls, Part[] parts,
        Dictionary<string, Protection> estimates, UiRect area, int[] colours)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        string font = Math.Max(1, (int)(area.Height * .067f)).ToString(CultureInfo.InvariantCulture);
        var axes = RadarParts.Select(shape => parts.FirstOrDefault(part => part.Shape == shape)).OfType<Part>().ToArray();
        if (axes.Length < 3)
        {
            AddText(controls, "radar-empty", area, "No body-part data available.", font, "Center VCenter");
            return;
        }

        var values = axes.Select(part =>
        {
            var protection = estimates[part.Id];
            return Vector3.Clamp(new(protection.Coverage, protection.Cut, protection.Blunt), Vector3.Zero, Vector3.One);
        }).ToArray();
        string texture = ArmourRadarImage.For(values, colours);
        if (texture.Length == 0)
        {
            AddText(controls, "radar-empty", area, "Armour chart unavailable.", font, "Center VCenter");
            return;
        }

        float row = area.Height * .105f;
        float side = Math.Min(area.Width * .61f, area.Height - row * 2);
        var centre = new Vector2(area.X + area.Width / 2, area.Y + (area.Height - row) / 2);
        var image = new UiRect(centre.X - side / 2, centre.Y - side / 2, side, side);
        controls.Add(new(ActionPrefix + "radar", "ImageBox", image, "", "", "", true,
            new Dictionary<string, string> { ["ImageTexture"] = texture }, default, Pickable: false, Clip: area));

        for (int axis = 0; axis < axes.Length; axis++)
        {
            var part = axes[axis];
            var direction = ArmourRadarImage.AxisDirection(axis, axes.Length);
            var anchor = centre + direction * (side * .49f);
            float labelWidth = area.Width * .29f;
            float alignment = Math.Clamp(direction.X * 1.2f, -1, 1);
            float labelX = Math.Clamp(anchor.X - labelWidth * (1 - alignment) / 2,
                area.X, area.X + area.Width - labelWidth);
            var label = new UiRect(labelX, Math.Max(area.Y, anchor.Y - row / 2), labelWidth, row);
            string align = alignment >= .5f ? "Left VCenter" : alignment <= -.5f ? "Right VCenter" : "Center VCenter";
            AddText(controls, "radar-axis-" + axis, label, part.Name, font, align, Gold);
            var protection = estimates[part.Id];
            var tooltip = UiTooltips.RowProperties([
                ("#E8BD72" + NueText.Get(part.Name).ToUpperInvariant() + " · " + NueText.Get("ARMOUR PROTECTION"), ""),
                (NueText.Get("Armour coverage"), protection.Coverage.ToString("P0")),
                (NueText.Get("Cut damage resistance"), protection.Cut.ToString("P0")),
                (NueText.Get("Blunt damage resistance"), protection.Blunt.ToString("P0")),
                (NueText.Get("Harpoon resistance"), $"{protection.Harpoon:0.#} pts")
            ]);
            controls.Add(new(ActionPrefix + "radar-tip-" + axis, "Button", label, "", ActionPrefix + "part:" + part.Id,
                "", true, tooltip, default, Pickable: true, Clip: area));
        }

        float legendWidth = area.Width / ArmourRadarImage.Metrics.Length;
        for (int i = 0; i < ArmourRadarImage.Metrics.Length; i++)
        {
            string label = NueText.Get(ArmourRadarImage.Metrics[i]);
            var colour = ArmourRadarImage.Palette[colours[i]];
            var bounds = new UiRect(area.X + i * legendWidth, area.Y + area.Height, legendWidth, row);
            var hint = string.Format(CultureInfo.CurrentCulture, NueText.Get("Click to change {0} colour"), label);
            var style = UiTooltips.RowProperties([(hint, "")]);
            style["TextAlign"] = "Center VCenter";
            style["FontName"] = "Kenshi_StandardFont_Small";
            style["FontPixels"] = font;
            controls.Add(new(ActionPrefix + "radar-legend-" + i, "Button", bounds, label,
                ActionPrefix + "chart-colour:" + i, "", true, style, default,
                TextColour: colour, Pickable: true, Clip: bounds));
        }
    }
}
