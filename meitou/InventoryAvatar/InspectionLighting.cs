using HarmonyLib;
using Meitou.Modding;
using System.Reflection.Emit;

namespace Nue.InventoryAvatar;

internal static class InspectionLighting
{
    internal static void Install(Harmony harmony)
    {
        var game = typeof(IMod).Assembly;
        var shaders = game.GetType("Meitou.Game.Rendering.Characters.CharacterShaders", true)!;
        var renderer = game.GetType("Meitou.Game.Rendering.Characters.CharacterRenderer", true)!;
        harmony.Patch(AccessTools.Method(renderer,"Soaked"), transpiler: Hook(nameof(MarkSubject)));
        harmony.Patch(AccessTools.Method(shaders,"VertexBody"), postfix: Hook(nameof(Vertex)));
        harmony.Patch(AccessTools.Method(shaders,"FragmentBody"), postfix: Hook(nameof(Fragment)));
    }

    private static HarmonyMethod Hook(string name) => new(typeof(InspectionLighting),name);

    // The nightly's SoakBits never emits a nonzero strength with zero height.
    // Use two such values for this frame only, after the renderer updates its ordinary wetness state.
    private static IEnumerable<CodeInstruction> MarkSubject(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var op in instructions)
        {
            yield return op;
            if(op.opcode!=OpCodes.Call || op.operand is not System.Reflection.MethodInfo {Name:"SoakBits"} pack) continue;
            var live=AccessTools.Inner(pack.DeclaringType,"Live");
            yield return new(OpCodes.Ldarg_1);
            yield return new(OpCodes.Ldfld,AccessTools.Field(live,"Key"));
            yield return new(OpCodes.Call,AccessTools.Method(typeof(AvatarMod),nameof(AvatarMod.InspectionMark)));
        }
    }

    private static void Vertex(ref string __result)
    {
        const string end="float(soak >> 6) / 31.0;";
        __result=__result.Replace(end,end+"\nif (soak == 64u) vSoak = -1.0; else if (soak == 128u) vSoak = -2.0;");
    }

    private static void Fragment(ref string __result)
    {
        const string output="fragColour = vec4(colour,";
        __result=__result.Replace(", vSoak);",", max(vSoak, 0.0));").Replace(output,"""
            if (view.fogDistance > 0.0 && vSoak < -0.5) {
                bool day = vSoak > -1.5;
                vec3 key = normalize(v + vec3(-0.45, 0.8, 0.1));
                vec3 fill = day ? vec3(0.42, 0.43, 0.46) : vec3(0.075, 0.10, 0.17);
                vec3 sun = day ? vec3(1.1, 0.97, 0.78) : vec3(0.21, 0.30, 0.50);
                colour = albedo * (fill + sun * max(dot(n, key), 0.0))
                    + sun * gloss * pow(max(dot(n, normalize(key + v)), 0.0), 24.0) * 0.3;
            }
            """+"\n"+output);
    }
}
