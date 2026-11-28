using HarmonyLib;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Simulation.Items;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Data.Characters;
using Meitou.Data.Ui;
using Meitou.Game.Rendering.Characters;
using Meitou.Game.Rendering.Frame;
using Meitou.Game.Rendering.Gpu;
using Meitou.Game.Host.Input;
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
        public bool showAccessories = true;
        public CharacterId subject = CharacterId.None;
        public UiPainter? publishedPainter;
        public CharacterId publishedSubject = CharacterId.None;
        public EditorStageRequest? request;
        public Vector3[]? framePoints;
        public object? framedAsset;
        public long framedKey;
        public string? openRack;
        public UiRect bagArea;
        public float inventoryCell, inventoryTitleHeight;
        public bool? showHealthOverride;
        public bool mainHudShowsStats;
#if NUE_UNIFIED
        public readonly Nue.Unified.CharacterInspector.View Inspector = new();
#endif
    }
    private static readonly PreviewState own = new(), other = new();
    private static PreviewState active = own, dragged = own;
    private static float lastDragX;
    private static MyGuiScreenHost? dragHost;
    private static readonly string[] Backgrounds = ["Desert", "Swamp", "Black"];
#if NUE_UNIFIED
    private static readonly (string Widget, string Caption)[] InventoryCaptions =
    [
        ("InventoryMainTitle", "INVENTORY"), ("ArrangeButton", "ARRANGE"), ("lbBackpack", "BACKPACK"),
        ("lbWeapon1", "PRIMARY"), ("lbWeapon2", "SECONDARY"), ("lbShirt", "SHIRT"),
        ("lbHead", "HELMET"), ("lbPants", "PANTS"), ("lbArmor", "BODY ARMOR"),
        ("lbBoots", "BOOTS"), ("lbBelt", "BELT"), ("NueArmTitle", "ARMS"), ("NueLegTitle", "LEGS")
    ];
#endif
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
    private static string InventoryText(string english)
    {
#if NUE_UNIFIED
        return Nue.SearchCompare.NueText.Get(english);
#else
        return english;
#endif
    }
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
            context.AddEquipmentSlot(new ModSlot(slot.Section, slot.Fcs, slot.Columns, slot.Rows, slot.X, slot.Y)
            {
                MaxItems = slot.Limit,
                Secondary = slot.Secondary,
                RollFromClothing = !slot.Secondary
            });
        context.Harmony.Patch(AccessTools.Method(typeof(CharacterAppearance), "WornArmour", [typeof(List<GameRecord>)]),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(KeepAllWornArmour)));
        After(GameType("Ui.InventoryLayout"), "AddModSlots", nameof(PlaceAccessories));
        if (!preview) return;
        After(Render, "StageCamera", nameof(FrameAvatar));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.ScreenRouter"), "BindFaithfulScreen"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(SelectLayout)),
            postfix: new HarmonyMethod(typeof(AvatarMod), nameof(BindTradeChrome)));
        After(GameType("Ui.ScreenRouter"), "AddFaithfulContent", nameof(FitTradeArrangeButton));
        After(typeof(MyGuiScreenHost), "PlaceRoots", nameof(PlaceVendorViewport));
        After(GameType("Ui.TradeWindows"), "ArrangeTraderWindow", nameof(FinishVendorLayout));
        var presentation = typeof(MyGuiLayout).Assembly.GetType("Meitou.Data.Ui.MyGuiPresentation", true)!;
        context.Harmony.Patch(AccessTools.Method(presentation, "VisitScrollView"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(PrepareVendorScrollView)));
        After(GameType("Ui.InventoryWindows"), "SectionsOf", nameof(IncludeLimbTargets));
        After(GameType("Ui.InventoryWindows"), "BagWanted", nameof(DefaultBag));
        After(Layout, "ArrangeEquipmentGrid", nameof(FitPocketGrid));
        After(Layout, "ArrangeEquipmentGrid", nameof(FitAnimalGrid));
        context.Harmony.Patch(AccessTools.Method(Layout, "TrimAnimalWindow"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(KeepAnimalPanelHeight)));
        After(Layout, "ArrangeContainerGrid", nameof(FitBagCells));
        context.Harmony.Patch(AccessTools.Method(GameType("Ui.TradeWindows"), "SyncLootScreen"),
            prefix: new HarmonyMethod(typeof(AvatarMod), nameof(EndTradeOnPartnerSelection)));
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
        After(Player, "MouseDown", nameof(StartDrag));
        After(Player, "MouseMove", nameof(Rotate));
        After(Player, "MouseUp", nameof(EndDrag));
        After(typeof(MyGuiScreenHost), "Wheel", nameof(Zoom));
    }

    private static readonly (string Section, string Label, int Fcs, int Columns, int Rows,
        int Limit, bool Secondary, int X, int Y)[] Accessories =
    [
        ("eyes_hats", "EXTRA HAT", 3, 3, 1, 1, true, 280, 294),
        ("eyes_eyes", "EYES", 4, 3, 1, 1, false, 280, 240),
        ("eyes_belts", "EXTRA BELT", 14, 3, 1, 1, true, 280, 267),
        ("gloves", "HANDS", 10, 2, 2, 4, false, 362, 240),
        ("neck", "NECK", 11, 2, 2, 1, false, 424, 240)
    ];

    private static bool KeepAllWornArmour(List<GameRecord> items, ref List<GameRecord> __result)
    {
        // The native collector replaces earlier armour with the same FCS attach slot.
        // Equipment in secondary sections still shares the attachment point with the primary section.
        __result = items.FindAll(item => item.Type == FcsRecordType.Armour);
        return false;
    }

    // The native item icons and hit targets are arranged after these slot controls.
    private static void PlaceAccessories(List<UiControl> controls)
    {
        if (controls.Find(c => c.Name == "NueAccessoriesArea") is not {} strip) return;
        var clip = strip.Bounds.Intersect(strip.Clip ?? strip.Bounds);
        if (clip.Width <= 0 || clip.Height <= 0) return;
        bool showing = strip.Properties.GetValueOrDefault("NueShowAccessories") == "true";
        if (controls.Find(c => c.Name == "NueToggleAccessoriesArea") is {} toggle)
        {
            controls.Add(new("NueToggleAccessories", "Button", toggle.Bounds,
                InventoryText(showing ? "LIMBS" : "EXTRA SLOTS"), "nue-toggle-accessories", "Kenshi_Button1Skin",
                true, new Dictionary<string, string> { ["TextAlign"] = "Center VCenter",
                    ["FontName"] = "Kenshi_StandardFont_Small" }, default,
                Pickable: true, Clip: toggle.Clip ?? toggle.Bounds));
        }
        if (!showing)
        {
            controls.RemoveAll(c => Accessories.Any(slot => slot.Section == c.Name));
            return;
        }
        if (controls.Find(c => c.Name == "main") is not {} inventory) return;
        var tileSize = inventory.Properties.TryGetValue("TileSize", out var dimensions)
            ? ReadDimensions(dimensions, 2) : null;
        float cellWidth = tileSize?[0] ?? inventory.Bounds.Width / pocketColumns;
        float cellHeight = tileSize?[1] ?? inventory.Bounds.Height / pocketRows;
        if (cellWidth <= 0 || cellHeight <= 0) return;
        float labelHeight = cellHeight * .65f;
        float gap = Math.Max(0, Math.Min(cellWidth * .25f, clip.Width - 4 * cellWidth));
        float y = inventory.Bounds.Y - labelHeight;
        foreach (var slot in Accessories)
        {
            int index = controls.FindIndex(c => c.Name == slot.Section);
            if (index < 0) continue;
            var tile = controls[index];
            bool paired = slot.Secondary == false && slot.Rows == 2;
            float width = cellWidth * slot.Columns;
            float left = paired ? clip.X + (clip.Width - 4 * cellWidth - gap) / 2
                : clip.X + (clip.Width - width) / 2;
            if (slot.Section == "neck") left += 2 * cellWidth + gap;
            var bounds = new UiRect(left, y + labelHeight, width, cellHeight * slot.Rows);
            var properties = new Dictionary<string, string>(tile.Properties)
            {
                ["TileSize"] = FormattableString.Invariant($"{cellWidth} {cellHeight}")
            };
            string skin = slot.Section switch
            {
                "eyes_hats" or "eyes_eyes" => "NUE_Equipment_head",
                "eyes_belts" => "NUE_Equipment_belt",
                "gloves" => "NUE_Equipment_shirt",
                _ => "NUE_Equipment_armour"
            };
            controls[index] = tile with { Bounds = bounds, Clip = clip, Properties = properties, Skin = skin };
            controls.Add(new(slot.Section + "-label", "TextBox",
                new(paired ? left : clip.X, y, paired ? width : clip.Width, labelHeight), InventoryText(slot.Label),
                "", "Kenshi_TextboxPaintedText", true,
                new Dictionary<string, string> { ["TextAlign"] = "Center VCenter", ["TextColour"] = "0.91 0.74 0.45" },
                default, Pickable: false, Clip: clip));
            if (!paired) y += labelHeight + bounds.Height;
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
        if (partner == null && !StorageLinked(player) && (bool)Linked.Invoke(GetLayout.Invoke(player,null),null)!) return null;
        return owner is {Animal:false, Appearance:not null} ? owner : null;
    }

    private static CharacterSnapshot? TradePartner(bool merchant, CharacterSnapshot? owner, CharacterSnapshot? partner) =>
        !merchant && owner != null && partner is { Animal:false, Appearance:not null } && partner.Id != owner.Id
            ? partner : null;

    private static bool NormalPlacement(object __instance)
    {
        var player = LayoutPlayer.GetValue(__instance)!;
        if (Screens.GetValue(player) is MyGuiScreenHost screens && StandaloneAnimal(player, screens) != null) return false;
        return StorageLinked(player) || Subject(player) == null;
    }

    private static void SelectLayout(string id, object __instance)
    {
        if (id is not ("inventory" or "loot")) return;
        var player = RouterPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
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
            active.inventoryTitleHeight = 0;
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
        active.inventoryTitleHeight = 0;
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
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        if (!paired && id is ("inventory" or "bag") && StandaloneAnimal(player, screens) is {} animal)
        {
            var (width, height, gap, half, panelHeight) = TradeDimensions(player);
            BindAnimalInventory(id, b, root, player, screens, animal, width, height, gap, half, panelHeight, trading: false);
            return;
        }
        if (paired && id == "loot" &&
            Owner.Invoke(GetInventory.Invoke(player, null), ["loot"]) is CharacterSnapshot { IsPlayer: true })
        {
            b.Disabled.Remove("ArrangeButton");
            b.Actions["ArrangeButton"] = "ArrangeButton";
        }
        if (Subject(player) is not {} subject) return;
        if (id is "bag" or "loot-bag")
        {
            if (active.bagArea.Width <= 0 || active.bagArea.Height <= 0) return;
            b.Positions["Root"] = root with { Width = active.bagArea.Width, Height = active.bagArea.Height };
            b.WindowPosition = new Vector2(active.bagArea.X, active.bagArea.Y) - screens.GetOffset(id);
            return;
        }

#if NUE_UNIFIED
        foreach (var (widget, caption) in InventoryCaptions)
            b.Text[widget] = InventoryText(caption);
        if (Nue.Unified.CharacterInspector.Enabled)
        {
            active.Inspector.Use(subject.Id);
            const string button = "OpenBagButton";
            bool hasBag = subject.ItemGrid is { BagWidth: > 0, BagHeight: > 0 } grid &&
                grid.Items.Any(item => item.Section == "backpack_attach");
            bool bagOpen = hasBag && screens.IsOpen(id == "loot" ? "loot-bag" : "bag");
            b.Text[button] = InventoryText(bagOpen ? "CHARACTER INSPECTOR" : "OPEN BAG");
            if (hasBag)
            {
                b.Disabled.Remove(button);
                b.Actions[button] = button;
            }
            else
            {
                b.Disabled.Add(button);
                b.Actions.Remove(button);
            }
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
        if (!paired && !StorageLinked(player))
            b.WindowPosition = new Vector2(origin.X, origin.Y) - screens.GetOffset(id);
        // The native character fitter bottom-anchors the heading for its own template.
        var title = screens.Layout(id).Find("InventoryMainTitle")!;
        b.RelativePositions[title] = title.Position;
        b.Properties["NuePocketArea"] = new Dictionary<string, string> { ["NueBagScreen"] = id == "loot" ? "loot-bag" : "bag" };
        b.Properties["NueAccessoriesArea"] = new Dictionary<string, string>
            { ["NueShowAccessories"] = active.showAccessories ? "true" : "false" };
        if (active.showAccessories)
        {
            foreach (string limb in LimbSections) b.Hidden.Add(limb);
            b.Hidden.Add("NueArmTitle");
            b.Hidden.Add("NueLegTitle");
        }
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

    private static void FitPocketGrid(List<UiControl> controls, InventoryView grid, object __instance)
    {
        if (controls.Find(c => c.Name == "NuePocketArea") is not {} anchor) return;
        int index = controls.FindIndex(c => c.Name == "main");
        if (index < 0 || grid.Width <= 0 || grid.Height <= 0) return;
        var slot = controls[index];
        float side = Math.Min(slot.Bounds.Width / grid.Width,
            Math.Min(anchor.Bounds.Width / grid.Width, anchor.Bounds.Height / grid.Height));
        var player = LayoutPlayer.GetValue(__instance)!;
        var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
        string bag = anchor.Properties.GetValueOrDefault("NueBagScreen", "bag");
        if (grid.BagWidth > 0 && grid.BagHeight > 0 && screens.IsOpen(bag) &&
            controls.Find(c => c.Name == "NueBagArea") is {} area)
        {
            // Reserve room for the bag frame, title, and Arrange button.
            side = Math.Min(side, Math.Min(area.Bounds.Width * .9f / grid.BagWidth,
                area.Bounds.Height * .8f / grid.BagHeight));
        }
        var view = bag == "loot-bag" ? other : own;
        view.inventoryCell = side;
        view.inventoryTitleHeight = controls.Find(c => c.Skin == "Kenshi_GenericWindowHeaderSkin")?.Bounds.Height ?? 0;
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
        var owner = Owner.Invoke(GetInventory.Invoke(player, null), [id]) as CharacterSnapshot;
        if (owner?.ItemGrid is not {BagWidth: >0, BagHeight: >0} grid) return;
        if (FitEmbeddedAnimalBag(id, controls, grid)) return;
        int i = controls.FindIndex(c => c.Name == "backpack_content");
        if (i < 0) return;
        var slot = controls[i];
        string parent = id == "loot-bag" ? "loot" : "inventory";
        var view = id == "loot-bag" ? other : own;
        if (screens.IsOpen(parent) && screens.Layout(parent).Find("NuePocketArea") != null &&
            view.inventoryCell > 0 && controls.Find(c => c.Type == "Window") is {} window)
        {
            var frame = window.Bounds;
            float width = view.inventoryCell * grid.BagWidth;
            float height = view.inventoryCell * grid.BagHeight;
            var bounds = new UiRect(frame.X + (frame.Width - width) / 2,
                frame.Y + frame.Height * .1f, width, height);
            slot = slot with { Bounds = bounds, Clip = bounds.Intersect(frame) };
            int arrangeIndex = controls.FindIndex(c => c.Name == "ArrangeButton");
            if (arrangeIndex >= 0)
            {
                var button = controls[arrangeIndex];
                float buttonHeight = Math.Min(button.Bounds.Height, frame.Height * .08f);
                var buttonBounds = button.Bounds with { X = frame.X + (frame.Width - button.Bounds.Width) / 2,
                    Y = Math.Min(bounds.Y + height + 4, frame.Y + frame.Height - buttonHeight), Height = buttonHeight };
                controls[arrangeIndex] = button with { Bounds = buttonBounds, Clip = buttonBounds.Intersect(frame) };
            }
        }
        controls[i] = WithCells(slot, grid.BagWidth, grid.BagHeight);
        float titleHeight = screens.IsOpen(parent) && view.inventoryTitleHeight > 0
            ? view.inventoryTitleHeight : 38 * (float)AccessTools.PropertyGetter(Player, "UiScale").Invoke(player, null)!;
        ResizeBackpackTitle(controls, titleHeight);
    }

    private static void ResizeBackpackTitle(List<UiControl> controls, float height)
    {
        int index = controls.FindIndex(c => c.Skin == "Kenshi_GenericWindowHeaderSkin");
        if (index < 0 || controls[index].Bounds.Height <= 0 || height <= 0) return;
        var header = controls[index];
        var oldBounds = header.Bounds;
        if (oldBounds.Height == height) return;
        var bounds = oldBounds with { Height = height };
        controls[index] = header with { Bounds = bounds, Clip = bounds };
        float ratio = height / oldBounds.Height;
        for (int i = 0; i < controls.Count; i++)
        {
            var child = controls[i];
            if (child.Name != "Caption") continue;
            var rect = child.Bounds with
            {
                Y = bounds.Y + (child.Bounds.Y - oldBounds.Y) * ratio,
                Height = child.Bounds.Height * ratio
            };
            controls[i] = child with { Bounds = rect, Clip = rect.Intersect(bounds) };
        }
    }

    private static readonly FieldInfo RouterPlayer = GameType("Ui.ScreenRouter").GetFields(AccessTools.allDeclared).Single(f=>f.FieldType==Player);


    private static void ArrangeLimbGrids(List<UiControl> controls, UiRect root, CharacterSnapshot owner)
    {
        if (controls.Find(c => c.Name == "main") is not {} pocket ||
            owner.ItemGrid is not { Width: > 0, Height: > 0 } grid) return;
        float cellWidth = pocket.Bounds.Width / grid.Width;
        float cellHeight = pocket.Bounds.Height / grid.Height;
        for (int i = 0; i < LimbSections.Length; i++)
        {
            string section=LimbSections[i];
            int at=controls.FindIndex(c=>c.Name==section);
            if(at<0) continue;
            var native=controls[at];
            const int columns = 2;
            int rows = i < 2 ? 4 : 5;
            var rect = native.Bounds with { Y = pocket.Bounds.Y, Width = columns * cellWidth, Height = rows * cellHeight };
            if (i >= 2)
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
            var slot=WithCells(native with {Bounds=rect, Clip=rect.Intersect(root)},columns,rows);
            controls[at]=slot with {Properties=new Dictionary<string,string>(slot.Properties) {
                ["State"]=((owner.LimbBits >> (2*i)) & 3)==0 ? "disabled":"normal" }};
            foreach(var item in grid.Items.Where(v=>v.Section==section))
            {
                string name=$"inventory-item-{owner.Id}-{item.Index}";
                var parts=controls.Where(c=>c.Name==name || c.Name.StartsWith(name+"-",StringComparison.Ordinal)).ToList();
                if (parts.Count == 0) continue;
                foreach(var part in parts) controls.Remove(part);
                var footprint = new UiRect(rect.X + item.X * cellWidth, rect.Y + item.Y * cellHeight,
                    item.Width * cellWidth, item.Height * cellHeight);
                PlaceItem(controls,parts,name,footprint,item);
            }
        }
    }

    private static void DrawInventoryFootprints(string id, List<UiControl> controls)
    {
        string section = id is "bag" or "loot-bag" ? "backpack_content" : "main";
        if (controls.Find(c => c.Name == section) is not {} slot) return;
        foreach (var hit in controls.ToArray())
        {
            if (!hit.Name.StartsWith("inventory-item-", StringComparison.Ordinal) ||
                !hit.Properties.TryGetValue("InventoryItem", out var item)) continue;
            var clip = hit.Bounds.Intersect(hit.Clip ?? slot.Bounds).Intersect(slot.Bounds);
            if (clip.Width <= 0 || clip.Height <= 0) continue;
            int index = controls.FindIndex(c => c.Name == hit.Name || c.Name.StartsWith(hit.Name + "-", StringComparison.Ordinal));
            controls.Insert(index, new("NueInventoryFootprint-" + item, "Widget", hit.Bounds,
                "", "", "", true, new Dictionary<string, string>(),
                new Vector4(.25f, .21f, .16f, 1), Pickable: false, Clip: clip));
        }
    }

    private static void Controls(string id, List<UiControl> controls,object __instance)
    {
        active = id == "loot" ? other : own;
        if (id is "inventory" or "loot" or "bag" or "loot-bag") DrawInventoryFootprints(id, controls);
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
        ArrangeLimbGrids(controls, root, owner);
        active.width = Math.Max(1,(int)image.Width);
        active.height = Math.Max(1,(int)image.Height);
        var backdrop = new Dictionary<string,string>();
        if (active.background < 2) backdrop["ImageTexture"] = Meitou.Game.Rendering.Ui.UiPainter.IconTexture(
            Path.Combine(folder, Backgrounds[active.background] + ".png"));
        // Keep the backdrop at its original proportions behind the shorter viewport.
        float backdropHeight = image.Height / .905f;
        var backdropBounds = image with { Y = image.Y - backdropHeight * .035f, Height = backdropHeight };
        controls.Add(new("NueBackdrop", backdrop.Count == 0 ? "Widget" : "ImageBox", backdropBounds,"","","",true,
            backdrop, backdrop.Count == 0 ? new Vector4(0,0,0,1) : default, Pickable:false, Clip:image));
        if (active.publishedSubject == owner.Id && Painter.GetValue(player) is UiPainter painter &&
            ReferenceEquals(active.publishedPainter,painter) && painter.HasTexture(TextureName))
            controls.Add(new("NueAvatar","ImageBox",image,"","","",true,
                new Dictionary<string,string>{{"ImageTexture",TextureName},{"ImageFlipV","true"}},default,Pickable:true,Clip:root));
        ArrangeEquipment(controls,root,owner,player);
        AddAvatarReadouts(controls, player, owner);
#if NUE_UNIFIED
        PlaceMarkers(controls, player, markedCells);
        if (Nue.Unified.CharacterInspector.Enabled)
        {
            var screens = (MyGuiScreenHost)Screens.GetValue(player)!;
            active.Inspector.Use(owner.Id);
            if (!screens.IsOpen(id == "loot" ? "loot-bag" : "bag"))
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
            Button(-1,InventoryText(options[selected])+"  ▾",bounds.Y);
            if (open) for(int i=0;i<options.Length;i++) Button(i,InventoryText(options[i]),bounds.Y+(i+1)*bounds.Height);
        }
        Menu("NuePoseMenu","nue-avatar:",Labels,active.choice,active.expanded);
        Menu("NueBackdropMenu","nue-background:",Backgrounds,active.background,active.backgroundsOpen);
    }

    private static readonly MethodInfo Raise=AccessTools.Method(typeof(MyGuiScreenHost),"Raise");
    private static void KeepBagAbove(MyGuiScreenHost __instance,string id)
    {
        var bag = id == "loot" ? "loot-bag" : "bag";
        if (id is not ("inventory" or "loot") || !__instance.IsOpen(bag)) return;
        var layout = __instance.Layout(id);
        if (layout.Find("NuePreviewArea") != null || layout.Find("MainSectionName") != null)
            Raise.Invoke(__instance,[bag]);
    }

    private static bool OverAvatar(MyGuiScreenHost host, Vector2 p)
    {
        if (host.HitTest(p) is not { Control.Name: "NueAvatar" } hit) return false;
        active = hit.Screen == "loot" ? other : own;
        return true;
    }
    private static void StartDrag(object __instance, MouseButton button, Vector2 p)
    {
        if (button != MouseButton.Left) return;
        dragHost = null;
        if (Screens.GetValue(__instance) is not MyGuiScreenHost host || !OverAvatar(host, p)) return;
        dragged = active;
        dragHost = host;
        lastDragX = p.X;
    }

    private static void Rotate(object __instance, Vector2 p)
    {
        if (dragHost == null || !ReferenceEquals(dragHost, Screens.GetValue(__instance))) return;
        dragged.yaw = MathF.IEEERemainder(dragged.yaw - (p.X - lastDragX) * MathF.Tau / dragged.width, MathF.Tau);
        lastDragX = p.X;
    }

    private static void EndDrag(MouseButton button)
    {
        if (button == MouseButton.Left) dragHost = null;
    }

    private static void Zoom(MyGuiScreenHost __instance, Vector2 p, float steps)
    {
        if (!OverAvatar(__instance, p)) return;
        active.zoom = Math.Clamp(active.zoom * MathF.Pow(.87f, steps), .9f, 2f);
    }

    private static void Click(MyGuiEvent e)
    {
        active = e.Screen == "loot" ? other : own;
        if (e.Screen is ("inventory" or "loot") && e.Action == "nue-toggle-accessories")
        {
            active.showAccessories = !active.showAccessories;
            return;
        }
        if (e.Screen is ("inventory" or "loot") && e.Action == ToggleVitals)
        {
            active.showHealthOverride = !AvatarShowsHealth(active);
            return;
        }
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
