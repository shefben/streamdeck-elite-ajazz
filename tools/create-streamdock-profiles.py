"""Create the Elite profiles that automatic profile switching needs, in Stream Dock.

The plugin switches profiles by name: Profile.ReadProfiles reads the names out of the plugin's
own manifest.json and StreamDeckCommon.HandleOnTick calls SwitchProfileAsync with one of them as
the game state changes. That only does anything if profiles with those exact names exist in the
host, and the plugin ships none - manifest.json declares the names but the Profiles folder is
empty, so on Elgato you are expected to build them by hand.

Stream Dock keeps profiles as a folder per profile under
    %APPDATA%\\HotSpot\\StreamDock\\profiles\\<UUID>.sdProfile\\manifest.json
with the display name in the manifest's "Name", and a registry row per folder in
    %APPDATA%\\HotSpot\\StreamDock\\config\\DataCache.db, table "<DeviceUUID>".

Both are backed up before anything is written. Stream Dock must not be running: it holds these
in memory and rewrites them on exit, which would undo the changes.

Usage:  python tools/create-streamdock-profiles.py [--dry-run]
"""

import argparse
import datetime
import io
import json
import os
import random
import shutil
import sqlite3
import sys

PROFILE_NAMES = ['Elite Main', 'Elite OnFoot', 'Elite InSRV', 'Elite InFighter']

APPDATA = os.environ.get('APPDATA', '')
ROOT = os.path.join(APPDATA, 'HotSpot', 'StreamDock')
PROFILES = os.path.join(ROOT, 'profiles')
DB = os.path.join(ROOT, 'config', 'DataCache.db')
CONFIG = os.path.join(ROOT, 'config', 'StreamDockConfig.ini')

ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'


def new_uuid():
    """Stream Dock's own id shape: uppercase alphanumeric, 8-4-4-4-12."""
    def block(n):
        return ''.join(random.choice(ALPHABET) for _ in range(n))
    return f'{block(8)}-{block(4)}-{block(4)}-{block(4)}-{block(12)}'


def running():
    try:
        import subprocess
        out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Stream Dock*'],
                             capture_output=True, text=True, timeout=20).stdout
        return 'Stream Dock' in out
    except Exception:
        return False


def load_profiles():
    """Every existing profile folder, with its manifest."""
    found = []
    if not os.path.isdir(PROFILES):
        return found
    for name in sorted(os.listdir(PROFILES)):
        path = os.path.join(PROFILES, name, 'manifest.json')
        if not os.path.isfile(path):
            continue
        try:
            found.append((name, json.load(io.open(path, encoding='utf-8-sig'))))
        except Exception:
            continue
    return found


def current_device():
    """The deck Stream Dock is currently set to, from its own config."""
    if not os.path.isfile(CONFIG):
        return None
    section = None
    for line in io.open(CONFIG, encoding='utf-8', errors='replace'):
        line = line.strip()
        if line.startswith('[') and line.endswith(']'):
            section = line[1:-1]
        elif section == 'StreamDockCurrentDevice' and line.lower().startswith('device='):
            return line.split('=', 1)[1].strip()
    return None


def device_fields(existing, wanted):
    """
    Copy the device identity off a profile that targets this deck.

    Several decks can have profiles here, and their ids look alike - picking the first one found
    silently builds the profiles for the wrong hardware, where they never show up.
    """
    if wanted:
        for _, m in existing:
            if m.get('DeviceUUID') == wanted:
                return {
                    'DeviceModel': m.get('DeviceModel', ''),
                    'DeviceSerialNumber': m.get('DeviceSerialNumber', ''),
                    'DeviceUUID': wanted,
                }

    return None


def backup(dry):
    stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
    dest = os.path.join(ROOT, f'backup-{stamp}')
    print(f'backup -> {dest}')
    if dry:
        return dest
    os.makedirs(dest, exist_ok=True)
    if os.path.isfile(DB):
        shutil.copy2(DB, os.path.join(dest, 'DataCache.db'))
    if os.path.isfile(CONFIG):
        shutil.copy2(CONFIG, os.path.join(dest, 'StreamDockConfig.ini'))
    if os.path.isdir(PROFILES):
        shutil.copytree(PROFILES, os.path.join(dest, 'profiles'))
    return dest


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dry-run', action='store_true')
    args = ap.parse_args()

    if not APPDATA or not os.path.isdir(ROOT):
        print('Stream Dock data folder not found - nothing to do.')
        return 0

    if running():
        print('ERROR: Stream Dock is running. Close it first, or it will rewrite these on exit.')
        return 1

    existing = load_profiles()
    wanted = current_device()

    if not wanted:
        print('ERROR: could not read the current device from StreamDockConfig.ini.')
        return 1

    device = device_fields(existing, wanted)

    if not device:
        print(f'ERROR: no existing profile for device {wanted} to copy the identity from.')
        print('       Open Stream Dock once with the deck plugged in, then re-run.')
        return 1

    print(f'device: {device["DeviceUUID"]} model {device["DeviceModel"]} '
          f'serial {device["DeviceSerialNumber"]}')

    have = {m.get('Name'): folder for folder, m in existing}
    todo = [n for n in PROFILE_NAMES if n not in have]

    for n in PROFILE_NAMES:
        if n in have:
            print(f'  exists: {n:16s} -> {have[n]}')

    if not todo:
        print('All Elite profiles already exist.')
        return 0

    backup(args.dry_run)

    created = []
    for name in todo:
        folder = new_uuid() + '.sdProfile'
        path = os.path.join(PROFILES, folder)

        manifest = {
            'Actions': {},
            'DeviceModel': device['DeviceModel'],
            'DeviceSerialNumber': device['DeviceSerialNumber'],
            'DeviceUUID': device['DeviceUUID'],
            'Name': name,
            'Pages': {'Current': folder, 'Pages': [folder]},
            'Version': '1.0',
        }

        print(f'  create: {name:16s} -> {folder}')
        created.append(folder)

        if args.dry_run:
            continue

        os.makedirs(os.path.join(path, 'Images'), exist_ok=True)
        os.makedirs(os.path.join(path, 'profiles'), exist_ok=True)
        io.open(os.path.join(path, 'manifest.json'), 'w', encoding='utf-8').write(
            json.dumps(manifest, indent=1, ensure_ascii=False))

    if args.dry_run:
        print('dry run - nothing written')
        return 0

    table = device['DeviceUUID']
    con = sqlite3.connect(DB)
    cur = con.cursor()
    cur.execute(f'CREATE TABLE IF NOT EXISTS "{table}"(Profile TEXT)')
    rows = {r[0] for r in cur.execute(f'SELECT Profile FROM "{table}"')}
    for folder in created:
        if folder not in rows:
            cur.execute(f'INSERT INTO "{table}"(Profile) VALUES (?)', (folder,))
    con.commit()

    total = cur.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0]
    con.close()

    print(f'registered {len(created)} profile(s); table "{table}" now has {total} row(s)')
    print('Start Stream Dock - the new profiles should appear in the profile list.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
