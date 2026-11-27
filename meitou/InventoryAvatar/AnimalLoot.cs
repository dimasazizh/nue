using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private const string AnimalLoot = "NueAnimalLoot";
    private static readonly System.Reflection.MethodInfo BindAnimalBag =
        AccessTools.Method(GameType("Ui.InventoryLayout"), "BindContainerGeometry");

    private static bool DockAnimalTrade(string id, UiBindings bindings, object layout, UiRect root, object player,
        MyGuiScreenHost screens, CharacterSnapshot animal, int width, int height, float gap, float half, float panelHeight)
    {
        if (id == "bag") return true; // The player's bag is already docked inside their panel.
        if (id is not ("inventory" or "loot" or "loot-bag")) return false;

        var grid = animal.ItemGrid;
        var cell = (Vector2)CellSize.Invoke(player, null)!;
        bool hasBag = grid is { BagWidth: > 0, BagHeight: > 0 };
        bool bagOpen = hasBag && screens.IsOpen("loot-bag");
        float nativeWidth = NativeInventoryLayout(player, animal: true).Roots[0].Position.Width * width;
        float sideWidth = hasBag ? 176 * bindings.TemplateScale : 0;
        float animalWidth = Math.Max(nativeWidth, (grid?.Width ?? 0) * cell.X + sideWidth + 3 * gap);
        var bagSize = default(UiRect);
        if (bagOpen && grid is not null)
        {
            if (id == "loot-bag") bagSize = root;
            else
            {
                var bag = new UiBindings();
                BindAnimalBag.Invoke(layout, ["loot-bag", bag, grid.BagWidth, grid.BagHeight, true]);
                bag.Positions.TryGetValue("Root", out bagSize);
            }
        }

        bool showBag = bagSize.Width > 0 && bagSize.Height > 0;
        float bagGap = showBag ? gap : 0;
        // Share the available width when both animal windows are wide, keeping the whole row onscreen.
        float bagWidthLimit = showBag ? Math.Min(bagSize.Width, half * .45f) : 0;
        animalWidth = Math.Min(animalWidth, half - bagGap - bagWidthLimit);
        float bagScale = showBag ? Math.Min(1, Math.Min((half - animalWidth - gap) / bagSize.Width, panelHeight / bagSize.Height)) : 0;
        float bagWidth = bagSize.Width * bagScale, bagHeight = bagSize.Height * bagScale;
        float start = TradeStart(width, half, gap, animalWidth + bagGap + bagWidth);
        float top = (height - panelHeight) / 2;
        float animalX = start + half + gap;
        switch (id)
        {
            case "inventory":
                bindings.Positions["Root"] = root with { Width = half, Height = panelHeight };
                bindings.WindowPosition = new Vector2(start, top) - screens.GetOffset(id);
                break;
            case "loot":
                bindings.Positions["Root"] = root with { Width = animalWidth, Height = panelHeight };
                bindings.WindowPosition = new Vector2(animalX, top) - screens.GetOffset(id);
                var properties = bindings.Properties.GetValueOrDefault("Root") ?? new Dictionary<string,string>();
                bindings.Properties["Root"] = new Dictionary<string,string>(properties) { [AnimalLoot] = hasBag ? "bag" : "plain" };
                break;
            case "loot-bag" when showBag:
                bindings.Positions["Root"] = root with { Width = bagWidth, Height = bagHeight };
                bindings.WindowPosition = new Vector2(animalX + animalWidth + gap, (height - bagHeight) / 2) - screens.GetOffset(id);
                break;
        }
        return true;
    }

    private static void FitAnimalLootGrid(List<UiControl> controls, InventoryView grid, object __instance)
    {
        if (controls.Find(c => c.Type == "Window" && c.Properties.ContainsKey(AnimalLoot)) is not {} window) return;
        int main = controls.FindIndex(c => c.Name == "main");
        if (main < 0 || grid.Width <= 0 || grid.Height <= 0) return;
        var cell = (Vector2)CellSize.Invoke(LayoutPlayer.GetValue(__instance), null)!;
        bool hasBag = window.Properties[AnimalLoot] == "bag";
        var bounds = window.Bounds;
        float pad = Math.Max(4, cell.X * .48f), heading = Math.Max(12, cell.Y);
        var interior = new UiRect(bounds.X + pad, bounds.Y + 2 * heading, bounds.Width - 2 * pad,
            bounds.Height - 2 * heading - pad);
        if (interior.Width <= 0 || interior.Height <= 0) return;
        float side = hasBag ? Math.Min(interior.Width * .43f, 9 * cell.X) : 0;
        float weightHeight = Math.Min(interior.Height * .4f, 6 * heading);
        float gridWidth = hasBag ? interior.Width - side - pad : interior.Width;
        float gridTop = hasBag ? interior.Y : interior.Y + weightHeight + pad;
        float gridHeight = interior.Y + interior.Height - gridTop;
        var available = new UiRect(interior.X, gridTop, gridWidth, gridHeight);
        if (available.Width <= 0 || available.Height <= heading) return;

        var native = controls[main];
        float scale = Math.Min(1, Math.Min(available.Width / native.Bounds.Width, (available.Height - heading) / native.Bounds.Height));
        float fittedWidth = native.Bounds.Width * scale, fittedHeight = native.Bounds.Height * scale;
        var fitted = new UiRect(available.X + (available.Width - fittedWidth) / 2,
            available.Y + (available.Height + heading - fittedHeight) / 2, fittedWidth, fittedHeight);
        controls[main] = WithCells(native with { Bounds = fitted, Clip = fitted.Intersect(bounds) }, grid.Width, grid.Height);
        Move("MainSectionName", fitted with { Y = fitted.Y - heading, Height = heading });

        var slot = InventoryCommands.SectionSize("backpack_attach");
        float slotWidth = Math.Min(side, slot.Width * cell.X), slotHeight = slot.Height * cell.Y;
        float equipmentHeight = heading * 2.25f + slotHeight + 2 * pad;
        if (hasBag) weightHeight = Math.Min(weightHeight, Math.Max(0, interior.Height - equipmentHeight));
        float weightWidth = hasBag ? side : Math.Min(interior.Width, 9 * cell.X);
        float weightX = hasBag ? interior.X + interior.Width - side : interior.X + (interior.Width - weightWidth) / 2;
        float weightY = hasBag
            ? Math.Min(fitted.Y - heading, interior.Y + interior.Height - weightHeight - equipmentHeight)
            : interior.Y;
        var weight = new UiRect(weightX, Math.Max(interior.Y, weightY), weightWidth, weightHeight);
        Move("NueAnimalWeightFrame", weight);
        Move("datapanel", new(weight.X + pad / 2, weight.Y + pad / 2,
            Math.Max(0, weight.Width - pad), Math.Max(0, weight.Height - pad)));
        if (!hasBag)
        {
            controls.RemoveAll(c => c.Name is "backpack_attach" or "lbBackpack" or "OpenBagButton" or "OpenBagButton.Client");
            return;
        }

        float y = weight.Y + weight.Height + pad;
        Move("lbBackpack", new(weight.X, y, side, heading));
        Move("backpack_attach", new(weight.X + (side - slotWidth) / 2, y + heading, slotWidth, slotHeight));
        Move("OpenBagButton", new(weight.X, y + heading + slotHeight + pad, side, heading * 1.25f));

        void Move(string name, UiRect rect)
        {
            int index = controls.FindIndex(c => c.Name == name);
            if (index >= 0) controls[index] = controls[index] with { Bounds = rect, Clip = rect.Intersect(bounds) };
        }
    }

    private static bool KeepAnimalLootHeight(List<UiControl> controls)
    {
        if (!controls.Any(c => c.Type == "Window" && c.Properties.ContainsKey(AnimalLoot))) return true;
        controls.RemoveAll(c => c.Name is "main" or "MainSectionName");
        return false;
    }
}
