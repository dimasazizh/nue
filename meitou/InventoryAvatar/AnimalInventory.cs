using System.Globalization;
using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private const string AnimalPanel = "NueAnimalPanel";
    private const string EmbeddedBag = "NueEmbeddedAnimalBag";

    private readonly record struct AnimalColumns(UiRect Grid, UiRect Details, float Heading, float Pad);

    private static AnimalColumns AnimalInterior(UiRect panel, Vector2 cell, bool hasBag, float title)
    {
        float pad = Math.Max(4, cell.X * .48f), heading = Math.Max(12, cell.Y);
        // Reserve one input row immediately below the fixed-size window chrome.
        float top = panel.Y + title + heading + 2 * pad;
        float width = Math.Max(1, panel.Width - 2 * pad);
        float height = Math.Max(1, panel.Y + panel.Height - pad - top);
        float side = hasBag ? Math.Min(width * .42f, 10 * cell.X) : 0;
        return new(new(panel.X + pad, top, Math.Max(1, width - (hasBag ? side + pad : 0)), height),
            new(panel.X + panel.Width - pad - side, top, side, height), heading, pad);
    }

    private static bool DockAnimalTrade(string id, UiBindings bindings, UiRect root, object player,
        MyGuiScreenHost screens, CharacterSnapshot animal, int width, int height, float gap, float half, float panelHeight)
    {
        if (id == "bag") return true;
        if (id is not ("inventory" or "loot" or "loot-bag")) return false;
        return BindAnimalInventory(id, bindings, root, player, screens, animal, width, height, gap, half, panelHeight,
            trading: true);
    }

    private static CharacterSnapshot? StandaloneAnimal(object player, MyGuiScreenHost screens)
    {
        if (!screens.IsOpen("inventory") || screens.IsOpen("trader") ||
            (bool)Linked.Invoke(GetLayout.Invoke(player, null), null)!) return null;
        return Owner.Invoke(GetInventory.Invoke(player, null), ["inventory"]) is CharacterSnapshot { Animal: true } animal
            ? animal : null;
    }

    private static bool BindAnimalInventory(string id, UiBindings bindings, UiRect root, object player,
        MyGuiScreenHost screens, CharacterSnapshot animal, int width, int height, float gap, float half, float panelHeight,
        bool trading)
    {
        string panelId = trading ? "loot" : "inventory";
        string bagId = trading ? "loot-bag" : "bag";
        var grid = animal.ItemGrid;
        var cell = (Vector2)CellSize.Invoke(player, null)!;
        bool hasMain = grid is { Width: > 0, Height: > 0 } && !animal.ClosedSlots.Contains("main");
        bool hasBag = grid is { BagWidth: > 0, BagHeight: > 0 };
        bool bagOpen = hasBag && screens.IsOpen(bagId);
        float nativeWidth = NativeInventoryLayout(player, animal: true).Roots[0].Position.Width * width;
        float gridWidth = Math.Max(hasMain ? grid!.Width : 0, hasBag ? grid!.BagWidth : 0) * cell.X;
        float animalWidth = Math.Min(half, Math.Max(nativeWidth, gridWidth + (hasBag ? 10 * cell.X : 0) + 3 * gap));
        float top = (height - panelHeight) / 2;
        float centeredX = (width - animalWidth) / 2;
        float animalX = trading && animal.IsPlayer ? TradeStart(width, half, gap, animalWidth) + half + gap : centeredX;
        var origin = new Vector2(animalX, top) + (trading ? Vector2.Zero : screens.GetOffset(panelId));
        float playerWidth = Math.Min(half, Math.Max(1, animalX - gap));
        var animalPanel = new UiRect(origin.X, origin.Y, animalWidth, panelHeight);
        var character = inspection.Roots[0].Position.RelativeTo(new(0, 0, width, height), inspection.Roots[0].RealPosition);
        float interfaceScale = bindings.TemplateScale * 1080 / height;
        float title = 38 * bindings.TemplateScale * panelHeight / (character.Height * interfaceScale);

        if (trading && id == "inventory")
        {
            bindings.Positions["Root"] = root with { Width = playerWidth, Height = panelHeight };
            bindings.WindowPosition = new Vector2(Math.Max(0, animalX - gap - playerWidth), top) - screens.GetOffset(id);
        }
        else if (id == panelId)
        {
            bindings.Positions["Root"] = root with { Width = animalWidth, Height = panelHeight };
            bindings.WindowPosition = origin - screens.GetOffset(id);
            Mark(bindings, AnimalPanel, $"{(bagOpen ? 2 : hasBag ? 1 : 0)} {title.ToString(CultureInfo.InvariantCulture)}");
            MatchAnimalChrome(bindings, screens.Layout(id).Roots[0], root, animalPanel,
                character.Width * interfaceScale, trading ? playerWidth : half, title);
        }
        else if (id == bagId && bagOpen)
        {
            var columns = AnimalInterior(animalPanel, cell, hasBag, title);
            float mainHeight = hasMain ? columns.Grid.Height * .39f : 0;
            float bagTop = columns.Grid.Y + mainHeight;
            float buttonHeight = 24 * bindings.TemplateScale;
            float available = columns.Grid.Height - mainHeight - buttonHeight - 2 * columns.Pad;
            float nativeHeight = grid!.BagHeight * cell.Y;
            float scale = Math.Min(1, Math.Min(columns.Grid.Width / (grid.BagWidth * cell.X), available / nativeHeight));
            if (scale <= 0) return false;
            float bagWidth = grid.BagWidth * cell.X * scale, bagHeight = nativeHeight * scale;
            float embeddedWidth = Math.Min(columns.Grid.Width, Math.Max(bagWidth, 100 * bindings.TemplateScale));
            bindings.Positions["Root"] = root with { Width = embeddedWidth, Height = bagHeight + buttonHeight + 2 * columns.Pad };
            bindings.WindowPosition = new Vector2(columns.Grid.X + (columns.Grid.Width - embeddedWidth) / 2, bagTop) - screens.GetOffset(id);
            Mark(bindings, EmbeddedBag, FormattableString.Invariant($"{bagWidth} {bagHeight} {columns.Pad} {buttonHeight} {100 * bindings.TemplateScale}"));
            if (animal.IsPlayer)
            {
                bindings.Disabled.Remove("ArrangeButton");
                bindings.Actions["ArrangeButton"] = "ArrangeButton";
            }
        }
        return true;
    }

    private static void Mark(UiBindings bindings, string key, string value)
    {
        var previous = bindings.Properties.GetValueOrDefault("Root") ?? new Dictionary<string,string>();
        bindings.Properties["Root"] = new Dictionary<string,string>(previous) { [key] = value };
    }

    private static void MatchAnimalChrome(UiBindings bindings, MyGuiWidget root, UiRect old, UiRect panel,
        float characterWidth, float fittedCharacterWidth, float title)
    {
        var header = root.Children.FirstOrDefault(child => child.Skin == "Kenshi_GenericWindowHeaderSkin");
        if (header is null || old.Width <= 0 || panel.Width <= 0 || characterWidth <= 0 ||
            !bindings.RelativePositions.TryGetValue(header, out var headerPosition)) return;
        float height = title / panel.Height;
        bindings.RelativePositions[header] = headerPosition with { Height = height };
        var body = root.Children.FirstOrDefault(child => child.Skin == "Kenshi_GenericWindowSkin");
        if (body is not null && bindings.RelativePositions.TryGetValue(body, out var bodyPosition))
            bindings.RelativePositions[body] = bodyPosition with
            {
                Y = height, Height = Math.Max(0, bodyPosition.Y + bodyPosition.Height - height)
            };

        float widthRatio = old.Width * fittedCharacterWidth / (characterWidth * panel.Width);
        foreach (var child in header.Children)
        {
            if (!bindings.RelativePositions.TryGetValue(child, out var rect)) continue;
            if (child.Type == "Button")
            {
                bindings.RelativePositions[child] = rect with
                {
                    X = 1 - (1 - rect.X) * widthRatio, Width = rect.Width * widthRatio
                };
            }
            else if (child.Name == "Caption")
                bindings.RelativePositions[child] = rect with { Width = Math.Max(0, 1 - rect.X - (1 - rect.X - rect.Width) * widthRatio) };
        }
    }

    private static void FitAnimalGrid(List<UiControl> controls, InventoryView grid, object __instance)
    {
        if (controls.Find(c => c.Type == "Window" && c.Properties.ContainsKey(AnimalPanel)) is not {} window ||
            ReadDimensions(window.Properties[AnimalPanel], 2) is not {} sizing) return;
        var cell = (Vector2)CellSize.Invoke(LayoutPlayer.GetValue(__instance), null)!;
        bool hasBag = sizing[0] > 0, bagOpen = sizing[0] == 2;
        var (lane, details, heading, pad) = AnimalInterior(window.Bounds, cell, hasBag, sizing[1]);
        controls.Add(new("NueAnimalSearchArea", "Widget",
            new(lane.X, window.Bounds.Y + sizing[1] + pad, Math.Min(lane.Width, 13 * cell.X), heading),
            "", "", "", true, new Dictionary<string,string>(), default, Pickable: false));
        float weightHeight = Math.Min(6.5f * heading, lane.Height * .4f);
        int main = controls.FindIndex(c => c.Name == "main");
        if (main >= 0 && grid.Width > 0 && grid.Height > 0)
        {
            var native = controls[main];
            float gridTop = hasBag ? lane.Y : lane.Y + weightHeight + pad;
            float room = bagOpen ? lane.Height * .39f : lane.Y + lane.Height - gridTop;
            float scale = Math.Min(1, Math.Min(lane.Width / native.Bounds.Width, (room - heading - pad) / native.Bounds.Height));
            if (scale > 0)
            {
                float w = native.Bounds.Width * scale, h = native.Bounds.Height * scale;
                var fitted = new UiRect(lane.X + (lane.Width - w) / 2, gridTop + heading, w, h);
                controls[main] = WithCells(native with { Bounds = fitted, Clip = fitted.Intersect(window.Bounds) }, grid.Width, grid.Height);
                Move("MainSectionName", fitted with { Y = gridTop, Height = heading });
            }
        }

        float weightWidth = hasBag ? details.Width : Math.Min(lane.Width, 10 * cell.X);
        var weight = new UiRect(hasBag ? details.X : lane.X + (lane.Width - weightWidth) / 2, lane.Y, weightWidth, weightHeight);
        Move("NueAnimalWeightFrame", weight);
        Move("datapanel", new(weight.X + pad / 2, weight.Y + pad / 2,
            Math.Max(0, weight.Width - pad), Math.Max(0, weight.Height - pad)));
        if (!hasBag)
        {
            controls.RemoveAll(c => c.Name is "backpack_attach" or "lbBackpack" or "OpenBagButton" or "OpenBagButton.Client");
            return;
        }

        var slot = InventoryCommands.SectionSize("backpack_attach");
        float slotWidth = Math.Min(details.Width, slot.Width * cell.X), slotHeight = slot.Height * cell.Y;
        float y = weight.Y + weight.Height + pad;
        Move("lbBackpack", new(details.X, y, details.Width, heading));
        Move("backpack_attach", new(details.X + (details.Width - slotWidth) / 2, y + heading, slotWidth, slotHeight));
        if (controls.Find(c => c.Name == "OpenBagButton") is {} button)
        {
            float buttonWidth = Math.Min(details.Width, 6 * cell.X);
            float buttonHeight = Math.Min(button.Bounds.Height, 1.25f * heading);
            Move("OpenBagButton", new(details.X + (details.Width - buttonWidth) / 2, y + heading + slotHeight + pad, buttonWidth, buttonHeight));
        }

        void Move(string name, UiRect rect)
        {
            int index = controls.FindIndex(c => c.Name == name);
            if (index >= 0) controls[index] = controls[index] with { Bounds = rect, Clip = rect.Intersect(window.Bounds) };
        }
    }

    private static bool FitEmbeddedAnimalBag(string id, List<UiControl> controls, InventoryView grid)
    {
        if (id is not ("bag" or "loot-bag") ||
            controls.Find(c => c.Type == "Window" && c.Properties.ContainsKey(EmbeddedBag)) is not {} window ||
            ReadDimensions(window.Properties[EmbeddedBag], 5) is not {} dimensions ||
            controls.Find(c => c.Name == "backpack_content") is not {} slot) return false;
        var bounds = window.Bounds;
        var (width, height, pad, buttonHeight, buttonWidth) = (dimensions[0], dimensions[1], dimensions[2], dimensions[3], dimensions[4]);
        var gridBounds = new UiRect(bounds.X + (bounds.Width - width) / 2, bounds.Y, width, height);
        var arrange = controls.Find(c => c.Name == "ArrangeButton");
        controls.Clear();
        controls.Add(window with { Caption = "", Skin = "", Background = default,
            Properties = new Dictionary<string,string>(window.Properties) { ["Movable"] = "false" } });
        controls.Add(WithCells(slot with { Bounds = gridBounds, Clip = gridBounds }, grid.BagWidth, grid.BagHeight));
        if (arrange is not null)
        {
            float fixedWidth = Math.Min(bounds.Width, buttonWidth);
            var button = new UiRect(bounds.X + (bounds.Width - fixedWidth) / 2, bounds.Y + height + pad, fixedWidth, buttonHeight);
            controls.Add(arrange with { Bounds = button, Clip = button });
        }
        return true;
    }

    private static float[]? ReadDimensions(string text, int count)
    {
        var parts = text.Split(' ');
        if (parts.Length != count) return null;
        var result = new float[count];
        for (int i = 0; i < count; i++)
            if (!float.TryParse(parts[i], CultureInfo.InvariantCulture, out result[i])) return null;
        return result;
    }

    private static bool KeepAnimalPanelHeight(List<UiControl> controls)
    {
        if (!controls.Any(c => c.Type == "Window" && c.Properties.ContainsKey(AnimalPanel))) return true;
        controls.RemoveAll(c => c.Name is "main" or "MainSectionName");
        return false;
    }

}
