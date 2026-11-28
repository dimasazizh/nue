using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Meitou.Game.Host;
using Meitou.Game.Host.Input;
using Meitou.Modding;

namespace Nue.Unified;

internal static class CharacterNameReveal
{
    private static readonly Type Player = typeof(IMod).Assembly.GetType("Meitou.Game.PlayerInterface", true)!;
    private static readonly MethodInfo ShowNames = AccessTools.PropertyGetter(Player, "ShowNames");
    private static readonly FieldInfo Session = AccessTools.Field(Player, "Session");
    private static UnifiedMod.Options? options;

    internal static void Install(ModContext context, UnifiedMod.Options settings)
    {
        options = settings;
        var view = typeof(IMod).Assembly.GetType("Meitou.Game.Ui.StealthView", true)!;
        context.Harmony.Patch(AccessTools.Method(view, "AddNameTags"),
            transpiler: new HarmonyMethod(typeof(CharacterNameReveal), nameof(OverrideVisibility)));
    }

    private static IEnumerable<CodeInstruction> OverrideVisibility(IEnumerable<CodeInstruction> instructions)
    {
        var revealed = AccessTools.Method(typeof(CharacterNameReveal), nameof(NamesVisible));
        foreach (var instruction in instructions)
        {
            // Use the native name renderer and keep the saved preference intact.
            if (instruction.Calls(ShowNames))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = revealed;
            }
            yield return instruction;
        }
    }

    private static bool NamesVisible(object player)
    {
        if (ShowNames.Invoke(player, null) is true) return true;
        return options?.AltCharacterNames == true && Session.GetValue(player) is WorldSession session &&
            (session.Input.IsHeld(Binding.Of(Key.LeftAlt)) || session.Input.IsHeld(Binding.Of(Key.RightAlt)));
    }
}
