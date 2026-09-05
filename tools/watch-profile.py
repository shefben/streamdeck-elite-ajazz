"""Watch which Stream Dock profile is active, and print every change.

Stream Dock records the active profile per device in StreamDockConfig.ini, so polling that is a
trustworthy oracle - it is the host's own state, not something the bridge reports about itself.

    python tools/watch-profile.py            # follow until ctrl-c
    python tools/watch-profile.py --for 90   # stop after 90 seconds

Pair it with the profile-cycle-test plugin to verify switching without running the game.
"""

import argparse
import configparser
import datetime
import glob
import io
import json
import os
import sys
import time

ROOT = os.path.join(os.environ.get('APPDATA', ''), 'HotSpot', 'StreamDock')
CONFIG = os.path.join(ROOT, 'config', 'StreamDockConfig.ini')
PROFILES = os.path.join(ROOT, 'profiles')


def folder_names():
    """folder -> display name, so the log reads in profile names rather than UUIDs."""
    out = {}
    for d in glob.glob(os.path.join(PROFILES, '*.sdProfile')):
        mf = os.path.join(d, 'manifest.json')
        if not os.path.isfile(mf):
            continue
        try:
            m = json.load(io.open(mf, encoding='utf-8-sig'))
        except Exception:
            continue
        out[os.path.basename(d)] = m.get('Name') or '(unnamed)'
    return out


def active():
    """The active profile folder for every device section that has one."""
    cp = configparser.ConfigParser(strict=False)
    try:
        cp.read_string(io.open(CONFIG, encoding='utf-8', errors='replace').read())
    except Exception:
        return {}
    found = {}
    for section in cp.sections():
        if not section.endswith('DeviceConfig'):
            continue
        val = cp[section].get('sdprofilename')
        if val:
            found[section] = val
    return found


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--for', dest='seconds', type=float, default=None,
                    help='stop after this many seconds')
    ap.add_argument('--interval', type=float, default=1.0)
    args = ap.parse_args()

    if not os.path.isfile(CONFIG):
        sys.exit('Stream Dock config not found: ' + CONFIG)

    names = folder_names()
    last = {}
    started = time.time()
    changes = 0

    print('watching %s' % CONFIG)
    print('%-8s %-28s %s' % ('time', 'device', 'profile'))

    while True:
        cur = active()

        for section, folder in cur.items():
            if last.get(section) == folder:
                continue
            if folder not in names:
                names = folder_names()      # a profile may have been created since
            stamp = datetime.datetime.now().strftime('%H:%M:%S')
            label = names.get(folder, folder)
            prefix = '' if section not in last else '-> '
            print('%-8s %-28s %s%s' % (stamp, section.replace('DeviceConfig', ''), prefix, label))
            if section in last:
                changes += 1
            last[section] = folder

        if args.seconds is not None and time.time() - started >= args.seconds:
            print('\n%d change(s) observed in %.0fs' % (changes, time.time() - started))
            return 0 if changes else 1

        time.sleep(args.interval)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print('\nstopped')
