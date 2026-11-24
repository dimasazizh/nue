# nue
NUE (Native UI Enhancement) is Kenshi UI modification that seeks to improve quality of life but still maintaining the vanilla UI vibes.

Version: **0.1.1**

[Download the complete MeitouClient mod bundle](NUE-0.1.1.zip). Extract its folders into your mods directory and enable them in the launcher.

## Character tabs and building information for MeitouClient

With the NUE layout enabled, the lower-left character panel opens on Health. Click the vertical Stats tab to see combat attributes, XP rates, KO point, run speed and hunger rate. The character name, condition and current activity sit immediately above either view.

Selecting a building shows a compact bottom-left panel. It grows with the visible actions and condition meters. Buildings with extended stats have an expand button.

Extract the enclosed `NueHudTabs` folder into your mods directory and enable it alongside NUE on nightly `0.22.2-nightly.20261122.2`. [Download HUD Tabs 0.1.1](meitou/HudTabs/NueHudTabs-0.1.1.zip)

## Inventory inspection for MeitouClient

Opening a human character’s standalone inventory shows a separately rendered, equipped 3D avatar on the left. The gameplay camera stays in place. Nine equal equipment tiles form a 3×3 block in the middle, above a 10×8 personal inventory. Arrange sits beside the inventory heading. Weight, disguise, and ownership appear above the open backpack in the right column.

Open the badge on Primary or Secondary to access the individual weapons in that section. Equipped weapon thumbnails turn 45° clockwise. Stored items keep their usual footprints. When loading an older save, items that fit keep their positions where possible. If an oversized modded inventory cannot fit safely, it keeps its extra space.

The Eyes, Hands, and Neck sockets are hidden. Their registrations remain available to item mods. Item mods should use FCS slot 4 for eyes, 10 for hands, and 11 for neck.

Drag the preview to rotate it and scroll over it to zoom. Use the pose menu to select idle, attack stance, sitting, or lying. The selector opposite the pose menu switches between Desert, Swamp, and Black. The preview camera frames the posed body from near shoulder height.

Replace the installed `NueInventoryAvatar` folder with the one in the archive. Edit its `InventoryInspection.layout` to adjust window dimensions, column positions, or pocket rows and columns. Built for nightly `0.22.2-nightly.20261122.2`. [Download Inventory Avatar 0.5.0](meitou/InventoryAvatar/NueInventoryAvatar-0.5.0.zip)

## HUD compass for MeitouClient

The top-center compass follows the camera and shows eight directions. Nearby discovered locations and player outposts appear as map icons. When icons overlap, the closest location takes priority.

Extract the enclosed `NueCompass` folder into your mods directory and enable it in MeitouClient nightly `0.22.2-nightly.20261122.2`. [Download Compass 0.1.0](meitou/Compass/NueCompass-0.1.0.zip)

## Search and gear comparison for MeitouClient

[Download Search & Compare 0.1.0](meitou/SearchCompare/NueSearchCompare-0.1.0.zip) for nightly `0.22.2-nightly.20261122.2`. Extract the enclosed `NueSearchCompare` folder into your mods directory and enable it.

Type in the crafting or research list to filter recipes or topics in the current category. Inventory and trade searches dim items whose names do not match, keeping their grid positions visible. Hover over gear to see its differing shared stats alongside the equipped item's values, with the hovered value first.

## Hover Stats for MeitouClient

[Download Hover Stats 0.1.2](meitou/HoverStats/NueHoverStats-0.1.2.zip) for nightly `0.22.2-nightly.20261122.2`. Extract the enclosed `NueHoverStats` folder into your mods directory and enable it.

Point at a character in the world to preview their information in the HUD. Move the pointer away to return to the selected building or the selected or inspected character. In the Skills window, hover a built-in skill or attribute to see its racial XP multiplier beside the description heading.

Hold right-click on an NPC and release over **Examine** to open their Skills window. Use **Settings → Mods → Show character hover outline** to hide or allow the normal hover outline. Showing it also requires the game’s **Hover outline** setting.

## Planned UI improvements

See the [UI improvements checklist](TODO.md) for progress on changes to search, inventory and equipment, squad portraits, maps, and character stats. The checklist also tracks an on/off setting for each improvement.
