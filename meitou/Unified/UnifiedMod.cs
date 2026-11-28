using Meitou.Modding;
using Nue.HoverStats;

namespace Nue.Unified;

public sealed class UnifiedMod : IMod
{
    public sealed class Options
    {
        private const string Restart = "Takes effect after restarting MeitouClient.";
        private const string Core = "Core Features";
        [Setting("Tooltips follow the cursor", Hint = Restart, SubGroup = Core)] public bool CursorTooltips { get; set; } = true;
        [Setting("HUD compass", Hint = Restart, SubGroup = Core)] public bool Compass { get; set; } = true;
        [Setting("Health and Stats tabs and building panel", Hint = Restart, SubGroup = Core)] public bool Tabs { get; set; } = true;
        [Setting("Hover stats", Hint = Restart, SubGroup = Core)] public bool Hover { get; set; } = true;
        [Setting("↳ Show character hover outline", Hint = "Requires Hover stats and the game's hover outline.",
            SubGroup = Core, VisibleWhen = nameof(Hover))]
        public bool ShowOutline { get; set; } = true;
        [Setting("Container highlights", SubGroup = Core)]
        public bool ContainerHighlights { get; set; } = true;
        [Setting("↳ Additional container highlight key", Hint = "Leave blank to use the game's highlight key (Alt by default). Enter a key or chord, such as H or Ctrl+H.",
            SubGroup = Core, VisibleWhen = nameof(ContainerHighlights))]
        public string ContainerHighlightKey { get; set; } = "";
        [Setting("Examine", Hint = Restart, SubGroup = Core)] public bool Examine { get; set; } = true;
        [Setting("Racial XP", Hint = Restart, SubGroup = Core)] public bool RacialXp { get; set; } = true;
        [Setting("Inventory inspection and avatar", Hint = Restart, SubGroup = Core)] public bool Avatar { get; set; } = true;
        [Setting("Inventory, crafting and research search", Hint = Restart, SubGroup = Core)] public bool Search { get; set; } = true;
        [Setting("Gear comparison", Hint = Restart, SubGroup = Core)] public bool GearComparison { get; set; } = true;
        [Setting("Character inspector", Hint = Restart, SubGroup = Core)] public bool CharacterInspector { get; set; } = true;
        [Setting("Skills window portrait", Hint = Restart, SubGroup = Core)] public bool Portrait { get; set; } = true;
        [Setting("Item Markers compatibility", Hint = "Fit Item Markers to NUE equipment slots. Requires Item Markers. " + Restart,
            SubGroup = "Mod Compatibility")]
        public bool ItemMarkersCompatibility { get; set; } = true;
        [Setting("Timelight world map", Hint = "Show Timelight's live view in the interactive map when its embedding API is available. " + Restart,
            SubGroup = "Mod Compatibility")]
        public bool TimelightWorldMap { get; set; } = true;
    }

    private static void FollowPointer(ref Meitou.Game.Ui.TooltipKind kind, ref Meitou.Data.Ui.UiRect? given,
        ref System.Numerics.Vector2? point, string prefix)
    {
        if(prefix!="tooltip" || kind==Meitou.Game.Ui.TooltipKind.Static) return;
        kind=Meitou.Game.Ui.TooltipKind.Floating;
        given=null;
        point=null;
    }

    public void Initialize(ModContext context)
    {
        var options = new Options();
        context.AddSettings(options);
        ContainerHighlights.Install(context, options);
        if (options.CursorTooltips) context.Harmony.Patch(HarmonyLib.AccessTools.Method(typeof(IMod).Assembly.GetType("Meitou.Game.Ui.TooltipRuntime"),"PlaceTooltip"),
            prefix:new HarmonyLib.HarmonyMethod(typeof(UnifiedMod),nameof(FollowPointer)));
        new StatsMod().Install(context, () => options.Hover && options.ShowOutline,
            options.Hover, options.Examine, options.RacialXp, publishInventoryReadouts: options.Avatar);
        if (options.Tabs) new HudTabs.HudTabsMod().Initialize(context);
        if (options.Compass) new Compass.CompassMod().Initialize(context);
        new InventoryAvatar.AvatarMod().Install(context, options.Avatar);
        if (options.Avatar && options.ItemMarkersCompatibility)
            InventoryAvatar.AvatarMod.EnableItemMarkersCompatibility(context);
        if (options.Search || options.GearComparison)
            new SearchCompare.SearchMod().Install(context, options.Search, options.GearComparison);
        if (options.Portrait) new SkillPortrait.PortraitMod().Initialize(context);
        SkillsLayout.Install(context);
        if (options.CharacterInspector) CharacterInspector.Enable(context);
        if (options.TimelightWorldMap) TimelightWorldMap.Install(context);
    }
}
