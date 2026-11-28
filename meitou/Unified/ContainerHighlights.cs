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
    private const string FixturePrefix = "nue:container:";
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
        CameraTarget = AccessTools.PropertyGetter(Camera.FieldType, "Target");
    private static UnifiedMod.Options settings = null!;
    private static string? parsedText;
    private static Binding? custom;

    internal static void Install(ModContext context, UnifiedMod.Options options)
    {
        settings = options;
        context.Harmony.Patch(AccessTools.Method(Loop, "Outline"),
            transpiler: new HarmonyMethod(typeof(ContainerHighlights), nameof(AddOutlineTargets)));
        context.Harmony.Patch(AccessTools.Method(GameType("Rendering.Objects.WorldObjectRenderer"), "Selects"),
            postfix: new HarmonyMethod(typeof(ContainerHighlights), nameof(MatchFixture)));
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
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ContainerHighlights), nameof(Targets)));
            }
            yield return instruction;
        }
    }

    private static IReadOnlyList<(string Key, float Y, Vector4 Colour)> Targets(
        IReadOnlyList<(string Key, float Y, Vector4 Colour)> native, object loop, bool visible, bool highlighting)
    {
        if (!visible || !settings.ContainerHighlights || LoopHost.GetValue(loop) is not {} host ||
            Session.GetValue(host) is not WorldSession session) return native;
        if (!highlighting && (Focused.GetValue(loop) is not true || Covered.Invoke(host, null) is true ||
            !CustomHeld(session))) return native;
        if (CameraTarget.Invoke(Camera.GetValue(host), null) is not Vector3 centre) return native;

        var snapshot = session.CurrentSnapshot;
        int faction = session.World.Player.Faction;
        var result = new List<(string Key, float Y, Vector4 Colour)>();
        var included = new HashSet<string>(StringComparer.Ordinal);
        void Add(string key, Vector3 position, int owner, bool fixture = false)
        {
            if (string.IsNullOrEmpty(key) || Vector3.DistanceSquared(centre, position) > RangeSquared ||
                !included.Add(key)) return;
            result.Add((fixture ? FixturePrefix + key : key, position.Y, owner == faction ? Owned : Other));
        }

        foreach (var container in snapshot.Containers)
            Add(container.Id, container.Position, container.Owner, fixture: true);

        var storageSites = snapshot.Machines.Where(m => m.IsContainer ||
            m.HasStorage && m.Inputs.Count == 0 && !m.Output.HasValue && m.TechLevel == 0)
            .Select(m => m.Site).ToHashSet(StringComparer.Ordinal);
        foreach (var building in snapshot.Buildings)
            if (building.Complete && !building.Destroyed && storageSites.Contains(building.Id))
                Add(building.Id, building.Position, building.Owner);

        if (result.Count == 0) return native;
        foreach (var outline in native)
            if (!included.Contains(outline.Key)) result.Add(outline);
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

    private static void MatchFixture(PlacedMesh p, string key, ref bool __result)
    {
        if (__result || !key.StartsWith(FixturePrefix, StringComparison.Ordinal)) return;
        var id = key.AsSpan(FixturePrefix.Length);
        __result = id.SequenceEqual(p.ContainerId.AsSpan()) || id.SequenceEqual(p.PlacementId.AsSpan()) ||
            id.SequenceEqual(p.ParentPlacementId.AsSpan());
    }
}
