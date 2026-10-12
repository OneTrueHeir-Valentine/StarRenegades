"""Build the minimal patch file from reviewed corrections. No game extraction."""
import hashlib
import json
from pathlib import Path

source = Path(__file__).resolve().parent / "Source"
core = json.loads((source / "core-fixes.json").read_text(encoding="utf-8"))
affixes = json.loads((source / "affix-fixes.json").read_text(encoding="utf-8"))
assert len(core) == 341
assert len(affixes["tagChanges"]) == 5
assert len(affixes["poolAdditions"]) == 36
changes = core + affixes["tagChanges"]
assert len({(c["table"], c["id"], c["field"]) for c in changes}) == 346
assert len({a["id"] for a in affixes["poolAdditions"]}) == 36
manifest = {
    "version": "0.1.0",
    "fingerprints": {
        "data.unity3d": "4c405e4f179f475a67e8561a5163bddb55e7e6340de83bb876ebb1f94c44275d",
        "Assembly-CSharp.dll": "a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0",
    },
    "dictionary_counts": {
        "EquipmentData": 3366,
        "PowerModifierData": 2443,
        "CamaraderieStringData": 240,
        "TraitData": 3261,
        "TagGroupData": 274,
    },
    "changes": changes,
    "additions": affixes["poolAdditions"],
}
target = source / "fixes.json"
target.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("Generated 346 field corrections and 36 affix pools.")
print("SHA-256:", hashlib.sha256(target.read_bytes()).hexdigest())
