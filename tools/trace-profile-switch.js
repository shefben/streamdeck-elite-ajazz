/*
 * Find how Stream Dock actually switches profiles.
 *
 * The websocket dispatcher parses switchToProfile and emits Qt signal 12 on SDPluginServer, but
 * nothing acts on it - thirteen payload shapes were ignored. Everything else the plugin sends
 * (setImage, setTitle, setState) works, so the gap is a missing connection rather than a missing
 * feature. Qt routes every signal through QMetaObject::activate, so hooking that one function
 * shows which signal the app raises when a profile really does change.
 *
 * Run it, then switch profile by hand in the Stream Dock window. The signal that fires on the
 * click is the one an injected script must raise, or connect our own to.
 *
 *   C:\Python39\Scripts\frida -n "Stream Dock AJAZZ.exe" -l tools/trace-profile-switch.js
 */

'use strict';

const IMAGE_BASE = ptr('0x140000000');

// Addresses recovered statically from Stream Dock AJAZZ 3.10.191.0421.
const OFF = {
    pluginServerMeta: ptr('0x1426CDCC8').sub(IMAGE_BASE),   // SDPluginServer staticMetaObject
    pluginServerVft: ptr('0x1425CBDE0').sub(IMAGE_BASE),
    profileManagerVft: ptr('0x1425CE440').sub(IMAGE_BASE),
};

// Signal indices on SDPluginServer, from the emitter bodies.
const SIGNALS = {
    0x0C: 'switchToProfile',
    0x12: 'onSwitchToFolderProfile',
    0x13: 'onSwitchFromFolderProfile',
};

const mod = Process.enumerateModules().find(m => m.name.indexOf('Stream Dock') === 0);
if (!mod) {
    console.log('[!] main module not found');
} else {
    console.log('[*] ' + mod.name + ' base=' + mod.base + ' size=' + mod.size);
}

const base = mod ? mod.base : ptr(0);
const pluginServerMeta = base.add(OFF.pluginServerMeta);
const pluginServerVft = base.add(OFF.pluginServerVft);
const profileManagerVft = base.add(OFF.profileManagerVft);

console.log('[*] SDPluginServer   meta=' + pluginServerMeta + ' vft=' + pluginServerVft);
console.log('[*] SDProfileManager vft=' + profileManagerVft);

// QMetaObject::activate(QObject *sender, const QMetaObject *m, int local_signal_index, void **argv)
const qt = Process.enumerateModules().find(m => /Qt5Core/i.test(m.name));
if (!qt) {
    console.log('[!] Qt5Core not loaded');
} else {
    // Frida 17 moved export lookup onto the Module instance.
    const SYM = '?activate@QMetaObject@@SAXPEAVQObject@@PEBU1@HPEAPEAX@Z';
    const activate = (typeof qt.findExportByName === 'function')
        ? qt.findExportByName(SYM)
        : Module.findExportByName(qt.name, SYM);
    console.log('[*] Qt5Core=' + qt.name + ' activate=' + activate);

    // Read a QString (Qt5: d ptr -> {ref,size,alloc,...,offset}) for logging only.
    function qstr(pp) {
        try {
            const d = pp.readPointer();
            if (d.isNull()) return null;
            const size = d.add(4).readS32();
            if (size <= 0 || size > 200) return null;
            const off = d.add(16).readU64().toNumber();
            return d.add(16).add(off).readUtf16String(size);
        } catch (e) {
            return null;
        }
    }

    let inOurModule = 0;

    Interceptor.attach(activate, {
        onEnter(args) {
            const sender = args[0];
            const meta = args[1];
            const idx = args[2].toInt32();
            const argv = args[3];

            // only signals raised by this application's own classes
            if (meta.compare(base) < 0 || meta.compare(base.add(mod.size)) > 0) return;

            let vft = ptr(0);
            try { vft = sender.readPointer(); } catch (e) { }

            let who = 'meta=' + meta.sub(base);
            if (meta.equals(pluginServerMeta)) who = 'SDPluginServer';
            if (vft.equals(pluginServerVft)) who = 'SDPluginServer(obj)';
            if (vft.equals(profileManagerVft)) who = 'SDProfileManager';

            const named = SIGNALS[idx];
            const isProfile = named !== undefined ||
                /Profile/i.test(who) ||
                who === 'SDProfileManager';

            // first argument, if it looks like a QString, is usually the profile name
            let a1 = null;
            try { if (!argv.isNull()) a1 = qstr(argv.add(Process.pointerSize)); } catch (e) { }

            const line = '[signal] ' + who + ' idx=' + idx +
                (named ? ' (' + named + ')' : '') +
                (a1 ? ' arg1="' + a1 + '"' : '') +
                ' sender=' + sender + ' vft=' + vft.sub(base);

            if (isProfile) {
                console.log('*** ' + line);
            } else if (inOurModule++ < 400) {
                console.log(line);
            }
        }
    });

    console.log('');
    console.log('[*] tracing. Now switch profile BY HAND in the Stream Dock window.');
    console.log('[*] the signal logged at that moment is the one to reproduce.');
    console.log('');
}
