/*
 * Make switchToProfile work on Stream Dock (Ajazz / Mirabox).
 *
 * Stream Dock parses the Elgato switchToProfile event and emits signal 12 on SDPluginServer, but
 * nothing is connected to it, so the event does nothing - verified with thirteen payload shapes.
 * Every other event the plugin needs (setImage, setTitle, setState) works. Without this the
 * plugin's automatic profile switching - ship / SRV / on foot / fighter - silently never fires.
 *
 * This bridges the gap. It hooks the switchToProfile handler, reads the requested profile name,
 * and calls the app's own profile selector with the matching item, which is the same call the UI
 * makes when you click a profile in the list.
 *
 * Nothing is patched. The hook only observes, then calls an existing routine.
 *
 *   C:\Python39\Scripts\frida -n "Stream Dock AJAZZ.exe" -l tools/streamdock-profile-bridge.js
 *
 * Offsets are for Stream Dock AJAZZ 3.10.191.0421 and are checked against a byte signature at
 * load. On any other build it refuses to run rather than calling into whatever now lives there.
 */

'use strict';

const IMAGE_BASE = ptr('0x140000000');

const RVA = {
    // dispatcher branch handler for "switchToProfile"; 5th arg is the profile QString
    switchHandler: ptr('0x1402F82D0').sub(IMAGE_BASE),
    // sub_140129BE0(ctrl, item) - "select this profile", what the UI list calls
    selector: ptr('0x140129BE0').sub(IMAGE_BASE),
    // vftables used to find the live objects
    itemVft: ptr('0x1425D31B8').sub(IMAGE_BASE),
    ctrlVft: ptr('0x1425C2B80').sub(IMAGE_BASE),
};

// Byte signatures guarding against a different build.
const SIG = {
    selector: [0x40, 0x53, 0x48, 0x83, 0xEC, 0x60, 0x48, 0x8B, 0xD9],
    switchHandler: [0x48, 0x83, 0xEC, 0x58, 0x48, 0x8B, 0x05],
};

const NAME_OFF = 0x1b8;   // profile item: d_ptr(+8) + 0x1b8 -> QString name
const CURRENT_OFF = 0x80; // ctrl + 0x80 -> currently selected item

const mod = Process.enumerateModules().find(m => m.name.indexOf('Stream Dock') === 0);
if (!mod) throw new Error('Stream Dock main module not found');
const base = mod.base;

function at(rva) { return base.add(rva); }

function sigOk(addr, bytes) {
    try {
        const got = Array.from(new Uint8Array(addr.readByteArray(bytes.length)));
        return bytes.every((b, i) => b === got[i]);
    } catch (e) { return false; }
}

const selector = at(RVA.selector);
const switchHandler = at(RVA.switchHandler);

console.log('[bridge] module base ' + base);

let ok = true;
[['selector', selector, SIG.selector], ['switchHandler', switchHandler, SIG.switchHandler]]
    .forEach(function (e) {
        const good = sigOk(e[1], e[2]);
        console.log('[bridge] ' + e[0] + ' @ ' + e[1] + ' signature ' + (good ? 'ok' : 'MISMATCH'));
        if (!good) ok = false;
    });

if (!ok) {
    console.log('[bridge] refusing to run: this is not Stream Dock AJAZZ 3.10.191.0421.');
    console.log('[bridge] the offsets must be re-derived before using it on this build.');
} else {

    function qstr(d) {
        try {
            if (d.isNull() || d.compare(ptr(0x10000)) < 0) return null;
            const size = d.add(4).readS32();
            if (size <= 0 || size > 300) return null;
            const off = d.add(16).readU64().toNumber();
            if (off < 0 || off > 0x10000) return null;
            const s = d.add(off).readUtf16String(size);
            return (s && /^[\x20-\x7e]+$/.test(s)) ? s : null;
        } catch (e) { return null; }
    }

    function nameOf(item) {
        try {
            const d = item.add(8).readPointer();
            return d.isNull() ? null : qstr(d.add(NAME_OFF).readPointer());
        } catch (e) { return null; }
    }

    // Profile items and the controller are found by vtable, so no addresses are hard-coded
    // beyond the module-relative ones above.
    function scanFor(vftRva) {
        const pattern = at(vftRva).toMatchPattern();
        const out = [];
        Process.enumerateRanges('rw-').forEach(function (r) {
            if (r.size > 0x4000000) return;
            try {
                Memory.scanSync(r.base, r.size, pattern).forEach(m => out.push(m.address));
            } catch (e) { }
        });
        return out;
    }

    function findCtrl() {
        const c = scanFor(RVA.ctrlVft);
        return c.length ? c[0] : null;
    }

    function findItems() {
        const map = {};
        scanFor(RVA.itemVft).forEach(function (a) {
            const n = nameOf(a);
            if (n) map[n] = a;
        });
        return map;
    }

    const select = new NativeFunction(selector, 'void', ['pointer', 'pointer']);

    let ctrl = findCtrl();
    let items = findItems();
    console.log('[bridge] controller: ' + ctrl);
    console.log('[bridge] profiles: ' + Object.keys(items).map(k => '"' + k + '"').join(', '));

    function switchTo(name) {
        if (!ctrl) ctrl = findCtrl();
        if (!ctrl) { console.log('[bridge] no controller found'); return false; }

        if (!(name in items)) items = findItems();   // profile may have been added since
        const item = items[name];
        if (!item) {
            console.log('[bridge] no profile named "' + name + '" - have: ' +
                Object.keys(items).join(', '));
            return false;
        }

        let current = null;
        try { current = ctrl.add(CURRENT_OFF).readPointer(); } catch (e) { }
        if (current && current.equals(item)) {
            console.log('[bridge] already on "' + name + '"');
            return true;
        }

        console.log('[bridge] switching to "' + name + '" (' + item + ')');
        select(ctrl, item);
        return true;
    }

    // The dispatcher calls the handler with the profile QString as the 5th argument.
    Interceptor.attach(switchHandler, {
        onEnter(args) {
            let name = null;
            try { name = qstr(args[4].readPointer()); } catch (e) { }
            if (!name) {
                console.log('[bridge] switchToProfile with no readable name');
                return;
            }
            console.log('[bridge] plugin requested profile "' + name + '"');
            try {
                switchTo(name);
            } catch (e) {
                console.log('[bridge] switch failed: ' + e);
            }
        }
    });

    rpc.exports = {
        list: () => Object.keys(findItems()),
        switchTo: switchTo,
    };

    console.log('[bridge] active - switchToProfile from any plugin will now switch profiles.');
}
