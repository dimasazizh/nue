using System.Reflection;
using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Modding;
using Meitou.Simulation;

namespace Nue.SkillPortrait;

public sealed class PortraitMod : IMod
{
    // These internal UI methods belong to the targeted nightly. Resolve them once at startup.
    private static readonly Assembly Game = typeof(IMod).Assembly;
    private static readonly Type Stats = Game.GetType("Meitou.Game.Ui.StatsWindow", true)!;
    private static readonly Type Player = Game.GetType("Meitou.Game.PlayerInterface", true)!;
    private static readonly Type Bar = Game.GetType("Meitou.Game.Ui.PortraitBar", true)!;
    private static readonly FieldInfo StatsPlayer = Stats.GetFields(AccessTools.allDeclared)
        .Single(f => f.FieldType == Player);
    private static readonly MethodInfo Selected = AccessTools.Method(Player, "Selected");
    private static readonly MethodInfo GetBar = AccessTools.PropertyGetter(Player, "PortraitBar");
    private static readonly MethodInfo Members = AccessTools.Method(Bar, "PortraitMembers");
    private static readonly MethodInfo CellRect = AccessTools.Method(Bar, "PortraitRect");

    public void Initialize(ModContext context)
    {
        context.Harmony.Patch(AccessTools.Method(Stats, "BindStatsGeometry"),
            postfix: new HarmonyMethod(typeof(PortraitMod), nameof(ReserveSpace)));
        context.Harmony.Patch(AccessTools.Method(Stats, "AddStatsContent"),
            postfix: new HarmonyMethod(typeof(PortraitMod), nameof(AddPortrait)));
    }

    private static readonly UiRect Heading = new(.008f, .341f, .25f, .036f);
    private static readonly UiRect Frame = new(.008f, .380f, .25f, .162f);

    private static void ReserveSpace(MyGuiLayout layout, UiBindings bindings)
    {
        var panel = layout.Find("AttributesPanel");
        var heading = layout.Find("lbAttributes");
        if (panel == null || heading == null) return;

        var parent = layout.Widgets.FirstOrDefault(w => w.Children.Contains(panel));
        if (parent == null) return;

        // NUE puts the frame, actual heading and row container in a dedicated group.
        // The stock layout uses the frame itself as the row container.
        if (layout.Find("NUE_SkillsHeading") != null)
        {
            var group = layout.Widgets.FirstOrDefault(w => w.Children.Contains(parent));
            var backdrop = group?.Children.FirstOrDefault(w => w.Skin == "Kenshi_GenericTextBoxSkin");
            var title = group?.Children.FirstOrDefault(w => w.Type == "TextBox");
            if (group == null || backdrop == null || title == null) return;
            bindings.RelativePositions[group] = new(0, 0, 1, 1);
            bindings.RelativePositions[backdrop] = Frame;
            bindings.RelativePositions[title] = Heading;
        }

        bindings.RelativePositions[heading] = Heading;
        bindings.RelativePositions[parent] = Frame;
        bindings.RelativePositions[panel] = new(.04f, .04f, .92f, .95f);
    }

    private static void AddPortrait(object __instance, List<UiControl> controls)
    {
        var heading = controls.Find(c => c.Name == "lbAttributes");
        if (heading == null || heading.Clip is not { } client) return;

        var player = StatsPlayer.GetValue(__instance);
        var selected = (IReadOnlyList<CharacterSnapshot>)Selected.Invoke(player, null)!;
        if (selected.Count == 0) return;

        var bar = GetBar.Invoke(player, null);
        var members = (IReadOnlyList<CharacterSnapshot>)Members.Invoke(bar, null)!;
        for (int cell = 0; cell < members.Count; cell++)
        {
            if (members[cell].Id != selected[0].Id) continue;
            var bounds = heading.Bounds;
            // Keep the face square and trim the vertical edges evenly to the available slot.
            float gap = Math.Max(1, MathF.Round(bounds.Height * .15f));
            float top = client.Y + gap;
            float height = bounds.Y - gap - top;
            if (height <= 0 || bounds.Width <= 0) return;
            var slot = new UiRect(bounds.X, top, bounds.Width, height);
            var image = new UiRect(slot.X, top + (height - slot.Width) / 2, slot.Width, slot.Width);
            var properties = new Dictionary<string, string>
            {
                ["ImageTexture"] = "meitou:portraits",
                ["ImageFlipV"] = "true",
                ["ImageRect"] = (string)CellRect.Invoke(bar, [cell])!
            };
            controls.Add(new("NueSkillPortrait", "ImageBox", image, "", "", "", true, properties,
                default, Pickable: false, Clip: slot.Intersect(client)));
            return;
        }
    }
}
