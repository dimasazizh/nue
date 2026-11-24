using HarmonyLib;
using Meitou.Simulation.Items;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod : IMod
{
    private const string Texture = "nue:inventory-avatar";
    private static readonly string[] Labels = ["Idle", "Attack stance", "Sitting on mat", "Sleeping / lying"];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static int choice;
    private static float yaw = .12f, zoom = 1f, lastDragX;
    private static MyGuiScreenHost? dragHost;
    private static bool expanded, backgroundsOpen;
    private static readonly string[] Backgrounds = ["Desert", "Swamp", "Black"];
    private static int background;
    private static string folder = "";
    private static MyGuiLayout inspection = null!;
    private static object? ui, renderer;
    private static EditorStageRequest? request;
    private static CharacterId subject = CharacterId.None;
    private static int width = 280, height = 540;

    private static Type GameType(string name) => typeof(IMod).Assembly.GetType("Meitou.Game." + name, true)!;
    private static readonly Type Player = GameType("PlayerInterface"), Host = GameType("GameHost"),
        Render = GameType("Rendering.Characters.CharacterRenderer");
    private static readonly FieldInfo PlayerField = AccessTools.Field(Host, "Player");
    private static readonly FieldInfo Screens = AccessTools.Field(Player, "FaithfulScreens");
    private static readonly MethodInfo GetInventory = AccessTools.PropertyGetter(Player, "InventoryWindows");
    private static readonly MethodInfo Owner = AccessTools.Method(GameType("Ui.InventoryWindows"), "WindowCharacter");
    private static readonly MethodInfo GetRenderer = AccessTools.PropertyGetter(Host, "ActiveCharacters");
    private static readonly PropertyInfo Stage = AccessTools.Property(Render, "StageRequest");
    private static readonly MethodInfo Draw = AccessTools.Method(Render, "DrawStage"), Resolve = AccessTools.Method(Render, "ResolveStage");
    private static readonly PropertyInfo Picture = AccessTools.Property(Render, "StageTexture");
    private static readonly MethodInfo Publish = AccessTools.Method(Player, "SetUiTexture");
    private static readonly FieldInfo Library = AccessTools.Field(Host, "AnimationLibrary");

    private static readonly FieldInfo Editor = AccessTools.Field(Host, "CharacterEditor");
    private static readonly MethodInfo Bar = AccessTools.PropertyGetter(Editor.FieldType, "Bar");
    private static readonly MethodInfo ClipTime = AccessTools.Method(Bar.ReturnType, "EditorClipTime");

    public void Initialize(ModContext context)
    {
        folder = context.Folder;
        inspection = MyGuiReader.ReadLayout(Path.Combine(folder, "InventoryInspection.layout"));
        ConfigurePockets(inspection.Roots[0]);
        After(typeof(ItemFactory), nameof(ItemFactory.MainSize), nameof(PocketSize));
        context.Harmony.Patch(AccessTools.Method(typeof(Meitou.Simulation.Saving.SavedKit), "ToInventory"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(BeforeLoad)),
            postfix: new HarmonyMethod(typeof(AvatarMod), nameof(FitSavedPockets)),
            finalizer: new HarmonyMethod(typeof(AvatarMod), nameof(EndLoad)));
        After(Render, "StageCamera", nameof(FrameAvatar));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.ScreenRouter"), "BindFaithfulScreen"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(SelectLayout)));
        After(Layout, "ArrangeEquipmentGrid", nameof(FitPocketGrid));
        After(Layout, "ArrangeContainerGrid", nameof(FitBagCells));
        foreach (var slot in Accessories)
            context.AddEquipmentSlot(new ModSlot(slot.Section, slot.Fcs, 2, 2, slot.X, 305));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.InventoryWindows"),"Spot"),
            prefix:new HarmonyMethod(typeof(AvatarMod),nameof(WeaponDrop)));
        After(GameType("Ui.InventoryLayout"), "AddModSlots", nameof(PlaceAccessories));
        void After(Type type, string target, string method) => context.Harmony.Patch(AccessTools.Method(type,target),
            postfix: new HarmonyMethod(typeof(AvatarMod),method));
        foreach(string name in new[]{"PlaceCharacterWindow","PlaceBagWindow"})
            context.Harmony.Patch(AccessTools.Method(Layout,name),prefix:new HarmonyMethod(typeof(AvatarMod),nameof(NormalPlacement)));
        After(GameType("Ui.InventoryLayout"), "BindInventoryGeometry", nameof(DockInventory));
        After(GameType("Ui.ScreenRouter"), "AddInventoryContent", nameof(Controls));
        After(GameType("Ui.ScreenRouter"), "FaithfulAction", nameof(Click));
        After(Host, "FillDrawList", nameof(Prepare));
        After(typeof(MyGuiScreenHost),"Raise",nameof(KeepBagAbove));
        After(Render, "DrawPortraits", nameof(Paint));
        void Before(string target, string method) => context.Harmony.Patch(
            AccessTools.Method(typeof(MyGuiScreenHost), target), prefix: new HarmonyMethod(typeof(AvatarMod), method));
        Before("PointerDown", nameof(StartDrag));
        Before("PointerMove", nameof(Rotate));
        Before("PointerUp", nameof(EndDrag));
        Before("Wheel", nameof(Zoom));
    }

    private static readonly (string Section, string Label, int Fcs, int X)[] Accessories =
        [("nue.eyes", "EYES", 4, 12), ("nue.hands", "HANDS", 10, 75), ("nue.neck", "NECK", 11, 138)];

    private static readonly bool ShowAccessorySockets = false;

    // AddModSlots runs before item icons and hit targets are generated from the slot bounds.
    private static void PlaceAccessories(List<UiControl> controls)
    {
        if (!ShowAccessorySockets)
        {
            controls.RemoveAll(c => Accessories.Any(slot => slot.Section == c.Name));
            return;
        }
        if (controls.Find(c=>c.Name=="nue.eyes") is not {} first ||
            controls.Find(c=>c.Name=="datapanel") is not {} summary) return;
        float size=first.Bounds.Height, labelHeight=size*.34f;
        // In the inspection window the summary follows the boots. In linked windows it precedes the grid.
        bool belowGear=controls.Find(c=>c.Name=="boots") is {} boots && summary.Bounds.Y>boots.Bounds.Y;
        float y=belowGear ? summary.Bounds.Y-size-labelHeight*.65f : summary.Bounds.Y+summary.Bounds.Height+labelHeight;
        float step=Math.Max(first.Bounds.Width+3,summary.Bounds.Width/3);
        for (int i=0;i<Accessories.Length;i++)
        {
            var (name,label,_,_)=Accessories[i];
            int index=controls.FindIndex(c=>c.Name==name);
            if(index<0) continue;
            var tile=controls[index];
            var bounds=tile.Bounds with { X=summary.Bounds.X+i*step,Y=y };
            controls[index]=tile with { Bounds=bounds,Skin="Kenshi_InventorySlotSkin" };
            controls.Add(new(name+"-label","TextBox",bounds with { Y=y-labelHeight,Height=labelHeight },
                label,"","Kenshi_TextboxPaintedText",true,new Dictionary<string,string>{{"TextAlign","Left VCenter"}},
                default,Pickable:false,Clip:tile.Clip));
        }
    }

    private static readonly Type Layout = GameType("Ui.InventoryLayout");
    private static readonly FieldInfo LayoutPlayer = Layout.GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);
    private static readonly FieldInfo Width = AccessTools.Field(Player,"Width"), Height = AccessTools.Field(Player,"Height");
    private static readonly MethodInfo GetLayout = AccessTools.PropertyGetter(Player,"InventoryLayout"),
        Linked = AccessTools.Method(Layout,"InWindowRow");

    private static CharacterSnapshot? Subject(object player)
    {
        var screens=(MyGuiScreenHost)Screens.GetValue(player)!;
        if(!screens.IsOpen("inventory") || screens.IsOpen("trader") || (bool)Linked.Invoke(GetLayout.Invoke(player,null),null)!) return null;
        return Owner.Invoke(GetInventory.Invoke(player,null),["inventory"]) is CharacterSnapshot { Animal:false, Appearance:not null } c ? c : null;
    }

    private static UiRect bagArea;
    private static bool NormalPlacement(object __instance) => Subject(LayoutPlayer.GetValue(__instance)!)==null;

    private static void SelectLayout(string id, object __instance)
    {
        if (id != "inventory") return;
        var player = RouterPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        bool inspecting = Subject(player) != null;
        if ((screens.Layout(id).Find("NuePreviewArea") != null) == inspecting) return;
        if (inspecting) screens.Replace(id, inspection);
        else
        {
            var trade = AccessTools.PropertyGetter(Player, "TradeWindows").Invoke(player, null)!;
            var original = (MyGuiLayout)AccessTools.Field(trade.GetType(), "CharacterInventoryLayout").GetValue(trade)!;
            screens.Replace(id, original);
            bagArea = default;
        }
    }

    private static void DockInventory(string id, UiBindings b, object __instance)
    {
        if (id is not ("inventory" or "bag") || !b.Positions.TryGetValue("Root", out var root)) return;
        var player = LayoutPlayer.GetValue(__instance)!;
        if (Subject(player) == null) return;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (id == "bag")
        {
            if (bagArea.Width <= 0 || bagArea.Height <= 0) return;
            float scale = Math.Min(1, Math.Min(bagArea.Width / root.Width, bagArea.Height / root.Height));
            b.Positions["Root"] = root with { Width = root.Width * scale, Height = root.Height * scale };
            b.WindowPosition = new Vector2(bagArea.X, bagArea.Y) - screens.GetOffset(id);
            return;
        }

        var origin = inspection.Roots[0].Position.RelativeTo(new(0, 0, (int)Width.GetValue(player)!, (int)Height.GetValue(player)!), true);
        b.WindowPosition = new Vector2(origin.X, origin.Y) - screens.GetOffset(id);
        // The native character fitter bottom-anchors the heading for its own template.
        var title = screens.Layout(id).Find("InventoryMainTitle")!;
        b.RelativePositions[title] = title.Position;
    }

    private static void FitPocketGrid(List<UiControl> controls, InventoryView grid)
    {
        if (controls.Find(c => c.Name == "NuePocketArea") is not {} anchor) return;
        int index = controls.FindIndex(c => c.Name == "main");
        if (index < 0 || grid.Width <= 0 || grid.Height <= 0) return;
        var slot = controls[index];
        float side = Math.Min(slot.Bounds.Width / grid.Width,
            Math.Min(anchor.Bounds.Width / grid.Width, anchor.Bounds.Height / grid.Height));
        controls[index] = WithCells(slot with { Bounds = anchor.Bounds with {
            Width = side * grid.Width, Height = side * grid.Height }, Clip = anchor.Bounds }, grid.Width, grid.Height);
    }

    private static UiControl WithCells(UiControl slot, int cols, int rows) => slot with {
        Properties = new Dictionary<string,string>(slot.Properties) {
            ["TileSize"] = FormattableString.Invariant($"{slot.Bounds.Width / cols} {slot.Bounds.Height / rows}"),
            ["NueFittedGrid"] = "true" } };

    private static void FitBagCells(string id, List<UiControl> controls, object __instance)
    {
        if (id != "bag" || Subject(LayoutPlayer.GetValue(__instance)!)?.ItemGrid is not {BagWidth: >0, BagHeight: >0} grid) return;
        int i = controls.FindIndex(c => c.Name == "backpack_content");
        if (i >= 0) controls[i] = WithCells(controls[i], grid.BagWidth, grid.BagHeight);
    }

    private static readonly FieldInfo RouterPlayer = GameType("Ui.ScreenRouter").GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);


    private static void Controls(string id, List<UiControl> controls,object __instance)
    {
        if (id != "inventory" || controls.Find(c => c.Name == "NuePreviewArea") is not {} preview ||
            controls.Find(c=>c.Type=="Window") is not {} window) return;
        var player = RouterPlayer.GetValue(__instance)!;
        if (Subject(player) is not {} owner) return;
        var image = preview.Bounds;
        var root = window.Bounds;
        bagArea = controls.First(c => c.Name == "NueBagArea").Bounds;
        width = Math.Max(1,(int)image.Width);
        height = Math.Max(1,(int)image.Height);
        var backdrop = new Dictionary<string,string>();
        if (background < 2) backdrop["ImageTexture"] = Meitou.Game.Rendering.Ui.UiPainter.IconTexture(
            Path.Combine(folder, Backgrounds[background] + ".png"));
        controls.Add(new("NueBackdrop", backdrop.Count == 0 ? "Widget" : "ImageBox", image,"","","",true,
            backdrop, backdrop.Count == 0 ? new Vector4(0,0,0,1) : default, Pickable:false, Clip:root));
        controls.Add(new("NueAvatar","ImageBox",image,"","","",true,
            new Dictionary<string,string>{{"ImageTexture",Texture},{"ImageFlipV","true"}},default,Pickable:true,Clip:root));
        ArrangeEquipment(controls,root,owner,player);
        void Menu(string anchor, string action, string[] options, int selected, bool open)
        {
            var bounds = controls.First(c => c.Name == anchor).Bounds;
            void Button(int index, string label, float y) => controls.Add(new(anchor+index,"Button",bounds with { Y=y },
                label,action+index,"Kenshi_Button1",true,new Dictionary<string,string>(),default,Pickable:true,Clip:root));
            Button(-1,options[selected]+"  ▾",bounds.Y);
            if (open) for(int i=0;i<options.Length;i++) Button(i,options[i],bounds.Y+(i+1)*bounds.Height);
        }
        Menu("NuePoseMenu","nue-avatar:",Labels,choice,expanded);
        Menu("NueBackdropMenu","nue-background:",Backgrounds,background,backgroundsOpen);
    }

    private static readonly MethodInfo Raise=AccessTools.Method(typeof(MyGuiScreenHost),"Raise");
    private static void KeepBagAbove(MyGuiScreenHost __instance,string id)
    {
        if(id=="inventory" && __instance.IsOpen("bag") && ui!=null && Subject(ui)!=null)
            Raise.Invoke(__instance,["bag"]);
    }

    private static bool OverAvatar(MyGuiScreenHost host, Vector2 p) =>
        host.IsOpen("inventory") && host.HitTest(p) is { Screen: "inventory", Control.Name: "NueAvatar" };

    private static bool StartDrag(MyGuiScreenHost __instance, Vector2 p, ref bool __result)
    {
        dragHost = null;
        if (!OverAvatar(__instance, p)) return true;
        dragHost = __instance;
        lastDragX = p.X;
        __result = true;
        return false;
    }

    private static bool Rotate(MyGuiScreenHost __instance, Vector2 p, ref bool __result)
    {
        if (dragHost != __instance) return true;
        yaw = MathF.IEEERemainder(yaw - (p.X - lastDragX) * MathF.Tau / width, MathF.Tau);
        lastDragX = p.X;
        __result = true;
        return false;
    }

    private static void EndDrag() => dragHost = null;

    private static bool Zoom(MyGuiScreenHost __instance, Vector2 p, float steps, ref bool __result)
    {
        if (!OverAvatar(__instance, p)) return true;
        zoom = Math.Clamp(zoom * MathF.Pow(.87f, steps), .4f, 2f);
        __result = true;
        return false;
    }

    private static void Click(MyGuiEvent e)
    {
        if(e.Screen=="inventory" && e.Action is "nue-rack:back" or "nue-rack:hip")
        {
            var section=e.Action[9..];
            openRack=openRack==section ? null : section;
            return;
        }
        if(e.Screen == "inventory" && e.Action.StartsWith("nue-background:") && int.TryParse(e.Action[15..],out int bg))
        {
            expanded = false;
            if(bg == -1) backgroundsOpen = !backgroundsOpen;
            else if ((uint)bg < Backgrounds.Length) { background = bg; backgroundsOpen = false; }
            return;
        }
        if(e.Screen!="inventory" || !e.Action.StartsWith("nue-avatar:") || !int.TryParse(e.Action[11..],out int index)) return;
        backgroundsOpen = false;
        if(index==-1) expanded=!expanded;
        else if((uint)index<Labels.Length) { choice=index; expanded=false; Clock.Restart(); }
    }

    private static void Prepare(object __instance, CharacterDrawList list)
    {
        ui=PlayerField.GetValue(__instance);
        renderer=GetRenderer.Invoke(__instance,null);
        if(renderer==null || ui==null) return;
        if(Stage.GetValue(renderer) is EditorStageRequest prior && prior==request) Stage.SetValue(renderer,null);
        request=null;
        var screens=(MyGuiScreenHost)Screens.GetValue(ui)!;
        if(!screens.IsOpen("inventory") || screens.IsOpen("editor") || Stage.GetValue(renderer)!=null ||
           Subject(ui) is not CharacterSnapshot { Animal: false, Appearance: {} look } c)
        {
            dragHost=null; subject=CharacterId.None; openRack=null; expanded=backgroundsOpen=false; choice=0; return;
        }
        if(subject!=c.Id) { framePoints=null; subject=c.Id; openRack=null; yaw=.12f; zoom=1f; dragHost=null; choice=0; expanded=backgroundsOpen=false; Clock.Restart(); }
        float t=(float)Clock.Elapsed.TotalSeconds;
        var bar = Bar.Invoke(Editor.GetValue(__instance), null);
        CharacterPose StandingPose(string name) =>
            new(name, (float)ClipTime.Invoke(bar, [name, t])!, 1);
        IReadOnlyList<CharacterPose> poses = choice switch {
            2 => [new("sitting idle",t*.55f,1)],
            3 => [new("sleeponfloor",t*.1f,1)],
            _ => [StandingPose(look.Body?.IdleStance ?? "idle_stand_relax")] };
        bool drawn = false, backDrawn = false;
        if (choice == 1 && Library.GetValue(__instance) is AnimationLibrary lib)
        {
            var preferred = c.BackDrawn ? WeaponShown.BackDrawn : WeaponShown.Drawn;
            var weapon = look.Parts.FirstOrDefault(p => p.Shown == preferred)
                ?? look.Parts.FirstOrDefault(p => p.Shown is WeaponShown.Drawn or WeaponShown.BackDrawn);
            var hand = weapon == null ? HandHold.Empty : HandHold.OfCategory(WeaponData.From(weapon.Record).AnimationCategory);
            var layers = CombatLayers(lib, hand);
            if (layers.Count > 0)
            {
                var combat = new CharacterPose[layers.Count];
                for (int i = 0; i < combat.Length; i++)
                {
                    var (index, weight) = layers[i];
                    var clip = lib.Definitions[index];
                    // A movement-driven layer holds its standing phase at zero speed.
                    float time = clip.MoveSpeed == 0 ? (float)ClipTime.Invoke(bar, [clip.Name, t])! :
                        clip.Synchs ? (clip.SynchOffset - MathF.Floor(clip.SynchOffset)) * ClipLength(__instance, clip.Clip) : 0;
                    combat[i] = new CharacterPose(clip.Name, time, weight);
                }
                poses = combat;
                drawn = weapon != null;
                backDrawn = weapon?.Shown == WeaponShown.BackDrawn;
            }
        }
        long key=long.MinValue+((long)(uint)c.Id.Slot<<32)+(uint)c.Id.Generation;
        list.Items.Add(new CharacterInstance(key,look,c.Position,0,poses) { Hidden=true, FullRateAnimation=true,
            Scale=c.Scale,Build=c.Build,WeaponDrawn=drawn,BackDrawn=backDrawn,UnitMovement=choice==3 });
        float distance=choice==3 ? 78 : 54*Math.Max(1,c.Scale);
        request=new(key,width,height,yaw,choice==3?.87f:.09f,distance*zoom,false,.66f,0);
        Stage.SetValue(renderer,request);
    }

    private static readonly FieldInfo Lengths = AccessTools.Field(Host, "AnimationLengths");

    private static float ClipLength(object host, string clip) =>
        (Lengths.GetValue(host) as AnimationLengths)?.Of(clip) ?? 1;

    private static List<(int Index, float Weight)> CombatLayers(AnimationLibrary library, HandHold hand)
    {
        var stance = new AnimationStance { Combat = true, Right = hand };
        List<(int Index, float Weight)> layers = [];
        int guard = library.CombatGuard(stance, 0);
        if (guard < 0) return layers;
        if (library.Definitions[guard].Area != AnimationArea.All)
            library.Movement(AnimationArea.Lower, 0, stance, layers);
        layers.Add((guard, 1));
        return layers;
    }

    private static void Paint(object __instance)
    {
        if(request==null || !ReferenceEquals(renderer,__instance) || !Equals(Stage.GetValue(renderer),request)) return;
        float clearColour = PreviewStudio.Background;
        try {
            PreviewStudio.Background = -1;
            Draw.Invoke(renderer,null);
            Resolve.Invoke(renderer,null);
            if(Picture.GetValue(renderer) is ValueTuple<GpuTexture,SampledTexture> image)
                Publish.Invoke(ui,[Texture,image.Item1,image.Item2]);
        } finally { PreviewStudio.Background = clearColour; Stage.SetValue(renderer,null); request=null; }
    }
}
