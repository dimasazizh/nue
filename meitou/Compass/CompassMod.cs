using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Gameplay;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation.Player;
using Meitou.Simulation.Population;

namespace Nue.Compass;

public sealed class CompassMod : IMod
{
    const float Reach = 4608; // One world zone.
    const float HalfArc = MathF.PI / 3;
    static readonly Type Player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
    static readonly FieldInfo Session = AccessTools.Field(Player,"Session");
    static readonly FieldInfo Width = AccessTools.Field(Player,"Width"), Height = AccessTools.Field(Player,"Height");
    static readonly PropertyInfo Map = AccessTools.Property(Player,"WorldMap");
    static readonly PropertyInfo Towns = AccessTools.Property(Map.PropertyType,"MapTowns");
    static readonly string[] Directions = ["N","NE","E","SE","S","SW","W","NW"];
    static readonly Dictionary<string,string> Plain = new();
    static readonly Dictionary<string,string> Face = new() { ["TextAlign"]="Center", ["FontName"]="Kenshi_PaintedTextFont_Medium" };
    static readonly Vector4 Gold = new(.93f,.77f,.49f,1);
    public void Initialize(ModContext context) =>
        context.Harmony.Patch(AccessTools.Method(Player,"AddFrameControls"),prefix:new HarmonyMethod(typeof(CompassMod),nameof(Draw)));

    static float Offset(float bearing,float heading) => MathF.IEEERemainder(bearing-heading, MathF.Tau);

    static void Draw(object __instance,List<UiControl> controls,bool hud)
    {
        if (!hud) return;
        var session = (WorldSession)Session.GetValue(__instance)!;
        var camera = session.CameraAt();
        float heading = -camera.Yaw;
        var origin = camera.IsFree ? camera.Eye : camera.Target;
        var snapshot = session.CurrentSnapshot;
        var provider = (Func<IReadOnlyList<TownSite>>?)Towns.GetValue(Map.GetValue(__instance));
        List<(float Angle,float Distance,string Icon)> places = [];
        void Mark(Vector3 position,string icon)
        {
            float dx=position.X-origin.X, dz=position.Z-origin.Z;
            float distance=dx*dx+dz*dz;
            if(distance<1 || distance>Reach*Reach) return;
            float angle=Offset(MathF.Atan2(dx,-dz),heading);
            if(MathF.Abs(angle)<=HalfArc) places.Add((angle,distance,icon));
        }
        if(provider?.Invoke() is {} towns)
            for(int i=0;i<Math.Min(towns.Count,snapshot.Towns.Count);i++)
            {
                if(!snapshot.Towns[i].HasFlag(TownKnowledge.Discovered)) continue;
                var type=towns[i].Town.Type;
                if(type is TownType.NestMarker or TownType.Null) continue;
                Mark(towns[i].Position,type switch {
                    TownType.Town=>"Town", TownType.Village=>"Village", TownType.Ruins=>"Ruin",
                    TownType.Outpost or TownType.Prison or TownType.Military=>"Outpost",
                    TownType.SlaveCamp=>"Workcamp", TownType.Nest=>"Nest", _=>"SmallPlace" });
            }
        foreach(var camp in snapshot.Camps)
            if(camp.Known.HasFlag(TownKnowledge.Discovered) && camp.Position is {} pos) Mark(pos,"Camp");
        foreach(var outpost in snapshot.Outposts) Mark(outpost.Position,"Outpost");
        int width=(int)Width.GetValue(__instance)!, height=(int)Height.GetValue(__instance)!;
        controls.InsertRange(0,Strip(width,height,heading,places));
    }

    static List<UiControl> Strip(int width,int height,float heading,List<(float Angle,float Distance,string Icon)> places)
    {
        List<UiControl> result=[];
        float scale=Math.Clamp(height/1080f,.7f,2f), span=Math.Min(width*.54f,490*scale), mid=width*.5f;
        var box=new UiRect(mid-span/2,4*scale,span,30*scale);
        float X(float angle)=>mid+angle/HalfArc*span/2;
        void Rect(string name,UiRect area,Vector4 colour)=>result.Add(new(name,"Widget",area,"","","",true,Plain,colour,Pickable:false,Clip:box));
        Rect("NueCompass",box,new(.075f,.063f,.054f,.82f));
        for(int i=0;i<48;i++)
        {
            float angle=Offset(i*MathF.Tau/48,heading);
            if(MathF.Abs(angle)>HalfArc) continue;
            float x=X(angle);
            if(i%6==0)
                result.Add(new("NueCompassDirection"+i,"TextBox",new(x-16*scale,box.Y,32*scale,16*scale),
                    Directions[i/6],"","",true,Face,default,Gold,TextScale:scale,Pickable:false,Clip:box));
            else Rect("NueCompassTick"+i,new(x,box.Y+8*scale,scale,4*scale),Gold);
        }
        Rect("NueCompassIndex",new(mid-scale,box.Y+15*scale,2*scale,14*scale),Gold);
        List<float> occupied=[];
        foreach(var place in places.OrderBy(p=>p.Distance))
        {
            float x=X(place.Angle);
            if(occupied.Any(other=>Math.Abs(other-x)<16*scale)) continue;
            occupied.Add(x);
            var image=new Dictionary<string,string>{{"ImageResource","Kenshi_MapMarkers"},{"ImageGroup",place.Icon},{"ImageName",place.Icon}};
            result.Add(new("NueCompassPlace"+occupied.Count,"ImageBox",new(x-7*scale,box.Y+15*scale,14*scale,14*scale),
                "","","",true,image,default,Pickable:false,Clip:box));
        }
        return result;
    }
}
