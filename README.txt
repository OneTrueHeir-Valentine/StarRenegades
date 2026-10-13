Star Renegades Bugfix Mod

Fixes for the Windows x64 Steam version of Star Renegades 1.5.1.5.
Version 0.1.2 test release.

CHOOSING A MOD

This repository also hosts Star Renegades Rebalance. Choose one download:

- Bugfix Mod: game corrections without the character, combo and camping
  balance changes.
- Rebalance: the same fixes plus the approved balance changes. Its source
  and documentation are in Rebalance/.

The two mods cannot be installed together. Uninstall one before switching.

INSTALL

1. Download Star-Renegades-Bugfix-Mod-v0.1.2.zip from:
   https://github.com/OneTrueHeir-Valentine/StarRenegades/releases
2. Extract it into its own folder and make sure the game is closed.
3. Double click on Install.cmd
   The installer finds the Steam library, or asks you to select the
   folder containing Star Renegades.exe
4. Start the game normally through Steam.

Windows may ask for administrator permission if Steam is installed under
Program Files.

To remove the mod, close the game and run Uninstall.cmd. To update from
an older version, extract the new release into a new folder and run its
Install.cmd. You do not need to uninstall first.

The installer checks the game build and refuses another loader or the
SRRebalance mod. If an installed file has been edited, it preserves the
file and reports the conflict.

FIXES

- Focus Cortex and Disruptor Implant: repair 336 broken damage-bonus
  references across their levels and grades.
- Paragon camping traits: add their advertised +5 or +10 CRIT shield
  steal to other bonuses instead of replacing them.
- Aegis camping card: describe health regeneration rather than shields.
- Camping card previews: show the required relationship rank.
- Weapon affixes: restore 36 missing kinetic, blaster and rifle pools
  using the existing pool at the matching grade and level. This
  follows surviving definitions; exact developer intent is unknown.
- Blaster affixes: repair five generation tags for buff-removal effects.
- Combat forecasts: correct reflected armor loss, damage shown as healing
  during inspection, and rounding of resisted delay.
- Combat forecasts: include projected target effects in vulnerability and
  CRIT-defense checks, and include incoming CRIT damage in reflection.
- Enemy AI: honor weighted action lists instead of choosing equally after
  discarding their weights.
- Enemy sequences: restore the Dragoon Overseer summon continuation and
  the Panzer repair continuation.
- Achievements: retry earned counter achievements when Steam becomes
  ready or a submission fails. The mod does not add kills or progress.

Enemy corrections can make encounters harder. Firewulf and Demolisher
choices now follow their declared weights; repaired enemy continuations
can execute actions that were previously skipped.

Forecast corrections do not change executed damage.
Item repairs restore the intended bonuses.
Existing item rolls are retained, including items with empty rolls.
Corrected item bonuses are read again when saved equipment is loaded.
The mod does not edit save files or unlock characters.

TESTING AND BUG REPORTS

Effect expiry and removal affecting other characters are outside the new
forecast corrections.
Achievement retries are limited to three attempts per earned achievement
in each Steam-ready session, at least ten seconds apart.
Restarting or a fresh readiness session allows another attempt.

Report problems at:
https://github.com/OneTrueHeir-Valentine/StarRenegades/issues

For an incorrect forecast, include the attacks, equipment, active effects
and predicted versus actual result.
The mod log is SRBugfix/bugfix.log inside the game folder.
Remove personal paths before posting it.

SOURCE

BUILD.txt explains how to build and test the mod.
Local game files are required and are not included.
Dependency notices and Doorstop source are supplied in both release archives.

This is an unofficial mod. Star Renegades belongs to its developers and
publishers.
