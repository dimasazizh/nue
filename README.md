# nue
NUE (Native UI Enhancement) is Kenshi GUI modification that seeks to improve quality of life but still maintaining the vanilla GUI vibes.

Version: **0.1.0**

## Character tabs and building information for MeitouClient

With the NUE layout enabled, the lower-left character panel opens on Health. Click the vertical Stats tab to see combat attributes, XP rates, KO point, run speed and hunger rate. The character name, condition and current activity sit immediately above either view.

Selecting a building shows a compact bottom-left panel. It grows with the visible actions and condition meters. Buildings with extended stats have an expand button.

Extract the enclosed `NueHudTabs` folder into your mods directory and enable it alongside NUE on nightly `0.22.2-nightly.20261122.2`. [Download HUD Tabs 0.1.1](meitou/HudTabs/NueHudTabs-0.1.1.zip)

## Inventory inspection for MeitouClient

Opening a human character’s standalone inventory brings the world camera in close. The live character appears on the far left at 1.5× the previous default magnification. Equipment occupies the middle column. The personal inventory and open backpack stack on the right. The NUE character inventory layout uses 80% of the screen height at default interface scale. Close the inventory or press Escape to return to the previous camera position.

Click and release on the character, then move the mouse to rotate. Click again anywhere to release rotation. Use the wheel over the world view to zoom. The two menus select a pose (idle, attack stance, sitting, or lying) and character lighting (world, daylight, or night). The lighting presets leave the game clock alone.

Replace the existing `NueInventoryAvatar` folder, merge the enclosed `NUE (Native UI Enhancement)` layout into your installed NUE folder, and enable both mods on nightly `0.22.2-nightly.20261122.2`. [Download Inventory Avatar 0.2.1](meitou/InventoryAvatar/NueInventoryAvatar-0.2.1.zip)

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
