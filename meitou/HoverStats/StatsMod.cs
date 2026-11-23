using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Host;
using Meitou.Modding;
using Meitou.Simulation;

namespace Nue.HoverStats;

public sealed class StatsMod : IMod
{
    static Type Game(string name) => typeof(IMod).Assembly.GetType("Meitou.Game."+name,true)!;
    static readonly Type Player = Game("PlayerInterface");
    static readonly MethodInfo Lead = AccessTools.Method(Player,"Lead");
    static readonly FieldInfo Session = AccessTools.Field(Player,"Session");
    static readonly Type Stats = Game("Ui.StatsWindow");
    static readonly FieldInfo StatsPlayer = Stats.GetFields(AccessTools.allDeclared).Single(f => f.FieldType == Player);
    static readonly MethodInfo Selected = AccessTools.Method(Player,"Selected");
    static CharacterId? hovered;

    public void Initialize(ModContext context)
    {
        void Rewrite(Type type,string method,string patch) => context.Harmony.Patch(AccessTools.Method(type,method),
            transpiler:new HarmonyMethod(typeof(StatsMod),patch));
        Rewrite(Game("Ui.PointerView"),"Point",nameof(ObservePointer));
        Rewrite(typeof(World),"Viewed",nameof(PublishHoveredDetails));
        foreach (var (type,method) in new[]{(Player,"BindUi"),(Game("Ui.HudPanels"),"AddHudText"),(Game("Ui.TooltipRuntime"),"BindTooltips")})
            Rewrite(type,method,nameof(ReadDisplayLead));
        context.Harmony.Patch(AccessTools.Method(Stats,"AddStatsContent"),postfix:new HarmonyMethod(typeof(StatsMod),nameof(ShowXp)));
    }

    // Point also returns an empty target over menus, while dragging and over open terrain.
    static IEnumerable<CodeInstruction> ObservePointer(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
    {
        var observe = AccessTools.Method(typeof(StatsMod),nameof(Track)).MakeGenericMethod(((MethodInfo)__originalMethod).ReturnType);
        foreach (var op in instructions)
        {
            if (op.opcode == OpCodes.Ret)
                yield return new CodeInstruction(OpCodes.Call,observe).MoveLabelsFrom(op);
            yield return op;
        }
    }

    static T Track<T>(T result) where T:struct,ITuple
    {
        hovered = result[5] as CharacterId?;
        return result;
    }

    // Viewed already marks the pointed character Shown, including during paused publishes.
    // Request the same detail payload used for inspection without changing selection or inspection.
    static IEnumerable<CodeInstruction> PublishHoveredDetails(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod,ILGenerator generator)
    {
        var type = ((MethodInfo)__originalMethod).ReturnType;
        var local = generator.DeclareLocal(type);
        foreach (var op in instructions)
        {
            if (op.opcode == OpCodes.Ret)
            {
                yield return new CodeInstruction(OpCodes.Stloc,local).MoveLabelsFrom(op);
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Shown"));
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Cold"));
                yield return new CodeInstruction(OpCodes.Ldnull);
                yield return new CodeInstruction(OpCodes.Cgt_Un);
                yield return new CodeInstruction(OpCodes.And);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertySetter(type,"Detailed"));
                yield return new CodeInstruction(OpCodes.Ldloc,local);
            }
            yield return op;
        }
    }

    static IEnumerable<CodeInstruction> ReadDisplayLead(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var code = instructions.ToList();
        // BindUi reuses its display subject as the orders fallback when selection is empty.
        // Keep that fallback tied to the actual inspected character.
        if (original.Name == "BindUi")
        {
            int own = code.FindIndex(op => op.Calls(Selected));
            int fallback = code.FindIndex(own, op => op.opcode == OpCodes.Pop) + 1;
            if (fallback == 0 || code[fallback].opcode != OpCodes.Ldloc_1)
                throw new InvalidOperationException("Unexpected orders binding in BindUi");
            code[fallback] = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[fallback]);
            code.Insert(fallback + 1, new CodeInstruction(OpCodes.Call,Lead));
            // Replace only the first Lead call, which feeds the HUD.
        }
        bool replaced = false;
        foreach (var op in code)
        {
            if (!replaced && op.Calls(Lead)) { replaced=true; op.opcode=OpCodes.Call; op.operand=AccessTools.Method(typeof(StatsMod),nameof(DisplayLead)); }
            yield return op;
        }
    }

    static CharacterSnapshot? DisplayLead(object player)
    {
        if (hovered is {} id && Session.GetValue(player) is WorldSession session &&
            session.CurrentSnapshot.Characters.FirstOrDefault(c=>c.Id==id && c.Hud!=null) is {} character)
            return character;
        return (CharacterSnapshot?)Lead.Invoke(player,null);
    }

    static void ShowXp(object __instance,List<UiControl> controls)
    {
        var selected=(IReadOnlyList<CharacterSnapshot>)Selected.Invoke(StatsPlayer.GetValue(__instance),null)!;
        if (selected.FirstOrDefault() is not {} character) return;
        var rates=character.SkillXpMultipliers;
        for (int i=0;i<controls.Count;i++)
        {
            var c=controls[i];
            if (c.Name!="stat-description-heading") continue;
            var stat=(string?)AccessTools.Field(Stats,"SelectedStat").GetValue(__instance);
            if (int.TryParse(stat,out int index) && index>0 && index<rates.Count)
                controls[i]=c with { Caption=c.Caption+"   Racial XP ×"+rates[index].ToString("0.##",CultureInfo.CurrentCulture) };
            break;
        }
    }
}
