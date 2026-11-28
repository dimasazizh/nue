using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay.Construction;
using Meitou.Data.World;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Nue.Unified;

internal static class ContainerHighlights
{
    private const string OwnedMask = "nue:container:owned", OtherMask = "nue:container:other";
    private const string TownWorkPrefix = "townwork:";
    private static readonly Vector4 Owned = new(.27f, 1f, .23f, 1f);
    private static readonly Vector4 Other = new(1f, .23f, .19f, 1f);
    private static Type GameType(string name) => typeof(IMod).Assembly.GetType("Meitou.Game." + name, true)!;
    private static readonly Type Host = GameType("GameHost");
    private static readonly FieldInfo Session = AccessTools.Field(Host, "Session"),
        Gpu = AccessTools.Field(Host, "Gpu"),
        ScreenshotRequested = AccessTools.Field(Host, "ScreenshotRequested"),
        OutlinedBuildings = AccessTools.Field(GameType("Rendering.Frame.WorldGpu"), "OutlinedBuildings");
    private static readonly MethodInfo Covered = AccessTools.PropertyGetter(Host, "Covered"),
        HighlightHeld = AccessTools.Method(Host, "HighlightHeld");
    private static readonly Dictionary<string, bool> StorageRecords = new(StringComparer.Ordinal);
    private static UnifiedMod.Options settings = null!;
    private static GameDatabase database = null!;
    private static string? parsedText;
    private static Binding? custom;

    private sealed record Frame(object Gpu, IReadOnlyList<(string Key, float Y, Vector4 Colour)> Native,
        Dictionary<string, bool> Containers, Dictionary<string, bool> Buildings,
        Dictionary<string, List<(Vector3 Position, bool Owned)>> FixturesByRecord,
        IReadOnlySet<string> OwnedTownBuildings, Frame? Previous);
    [ThreadStatic] private static Frame? frame;

    internal static void Install(ModContext context, UnifiedMod.Options options)
    {
        settings = options;
        database = context.Data;
        var renderer = GameType("Rendering.Objects.WorldObjectRenderer");
        context.Harmony.Patch(AccessTools.Method(Host, "DrawFrame"),
            prefix: new HarmonyMethod(typeof(ContainerHighlights), nameof(BeginFrame)),
            finalizer: new HarmonyMethod(typeof(ContainerHighlights), nameof(EndFrame)));
        context.Harmony.Patch(AccessTools.Method(renderer, "SelectingKey"),
            postfix: new HarmonyMethod(typeof(ContainerHighlights), nameof(SelectContainerMask)));
    }

    private static void BeginFrame(object __instance, out Frame? __state)
    {
        __state = null;
        if (!settings.ContainerHighlights || Session.GetValue(__instance) is not WorldSession session ||
            session.Camera.IsFree || ScreenshotRequested.GetValue(__instance) is true ||
            Covered.Invoke(__instance, null) is true || !Held(__instance, session) ||
            Gpu.GetValue(__instance) is not {} gpu ||
            OutlinedBuildings.GetValue(gpu) is not IReadOnlyList<(string Key, float Y, Vector4 Colour)> native) return;

        var snapshot = session.CurrentSnapshot;
        int faction = session.World.Player.Faction;
        var containers = new Dictionary<string, bool>(StringComparer.Ordinal);
        var buildings = new Dictionary<string, bool>(StringComparer.Ordinal);
        var fixtures = new Dictionary<string, List<(Vector3 Position, bool Owned)>>(StringComparer.Ordinal);

        foreach (var container in snapshot.Containers)
        {
            bool owned = container.Owner == faction || snapshot.OwnedTownBuildings.Contains(container.Id);
            containers[container.Id] = owned;
            if (!fixtures.TryGetValue(container.Record, out var placements))
                fixtures.Add(container.Record, placements = []);
            placements.Add((container.Position, owned));
        }

        var storageSites = snapshot.Machines.Where(m => m.IsContainer ||
            m.HasStorage && m.Inputs.Count == 0 && !m.Output.HasValue && m.TechLevel == 0)
            .Select(m => m.Site).ToHashSet(StringComparer.Ordinal);
        foreach (var building in snapshot.Buildings)
        {
            if (!building.Complete || building.Destroyed) continue;
            bool owned = building.Owner == faction;
            buildings[building.Id] = owned;
            if (storageSites.Contains(building.Id)) containers[building.Id] = owned;
        }
        foreach (var site in snapshot.WorkSites)
        {
            if (!storageSites.Contains(site.Id) || !site.Id.StartsWith(TownWorkPrefix, StringComparison.Ordinal)) continue;
            string placement = site.Id[TownWorkPrefix.Length..];
            containers[placement] = site.Owner == faction || snapshot.OwnedTownBuildings.Contains(placement);
        }

        // Register one mask per colour. The renderer supplies the visible meshes for each mask below.
        var outlines = new List<(string Key, float Y, Vector4 Colour)>(native.Count + 2)
            { (OwnedMask, 0, Owned), (OtherMask, 0, Other) };
        outlines.AddRange(native);
        OutlinedBuildings.SetValue(gpu, outlines);
        __state = frame = new(gpu, native, containers, buildings, fixtures, snapshot.OwnedTownBuildings, frame);
    }

    private static void EndFrame(Frame? __state)
    {
        if (__state is not {} current) return;
        frame = current.Previous;
        OutlinedBuildings.SetValue(current.Gpu, current.Native);
    }

    private static bool Held(object host, WorldSession session)
    {
        if (HighlightHeld.Invoke(host, null) is true || session.Input.IsHeld(Binding.Of(Key.LeftAlt)) ||
            session.Input.IsHeld(Binding.Of(Key.RightAlt))) return true;
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
        if (frame is not {} current) return;
        var mesh = __0;
        if (Owns(current.Containers, mesh.PlacementId, mesh.ParentPlacementId) is {} exact)
        {
            __result = exact ? OwnedMask : OtherMask;
            return;
        }

        // Fixture IDs and renderer placement IDs can differ for furniture inside town buildings.
        var position = mesh.Transform.Translation;
        bool? fixture = NearbyFixture(current, mesh.Owner?.StringId, position) ??
            NearbyFixture(current, mesh.Source?.StringId, position);
        if (fixture is {} fixtureOwned)
        {
            __result = fixtureOwned ? OwnedMask : OtherMask;
            return;
        }
        var record = mesh.Owner ?? mesh.Source;
        if (!IsStorage(record) && (ReferenceEquals(record, mesh.Source) || !IsStorage(mesh.Source))) return;

        bool owned = Owns(current.Buildings, mesh.PlacementId, mesh.ParentPlacementId, mesh.ContainerId) ??
            (IsOwnedTown(current, mesh.PlacementId) || IsOwnedTown(current, mesh.ParentPlacementId) ||
                IsOwnedTown(current, mesh.ContainerId));
        __result = owned ? OwnedMask : OtherMask;
    }

    private static bool? Owns(Dictionary<string, bool> entries, string? key, string? parent, string? interior = null)
    {
        if (key is not null && entries.TryGetValue(key, out bool owned) ||
            parent is not null && entries.TryGetValue(parent, out owned) ||
            interior is not null && entries.TryGetValue(interior, out owned)) return owned;
        return null;
    }

    private static bool IsOwnedTown(Frame current, string? id) =>
        id is not null && current.OwnedTownBuildings.Contains(id);

    private static bool? NearbyFixture(Frame current, string? record, Vector3 position)
    {
        if (record is null || !current.FixturesByRecord.TryGetValue(record, out var fixtures)) return null;
        // Allow for a small offset between the mesh origin and the fixture's interaction point.
        float nearest = 32 * 32;
        bool? owned = null;
        foreach (var fixture in fixtures)
        {
            float distance = Vector3.DistanceSquared(position, fixture.Position);
            if (distance >= nearest) continue;
            nearest = distance;
            owned = fixture.Owned;
        }
        return owned;
    }

    private static bool IsStorage(GameRecord? record)
    {
        if (record is not { Type: FcsRecordType.Building }) return false;
        if (StorageRecords.TryGetValue(record.StringId, out bool stored)) return stored;
        var machine = MachineData.From(database, record);
        bool storage = machine is not null && (machine.IsStorage ||
            machine.HasInventory && machine.Inputs.Count == 0 && !machine.Output.HasValue && machine.TechLevel == 0);
        StorageRecords.Add(record.StringId, storage);
        return storage;
    }
}
