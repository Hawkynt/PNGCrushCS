/* fiforacle <in> <out.raw> [ftt|-] [outW outH]
 * Drives Iterated Systems' DECO_32.DLL (6.1) and writes u32 w, u32 h, then RGB24 rows top-down.
 * Signatures as recorded by sp00nznet/encarta tools/decooracle (MIT). */
typedef unsigned int u32;
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

static void out(const char *s) { u32 n = 0, w; while (s[n]) n++; WriteFile(GetStdHandle((u32)-11), s, n, &w, 0); }
static void outnum(int v) { char b[16]; int i = 15; unsigned u = v < 0 ? -v : v; b[i] = 0; if (!u) b[--i] = '0'; while (u) { b[--i] = '0' + u % 10; u /= 10; } if (v < 0) b[--i] = '-'; out(b + i); }
static void *alloc(u32 n) { return VirtualAlloc(0, n + 16, 0x3000, 4); }
static u8 *slurp(const char *p, u32 *len) {
  HANDLE h = CreateFileA(p, 0x80000000, 1, 0, 3, 0x80, 0);
  if (h == (HANDLE)-1) return 0;
  u32 n = GetFileSize(h, 0), r; u8 *b = alloc(n); ReadFile(h, b, n, &r, 0); CloseHandle(h); *len = n; return b;
}
static void spit(const char *p, const void *d, u32 n) {
  HANDLE h = CreateFileA(p, 0x40000000, 0, 0, 2, 0x80, 0); u32 w;
  WriteFile(h, d, n, &w, 0); CloseHandle(h);
}
static int atoi_(const char *s) { int v = 0; while (*s >= '0' && *s <= '9') v = v * 10 + (*s++ - '0'); return v; }
static char *argv_[16]; static int argc_;
static void parse(void) {
  char *c = GetCommandLineA(); static char buf[4096]; int i = 0;
  while (*c && argc_ < 16) {
    while (*c == ' ') c++;
    if (!*c) break;
    argv_[argc_++] = buf + i;
    if (*c == '"') { c++; while (*c && *c != '"') buf[i++] = *c++; if (*c) c++; }
    else while (*c && *c != ' ') buf[i++] = *c++;
    buf[i++] = 0;
  }
}
typedef int (*fOpen)(int *);
typedef int (*fSetBuf)(int, const void *, int);
typedef int (*fGetRes)(int, int *, int *, int *);
typedef int (*fSetRes)(int, int, int);
typedef int (*fGetName)(int, char *);
typedef int (*fSetFmt)(int, int, int, int, int, int);
typedef int (*fDecomp)(int, void *, int, int, int, int, int);
typedef void (*fClose)(int);
typedef int (*fTest)(const void *, int);

void __stdcall mainCRTStartup(void) {
  parse();
  if (argc_ < 3) { out("usage\n"); ExitProcess(1); }
  HANDLE dll = LoadLibraryA("DECO_32.DLL");
  if (!dll) { out("no dll\n"); ExitProcess(3); }
  fOpen Open = (fOpen)GetProcAddress(dll, "OpenDecompressor");
  fSetBuf SetFIF = (fSetBuf)GetProcAddress(dll, "SetFIFBuffer");
  fSetBuf SetFTT = (fSetBuf)GetProcAddress(dll, "SetFTTBuffer");
  fGetRes GetRes = (fGetRes)GetProcAddress(dll, "GetOriginalResolution");
  fSetRes SetRes = (fSetRes)GetProcAddress(dll, "SetOutputResolution");
  fGetName GetName = (fGetName)GetProcAddress(dll, "GetFIFFTTFileName");
  fSetFmt SetFmt = (fSetFmt)GetProcAddress(dll, "SetOutputFormat");
  fDecomp Decomp = (fDecomp)GetProcAddress(dll, "DecompressToBuffer");
  fClose Close = (fClose)GetProcAddress(dll, "CloseDecompressor");
  u32 len; u8 *data = slurp(argv_[1], &len);
  if (!data) { out("cannot read\n"); ExitProcess(2); }
  int hd = 0, rc = Open(&hd);
  rc = SetFIF(hd, data, (int)len);
  out("SetFIFBuffer="); outnum(rc); out("\n");
  if (rc) ExitProcess(4);
  char name[300]; for (int i = 0; i < 300; i++) name[i] = 0;
  if (GetName) { GetName(hd, name); if (name[0]) { out("ftt="); out(name); out("\n"); } }
  if (argc_ > 3 && argv_[3][0] != '-') {
    u32 fl; u8 *ftt = slurp(argv_[3], &fl);
    if (ftt) { rc = SetFTT(hd, ftt, (int)fl); out("SetFTTBuffer="); outnum(rc); out("\n"); }
  }
  int w = 0, h = 0, extra = 0;
  rc = GetRes(hd, &w, &h, &extra);
  out("orig="); outnum(w); out("x"); outnum(h); out(" extra="); outnum(extra); out(" rc="); outnum(rc); out("\n");
  if (argc_ > 5 && atoi_(argv_[4]) > 0) { w = atoi_(argv_[4]); h = atoi_(argv_[5]); }
  rc = SetRes(hd, w, h);
  out("SetOutputResolution="); outnum(rc); out("\n");
  rc = SetFmt(hd, 1, 2, 3, 4, 0);
  out("SetOutputFormat="); outnum(rc); out("\n");
  {
    typedef int (*fGetB)(int, char *); typedef int (*fSetI)(int, int);
    char f = 0, d = 0;
    ((fGetB)GetProcAddress(dll, "GetOutputFilter"))(hd, &f);
    ((fGetB)GetProcAddress(dll, "GetOutputDither"))(hd, &d);
    out("filter="); outnum(f); out(" dither="); outnum(d); out("\n");
    if (argc_ > 6 && argv_[6][0] != '-') ((fSetI)GetProcAddress(dll, "SetOutputFilter"))(hd, atoi_(argv_[6]));
    if (argc_ > 7 && argv_[7][0] != '-') ((fSetI)GetProcAddress(dll, "SetOutputDither"))(hd, atoi_(argv_[7]));
  }
  u32 stride = w * 3;
  u8 *px = alloc(8 + stride * h);
  ((u32 *)px)[0] = w; ((u32 *)px)[1] = h;
  rc = Decomp(hd, px + 8, 0, 0, w, h, (int)stride);
  out("DecompressToBuffer="); outnum(rc); out("\n");
  if (rc == 0) spit(argv_[2], px, 8 + stride * h);
  Close(hd);
  ExitProcess(rc ? 5 : 0);
}
