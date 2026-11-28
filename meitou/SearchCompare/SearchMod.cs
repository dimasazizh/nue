using Meitou.Simulation.Trading;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;

namespace Nue.SearchCompare;

public sealed partial class SearchMod : IMod
{
    const string Prefix = "NueSearch.";
    const string StockView = "scrollview_backpack_content";
    const string ClearAction = "nue-search-clear";
    static readonly Dictionary<string,string> Queries = new();
    static readonly ConditionalWeakTable<object,InventoryItemView> Comparisons = new();
    static Type Ui(string name) => typeof(MyGuiScreenHost).Assembly.GetType("Meitou.Game.Ui." + name, true)!;
    static readonly Type Router = Ui("ScreenRouter"), Production = Ui("ProductionWindows"), Tips = Ui("TooltipRuntime");
    static readonly FieldInfo RouterPlayer = AccessTools.Field(Router,"<player>P") ??
        throw new MissingFieldException(Router.FullName, "<player>P");
    private static readonly FieldInfo CraftStation = AccessTools.Field(Production, "_craftStation"),
        CraftScroll = AccessTools.Field(Production, "_craftScroll"),
        ResearchScroll = AccessTools.Field(Production, "_researchScroll"),
        ProductionPlayer = AccessTools.Field(Production, "<player>P"),
        PlayerScreens = AccessTools.Field(RouterPlayer.FieldType, "FaithfulScreens"),
        PlayerSession = AccessTools.Field(RouterPlayer.FieldType, "Session"),
        PlayerMouse = AccessTools.Field(RouterPlayer.FieldType, "Mouse"),
        PlayerWidth = AccessTools.Field(RouterPlayer.FieldType, "Width"),
        PlayerHeight = AccessTools.Field(RouterPlayer.FieldType, "Height");
    private static readonly MethodInfo ItemCells = AccessTools.PropertyGetter(RouterPlayer.FieldType, "ItemCells"),
        DrawItemCell = AccessTools.Method(ItemCells.ReturnType, "AddItemCell");
    static readonly MethodInfo ToTip = AccessTools.Method(Tips.GetNestedType("ItemTip", BindingFlags.NonPublic)!, "Of",
        [typeof(InventoryItemView), typeof(TradeItemView), typeof(string)]);
    static readonly MethodInfo ItemLines = AccessTools.Method(Tips,"ItemLines");
    static readonly MethodInfo PlaceTooltip = AccessTools.Method(Tips,"PlaceTooltip");
    static readonly FieldInfo TooltipPlayer = AccessTools.Field(Tips,"<player>P");
    static readonly FieldInfo LastModTooltip = AccessTools.Field(Tips,"_modRowsShownFor");
    static readonly MethodInfo SectionOf = AccessTools.Method(typeof(ItemFactory),"SectionOf");

    internal static bool Installed { get; private set; }
    static bool GearComparison;

    public void Initialize(ModContext context)
    {
        NueText.Register(context);
        Install(context, search: true, gearComparison: true);
    }

    internal void Install(ModContext context, bool search, bool gearComparison)
    {
        Installed = search;
        GearComparison = gearComparison;
        HarmonyMethod Hook(string name) => new(typeof(SearchMod),name);
        if (search)
        {
            context.Harmony.Patch(AccessTools.Method(Production, "AddCraftContent"), prefix: Hook(nameof(CraftBar)),
                transpiler: Hook(nameof(SearchAllCrafts)), finalizer: Hook(nameof(RestoreCraftStation)));
            context.Harmony.Patch(AccessTools.Method(Production, "AddResearchContent"), prefix: Hook(nameof(ResearchBar)),
                transpiler: Hook(nameof(SearchAllResearch)));
            context.Harmony.Patch(AccessTools.Method(Production, "CraftAction"), prefix: Hook(nameof(RouteCraftResult)));
            context.Harmony.Patch(AccessTools.Method(Router,"FaithfulAction"), prefix:Hook(nameof(Input)));
        }
        context.Harmony.Patch(AccessTools.Method(Tips, "AddHoverLabel"), postfix: Hook(nameof(CraftIconTooltip)));
        context.Harmony.Patch(AccessTools.Method(Router,"AddFaithfulContent"), postfix:Hook(nameof(Inventory)));
        if (gearComparison)
        {
            context.Harmony.Patch(ItemLines, postfix:Hook(nameof(RememberComparison)));
            context.Harmony.Patch(PlaceTooltip, prefix:Hook(nameof(CompareTooltipValues)),
                postfix:Hook(nameof(PlaceEquippedTooltip)));
        }
    }

    static bool Input(MyGuiEvent e,object __instance)
    {
        var player = RouterPlayer.GetValue(__instance)!;
        if (ProductionSearchInput(e, player)) return false;
        if(e.Screen is ("inventory" or "loot" or "trader") &&
            e.Widget=="NueClear" && e.Action==ClearAction)
        {
            var screens=(MyGuiScreenHost)AccessTools.Field(player.GetType(),"FaithfulScreens").GetValue(player)!;
            screens.SetText(e.Screen,Prefix+e.Screen,"");
            Queries.Remove(Prefix+e.Screen);
            return false;
        }
        if (!e.Widget.StartsWith(Prefix) || e.Action != "text") return true;
        Queries[e.Widget] = e.Text;
        return false;
    }

    static string Query(string key) => Queries.GetValueOrDefault(Prefix + key,"").Trim();
    static bool Match(string name,string query) => name.Contains(query,StringComparison.OrdinalIgnoreCase);

    static void Bar(List<UiControl> controls,string key,UiRect r)
    {
        float label = Math.Min(48,r.Width*.4f);
        var style = new Dictionary<string,string>{{"TextAlign","Left VCenter"}};
        controls.Add(new(Prefix+key+".label","TextBox",r with {Width=label},NueText.Get("Search"),"","",true,style,default,Pickable:false));
        controls.Add(new(Prefix+key,"EditBox",r with {X=r.X+label,Width=r.Width-label},
            Queries.GetValueOrDefault(Prefix+key,""),"","",true,style,new Vector4(.1f,.08f,.06f,.95f),Pickable:true));
    }

    static object Property(object obj,string name) => AccessTools.Property(obj.GetType(),name).GetValue(obj)!;

    static void VendorSearch(List<UiControl> controls, MyGuiScreenHost screens, string id, UiRect window)
    {
        var layout = screens.Layout(id);
        if (layout.Find("datapanel") is not {} panel ||
            layout.Find("Content")?.Children.FirstOrDefault(w => w.Children.Contains(panel)) is not {} moneyBox ||
            controls.Find(c => c.Name == StockView) is not {} view) return;
        int backgroundIndex = controls.FindIndex(c => c.Name == moneyBox.Name && c.Skin == moneyBox.Skin);
        int labelIndex = controls.FindIndex(c => c.Name == "datapanel-label-0");
        int valueIndex = controls.FindIndex(c => c.Name == "datapanel-value-0");
        if (backgroundIndex < 0 || labelIndex < 0 || valueIndex < 0) return;
        var background = controls[backgroundIndex];
        var label = controls[labelIndex];
        var stock = view.Bounds;
        float top = background.Bounds.Y;
        float gap = Math.Max(2, label.Bounds.Height * .20f);
        float height = Math.Min(background.Bounds.Height, Math.Min(label.Bounds.Height * 1.20f, stock.Y - top - gap));
        if (height <= 0 || stock.Width <= 4 * gap) return;
        float moneyWidth = Math.Min(background.Bounds.Width, stock.Width * .44f);
        float clearWidth = Math.Min(height * 1.25f, stock.Width * .18f);
        float searchWidth = stock.Width - moneyWidth - clearWidth - 2 * gap;
        if (searchWidth <= 0) return;

        var money = new UiRect(stock.X, top, moneyWidth, height);
        var moneyClip = window.Intersect(money);
        controls[backgroundIndex] = background with { Bounds = money, Clip = moneyClip };
        float inset = Math.Min(gap, moneyWidth * .05f);
        float innerWidth = moneyWidth - 2 * inset;
        MoveMoneyText(labelIndex, new(money.X + inset, top, innerWidth * .40f, height), "Left VCenter");
        MoveMoneyText(valueIndex, new(money.X + inset + innerWidth * .40f, top, innerWidth * .60f, height), "Right VCenter");

        var field = new UiRect(money.X + money.Width + gap, top, searchWidth, height);
        AddStockSearch(controls, screens, id, window, field, stock.X + stock.Width - clearWidth, clearWidth);

        void MoveMoneyText(int index, UiRect bounds, string align)
        {
            var control = controls[index];
            var properties = new Dictionary<string,string>(control.Properties) { ["TextAlign"] = align };
            controls[index] = control with { Bounds = bounds, Properties = properties, Clip = moneyClip };
        }
    }

    static void AddStockSearch(List<UiControl> controls, MyGuiScreenHost screens, string id, UiRect window,
        UiRect field, float clearX, float clearWidth, string? key = null, string hint = "Search items")
    {
        string name = Prefix + (key ?? id);
        string query = screens.GetText(id, name);
        var style = new Dictionary<string,string> { ["TextPadding"] = "8 2", ["TextAlign"] = "Left VCenter" };
        var focus = screens.Focus;
        bool focused = focus?.Screen == id && focus?.Widget == name;
        // The host applies caret properties automatically to layout widgets, before decoration adds this field.
        if (focus is {} edit && focused)
        {
            style["Focused"] = "true";
            style["CaretIndex"] = edit.Caret.ToString(CultureInfo.InvariantCulture);
            style["SelectionStart"] = edit.SelectionStart.ToString(CultureInfo.InvariantCulture);
            style["SelectionLength"] = edit.SelectionLength.ToString(CultureInfo.InvariantCulture);
        }
        // Runtime controls use the actual skin from the character inventory's EditBox template.
        controls.Add(new(name,"EditBox",field,query,"","Kenshi_GenericInputBoxSkin",true,style,default,Pickable:true,Clip:window));
        if (!focused && query.Length == 0)
            controls.Add(new("NueSearchHint","TextBox",field,NueText.Get(hint),"","",true,style,default,Pickable:false,Clip:field.Intersect(window)));
        var clear = new UiRect(clearX, field.Y, clearWidth, field.Height);
        controls.Add(new(key is null ? "NueClear" : "NueClear." + key,"Button",clear,"×",ClearAction,"Kenshi_Button1Skin",true,
            new Dictionary<string,string> { ["TextAlign"] = "Center", ["FontName"] = "Kenshi_PaintedTextFont_Medium" },
            default,Pickable:true,Clip:window));
    }

    static void Inventory(object __instance,string id,List<UiControl> controls)
    {
        string kind = (string)AccessTools.Method(Ui("InventoryWindows"),"ScreenKind").Invoke(null,[id])!;
        if (kind is not ("trader" or "inventory" or "bag" or "loot" or "loot-bag")) return;
        if (controls.FirstOrDefault(c=>c.Type=="Window") is not {} window) return;
        var r = window.Bounds;

        var player = RouterPlayer.GetValue(__instance)!;
        var runtime = Property(player,"TooltipRuntime");
        var tips = (IDictionary)AccessTools.Field(Tips,"ItemTips").GetValue(runtime)!;
        var lookup = (Func<string,GameRecord?>?)Property(runtime,"ItemRecordOf");
        string q = Installed ? AddInventorySearch(player, id, kind, controls, r) : "";
        CharacterSnapshot? owner = null;
        if (GearComparison)
        {
            var windows = Property(player,"InventoryWindows");
            owner = AccessTools.Method(windows.GetType(),"WindowCharacter").Invoke(windows,[kind=="trader" ? "inventory" : id]) as CharacterSnapshot;
        }
        if (q.Length == 0 && owner?.ItemGrid is null) return;
        foreach (var cell in controls.ToArray())
        {
            if (!(cell.Name.StartsWith("inventory-item-") || cell.Name.StartsWith("trade-item-")) || tips[cell.Name] is not {} tip) continue;
            var record=lookup?.Invoke((string)Property(tip,"Record"));
            if (q.Length>0 && !Match((string)Property(tip,"Name"),q) && !Match(record?.Type.ToString() ?? "",q)
                && !(record?.Type==FcsRecordType.Armour && Match((string)SectionOf.Invoke(null,[record])!,q)))
                controls.Add(new(cell.Name+"-dim","Widget",cell.Bounds,"","","",true,new Dictionary<string,string>(),
                    new Vector4(.045f,.035f,.025f,.82f),Pickable:false,Clip:cell.Clip));
            if (owner?.ItemGrid is not {} grid || record == null) continue;
            string slot = record.Type switch {
                FcsRecordType.Weapon or FcsRecordType.Crossbow => "back",
                FcsRecordType.Container => "backpack_attach",
                FcsRecordType.Armour or FcsRecordType.Item => (string)SectionOf.Invoke(null,[record])!,
                _ => "" };
            if (!InventoryCommands.IsEquipmentSection(slot)) continue;
            string? index = kind!="trader" ? cell.Properties.GetValueOrDefault("InventoryItem") : null;
            var equipped = grid.Items.Where(i => i.Index.ToString()!=index &&
                (i.Section==slot || slot=="back" && i.Section=="hip"))
                .OrderBy(i => i.Section!=slot).FirstOrDefault();
            if (equipped != null) Comparisons.AddOrUpdate(tip,equipped);
        }
    }

    static string AddInventorySearch(object player, string id, string kind, List<UiControl> controls, UiRect r)
    {
        var screens=(MyGuiScreenHost)AccessTools.Field(player.GetType(),"FaithfulScreens").GetValue(player)!;
        if (id is ("bag" or "loot-bag") && controls.Any(c => c.Type == "Window" && c.Properties.ContainsKey("NueEmbeddedAnimalBag")))
            return Query(id == "bag" ? "inventory" : "loot");
        if (id is ("inventory" or "loot") && controls.Find(c => c.Name == "NueAnimalSearchArea") is {} animal)
        {
            var area = animal.Bounds;
            float clear = Math.Min(area.Height * 1.25f, area.Width / 4), gap = Math.Max(2, area.Height * .15f);
            if (area.Width > clear + gap)
                AddStockSearch(controls, screens, id, r, area with { Width = area.Width - clear - gap },
                    area.X + area.Width - clear, clear);
            return Query(id);
        }
        string source = id is "loot" or "loot-bag" ? "loot" : "inventory";
        bool shared=(id is "inventory" or "bag" or "loot" or "loot-bag") && screens.IsOpen(source) && screens.Layout(source).Find("NuePreviewArea")!=null;
        if(kind=="trader") VendorSearch(controls,screens,id,r);
        else if(!shared) Bar(controls,id,new(r.X+6,r.Y+4,Math.Min(160,r.Width*.35f),24));
        if(shared && id==source && screens.Focus?.Widget!=Prefix+source && Query(source).Length==0 &&
            controls.Find(c=>c.Name==Prefix+source) is {} field)
            controls.Add(new("NueSearchHint","TextBox",field.Bounds,NueText.Get("Search items"),"","",true,field.Properties,default,Pickable:false));
        return Query(shared ? source : id);
    }
}
