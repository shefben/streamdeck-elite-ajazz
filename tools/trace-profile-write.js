/*
 * Locate the routine that performs a profile switch, by its side effect.
 *
 * Tracing QMetaObject::activate during a hand-made profile switch showed no profile signal at
 * all - only UI/widget classes - so the switch is a direct call, not a signal. But every switch
 * writes sdProfileName into StreamDockConfig.ini, so hooking QSettings::setValue and taking a
 * backtrace when that key is written gives the call chain that performs it.
 *
 *   C:\Python39\Scripts\frida -n "Stream Dock AJAZZ.exe" -l tools/trace-profile-write.js
 *
 * Then switch profile by hand. The frames inside the main module are the switch path.
 */

'use strict';

const mod = Process.enumerateModules().find(m => m.name.indexOf('Stream Dock') === 0);
const qt = Process.enumerateModules().find(m => /Qt5Core/i.test(m.name));
console.log('[*] main=' + (mod && mod.base) + '  Qt5Core=' + (qt && qt.base));

function sym(name) {
    if (!qt) return null;
    return (typeof qt.findExportByName === 'function')
        ? qt.findExportByName(name)
        : Module.findExportByName(qt.name, name);
}

// QString out of a const QString& argument
function qstr(p) {
    try {
        const d = p.readPointer();
        if (d.isNull()) return null;
        const size = d.add(4).readS32();
        if (size <= 0 || size > 400) return null;
        const off = d.add(16).readU64().toNumber();
        return d.add(16).add(off).readUtf16String(size);
    } catch (e) {
        return null;
    }
}

function frames(ctx) {
    return Thread.backtrace(ctx, Backtracer.ACCURATE)
        .filter(a => mod && a.compare(mod.base) >= 0 && a.compare(mod.base.add(mod.size)) < 0)
        .map(a => '        main+' + a.sub(mod.base));
}

const targets = [
    ['?setValue@QSettings@@QEAAXAEBVQString@@AEBVQVariant@@@Z', 'QSettings::setValue'],
    ['?beginGroup@QSettings@@QEAAXAEBVQString@@@Z', 'QSettings::beginGroup'],
];

targets.forEach(function (t) {
    const addr = sym(t[0]);
    if (!addr) {
        console.log('[!] not found: ' + t[1]);
        return;
    }
    console.log('[*] hooked ' + t[1] + ' @ ' + addr);
    Interceptor.attach(addr, {
        onEnter(args) {
            const key = qstr(args[1]);
            if (!key) return;
            if (!/Profile|profile/.test(key)) return;
            console.log('');
            console.log('*** ' + t[1] + '("' + key + '")');
            frames(this.context).slice(0, 14).forEach(f => console.log(f));
        }
    });
});

console.log('');
console.log('[*] switch profile BY HAND now; the main+OFFSET frames are the switch path.');
