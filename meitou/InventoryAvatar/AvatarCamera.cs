using System.Numerics;
using HarmonyLib;
using Meitou.Data.Characters;
using Meitou.Game.Rendering.Characters;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod
{
    private static readonly string[] FrameBones = ["Bip01 Head","Bip01 Neck","Bip01 Pelvis",
        "Bip01 L Hand","Bip01 R Hand","Bip01 L UpperArm","Bip01 R UpperArm",
        "Bip01 L Calf","Bip01 R Calf","Bip01 L Foot","Bip01 R Foot","Bip01 L Toe0","Bip01 R Toe0"];
    private static class StageFields
    {
        internal static readonly System.Reflection.MethodInfo Index = AccessTools.Method(Render, "StageLiveIndex");
        internal static readonly System.Reflection.FieldInfo Instances = AccessTools.Field(Render, "_live");
        private static readonly Type Live = Instances.FieldType.GetElementType()!;
        internal static readonly System.Reflection.FieldInfo World = AccessTools.Field(Live, "World"),
            Asset = AccessTools.Field(Live, "Asset"), State = AccessTools.Field(Live, "State"),
            Rig = AccessTools.Field(Asset.FieldType, "Rig"), Bones = AccessTools.Field(State.FieldType, "Bones");
    }
    private static Vector3[]? framePoints;
    private static float framePadding;
    private static object? framedAsset;
    private static int framedPose = -1;
    private static long framedKey;

    // Only the mod's transparent inventory stage gets this camera. Freeze the measured
    // pose envelope across animation ticks so breathing cannot move the camera.
    private static void FrameAvatar(object __instance, in EditorStageRequest r, ref (Vector3 Eye, Vector3 Pivot) __result)
    {
        if (request is not {} active || active != r || !ReferenceEquals(renderer,__instance)) return;
        int index = (int)StageFields.Index.Invoke(__instance,[r.Key])!;
        if (index < 0) return;
        var live = ((Array)StageFields.Instances.GetValue(__instance)!).GetValue(index)!;
        var world = (Matrix4x4)StageFields.World.GetValue(live)!;
        var asset = StageFields.Asset.GetValue(live)!;
        if (framePoints == null || framedKey != r.Key || framedPose != choice || !ReferenceEquals(asset,framedAsset))
        {
            var state = StageFields.State.GetValue(live);
            var rig = (SkeletonRig)StageFields.Rig.GetValue(asset)!;
            if (state == null || StageFields.Bones.GetValue(state) is not Matrix4x4[] bones) return;
            var points = new List<Vector3>();
            foreach (var name in FrameBones)
                if (rig.Handle(name) is int h && h < bones.Length)
                    points.Add(Vector3.TransformNormal(Vector3.Transform(rig.BindPosition[h],bones[h]),world));
            if (points.Count < 3 || Vector3.DistanceSquared(points[0],points[^1]) < 1) return;
            framePoints = points.ToArray();
            float stature = Math.Max(1, (rig.HeadBone >= 0 ? rig.BindPosition[rig.HeadBone].Length() : 20)
                * Vector3.TransformNormal(Vector3.UnitY,world).Length());
            framePadding = stature * .095f;
            framedPose = choice;
            framedKey = r.Key;
            framedAsset = asset;
        }
        var orbit = __result.Eye - __result.Pivot;
        var forward = Vector3.Normalize(new Vector3(orbit.X,0,orbit.Z));
        var right = Vector3.Cross(Vector3.UnitY, forward);
        var lo = framePoints.Aggregate(Vector3.Min);
        var hi = framePoints.Aggregate(Vector3.Max);
        var centre = (lo + hi) / 2 + Vector3.UnitY * framePadding * .28f;
        float tanY = MathF.Tan(r.FieldOfView/2), tanX = tanY*r.Width/r.Height;
        float tilt = choice == 3 ? .45f : .20f;
        float distance = 0;
        Vector3 direction = default;
        for (int pass = 0; pass < 2; pass++)
        {
            direction = forward*MathF.Cos(tilt) + Vector3.UnitY*MathF.Sin(tilt);
            var up = Vector3.Cross(direction,right);
            distance = 0;
            foreach (var point in framePoints)
            {
                var delta = point-centre;
                float horizontal = (Math.Abs(Vector3.Dot(delta,right)) + framePadding) / tanX;
                float vertical = (Math.Abs(Vector3.Dot(delta,up)) + framePadding) / tanY;
                distance = Math.Max(distance, Vector3.Dot(delta,direction)+Math.Max(horizontal,vertical));
            }
            if (pass == 0 && choice != 3)
                tilt = MathF.Asin(Math.Clamp((hi.Y-centre.Y)*.80f / Math.Max(1,distance),.04f,.35f));
        }
        var pivot = world.Translation + centre;
        __result = (pivot + direction * (distance * zoom), pivot);
    }
}
