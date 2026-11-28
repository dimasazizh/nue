using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Data.World;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Nue.Unified;

internal static class ContainerHighlights
{
    private const string OwnedMask = "nue:container:owned", OtherMask = "nue:container:other";
    private const string TownWorkPrefix = "townwork:";
    private const float RangeSquared = 600f * 600f;
    private static readonly Vector4 Owned = new(.27f, 1f, .23f, 1f);
    private static readonly Vector4 Other = new(1f, .23f, .19f, 1f);
    private static Type GameType(string name) => typeof(IMod).Assembly.GetType("Meitou.Game." + name, true)!;
    private static readonly Type Loop = GameType("Host.GameLoop"), Host = GameType("GameHost");
    private static readonly FieldInfo LoopHost = AccessTools.Field(Loop, "<host>P"),
        Focused = AccessTools.Field(Loop, "_windowFocused"),
        Session = AccessTools.Field(Host, "Session"),
        Camera = AccessTools.Field(Host, "Camera"),
        OutlinedBuildings = AccessTools.Field(GameType("Rendering.Frame.WorldGpu"), "OutlinedBuildings");
    private static readonly MethodInfo Covered = AccessTools.PropertyGetter(Host, "Covered"),
        HighlightHeld = AccessTools.Method(Host, "HighlightHeld"),
        CameraTarget = AccessTools.PropertyGetter(Camera.FieldType, "Target");
    private static UnifiedMod.Options settings = null!;
    private static string? parsedText;
    private static Binding? custom;
    private static readonly Dictionary<string, string> MasksByPlacement = new(StringComparer.Ordinal);

    internal static void Install(ModContext context, UnifiedMod.Options options)
    {
        settings = options;
        context.Harmony.Patch(AccessTools.Method(Loop, "Outline"),
            transpiler: new HarmonyMethod(typeof(ContainerHighlights), nameof(AddOutlineTargets)));
        context.Harmony.Patch(AccessTools.Method(GameType("Rendering.Objects.WorldObjectRenderer"), "SelectingKey"),
            postfix: new HarmonyMethod(typeof(ContainerHighlights), nameof(SelectContainerMask)));
    }

    private static IEnumerable<CodeInstruction> AddOutlineTargets(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.StoresField(OutlinedBuildings))
            {
                // The native visibility gate excludes screenshots, free camera and clean UI.
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldloc_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ContainerHighlights), nameof(Targets)));
            }
            yield return instruction;
        }
    }

    private static IReadOnlyList<(string Key, float Y, Vector4 Colour)> Targets(
        IReadOnlyList<(string Key, float Y, Vector4 Colour)> native, object loop, bool visible)
    {
        MasksByPlacement.Clear();
        if (!visible || !settings.ContainerHighlights || LoopHost.GetValue(loop) is not {} host ||
            Session.GetValue(host) is not WorldSession session || Focused.GetValue(loop) is not true ||
            Covered.Invoke(host, null) is true) return native;
        // The native highlight argument is sampled on simulation ticks. Poll held keys on the render frame too.
        if (HighlightHeld.Invoke(host, null) is not true &&
            !session.Input.IsHeld(Binding.Of(Key.LeftAlt)) && !session.Input.IsHeld(Binding.Of(Key.RightAlt)) &&
            !CustomHeld(session)) return native;
        if (CameraTarget.Invoke(Camera.GetValue(host), null) is not Vector3 centre) return native;

        var snapshot = session.CurrentSnapshot;
        int faction = session.World.Player.Faction;
        bool hasOwned = false, hasOther = false;
        void Add(string key, Vector3 position, int owner)
        {
            float dx = centre.X - position.X, dz = centre.Z - position.Z;
            if (string.IsNullOrEmpty(key) || dx * dx + dz * dz > RangeSquared) return;
            bool owned = owner == faction || snapshot.OwnedTownBuildings.Contains(key);
            if (!MasksByPlacement.TryAdd(key, owned ? OwnedMask : OtherMask)) return;
            hasOwned |= owned;
            hasOther |= !owned;
        }

        foreach (var container in snapshot.Containers)
            Add(container.Id, container.Position, container.Owner);

        var storageSites = snapshot.Machines.Where(m => m.IsContainer ||
            m.HasStorage && m.Inputs.Count == 0 && !m.Output.HasValue && m.TechLevel == 0)
            .Select(m => m.Site).ToHashSet(StringComparer.Ordinal);
        foreach (var building in snapshot.Buildings)
            if (building.Complete && !building.Destroyed && storageSites.Contains(building.Id))
                Add(building.Id, building.Position, building.Owner);
        foreach (var site in snapshot.WorkSites)
        {
            if (!storageSites.Contains(site.Id) || !site.Id.StartsWith(TownWorkPrefix, StringComparison.Ordinal)) continue;
            Add(site.Id[TownWorkPrefix.Length..], site.Position, site.Owner);
        }

        if (MasksByPlacement.Count == 0) return native;
        // Batch nearby containers by ownership through the native object mask pass.
        var result = new List<(string Key, float Y, Vector4 Colour)>(native.Count + 2);
        if (hasOwned) result.Add((OwnedMask, centre.Y, Owned));
        if (hasOther) result.Add((OtherMask, centre.Y, Other));
        foreach (var outline in native)
            if (!MasksByPlacement.ContainsKey(outline.Key)) result.Add(outline);
        return result;
    }

    private static bool CustomHeld(WorldSession session)
    {
        string text = settings.ContainerHighlightKey?.Trim() ?? "";
        if (text != parsedText)
        {
            parsedText = text;
            custom = Binding.TryParse(text, out var binding) && !binding.IsMouse ? binding : null;
        }
        return custom is {} key && session.Input.IsHeld(key);
    }

    private static void SelectContainerMask(PlacedMesh __0, ref string? __result)
    {
        if (MasksByPlacement.Count == 0) return;
        // ContainerId identifies the enclosing interior on furniture meshes. Match the object placement itself.
        if (__0.PlacementId is {} placement && MasksByPlacement.TryGetValue(placement, out var mask) ||
            __0.ParentPlacementId is {} parent && MasksByPlacement.TryGetValue(parent, out mask))
            __result = mask;
    }
}
