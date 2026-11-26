# NUE | 鵺
NUE (Native UI Enhancement) is Kenshi UI modification that seeks to improve quality of life but still maintaining the vanilla UI vibes. Less interface. More Kenshi.

Base UI version: **0.1.0** · Unified package: **0.1.5** for **MeitouClient 0.23.1**

[Download NUE Unified for MeitouClient](NUE-Unified-0.1.5.zip). Extract the single `NUE (Native UI Enhancement)` folder into your mods directory. Enable NUE before launching.

In **Settings → Mods → NUE**, switch each features on or off, then restart MeitouClient. All six start enabled, with the options to toggle them individually on/off. The base GUI skin stays active. The inventory toggle controls the inspection UI; pocket geometry and accessory registrations stay active for save compatibility.

In the unified inventory, personal storage uses the vanilla 8×10 grid. Two 2×4 arm slots and two 2×5 leg slots appear beside it. An equipped bag opens by default, with its toggle below the bag. The shared search field above the equipment matches item names and types across equipment, pockets, prosthetics, and the open bag. Use × to clear it. The avatar viewport is 80% of its previous width and height. Equipped weapon icons point up and right. Hover tooltips follow your cursor and have their own mod setting.

Squad trade uses two side-by-side inspection panels for human characters. The companion panel swaps the avatar and bag columns and keeps the equipment and pockets in the usual order. Vendor trade centers both panels together at the same height and sizes the vendor panel's width to its stock grid. The vendor search field sits beside the money display. Animal trade uses the partner panel for the animal inventory and bag.

Item comparison shows the hovered item on the left and the equipped item on the right. Arrows indicate higher and lower values for matching, uncolored numeric stats. Higher values are green and lower values are red. Weight uses green for the lighter item and red for the heavier item.

NUE includes **Item Markers compatibility** under **Settings → Mods → NUE → Mod Compatibility**. With Item Markers enabled, it fits grade frames and badges to NUE's equipment slots. Disable the old standalone compatibility add-on when using a build with the integrated option.

The inventory shows estimated armour coverage and cut, blunt, and harpoon resistances below the 3D avatar. The estimates account for overlapping armour layers and are averaged across body parts. **Armour protection summary** has its own toggle under **Core Features**.

## Character tabs and building information for MeitouClient

With the NUE layout enabled, the lower-left character panel opens on Health. Click the vertical Stats tab to see combat attributes, XP rates, KO point, run speed and hunger rate. The character name, condition and current activity sit immediately above either view.

Selecting a building shows a compact bottom-left panel. It grows with the visible actions and condition meters. Buildings with extended stats have an expand button.

## Inventory inspection for MeitouClient

Opening a human character’s standalone inventory shows a separately rendered, equipped 3D avatar on the left. The gameplay camera stays in place. Nine equal equipment tiles form a 3×3 block in the middle, above a 10×8 personal inventory in the legacy standalone build. Arrange sits beside the inventory heading. Weight, disguise, and ownership appear above the open backpack in the right column.

Open the badge on Primary or Secondary to access the individual weapons in that section. Equipped weapon thumbnails turn 45° clockwise. Stored items keep their usual footprints. When loading an older save, items that fit keep their positions where possible. If an oversized modded inventory cannot fit safely, it keeps its extra space.

The Eyes, Hands, and Neck sockets are hidden. Their registrations remain available to item mods. Item mods should use FCS slot 4 for eyes, 10 for hands, and 11 for neck.

Drag the preview to rotate it and scroll over it to zoom. Use the pose menu to select idle, attack stance, sitting, or lying. The selector opposite the pose menu switches between Desert, Swamp, and Black. The preview camera frames the posed body from near shoulder height.

## HUD compass for MeitouClient

The top-center compass follows the camera and shows eight directions. Nearby discovered locations and player outposts appear as map icons. When icons overlap, the closest location takes priority.

Extract the enclosed `NueCompass` folder into your mods directory and enable it in MeitouClient nightly `0.22.2-nightly.20261122.2`. [Download Compass 0.1.0](meitou/Compass/NueCompass-0.1.0.zip)

## Search and gear comparison for MeitouClient

Type in the crafting or research list to filter recipes or topics in the current category. Inventory and trade searches dim items whose names do not match, keeping their grid positions visible. Hover over gear to see its differing shared stats alongside the equipped item's values, with the hovered value first.

## Hover Stats for MeitouClient

Point at a character in the world to preview their information in the HUD. Move the pointer away to return to the selected building or the selected or inspected character. In the Skills window, hover a built-in skill or attribute to see its racial XP multiplier beside the description heading.

Hold right-click on an NPC and release over **Examine** to open their Skills window. Use **Settings → Mods → Show character hover outline** to hide or allow the normal hover outline. Showing it also requires the game’s **Hover outline** setting.

## Planned UI improvements

See the [UI improvements checklist](TODO.md) for progress on changes to search, inventory and equipment, squad portraits, maps, and character stats. The checklist also tracks an on/off setting for each improvement.

## Etymology & Philosophy
In Japanese folklore, the Nue is traditionally depicted as a creature with the head of a monkey, the body of a tanuki, the limbs of a tiger, and a snake for a tail. The chimeric nature of the creature is also a fitting metaphor for a GUI combining native Kenshi MyGUI and MeitouClient's own GUI codes.


## Contributors & Special Mentions
Dimsky - Author, main contributor
Shutori - Character Inspector
