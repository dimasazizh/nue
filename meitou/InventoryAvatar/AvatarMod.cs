using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Host;
using Meitou.Game.Host.Cameras;
using Meitou.Game.Host.Input;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;

namespace Nue.InventoryAvatar;

public sealed class AvatarMod : IMod
{
    private static readonly string[] Labels = ["Idle", "Attack stance", "Sitting on mat", "Sleeping / lying"];
    private static readonly string[] Lights = ["World lighting", "Daylight", "Night"];
    private static readonly Stopwatch Clock = Stopwatch.StartNew(), Transition = new();
    private static int choice, light, menu;
    private static float yaw, zoom, lastX;
    private static bool captured, toggleOnRelease;
    private static readonly HashSet<MouseButton> blocked = [];
    private static CharacterId subject = CharacterId.None;
    private static long drawKey;
    private static CameraRig? rig;
    private static CameraRig.Saved saved;
    private static CameraState start;
    private static Type GameType(string name) => typeof(IMod).Assembly.GetType("Meitou.Game." + name, true)!;
    private static readonly Type Player = GameType("PlayerInterface"), Host = GameType("GameHost"),
        Layout = GameType("Ui.InventoryLayout"), Router = GameType("Ui.ScreenRouter");
    private static readonly FieldInfo PlayerField = AccessTools.Field(Host, "Player"),
        Screens = AccessTools.Field(Player, "FaithfulScreens"), Session = AccessTools.Field(Host,"Session"),
        Camera = AccessTools.Field(Host,"Camera"), Width = AccessTools.Field(Player,"Width"), Height = AccessTools.Field(Player,"Height"),
        Library = AccessTools.Field(Host, "AnimationLibrary"), Editor = AccessTools.Field(Host, "CharacterEditor");
    private static readonly MethodInfo GetInventory = AccessTools.PropertyGetter(Player, "InventoryWindows"),
        Owner = AccessTools.Method(GameType("Ui.InventoryWindows"), "WindowCharacter"),
        GetLayout = AccessTools.PropertyGetter(Player,"InventoryLayout"), Linked = AccessTools.Method(Layout,"InWindowRow"),
        KeyOf = AccessTools.Method(Host,"DrawKey"), Contains = AccessTools.Method(Player,"ContainsUi"),
        Picking = AccessTools.PropertyGetter(Player,"Picking"),
        Pick = AccessTools.Method(GameType("Ui.WorldPicking"),"PickWhere"),
        Bar = AccessTools.PropertyGetter(Editor.FieldType,"Bar"), ClipTime = AccessTools.Method(Bar.ReturnType,"EditorClipTime");
    private static readonly FieldInfo LayoutPlayer = Layout.GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player),
        RouterPlayer = Router.GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);

    public void Initialize(ModContext context)
    {
        void Patch(Type type,string target,string method,bool before=false)
        {
            var hook = new HarmonyMethod(typeof(AvatarMod),method);
            context.Harmony.Patch(AccessTools.Method(type,target),prefix:before?hook:null,postfix:before?null:hook);
        }
        Patch(Host,"ApplyCamera",nameof(FrameCamera),true);
        Patch(Host,"FillDrawList",nameof(Prepare));
        Patch(Layout,"PlaceCharacterWindow",nameof(UseNormalPlacement),true);
        Patch(Layout,"PlaceBagWindow",nameof(UseNormalPlacement),true);
        Patch(Layout,"BindInventoryGeometry",nameof(Place));
        Patch(Router,"AddInventoryContent",nameof(MoveGrid),true);
        Patch(Router,"AddInventoryContent",nameof(Controls));
        Patch(Router,"FaithfulAction",nameof(Click));
        Patch(Player,"MouseDown",nameof(Press),true);
        Patch(Player,"MouseUp",nameof(Release),true);
        Patch(Player,"MouseMove",nameof(Move),true);
        Patch(Player,"UiScroll",nameof(Wheel),true);
        InspectionLighting.Install(context.Harmony);
    }

    private static CharacterSnapshot? Inventory(object? player)
    {
        if (player == null || Screens.GetValue(player) is not MyGuiScreenHost screens ||
            !screens.IsOpen("inventory") || screens.IsOpen("trader") || screens.IsOpen("editor") ||
            (bool)Linked.Invoke(GetLayout.Invoke(player,null),null)!) return null;
        return Owner.Invoke(GetInventory.Invoke(player,null),["inventory"]) is CharacterSnapshot { Animal:false, Appearance:not null } c ? c : null;
    }

    private static void FrameCamera(object __instance, ref CameraState s)
    {
        var player = PlayerField.GetValue(__instance);
        var currentRig = ((WorldSession?)Session.GetValue(__instance))?.Camera;
        var c = Inventory(player);
        if (rig != null && (currentRig != rig || c == null))
        {
            if (currentRig == rig) { rig.Restore(saved); s = rig.Current; }
            rig = null;
            subject = CharacterId.None;
            captured = toggleOnRelease = false;
            menu = light = 0;
            drawKey = 0;
        }
        if (c == null || currentRig == null) return;
        if (rig == null) { rig = currentRig; saved = rig.Save(); }
        if (subject != c.Id)
        {
            subject = c.Id;
            start = s;
            drawKey = (long)KeyOf.Invoke(__instance,[subject])!;
            yaw = s.Yaw;
            zoom = 1;
            captured = toggleOnRelease = false;
            menu = choice = 0;
            Transition.Restart();
            Clock.Restart();
        }
        var world = (WorldCamera)Camera.GetValue(__instance)!;
        float aspect = (int)Width.GetValue(player)! / (float)Math.Max(1,(int)Height.GetValue(player)!);
        float pitch = choice == 3 ? .62f : .10f;
        float halfHeight = (choice == 3 ? 30 : 25) * Math.Max(.6f,c.Scale) * zoom;
        float distance = halfHeight / MathF.Tan(world.FieldOfView * .5f);
        var right = new Vector3(MathF.Cos(start.Yaw),0,-MathF.Sin(start.Yaw));
        var forward = Vector3.Normalize(-CameraState.EyeFor(Vector3.Zero,start.Yaw,pitch,1));
        var up = Vector3.Cross(right,forward);
        var pivot = c.Position + Vector3.UnitY * ((choice >= 2 ? 6 : 12) * c.Scale);
        var target = pivot + right * (.34f * halfHeight * aspect) - up * (.39f * halfHeight);
        var end = new CameraState(CameraState.EyeFor(target,start.Yaw,pitch,distance),forward,up,target,start.Yaw,pitch,distance,false);
        float t = Math.Clamp((float)Transition.Elapsed.TotalSeconds / .4f,0,1);
        s = CameraState.Lerp(start with { IsFree=false },end,t*t*(3-2*t));
    }

    private static bool UseNormalPlacement(object __instance) => Inventory(LayoutPlayer.GetValue(__instance)) == null;

    private static readonly MethodInfo CellWidth = AccessTools.PropertyGetter(Player,"InventoryCell");
    private static void Place(object __instance,string id,UiBindings b)
    {
        if (id is not ("inventory" or "bag") || !b.Positions.TryGetValue("Root",out var r)) return;
        var p = LayoutPlayer.GetValue(__instance)!;
        if (Inventory(p)?.ItemGrid is not {} grid) return;
        float w=(int)Width.GetValue(p)!, h=(int)Height.GetValue(p)!;
        var screens=(MyGuiScreenHost)Screens.GetValue(p)!;
        // The inspection layout owns the transient placement. Leave the engine's drag offset intact.
        float gridLeft = w*.33f - grid.Width*(float)CellWidth.Invoke(p,null)!*.5f;
        float x=id=="inventory" ? Math.Min(w*.57f,w-r.Width-6) : gridLeft-r.Width-6;
        float y=id=="inventory" ? h*.10f : h*.57f;
        b.WindowPosition = new Vector2(Math.Max(0,x),Math.Clamp(y,0,Math.Max(0,h-r.Height))) - screens.GetOffset(id);
    }

    private static void MoveGrid(object __instance,string id,List<UiControl> controls)
    {
        if (id!="inventory") return;
        var p=RouterPlayer.GetValue(__instance)!;
        if (Inventory(p)==null || controls.Find(c=>c.Name=="main") is not {} main) return;
        float w=(int)Width.GetValue(p)!, h=(int)Height.GetValue(p)!;
        float x=Math.Max(0,w*.33f-main.Bounds.Width*.5f);
        float y=Math.Clamp(h*.60f,0,Math.Max(0,h-main.Bounds.Height-40));
        foreach (string name in new[]{"main","InventoryMainTitle","ArrangeButton"})
        {
            int index=controls.FindIndex(c=>c.Name==name);
            if(index<0) continue;
            var control=controls[index];
            var bounds=control.Bounds with { X=x+control.Bounds.X-main.Bounds.X,
                Y=name=="ArrangeButton" ? y+main.Bounds.Height+4 : y+control.Bounds.Y-main.Bounds.Y };
            controls[index]=control with { Bounds=bounds,Clip=new UiRect(0,0,w,h) };
        }
    }

    private static void Controls(object __instance,string id,List<UiControl> controls)
    {
        if(id!="inventory" || Inventory(RouterPlayer.GetValue(__instance))==null ||
            controls.Find(c=>c.Type=="Window") is not {} window) return;
        var r=window.Bounds;
        float row=Math.Max(22,r.Height*.045f), width=r.Width*.46f;
        float x=Math.Max(0,r.X-width*2-8), y=Math.Max(2,r.Y-row*2);
        void Button(int column,int index,string caption,float top) =>
            controls.Add(new($"NueChoice{column}_{index}","Button",new(x+column*width,top,width-2,row),
                caption,$"nue.inspect.{column}.{index}","Kenshi_Button1",true,new Dictionary<string,string>(),default,Pickable:true));
        Button(0,-1,Labels[choice]+" ▾",y);
        Button(1,-1,Lights[light]+" ▾",y);
        if(menu==0) return;
        var labels=menu==1?Labels:Lights;
        for(int i=0;i<labels.Length;i++) Button(menu-1,i,labels[i],y+(i+1)*row);
    }

    private static void Click(MyGuiEvent e)
    {
        if(e.Screen!="inventory" || !e.Action.StartsWith("nue.inspect.",StringComparison.Ordinal)) return;
        var parts=e.Action.Split('.');
        if(parts.Length!=4 || !int.TryParse(parts[2],out int column) || !int.TryParse(parts[3],out int i)) return;
        if(i==-1) menu=menu==column+1?0:column+1;
        else
        {
            if(column==0 && (uint)i<Labels.Length) { choice=i; Clock.Restart(); }
            if(column==1 && (uint)i<Lights.Length) light=i;
            menu=0;
        }
    }

    private static bool OverUi(object player,Vector2 p) => (bool)Contains.Invoke(player,[p])!;
    private static readonly Func<CharacterSnapshot,bool> AnyCharacter = _=>true;
    private static bool Press(object __instance,MouseButton button,Vector2 p,ref bool __result)
    {
        blocked.Remove(button);
        if(Inventory(__instance)==null || (!captured && OverUi(__instance,p))) return true;
        blocked.Add(button);
        if(button==MouseButton.Left)
            toggleOnRelease=captured || Equals(Pick.Invoke(Picking.Invoke(__instance,null),[p,AnyCharacter]),subject);
        __result=true;
        return false;
    }

    private static bool Release(MouseButton button,Vector2 p)
    {
        if(!blocked.Remove(button)) return true;
        if(button==MouseButton.Left && toggleOnRelease)
        {
            captured=!captured;
            lastX=p.X;
            toggleOnRelease=false;
        }
        return false;
    }

    private static bool Move(object __instance,Vector2 p)
    {
        if(!captured) return true;
        yaw += (p.X-lastX)*MathF.Tau/Math.Max(1,(int)Width.GetValue(__instance)!*.45f);
        lastX=p.X;
        return false;
    }

    private static bool Wheel(object __instance,Vector2 p,float steps,ref bool __result)
    {
        if(Inventory(__instance)==null || (!captured && OverUi(__instance,p))) return true;
        zoom=Math.Clamp(zoom*MathF.Pow(.87f,steps),.55f,1.8f);
        __result=true;
        return false;
    }

    internal static uint InspectionMark(uint original,long key) =>
        rig!=null && key==drawKey && light!=0 ? (uint)(light<<7) : original;

    private static void Prepare(object __instance, CharacterDrawList list)
    {
        if(rig==null || Inventory(PlayerField.GetValue(__instance)) is not {Appearance:{} look} c || c.Id!=subject) return;
        int drawIndex=list.Items.FindIndex(item=>item.Key==drawKey);
        if(drawIndex<0) return;
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
        list.Items[drawIndex]=list.Items[drawIndex] with { Yaw=yaw, Poses=poses, FullRateAnimation=true,
            WeaponDrawn=drawn, BackDrawn=backDrawn, RangedDrawn=false, UnitMovement=choice==3 };
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

}
