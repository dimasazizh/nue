using System.Globalization;
using System.Reflection;
using System.Numerics;
using Meitou.Game.Ui;
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
    [ThreadStatic] static CharacterSnapshot? preview;
    [ThreadStatic] static bool characterPanel;
    static readonly PropertyInfo Pointer = AccessTools.Property(Player,"PointerPanel");
    static readonly MethodInfo PointerName = AccessTools.PropertyGetter(Pointer.PropertyType,"PointerName");

    // Sample the native cached pick before the HUD binds. Outline rendering is optional.
    static void BeginFrame(object __instance, bool hud, out CharacterSnapshot? __state)
    {
        __state = preview;
        preview = null;
        if (!hud) return;
        PointerName.Invoke(Pointer.GetValue(__instance), null);
        preview = HoveredCharacter(__instance);
    }

    static void EndFrame(CharacterSnapshot? __state) => preview = __state;
    static void BeginPanel(out bool __state)
    {
        __state = characterPanel;
        characterPanel = preview != null;
    }
    static void EndPanel(bool __state) => characterPanel = __state;
    static bool UseSelectedBuilding() => !characterPanel;

    public class Options
    {
        [Setting("Show character hover outline", Hint = "Show the character under the pointer when the game’s hover outline is enabled.")]
        public bool ShowOutline { get; set; } = true;
    }

    static Options Settings = new();
    static Func<bool> ShowOutline = () => true;
    sealed record Examination(World World, CharacterId Id, MyGuiScreenHost Screens);
    static volatile Examination? examination;
    [ThreadStatic] static IReadOnlyList<CharacterSnapshot>? statsSelection;
    static readonly FieldInfo Screens = AccessTools.Field(Player,"FaithfulScreens");
    static readonly Type Menu = Game("Ui.ContextMenuWindow");
    static readonly FieldInfo MenuPlayer = Menu.GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);

    static void Outline(ref (CharacterId Id, Vector4 Colour)? __result)
    {
        if (!ShowOutline()) __result = null;
    }

    static void AddExamine(object __instance, CharacterSnapshot target, List<(string Caption,Action Give)> __result)
    {
        if (target.IsPlayer) return;
        var id = target.Id;
        var player = MenuPlayer.GetValue(__instance)!;
        __result.Add(("Examine", () =>
        {
            var session = (WorldSession)Session.GetValue(player)!;
            if (!session.CurrentSnapshot.Characters.Any(c=>c.Id==id)) return;
            var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
            AccessTools.Method(Player,"Inspect").Invoke(player,[id]);
            examination = new(session.World,id,screens);
            screens.Open("stats");
        }));
    }

    static void Closed(MyGuiScreenHost __instance,string id)
    {
        if (id=="stats" && examination?.Screens==__instance) examination=null;
    }

    static bool inventoryReadouts;
    static bool NeedsDetails(bool shown, World world, CharacterId id) =>
        shown || inventoryReadouts && world.Player.OpenLoot == id ||
        examination is {} e && ReferenceEquals(e.World,world) && e.Id==id;

    static CharacterSnapshot? Examined(object player)
    {
        var e=examination;
        if (e==null || Session.GetValue(player) is not WorldSession session || !ReferenceEquals(e.World,session.World)) return null;
        return session.CurrentSnapshot.Characters.FirstOrDefault(c=>c.Id==e.Id);
    }

    static void StatsTitle(string id,ref CharacterSnapshot? lead,object __instance)
    {
        if (id=="stats" && examination!=null)
            lead=Examined(AccessTools.Field(__instance.GetType(),"<player>P").GetValue(__instance)!);
    }

    static void BeginStats(object __instance,out IReadOnlyList<CharacterSnapshot>? __state)
    {
        __state=statsSelection;
        if (examination!=null) statsSelection=Examined(StatsPlayer.GetValue(__instance)!) is {} npc ? [npc] : [];
    }

    static void EndStats(IReadOnlyList<CharacterSnapshot>? __state) => statsSelection=__state;
    static bool StatsSubject(ref IReadOnlyList<CharacterSnapshot> __result)
    {
        if (statsSelection==null) return true;
        __result=statsSelection;
        return false;
    }

    public void Initialize(ModContext context)
    {
        context.AddSettings(Settings);
        Install(context, () => Settings.ShowOutline, hover: true, examine: true, racialXp: true);
    }

    internal void Install(ModContext context, Func<bool> showOutline, bool hover, bool examine, bool racialXp,
        bool publishInventoryReadouts = false)
    {
        ShowOutline = hover ? showOutline : () => false;
        inventoryReadouts = publishInventoryReadouts;
        HarmonyMethod Hook(string name) => new(typeof(StatsMod),name);
        void Rewrite(Type type,string method,string patch) => context.Harmony.Patch(AccessTools.Method(type,method),
            transpiler:Hook(patch));
        context.Harmony.Patch(AccessTools.PropertyGetter(Game("Ui.PointerView"),"HoverTarget"),postfix:Hook(nameof(Outline)));
        if (examine)
        {
            context.Harmony.Patch(AccessTools.Method(Menu,"Verbs"),postfix:Hook(nameof(AddExamine)));
            context.Harmony.Patch(AccessTools.Method(typeof(MyGuiScreenHost),"Close"),postfix:Hook(nameof(Closed)));
            context.Harmony.Patch(AccessTools.Method(Game("Ui.ScreenRouter"),"BindCharacterWindow"),prefix:Hook(nameof(StatsTitle)));
            context.Harmony.Patch(Selected,prefix:Hook(nameof(StatsSubject)));
            context.Harmony.Patch(AccessTools.Method(Stats,"AddStatsContent"),prefix:Hook(nameof(BeginStats)),finalizer:Hook(nameof(EndStats)));
        }
        if (hover)
        {
            context.Harmony.Patch(AccessTools.Method(Player,"ArrangeBase"),prefix:Hook(nameof(BeginFrame)),finalizer:Hook(nameof(EndFrame)));
            foreach (var (type,method) in new[]{(Player,"BindBuildingFocus"),(Player,"ArrangeHud"),
                (Game("Ui.HudPanels"),"AddHudText"),(Game("Ui.TooltipRuntime"),"BindTooltips")})
                context.Harmony.Patch(AccessTools.Method(type,method),
                    prefix:new HarmonyMethod(typeof(StatsMod),nameof(BeginPanel)) { priority=Priority.First },
                    finalizer:Hook(nameof(EndPanel)));
            context.Harmony.Patch(AccessTools.Method(Game("Building.BuildingSelection"),"BuildingFocus"),prefix:Hook(nameof(UseSelectedBuilding)));
            Rewrite(Game("Ui.PointerView"),"Point",nameof(ObservePointer));
            foreach (var (type,method) in new[]{(Player,"BindUi"),(Game("Ui.HudPanels"),"AddHudText"),(Game("Ui.TooltipRuntime"),"BindTooltips")})
                Rewrite(type,method,nameof(ReadDisplayLead));
        }
        if (hover || examine || inventoryReadouts) Rewrite(typeof(World),"Viewed",nameof(PublishCharacterDetails));
        if (racialXp) context.Harmony.Patch(AccessTools.Method(Stats,"AddStatsContent"),postfix:Hook(nameof(ShowXp)));
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

    // Request the inspection detail payload for hover and open loot without changing selection.
    static IEnumerable<CodeInstruction> PublishCharacterDetails(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod,ILGenerator generator)
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
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Detailed"));
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Shown"));
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Id"));
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StatsMod),nameof(NeedsDetails)));
                yield return new CodeInstruction(OpCodes.Ldloca,local);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(type,"Cold"));
                yield return new CodeInstruction(OpCodes.Ldnull);
                yield return new CodeInstruction(OpCodes.Cgt_Un);
                yield return new CodeInstruction(OpCodes.And);
                yield return new CodeInstruction(OpCodes.Or);
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

    static CharacterSnapshot? HoveredCharacter(object player)
    {
        if (hovered is {} id && Session.GetValue(player) is WorldSession session &&
            session.CurrentSnapshot.Characters.FirstOrDefault(c=>c.Id==id && c.Hud!=null) is {} character)
            return character;
        return null;
    }

    static CharacterSnapshot? DisplayLead(object player) =>
        preview ?? HoveredCharacter(player) ?? (CharacterSnapshot?)Lead.Invoke(player,null);

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
