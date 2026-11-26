using System.Numerics;
using Meitou.Data.Textures;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Rendering.Characters;
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
        ("belt","lbBelt"),("legs","lbPants"),("boots","lbBoots")
    ];
    private static readonly System.Reflection.MethodInfo CellSize=AccessTools.PropertyGetter(Player,"InventoryCellSize");


    private static void ArrangeEquipment(List<UiControl> controls,UiRect root,CharacterSnapshot owner,object player)
    {
        if(owner.ItemGrid is not {} inventory) return;
        var cell=(Vector2)CellSize.Invoke(player,null)!;
        foreach(var (section,caption) in Tiles)
        {
            int at=controls.FindIndex(c=>c.Name==section);
            if(at<0 || controls.Find(c=>c.Name=="NueTile-"+section) is not {} region) continue;
            var native=controls[at];
            var box=region.Bounds;
            float side=Math.Min(box.Width,box.Height);
            var tile=new UiRect(box.X+(box.Width-side)/2,box.Y,side,side);
            float label=controls.Find(c=>c.Name==caption)?.Bounds.Height ?? side*.2f;
            var props=new Dictionary<string,string>(native.Properties){["TileSize"]=FormattableString.Invariant($"{side} {side}"),["NueEquipmentTile"]="1"};
            controls[at]=native with {Bounds=tile,Clip=root,Properties=props};
            var items=inventory.Items.Where(item=>item.Section==section).ToArray();
            bool weapon=section is "back" or "hip";
            var capacity=InventoryCommands.SectionSize(section);
            var rack=new UiRect(tile.X-capacity.Width*cell.X-6,tile.Y,capacity.Width*cell.X,capacity.Height*cell.Y);
            bool opened=weapon && active.openRack==section;
            if(opened) controls.Add(native with {Bounds=rack,Clip=root});
            bool first=true;
            foreach(var item in items)
            {
                string name=$"inventory-item-{owner.Id}-{item.Index}";
                var parts=controls.Where(c=>c.Name==name || c.Name.StartsWith(name+"-",StringComparison.Ordinal)).ToList();
                if(parts.Count==0) continue; // Already lifted.
                foreach(var part in parts) controls.Remove(part);
                if(first) PlaceItem(controls,parts,name,tile,item,weapon,section == "head");
                if(opened) PlaceItem(controls,parts,name,new(rack.X+item.X*cell.X,rack.Y+item.Y*cell.Y,item.Width*cell.X,item.Height*cell.Y),item);
                first=false;
            }
            if(weapon)
                controls.Add(new("NueRack"+section,"Button",new(tile.X+side-label*1.4f,tile.Y,label*1.4f,label),
                    opened?"−":items.Length>1?items.Length.ToString():"+","nue-rack:"+section,"Kenshi_Button1",true,
                    new Dictionary<string,string>(),default,Pickable:true,Clip:root));
        }
    }

    private static void PlaceItem(List<UiControl> output,List<UiControl> parts,string name,UiRect target,InventoryItemView item,bool diagonal=false, bool centreHelmet=false)
    {
        float scale=Math.Min(target.Width/item.Width,target.Height/item.Height);
        if (diagonal)
            scale = MathF.Sqrt(2) * Math.Min(target.Width,target.Height) / (item.Width + item.Height);
        float w=item.Width*scale,h=item.Height*scale;
        var icon=new UiRect(target.X+(target.Width-w)/2,target.Y+(target.Height-h)/2,w,h);
        foreach(var part in parts)
        {
            var bounds=target;
            if(part.Name==name+"-icon")
            {
                bounds = icon;
                if (centreHelmet && part.Properties.TryGetValue("ImageTexture", out var texture))
                    bounds = icon with { Y = icon.Y + icon.Height * HelmetOffset(texture) };
            }
            else if(part.Name.StartsWith(name+"-charge",StringComparison.Ordinal))
            {
                var original=parts.First(c=>c.Name==name).Bounds;
                bounds=new(target.X,target.Y+target.Height-3,target.Width*part.Bounds.Width/original.Width,3);
            }
            var properties = diagonal && part.Name == name+"-icon"
                ? new Dictionary<string,string>(part.Properties) { ["ImageAngle"] = (-MathF.PI/4).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : part.Properties;
            output.Add(part with {Bounds=bounds,Clip=target,Properties=properties});
        }
    }

    // Inventory art includes transparent padding. Centre the visible rows inside the equipment tile.
    private static readonly Dictionary<string,(float Offset,long Retry)> HelmetOffsets = new();
    private static float HelmetOffset(string texture)
    {
        long now = Environment.TickCount64;
        if (HelmetOffsets.TryGetValue(texture, out var cached) && now < cached.Retry)
            return cached.Offset;
        string? path = null;
        if (texture.StartsWith("file:", StringComparison.Ordinal))
            path = texture[5..];
        else if (renderer != null && UiPainter.TryParseRenderedIcon(texture, out var record, out var material, out var colour) &&
            AccessTools.Property(Render, "IconSaveDirectory").GetValue(renderer) is string directory &&
            AccessTools.Field(Render, "_icons").GetValue(renderer) is System.Collections.IDictionary entries &&
            entries[new IconKey(record, material, colour)] is {} entry &&
            AccessTools.Field(entry.GetType(), "MaterialId").GetValue(entry) is string materialId && materialId.Length > 0)
            path = Path.Combine(directory, record + "." + materialId + (colour.Length > 0 ? "." + colour : "") + ".png");

        // A freshly rendered icon can reach the UI before the client's active.background PNG write completes.
        if (path != null && File.Exists(path))
        {
            try
            {
                var image = TextureLoader.LoadFile(path, false).Levels[0];
                int first = image.Height, last = -1;
                for (int y = 0; y < image.Height; y++)
                    for (int x = 0; x < image.Width; x++)
                        if (image.Pixels[(y * image.Width + x) * 4 + 3] >= 16)
                        {
                            first = Math.Min(first, y);
                            last = y;
                            break;
                        }
                float offset = last < 0 ? 0 : .5f - (first + last + 1f) / (2 * image.Height);
                HelmetOffsets[texture] = (offset, long.MaxValue);
                return offset;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        HelmetOffsets[texture] = (0, now + 2000);
        return 0;
    }

    // The active.expanded rack uses native cells. A drop on the compact tile tries a free
    // rectangle first, then offers the displayed weapon's position for a native swap.
    private static void WeaponDrop(ItemOwner window,string section,ref UiControl slot,ref Vector2 topLeft,HeldItemView h,object __instance)
    {
        if (section is ("main" or "backpack_content") && slot.Properties.ContainsKey("NueFittedGrid"))
        {
            var cells = slot.Properties["TileSize"].Split(' ');
            var fitted = new Vector2(float.Parse(cells[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(cells[1], System.Globalization.CultureInfo.InvariantCulture));
            var native = (Vector2)CellSize.Invoke(InventoryPlayer.GetValue(__instance), null)!;
            topLeft += (Vector2)HeldGrab.GetValue(__instance)! * (native - fitted);
            return;
        }
        if(section is not ("back" or "hip") || !slot.Properties.ContainsKey("NueEquipmentTile")) return;
        var player=InventoryPlayer.GetValue(__instance)!;
        var session = (Meitou.Game.Host.WorldSession)AccessTools.Field(Player,"Session").GetValue(player)!;
        if (session.CurrentSnapshot.Characters.FirstOrDefault(c=>c.Id==window.Character)?.ItemGrid is not {} grid) return;
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
    private static readonly System.Reflection.FieldInfo HeldGrab=AccessTools.Field(GameType("Ui.InventoryWindows"),"_heldGrab");
    private static readonly System.Reflection.FieldInfo InventoryPlayer=GameType("Ui.InventoryWindows").GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);
}
