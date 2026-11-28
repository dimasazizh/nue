using Meitou.Game.Ui;
using HarmonyLib;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private static readonly System.Reflection.MethodInfo BindVendorGeometry =
        AccessTools.Method(GameType("Ui.TradeWindows"), "BindTraderGeometry");
    private static readonly System.Reflection.MethodInfo GetProduction = AccessTools.PropertyGetter(Player, "Production"),
        GetLinkedMachine = AccessTools.Method(GameType("Ui.ProductionWindows"), "LinkedScreen");
    private static readonly System.Reflection.FieldInfo TradePlayer =
        AccessTools.Field(GameType("Ui.TradeWindows"), "<player>P");
    private static readonly System.Reflection.MethodInfo SelectedForLoot =
        AccessTools.Method(GameType("PlayerInterface"), "Selected");
    private const string StockScroll = "scrollview_backpack_content";
    private const string TradeArrangeSize = "NueTradeArrangeSize";
    private static readonly System.Reflection.MethodInfo BindFaithfulScreen =
        AccessTools.Method(GameType("Ui.ScreenRouter"), "BindFaithfulScreen");
    [ThreadStatic] private static bool measuringLinkedStorage;

    private static bool IsStockWindow(string id) => id is "trader" or "storage" or "storage-own";

    private static void BindTradeChrome(string id, object __instance, UiBindings __result)
    {
        if (id == "inventory")
        {
            if (!measuringLinkedStorage) DockLinkedStorageInventory(__instance, __result);
            return;
        }
        if (!IsStockWindow(id) || !__result.Positions.TryGetValue("Root", out var stock) ||
            stock.Width <= 0 || stock.Height <= 0) return;
        var player = RouterPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (id is "storage" or "storage-own")
            __result.FitWindowToViewport = true;
        if (!screens.IsRegistered("inventory")) return;
        var layout = screens.Layout(id);
        var header = layout.Roots[0].Children.FirstOrDefault(w => w.Skin == "Kenshi_GenericWindowHeaderSkin");
        if (header is null) return;

        var reference = (UiBindings)BindFaithfulScreen.Invoke(__instance, ["inventory"])!;
        var inventory = screens.Layout("inventory");
        var inventoryHeader = inventory.Roots[0].Children.FirstOrDefault(w => w.Skin == header.Skin);
        if (inventoryHeader is null || !reference.Positions.TryGetValue("Root", out var inventoryRoot) ||
            inventoryRoot.Width <= 0 || inventoryRoot.Height <= 0) return;

        if (id == "storage" && screens.IsOpen("inventory") && StorageLinked(player))
        {
            var (width, height, gap, _, _) = TradeDimensions(player);
            stock = stock with { Width = Math.Min(stock.Width, LinkedStorageWidth(width, gap, inventoryRoot.Width)) };
            __result.Positions["Root"] = stock;
            float top = StorageTradeTop(height, stock, inventoryRoot);
            __result.WindowPosition = new Vector2(Math.Max(0, width - stock.Width), top) - screens.GetOffset(id);
        }

        var referenceHeader = BoundWithin(reference, inventoryHeader, inventoryRoot);
        if (referenceHeader.Height <= 0 || referenceHeader.Width <= 0) return;
        float titleHeight = referenceHeader.Height / stock.Height;
        var stockHeader = new UiRect(0, 0, stock.Width, referenceHeader.Height);
        __result.RelativePositions[header] = new(0, 0, 1, titleHeight);
        var body = layout.Roots[0].Children.FirstOrDefault(w => w.Skin == "Kenshi_GenericWindowSkin");
        if (body is not null && !(id == "trader" && body.Name == "Content"))
            __result.RelativePositions[body] = new(0, titleHeight, 1, Math.Max(0, 1 - titleHeight));
        if (id == "trader" && screens.IsOpen("inventory") &&
            layout.Find("Content") is {} content && layout.Find(StockScroll) is {} view)
            FitVendorContent(__result, content, view, stock.Width, stock.Height, referenceHeader.Height);

        float captionRight = 1;
        foreach (var button in header.Children.Where(w => w.Type == "Button"))
        {
            var inventoryButton = inventoryHeader.Children.FirstOrDefault(w => w.Type == "Button" && w.Skin == button.Skin);
            if (inventoryButton is null) continue;
            var rect = BoundWithin(reference, inventoryButton, referenceHeader);
            float right = referenceHeader.X + referenceHeader.Width - rect.X;
            captionRight = Math.Min(captionRight, Math.Max(0, 1 - (right + 2) / stockHeader.Width));
            __result.RelativePositions[button] = new(1 - right / stockHeader.Width,
                (rect.Y - referenceHeader.Y) / stockHeader.Height, rect.Width / stockHeader.Width, rect.Height / stockHeader.Height);
        }
        var caption = header.Children.FirstOrDefault(w => w.Name == "Caption");
        var inventoryCaption = inventoryHeader.Children.FirstOrDefault(w => w.Name == "Caption");
        if (caption is not null && inventoryCaption is not null)
        {
            var rect = BoundWithin(reference, inventoryCaption, referenceHeader);
            float x = (rect.X - referenceHeader.X) / stockHeader.Width;
            __result.RelativePositions[caption] = new(x, (rect.Y - referenceHeader.Y) / stockHeader.Height,
                Math.Max(0, captionRight - x), rect.Height / stockHeader.Height);
        }

        if (layout.Find("ArrangeButton") is not null && inventory.Find("ArrangeButton") is {} inventoryArrange)
        {
            var rect = BoundWithin(reference, inventoryArrange, inventoryRoot);
            var properties = __result.Properties.GetValueOrDefault("ArrangeButton") ?? new Dictionary<string, string>();
            __result.Properties["ArrangeButton"] = new Dictionary<string, string>(properties)
                { [TradeArrangeSize] = FormattableString.Invariant($"{rect.Width} {rect.Height}") };
        }
    }

    private static void DockLinkedStorageInventory(object router, UiBindings inventory)
    {
        if (!inventory.Positions.TryGetValue("Root", out var root)) return;
        var player = RouterPlayer.GetValue(router)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (!screens.IsOpen("storage") || !StorageLinked(player)) return;

        UiBindings storage;
        measuringLinkedStorage = true;
        try
        {
            storage = (UiBindings)BindFaithfulScreen.Invoke(router, ["storage"])!;
        }
        finally
        {
            measuringLinkedStorage = false;
        }
        if (!storage.Positions.TryGetValue("Root", out var stock) || stock.Width <= 0 || stock.Height <= 0) return;

        var (width, height, gap, _, _) = TradeDimensions(player);
        root = root with { Width = Math.Min(root.Width, Math.Max(1, width - stock.Width - gap)) };
        inventory.Positions["Root"] = root;
        float x = Math.Max(0, width - stock.Width - gap - root.Width);
        inventory.WindowPosition = new Vector2(x, StorageTradeTop(height, stock, root)) - screens.GetOffset("inventory");
    }

    private static float LinkedStorageWidth(float width, float gap, float inventoryWidth) =>
        Math.Max(1, Math.Min(width - 24, width - gap - Math.Min(inventoryWidth, width / 2)));

    private static float StorageTradeTop(float height, UiRect storage, UiRect inventory) =>
        Math.Max(0, (height - Math.Max(Math.Min(storage.Height, height - 24), inventory.Height)) / 2);

    private static UiRect BoundWithin(UiBindings bindings, MyGuiWidget widget, UiRect parent)
    {
        if (bindings.RelativePositions.TryGetValue(widget, out var relative))
            return relative.RelativeTo(parent, true);
        if (widget.RealPosition)
            return widget.Position.RelativeTo(parent, true);
        float scale = bindings.TemplateScale;
        var rect = widget.Position;
        return new(parent.X + rect.X * scale, parent.Y + rect.Y * scale, rect.Width * scale, rect.Height * scale);
    }

    private static void FitTradeArrangeButton(string id, List<UiControl> controls)
    {
        if (!IsStockWindow(id) || controls.Find(c => c.Type == "Window") is not {} window) return;
        int index = controls.FindIndex(c => c.Name == "ArrangeButton");
        if (index < 0) return;
        var button = controls[index];
        if (!button.Properties.TryGetValue(TradeArrangeSize, out string? size) ||
            ReadDimensions(size, 2) is not {} dimensions) return;
        float width = Math.Min(dimensions[0], window.Bounds.Width);
        var bounds = button.Bounds with
        {
            X = Math.Clamp(button.Bounds.X + (button.Bounds.Width - width) / 2,
                window.Bounds.X, window.Bounds.X + window.Bounds.Width - width),
            Y = button.Bounds.Y + button.Bounds.Height - dimensions[1],
            Width = width, Height = dimensions[1]
        };
        controls[index] = button with { Bounds = bounds, Clip = bounds.Intersect(window.Bounds) };
    }
    private static bool StorageLinked(object player) =>
        GetLinkedMachine.Invoke(GetProduction.Invoke(player, null), null) as string == "storage";

    private static void EndTradeOnPartnerSelection(object __instance, ref bool ____lootLinked)
    {
        var player = TradePlayer.GetValue(__instance)!;
        if (Screens.GetValue(player) is not MyGuiScreenHost screens || !screens.IsOpen("loot")) return;
        if (Owner.Invoke(GetInventory.Invoke(player, null), ["loot"]) is not CharacterSnapshot { IsPlayer: true } partner ||
            SelectedForLoot.Invoke(player, null) is not IReadOnlyList<CharacterSnapshot> selected ||
            selected.FirstOrDefault()?.Id != partner.Id) return;

        // Let native loot synchronization close the simulated container without closing the active inventory.
        ____lootLinked = false;
        if (screens.IsOpen("loot-bag")) screens.Close("loot-bag");
        screens.Close("loot");
        screens.SetOffset("inventory", Vector2.Zero);
    }

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
        if (!merchant && target == null) return false;

        var (width, height, gap, half, panelHeight) = TradeDimensions(player);
        if (merchant)
            return DockVendorTrade(id, bindings, player, screens, width, height, gap, half, panelHeight);
        if (target is { Animal: true })
            return DockAnimalTrade(id, bindings, root, player, screens, target, width, height, gap, half, panelHeight);

        bool partner = id is "loot" or "loot-bag";
        bool bag = id is "bag" or "loot-bag";
        bindings.Positions["Root"] = root with {Width = half, Height = panelHeight};
        bindings.WindowPosition = new Vector2(partner ? half + 2 * gap : gap, (height-panelHeight)/2) - screens.GetOffset(id);
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
        if (!vendor.Positions.TryGetValue("Root", out var native) || native.Width <= 0 || native.Height <= 0 ||
            layout.Find("Content") is null || layout.Find(StockScroll) is null) return false;

        float vendorWidth = Math.Min(native.Width, width - playerWidth - 3 * gap);
        float start = TradeStart(width, playerWidth, gap, vendorWidth);
        float top = (height - playerHeight) / 2;

        if (id == "inventory")
        {
            bindings.Positions["Root"] = bindings.Positions["Root"] with { Width = playerWidth, Height = playerHeight };
            bindings.WindowPosition = new Vector2(start, top) - screens.GetOffset(id);
            return true;
        }

        bindings.Positions["Root"] = native with { Width = vendorWidth, Height = playerHeight };
        bindings.WindowPosition = new Vector2(start + playerWidth + gap, top) - screens.GetOffset(id);
        return true;
    }

    private static void FitVendorContent(UiBindings bindings, MyGuiWidget content, MyGuiWidget view,
        float vendorWidth, float playerHeight, float header)
    {
        float scale = bindings.TemplateScale;
        float bodyHeight = playerHeight - header;
        if (bodyHeight <= 0) return;
        float inset = Math.Min(10 * scale, vendorWidth * .05f);
        float moneyHeight = Math.Min(28 * scale, bodyHeight * .12f);
        float footer = Math.Min(56 * scale, bodyHeight * .20f);
        float viewTop = moneyHeight + 2 * inset;
        bindings.RelativePositions[content] = new(0, header / playerHeight, 1, bodyHeight / playerHeight);
        bindings.RelativePositions[view] = new(inset / vendorWidth, viewTop / bodyHeight,
            1 - 2 * inset / vendorWidth, Math.Max(1, bodyHeight - viewTop - footer) / bodyHeight);
        if (content.Children.FirstOrDefault(widget => widget.Children.Any(child => child.Name == "datapanel")) is {} moneyBox &&
            moneyBox.Children.FirstOrDefault(child => child.Name == "datapanel") is {} money)
        {
            bindings.RelativePositions[moneyBox] = new(inset / vendorWidth, inset / bodyHeight,
                .40f, moneyHeight / bodyHeight);
            bindings.RelativePositions[money] = new(0, 0, 1, 1);
        }
        if (content.Children.FirstOrDefault(widget => widget.Name == "ArrangeButton") is {} arrange)
            bindings.RelativePositions[arrange] = new(.38f, 1 - .75f * footer / bodyHeight, .24f, .5f * footer / bodyHeight);
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
