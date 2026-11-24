using Meitou.Modding;
using Nue.HoverStats;

namespace Nue.Unified;

public sealed class UnifiedMod : IMod
{
    public sealed class Options : StatsMod.Options
    {
        private const string Restart = "Takes effect after restarting MeitouClient.";
        [Setting("HUD compass", Hint = Restart)] public bool Compass { get; set; } = true;
        [Setting("Health and Stats tabs and building panel", Hint = Restart)] public bool Tabs { get; set; } = true;
        [Setting("Hover stats, Examine and racial XP", Hint = Restart)] public bool Hover { get; set; } = true;
        [Setting("Inventory inspection and avatar", Hint = Restart)] public bool Avatar { get; set; } = true;
        [Setting("Inventory, crafting and research search and gear comparison", Hint = Restart)] public bool Search { get; set; } = true;
        [Setting("Skills window portrait", Hint = Restart)] public bool Portrait { get; set; } = true;
    }

    public void Initialize(ModContext context)
    {
        var options = new Options();
        context.AddSettings(options);
        if (options.Hover) new StatsMod().Install(context, options);
        if (options.Tabs) new HudTabs.HudTabsMod().Initialize(context);
        if (options.Compass) new Compass.CompassMod().Initialize(context);
        new InventoryAvatar.AvatarMod().Install(context, options.Avatar);
        if (options.Search) new SearchCompare.SearchMod().Initialize(context);
        if (options.Portrait) new SkillPortrait.PortraitMod().Initialize(context);
    }
}
