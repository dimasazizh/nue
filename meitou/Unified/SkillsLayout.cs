using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data.Gameplay.Bodies;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Crime;
using Meitou.Simulation.Locks;
using Meitou.Simulation.Player;
using Meitou.Simulation.Production;
using Nue.SearchCompare;

namespace Nue.Unified;

internal static class SkillsLayout
{
    private const string Athletics = "Skills3Panel-heading-4";
    private const string BrutalityLabel = "brutality-stat-label", BrutalityValue = "brutality-stat-value";
    private const string ToggleAction = "nue-skills-modifiers";
    private const int PieSize = 24, PiesPerRow = 10;
    private static readonly Assembly Game = typeof(IMod).Assembly;
    private static readonly Type Stats = Game.GetType("Meitou.Game.Ui.StatsWindow", true)!;
    private static readonly Type Player = Game.GetType("Meitou.Game.PlayerInterface", true)!;
    private static readonly FieldInfo StatsPlayer = AccessTools.GetDeclaredFields(Stats).Single(field => field.FieldType == Player);
    private static readonly MethodInfo Selected = AccessTools.Method(Player, "Selected");
    private static readonly MethodInfo UiScale = AccessTools.PropertyGetter(Player, "UiScale");
    private static readonly MethodInfo SkillTerms = AccessTools.Method(typeof(World), "SkillTermsOf");
    private static readonly MethodInfo SkillTotal = AccessTools.PropertyGetter(SkillTerms.ReturnType, "Total");
    private static readonly Func<CharacterCold, StatsEnumerated, float> WorkSkill =
        AccessTools.Method(typeof(ProductionSystem), "Effective").CreateDelegate<Func<CharacterCold, StatsEnumerated, float>>();
    private static readonly Func<CharacterCold, StatsEnumerated, float> GearFactor =
        AccessTools.Method(typeof(CombatSystem), "GearFactor").CreateDelegate<Func<CharacterCold, StatsEnumerated, float>>();
    private static readonly (int Id, object Stat)[] DisplayedStats = ((Array[])AccessTools.Field(Stats, "StatPanels").GetValue(null)!)
        .SelectMany(panel => panel.Cast<object>()).Distinct().Select(stat => (Convert.ToInt32(stat), stat)).ToArray();
    private sealed record ModifierTarget(CharacterId Id);
    private static volatile ModifierTarget? modifierTarget;
    private static bool showModifiers;
    private static readonly ConditionalWeakTable<HudView, Dictionary<int, float>> EffectiveByHud = new();
    private static readonly string[] HeadingNames =
    [
        "Skills1Panel-heading-0", "Skills2Panel-heading-0", "Skills2Panel-heading-4",
        "Skills3Panel-heading-0", Athletics, "Skills4Panel-heading-0", "Skills4Panel-heading-4"
    ];
    private static readonly string[] HeadingIcons = ["weapons", "combat", "ranged", "thievery", "athletics", "sciences", "trades"];
    private static string pieTexture = "";

    internal static void Install(ModContext context)
    {
        var atlas = Path.Combine(context.Folder, "gui", "gfx", "nue_skill_xp.png");
        if (File.Exists(atlas)) pieTexture = UiPainter.IconTexture(atlas);
        context.Harmony.Patch(AccessTools.Method(Stats, "BindStatsGeometry"),
            postfix: new HarmonyMethod(typeof(SkillsLayout), nameof(BindDescription)));
        context.Harmony.Patch(AccessTools.Method(Stats, "AddStatsContent"),
            postfix: new HarmonyMethod(typeof(SkillsLayout), nameof(Arrange)) { priority = Priority.Last });
        context.Harmony.Patch(AccessTools.Method(typeof(World), "HudOf"),
            postfix: new HarmonyMethod(typeof(SkillsLayout), nameof(CaptureEffectiveValues)));
        context.Harmony.Patch(AccessTools.Method(Game.GetType("Meitou.Game.Ui.ScreenRouter", true), "FaithfulAction"),
            prefix: new HarmonyMethod(typeof(SkillsLayout), nameof(ToggleModifiers)));
        context.Harmony.Patch(AccessTools.Method(typeof(MyGuiScreenHost), "Close"),
            postfix: new HarmonyMethod(typeof(SkillsLayout), nameof(ClearTarget)));
    }

    private static bool ToggleModifiers(MyGuiEvent e)
    {
        if (e.Screen != "stats" || e.Action != ToggleAction) return true;
        showModifiers = !showModifiers;
        return false;
    }

    private static void ClearTarget(string id)
    {
        if (id == "stats") modifierTarget = null;
    }

    private static void CaptureEffectiveValues(CharacterId id, CharacterCold cold, object? med, object? stats, HudView __result)
    {
        if (modifierTarget?.Id != id || med is null || stats is null) return;
        var values = new Dictionary<int, float>();
        object[] args = [cold, med, stats, 0];
        foreach (var (statId, stat) in DisplayedStats)
        {
            args[3] = stat;
            // Prefer activity-specific calculations where the game supplies an effective level.
            float? effective = (StatsEnumerated)statId switch
            {
                StatsEnumerated.Stealth => MathF.Round(Orders.Stealth(cold) * 100f, 4),
                StatsEnumerated.Lockpicking => Lockpicking.Skill(cold),
                StatsEnumerated.Thieving => CrimeSystem.Thievery(cold),
                StatsEnumerated.Assassination =>
                    WorkSkill(cold, StatsEnumerated.Assassination) * GearFactor(cold, StatsEnumerated.Assassination),
                StatsEnumerated.Crossbows or StatsEnumerated.FriendlyFire =>
                    WorkSkill(cold, (StatsEnumerated)statId) * GearFactor(cold, StatsEnumerated.Crossbows),
                StatsEnumerated.Labouring or StatsEnumerated.Farming or StatsEnumerated.Cooking or
                    StatsEnumerated.Engineering or StatsEnumerated.Science or StatsEnumerated.Robotics =>
                    WorkSkill(cold, (StatsEnumerated)statId),
                _ => SkillTotal.Invoke(SkillTerms.Invoke(null, args), null) as float?
            };
            if (effective is float value && float.IsFinite(value))
                values[statId] = value;
        }

        // Where the HUD reports a combat skill level, use its contextual total.
        foreach (var line in __result.Stats)
        {
            var stat = line.Label switch
            {
                "Attack:" => StatsEnumerated.MeleeAttack,
                "Defense:" => StatsEnumerated.MeleeDefence,
                "Dodge:" => StatsEnumerated.Dodge,
                "Martial Arts:" => StatsEnumerated.MartialArts,
                "Crossbows:" => StatsEnumerated.Crossbows,
                "Precision:" => StatsEnumerated.FriendlyFire,
                _ => StatsEnumerated.None
            };
            if (stat != StatsEnumerated.None && float.IsFinite(line.Value))
                values[(int)stat] = line.Value;
        }
        EffectiveByHud.AddOrUpdate(__result, values);
    }

    private static void BindDescription(MyGuiLayout layout, UiBindings bindings)
    {
        // Native stats geometry overrides the container of Description1Panel.
        if (layout.Find("NueSkillsDescription") is {} description)
            bindings.RelativePositions[description] = description.Position;
    }

    private static void Arrange(object __instance, List<UiControl> controls)
    {
        if (controls.Find(c => c.Name == "NueSkillsFrame") is not {} frame) return;
        var player = StatsPlayer.GetValue(__instance)!;
        float scale = (float)UiScale.Invoke(player, null)!;
        var lead = ((IReadOnlyList<CharacterSnapshot>)Selected.Invoke(player, null)!).FirstOrDefault();
        if (modifierTarget?.Id != lead?.Id)
            modifierTarget = lead is null ? null : new(lead.Id);
        Dictionary<int, float>? effective = null;
        if (showModifiers && lead?.Hud is {} hud)
            EffectiveByHud.TryGetValue(hud, out effective);
        AddModifierToggle(controls);
        if (showModifiers) ShiftAttributeValues(controls, effective, scale);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var columns = new List<int>[] { [], [], [], [] };
        var leftMods = new List<int>();
        var rightMods = new List<int>();
        for (int i = 0; i < controls.Count; i++)
        {
            var name = controls[i].Name;
            if (!name.StartsWith("Skills", StringComparison.Ordinal) || name.Length <= 12 ||
                name[6] is < '1' or > '4' || !name.AsSpan(7).StartsWith("Panel-", StringComparison.Ordinal)) continue;
            if (!indices.TryAdd(name, i)) continue;
            if (name.Contains("-value-", StringComparison.Ordinal)) continue;
            int column = name[6] - '1';
            if (name.Contains("-mod-label-", StringComparison.Ordinal))
                (column < 2 ? leftMods : rightMods).Add(i);
            else
                columns[column].Add(i);
        }

        if (!indices.TryGetValue(Athletics, out int athletics) || columns[0].Count == 0 || columns[3].Count == 0) return;
        int split = columns[2].IndexOf(athletics);
        if (split < 0) return;
        int brutalityLabel = controls.FindIndex(c => c.Name == BrutalityLabel && c.Action == "stat:brutality");
        int brutalityValue = controls.FindIndex(c => c.Name == BrutalityValue && c.Action == "stat:brutality");
        int[] brutality = brutalityLabel >= 0 && brutalityValue >= 0 ? [brutalityLabel] : [];
        if (brutality.Length > 0) indices[BrutalityValue] = brutalityValue;
        int[] left = [..columns[0], ..columns[1], ..columns[2].Skip(split), ..leftMods];
        int[] right = [..columns[2].Take(split), ..columns[3], ..brutality, ..rightMods];
        var area = frame.Bounds;
        float topMargin = 10 * scale, bottomMargin = 5 * scale, groupGap = 6 * scale;
        float FitRows(int[] rows)
        {
            int groups = rows.Count(index => index == brutalityLabel || Array.IndexOf(HeadingNames, controls[index].Name) >= 0);
            return (area.Height - topMargin - bottomMargin - Math.Max(0, groups - 1) * groupGap) / rows.Length;
        }
        float row = Math.Min(20 * scale, Math.Min(FitRows(left), FitRows(right)));
        if (row <= 0 || area.Width <= 0) return;
        var clip = area.Intersect(frame.Clip ?? area);
        PlaceColumn(left, area.X + area.Width * .066f, area.X + area.Width * .362f);
        PlaceColumn(right, area.X + area.Width * .560f, area.X + area.Width * .856f);

        void PlaceColumn(int[] rows, float x, float oldValueRight)
        {
            float y = area.Y + topMargin;
            bool firstGroup = true;
            float valueRight = oldValueRight - Math.Min(40 * scale, (oldValueRight - x) * .22f);
            float valueWidth = Math.Min(32 * scale, (valueRight - x) * .22f);
            float gap = 4 * scale;
            foreach (int index in rows)
            {
                var label = controls[index];
                int heading = Array.IndexOf(HeadingNames, label.Name);
                if (heading >= 0)
                {
                    if (!firstGroup) y += groupGap;
                    firstGroup = false;
                    controls[index] = Move(label, new(x, y, oldValueRight - x, row), clip, row * .89f);
                    int iconIndex = controls.FindIndex(c => c.Skin == "NUE_Stats_" + HeadingIcons[heading]);
                    if (iconIndex >= 0)
                    {
                        float side = Math.Min(row * 1.24f, area.Width * .045f);
                        var icon = controls[iconIndex];
                        controls[iconIndex] = icon with { Bounds = new(x - side - gap, y + (row - side) / 2, side, side), Clip = clip };
                    }
                }
                else if (indices.TryGetValue(label.Name.Replace("-label-", "-value-", StringComparison.Ordinal), out int number))
                {
                    bool isBrutality = index == brutalityLabel;
                    if (isBrutality)
                    {
                        y += groupGap;
                        float side = Math.Min(row * 1.24f, area.Width * .045f);
                        controls.Add(new("NueBrutalityIcon", "Widget", new(x - side - gap, y + (row - side) / 2, side, side),
                            "", "", "NUE_Stats_brutality", true, new Dictionary<string, string>(), default, Pickable: false, Clip: clip));
                    }
                    float labelWidth = Math.Max(0, valueRight - valueWidth - gap - x);
                    controls[index] = Move(label, new(x, y, labelWidth, row), clip, row * .77f);
                    var value = controls[number];
                    controls[number] = Move(value, new(valueRight - valueWidth, y, valueWidth, row), clip, row * .77f);
                    float modifierWidth = Math.Min(34 * scale, area.Width * .048f);
                    if (showModifiers)
                        AddModifiedValue(controls, value, label, effective,
                            new(valueRight + gap, y, modifierWidth, row), clip, row * .77f);
                    float pieX = valueRight + gap + (showModifiers ? modifierWidth + gap : 0);
                    if (isBrutality)
                        AddProgressPie(controls, value, BrutalityProgress.Read(player, lead), pieX, y, row, clip);
                    else
                        AddPie(controls, lead, value, pieX, y, row, clip);
                }
                y += row;
            }
        }
    }

    private static class BrutalityProgress
    {
        private static readonly Type? StockRow = AccessTools.TypeByName("Meitou.Brutality.StockStatsRow");
        private static readonly FieldInfo? Installed = StockRow is null ? null : AccessTools.Field(StockRow, "installed");
        private static readonly MethodInfo? StockProgress = StockRow is null ? null : AccessTools.Method(StockRow, "Progress");
        private static readonly MethodInfo? StockFraction = StockProgress is null
            ? null : AccessTools.PropertyGetter(StockProgress.ReturnType, "Progress01");
        private static readonly Type? Adapter = AccessTools.TypeByName("Meitou.Brutality.Ui.BrutalityUiAdapter");
        private static readonly MethodInfo? Session = Adapter is null ? null : AccessTools.Method(Adapter, "Session");
        private static readonly MethodInfo? Actor = Adapter is null ? null : AccessTools.Method(Adapter, "Actor");

        internal static float Read(object player, CharacterSnapshot? lead)
        {
            if (Installed?.GetValue(null) is {} row && StockProgress?.Invoke(row, [player]) is {} progress &&
                StockFraction?.Invoke(progress, null) is float fraction)
                return fraction;
            if (lead is not null && Session?.Invoke(null, [player]) is {} session &&
                Actor?.Invoke(null, [session, lead.Id]) is {} actor &&
                AccessTools.PropertyGetter(actor.GetType(), "Progress")?.Invoke(actor, null) is float legacyFraction)
                return legacyFraction;
            return 0;
        }
    }

    private static void AddModifierToggle(List<UiControl> controls)
    {
        if (controls.Find(c => c.Name == "NueSkillModifiersArea") is not {} anchor) return;
        var bounds = anchor.Bounds;
        controls.Add(new("NueSkillModifiersToggle", "Button", bounds,
            NueText.Get(showModifiers ? "HIDE MODIFIERS" : "SHOW MODIFIERS"), ToggleAction, "Kenshi_Button1Skin",
            true, new Dictionary<string, string> { ["TextAlign"] = "Center VCenter",
                ["FontName"] = "Kenshi_StandardFont_Small" }, default, Pickable: true, Clip: bounds));
    }

    private static void ShiftAttributeValues(List<UiControl> controls, Dictionary<int, float>? effective, float scale)
    {
        if (controls.Find(c => c.Name == "AttributesPanel") is not {} panel) return;
        var clip = panel.Bounds.Intersect(panel.Clip ?? panel.Bounds);
        float gap = 3 * scale, width = Math.Min(34 * scale, clip.Width * .25f);
        int count = controls.Count;
        for (int i = 0; i < count; i++)
        {
            var value = controls[i];
            if (!value.Name.StartsWith("AttributesPanel-value-", StringComparison.Ordinal)) continue;
            var label = controls.Find(c => c.Name == value.Name.Replace("-value-", "-label-", StringComparison.Ordinal));
            float right = Math.Min(value.Bounds.X + value.Bounds.Width, clip.X + clip.Width);
            AddModifiedValue(controls, value, label, effective,
                new(right - width, value.Bounds.Y, width, value.Bounds.Height), clip, value.Bounds.Height * .77f);
            float baseRight = right - width - gap;
            float baseWidth = Math.Min(value.Bounds.Width, Math.Max(0, baseRight - clip.X) * .35f);
            float baseLeft = baseRight - baseWidth;
            controls[i] = value with { Bounds = value.Bounds with { X = baseLeft, Width = baseWidth } };
            if (label is not null && label.Bounds.X + label.Bounds.Width > baseLeft)
            {
                int index = controls.IndexOf(label);
                controls[index] = label with { Bounds = label.Bounds with { Width = Math.Max(0, baseLeft - label.Bounds.X - gap) } };
            }
        }
    }

    private static void AddModifiedValue(List<UiControl> controls, UiControl value, UiControl? label,
        Dictionary<int, float>? effective, UiRect bounds, UiRect clip, float fontSize)
    {
        string action = value.Action.StartsWith("stat:", StringComparison.Ordinal) ? value.Action : label?.Action ?? "";
        if (effective is null || !action.StartsWith("stat:", StringComparison.Ordinal) ||
            !int.TryParse(action.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out int stat) ||
            !effective.TryGetValue(stat, out float modified) ||
            !float.TryParse(value.Caption, NumberStyles.Float, CultureInfo.InvariantCulture, out float baseline)) return;
        modified = MathF.Floor(modified);
        if (modified == baseline) return;
        string tint = modified < baseline ? "0.90 0.30 0.24" : "0.42 0.76 0.32";
        var text = new UiControl(value.Name + "-modified", "TextBox", bounds, $"({modified:0})", "", "", true,
            new Dictionary<string, string> { ["TextAlign"] = "Right VCenter", ["TextColour"] = tint,
                ["FontName"] = "Kenshi_StandardFont_Small" }, default, Pickable: false, Clip: clip);
        controls.Add(Move(text, bounds, clip, fontSize));
    }

    private static UiControl Move(UiControl control, UiRect bounds, UiRect clip, float fontSize)
    {
        var style = new Dictionary<string, string>(control.Properties)
        {
            ["FontPixels"] = Math.Max(1, (int)MathF.Floor(fontSize)).ToString(CultureInfo.InvariantCulture)
        };
        style.Remove("WordWrap");
        return control with { Bounds = bounds, Clip = clip, Properties = style };
    }

    private static void AddPie(List<UiControl> controls, CharacterSnapshot? lead, UiControl value,
        float x, float y, float row, UiRect clip)
    {
        if (lead is null || !value.Action.StartsWith("stat:", StringComparison.Ordinal)) return;
        var id = value.Action.AsSpan(5);
        bool mod = id.StartsWith("mod:", StringComparison.Ordinal);
        if (!int.TryParse(mod ? id[4..] : id, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) return;
        float level = mod ? lead.ModSkills.ElementAtOrDefault(index) : lead.AllSkills.ElementAtOrDefault(index);
        if (!float.IsFinite(level)) return;
        AddProgressPie(controls, value, level - MathF.Floor(level), x, y, row, clip);
    }

    private static void AddProgressPie(List<UiControl> controls, UiControl value, float fraction,
        float x, float y, float row, UiRect clip)
    {
        if (pieTexture.Length == 0 || !float.IsFinite(fraction)) return;
        int progress = Math.Clamp((int)MathF.Floor(fraction * 100), 0, 99);
        float side = row * .76f;
        var style = new Dictionary<string, string>
        {
            ["ImageTexture"] = pieTexture,
            ["ImageRect"] = FormattableString.Invariant($"{(progress % PiesPerRow) * PieSize} {(progress / PiesPerRow) * PieSize} {PieSize} {PieSize}")
        };
        controls.Add(new(value.Name + "-xp", "ImageBox", new(x, y + (row - side) / 2, side, side),
            "", "", "", true, style, default, Pickable: false, Clip: clip));
    }
}
