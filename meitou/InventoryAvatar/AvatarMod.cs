using HarmonyLib;
using Meitou.Simulation.Items;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Ui;
using Meitou.Game.Rendering.Ui;
using Meitou.Modding;
using Meitou.Simulation;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;

namespace Nue.InventoryAvatar;

public sealed partial class AvatarMod : IMod
{
    private const string Texture = "nue:inventory-avatar";
    private static readonly string[] Labels = ["Idle", "Attack stance", "Sitting on mat", "Sleeping / lying"];
    private sealed class PreviewState
    {
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public int choice, background, width = 280, height = 540, framedPose = -1;
        public float yaw = .12f, zoom = 1f, framePadding, eyeHeight;
        public bool expanded, backgroundsOpen;
        public CharacterId subject = CharacterId.None;
        public UiPainter? publishedPainter;
        public CharacterId publishedSubject = CharacterId.None;
        public EditorStageRequest? request;
        public Vector3[]? framePoints;
        public object? framedAsset;
        public long framedKey;
        public string? openRack;
        public UiRect bagArea;
#if NUE_UNIFIED
        public readonly Nue.Unified.CharacterInspector.View Inspector = new();
#endif
    }
    private static readonly PreviewState own = new(), other = new();
    private static PreviewState active = own, dragged = own;
    private static float lastDragX;
    private static MyGuiScreenHost? dragHost;
    private static readonly string[] Backgrounds = ["Desert", "Swamp", "Black"];
    private static string folder = "";
    private static MyGuiLayout inspection = null!;
    private static object? ui, renderer;

    private static Type GameType(string name) => typeof(IMod).Assembly.GetType("Meitou.Game." + name, true)!;
    private static readonly Type Player = GameType("PlayerInterface"), Host = GameType("GameHost"),
        Render = GameType("Rendering.Characters.CharacterRenderer");
    private static readonly FieldInfo PlayerField = AccessTools.Field(Host, "Player");
    private static readonly FieldInfo Screens = AccessTools.Field(Player, "FaithfulScreens");
    private static readonly MethodInfo GetInventory = AccessTools.PropertyGetter(Player, "InventoryWindows");
    private static readonly MethodInfo Owner = AccessTools.Method(GameType("Ui.InventoryWindows"), "WindowCharacter");
    private static readonly MethodInfo GetRenderer = AccessTools.PropertyGetter(Host, "ActiveCharacters");
    private static readonly PropertyInfo Stage = AccessTools.Property(Render, "StageRequest");
    private static readonly MethodInfo Draw = AccessTools.Method(Render, "DrawStage"), Resolve = AccessTools.Method(Render, "ResolveStage");
    private static readonly PropertyInfo Picture = AccessTools.Property(Render, "StageTexture");
    private static readonly FieldInfo Painter = AccessTools.Field(Player, "UiPainter");
    private static readonly PropertyInfo StageView = AccessTools.Property(Render, "StageView");
    private static string TextureName => active == other ? Texture+"-partner" : Texture;
    private static readonly FieldInfo Library = AccessTools.Field(Host, "AnimationLibrary");

    private static readonly FieldInfo Editor = AccessTools.Field(Host, "CharacterEditor");
    private static readonly MethodInfo Bar = AccessTools.PropertyGetter(Editor.FieldType, "Bar");
    private static readonly MethodInfo ClipTime = AccessTools.Method(Bar.ReturnType, "EditorClipTime");

    public void Initialize(ModContext context) => Install(context, true);

    internal void Install(ModContext context, bool preview)
    {
        folder = context.Folder;
        inspection = MyGuiReader.ReadLayout(Path.Combine(folder, "InventoryInspection.layout"));
        ConfigurePockets(inspection.Roots[0]);
        After(typeof(ItemFactory), nameof(ItemFactory.MainSize), nameof(PocketSize));
        context.Harmony.Patch(AccessTools.Method(typeof(Meitou.Simulation.Saving.SavedKit), "ToInventory"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(BeforeLoad)),
            postfix: new HarmonyMethod(typeof(AvatarMod), nameof(FitSavedPockets)),
            finalizer: new HarmonyMethod(typeof(AvatarMod), nameof(EndLoad)));
        foreach (var slot in Accessories)
            context.AddEquipmentSlot(new ModSlot(slot.Section, slot.Fcs, 2, 2, slot.X, 305));
        After(GameType("Ui.InventoryLayout"), "AddModSlots", nameof(PlaceAccessories));
        if (!preview) return;
        After(Render, "StageCamera", nameof(FrameAvatar));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.ScreenRouter"), "BindFaithfulScreen"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(SelectLayout)));
        After(GameType("Ui.InventoryWindows"), "SectionsOf", nameof(IncludeLimbTargets));
        After(GameType("Ui.InventoryWindows"), "BagWanted", nameof(DefaultBag));
        After(Layout, "ArrangeEquipmentGrid", nameof(FitPocketGrid));
        After(Layout, "ArrangeContainerGrid", nameof(FitBagCells));
        After(Layout, "BindContainerGeometry", nameof(BindStorageTradeGeometry));
        After(Layout, "ArrangeContainerGrid", nameof(ArrangeStorageTradeGrid));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.InventoryWindows"),"Spot"),
            prefix:new HarmonyMethod(typeof(AvatarMod),nameof(WeaponDrop)));
        void After(Type type, string target, string method) => context.Harmony.Patch(AccessTools.Method(type,target),
            postfix: new HarmonyMethod(typeof(AvatarMod),method));
        foreach(string name in new[]{"PlaceCharacterWindow","PlaceBagWindow"})
            context.Harmony.Patch(AccessTools.Method(Layout,name),prefix:new HarmonyMethod(typeof(AvatarMod),nameof(NormalPlacement)));
        After(GameType("Ui.InventoryLayout"), "BindInventoryGeometry", nameof(DockInventory));
        After(GameType("Ui.ScreenRouter"), "AddInventoryContent", nameof(Controls));
        After(GameType("Ui.ScreenRouter"), "FaithfulAction", nameof(Click));
#if NUE_UNIFIED
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.ScreenRouter"), "FaithfulAction"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(InspectorAction)));
#endif
        After(Host, "FillDrawList", nameof(Prepare));
        After(typeof(MyGuiScreenHost),"Raise",nameof(KeepBagAbove));
        After(Render,"GatherPortraitKeys",nameof(IncludePartnerPose));
        context.Harmony.Patch(AccessTools.Method(Render,"Dispose"), prefix:new HarmonyMethod(typeof(AvatarMod),nameof(ReleasePartnerTargets)));
        After(Render, "DrawPortraits", nameof(Paint));
        void Observe(string target, string method) => context.Harmony.Patch(
            AccessTools.Method(typeof(MyGuiScreenHost), target, target == "PointerDown" ? [typeof(Vector2), typeof(Func<string,UiControl,Vector2,bool>)] : null), postfix: new HarmonyMethod(typeof(AvatarMod), method));
        Observe("PointerDown", nameof(StartDrag));
        Observe("PointerMove", nameof(Rotate));
        Observe("PointerUp", nameof(EndDrag));
        Observe("Wheel", nameof(Zoom));
    }

    private static readonly (string Section, string Label, int Fcs, int X)[] Accessories =
        [("nue.eyes", "EYES", 4, 12), ("nue.hands", "HANDS", 10, 75), ("nue.neck", "NECK", 11, 138)];

    private static readonly bool ShowAccessorySockets = false;

    // AddModSlots runs before item icons and hit targets are generated from the slot bounds.
    private static void PlaceAccessories(List<UiControl> controls)
    {
        if (!ShowAccessorySockets)
        {
            controls.RemoveAll(c => Accessories.Any(slot => slot.Section == c.Name));
            return;
        }
        if (controls.Find(c=>c.Name=="nue.eyes") is not {} first ||
            controls.Find(c=>c.Name=="datapanel") is not {} summary) return;
        float size=first.Bounds.Height, labelHeight=size*.34f;
        // In the inspection window the summary follows the boots. In linked windows it precedes the grid.
        bool belowGear=controls.Find(c=>c.Name=="boots") is {} boots && summary.Bounds.Y>boots.Bounds.Y;
        float y=belowGear ? summary.Bounds.Y-size-labelHeight*.65f : summary.Bounds.Y+summary.Bounds.Height+labelHeight;
        float step=Math.Max(first.Bounds.Width+3,summary.Bounds.Width/3);
        for (int i=0;i<Accessories.Length;i++)
        {
            var (name,label,_,_)=Accessories[i];
            int index=controls.FindIndex(c=>c.Name==name);
            if(index<0) continue;
            var tile=controls[index];
            var bounds=tile.Bounds with { X=summary.Bounds.X+i*step,Y=y };
            controls[index]=tile with { Bounds=bounds,Skin="Kenshi_InventorySlotSkin" };
            controls.Add(new(name+"-label","TextBox",bounds with { Y=y-labelHeight,Height=labelHeight },
                label,"","Kenshi_TextboxPaintedText",true,new Dictionary<string,string>{{"TextAlign","Left VCenter"}},
                default,Pickable:false,Clip:tile.Clip));
        }
    }

    private static readonly Type Layout = GameType("Ui.InventoryLayout");
    private static readonly FieldInfo LayoutPlayer = Layout.GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);
    private static readonly FieldInfo Width = AccessTools.Field(Player,"Width"), Height = AccessTools.Field(Player,"Height");
    private static readonly MethodInfo GetLayout = AccessTools.PropertyGetter(Player,"InventoryLayout"),
        Linked = AccessTools.Method(Layout,"InWindowRow");

    private static CharacterSnapshot? Subject(object player)
    {
        var screens=(MyGuiScreenHost)Screens.GetValue(player)!;
        if (!screens.IsOpen("inventory")) return null;
        var inventories = GetInventory.Invoke(player,null);
        var owner = Owner.Invoke(inventories,["inventory"]) as CharacterSnapshot;
        var partner = screens.IsOpen("loot") ? Owner.Invoke(inventories,["loot"]) as CharacterSnapshot : null;
        if (active == other)
            return TradePartner(screens.IsOpen("trader"),owner,partner);
        if (partner == null && LinkedStorage(player) == null && (bool)Linked.Invoke(GetLayout.Invoke(player,null),null)!) return null;
        return owner is {Animal:false, Appearance:not null} ? owner : null;
    }

    private static CharacterSnapshot? TradePartner(bool merchant, CharacterSnapshot? owner, CharacterSnapshot? partner) =>
        !merchant && owner != null && partner is { Animal:false, Appearance:not null } && partner.Id != owner.Id
            ? partner : null;

    private static bool NormalPlacement(object __instance) => Subject(LayoutPlayer.GetValue(__instance)!)==null;

    private static void SelectLayout(string id, object __instance)
    {
        if (id is not ("inventory" or "loot") && !IsStorageScreen(id)) return;
        var player = RouterPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (IsStorageScreen(id))
        {
            SelectStorageLayout(screens, id);
            return;
        }
        active = id == "loot" ? other : own;
        var character = Owner.Invoke(GetInventory.Invoke(player, null), [id]) as CharacterSnapshot;
        var current = screens.Layout(id);
        if (character is { Animal: true })
        {
            var animal = NativeInventoryLayout(player, animal: true);
            if (current.Find("MainSectionName") == null ||
                current.Roots[0].Position != animal.Roots[0].Position)
                screens.Replace(id, animal);
            active.bagArea = default;
            return;
        }

        bool inspecting = Subject(player) != null;
        if (inspecting)
        {
            if (current.Find("NuePreviewArea") == null)
                screens.Replace(id, id == "loot" ? MirroredInspection() : inspection);
            return;
        }
        if (current.Find("NuePreviewArea") != null || current.Find("MainSectionName") != null)
            screens.Replace(id, NativeInventoryLayout(player, animal: false));
        active.bagArea = default;
    }

    private static MyGuiLayout NativeInventoryLayout(object player, bool animal)
    {
        var trade = AccessTools.PropertyGetter(Player, "TradeWindows").Invoke(player, null)!;
        string field = animal ? "AnimalInventoryLayout" : "CharacterInventoryLayout";
        return (MyGuiLayout)AccessTools.Field(trade.GetType(), field).GetValue(trade)!;
    }

    private static void DockInventory(string id, UiBindings b, object __instance)
    {
        if (!b.Positions.TryGetValue("Root", out var root)) return;
        active = id is "loot" or "loot-bag" ? other : own;
        bool paired = DockTrade(id, b, __instance, root);
        if (id is not ("inventory" or "bag" or "loot" or "loot-bag")) return;
        var player = LayoutPlayer.GetValue(__instance)!;
        if (paired && id == "loot" &&
            Owner.Invoke(GetInventory.Invoke(player, null), ["loot"]) is CharacterSnapshot { IsPlayer: true })
        {
            b.Disabled.Remove("ArrangeButton");
            b.Actions["ArrangeButton"] = "ArrangeButton";
        }
        if (Subject(player) is not {} subject) return;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (id is "bag" or "loot-bag")
        {
            if (active.bagArea.Width <= 0 || active.bagArea.Height <= 0) return;
            float scale = Math.Min(1, Math.Min(active.bagArea.Width / root.Width, active.bagArea.Height / root.Height));
            b.Positions["Root"] = root with { Width = root.Width * scale, Height = root.Height * scale };
            b.WindowPosition = new Vector2(active.bagArea.X, active.bagArea.Y) - screens.GetOffset(id);
            return;
        }

#if NUE_UNIFIED
        if (Nue.Unified.CharacterInspector.Enabled)
        {
            active.Inspector.Use(subject.Id);
            bool hasBag = subject.ItemGrid is { BagWidth: > 0, BagHeight: > 0 };
            bool bagOpen = screens.IsOpen(id == "loot" ? "loot-bag" : "bag");
            bool inspectorShown = !bagOpen && (hasBag || !active.Inspector.Hidden);
            b.Text["OpenBagButton"] = inspectorShown ? "OPEN BAG | HIDE CHAR INSPECTOR" : "CLOSE BAG | SHOW CHAR INSPECTOR";
            b.Disabled.Remove("OpenBagButton");
            b.Actions["OpenBagButton"] = hasBag ? "OpenBagButton" : Nue.Unified.CharacterInspector.ActionPrefix + "toggle";
        }
        if (Nue.SearchCompare.SearchMod.Installed)
        {
            b.Disabled.Remove("NueClear");
            b.Actions["NueClear"]="nue-search-clear";
        }
        else
#endif
        {
            b.Hidden.Add("NueClear");
            b.Hidden.Add("NueSearch."+id);
        }
        var origin = inspection.Roots[0].Position.RelativeTo(new(0, 0, (int)Width.GetValue(player)!, (int)Height.GetValue(player)!), true);
        if (!paired) b.WindowPosition = new Vector2(origin.X, origin.Y) - screens.GetOffset(id);
        // The native character fitter bottom-anchors the heading for its own template.
        var title = screens.Layout(id).Find("InventoryMainTitle")!;
        b.RelativePositions[title] = title.Position;
    }

    private static readonly string[] LimbSections = ["left_arm_replace","right_arm_replace","left_leg_replace","right_leg_replace"];
    private static void IncludeLimbTargets(string screen, ref (string,string)[] __result)
    {
        if (screen is "inventory" or "loot") __result = [..__result, ..LimbSections.Select(n => (n,n))];
    }

    private static void DefaultBag(CharacterId id, HashSet<CharacterId> ____bagClosed, ref bool __result, object __instance)
    {
        if (!__result && !____bagClosed.Contains(id) && Owner.Invoke(__instance,["inventory"]) is CharacterSnapshot {Animal:false} owner && owner.Id == id && ((MyGuiScreenHost)Screens.GetValue(InventoryPlayer.GetValue(__instance)!)!).IsOpen("inventory")) __result = true;
    }

    private static void FitPocketGrid(List<UiControl> controls, InventoryView grid)
    {
        if (controls.Find(c => c.Name == "NuePocketArea") is not {} anchor) return;
        int index = controls.FindIndex(c => c.Name == "main");
        if (index < 0 || grid.Width <= 0 || grid.Height <= 0) return;
        var slot = controls[index];
        float side = Math.Min(slot.Bounds.Width / grid.Width,
            Math.Min(anchor.Bounds.Width / grid.Width, anchor.Bounds.Height / grid.Height));
        controls[index] = WithCells(slot with { Bounds = anchor.Bounds with {
            Width = side * grid.Width, Height = side * grid.Height }, Clip = anchor.Bounds }, grid.Width, grid.Height);
        int arrangeIndex = controls.FindIndex(c => c.Name == "ArrangeButton");
        if (arrangeIndex >= 0)
        {
            var button = controls[arrangeIndex];
            var gridBounds = controls[index].Bounds;
            float gap = button.Bounds.Y - (anchor.Bounds.Y + anchor.Bounds.Height);
            controls[arrangeIndex] = button with { Bounds = button.Bounds with {
                X = gridBounds.X + (gridBounds.Width - button.Bounds.Width) / 2,
                Y = gridBounds.Y + gridBounds.Height + gap } };
        }

    }

    private static UiControl WithCells(UiControl slot, int cols, int rows) => slot with {
        Properties = new Dictionary<string,string>(slot.Properties) {
            ["TileSize"] = FormattableString.Invariant($"{slot.Bounds.Width / cols} {slot.Bounds.Height / rows}"),
            ["NueFittedGrid"] = "true" } };

    private static void FitBagCells(string id, List<UiControl> controls, object __instance)
    {
        if (id is not ("bag" or "loot-bag")) return;
        var player = LayoutPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        var owner = Owner.Invoke(GetInventory.Invoke(player,null), [id]) as CharacterSnapshot;
        if (owner == null && (screens.IsOpen("trader") || screens.IsOpen("loot")))
            owner = Owner.Invoke(GetInventory.Invoke(player, null), [id]) as CharacterSnapshot;
        if (owner?.ItemGrid is not {BagWidth: >0, BagHeight: >0} grid) return;
        int i = controls.FindIndex(c => c.Name == "backpack_content");
        if (i >= 0) controls[i] = WithCells(controls[i], grid.BagWidth, grid.BagHeight);
    }

    private static readonly FieldInfo RouterPlayer = GameType("Ui.ScreenRouter").GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);


    private static void Controls(string id, List<UiControl> controls,object __instance)
    {
        active = id == "loot" ? other : own;
        if (id is not ("inventory" or "loot") || controls.Find(c => c.Name == "NuePreviewArea") is not {} preview ||
            controls.Find(c=>c.Type=="Window") is not {} window) return;
        var player = RouterPlayer.GetValue(__instance)!;
        if (Subject(player) is not {} owner) return;
#if NUE_UNIFIED
        var markedCells = CaptureMarkedCells(controls);
#endif
        var image = preview.Bounds;
        var root = window.Bounds;
        active.bagArea = controls.First(c => c.Name == "NueBagArea").Bounds;
        var pocket = controls.Find(c => c.Name == "main");
        for (int i=0; i<LimbSections.Length; i++)
        {
            string section=LimbSections[i];
            int at=controls.FindIndex(c=>c.Name==section);
            if(at<0) continue;
            var native=controls[at];
            int rows = i < 2 ? 4 : 5;
            float unit=Math.Min(native.Bounds.Width/2,native.Bounds.Height/rows);
            var rect=native.Bounds with {Width=2*unit,Height=rows*unit};
            if (i >= 2 && pocket is not null)
            {
                rect = rect with { Y = pocket.Bounds.Y + pocket.Bounds.Height - rect.Height };
                if (i == 2)
                {
                    int titleIndex = controls.FindIndex(c => c.Name == "NueLegTitle");
                    if (titleIndex >= 0)
                    {
                        var title = controls[titleIndex];
                        controls[titleIndex] = title with { Bounds = title.Bounds with {
                            Y = title.Bounds.Y + rect.Y - native.Bounds.Y } };
                    }
                }
            }
            var slot=WithCells(native with {Bounds=rect},2,rows);
            controls[at]=slot with {Properties=new Dictionary<string,string>(slot.Properties) {
                ["State"]=((owner.LimbBits >> (2*i)) & 3)==0 ? "disabled":"normal" }};
            foreach(var item in owner.ItemGrid!.Items.Where(v=>v.Section==section))
            {
                string name=$"inventory-item-{owner.Id}-{item.Index}";
                var parts=controls.Where(c=>c.Name==name || c.Name.StartsWith(name+"-",StringComparison.Ordinal)).ToList();
                foreach(var part in parts) controls.Remove(part);
                PlaceItem(controls,parts,name,rect,item);
            }
        }
        active.width = Math.Max(1,(int)image.Width);
        active.height = Math.Max(1,(int)image.Height);
        var backdrop = new Dictionary<string,string>();
        if (active.background < 2) backdrop["ImageTexture"] = Meitou.Game.Rendering.Ui.UiPainter.IconTexture(
            Path.Combine(folder, Backgrounds[active.background] + ".png"));
        controls.Add(new("NueBackdrop", backdrop.Count == 0 ? "Widget" : "ImageBox", image,"","","",true,
            backdrop, backdrop.Count == 0 ? new Vector4(0,0,0,1) : default, Pickable:false, Clip:root));
        if (active.publishedSubject == owner.Id && Painter.GetValue(player) is UiPainter painter &&
            ReferenceEquals(active.publishedPainter,painter) && painter.HasTexture(TextureName))
            controls.Add(new("NueAvatar","ImageBox",image,"","","",true,
                new Dictionary<string,string>{{"ImageTexture",TextureName},{"ImageFlipV","true"}},default,Pickable:true,Clip:root));
        ArrangeEquipment(controls,root,owner,player);
#if NUE_UNIFIED
        PlaceMarkers(controls, player, markedCells);
        if (Nue.Unified.CharacterInspector.Enabled)
        {
            var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
            active.Inspector.Use(owner.Id);
            if (!screens.IsOpen(id == "loot" ? "loot-bag" : "bag") &&
                (owner.ItemGrid is { BagWidth: > 0, BagHeight: > 0 } || !active.Inspector.Hidden))
            {
                var hit = screens.HitTest((Vector2)Nue.Unified.CharacterInspector.Mouse.GetValue(player)!);
                Nue.Unified.CharacterInspector.Draw(controls, player, owner, active.bagArea, active.Inspector,
                    hit is {} target && target.Screen == id ? target.Control.Action : null);
            }
        }
#endif
        void Menu(string anchor, string action, string[] options, int selected, bool open)
        {
            var bounds = controls.First(c => c.Name == anchor).Bounds;
            var style = new Dictionary<string,string> { ["TextAlign"] = anchor == "NueBackdropMenu" ? "Right VCenter" : "Left VCenter" };
            void Button(int index, string label, float y) => controls.Add(new(anchor+index,"Button",bounds with { Y=y },
                label,action+index,"Kenshi_Button1",true,style,default,Pickable:true,Clip:root));
            Button(-1,options[selected]+"  ▾",bounds.Y);
            if (open) for(int i=0;i<options.Length;i++) Button(i,options[i],bounds.Y+(i+1)*bounds.Height);
        }
        Menu("NuePoseMenu","nue-avatar:",Labels,active.choice,active.expanded);
        Menu("NueBackdropMenu","nue-background:",Backgrounds,active.background,active.backgroundsOpen);
    }

    private static readonly MethodInfo Raise=AccessTools.Method(typeof(MyGuiScreenHost),"Raise");
    private static void KeepBagAbove(MyGuiScreenHost __instance,string id)
    {
        var bag = id == "loot" ? "loot-bag" : "bag";
        if(id is ("inventory" or "loot") && __instance.IsOpen(bag) && __instance.Layout(id).Find("NuePreviewArea") != null) Raise.Invoke(__instance,[bag]);
    }

    private static bool OverAvatar(MyGuiScreenHost host, Vector2 p)
    {
        if (host.HitTest(p) is not { Control.Name: "NueAvatar" } hit) return false;
        active = hit.Screen == "loot" ? other : own;
        return true;
    }
    private static void StartDrag(MyGuiScreenHost __instance, Vector2 p)
    {
        dragHost = null;
        if (!OverAvatar(__instance, p)) return;
        dragged = active;
        dragHost = __instance;
        lastDragX = p.X;
    }

    private static void Rotate(MyGuiScreenHost __instance, Vector2 p)
    {
        if (dragHost != __instance) return;
        active = dragged;
        active.yaw = MathF.IEEERemainder(active.yaw - (p.X - lastDragX) * MathF.Tau / active.width, MathF.Tau);
        lastDragX = p.X;
    }

    private static void EndDrag() => dragHost = null;

    private static void Zoom(MyGuiScreenHost __instance, Vector2 p, float steps)
    {
        if (!OverAvatar(__instance, p)) return;
        active.zoom = Math.Clamp(active.zoom * MathF.Pow(.87f, steps), .9f, 2f);
    }

    private static void Click(MyGuiEvent e)
    {
        active = e.Screen == "loot" ? other : own;
        if (e.Screen is ("loot" or "loot-bag") && e.Widget == "ArrangeButton" && ui != null &&
            Owner.Invoke(GetInventory.Invoke(ui,null), ["loot"]) is CharacterSnapshot { IsPlayer:true } member)
        {
            var session = (Meitou.Game.Host.WorldSession)AccessTools.Field(Player,"Session").GetValue(ui)!;
            AccessTools.Method(Player,"Say").Invoke(ui,[new ArrangeInventory(member.Id,e.Screen=="loot-bag") {Tick=session.World.Tick}]);
            return;
        }
        if(e.Screen is ("inventory" or "loot") && e.Action is "nue-rack:back" or "nue-rack:hip")
        {
            var section=e.Action[9..];
            active.openRack=active.openRack==section ? null : section;
            return;
        }
        if(e.Screen is ("inventory" or "loot") && e.Action.StartsWith("nue-background:") && int.TryParse(e.Action[15..],out int bg))
        {
            active.expanded = false;
            if(bg == -1) active.backgroundsOpen = !active.backgroundsOpen;
            else if ((uint)bg < Backgrounds.Length) { active.background = bg; active.backgroundsOpen = false; }
            return;
        }
        if(e.Screen is not ("inventory" or "loot") || !e.Action.StartsWith("nue-avatar:") || !int.TryParse(e.Action[11..],out int index)) return;
        active.backgroundsOpen = false;
        if(index==-1) active.expanded=!active.expanded;
        else if((uint)index<Labels.Length) { active.choice=index; active.expanded=false; active.Clock.Restart(); }
    }

#if NUE_UNIFIED
    private static bool InspectorAction(MyGuiEvent e)
    {
        const string prefix = Nue.Unified.CharacterInspector.ActionPrefix;
        if (e.Screen is not ("inventory" or "loot") || !e.Action.StartsWith(prefix, StringComparison.Ordinal))
            return true;
        var view = (e.Screen == "loot" ? other : own).Inspector;
        view.Act(e.Action[prefix.Length..]);
        return false;
    }
#endif

    private static void Prepare(object __instance, CharacterDrawList list)
    {
        ui=PlayerField.GetValue(__instance);
        var nextRenderer = GetRenderer.Invoke(__instance,null);
        if (!ReferenceEquals(renderer,nextRenderer))
        {
            own.publishedPainter = other.publishedPainter = null;
            own.request = other.request = null;
        }
        renderer = nextRenderer;
        if(renderer==null || ui==null) return;
        active = other;
        PrepareSubject(__instance,list);
        if (Equals(Stage.GetValue(renderer),other.request)) Stage.SetValue(renderer,null);
        active = own;
        PrepareSubject(__instance,list);
    }

    private static void PrepareSubject(object __instance, CharacterDrawList list)
    {
        if(Stage.GetValue(renderer) is EditorStageRequest prior && prior==active.request) Stage.SetValue(renderer,null);
        active.request=null;
        var screens=(MyGuiScreenHost)Screens.GetValue(ui)!;
        if(!screens.IsOpen("inventory") || screens.IsOpen("editor") || Stage.GetValue(renderer)!=null ||
           Subject(ui!) is not CharacterSnapshot { Animal: false, Appearance: {} look } c)
        {
            active.publishedPainter=null;
            if (dragged == active) dragHost=null; active.subject=CharacterId.None; active.openRack=null; active.expanded=active.backgroundsOpen=false; active.choice=0; return;
        }
        if(active.subject!=c.Id) { active.publishedPainter=null; active.framePoints=null; active.subject=c.Id; active.openRack=null; active.yaw=.12f; active.zoom=1f; dragHost=null; active.choice=0; active.expanded=active.backgroundsOpen=false; active.Clock.Restart(); }
        float t=(float)active.Clock.Elapsed.TotalSeconds;
        var bar = Bar.Invoke(Editor.GetValue(__instance), null);
        CharacterPose StandingPose(string name) =>
            new(name, (float)ClipTime.Invoke(bar, [name, t])!, 1);
        IReadOnlyList<CharacterPose> poses = active.choice switch {
            2 => [new("sitting idle",t*.55f,1)],
            3 => [new("sleeponfloor",t*.1f,1)],
            _ => [StandingPose(look.Body?.IdleStance ?? "idle_stand_relax")] };
        bool drawn = false, backDrawn = false;
        if (active.choice == 1 && Library.GetValue(__instance) is AnimationLibrary lib)
        {
            var preferred = c.BackDrawn ? WeaponShown.BackDrawn : WeaponShown.Drawn;
            var weapon = look.Parts.FirstOrDefault(p => p.Shown == preferred)
                ?? look.Parts.FirstOrDefault(p => p.Shown is WeaponShown.Drawn or WeaponShown.BackDrawn);
            var hand = weapon == null ? HandHold.Empty : HandHold.OfCategory(WeaponData.From(weapon.Record).AnimationCategory);
            var layers = CombatLayers(lib, hand);
            if (layers.Count > 0)
            {
                var combat = new CharacterPose[layers.Count];
                for (int i = 0; i < combat.Length; i++)
                {
                    var (index, weight) = layers[i];
                    var clip = lib.Definitions[index];
                    // A movement-driven layer holds its standing phase at zero speed.
                    float time = clip.MoveSpeed == 0 ? (float)ClipTime.Invoke(bar, [clip.Name, t])! :
                        clip.Synchs ? (clip.SynchOffset - MathF.Floor(clip.SynchOffset)) * ClipLength(__instance, clip.Clip) : 0;
                    combat[i] = new CharacterPose(clip.Name, time, weight);
                }
                poses = combat;
                drawn = weapon != null;
                backDrawn = weapon?.Shown == WeaponShown.BackDrawn;
            }
        }
        long key=long.MinValue+((long)(uint)c.Id.Slot<<32)+(uint)c.Id.Generation;
        list.Items.Add(new CharacterInstance(key,look,c.Position,0,poses) { Hidden=true, FullRateAnimation=true,
            Scale=c.Scale,Build=c.Build,WeaponDrawn=drawn,BackDrawn=backDrawn,UnitMovement=active.choice==3 });
        float distance=active.choice==3 ? 78 : 54*Math.Max(1,c.Scale);
        active.request=new(key,active.width,active.height,active.yaw,active.choice==3?.87f:.09f,distance*active.zoom,false,.66f,0);
        Stage.SetValue(renderer,active.request);
    }

    private static readonly FieldInfo Lengths = AccessTools.Field(Host, "AnimationLengths");

    private static float ClipLength(object host, string clip) =>
        (Lengths.GetValue(host) as AnimationLengths)?.Of(clip) ?? 1;

    private static List<(int Index, float Weight)> CombatLayers(AnimationLibrary library, HandHold hand)
    {
        var stance = new AnimationStance { Combat = true, Right = hand };
        List<(int Index, float Weight)> layers = [];
        int guard = library.CombatGuard(stance, 0);
        if (guard < 0) return layers;
        if (library.Definitions[guard].Area != AnimationArea.All)
            library.Movement(AnimationArea.Lower, 0, stance, layers);
        layers.Add((guard, 1));
        return layers;
    }

    private static void Paint(object __instance)
    {
        active = own;
        PaintSubject(__instance);
        if (other.request == null || !ReferenceEquals(renderer,__instance) || Stage.GetValue(renderer)!=null) return;
        active = other;
        SwapTargets(__instance);
        try { Stage.SetValue(renderer,other.request); PaintSubject(__instance); }
        finally { SwapTargets(__instance); active = own; }
    }

    private static void PaintSubject(object __instance)
    {
        if(active.request==null || !ReferenceEquals(renderer,__instance) || !Equals(Stage.GetValue(renderer),active.request)) return;
        float clearColour = PreviewStudio.Background;
        try {
            PreviewStudio.Background = -1;
            Draw.Invoke(renderer,null);
            Resolve.Invoke(renderer,null);
            active.publishedPainter = null;
            if (StageView.GetValue(renderer) != null && Picture.GetValue(renderer) is ValueTuple<GpuTexture,SampledTexture> image &&
                Painter.GetValue(ui) is UiPainter painter)
            {
                painter.SetTexture(TextureName,image.Item1,image.Item2);
                active.publishedSubject = active.subject;
                active.publishedPainter = painter;
            }
        } finally { PreviewStudio.Background = clearColour; Stage.SetValue(renderer,null); active.request=null; }
    }
}
