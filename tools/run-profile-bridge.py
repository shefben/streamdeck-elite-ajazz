"""Keep the Stream Dock profile bridge attached.

Stream Dock ignores the switchToProfile event, so the plugin's automatic profile switching never
fires on it. streamdock-profile-bridge.js supplies the missing step. This attaches it and stays
running, reattaching if Stream Dock restarts.

    python tools/run-profile-bridge.py

Requires frida (pip install frida). Stream Dock must be running, or will be waited for.
"""

import io
import os
import sys
import time

try:
    import frida
except ImportError:
    sys.exit('frida is not installed:  pip install frida')

HERE = os.path.dirname(os.path.abspath(__file__))
AGENT = os.path.join(HERE, 'streamdock-profile-bridge.js')
PROC = 'Stream Dock AJAZZ.exe'


def find():
    device = frida.get_local_device()
    for p in device.enumerate_processes():
        if PROC.lower() in p.name.lower():
            return device, p.pid
    return device, None


def main():
    if not os.path.isfile(AGENT):
        sys.exit('agent not found: ' + AGENT)

    source = io.open(AGENT, encoding='utf-8').read()
    attached_to = None

    print('watching for ' + PROC + ' (ctrl-c to stop)')

    while True:
        device, pid = find()

        if pid is None:
            attached_to = None
            time.sleep(3)
            continue

        if pid == attached_to:
            time.sleep(2)
            continue

        try:
            session = device.attach(pid)
            script = session.create_script(source)
            script.set_log_handler(lambda level, text: print(text, flush=True))
            script.load()
            attached_to = pid
            print('attached to pid %d' % pid)

            # Stream Dock rebuilds its profile list on some actions; the agent rescans by
            # vtable when a name is not found, so nothing further is needed here.
            session.on('detached', lambda *a: print('detached from pid %d' % pid, flush=True))
        except Exception as exc:
            print('attach failed: %s' % exc)
            attached_to = None
            time.sleep(3)


if __name__ == '__main__':
    try:
        main()
    except KeyboardInterrupt:
        print('\nstopped')
