/* Drives Sony's libprcim.dll (PictureGear Lite 4.2, Image Converter 1.1) under Wine.
 *
 *   prcim d in.prc out.raw                     decode with prc_read_rgb_image
 *   prcim e in.raw out.prc name comp depth     encode with prc_write_rgb_image
 *
 * A .raw is the width and height as little-endian 32-bit words, then rows of blue, green, red,
 * top row first, unpadded. comp is the value put in the state's compression field (1 = run-length,
 * 255 = plain, as PgCore.dll sets it); depth is 8 or 16 (16 needs the 2003 library).
 *
 * Built without a C runtime, beside the DLL it loads:
 *   llvm-dlltool -k -m i386 -d k32.def -l k32.lib     (k32.def lists the kernel32 imports below)
 *   clang --target=i686-pc-windows-msvc -O1 -ffreestanding -fno-builtin -fno-stack-protector -mno-sse -c prcim.c
 *   lld-link /subsystem:console /entry:mainCRTStartup /nodefaultlib prcim.obj k32.lib */
typedef unsigned int u32;
typedef unsigned short u16;
typedef unsigned char u8;
typedef void *HANDLE;
#define WINAPI __stdcall
__declspec(dllimport) char *WINAPI GetCommandLineA(void);
__declspec(dllimport) HANDLE WINAPI CreateFileA(const char *, u32, u32, void *, u32, u32, HANDLE);
__declspec(dllimport) int WINAPI WriteFile(HANDLE, const void *, u32, u32 *, void *);
__declspec(dllimport) int WINAPI ReadFile(HANDLE, void *, u32, u32 *, void *);
__declspec(dllimport) int WINAPI CloseHandle(HANDLE);
__declspec(dllimport) u32 WINAPI GetFileSize(HANDLE, u32 *);
__declspec(dllimport) void WINAPI ExitProcess(u32);
__declspec(dllimport) HANDLE WINAPI GetStdHandle(u32);
__declspec(dllimport) void *WINAPI VirtualAlloc(void *, u32, u32, u32);
__declspec(dllimport) HANDLE WINAPI LoadLibraryA(const char *);
__declspec(dllimport) void *WINAPI GetProcAddress(HANDLE, const char *);
__declspec(dllimport) u32 WINAPI GetLastError(void);

static void out(const char *s) { u32 n = 0, w; while (s[n]) n++; WriteFile(GetStdHandle((u32)-11), s, n, &w, 0); }
static void outnum(int v) { char b[16]; int i = 15; unsigned u = v < 0 ? -v : v; b[i] = 0; if (!u) b[--i] = '0'; while (u) { b[--i] = '0' + u % 10; u /= 10; } if (v < 0) b[--i] = '-'; out(b + i); }
static void *alloc(u32 n) { return VirtualAlloc(0, n + 64, 0x3000, 4); }
static u8 *slurp(const char *p, u32 *len) {
  HANDLE h = CreateFileA(p, 0x80000000, 1, 0, 3, 0x80, 0);
  if (h == (HANDLE)-1) return 0;
  u32 n = GetFileSize(h, 0), r; u8 *b = alloc(n); ReadFile(h, b, n, &r, 0); CloseHandle(h); *len = n; return b;
}
static int atoi_(const char *s) { int v = 0; while (*s >= '0' && *s <= '9') v = v * 10 + (*s++ - '0'); return v; }
static char *argv_[32]; static int argc_;
static void parse(void) {
  char *c = GetCommandLineA(); static char buf[8192]; int i = 0;
  while (*c && argc_ < 32) {
    while (*c == ' ') c++;
    if (!*c) break;
    argv_[argc_++] = buf + i;
    if (*c == '"') { c++; while (*c && *c != '"') buf[i++] = *c++; if (*c) c++; }
    else while (*c && *c != ' ') buf[i++] = *c++;
    buf[i++] = 0;
  }
}

struct mem { u8 *p; u32 len, pos; HANDLE f; };
static int __cdecl rd(void *buf, int n, struct mem *m) {
  int k = 0; u8 *d = buf;
  while (k < n && m->pos < m->len) d[k++] = m->p[m->pos++];
  return k;
}
static int __cdecl wr(const void *buf, int n, struct mem *m) { u32 w; WriteFile(m->f, buf, (u32)n, &w, 0); return (int)w; }

typedef u8 *(__cdecl *fOpen)(const char *, const char *);
typedef void (__cdecl *fCb)(void *, void **);
typedef int (__cdecl *fHdr)(void *);
typedef void (__cdecl *fStart)(void *);
typedef int (__cdecl *fRgb)(void *, u8 *, int);
typedef int (__cdecl *fW)(void *, u8 *, u8 *);
typedef void (__cdecl *fClose)(void *);

void __stdcall mainCRTStartup(void) {
  parse();
  HANDLE dll = LoadLibraryA("libprcim.dll");
  if (!dll) { out("load failed\n"); ExitProcess(3); }
  fOpen Open = (fOpen)GetProcAddress(dll, "prc_open");
  fCb Cb = (fCb)GetProcAddress(dll, "prc_callback_src");
  fHdr Hdr = (fHdr)GetProcAddress(dll, "prc_read_header");
  fStart Start = (fStart)GetProcAddress(dll, "prc_start_decompress");
  fRgb Rgb = (fRgb)GetProcAddress(dll, "prc_read_rgb_image");
  fW Wr = (fW)GetProcAddress(dll, "prc_write_rgb_image");
  fClose Close = (fClose)GetProcAddress(dll, "prc_close");
  if (argc_ < 4) { out("usage\n"); ExitProcess(1); }
  static struct mem m;
  void *cb[4] = { (void *)rd, &m, (void *)wr, &m };
  if (argv_[1][0] == 'd') {
    m.p = slurp(argv_[2], &m.len);
    if (!m.p) { out("cannot read\n"); ExitProcess(2); }
    u8 *st = Open(argv_[2], "r");
    Cb(st, cb);
    if (!Hdr(st)) { out("header rejected\n"); ExitProcess(4); }
    Start(st);
    u16 w = *(u16 *)(st + 0x60), h = *(u16 *)(st + 0x62);
    out("w="); outnum(w); out(" h="); outnum(h); out(" tile="); outnum(*(u16 *)(st + 0x6a)); out(" comp="); outnum(*(u16 *)(st + 0x6c)); out(" depth="); outnum(*(u16 *)(st + 0x66)); out(" pal="); outnum(*(u16 *)(st + 0x72)); out("\n");
    u8 *px = alloc((u32)w * h * 3 + 16);
    int ok = Rgb(st, px, 0);
    out("read="); outnum(ok); out("\n");
    HANDLE f = CreateFileA(argv_[3], 0x40000000, 0, 0, 2, 0x80, 0);
    u32 hw[2] = { w, h }, wn; WriteFile(f, hw, 8, &wn, 0); WriteFile(f, px, (u32)w * h * 3, &wn, 0); CloseHandle(f);
    Close(st);
    ExitProcess(ok ? 0 : 5);
  } else {
    u32 len; u8 *in = slurp(argv_[2], &len);
    if (!in) { out("cannot read\n"); ExitProcess(2); }
    u32 w = ((u32 *)in)[0], h = ((u32 *)in)[1];
    u8 *st = Open(argc_ > 4 ? argv_[4] : "out", "wb");
    m.f = CreateFileA(argv_[3], 0x40000000, 0, 0, 2, 0x80, 0);
    Cb(st, cb);
    *(u16 *)(st + 0x60) = (u16)w; *(u16 *)(st + 0x62) = (u16)h;
    *(u16 *)(st + 0x6c) = (u16)(argc_ > 5 ? atoi_(argv_[5]) : 1);
    *(u16 *)(st + 0x66) = (u16)(argc_ > 6 ? atoi_(argv_[6]) : 8);
    int ok = Wr(st, in + 8, 0);
    CloseHandle(m.f);
    out("write="); outnum(ok); out("\n");
    ExitProcess(ok ? 0 : 5);
  }
}
