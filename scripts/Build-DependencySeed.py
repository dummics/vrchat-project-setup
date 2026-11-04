"""Generate metadata-only signatures from explicitly supplied installed Packages folders.

No project writes or asset copies. Provenance records observed package versions,
relative metadata paths and SHA256; it does not assert publisher verification.
"""
import argparse, hashlib, json, pathlib, re

ALLOWED = {'com.vrchat.base', 'com.vrchat.avatars', 'com.vrchat.core.vpm-resolver',
           'com.vrcfury.vrcfury', 'com.poiyomi.toon', 'com.poiyomi.thryeditor', 'gogoloco',
           'com.llealloo.audiolink', 'adjerry91.vrcft.templates', 'dev.foxscore.easy-login',
           'nadena.dev.modular-avatar', 'nadena.dev.ndmf'}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--packages', action='append', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    signatures, provenance, seen = [], [], set()
    for root in args.packages:
        for folder in sorted(pathlib.Path(root).iterdir()):
            manifest = folder / 'package.json'
            if not manifest.is_file(): continue
            try: package = json.loads(manifest.read_text(encoding='utf-8-sig'))
            except (ValueError, OSError): continue
            package_id, version = package.get('name'), package.get('version')
            if package_id not in ALLOWED or not isinstance(version, str) or (package_id, version) in seen: continue
            seen.add((package_id, version))
            records = []
            for meta in sorted(folder.rglob('*.meta')):
                suffix = str(meta).lower()
                kind = 'script' if suffix.endswith(('.cs.meta', '.dll.meta')) else 'shader' if suffix.endswith(('.shader.meta', '.shadergraph.meta')) else 'asset' if package_id in {'gogoloco', 'adjerry91.vrcft.templates'} and suffix.endswith(('.controller.meta', '.asset.meta')) else None
                if not kind or meta.is_symlink() or meta.stat().st_size > 65536: continue
                data = meta.read_bytes()
                match = re.search(rb'(?m)^guid:\s*([a-fA-F0-9]{32})', data)
                if not match: continue
                guid = match[1].decode().lower()
                signatures.append({'Guid': guid, 'PackageId': package_id, 'Version': version, 'Kind': kind, 'Source': f'observed:{package_id}@{version}'})
                records.append({'path': meta.relative_to(folder).as_posix(), 'guid': guid, 'sha256': hashlib.sha256(data).hexdigest()})
            provenance.append({'package': package_id, 'version': version, 'manifestSha256': hashlib.sha256(manifest.read_bytes()).hexdigest(), 'metadata': records})
    destination = pathlib.Path(args.output)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(sorted(signatures, key=lambda x: (x['PackageId'], x['Version'], x['Guid'])), separators=(',', ':')), encoding='utf-8')
    destination.with_suffix('.provenance.json').write_text(json.dumps({'schema': 1, 'origin': 'Observed local package metadata, not a publisher attestation', 'packages': provenance}, indent=2), encoding='utf-8')
    print(json.dumps({'signatures': len(signatures), 'packages': len(provenance), 'bytes': destination.stat().st_size}))

if __name__ == '__main__': main()
