# Optional GK2 Mod Framework integration

Install the main Keeper's Little Helpers 0.3.7 pack first. Install GK2 Mod Framework 0.1.8 or later separately from its author. Then extract this optional bridge into the game folder. Do not replace your existing BepInEx installation.

The bridge adds Keeper's Little Helpers and its twelve feature switches to the Framework Mods menu. Both settings menus edit the same configuration. The native Esc menu remains available. The bridge does not own gameplay, and deliberately does not offer a misleading master Enable/Disable switch. Without the Framework, BepInEx skips the optional bridge and the main mod continues to work.

Translation files belong in the main mod's Localization folder. Restart after changing language to refresh Framework setting names. Framework compatibility status for this optional settings bridge is not a certification of every other mod or future game build.

For troubleshooting, include both versions and the relevant BepInEx log entries. Do not install multiple copies of either plugin.

Checked with Framework 0.1.11 on Windows, game 1.005: both plugins load, the save loads, settings display in the Mods menu, and changing Materials pin there is reflected in the native helper menu. The setting was restored through the native menu. Other Framework versions and combinations of mods remain unverified.
