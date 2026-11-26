using Meitou.Simulation.Trading;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Items;
using Meitou.Simulation.Production;

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
    static readonly FieldInfo RouterPlayer = AccessTools.Field(Router,"<player>P");
    static readonly MethodInfo ToTip = AccessTools.Method(Tips.GetNestedType("ItemTip", BindingFlags.NonPublic)!, "Of",
        [typeof(InventoryItemView), typeof(TradeItemView), typeof(string)]);
    static readonly MethodInfo ItemLines = AccessTools.Method(Tips,"ItemLines");
    static readonly MethodInfo PlaceTooltip = AccessTools.Method(Tips,"PlaceTooltip");
    static readonly FieldInfo TooltipPlayer = AccessTools.Field(Tips,"<player>P");
    static readonly FieldInfo LastModTooltip = AccessTools.Field(Tips,"_modRowsShownFor");
    static readonly MethodInfo SectionOf = AccessTools.Method(typeof(ItemFactory),"SectionOf");

    internal static bool Installed { get; private set; }
    static bool GearComparison;

    public void Initialize(ModContext context) => Install(context, search: true, gearComparison: true);

    internal void Install(ModContext context, bool search, bool gearComparison)
    {
        Installed = search;
        GearComparison = gearComparison;
        HarmonyMethod Hook(string name) => new(typeof(SearchMod),name);
        if (search)
        {
            foreach (var method in new[]{"AddCraftContent","AddResearchContent"})
                context.Harmony.Patch(AccessTools.Method(Production,method), prefix:Hook(nameof(ListBar)), transpiler:Hook(nameof(FilterRows)));
            context.Harmony.Patch(AccessTools.Method(Router,"FaithfulAction"), prefix:Hook(nameof(Input)));
        }
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
        if(e.Screen is ("inventory" or "loot" or "trader" or "storage" or "storage-own") &&
            e.Widget=="NueClear" && e.Action==ClearAction)
        {
            var player=RouterPlayer.GetValue(__instance)!;
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

    // Filter the sorted options before Meitou measures the scrollbar and binds queue actions.
    static IEnumerable<CodeInstruction> FilterRows(IEnumerable<CodeInstruction> instructions)
    {
        bool found = false;
        foreach (var op in instructions)
        {
            yield return op;
            if (op.operand is not MethodInfo { Name:"ToList", IsGenericMethod:true } m || m.DeclaringType != typeof(Enumerable)) continue;
            var row = m.GetGenericArguments()[0];
            string? filter = row == typeof(CraftOptionView) ? nameof(Crafts) : row == typeof(ResearchOption) ? nameof(Research) : null;
            if (filter == null) continue;
            found = true;
            yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(SearchMod),filter));
        }
        if (!found) throw new InvalidOperationException("Missing nightly production option list");
    }

    static List<CraftOptionView> Crafts(List<CraftOptionView> rows)
    {
        var q = Query("craft");
        if (q.Length > 0) rows.RemoveAll(r => !Match(r.Name,q));
        return rows;
    }

    static List<ResearchOption> Research(List<ResearchOption> rows)
    {
        var q = Query("research");
        if (q.Length > 0) rows.RemoveAll(r => !Match(r.Name,q));
        return rows;
    }

    static void ListBar(List<UiControl> controls, MethodBase __originalMethod)
    {
        bool craft = __originalMethod.Name == "AddCraftContent";
        int index = controls.FindIndex(c => c.Name == (craft ? "QueueItemsPanel" : "BlueprintsAvailablePanel"));
        if (index < 0) return;
        var panel = controls[index];
        var r = panel.Bounds;
        float height = Math.Min(28,r.Height / 4);
        controls[index] = panel with { Bounds = r with { Y=r.Y+height, Height=r.Height-height } };
        Bar(controls,craft ? "craft" : "research",r with { Height=height });
    }

    static void Bar(List<UiControl> controls,string key,UiRect r)
    {
        float label = Math.Min(48,r.Width*.4f);
        var style = new Dictionary<string,string>{{"TextAlign","Left VCenter"}};
        controls.Add(new(Prefix+key+".label","TextBox",r with {Width=label},"Search","","",true,style,default,Pickable:false));
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

    static void StorageSearch(List<UiControl> controls, MyGuiScreenHost screens, string id, UiRect window, UiRect stock)
    {
        var details = controls.Find(c => c.Name is "TypeText" or "CapacityText");
        float top = details?.Bounds.Y ?? window.Y + 4;
        float left = details is null ? stock.X : stock.X + stock.Width * .44f;
        float height = Math.Min(24, stock.Y - top - 4);
        if (height <= 0) return;
        float clearWidth = Math.Min(height * 1.25f, stock.Width * .18f);
        float clearX = stock.X + stock.Width - clearWidth;
        if (clearX - left <= 8) return;
        AddStockSearch(controls, screens, id, window, new(left + 4, top, clearX - left - 8, height), clearX, clearWidth);
    }

    static void AddStockSearch(List<UiControl> controls, MyGuiScreenHost screens, string id, UiRect window,
        UiRect field, float clearX, float clearWidth)
    {
        string name = Prefix + id;
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
            controls.Add(new("NueSearchHint","TextBox",field,"Search items","","",true,style,default,Pickable:false,Clip:field.Intersect(window)));
        var clear = new UiRect(clearX, field.Y, clearWidth, field.Height);
        controls.Add(new("NueClear","Button",clear,"×",ClearAction,"Kenshi_Button1Skin",true,
            new Dictionary<string,string> { ["TextAlign"] = "Center", ["FontName"] = "Kenshi_PaintedTextFont_Medium" },
            default,Pickable:true,Clip:window));
    }

    static void Inventory(object __instance,string id,List<UiControl> controls)
    {
        string kind = (string)AccessTools.Method(Ui("InventoryWindows"),"ScreenKind").Invoke(null,[id])!;
        if (kind is not ("trader" or "inventory" or "bag" or "loot" or "loot-bag" or "storage")) return;
        if (controls.FirstOrDefault(c=>c.Type=="Window") is not {} window) return;
        var r = window.Bounds;

        var player = RouterPlayer.GetValue(__instance)!;
        var runtime = Property(player,"TooltipRuntime");
        var tips = (IDictionary)AccessTools.Field(Tips,"ItemTips").GetValue(runtime)!;
        var lookup = (Func<string,GameRecord?>?)Property(runtime,"ItemRecordOf");
        string q = Installed ? AddInventorySearch(player, id, kind, controls, r) : "";
        CharacterSnapshot? owner = null;
        if (GearComparison && kind != "storage")
        {
            var windows = Property(player,"InventoryWindows");
            owner = AccessTools.Method(windows.GetType(),"WindowCharacter").Invoke(windows,[kind=="trader" ? "inventory" : id]) as CharacterSnapshot;
        }
        if (q.Length == 0 && owner?.ItemGrid is null) return;
        foreach (var cell in controls.ToArray())
        {
            if (!(cell.Name.StartsWith("inventory-item-") || cell.Name.StartsWith("trade-item-") ||
                kind == "storage" && cell.Name.StartsWith("machine-item-")) || tips[cell.Name] is not {} tip) continue;
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
        string source = id is "loot" or "loot-bag" ? "loot" : "inventory";
        bool shared=(id is "inventory" or "bag" or "loot" or "loot-bag") && screens.IsOpen(source) && screens.Layout(source).Find("NuePreviewArea")!=null;
        if(kind=="trader") VendorSearch(controls,screens,id,r);
        else if (kind == "storage" && controls.Find(c => c.Name == StockView) is {} view)
            StorageSearch(controls, screens, id, r, view.Bounds);
        else if(!shared) Bar(controls,id,new(r.X+6,r.Y+4,Math.Min(160,r.Width*.35f),24));
        if(shared && id==source && screens.Focus?.Widget!=Prefix+source && Query(source).Length==0 &&
            controls.Find(c=>c.Name==Prefix+source) is {} field)
            controls.Add(new("NueSearchHint","TextBox",field.Bounds,"Search items","","",true,field.Properties,default,Pickable:false));
        return Query(shared ? source : id);
    }
}
