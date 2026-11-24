using System.Numerics;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private static readonly (string Section,string Caption)[] Tiles = [
        ("back","lbWeapon1"),("hip","lbWeapon2"),("backpack_attach","lbBackpack"),
        ("head","lbHead"),("shirt","lbShirt"),("armour","lbArmor"),
        ("belt","lbBelt"),("legs","lbPants"),("boots","lbBoots"),
        ("nue.eyes","nue.eyes-label"),("nue.hands","nue.hands-label"),("nue.neck","nue.neck-label")
    ];
    private static readonly System.Reflection.MethodInfo CellSize=AccessTools.PropertyGetter(Player,"InventoryCellSize");
    private static string? openRack;

    private static void ArrangeEquipment(List<UiControl> controls,UiRect root,CharacterSnapshot owner,object player)
    {
        if(owner.ItemGrid is not {} inventory) return;
        float x=root.X+root.Width*.40f, top=root.Y+root.Height*.13f;
        float pitch=Math.Min(root.Width*.26f/3,root.Height*.66f/4);
        float label=Math.Min(root.Height*.028f,pitch*.24f), side=Math.Min(pitch-4,root.Height*.139f);
        side=Math.Min(side,root.Height*.66f/4-label-3);
        float row=side+label+4;
        var cell=(Vector2)CellSize.Invoke(player,null)!;
        int buttonColumn=0;
        foreach(string button in new[]{"OpenBagButton","LimbsButton"})
        {
            int at=controls.FindIndex(c=>c.Name==button);
            if(at>=0) controls[at]=controls[at] with {Bounds=new(x+buttonColumn*pitch,top-label*1.7f,pitch-3,label*1.5f),Clip=root};
            buttonColumn++;
        }
        for(int i=0;i<Tiles.Length;i++)
        {
            var (section,caption)=Tiles[i];
            int at=controls.FindIndex(c=>c.Name==section);
            if(at<0) continue;
            var native=controls[at];
            var tile=new UiRect(x+i%3*pitch,top+i/3*row+label,side,side);
            var props=new Dictionary<string,string>(native.Properties){["TileSize"]=FormattableString.Invariant($"{side} {side}"),["NueEquipmentTile"]="1"};
            controls[at]=native with {Bounds=tile,Clip=root,Properties=props};
            int title=controls.FindIndex(c=>c.Name==caption);
            if(title>=0) controls[title]=controls[title] with {Bounds=tile with {Y=tile.Y-label,Height=label,Width=pitch},Clip=root};
            var items=inventory.Items.Where(item=>item.Section==section).ToArray();
            bool weapon=section is "back" or "hip";
            var capacity=InventoryCommands.SectionSize(section);
            var rack=new UiRect(x-capacity.Width*cell.X-6,tile.Y,capacity.Width*cell.X,capacity.Height*cell.Y);
            bool opened=weapon && openRack==section;
            if(opened) controls.Add(native with {Bounds=rack,Clip=root});
            bool first=true;
            foreach(var item in items)
            {
                string name=$"inventory-item-{owner.Id}-{item.Index}";
                var parts=controls.Where(c=>c.Name==name || c.Name.StartsWith(name+"-",StringComparison.Ordinal)).ToList();
                if(parts.Count==0) continue; // Already lifted.
                foreach(var part in parts) controls.Remove(part);
                if(first) PlaceItem(controls,parts,name,tile,item);
                if(opened) PlaceItem(controls,parts,name,new(rack.X+item.X*cell.X,rack.Y+item.Y*cell.Y,item.Width*cell.X,item.Height*cell.Y),item);
                first=false;
            }
            if(weapon)
                controls.Add(new("NueRack"+section,"Button",new(tile.X+side-label*1.4f,tile.Y,label*1.4f,label),
                    opened?"−":items.Length>1?items.Length.ToString():"+","nue-rack:"+section,"Kenshi_Button1",true,
                    new Dictionary<string,string>(),default,Pickable:true,Clip:root));
        }
    }

    private static void PlaceItem(List<UiControl> output,List<UiControl> parts,string name,UiRect target,InventoryItemView item)
    {
        float scale=Math.Min(target.Width/item.Width,target.Height/item.Height);
        float w=item.Width*scale,h=item.Height*scale;
        var icon=new UiRect(target.X+(target.Width-w)/2,target.Y+(target.Height-h)/2,w,h);
        foreach(var part in parts)
        {
            var bounds=target;
            if(part.Name==name+"-icon") bounds=icon;
            else if(part.Name.StartsWith(name+"-charge",StringComparison.Ordinal))
            {
                var original=parts.First(c=>c.Name==name).Bounds;
                bounds=new(target.X,target.Y+target.Height-3,target.Width*part.Bounds.Width/original.Width,3);
            }
            output.Add(part with {Bounds=bounds,Clip=target});
        }
    }

    // The expanded rack uses native cells. A drop on the compact tile tries a free
    // rectangle first, then offers the displayed weapon's position for a native swap.
    private static void WeaponDrop(string section,ref UiControl slot,ref Vector2 topLeft,HeldItemView h,object __instance)
    {
        if(section is not ("back" or "hip") || !slot.Properties.ContainsKey("NueEquipmentTile")) return;
        var player=InventoryPlayer.GetValue(__instance)!;
        if(Subject(player)?.ItemGrid is not {} grid) return;
        var items=grid.Items.Where(i=>i.Section==section).ToArray();
        var (cols,rows)=InventoryCommands.SectionSize(section);
        if(h.Rotated) h=h.Turned();
        var dest=new Vector2(items.FirstOrDefault()?.X??0,items.FirstOrDefault()?.Y??0);
        bool found=false;
        for(int y=0;y+h.Height<=rows && !found;y++)
            for(int x=0;x+h.Width<=cols;x++)
                if(!items.Any(i=>x<i.X+i.Width && x+h.Width>i.X && y<i.Y+i.Height && y+h.Height>i.Y))
                { dest=new(x,y); found=true; break; }
        var cell=(Vector2)CellSize.Invoke(player,null)!;
        var tile=slot.Bounds;
        slot=slot with {Bounds=tile with {X=tile.X-dest.X*cell.X,Y=tile.Y-dest.Y*cell.Y},Clip=tile};
        topLeft=new(tile.X,tile.Y);
    }
    private static readonly System.Reflection.FieldInfo InventoryPlayer=GameType("Ui.InventoryWindows").GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);
}
