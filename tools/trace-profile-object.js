/*
 * Recover the object layout behind a profile switch.
 *
 * sub_140129BE0(ctrl, profileObj) is "select this profile": it stores profileObj at [ctrl+0x80]
 * and emits signal 2 on meta 0x1426CB620, which lands in sub_14024AEE0 -> sub_140249E80 and ends
 * up writing sdProfileName. To drive that from an injected script we need the ctrl instance and
 * a way to turn a profile NAME into the matching profileObj.
 *
 * This hooks the selector and dumps both objects, decoding any QString members it finds, so the
 * name/UUID field offsets become visible.
 *
 *   C:\Python39\Scripts\frida -n "Stream Dock AJAZZ.exe" -l tools/trace-profile-object.js
 *
 * Then switch profile by hand, ideally to one of the Elite profiles.
 */

'use strict';

const IMAGE_BASE = ptr('0x140000000');
const SELECT_OFF = ptr('0x140129BE0').sub(IMAGE_BASE);

const mod = Process.enumerateModules().find(m => m.name.indexOf('Stream Dock') === 0);
if (!mod) throw new Error('main module not found');

const base = mod.base;
const select = base.add(SELECT_OFF);
console.log('[*] main base=' + base);
console.log('[*] hooking selector at ' + select + '  (main+0x129be0)');

// Guard against a different build: the prologue should be push rbx / sub rsp,60h / mov rbx,rcx.
// push rbx carries a redundant REX prefix here (40 53), which the disassembly listing hides.
const expect = [0x40, 0x53, 0x48, 0x83, 0xEC, 0x60, 0x48, 0x8B, 0xD9];
const actual = Array.from(new Uint8Array(select.readByteArray(expect.length)));
const ok = expect.every((b, i) => b === actual[i]);
console.log('[*] prologue ' + (ok ? 'matches' : 'MISMATCH - offsets are for 3.10.191.0421') +
    ' : ' + actual.map(b => ('0' + b.toString(16)).slice(-2)).join(' '));
if (!ok) {
    console.log('[!] refusing to continue - this is a different build');
} else {

    // Qt5 QString: d -> { ref(4), size(4), alloc(4), pad(4), offset(8) }, data at d+16+offset
    function qstrFromD(d) {
        try {
            if (d.isNull() || d.compare(ptr(0x10000)) < 0) return null;
            const size = d.add(4).readS32();
            if (size <= 0 || size > 300) return null;
            const off = d.add(16).readU64().toNumber();
            if (off < 0 || off > 0x10000) return null;
            const s = d.add(16).add(off).readUtf16String(size);
            if (!s) return null;
            // Plain ASCII only. Anything wider is almost always a non-QString pointer being
            // decoded as one, and the garbage code points break the console encoding.
            return /^[\x20-\x7e]+$/.test(s) ? s : null;
        } catch (e) {
            return null;
        }
    }

    function dumpObj(label, obj, span) {
        console.log('  ' + label + ' = ' + obj);
        if (obj.isNull()) return;
        let vft = null;
        try { vft = obj.readPointer(); } catch (e) { }
        if (vft) console.log('     vft = main+' + vft.sub(base));
        for (let o = 0; o < span; o += 8) {
            let v;
            try { v = obj.add(o).readPointer(); } catch (e) { continue; }
            if (v.isNull()) continue;
            const s = qstrFromD(v);
            if (s) console.log('     +0x' + o.toString(16) + '  QString "' + s + '"');
        }
    }

    let hits = 0;
    Interceptor.attach(select, {
        onEnter(args) {
            hits++;
            console.log('');
            console.log('=== profile select #' + hits + ' ===');
            const ctrl = args[0];
            const prof = args[1];
            dumpObj('ctrl   ', ctrl, 0x140);
            dumpObj('profile', prof, 0x140);
            try {
                console.log('     ctrl[0x78] = ' + ctrl.add(0x78).readPointer());
                console.log('     ctrl[0x80] = ' + ctrl.add(0x80).readPointer() + '  (current)');
                console.log('     ctrl[0xD8] = ' + ctrl.add(0xD8).readPointer());
                console.log('     ctrl[0xE0] = ' + ctrl.add(0xE0).readPointer());
            } catch (e) { }
        }
    });

    console.log('');
    console.log('[*] switch profile BY HAND now (an Elite one is ideal).');
}
