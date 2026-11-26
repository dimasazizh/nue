using Meitou.Game.Ui;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
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
        bool partner = id is "loot" or "loot-bag" or "trader";
        bool bag = id is "bag" or "loot-bag";
        float panelHeight = Math.Min(height * .84f, half * inspection.Roots[0].Position.Height * height / (inspection.Roots[0].Position.Width * width));
        bindings.Positions["Root"] = root with {Width = half, Height = panelHeight};
        bindings.WindowPosition = new Vector2(partner ? half + 2 * gap : gap, (height-panelHeight)/2) - screens.GetOffset(id);
        if (partner && !merchant && screens.IsOpen("loot-bag") &&
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

    private static MyGuiLayout? mirrored;
    private static MyGuiLayout MirroredInspection()
    {
        if (mirrored != null) return mirrored;
        var root = inspection.Roots[0];
        var panel = root.Children.Single(c => c.Children.Any(w => w.Name == "NuePreviewArea"));
        var items = panel.Children.Select(w => w with {
            Name = w.Name == "NueSearch.inventory" ? "NueSearch.loot" : w.Name,
            Position = w.Position with { X = 1 - w.Position.X - w.Position.Width }
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
