Star Renegades Mods

Two mods for the Windows x64 Steam version of Star Renegades 1.5.1.5.

Bugfix Mod 0.1.6
Fixes item effects, English text, combat forecasts, earned achievement
submissions and enemy action selection. Keeps the base character balance.
Download:
https://github.com/OneTrueHeir-Valentine/StarRenegades/releases/tag/bugfix-v0.1.6

Rebalance 0.3.5
Includes every Bugfix repair, plus the approved base-character and alternate
form changes, eight combo buffs and four camping cost reductions. The aim
is stronger starter choices on Entropy V; complete roster testing is ongoing.
Download:
https://github.com/OneTrueHeir-Valentine/StarRenegades/releases/tag/rebalance-v0.3.5

Install one mod at a time. Rebalance already includes the Bugfix repairs.
Both versions are test releases.

NEW IN BUGFIX 0.1.6 AND REBALANCE 0.3.5

Save preparation and reconstruction errors now offer a return to save
selection through an error message. Confirm and Back both dismiss it.
Each load captures its prepared save; older results cannot replace a newer
load. Native backup fallback and successful scene routing remain.
This update makes no balance changes.

The controlled-error check passed with a PlayStation controller. Normal
Steam restart and existing-save loading also passed. Other progression
and controller-camera reports remain under investigation.

INSTALL

1. Download the player ZIP for the mod you want. The ZIP ending in
   -source.zip is for contributors.
2. Extract it into its own folder and make sure the game is closed.
3. Double click on Install.cmd. The installer finds the Steam library,
   or asks you to select the folder containing Star Renegades.exe.
4. Start the game normally through Steam.

No previous version or separate framework download is required. Windows
may ask for administrator permission for a Program Files Steam library.
Keep the extracted release folder for Uninstall.cmd.

To update the same mod, extract the new player ZIP into a new folder and
run its Install.cmd with the game closed. To switch between the two mods,
uninstall the active mod first, then install the other one.

Older private rebalance builds without an installation receipt must use
their own backup/rollback instructions before installing a public release.
The installer preserves changed files and reports conflicts. Do not delete
your saves to resolve a mod conflict.

CHECKING THE MOD AND FINDING ITS LOG

Open the installed game folder through Steam: Manage, Browse local files.
The log is inside that folder:

Bugfix: SRBugfix/bugfix.log
Rebalance: SRRebalance/rebalance.log

Logs are created or appended during game startup. If no log appears after
a launch, the mod has not confirmed startup. An empty file by itself does
not establish that the mod loaded. Look for the Applied version line.

Bugfix 0.1.6 reports 351 fields and 36 affix pools.
Rebalance 0.3.5 reports 483 fields and 61 added records.

EARLIER ENEMY ENERGY CORRECTION

Dragoon Overseer's energy condition now respects its exact threshold.
At 30 out of 50 energy, floating-point rounding treated 60% as greater
than 60% and selected Supercharge. It now follows the normal attack path
at that boundary. The configured threshold and move values are retained.

INCLUDED FORECAST CORRECTIONS

- Forecast healing stops at maximum health. Previously, excess healing
  could conceal a lethal hit later in the same forecast.
- Temporary armor, including Spy mode armor, protects against forecast
  damage and is consumed before base armor. It no longer appears as an
  increase in permanent armor.
- Forecasts track source, ally and enemy cleanses, including cancellation
  after counterattacks. Defend's debuff immunity follows its projected
  application and removal.
- Removing a tethered effect removes its linked guard, counter or overwatch
  effects. Tethered grants fail together when an effect is resisted, as
  they do in combat. Ally support is no longer redirected through a guard.

These corrections change forecasts, not executed combat. They do not
establish that every forecast is exact. Cross-round effect expiry and
other effects that the original forecast does not model still need work.

INCLUDED COMBO CORRECTIONS

- Illuminate deactivates itself after use, preserving Exhilaration.
- Shadow Puppets applies its instant-attack effect to the selected Dark
  Paragon partner. Spectre keeps its existing effect; other allies do not
  receive this participant effect. The instant-attack effect expires when
  Dark Paragon attacks.
- Hard Reset's English description lists its working enemy/hero cleanses.
  Its missing shield-recharge effect remains unresolved; the advertised
  recharge was not occurring and its intended amount is unknown.

The missing enemy references remain held because their intended replacements
are not established. Bounty Hunter's invalid self-reference is not trimmed
because doing so triggers the AI loop guard. The previous enemy fixes remain.

SHARED FIXES

- Focus Cortex and Disruptor Implant: repair 336 broken damage-bonus
  references across levels and grades.
- Paragon camping traits: add their advertised CRIT shield steal to other
  bonuses instead of replacing them.
- Aegis Thrilling Lesson: describe its existing health regeneration.
- Camping previews: show the required relationship rank.
- Weapon affixes: restore 36 missing kinetic, blaster and rifle pools using
  the working sword pool at the matching grade and level, with the weapon
  slot changed. Exact developer intent remains an inference.
- Blaster affixes: repair five generation tags for buff-removal effects.
- Combat forecasts: correct damage shown as healing during inspection,
  reflected armor loss, resisted-delay rounding, projected target effects
  and incoming CRIT damage in reflection, plus the corrections above.
- Achievements: retry earned counter achievements after Steam becomes
  ready or a submission fails. The mod does not add achievement progress.
- Enemy AI: honor valid weighted action lists. Ordinary unweighted lists
  retain their equal selection chances. Invalid definitions need individual
  investigation; the selector does not guess missing actions or weights.
- Enemy energy conditions: compare the configured percentage without the
  intermediate float-ratio rounding that changed Dragoon's boundary choice.
- Enemy sequences: restore Dragoon Overseer's summon continuation and
  Panzer's repair continuation. These corrections can make encounters harder.

Existing random item rolls are retained, including empty rolls. Newly
created equipment uses the corrected pools. Fixed item bonuses are read
again when saved equipment loads. The mods do not edit save files or unlock
characters.

REBALANCE DETAILS

Rebalance/README.txt covers the balance build. Rebalance/CHANGELOG.txt lists
combo and camping values, and Rebalance/CHANGE-RATIONALE.txt explains them.
Exact character and variant changes are in Rebalance/balance-changes.json.
The shared repairs are compiled from the same source for both mods.

BUG REPORTS AND TESTING

Report problems at:
https://github.com/OneTrueHeir-Valentine/StarRenegades/issues

Include the game version, which mod/version you use, what happened and how
to reproduce it. For forecasts, include characters, enemies, attacks,
equipment, active effects and predicted versus actual results if available.
Attach the relevant mod log after removing personal paths. Screenshots help;
a video is not required.

Automated checks cover native methods, combined activation, rollback,
installer recovery and release contents. Full battles and rendered UI still
need testing. Loading failures, softlocks and further forecast errors remain
under investigation. A missing reference in unused data is not automatically
a bug in the current campaign.

Achievement retries are limited to three attempts per earned achievement
in each Steam-ready session, at least ten seconds apart. Restarting or a
fresh readiness session permits another attempt.

SOURCE

The Bugfix project is at the repository root; Rebalance is in Rebalance/.
BUILD.txt and Rebalance/BUILD.txt explain contributor builds and tests.
Local game files are required and are not distributed. The separate source
ZIPs include the applicable source, tests and dependency notices. Doorstop
source and license notices are supplied with both player releases.

This is an unofficial mod. Star Renegades belongs to its developers and
publishers.

Shadow Puppets testing covers the selected partner effect and its expiry
through native methods. Changes to already scheduled timeline positions
still need battle verification; the forecast is not guaranteed exact.
