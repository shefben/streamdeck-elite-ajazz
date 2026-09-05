"""Code-cave patcher for Stream Dock AJAZZ, so it auto-loads the profile bridge DLL.

Adds a small section to the executable and redirects its entry point through a stub that
LoadLibraryW's the bridge DLL, then jumps to the original entry point. The DLL installs the
profile-switch hook itself. Nothing in the original code is overwritten; a new section and a new
entry point are added, and the original entry point still runs.

This modifies a copy of Stream Dock on THIS machine. It does not redistribute anything - the
proprietary executable stays where it is, patched in place, with a .bak backup beside it.

Usage:
    python patch-streamdock.py               # patch the installed Stream Dock (needs admin)
    python patch-streamdock.py --exe PATH    # patch a specific copy (for testing)
    python patch-streamdock.py --revert       # restore from the .bak backup

The bridge DLL (streamdock-profile-bridge.dll) is copied next to the executable as sdbridge.dll,
which is where LoadLibraryW finds it by bare name.
"""

import argparse
import os
import shutil
import struct
import sys

DEFAULT_EXE = r"C:\Program Files (x86)\Stream Dock AJAZZ Global\Stream Dock AJAZZ.exe"
DLL_NAME = "sdbridge.dll"
SECTION_NAME = b".sdbrdg\x00"

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_DLL = os.path.join(HERE, "build", "streamdock-profile-bridge.dll")

# The build this patcher understands. Guard on the entry point so we never patch a changed exe.
EXPECT_ENTRY_RVA = 0x3395E0
EXPECT_LOADLIB_RVA = 0x385188   # LoadLibraryW import thunk


def u16(b, o): return struct.unpack_from("<H", b, o)[0]
def u32(b, o): return struct.unpack_from("<I", b, o)[0]
def p16(b, o, v): struct.pack_into("<H", b, o, v)
def p32(b, o, v): struct.pack_into("<I", b, o, v)


def align(v, a):
    return (v + a - 1) & ~(a - 1)


def patch(data):
    data = bytearray(data)

    if u16(data, 0) != 0x5A4D:
        raise SystemExit("not a PE file (no MZ header)")
    pe = u32(data, 0x3C)
    if u32(data, pe) != 0x00004550:
        raise SystemExit("not a PE file (no PE signature)")

    coff = pe + 4
    num_sections = u16(data, coff + 2)
    opt = coff + 20
    magic = u16(data, opt)
    if magic != 0x20B:
        raise SystemExit("not a 64-bit PE")

    entry_rva = u32(data, opt + 16)
    image_base = struct.unpack_from("<Q", data, opt + 24)[0]
    section_align = u32(data, opt + 32)
    file_align = u32(data, opt + 36)
    size_of_image = u32(data, opt + 56)
    size_of_headers = u32(data, opt + 60)
    num_dirs = u32(data, opt + 108)
    dir_table = opt + 112

    if entry_rva == EXPECT_ENTRY_RVA + 0:  # already the original entry
        pass
    else:
        # Could already be patched, or a different build.
        pass

    sec_table = opt + u16(data, coff + 16)   # opt + SizeOfOptionalHeader
    # sanity: recompute from COFF SizeOfOptionalHeader
    size_opt = u16(data, coff + 16)
    sec_table = opt + size_opt

    # Detect an existing patch.
    for i in range(num_sections):
        name = bytes(data[sec_table + i * 40: sec_table + i * 40 + 8])
        if name.rstrip(b"\x00") == SECTION_NAME.rstrip(b"\x00"):
            raise SystemExit("already patched (found %s section)" % SECTION_NAME.rstrip(b'\x00').decode())

    if entry_rva != EXPECT_ENTRY_RVA:
        raise SystemExit(
            "entry point is 0x%X, expected 0x%X - this is a different build; refusing to patch"
            % (entry_rva, EXPECT_ENTRY_RVA))

    # New section geometry.
    last = sec_table + (num_sections - 1) * 40
    last_va = u32(data, last + 12)
    last_vsz = u32(data, last + 8)
    new_va = align(last_va + last_vsz, section_align)

    raw_size = file_align                       # one file-alignment unit is plenty
    new_raw = align(len(data), file_align)

    # Header room check.
    header_end = sec_table + num_sections * 40
    if header_end + 40 > size_of_headers:
        raise SystemExit("no room in the header for another section")

    # ---- build the cave contents ----
    body = bytearray(raw_size)

    path_off = 0x00
    wpath = (DLL_NAME + "\x00").encode("utf-16-le")
    body[path_off:path_off + len(wpath)] = wpath

    stub_off = 0x80
    path_rva = new_va + path_off
    stub_rva = new_va + stub_off
    loadlib_rva = EXPECT_LOADLIB_RVA

    stub = bytearray()

    def rip(disp_target_rva, insn_rva, insn_len):
        return (disp_target_rva - (insn_rva + insn_len)) & 0xFFFFFFFF

    cur = stub_rva
    # sub rsp, 28h
    stub += bytes([0x48, 0x83, 0xEC, 0x28]); cur += 4
    # lea rcx, [rip+path]
    d = rip(path_rva, cur, 7); stub += bytes([0x48, 0x8D, 0x0D]) + struct.pack("<I", d); cur += 7
    # call qword ptr [rip+loadlib]
    d = rip(loadlib_rva, cur, 6); stub += bytes([0xFF, 0x15]) + struct.pack("<I", d); cur += 6
    # add rsp, 28h
    stub += bytes([0x48, 0x83, 0xC4, 0x28]); cur += 4
    # lea rax, [rip+oep]
    d = rip(EXPECT_ENTRY_RVA, cur, 7); stub += bytes([0x48, 0x8D, 0x05]) + struct.pack("<I", d); cur += 7
    # jmp rax
    stub += bytes([0xFF, 0xE0]); cur += 2

    body[stub_off:stub_off + len(stub)] = stub

    # ---- section header ----
    CHAR_CODE = 0x20000000  # executable
    CHAR_READ = 0x40000000
    hdr = bytearray(40)
    hdr[0:8] = SECTION_NAME
    p32(hdr, 8, raw_size)          # VirtualSize
    p32(hdr, 12, new_va)           # VirtualAddress
    p32(hdr, 16, raw_size)         # SizeOfRawData
    p32(hdr, 20, new_raw)          # PointerToRawData
    p32(hdr, 36, CHAR_CODE | CHAR_READ)
    data[header_end:header_end + 40] = hdr

    # ---- header fixups ----
    p16(data, coff + 2, num_sections + 1)
    p32(data, opt + 16, stub_rva)                       # AddressOfEntryPoint -> stub
    p32(data, opt + 56, align(new_va + raw_size, section_align))  # SizeOfImage

    # Drop the now-invalid certificate directory so nothing trips over a stale signature.
    if num_dirs > 4:
        p32(data, dir_table + 4 * 8, 0)      # security dir RVA
        p32(data, dir_table + 4 * 8 + 4, 0)  # security dir size

    # ---- append raw section data ----
    if new_raw > len(data):
        data += b"\x00" * (new_raw - len(data))
    data[new_raw:new_raw + raw_size] = body

    return bytes(data)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--exe", default=DEFAULT_EXE)
    ap.add_argument("--revert", action="store_true")
    args = ap.parse_args()

    exe = args.exe
    bak = exe + ".bak"

    if args.revert:
        if not os.path.isfile(bak):
            sys.exit("no backup at " + bak)
        shutil.copy2(bak, exe)
        print("restored", exe, "from backup")
        return

    if not os.path.isfile(exe):
        sys.exit("executable not found: " + exe)
    if not os.path.isfile(SRC_DLL):
        sys.exit("bridge DLL not built: " + SRC_DLL)

    exe_dir = os.path.dirname(exe)
    dst_dll = os.path.join(exe_dir, DLL_NAME)

    try:
        shutil.copy2(SRC_DLL, dst_dll)
    except PermissionError:
        sys.exit("cannot write %s - run as administrator, and close Stream Dock first" % dst_dll)
    print("copied bridge DLL ->", dst_dll)

    if not os.path.isfile(bak):
        shutil.copy2(exe, bak)
        print("backed up ->", bak)

    with open(exe, "rb") as f:
        original = f.read()

    patched = patch(original)

    try:
        with open(exe, "wb") as f:
            f.write(patched)
    except PermissionError:
        sys.exit("cannot write %s - run as administrator, and close Stream Dock first" % exe)

    print("patched", exe)
    print("entry point now runs the bridge loader, then the original startup.")
    print("start Stream Dock; switchToProfile from any plugin will switch profiles.")


if __name__ == "__main__":
    main()
