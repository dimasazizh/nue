using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;

namespace Nue.Unified;

internal static partial class CharacterInspector
{
    private static readonly string[] RadarParts =
        ["head", "chest", "right arm", "right leg", "left leg", "left arm", "stomach"];

    private static void DrawRadar(List<UiControl> controls, Part[] parts,
        Dictionary<string, Protection> estimates, UiRect area)
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
        string texture = ArmourRadarImage.For(values);
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
            double angle = axis * Math.Tau / axes.Length;
            float x = centre.X + (float)Math.Sin(angle) * side * .62f;
            float y = centre.Y - (float)Math.Cos(angle) * side * .48f;
            float labelWidth = area.Width * .29f;
            float labelX = Math.Clamp(x - labelWidth / 2, area.X, area.X + area.Width - labelWidth);
            var label = new UiRect(labelX, y - row / 2, labelWidth, row);
            AddText(controls, "radar-axis-" + axis, label, part.Name, font, "Center VCenter", Gold);
            controls.Add(new(ActionPrefix + "radar-tip-" + axis, "Button", label, "", ActionPrefix + "part:" + part.Id,
                "", true, Tooltip(part, estimates[part.Id]), default, Pickable: true, Clip: area));
        }

        float legendWidth = area.Width / ArmourRadarImage.Metrics.Length;
        for (int i = 0; i < ArmourRadarImage.Metrics.Length; i++)
        {
            var (label, colour) = ArmourRadarImage.Metrics[i];
            var bounds = new UiRect(area.X + i * legendWidth, area.Y + area.Height - row, legendWidth, row);
            AddText(controls, "radar-legend-" + i, bounds, label, font, "Center VCenter",
                FormattableString.Invariant($"{colour.X} {colour.Y} {colour.Z}"));
        }
    }
}
