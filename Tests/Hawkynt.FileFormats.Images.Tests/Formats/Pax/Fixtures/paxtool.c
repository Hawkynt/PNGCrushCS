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
__declspec(dllimport) void *WINAPI GlobalLock(HANDLE);
__declspec(dllimport) int WINAPI GlobalUnlock(HANDLE);
__declspec(dllimport) HANDLE WINAPI GlobalFree(HANDLE);
__declspec(dllimport) u32 WINAPI GlobalSize(HANDLE);
__declspec(dllimport) HANDLE WINAPI GetStdHandle(u32);
__declspec(dllimport) void *WINAPI VirtualAlloc(void *, u32, u32, u32);
__declspec(dllimport) HANDLE WINAPI LoadLibraryA(const char *);
__declspec(dllimport) void *WINAPI GetProcAddress(HANDLE, const char *);

static void out(const char *s) { u32 n = 0, w; while (s[n]) n++; WriteFile(GetStdHandle((u32)-11), s, n, &w, 0); }
static void outnum(u32 v) { char b[16]; int i = 15; b[i] = 0; if (!v) b[--i] = '0'; while (v) { b[--i] = '0' + v % 10; v /= 10; } out(b + i); }
static void *alloc(u32 n) { return VirtualAlloc(0, n + 16, 0x3000, 4); }
static u8 *slurp(const char *p, u32 *len) {
  HANDLE h = CreateFileA(p, 0x80000000, 1, 0, 3, 0x80, 0);
  if (h == (HANDLE)-1) { out("cannot open input\n"); ExitProcess(2); }
  u32 n = GetFileSize(h, 0), r; u8 *b = alloc(n); ReadFile(h, b, n, &r, 0); CloseHandle(h); *len = n; return b;
}
static void spit(const char *p, const void *d, u32 n) {
  HANDLE h = CreateFileA(p, 0x40000000, 0, 0, 2, 0x80, 0); u32 w;
  if (h == (HANDLE)-1) { out("cannot open output\n"); ExitProcess(2); }
  WriteFile(h, d, n, &w, 0); CloseHandle(h);
}
static u32 atou(const char *s) { u32 v = 0; while (*s >= '0' && *s <= '9') v = v * 10 + (*s++ - '0'); return v; }

static char *argv_[16]; static int argc_;
static void parse(void) {
  char *c = GetCommandLineA();
  static char buf[4096]; int i = 0;
  while (*c && argc_ < 16) {
    while (*c == ' ') c++;
    if (!*c) break;
    argv_[argc_++] = buf + i;
    if (*c == '"') { c++; while (*c && *c != '"') buf[i++] = *c++; if (*c) c++; }
    else while (*c && *c != ' ') buf[i++] = *c++;
    buf[i++] = 0;
  }
}

typedef void (WINAPI *fInit)(const char *);
typedef void *(WINAPI *fOpenSrc)(const char *);
typedef void *(WINAPI *fOpenDst)(const char *, int);
typedef u32 (WINAPI *fClose)(void *);
typedef HANDLE (WINAPI *fReadPAX)(void *, u32 *, u32 *, u32, u8 *, const char *, u32);
typedef int (WINAPI *fDims)(void *, u32 *, u32 *, u32 *, const char *, u32);
typedef u32 (WINAPI *fWritePAX)(void *, u8 *, u32, u32, u32, const char *, u32, u32, u8 *, u32);
typedef int (WINAPI *fSetInfo)(const char *, const char *, u8 *);
typedef u32 (WINAPI *fErr)(void);
typedef int (WINAPI *fPaxToRgb)(const char *, const char *, u16 *, u16 *, HANDLE *, u8 *);
typedef int (WINAPI *fRgbToPax)(const char *, const char *, u16, u16, u16, u8 *, u8 *);

void __stdcall mainCRTStartup(void) {
  parse();
  if (argc_ < 2) { out("usage\n"); ExitProcess(1); }
  char cmd = argv_[1][0];
  if (cmd == 'p' || cmd == 'q') {
    HANDLE dll = LoadLibraryA("PAXIO.dll");
    if (!dll) { out("no PAXIO\n"); ExitProcess(3); }
    if (cmd == 'p') { /* p <pax> <password> <out> */
      fPaxToRgb f = (fPaxToRgb)GetProcAddress(dll, "PA_PAXToRGB");
      if (!f) f = (fPaxToRgb)GetProcAddress(dll, "_PA_PAXToRGB@24");
      u16 w = 0, h = 0; HANDLE g = 0; u8 id[128];
      int rc = f(argv_[2], argv_[3], &w, &h, &g, id);
      out("rc="); outnum(rc); out(" w="); outnum(w); out(" h="); outnum(h); out("\n");
      if (rc == 0 && g) {
        u32 n = (u32)w * h * 3; u8 *b = alloc(n + 8); u8 *src = GlobalLock(g);
        ((u32 *)b)[0] = w; ((u32 *)b)[1] = h;
        for (u32 i = 0; i < n; i++) b[8 + i] = src[i];
        spit(argv_[4], b, n + 8);
      }
      ExitProcess(rc);
    } else { /* q <raw> <pax> <password> <enctype> */
      fRgbToPax f = (fRgbToPax)GetProcAddress(dll, "PA_RGBToPAX");
      if (!f) f = (fRgbToPax)GetProcAddress(dll, "_PA_RGBToPAX@28");
      u32 len; u8 *raw = slurp(argv_[2], &len); u8 id[128];
      for (int i = 0; i < 128; i++) id[i] = (u8)i;
      int rc = f(argv_[3], argv_[4], (u16)atou(argv_[5]), (u16)((u32 *)raw)[0], (u16)((u32 *)raw)[1], raw + 8, id);
      out("rc="); outnum(rc); out("\n");
      ExitProcess(rc);
    }
  }
  HANDLE dll = LoadLibraryA("_ISource50.dll");
  if (!dll) { out("no ISource\n"); ExitProcess(3); }
  ((fInit)GetProcAddress(dll, "_is5_Initialize"))("none");
  fErr lastErr = (fErr)GetProcAddress(dll, "_is5_GetLastError");
  if (cmd == 'r') { /* r <pax> <password> <bpp> <out> */
    void *src = ((fOpenSrc)GetProcAddress(dll, "_is5_OpenFileSource"))(argv_[2]);
    u32 w = 0, h = 0, bpp = atou(argv_[4]); u8 pal[1024];
    for (int i = 0; i < 1024; i++) pal[i] = 0;
    HANDLE g = ((fReadPAX)GetProcAddress(dll, "_is5_ReadPAX"))(src, &w, &h, bpp, pal, argv_[3], 0);
    ((fClose)GetProcAddress(dll, "_is5_CloseSource"))(src);
    out("w="); outnum(w); out(" h="); outnum(h); out(" err="); outnum(lastErr()); out("\n");
    if (!g) ExitProcess(4);
    u32 n = w * h * (bpp / 8); u8 *s = GlobalLock(g); if (!s) s = (u8 *)g;
    u8 *b = alloc(n + 12 + 1024);
    ((u32 *)b)[0] = w; ((u32 *)b)[1] = h; ((u32 *)b)[2] = bpp;
    for (int i = 0; i < 1024; i++) b[12 + i] = pal[i];
    for (u32 i = 0; i < n; i++) b[1036 + i] = s[i];
    spit(argv_[5], b, 1036 + n);
    ExitProcess(0);
  }
  if (cmd == 'd') { /* d <pax> <password> */
    void *src = ((fOpenSrc)GetProcAddress(dll, "_is5_OpenFileSource"))(argv_[2]);
    u32 w = 0, h = 0, d = 0;
    int ok = ((fDims)GetProcAddress(dll, "_is5_GetPAXDims"))(src, &w, &h, &d, argv_[3], 0);
    out("ok="); outnum(ok); out(" w="); outnum(w); out(" h="); outnum(h); out(" bpp="); outnum(d); out(" err="); outnum(lastErr()); out("\n");
    ExitProcess(ok ? 0 : 4);
  }
  if (cmd == 'w') { /* w <raw> <pax> <password> <enctype> <bpp> : raw = w,h,pal[1024],pixels */
    u32 len; u8 *raw = slurp(argv_[2], &len);
    u32 w = ((u32 *)raw)[0], h = ((u32 *)raw)[1], bpp = atou(argv_[6]);
    u8 id[128]; for (int i = 0; i < 128; i++) id[i] = (u8)(i * 7);
    ((fSetInfo)GetProcAddress(dll, "_is5_SetPAXOutputInfo"))("TestApp", "TestAuthor", id);
    if (argc_ > 7) { const char *t = argv_[7]; u32 n = 0; while (t[n]) n++;
      ((int (WINAPI *)(const char *, const u8 *, u32))GetProcAddress(dll, "_is5_AddPAXOutputTag"))("TAG1", (const u8 *)t, n);
      ((int (WINAPI *)(const char *, const u8 *, u32))GetProcAddress(dll, "_is5_AddPAXOutputTag"))("ZZZZ", (const u8 *)"0123456789abcdef", 16); }
    void *dst = ((fOpenDst)GetProcAddress(dll, "_is5_OpenFileDest"))(argv_[3], 0);
    u32 stride = bpp == 8 ? w : w * 3;
    u32 n = ((fWritePAX)GetProcAddress(dll, "_is5_WritePAX"))(dst, raw + 8 + 1024, w, h, stride, argv_[4], atou(argv_[5]), bpp, raw + 8, 0);
    ((fClose)GetProcAddress(dll, "_is5_CloseDest"))(dst);
    out("size="); outnum(n); out(" err="); outnum(lastErr()); out("\n");
    ExitProcess(n ? 0 : 4);
  }
  out("unknown command\n");
  ExitProcess(1);
}
