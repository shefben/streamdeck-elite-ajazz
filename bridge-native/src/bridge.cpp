// streamdock-profile-bridge.dll
//
// Makes the Elgato switchToProfile event actually switch profiles on Stream Dock (Ajazz /
// Mirabox). Stream Dock parses that event and emits an internal Qt signal that nothing is
// connected to, so profile switching from a plugin silently does nothing. The Elite Dangerous
// plugin relies on it to change layouts for ship / SRV / on-foot / fighter.
//
// This is the native form of tools/streamdock-profile-bridge.js, which was verified end to end.
// It replaces the (dead) switchToProfile handler with one that finds the profile the plugin
// asked for and calls the app's own profile selector - the same call the profile list makes on
// a click.
//
// Offsets are for Stream Dock AJAZZ 3.10.191.0421 and are checked against a byte signature at
// load. On any other build the DLL does nothing rather than calling into the wrong code.
//
// Nothing but the one dead handler is modified, and the switch goes through an existing routine.

#include <windows.h>
#include <cstdint>
#include <cwchar>

namespace {

// Preferred image base the RVAs were taken from.
constexpr uintptr_t kPreferredBase = 0x140000000ull;

// Relative virtual addresses in Stream Dock AJAZZ 3.10.191.0421.
constexpr uintptr_t kRvaSwitchHandler = 0x2F82D0;  // dead switchToProfile handler
constexpr uintptr_t kRvaSelector      = 0x129BE0;  // sub_140129BE0(ctrl, item): "select profile"
constexpr uintptr_t kRvaItemVft       = 0x25D31B8; // profile item widget vtable
constexpr uintptr_t kRvaCtrlVft       = 0x25C2B80; // profile controller vtable

// Object field offsets.
constexpr size_t kNameOff    = 0x1B8;  // item d_ptr -> QString name
constexpr size_t kCurrentOff = 0x80;   // ctrl -> current item
constexpr size_t kDPtrOff    = 0x08;   // QObject d-pointer

// Byte signatures guarding against a different build.
const uint8_t kSigSwitch[]   = {0x48, 0x83, 0xEC, 0x58, 0x48, 0x8B, 0x05};
const uint8_t kSigSelector[] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x60, 0x48, 0x8B, 0xD9};

uintptr_t g_base = 0;

typedef void(__fastcall* SelectFn)(void* ctrl, void* item);
SelectFn g_select = nullptr;

#ifdef BRIDGE_DEBUG
void DbgLog(const char* fmt, ...) {
    char line[512];
    va_list ap;
    va_start(ap, fmt);
    wvsprintfA(line, fmt, ap);
    va_end(ap);
    HANDLE f = CreateFileA("F:\\bridge_dll.log", FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (f != INVALID_HANDLE_VALUE) {
        DWORD w;
        SetFilePointer(f, 0, nullptr, FILE_END);
        WriteFile(f, line, lstrlenA(line), &w, nullptr);
        WriteFile(f, "\r\n", 2, &w, nullptr);
        CloseHandle(f);
    }
}
#define DBG(...) DbgLog(__VA_ARGS__)
#else
#define DBG(...) ((void)0)
#endif

inline void* At(uintptr_t rva) { return reinterpret_cast<void*>(g_base + rva); }

bool SigMatches(uintptr_t rva, const uint8_t* sig, size_t n) {
    const uint8_t* p = reinterpret_cast<const uint8_t*>(At(rva));
    for (size_t i = 0; i < n; ++i) {
        if (p[i] != sig[i]) return false;
    }
    return true;
}

// Read a Qt5 QString by its Data pointer (what a QString object holds). Returns the length, and
// writes up to cap-1 wide chars plus a terminator into out. Non-QString pointers are rejected.
int ReadQString(void* dataPtr, wchar_t* out, int cap) {
    if (!dataPtr || reinterpret_cast<uintptr_t>(dataPtr) < 0x10000) return -1;

    __try {
        const uint8_t* d = reinterpret_cast<const uint8_t*>(dataPtr);
        int size = *reinterpret_cast<const int*>(d + 4);
        if (size <= 0 || size > 300) return -1;

        int64_t off = *reinterpret_cast<const int64_t*>(d + 16);
        if (off < 0 || off > 0x10000) return -1;

        const wchar_t* chars = reinterpret_cast<const wchar_t*>(d + off);
        int n = size < cap - 1 ? size : cap - 1;
        for (int i = 0; i < n; ++i) {
            wchar_t c = chars[i];
            if (c < 0x20 || c > 0x7e) return -1;  // names are plain ASCII
            out[i] = c;
        }
        out[n] = 0;
        return n;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return -1;
    }
}

bool ReadPtr(const void* at, void** out) {
    __try {
        *out = *reinterpret_cast<void* const*>(at);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// Name of a profile item widget: item -> d_ptr -> +0x1B8 QString.
int ItemName(void* item, wchar_t* out, int cap) {
    void* dptr = nullptr;
    if (!ReadPtr(reinterpret_cast<uint8_t*>(item) + kDPtrOff, &dptr) || !dptr) return -1;
    void* nameData = nullptr;
    if (!ReadPtr(reinterpret_cast<uint8_t*>(dptr) + kNameOff, &nameData)) return -1;
    return ReadQString(nameData, out, cap);
}

bool HasVft(void* obj, uintptr_t vftRva) {
    void* vft = nullptr;
    if (!ReadPtr(obj, &vft)) return false;
    return vft == At(vftRva);
}

// Scan committed read/write memory for objects whose first qword is the given vtable.
// found[] collects up to max hits; returns the count.
int ScanVft(uintptr_t vftRva, void** found, int max) {
    void* wanted = At(vftRva);
    int count = 0;

    MEMORY_BASIC_INFORMATION mbi;
    uint8_t* addr = nullptr;
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    uint8_t* end = reinterpret_cast<uint8_t*>(si.lpMaximumApplicationAddress);

    while (addr < end && VirtualQuery(addr, &mbi, sizeof(mbi)) == sizeof(mbi)) {
        bool usable = mbi.State == MEM_COMMIT &&
                      (mbi.Protect == PAGE_READWRITE || mbi.Protect == PAGE_WRITECOPY) &&
                      mbi.RegionSize <= 0x4000000;
        if (usable) {
            uint8_t* base = reinterpret_cast<uint8_t*>(mbi.BaseAddress);
            size_t words = mbi.RegionSize / sizeof(void*);
            void** p = reinterpret_cast<void**>(base);
            __try {
                for (size_t i = 0; i < words; ++i) {
                    if (p[i] == wanted) {
                        found[count++] = &p[i];
                        if (count >= max) return count;
                    }
                }
            } __except (EXCEPTION_EXECUTE_HANDLER) {
                // skip a bad region
            }
        }
        addr = reinterpret_cast<uint8_t*>(mbi.BaseAddress) + mbi.RegionSize;
    }
    return count;
}

// A scan for the controller vtable also matches the scanner's own `wanted` local and any spilled
// copies on the stack. The real controller is the one whose current-item slot (+0x80) points at
// an actual profile item widget, so validate that rather than trusting the first hit.
bool LooksLikeCtrl(void* cand) {
    void* current = nullptr;
    if (!ReadPtr(reinterpret_cast<uint8_t*>(cand) + kCurrentOff, &current)) return false;
    return current != nullptr && HasVft(current, kRvaItemVft);
}

void* FindCtrl() {
    void* hits[16];
    int n = ScanVft(kRvaCtrlVft, hits, 16);
    for (int i = 0; i < n; ++i) {
        if (LooksLikeCtrl(hits[i])) return hits[i];
    }
    return nullptr;
}

// Find the profile item whose name matches. Rescans every call - cheap next to a bad pointer.
void* FindItemByName(const wchar_t* name) {
    void* hits[64];
    int n = ScanVft(kRvaItemVft, hits, 64);
    wchar_t buf[128];
    for (int i = 0; i < n; ++i) {
        if (ItemName(hits[i], buf, 128) > 0 && wcscmp(buf, name) == 0) {
            return hits[i];
        }
    }
    return nullptr;
}

volatile LONG g_busy = 0;

// Replacement for the dead switchToProfile handler. Same signature: the profile QString is the
// fifth argument. The original only emitted an unconnected signal, so it is not called.
void __fastcall SwitchHandler(void* /*sdPluginServer*/, void* /*a2*/, void* /*a3*/, void* /*a4*/,
                              void* nameQString) {
    if (InterlockedCompareExchange(&g_busy, 1, 0) != 0) return;

    __try {
        void* nameData = nullptr;
        DBG("handler: nameQString=%p", nameQString);
        if (nameQString && ReadPtr(nameQString, &nameData)) {
            wchar_t name[128];
            int len = ReadQString(nameData, name, 128);
            DBG("handler: nameData=%p len=%d name=%S", nameData, len, len > 0 ? name : L"?");
            if (len > 0) {
                void* ctrl = FindCtrl();
                void* item = FindItemByName(name);
                DBG("handler: ctrl=%p item=%p", ctrl, item);
                if (ctrl && item) {
                    void* current = nullptr;
                    ReadPtr(reinterpret_cast<uint8_t*>(ctrl) + kCurrentOff, &current);
                    DBG("handler: current=%p -> calling select", current);
                    if (current != item) {
                        g_select(ctrl, item);
                        DBG("handler: select returned");
                    }
                }
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        DBG("handler: EXCEPTION 0x%x", GetExceptionCode());
    }

    InterlockedExchange(&g_busy, 0);
}

// Overwrite the function entry with a 14-byte absolute jump to dest:
//   FF 25 00 00 00 00            jmp qword ptr [rip+0]
//   <8 byte absolute address>
bool WriteAbsoluteJump(void* at, void* dest) {
    uint8_t patch[14] = {0xFF, 0x25, 0, 0, 0, 0};
    *reinterpret_cast<uint64_t*>(patch + 6) = reinterpret_cast<uint64_t>(dest);

    DWORD old;
    if (!VirtualProtect(at, sizeof(patch), PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(at, patch, sizeof(patch));
    VirtualProtect(at, sizeof(patch), old, &old);
    FlushInstructionCache(GetCurrentProcess(), at, sizeof(patch));
    return true;
}

DWORD WINAPI Init(LPVOID) {
    g_base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    if (!g_base) return 0;

    if (!SigMatches(kRvaSwitchHandler, kSigSwitch, sizeof(kSigSwitch)) ||
        !SigMatches(kRvaSelector, kSigSelector, sizeof(kSigSelector))) {
        // Different build - do nothing rather than corrupt an unknown function.
        return 0;
    }

    g_select = reinterpret_cast<SelectFn>(At(kRvaSelector));
    WriteAbsoluteJump(At(kRvaSwitchHandler), reinterpret_cast<void*>(&SwitchHandler));
    return 0;
}

}  // namespace

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(module);
        // Do the work off the loader lock.
        HANDLE t = CreateThread(nullptr, 0, Init, nullptr, 0, nullptr);
        if (t) CloseHandle(t);
    }
    return TRUE;
}
