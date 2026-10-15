"""Generate the pinned balance overlay from reviewed changes; no game extraction."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parent
reviewed = json.loads((root / 'balance-changes.json').read_text(encoding='utf-8-sig'))
core = json.loads((root.parent / 'Source' / 'fixes.json').read_text(encoding='utf-8-sig'))
changes = reviewed['changes']
additions = reviewed['additions']
assert len(changes) == 132, 'expected 132 balance changes'
assert len(additions) == 25, 'expected 25 balance additions'
field_key = lambda c: (c['table'], c['id'], c['field'])
row_key = lambda a: (a['table'], a['id'])
assert len({field_key(c) for c in changes}) == 132, 'duplicate balance field'
assert len({row_key(a) for a in additions}) == 25, 'duplicate balance addition'
assert not {field_key(c) for c in changes} & {field_key(c) for c in core['changes']}, 'balance field overlaps shared fixes'
assert not {row_key(a) for a in additions} & {row_key(a) for a in core['additions']}, 'balance addition overlaps shared fixes'
assert not any(c['table'] == 'EquipmentData' for c in changes), 'equipment corrections belong to shared fixes'
assert not any(c['table'] == 'CamaraderieStringData' for c in changes), 'Aegis text correction belongs to shared fixes'
assert len(core['changes']) == 346 and len(core['additions']) == 36, 'unsupported shared-fix scope'
fingerprints = {
    'data.unity3d': '4c405e4f179f475a67e8561a5163bddb55e7e6340de83bb876ebb1f94c44275d',
    'Assembly-CSharp.dll': 'a9d49075bab3953b7748b7e91d94829d325580e870297df8ebdc8d43f55427d0',
}
assert core['fingerprints'] == fingerprints, 'shared-fix fingerprints differ'
counts = {
    'PowerData': 1165,
    'UnitClassData': 199,
    'UnitModifierData': 1642,
    'PowerModifierData': 2443,
    'TraitData': 3261,
    'TraitStringData': 4563,
    'PowerStringData': 1008,
    'EquipmentData': 3366,
    'CamaraderieCardData': 141,
    'CamaraderieStringData': 240,
}
assert all(counts[t] == n for t, n in core['dictionary_counts'].items() if t in counts), 'shared dictionary counts differ'
manifest = {
    'version': '0.3.4',
    'fingerprints': fingerprints,
    'dictionary_counts': counts,
    'changes': changes,
    'additions': additions,
}
target = root / 'Source' / 'rebalance.json'
target.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print('Generated 132 balance fields and 25 added records. Combined with shared fixes: 483 fields and 61 added records.')
print('SHA-256:', hashlib.sha256(target.read_bytes()).hexdigest())
