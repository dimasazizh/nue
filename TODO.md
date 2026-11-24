# NUE UI improvements

1. [x] Add a search bar to crafting stations.
2. [x] Add a search bar to research stations.
3. [x] Compare an inventory item with the gear currently equipped in its slot.
4. [x] Add a search bar to inventory and trade windows.
5. [ ] Add small, configurable status markers to squad portraits so critical downed and injury states are visible at a glance. Reference the Vital Read mod.
6. [ ] Add custom map markers.
7. [ ] Improve the map widget with a 3D height map. Reference the TimeLight mod in the MeitouClient Discord.
8. [ ] Show a local map when zoomed in far enough. Reference the Guild and Quest mod in the MeitouClient Discord.
9. [ ] Let players hide more of the stats panel.
   - [x] Switch between health and stats with vertical HUD tabs in MeitouClient.
10. [ ] Rework the equipment slots in the inventory to move away from the rigid grid.
11. [ ] Add equipment slots for eyes, hands, and neck.
12. [ ] Add a consolidated armor information view. Reference the Character Inspector mod.
13. [x] Show racial XP multipliers. Reference the Racial XP Multipliers - RaceMetrics mod.
14. [x] Update the stats panels when hovering over a character.
15. [ ] Add an on/off toggle in the mod options for each improvement.
16. [ ] Add customizable inventory sorting with **Sort By Type**, **Sort By Item**, and **Sort All** modes.
17. [ ] Add a bestiary panel that shows complete stats and skills for NPCs and beasts.
18. [x] Add an **Examine** option to the NPC right-click menu that opens the NPC’s full skill details.
19. [x] Add a Skyrim-inspired HUD compass bar that shows cardinal directions and nearby points of interest.
20. [ ] Rework the building stats panel into one compact panel anchored to the bottom left.
    - [ ] Size each section to its visible rows. Group the building name, owner, type, and town at the top. Keep warnings, access controls, and condition meters visible when applicable.
    - [ ] Show the expander only when there are extended stats. Allow room for construction materials, crops, wind and power, or ammo when the selected building supplies those rows.
    - [ ] Preserve the anchor MeitouClient uses to place storage windows.
21. [ ] Rework the MeitouClient Inventory Avatar mod to use the in-world camera.
    - [ ] Animate the main camera into a close-up of the selected character with a higher camera origin. Reuse the existing world render and measure the performance impact of removing the separate 3D preview.
    - [ ] Frame the character to the left of the inventory window. Put the bag and inventory grids below the character.
    - [ ] Keep the pose animations and mouse-wheel zoom. Click and release on the character to capture mouse movement for rotation. Click and release again anywhere in the world or inventory window to end rotation.
    - [ ] Block world commands such as movement while inventory camera mode is active. Keep inventory controls available when rotation is inactive, and consume the click that ends rotation.
    - [ ] Restore the pre-inventory camera position, orientation, and zoom when the close button or Escape closes the window.
    - [ ] Add a lighting dropdown beside the pose selector with Night, Daylight, and Current World Lighting. Apply the choice only to the character’s lighting without changing the in-game clock.
    - Trade-window design is deferred.
