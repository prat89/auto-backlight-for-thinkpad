#!/usr/bin/env python3
r"""Offline validator/summary for Phase 29C.1 Lenovo compatibility reports.

Read-only: never contacts hardware, network or Git; only prints to stdout.
Usage: py -3 Analyze-LenovoCompatibilityReports.py path\to\LenovoCompatibilityReport.json [other.json ...]
"""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path

SCHEMA = 'LenovoCompatibilityScanner-v1'
LEGACY_GUID = '8C5B9127-ECD4-4657-980F-851019F99CA5'
GAMEZONE_GUID = '887B54E3-DDDC-4B2C-8B88-68A26A8835D0'
BAD_KEYS = re.compile(
    r'^(serial(number)?|hostname|computername|username|user|domain|sid|'
    r'mac(address)?|ip(address)?|pathname|deviceid|pnpdeviceid|'
    r'password|systemname|machineguid|physicaladdress)$', re.I
)
PERSONAL_PATH = re.compile(r'(?i)\b[A-Z]:\\Users\\[^\\\s]+')


class InvalidReport(Exception):
    pass


def inspect_privacy(value, breadcrumb='root'):
    findings = []
    if isinstance(value, dict):
        for key, sub in value.items():
            if BAD_KEYS.fullmatch(str(key)):
                findings.append(f'Unexpected possibly identifying key at {breadcrumb}.{key}')
            findings.extend(inspect_privacy(sub, f'{breadcrumb}.{key}'))
    elif isinstance(value, list):
        for i, item in enumerate(value):
            findings.extend(inspect_privacy(item, f'{breadcrumb}[{i}]'))
    elif isinstance(value, str) and PERSONAL_PATH.search(value):
        findings.append(f'Windows user-profile path at {breadcrumb}')
    return findings


def load_and_validate(path: Path):
    if not path.is_file():
        raise InvalidReport('File does not exist')
    if path.stat().st_size > 3_000_000:
        raise InvalidReport('Report exceeds 3 MB inspection limit')
    try:
        raw = json.loads(path.read_text(encoding='utf-8-sig'))
    except (UnicodeDecodeError, ValueError) as exc:
        raise InvalidReport(f'Unreadable JSON ({type(exc).__name__})') from exc
    if not isinstance(raw, dict) or raw.get('Schema') != SCHEMA:
        raise InvalidReport('Wrong or missing report schema')
    for key in ('Device', 'Drivers', 'Scans', 'Matches', 'Warnings'):
        if key not in raw:
            raise InvalidReport(f'Missing {key} field')
    if not isinstance(raw['Device'], dict) or not isinstance(raw['Drivers'], list) \
       or not isinstance(raw['Scans'], list) or not isinstance(raw['Matches'], list) \
       or not isinstance(raw['Warnings'], list):
        raise InvalidReport('Unexpected field type')
    if raw.get('HardwareCompatibilityConfirmed') is not False:
        raise InvalidReport('HardwareCompatibilityConfirmed must be false for metadata-only scans')
    privacy = inspect_privacy(raw)
    if privacy:
        raise InvalidReport('Privacy review needed: ' + '; '.join(privacy[:8]))
    return raw


def summarize(raw: dict):
    device = raw['Device']
    lines = ['Device: ' + str(device.get('Manufacturer', '?')) + ' / '
             + str(device.get('Model', '?')),
             'BIOS: ' + str(device.get('BIOSVersion', '?'))
             + '; Windows: ' + str(device.get('OSVersion', '?'))]
    for name in ('IBMPmDrv', 'EnergyDrv'):
        items = [d for d in raw['Drivers'] if isinstance(d, dict) and d.get('Name', '').casefold() == name.casefold()]
        d = items[0] if items else {'Observation': 'not-checked'}
        lines.append(f"Driver {name}: {d.get('Observation')}"
                     + (f" / {d.get('State')}" if d.get('State') else ''))
    for guid, label in ((LEGACY_GUID, 'Legacy Legion BA'), (GAMEZONE_GUID, 'Gamezone WMI')):
        hits = [m for m in raw['Matches'] if isinstance(m, dict) and guid in
                {str(g).strip('{}').upper() for g in (m.get('Guids') or [])}]
        if hits:
            lines.append(label + ': class metadata observed: ' + ', '.join(
                str(x.get('Namespace', '?')) + '\\' + str(x.get('ClassName', '?')) for x in hits))
            if guid == GAMEZONE_GUID:
                for hit in hits:
                    known_methods = {str(m.get('Name')): m.get('WmiMethodId')
                                     for m in hit.get('MethodMetadataOnly') or [] if isinstance(m, dict)}
                    for method in ('IsSupportLightingFeature', 'SetKeyboardLight', 'GetKeyboardLight'):
                        lines.append(f'  {method}: ' + (
                            f"declared (ID {known_methods[method]})" if method in known_methods else 'not observed'))
        else:
            lines.append(label + ': not observed in the scanned metadata')
    for s in raw['Scans']:
        if not isinstance(s, dict):
            continue
        lines.append('WMI ' + str(s.get('Namespace')) + ': ' + str(s.get('Status')) +
                     ' (' + str(s.get('MatchingClasses', '?')) + ' matches)')
    if raw['Warnings']:
        lines.append('Warnings: ' + str(len(raw['Warnings'])) + ' — inspect source report')
    lines.append('Hardware compatibility: NOT CONFIRMED (metadata-only scan)')
    return lines


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('reports', nargs='+', type=Path)
    args = p.parse_args()
    models = Counter()
    invalid = 0
    for path in args.reports:
        print('\n' + '=' * 64)
        print('REPORT: ' + path.name)
        try:
            raw = load_and_validate(path)
        except InvalidReport as exc:
            print('FAILED: ' + str(exc))
            invalid += 1
            continue
        print('SCHEMA/PRIVACY STRUCTURAL CHECK: PASS')
        for line in summarize(raw):
            print(line)
        models[str(raw['Device'].get('Model', 'unknown'))] += 1
    print('\n' + '=' * 64)
    print('Accepted reports:', sum(models.values()), '| Rejected:', invalid)
    print('Models:', ', '.join(f'{k} ({n})' for k, n in sorted(models.items())) or '(none)')
    if invalid:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
