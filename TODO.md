# NUE UI improvements

Core unique features:
1. [x] Add a search bar to crafting stations.
2. [x] Add a search bar to research stations.
3. [x] Compare an inventory item with the gear currently equipped in its slot.
   - [ ] Make it so the text is green if the gear in inventory has better stat, and red if worse.
5. [x] Add a search bar to inventory and trade windows.
6. [x] Switch between health and stats with vertical HUD tabs in MeitouClient.
7. [x] Rework the equipment slots in the inventory to move away from the rigid grid.
8. [x] Add equipment slots for eyes, hands, and neck.
9. [x] Show racial XP multipliers. Reference the Racial XP Multipliers - RaceMetrics mod.
10. [x] Update the stats panels when hovering over a character.
11. [x] Add an on/off toggle in the mod options for each improvement.
12. [ ] Add customizable inventory sorting with **Sort By Type**, **Sort By Item**, and **Sort All** modes.
13. [x] Add an **Examine** option to the NPC right-click menu that opens the NPC’s full skill details.
14. [x] Add a Skyrim-inspired HUD compass bar that shows cardinal directions and nearby points of interest.
15. [x] Rework the building stats panel into one compact panel anchored to the bottom left.
    - [x] Size each section to its visible rows. Group the building name, owner, type, and town at the top. Keep warnings, access controls, and condition meters visible when applicable.
    - [x] Show the expander only when there are extended stats. Allow room for construction materials, crops, wind and power, or ammo when the selected building supplies those rows.
    - [x] Preserve the anchor MeitouClient uses to place storage windows.
16. [x] Restore the separate inventory avatar preview with compact equipment, personal storage below the slots, and details above the backpack. Preserve the gameplay camera.
    - Trade-window design is deferred.
17. [ ] Add a bestiary panel that shows complete stats and skills for NPCs and beasts.
18. [ ] Rework the trade/barter panel window with vendors.
19. [ ] Rework the trade/barter panel window with squad members.
20. [ ] Rework the inventory to incorporate limbs.
21. [ ] 3D avatar is too big, resize to make room for planned armor coverage stats.
22. [ ] Comment the lines of code of not yet implemented neck, eye, and hand socket.
   
Features from other mods to implement:
1. [ ] Add small, configurable status markers to squad portraits so critical downed and injury states are visible at a glance. Reference the Vital Read mod.
2. [ ] Add custom map markers.
3. [ ] Improve the map widget with a 3D height map. Reference the TimeLight mod in the MeitouClient Discord.
4. [ ] Show a local map when zoomed in far enough. Reference the Guild and Quest mod in the MeitouClient Discord.
5. [ ] Add a consolidated armor information view. Reference the Character Inspector mod.
6. [ ] Add a patch for item markers. Or make our own.
