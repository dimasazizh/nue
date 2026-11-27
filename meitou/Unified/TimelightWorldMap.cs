using System.Numerics;
using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Rendering.Ui;
using Meitou.Modding;

namespace Nue.Unified;

internal static class TimelightWorldMap
{
    private const string Owner = "nue.unified";
    private const string TextureName = "nue:timelight-world-map";

    private delegate bool SetMode(string owner, bool enabled);
    private delegate bool RequestFrame(string owner, Vector4 worldBounds, int width, int height);
    private delegate bool GetFrame(string owner, out Vector4 worldBounds, out GpuTexture texture,
        out SampledTexture sampled, out bool flipV);

    private static RequestFrame? request;
    private static GetFrame? getFrame;
    private static Action<string>? clearRequest;
    private static SetMode? setMode;
    private static FieldInfo player = null!, painterField = null!;
    private static MethodInfo mapTabShown = null!;
    private static float worldExtent;
    private static bool requested;
    private static Action<string>? log;

    internal static void Install(ModContext context)
    {
        var api = AccessTools.TypeByName("TimeLightMap.Embedding");
        if (api is null) return;
        try
        {
            if (api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is not 1)
                return;
            var mode = Bind<SetMode>(api, "TrySetEmbeddedMode");
            request = Bind<RequestFrame>(api, "TryRequestFrame");
            getFrame = Bind<GetFrame>(api, "TryGetFrame");
            clearRequest = Bind<Action<string>>(api, "ClearRequest");
            if (mode is null || request is null || getFrame is null || clearRequest is null) return;

            var map = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.WorldMap", true)!;
            player = AccessTools.Field(map, "<player>P");
            painterField = AccessTools.Field(player.FieldType, "UiPainter");
            mapTabShown = AccessTools.PropertyGetter(map, "MapTabShown");
            worldExtent = Convert.ToSingle(AccessTools.Field(map, "MapExtent").GetRawConstantValue());
            if (worldExtent <= 0 || !mode(Owner, true)) return;
            setMode = mode;
            log = message => context.Log(message);
            context.Harmony.Patch(AccessTools.Method(map, "AddMapContent"),
                postfix: new HarmonyMethod(typeof(TimelightWorldMap), nameof(PlaceFrame)));
            context.Harmony.Patch(AccessTools.Method(map, "UpdateMap"),
                postfix: new HarmonyMethod(typeof(TimelightWorldMap), nameof(ClearOnClose)));
        }
        catch (Exception error)
        {
            Stop(error);
            if (log is null) context.Log($"Timelight map embedding unavailable: {error.GetBaseException().Message}");
        }
    }

    private static T? Bind<T>(Type api, string name) where T : Delegate =>
        api.GetMethod(name, BindingFlags.Public | BindingFlags.Static)?.CreateDelegate<T>();

    private static void PlaceFrame(object __instance, string id, List<UiControl> controls)
    {
        if (setMode is null || id != "overview" || mapTabShown.Invoke(__instance, null) is not true) return;
        int index = controls.FindIndex(control => control.Name == "MapImage");
        try
        {
            if (index < 0) { Clear(); return; }
            var map = controls[index];
            var visible = map.Bounds.Intersect(map.Clip ?? map.Bounds);
            if (visible.Width < 16 || visible.Height < 16) { Clear(); return; }
            float worldWidth = 2 * worldExtent;
            float ToWorld(float point, float origin, float size) => (point - origin) / size * worldWidth - worldExtent;
            var wanted = new Vector4(ToWorld(visible.X, map.Bounds.X, map.Bounds.Width),
                ToWorld(visible.Y, map.Bounds.Y, map.Bounds.Height),
                ToWorld(visible.X + visible.Width, map.Bounds.X, map.Bounds.Width),
                ToWorld(visible.Y + visible.Height, map.Bounds.Y, map.Bounds.Height));
            if (!request!(Owner, wanted, (int)MathF.Round(visible.Width), (int)MathF.Round(visible.Height))) { Clear(); return; }
            requested = true;
            if (!getFrame!(Owner, out var rendered, out var texture, out var sampled, out bool flipV) ||
                !float.IsFinite(rendered.X) || !float.IsFinite(rendered.Y) ||
                !float.IsFinite(rendered.Z) || !float.IsFinite(rendered.W) ||
                rendered.Z <= rendered.X || rendered.W <= rendered.Y) return;

            // Reposition a completed frame using its rendered bounds so panning never moves the live image away from its map markers.
            var bounds = new UiRect(
                map.Bounds.X + (rendered.X + worldExtent) / worldWidth * map.Bounds.Width,
                map.Bounds.Y + (rendered.Y + worldExtent) / worldWidth * map.Bounds.Height,
                (rendered.Z - rendered.X) / worldWidth * map.Bounds.Width,
                (rendered.W - rendered.Y) / worldWidth * map.Bounds.Height);
            var clip = visible.Intersect(bounds);
            if (clip.Width <= 0 || clip.Height <= 0 ||
                painterField.GetValue(player.GetValue(__instance)) is not UiPainter painter) return;
            painter.SetTexture(TextureName, texture, sampled);
            controls.Insert(index + 1, new(TextureName, "ImageBox", bounds, "", "", "", true,
                new Dictionary<string, string> { ["ImageTexture"] = TextureName, ["ImageFlipV"] = flipV ? "true" : "false" },
                default, Pickable: false, Clip: clip));
        }
        catch (Exception error)
        {
            Stop(error);
        }
    }

    private static void ClearOnClose(object __instance)
    {
        if (setMode is null || mapTabShown.Invoke(__instance, null) is true) return;
        try { Clear(); }
        catch (Exception error) { Stop(error); }
    }

    private static void Clear()
    {
        if (!requested) return;
        requested = false;
        clearRequest!(Owner);
    }

    private static void Stop(Exception error)
    {
        var release = setMode;
        setMode = null;
        try { Clear(); } catch (Exception) { }
        try { release?.Invoke(Owner, false); } catch (Exception) { }
        log?.Invoke($"Timelight map embedding unavailable: {error.GetBaseException().Message}");
    }
}
