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

    private static bool DockTrade(string id, UiBindings bindings, object layout, UiRect root)
    {
        if (id is not ("inventory" or "loot" or "trader" or "bag" or "loot-bag")) return false;
        var player = LayoutPlayer.GetValue(layout)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (!screens.IsOpen("inventory")) return false;
        bool merchant = screens.IsOpen("trader");
        var inventories = GetInventory.Invoke(player, null);
        if (!merchant && !(screens.IsOpen("loot") &&
            Owner.Invoke(inventories, ["loot"]) is CharacterSnapshot { IsPlayer: true })) return false;

        int width = (int)Width.GetValue(player)!, height = (int)Height.GetValue(player)!;
        float gap = Math.Max(4, width * .005f);
        float half = (width - 3 * gap) / 2;
        float panelHeight = Math.Min(height * .84f, half * inspection.Roots[0].Position.Height * height / (inspection.Roots[0].Position.Width * width));
        if (merchant)
            return DockVendorTrade(id, bindings, player, screens, width, height, gap, half, panelHeight);

        bool partner = id is "loot" or "loot-bag";
        bool bag = id is "bag" or "loot-bag";
        bindings.Positions["Root"] = root with {Width = half, Height = panelHeight};
        bindings.WindowPosition = new Vector2(partner ? half + 2 * gap : gap, (height-panelHeight)/2) - screens.GetOffset(id);
        if (partner && screens.IsOpen("loot-bag") &&
            Owner.Invoke(inventories, ["loot"]) is CharacterSnapshot { Animal:true })
        {
            float header = Math.Min(panelHeight * .4f, root.Height);
            if (bag)
            {
                header = panelHeight * .4f;
                float scale = Math.Min(half/root.Width,(panelHeight-header-gap)/root.Height);
                bindings.Positions["Root"] = root with {Width=root.Width*scale, Height=root.Height*scale};
                bindings.WindowPosition += new Vector2(0,header+gap);
            }
            else bindings.Positions["Root"] = root with {Width=half, Height=header};
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
            layout.Find("Content") is not {} content || layout.Find("scrollview_backpack_content") is not {} view ||
            !vendor.RelativePositions.TryGetValue(view, out var relative) ||
            !vendor.Canvas.TryGetValue(view.Name, out var canvas)) return false;

        var cell = (Vector2)CellSize.Invoke(player, null)!;
        if (native.Width <= 0 || native.Height <= 0 || cell.X <= 0 ||
            content.Position.Width <= 0 || content.Position.Height <= 0) return false;

        float vendorWidth = Math.Min(native.Width, width - playerWidth - 3 * gap);
        float nativeContentHeight = native.Height * content.Position.Height;
        // Meitou initially sizes the stock viewport as a square. Fit the actual row count.
        float stockHeightChange = canvas.Item2 - MathF.Round(canvas.Item1 / cell.X) * cell.Y;
        float searchHeight = 0;
#if NUE_UNIFIED
        if (Nue.SearchCompare.SearchMod.Installed)
            searchHeight = Nue.SearchCompare.SearchMod.VendorSearchSpace * bindings.TemplateScale;
#endif
        float nativeViewHeight = relative.Height * nativeContentHeight;
        float desiredViewHeight = Math.Max(cell.Y, nativeViewHeight + stockHeightChange) + searchHeight;
        float vendorHeight = Math.Min(height * .84f, native.Height + (desiredViewHeight - nativeViewHeight) / content.Position.Height);
        float start = (width - playerWidth - gap - vendorWidth) / 2;

        if (id == "inventory")
        {
            bindings.Positions["Root"] = bindings.Positions["Root"] with { Width = playerWidth, Height = playerHeight };
            bindings.WindowPosition = new Vector2(start, (height - playerHeight) / 2) - screens.GetOffset(id);
            return true;
        }

        float nativeContentWidth = native.Width * content.Position.Width;
        float contentWidth = vendorWidth * content.Position.Width, contentHeight = vendorHeight * content.Position.Height;
        float x = relative.X * nativeContentWidth, y = relative.Y * nativeContentHeight;
        float viewWidth = relative.Width * nativeContentWidth + contentWidth - nativeContentWidth;
        float viewHeight = nativeViewHeight + contentHeight - nativeContentHeight;
        bindings.RelativePositions[view] = new(x / contentWidth, y / contentHeight,
            Math.Max(1, viewWidth) / contentWidth, Math.Max(1, viewHeight) / contentHeight);
        bindings.Positions["Root"] = native with { Width = vendorWidth, Height = vendorHeight };
        bindings.WindowPosition = new Vector2(start + playerWidth + gap, (height - vendorHeight) / 2) - screens.GetOffset(id);
        return true;
    }

    private static MyGuiLayout? mirrored;
    private static MyGuiLayout MirroredInspection()
    {
        if (mirrored != null) return mirrored;
        var root = inspection.Roots[0];
        var panel = root.Children.Single(c => c.Children.Any(w => w.Name == "NuePreviewArea"));
        var preview = panel.Children.Single(w => w.Name == "NuePreviewArea").Position;
        float centerEnd = panel.Children.Single(w => w.Name == "NueBagArea").Position.X;
        var items = panel.Children.Select(w => w with {
            Name = w.Name == "NueSearch.inventory" ? "NueSearch.loot" : w.Name,
            // Swap the side columns while keeping the equipment and pockets in their original order.
            Position = w.Position.X >= preview.X + preview.Width && w.Position.X < centerEnd
                ? w.Position
                : w.Position with { X = 1 - w.Position.X - w.Position.Width }
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
