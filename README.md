# Star Renegades Bugfix Mod

Fixes for the Windows Steam version of Star Renegades 1.5.1.5.

Version 0.1.0 is a test release. The data repairs, engine checks and installer tests pass. Normal Steam launch and the rendered combat previews still need playtesting.

## Install

1. Download the player ZIP from [Releases](https://github.com/OneTrueHeir-Valentine/StarRenegades/releases).
2. Extract it into its own folder. Close the game.
3. Double-click `Install.cmd`. The installer finds the Steam library, or asks you to select the folder containing `Star Renegades.exe`.
4. Start the game normally through Steam.

Windows may ask for administrator permission if Steam is installed under Program Files. No separate mod manager, compiler or framework download is needed.

Keep the extracted folder. To remove the mod, close the game and double-click `Uninstall.cmd`. To update, extract the new release and run its installer. The installer checks the game build and refuses another loader or the SRRebalance mod. If an installed file has been edited, it preserves that file and reports the conflict.

## Fixes

- Focus Cortex and Disruptor Implant: repair 336 broken damage-bonus references across their levels and grades.
- Paragon's camping shield-steal traits: add their advertised +5 or +10 CRIT shield steal to other bonuses instead of replacing them.
- Aegis's camping card: describe its health regeneration rather than shield regeneration.
- Camping card previews: show the relationship rank required by the card instead of always saying Friend.
- Weapon affixes: add 36 missing kinetic, blaster and rifle pools. They use the same tags as the existing sword pools at the matching grade and level. This follows the surviving current-build definitions; the original developer intent is unknown.
- Blaster affixes: correct five misspelled generation tags so the existing buff-removal effects can roll.
- Combat forecasts: prevent reflection from stripping armor in the estimate, correct reversed damage/healing signs in direct attack inspection, and make resisted delay use the same truncation as combat execution.

These are fixes to the base game. Character balance, costs and live combat rules are unchanged. Existing generated item rolls are kept, including older items with empty rolls. Fixed item bonuses are read again when saved equipment is loaded. The mod does not edit saves or unlock characters.

The forecast fixes cover specific reproduced discrepancies. Other prediction errors may remain. Please report an incorrect forecast with the attacks, equipment, effects and expected versus actual result, if available. The mod log is `SRBugfix/bugfix.log` inside the game folder.

## Source

See [BUILD.md](BUILD.md) to build or test the mod. Game files are required locally and are not included. Dependency notices and Doorstop's corresponding source are included in each release.

This is an unofficial mod. Star Renegades belongs to its developers and publishers.
