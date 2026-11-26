using Meitou.Game.Ui;
using HarmonyLib;
using System.Numerics;
using System.Globalization;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private static readonly System.Reflection.MethodInfo BindVendorGeometry =
        AccessTools.Method(GameType("Ui.TradeWindows"), "BindTraderGeometry");
    private static readonly System.Reflection.MethodInfo GetProduction = AccessTools.PropertyGetter(Player, "Production"),
        GetRouter = AccessTools.PropertyGetter(Player, "ScreenRouter"),
        GetLinkedMachine = AccessTools.Method(GameType("Ui.ProductionWindows"), "LinkedScreen"),
        BindScreen = AccessTools.Method(GameType("Ui.ScreenRouter"), "BindFaithfulScreen"),
        InventoryCell = AccessTools.PropertyGetter(Player, "InventoryCell"),
        InventoryCellHeight = AccessTools.PropertyGetter(Player, "InventoryCellHeight");
    private const string StockScroll = "scrollview_backpack_content";
    private const string StorageGridSize = "NueStorageGridSize";

    private static bool IsStorageScreen(string id) => id is "storage" or "storage-own";

    private static string? LinkedStorage(object player) =>
        GetLinkedMachine.Invoke(GetProduction.Invoke(player, null), null) as string == "storage" ? "storage" : null;

    private static bool DockTrade(string id, UiBindings bindings, object layout, UiRect root)
    {
        if (id is not ("inventory" or "loot" or "trader" or "bag" or "loot-bag")) return false;
        var player = LayoutPlayer.GetValue(layout)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (!screens.IsOpen("inventory")) return false;
        bool merchant = screens.IsOpen("trader");
        var target = screens.IsOpen("loot")
            ? Owner.Invoke(GetInventory.Invoke(player, null), ["loot"]) as CharacterSnapshot
            : null;
        string? storage = merchant || target != null ? null : LinkedStorage(player);
        if (!merchant && target == null && storage == null) return false;

        var (width, height, gap, half, panelHeight) = TradeDimensions(player);
        if (merchant)
            return DockVendorTrade(id, bindings, player, screens, width, height, gap, half, panelHeight);
        if (storage != null)
        {
            if (id == "bag") return true;
            if (id != "inventory") return false;
            var storageBindings = (UiBindings)BindScreen.Invoke(GetRouter.Invoke(player, null), [storage])!;
            if (!storageBindings.Positions.TryGetValue("Root", out var storageRoot)) return false;
            float start = TradeStart(width, half, gap, storageRoot.Width);
            bindings.Positions["Root"] = root with { Width = half, Height = panelHeight };
            bindings.WindowPosition = new Vector2(start, (height - panelHeight) / 2) - screens.GetOffset(id);
            return true;
        }

        bool partner = id is "loot" or "loot-bag";
        bool bag = id is "bag" or "loot-bag";
        bindings.Positions["Root"] = root with {Width = half, Height = panelHeight};
        bindings.WindowPosition = new Vector2(partner ? half + 2 * gap : gap, (height-panelHeight)/2) - screens.GetOffset(id);
        if (partner && target is { Animal: true })
        {
            // The bag can bind before the partner window switches to the animal template.
            float animalWidth = NativeInventoryLayout(player, animal: true).Roots[0].Position.Width * width;
            animalWidth = Math.Min(animalWidth, half - gap);
            if (bag)
            {
                float scale = Math.Min(1, Math.Min((half - animalWidth - gap) / root.Width, panelHeight / root.Height));
                bindings.Positions["Root"] = root with { Width = root.Width * scale, Height = root.Height * scale };
                bindings.WindowPosition += new Vector2(animalWidth + gap, 0);
            }
            else bindings.Positions["Root"] = root with { Width = animalWidth, Height = Math.Min(root.Height, panelHeight) };
            return true;
        }
        if (bag) bindings.Positions["Root"] = root; // DockInventory fits the native bag into its owner's bag area.
        return true;
    }

    private static bool DockVendorTrade(string id, UiBindings bindings, object player, MyGuiScreenHost screens,
        int width, int height, float gap, float playerWidth, float playerHeight)
    {
        if (id == "bag") return true; // DockInventory places the bag inside the player window.
        if (id is not ("inventory" or "trader")) return false;

        var vendor = bindings;
        if (id == "inventory")
        {
            // Both panels need the same native vendor dimensions, regardless of binding order.
            vendor = new UiBindings();
            var trade = AccessTools.PropertyGetter(Player, "TradeWindows").Invoke(player, null);
            BindVendorGeometry.Invoke(trade, [vendor]);
        }

        var layout = screens.Layout("trader");
        if (!vendor.Positions.TryGetValue("Root", out var native) ||
            layout.Find("Content") is not {} content || layout.Find(StockScroll) is not {} view ||
            !vendor.RelativePositions.TryGetValue(view, out var relative)) return false;

        if (native.Width <= 0 || native.Height <= 0 ||
            content.Position.Width <= 0 || content.Position.Height <= 0) return false;

        float vendorWidth = Math.Min(native.Width, width - playerWidth - 3 * gap);
        float start = TradeStart(width, playerWidth, gap, vendorWidth);
        float top = (height - playerHeight) / 2;

        if (id == "inventory")
        {
            bindings.Positions["Root"] = bindings.Positions["Root"] with { Width = playerWidth, Height = playerHeight };
            bindings.WindowPosition = new Vector2(start, top) - screens.GetOffset(id);
            return true;
        }

        float nativeContentWidth = native.Width * content.Position.Width;
        float nativeContentHeight = native.Height * content.Position.Height;
        float contentWidth = vendorWidth * content.Position.Width, contentHeight = playerHeight * content.Position.Height;
        float x = relative.X * nativeContentWidth, y = relative.Y * nativeContentHeight;
        float viewWidth = relative.Width * nativeContentWidth + contentWidth - nativeContentWidth;
        float viewHeight = relative.Height * nativeContentHeight + contentHeight - nativeContentHeight;
        bindings.RelativePositions[view] = new(x / contentWidth, y / contentHeight,
            Math.Max(1, viewWidth) / contentWidth, Math.Max(1, viewHeight) / contentHeight);
        bindings.Positions["Root"] = native with { Width = vendorWidth, Height = playerHeight };
        bindings.WindowPosition = new Vector2(start + playerWidth + gap, top) - screens.GetOffset(id);
        return true;
    }

    private static float TradeStart(float width, float playerWidth, float gap, float stockWidth) =>
        (width - playerWidth - gap - stockWidth) / 2;

    private static (int Width, int Height, float Gap, float Half, float PanelHeight) TradeDimensions(object player)
    {
        int width = (int)Width.GetValue(player)!, height = (int)Height.GetValue(player)!;
        float gap = Math.Max(4, width * .005f);
        float half = (width - 3 * gap) / 2;
        var template = inspection.Roots[0].Position;
        float panelHeight = Math.Min(height * .84f, half * template.Height * height / (template.Width * width));
        return (width, height, gap, half, panelHeight);
    }

    private static void SelectStorageLayout(MyGuiScreenHost screens, string id)
    {
        var storage = screens.Layout(id);
        if (storage.Find(StockScroll) != null) return;
        var trader = screens.Layout("trader");
        var root = trader.Roots[0];
        var content = trader.Find("Content")!;
        var view = trader.Find(StockScroll)!;
        var grid = trader.Find("backpack_content")! with { Position = new(0, 0, 1, 1) };
        var header = content.Children.Single(w => w.Children.Any(c => c.Name == "datapanel"));
        var children = content.Children.Where(w => w != header && w.Name != grid.Name)
            .Select(w => w == view ? view with { Children = [..view.Children, grid] } : w)
            .Concat(new[] { "TypeText", "CapacityText" }.Select(name =>
            {
                var label = storage.Find(name)!;
                return label with
                {
                    Position = header.Position with { Height = header.Position.Height / 2,
                        Y = header.Position.Y + (name == "CapacityText" ? header.Position.Height / 2 : 0) }
                };
            })).ToArray();
        screens.Replace(id, trader with { Roots = [root with
        {
            Position = storage.Roots[0].Position with { Width = root.Position.Width, Height = root.Position.Height },
            Children = root.Children.Select(w => w == content ? content with { Children = children } : w).ToArray()
        }] });
    }

    private static void BindStorageTradeGeometry(string id, UiBindings bindings, int columns, int rows, object __instance)
    {
        if (!IsStorageScreen(id) || columns <= 0 || rows <= 0) return;
        var player = LayoutPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        var layout = screens.Layout(id);
        if (layout.Find(StockScroll) is not {} view || layout.Find("Content") is not {} content ||
            layout.Find("backpack_content") is not {} grid ||
            !bindings.Positions.TryGetValue("Root", out var root)) return;

        var (width, height, gap, half, panelHeight) = TradeDimensions(player);
        float gridWidth = columns * (float)InventoryCell.Invoke(player, null)!;
        float gridHeight = rows * (float)InventoryCellHeight.Invoke(player, null)!;
        bool linked = LinkedStorage(player) == id && screens.IsOpen("inventory");
        var area = content.Position;
        float desiredWidth = (gridWidth + 2 * gap) / (area.Width * view.Position.Width);
        float panelWidth = Math.Min(linked ? half : width - 2 * gap, Math.Max(half * .45f, desiredWidth));
        bindings.Positions["Root"] = root with { Width = panelWidth, Height = panelHeight };
        // Keep the standalone building anchor and clamp its final position after the host adds the window offset.
        bindings.FitWindowToViewport = !linked;
        bindings.RelativePositions[content] = area;
        bindings.RelativePositions[view] = view.Position;
        bindings.RelativePositions[grid] = grid.Position;
        foreach (var widget in content.Children.Where(w => w.Name is "TypeText" or "CapacityText" or "ArrangeButton"))
            bindings.RelativePositions[widget] = widget.Position;
        bindings.Canvas[StockScroll] = (gridWidth, gridHeight);
        bindings.NativeScroll.Add(StockScroll);
        bindings.Properties[StockScroll] = new Dictionary<string, string> { ["VisibleHScroll"] = "true" };
        bindings.Properties[grid.Name] = new Dictionary<string, string>
            { [StorageGridSize] = FormattableString.Invariant($"{gridWidth} {gridHeight}") };
        if (linked)
        {
            float start = TradeStart(width, half, gap, panelWidth);
            bindings.WindowPosition = new Vector2(start + half + gap, (height - panelHeight) / 2) - screens.GetOffset(id);
        }
    }

    private static void ArrangeStorageTradeGrid(string id, List<UiControl> controls)
    {
        if (!IsStorageScreen(id)) return;
        int index = controls.FindIndex(c => c.Name == "backpack_content");
        if (index < 0) return;
        var grid = controls[index];
        // The scroll canvas can exceed the actual grid when storage has only a few rows.
        var size = grid.Properties.GetValueOrDefault(StorageGridSize)?.Split(' ');
        if (size is not { Length: 2 } ||
            !float.TryParse(size[0], CultureInfo.InvariantCulture, out float width) ||
            !float.TryParse(size[1], CultureInfo.InvariantCulture, out float height)) return;
        controls[index] = grid with { Bounds = grid.Bounds with { Width = width, Height = height } };
    }

    private static MyGuiLayout? mirrored;
    private static MyGuiLayout MirroredInspection()
    {
        if (mirrored != null) return mirrored;
        var root = inspection.Roots[0];
        var panel = root.Children.Single(c => c.Children.Any(w => w.Name == "NuePreviewArea"));
        var preview = panel.Children.Single(w => w.Name == "NuePreviewArea").Position;
        var bag = panel.Children.Single(w => w.Name == "NueBagArea").Position;
        float previewEnd = preview.X + preview.Width;
        // Keep the order and gaps within each column while swapping the side columns.
        float Offset(float x) => x < previewEnd ? bag.X + bag.Width - previewEnd
            : x < bag.X ? bag.Width - preview.Width
            : preview.X - bag.X;
        var items = panel.Children.Select(w => w with {
            Name = w.Name == "NueSearch.inventory" ? "NueSearch.loot" : w.Name,
            Position = w.Position with { X = w.Position.X + Offset(w.Position.X) }
        }).ToArray();
        return mirrored = inspection with {Roots = [root with { Children = root.Children.Select(c =>
            ReferenceEquals(c,panel) ? panel with {Children = items} : c).ToArray() }]};
    }

    private static readonly System.Reflection.FieldInfo[] TargetFields =
        new[]{"_stageColour","_stageDepth","_stageSampled"}.Select(n => HarmonyLib.AccessTools.Field(Render,n)).ToArray();
    private static readonly object?[] PartnerTargets = new object?[3];
    private static object? targetOwner;

    private static void SwapTargets(object renderer)
    {
        targetOwner = renderer;
        for (int i=0; i<TargetFields.Length;i++)
        {
            var original = TargetFields[i].GetValue(renderer);
            TargetFields[i].SetValue(renderer,PartnerTargets[i]);
            PartnerTargets[i] = original;
        }
    }

    private static void ReleasePartnerTargets(object __instance)
    {
        if (!ReferenceEquals(targetOwner,__instance)) return;
        for (int i=0;i<2;i++) (PartnerTargets[i] as IDisposable)?.Dispose();
        Array.Clear(PartnerTargets);
        targetOwner = null;
    }

    private static void IncludePartnerPose(HashSet<long> ____portraitKeys)
    {
        if (other.request is {} stage) ____portraitKeys.Add(stage.Key);
    }
}
