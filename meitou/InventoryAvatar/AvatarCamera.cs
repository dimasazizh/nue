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

    // Only the mod's transparent inventory stage gets this camera. Freeze the measured
    // pose envelope across animation ticks so breathing cannot move the camera.
    private static void FrameAvatar(object __instance, in EditorStageRequest r, ref (Vector3 Eye, Vector3 Pivot) __result)
    {
        if (active.request != r || !ReferenceEquals(renderer,__instance)) return;
        int index = (int)StageFields.Index.Invoke(__instance,[r.Key])!;
        if (index < 0) return;
        var live = ((Array)StageFields.Instances.GetValue(__instance)!).GetValue(index)!;
        var world = (Matrix4x4)StageFields.World.GetValue(live)!;
        var asset = StageFields.Asset.GetValue(live)!;
        if (active.framePoints == null || active.framedKey != r.Key || active.framedPose != active.choice || !ReferenceEquals(asset,active.framedAsset))
        {
            var state = StageFields.State.GetValue(live);
            var rig = (SkeletonRig)StageFields.Rig.GetValue(asset)!;
            if (state == null || StageFields.Bones.GetValue(state) is not Matrix4x4[] bones) return;
            var points = new List<Vector3>();
            foreach (var name in FrameBones)
                if (rig.Handle(name) is int h && h < bones.Length)
                    points.Add(Vector3.TransformNormal(Vector3.Transform(rig.BindPosition[h],bones[h]),world));
            if (points.Count < 3 || Vector3.DistanceSquared(points[0],points[^1]) < 1) return;
            float stature = Math.Max(1, (rig.HeadBone >= 0 ? rig.BindPosition[rig.HeadBone].Length() : 20)
                * Vector3.TransformNormal(Vector3.UnitY,world).Length());
            active.framePadding = stature * .055f;
            // The head joint is inside the skull. Extend the posed neck-to-head axis to frame the crown.
            active.eyeHeight = points.Max(p => p.Y);
            if (rig.Handle("Bip01 Head") is int head && head < bones.Length &&
                rig.Handle("Bip01 Neck") is int neck && neck < bones.Length)
            {
                var headPoint = Vector3.TransformNormal(Vector3.Transform(rig.BindPosition[head], bones[head]), world);
                var neckPoint = Vector3.TransformNormal(Vector3.Transform(rig.BindPosition[neck], bones[neck]), world);
                var neckToHead = headPoint - neckPoint;
                var crown = neckToHead.LengthSquared() > 1e-4f ? Vector3.Normalize(neckToHead) : Vector3.UnitY;
                points.Add(headPoint + crown * (stature * .12f));
                active.eyeHeight = headPoint.Y + neckToHead.Y * .35f;
            }
            active.framePoints = points.ToArray();
            active.framedPose = active.choice;
            active.framedKey = r.Key;
            active.framedAsset = asset;
        }
        var orbit = __result.Eye - __result.Pivot;
        var forward = Vector3.Normalize(new Vector3(orbit.X,0,orbit.Z));
        var right = Vector3.Cross(Vector3.UnitY, forward);
        var frame = active.framePoints;
        var lo = frame.Aggregate(Vector3.Min);
        var hi = frame.Aggregate(Vector3.Max);
        var centre = (lo + hi) / 2;
        float tanY = MathF.Tan(r.FieldOfView/2), tanX = tanY*r.Width/r.Height;
        float levelDistance = Math.Max(1, FitDistance(forward, .87f));
        float tilt = active.choice == 3 ? r.Elevation :
            Math.Clamp(MathF.Atan2(active.eyeHeight - centre.Y, levelDistance), -.5f, .5f);
        var direction = forward * MathF.Cos(tilt) + Vector3.UnitY * MathF.Sin(tilt);
        // Fit with the actual camera angle. Keep clearance even at the closest zoom.
        float distance = Math.Max(FitDistance(direction, .96f), FitDistance(direction, .87f) * active.zoom);
        var pivot = world.Translation + centre;
        __result = (pivot + direction * distance, pivot);

        float FitDistance(Vector3 viewDirection, float frameFraction)
        {
            var up = Vector3.Cross(viewDirection, right);
            float fit = 0;
            foreach (var point in frame)
            {
                var delta = point - centre;
                float horizontal = (Math.Abs(Vector3.Dot(delta, right)) + active.framePadding) / (tanX * frameFraction);
                float vertical = (Math.Abs(Vector3.Dot(delta, up)) + active.framePadding) / (tanY * frameFraction);
                fit = Math.Max(fit, Vector3.Dot(delta, viewDirection) + Math.Max(horizontal, vertical));
            }
            return fit;
        }
    }
}
