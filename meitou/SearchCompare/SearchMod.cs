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

public sealed class SearchMod : IMod
{
    const string Prefix = "NueSearch.";
    const string StockView = "scrollview_backpack_content";
    const string ClearAction = "nue-search-clear";
    static readonly Dictionary<string,string> Queries = new();
    static readonly ConditionalWeakTable<object,List<InventoryItemView>> Comparisons = new();
    static Type Ui(string name) => typeof(MyGuiScreenHost).Assembly.GetType("Meitou.Game.Ui." + name, true)!;
    static readonly Type Router = Ui("ScreenRouter"), Production = Ui("ProductionWindows"), Tips = Ui("TooltipRuntime");
    static readonly FieldInfo RouterPlayer = AccessTools.Field(Router,"<player>P");
    static readonly MethodInfo ToTip = AccessTools.Method(Tips.GetNestedType("ItemTip", BindingFlags.NonPublic)!, "Of",
        [typeof(InventoryItemView), typeof(TradeItemView), typeof(string)]);
    static readonly MethodInfo ItemLines = AccessTools.Method(Tips,"ItemLines");
    static readonly MethodInfo SectionOf = AccessTools.Method(typeof(ItemFactory),"SectionOf");

    internal static bool Installed { get; private set; }
    public void Initialize(ModContext context)
    {
        Installed = true;
        HarmonyMethod Hook(string name) => new(typeof(SearchMod),name);
        foreach (var method in new[]{"AddCraftContent","AddResearchContent"})
            context.Harmony.Patch(AccessTools.Method(Production,method), prefix:Hook(nameof(ListBar)), transpiler:Hook(nameof(FilterRows)));
        context.Harmony.Patch(AccessTools.Method(Router,"FaithfulAction"), prefix:Hook(nameof(Input)));
        context.Harmony.Patch(AccessTools.Method(Router,"AddFaithfulContent"), postfix:Hook(nameof(Inventory)));
        context.Harmony.Patch(AccessTools.Method(Router,"BindFaithfulScreen"), postfix:Hook(nameof(ReserveVendorSearch)));
        context.Harmony.Patch(ItemLines, postfix:Hook(nameof(Compare)));
    }

    static bool Input(MyGuiEvent e,object __instance)
    {
        if(e.Screen is ("inventory" or "loot" or "trader") && e.Widget=="NueClear" && e.Action==ClearAction)
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

    static void ReserveVendorSearch(string id, UiBindings __result, object __instance)
    {
        var b = __result;
        if (id != "trader" || !b.Positions.TryGetValue("Root", out var root)) return;
        var player = RouterPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)AccessTools.Field(player.GetType(),"FaithfulScreens").GetValue(player)!;
        var layout = screens.Layout(id);
        if (layout.Find("Content") is not {} content || layout.Find(StockView) is not {} view ||
            !b.RelativePositions.TryGetValue(view, out var bounds)) return;

        float contentHeight = root.Height * content.Position.Height;
        if (contentHeight <= 0) return;
        float inset = Math.Min(36 * b.TemplateScale / contentHeight, bounds.Height / 2);
        b.RelativePositions[view] = bounds with { Y = bounds.Y + inset, Height = bounds.Height - inset };
    }

    static void VendorSearch(List<UiControl> controls, MyGuiScreenHost screens, string id)
    {
        if (controls.Find(c => c.Name == "datapanel-label-0") is not {} money ||
            controls.Find(c => c.Name == StockView) is not {} view) return;
        var stock = view.Bounds;
        float moneyBottom = money.Bounds.Y + money.Bounds.Height;
        float top = Math.Max(moneyBottom, money.Clip is {} clip ? clip.Y + clip.Height : moneyBottom) + 3;
        float height = Math.Min(money.Bounds.Height * 1.4f, stock.Y - top - 4);
        if (height <= 0 || stock.Width <= 8) return;
        float clearWidth = Math.Min(height * 1.35f, stock.Width / 4);
        var field = new UiRect(stock.X, top, stock.Width - clearWidth - 4, height);
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
        controls.Add(new(name,"EditBox",field,query,"","Kenshi_EditBox",true,style,default,Pickable:true));
        if (!focused && query.Length == 0)
            controls.Add(new("NueSearchHint","TextBox",field,"Search items","","",true,style,default,Pickable:false));
        var clear = new UiRect(stock.X + stock.Width - clearWidth, top, clearWidth, height);
        controls.Add(new("NueClear","Button",clear,"×",ClearAction,"Kenshi_Button1",true,
            new Dictionary<string,string> { ["TextAlign"] = "Center" },default,Pickable:true));
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
        var windows = Property(player,"InventoryWindows");
        var owner = AccessTools.Method(windows.GetType(),"WindowCharacter").Invoke(windows,[kind=="trader" ? "inventory" : id]) as CharacterSnapshot;
        var screens=(MyGuiScreenHost)AccessTools.Field(player.GetType(),"FaithfulScreens").GetValue(player)!;
        string source = id is "loot" or "loot-bag" ? "loot" : "inventory";
        bool shared=(id is "inventory" or "bag" or "loot" or "loot-bag") && screens.IsOpen(source) && screens.Layout(source).Find("NuePreviewArea")!=null;
        if(kind=="trader") VendorSearch(controls,screens,id);
        else if(!shared) Bar(controls,id,new(r.X+6,r.Y+4,Math.Min(160,r.Width*.35f),24));
        if(shared && id==source && screens.Focus?.Widget!=Prefix+source && Query(source).Length==0 &&
            controls.Find(c=>c.Name==Prefix+source) is {} field)
            controls.Add(new("NueSearchHint","TextBox",field.Bounds,"Search items","","",true,field.Properties,default,Pickable:false));
        var q = Query(shared ? source : id);
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
            var equipped = grid.Items.Where(i => i.Section==slot || slot=="back" && i.Section=="hip").ToList();
            if (kind!="trader" && cell.Properties.TryGetValue("InventoryItem",out var index))
                equipped.RemoveAll(i=>i.Index.ToString()==index);
            if (equipped.Count>0) Comparisons.AddOrUpdate(tip,equipped);
        }
    }

    static void Compare(object __instance,object t,ref IReadOnlyList<TooltipLine> __result)
    {
        if (!Comparisons.TryGetValue(t,out var equipped)) return;
        var candidate = __result;
        var rows = candidate.ToList();
        foreach (var item in equipped)
        {
            var other = ToTip.Invoke(null,[item,null,null]);
            var lines = (IReadOnlyList<TooltipLine>)ItemLines.Invoke(__instance,[other])!;
            rows.Add(new("Equipped ("+item.Section+")",item.Name));
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line.Value)) continue;
                var hovered = candidate.FirstOrDefault(c => c.Key == line.Key);
                if (string.IsNullOrEmpty(hovered.Value) || hovered.Value == line.Value) continue;
                rows.Add(new(line.Key, $"{hovered.Value} vs {line.Value}"));
            }
        }
        __result = rows;
    }
}
