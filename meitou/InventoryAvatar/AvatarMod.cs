using HarmonyLib;
using Meitou.Data.Gameplay;
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

public sealed class AvatarMod : IMod
{
    private const string Texture = "nue:inventory-avatar";
    private static readonly string[] Labels = ["Idle", "Attack stance", "Sitting on mat", "Sleeping / lying"];
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static int choice;
    private static float yaw = .12f, zoom = 1f, lastDragX;
    private static MyGuiScreenHost? dragHost;
    private static bool expanded;
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
        void After(Type type, string target, string method) => context.Harmony.Patch(AccessTools.Method(type,target),
            postfix: new HarmonyMethod(typeof(AvatarMod),method));
        After(GameType("Ui.InventoryLayout"), "BindInventoryGeometry", nameof(Widen));
        After(GameType("Ui.ScreenRouter"), "AddInventoryContent", nameof(Controls));
        After(GameType("Ui.ScreenRouter"), "FaithfulAction", nameof(Click));
        After(Host, "FillDrawList", nameof(Prepare));
        After(Render, "DrawPortraits", nameof(Paint));
        void Before(string target, string method) => context.Harmony.Patch(
            AccessTools.Method(typeof(MyGuiScreenHost), target), prefix: new HarmonyMethod(typeof(AvatarMod), method));
        Before("PointerDown", nameof(StartDrag));
        Before("PointerMove", nameof(Rotate));
        Before("PointerUp", nameof(EndDrag));
        Before("Wheel", nameof(Zoom));
    }

    private static float PaneWidth(float h) => MathF.Round(h * .49f);
    private static void Widen(string id, UiBindings b, object __instance)
    {
        if (id != "inventory" || !b.Positions.TryGetValue("Root", out var root)) return;
        var player = __instance.GetType().GetFields(AccessTools.allDeclared).Single(f => f.FieldType == Player).GetValue(__instance);
        var layout = ((MyGuiScreenHost)Screens.GetValue(player)!).Layout(id);
        var content = layout.Roots[0].Children.FirstOrDefault(c => c.DescendantsAndSelf().Any(w => w.Name == "main"));
        if (content == null || !b.RelativePositions.TryGetValue(content,out var bounds)) return;
        float total = root.Width + PaneWidth(root.Height);
        float ratio = root.Width / total;
        b.RelativePositions[content] = bounds with { X = bounds.X * ratio, Width = bounds.Width * ratio };
        b.Positions["Root"] = root with { Width = total };
    }

    private static void Controls(string id, List<UiControl> controls)
    {
        if (id != "inventory" || controls.Find(c=>c.Type=="Window") is not {} window ||
            !controls.Any(c=>c.Name=="main")) return;
        var root = window.Bounds;
        float pane = PaneWidth(root.Height), row = Math.Max(18, MathF.Round(root.Height*.054f));
        var menu = new UiRect(root.X+root.Width-pane+4,root.Y+row+5,pane-10,row);
        var image = new UiRect(menu.X,menu.Y+row+3,menu.Width,root.Y+root.Height-menu.Y-row-9);
        width = Math.Max(1,(int)image.Width);
        height = Math.Max(1,(int)image.Height);
        controls.Add(new("NueAvatar","ImageBox",image,"","","",true,
            new Dictionary<string,string>{{"ImageTexture",Texture},{"ImageFlipV","true"}},default,Pickable:true,Clip:root));
        void Button(int index, string label, float y) => controls.Add(new("NuePose"+index,"Button",menu with { Y=y },
            label,"nue-avatar:"+index,"Kenshi_Button1",true,new Dictionary<string,string>(),default,Pickable:true,Clip:root));
        Button(-1,Labels[choice]+"  ▾",menu.Y);
        if (expanded) for(int i=0;i<Labels.Length;i++) Button(i,Labels[i],menu.Y+(i+1)*row);
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
        if(e.Screen!="inventory" || !e.Action.StartsWith("nue-avatar:") || !int.TryParse(e.Action[11..],out int index)) return;
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
           Owner.Invoke(GetInventory.Invoke(ui,null),["inventory"]) is not CharacterSnapshot { Animal: false, Appearance: {} look } c)
        {
            dragHost=null; subject=CharacterId.None; expanded=false; choice=0; return;
        }
        if(subject!=c.Id) { subject=c.Id; yaw=.12f; zoom=1f; dragHost=null; choice=0; expanded=false; Clock.Restart(); }
        float t=(float)Clock.Elapsed.TotalSeconds;
        var bar = Bar.Invoke(Editor.GetValue(__instance), null);
        CharacterPose StandingPose(string name) =>
            new(name, (float)ClipTime.Invoke(bar, [name, t])!, 1);
        IReadOnlyList<CharacterPose> poses = choice switch {
            2 => [new("sitting idle",t*.55f,1)],
            3 => [new("sleeponfloor",t*.1f,1)],
            _ => [StandingPose(look.Body?.IdleStance ?? "idle_stand_relax")] };
        if(choice==1 && Library.GetValue(__instance) is AnimationLibrary lib)
        {
            var weapon = look.Parts.FirstOrDefault(p => p.Shown == WeaponShown.Drawn)
                ?? look.Parts.FirstOrDefault(p => p.Shown == WeaponShown.BackDrawn);
            var stance = new AnimationStance { Combat = true, Left = HandHold.Empty,
                Right = weapon == null ? HandHold.Empty : HandHold.OfCategory(weapon.Record.GetInt("skill category")) };
            int guard=lib.CombatGuard(stance,0);
            if(guard>=0) poses=[poses[0],StandingPose(lib.Definitions[guard].Name)];
        }
        long key=long.MinValue+((long)(uint)c.Id.Slot<<32)+(uint)c.Id.Generation;
        list.Items.Add(new CharacterInstance(key,look,c.Position,0,poses) { Hidden=true, FullRateAnimation=true,
            Scale=c.Scale,Build=c.Build,WeaponDrawn=choice==1,BackDrawn=c.BackDrawn,UnitMovement=choice==3 });
        float distance=choice==3 ? 78 : 54*Math.Max(1,c.Scale);
        request=new(key,width,height,yaw,choice==3?.87f:.09f,distance*zoom,false,.66f,0);
        Stage.SetValue(renderer,request);
    }

    private static void Paint(object __instance)
    {
        if(request==null || !ReferenceEquals(renderer,__instance) || !Equals(Stage.GetValue(renderer),request)) return;
        try {
            Draw.Invoke(renderer,null);
            Resolve.Invoke(renderer,null);
            if(Picture.GetValue(renderer) is ValueTuple<GpuTexture,SampledTexture> image)
                Publish.Invoke(ui,[Texture,image.Item1,image.Item2]);
        } finally { Stage.SetValue(renderer,null); request=null; }
    }
}
