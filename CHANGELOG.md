# Changelog

## 0.1.0

First test release. Includes the item-reference, Paragon shield-steal, Aegis text, relationship-preview, missing-affix-pool and blaster-tag repairs listed in the README.

Corrects three combat forecast defects:

- Reflected damage could reduce the attacker's armor in the forecast even though execution leaves it intact. Later attacks could consequently show too much health damage.
- Direct attack inspection passed damage as a positive resource change to the HUD, making damage look like healing. Its armor delta also used maximum armor instead of the estimated remaining armor.
- Resisted delay rounded fractions to the nearest second in the forecast while execution discarded the fraction. Two 15-second staggers against 50% resistance could forecast a 45-second attack at 61 seconds when it actually executes at 59.

The corrections change estimates and display inputs. They do not change executed damage, delay or round boundaries.
