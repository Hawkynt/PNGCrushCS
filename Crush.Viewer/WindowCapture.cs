using System;
using System.Drawing;
using System.Runtime.InteropServices;
using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>Reads back the pixels a window is currently showing.</summary>
/// <remarks>
/// <para>
/// The toolkit has no capture of its own: its controls draw through an <c>IGraphics</c> the backend
/// owns, the painting entry points are protected, and the primitives it offers cannot rasterise text,
/// so there is no way to re-render a window into a buffer from the outside. What is left is asking
/// the platform for the pixels already on screen, which is what this does — a real read of the real
/// window, not a reconstruction of one.
/// </para>
/// <para>
/// The toolkit exposes no window handle — only the screen position of a control's client origin —
/// so the window has to be found from outside it. On Windows it is, by asking the platform which
/// top-level windows this process owns, and the capture is then the window drawing itself into a
/// bitmap: a window that is behind something else still yields its own pixels. On X11 there is no
/// equivalent, so the capture reads the screen at the client rectangle, and the window has to be
/// unobscured — which it is on a machine running nothing else, or a headless server with a single
/// client. Wherever the screen is read, the rectangle is checked to be this process's window first,
/// because a screen read that lands on someone else's window looks exactly like a good one.
/// </para>
/// </remarks>
internal static class WindowCapture {

  /// <summary>Whether this platform can be asked for screen pixels at all.</summary>
  internal static bool IsSupported => OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && !_IsWayland);

  /// <summary>Names the reason <see cref="IsSupported"/> is false, for a message a user can act on.</summary>
  internal static string UnsupportedReason
    => OperatingSystem.IsLinux() && _IsWayland
      ? "This session is Wayland, where a client cannot read back the compositor's screen. Run under "
        + "an X server, or force the X11 backend with GDK_BACKEND=x11."
      : $"Capturing a window is implemented for Windows and X11 only; this is {_PlatformName()}.";

  /// <summary>
  /// Whether the toolkit will put its windows on a Wayland compositor rather than an X server.
  /// </summary>
  /// <remarks>
  /// This matters more than it looks: with a Wayland session in the environment, GTK uses it even
  /// when DISPLAY names a perfectly good X server, so an X capture then reads an empty screen and
  /// writes a black picture that looks like a working screenshot. Refusing outright is the only
  /// honest answer, and it matches how GTK itself decides.
  /// </remarks>
  private static bool _IsWayland
    => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
      && !string.Equals(Environment.GetEnvironmentVariable("GDK_BACKEND"), "x11", StringComparison.OrdinalIgnoreCase);

  private static string _PlatformName()
    => OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsFreeBSD() ? "FreeBSD" : "an unrecognised platform";

  /// <summary>Whether a capture came back as one flat colour, which means it caught nothing.</summary>
  internal static bool IsBlank(RawImage image) {
    var pixels = image.PixelData;
    if (pixels.Length < 8)
      return true;

    for (var i = 4; i < pixels.Length; i += 4)
      if (pixels[i] != pixels[0] || pixels[i + 1] != pixels[1] || pixels[i + 2] != pixels[2])
        return false;

    return true;
  }

  /// <summary>The size of the screen the capture reads from, or an empty size when unknown.</summary>
  internal static Size ScreenSize {
    get {
      if (OperatingSystem.IsWindows())
        return new(GetSystemMetrics(_SM_CXSCREEN), GetSystemMetrics(_SM_CYSCREEN));

      if (!OperatingSystem.IsLinux() || _IsWayland)
        return Size.Empty;

      var display = XOpenDisplay(IntPtr.Zero);
      if (display == IntPtr.Zero)
        return Size.Empty;

      try {
        var screen = XDefaultScreen(display);
        return new(XDisplayWidth(display, screen), XDisplayHeight(display, screen));
      } finally {
        XCloseDisplay(display);
      }
    }
  }

  /// <summary>Reads a screen rectangle, or returns <c>null</c> when the platform refuses.</summary>
  /// <remarks>
  /// Asking for pixels outside the screen is an error on both platforms rather than a short read, so
  /// the rectangle is trimmed to what is actually there. A window larger than the screen — a 1024x768
  /// capture session is not unusual — then yields the part of it that exists instead of nothing.
  /// </remarks>
  internal static RawImage? Capture(int screenX, int screenY, int width, int height) {
    var screen = ScreenSize;
    if (screen.Width > 0 && screen.Height > 0) {
      var left = Math.Clamp(screenX, 0, screen.Width - 1);
      var top = Math.Clamp(screenY, 0, screen.Height - 1);
      width = Math.Min(width - (left - screenX), screen.Width - left);
      height = Math.Min(height - (top - screenY), screen.Height - top);
      screenX = left;
      screenY = top;
    }

    if (width < 1 || height < 1)
      return null;

    if (OperatingSystem.IsWindows())
      return _CaptureWindows(screenX, screenY, width, height);

    return OperatingSystem.IsLinux() ? _CaptureX11(screenX, screenY, width, height) : null;
  }

  // ============================================================================================
  // Windows — ask the window to draw itself, and read the screen only if it will not.
  // ============================================================================================

  private const int _SM_CXSCREEN = 0;
  private const int _SM_CYSCREEN = 1;
  private const int _SRCCOPY = 0x00CC0020;
  private const int _CAPTUREBLT = 0x40000000;
  private const int _BI_RGB = 0;
  private const int _DIB_RGB_COLORS = 0;

  /// <summary>Renders only the client area, which is what the capture is of.</summary>
  private const int _PW_CLIENTONLY = 1;

  /// <summary>Includes what the compositor draws, which plain PrintWindow leaves blank.</summary>
  private const int _PW_RENDERFULLCONTENT = 2;

  /// <summary>How far inside the rectangle the ownership samples sit.</summary>
  private const int _OWNERSHIP_INSET = 4;

  [StructLayout(LayoutKind.Sequential)]
  private struct BitmapInfoHeader {
    public int Size;
    public int Width;
    public int Height;
    public short Planes;
    public short BitCount;
    public int Compression;
    public int SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public int ClrUsed;
    public int ClrImportant;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct WindowPoint {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct WindowRect {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

  [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
  [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(WindowPoint point);
  [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);
  [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
  [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out WindowRect rect);
  [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, int flags);
  [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
  [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
  [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);
  [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, int usage, out IntPtr bits, IntPtr section, int offset);
  [DllImport("gdi32.dll")] private static extern bool GdiFlush();

  /// <summary>
  /// Asks the window for its own pixels, and only reads the screen when it will not give them.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Reading the screen was the whole of this, and it is wrong in a way no check on the pixels can
  /// see: a blit copies whatever is in front of those coordinates, so on a desktop with anything
  /// else open it writes a picture of another application's window and calls it a screenshot. It
  /// also needs the rectangle to be right, and the one the toolkit reports is in its own units —
  /// on a scaled display it does not line up with the physical pixels a blit addresses.
  /// </para>
  /// <para>
  /// <c>PrintWindow</c> has neither problem: it asks the window to render into a device context, so
  /// what comes back is that window whether or not it is in front, at the size its client area
  /// actually is. The screen blit stays as the fallback for a window that refuses to draw itself,
  /// and there it is guarded by an ownership check rather than trusted.
  /// </para>
  /// </remarks>
  private static RawImage? _CaptureWindows(int x, int y, int width, int height) {
    var own = _OwnTopLevelWindow();
    if (own != IntPtr.Zero && _RenderWindow(own) is { } rendered && !IsBlank(rendered))
      return rendered;

    if (!_IsOwnWindowAt(x, y, width, height))
      return null;

    var screen = GetDC(IntPtr.Zero);
    if (screen == IntPtr.Zero)
      return null;

    try {
      return _IntoDibSection(width, height, dc => BitBlt(dc, 0, 0, width, height, screen, x, y, _SRCCOPY | _CAPTUREBLT));
    } finally {
      ReleaseDC(IntPtr.Zero, screen);
    }
  }

  /// <summary>The largest visible top-level window this process owns, or zero when it has none.</summary>
  /// <remarks>
  /// The toolkit exposes no window handle, so the window has to be found from the outside. Largest
  /// rather than first: a process owns several top-level windows it never shows anyone — the
  /// message-only, tooltip and input-method helpers a toolkit creates — and the one worth
  /// photographing is the one with a client area to photograph.
  /// </remarks>
  private static IntPtr _OwnTopLevelWindow() {
    var self = Environment.ProcessId;
    var best = IntPtr.Zero;
    var bestArea = 0L;

    EnumWindows((window, _) => {
      if (!IsWindowVisible(window))
        return true;

      GetWindowThreadProcessId(window, out var owner);
      if (owner != self || !GetClientRect(window, out var client))
        return true;

      var area = (long)client.Right * client.Bottom;
      if (area <= bestArea)
        return true;

      bestArea = area;
      best = window;
      return true;
    }, IntPtr.Zero);

    return best;
  }

  /// <summary>Has a window draw its client area into a bitmap, or answers null when it will not.</summary>
  private static RawImage? _RenderWindow(IntPtr window) {
    if (!GetClientRect(window, out var client))
      return null;

    var width = client.Right - client.Left;
    var height = client.Bottom - client.Top;
    return width < 1 || height < 1
      ? null
      : _IntoDibSection(width, height, dc => PrintWindow(window, dc, _PW_CLIENTONLY | _PW_RENDERFULLCONTENT));
  }

  /// <summary>Runs a drawing operation into a top-down 32-bit bitmap and hands back its pixels.</summary>
  /// <remarks>
  /// A negative height asks GDI for a top-down bitmap, which is the row order the writers want, so
  /// nothing has to be flipped afterwards.
  /// </remarks>
  private static RawImage? _IntoDibSection(int width, int height, Func<IntPtr, bool> draw) {
    var reference = GetDC(IntPtr.Zero);
    if (reference == IntPtr.Zero)
      return null;

    var memory = IntPtr.Zero;
    var bitmap = IntPtr.Zero;
    var previous = IntPtr.Zero;
    try {
      memory = CreateCompatibleDC(reference);
      if (memory == IntPtr.Zero)
        return null;

      var header = new BitmapInfoHeader {
        Size = Marshal.SizeOf<BitmapInfoHeader>(),
        Width = width,
        Height = -height,
        Planes = 1,
        BitCount = 32,
        Compression = _BI_RGB,
      };

      bitmap = CreateDIBSection(reference, ref header, _DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
      if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        return null;

      previous = SelectObject(memory, bitmap);
      if (!draw(memory))
        return null;

      // GDI batches its drawing, and a DIB section's memory is only guaranteed to hold the result
      // once the batch has been flushed. Reading it without this can return the bitmap as created.
      GdiFlush();

      var pixels = new byte[width * height * 4];
      Marshal.Copy(bits, pixels, 0, pixels.Length);

      // Neither a screen blit nor a window's own painting carries alpha; the writers would otherwise
      // see a fully transparent picture.
      for (var i = 3; i < pixels.Length; i += 4)
        pixels[i] = 255;

      return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
    } finally {
      if (previous != IntPtr.Zero) SelectObject(memory, previous);
      if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
      if (memory != IntPtr.Zero) DeleteDC(memory);
      ReleaseDC(IntPtr.Zero, reference);
    }
  }

  /// <summary>Whether the corners and the middle of a screen rectangle all belong to this process.</summary>
  /// <remarks>
  /// <para>
  /// Only the fallback needs this: a blit copies whatever is in front, and nothing about the pixels
  /// says whose window that was. Without it, a capture on a busy desktop is somebody else's window
  /// reported as a success — the silent wrong answer <see cref="IsBlank"/> exists to prevent,
  /// arriving by the one route it cannot see.
  /// </para>
  /// <para>
  /// The samples sit a few pixels inside the rectangle. Its last row and column are exactly where a
  /// rounding difference — a scaled display, a border counted on one side only — hit-tests to the
  /// neighbouring window instead of ours, and a few pixels in is still inside any corner worth the
  /// name. Our own dialogs pass, because the test is the owning process and not the owning window.
  /// </para>
  /// </remarks>
  private static bool _IsOwnWindowAt(int x, int y, int width, int height) {
    var inset = Math.Min(_OWNERSHIP_INSET, (Math.Min(width, height) - 1) / 2);
    var left = x + inset;
    var top = y + inset;
    var right = x + width - 1 - inset;
    var bottom = y + height - 1 - inset;
    ReadOnlySpan<WindowPoint> samples = [
      new() { X = left, Y = top },
      new() { X = right, Y = top },
      new() { X = left, Y = bottom },
      new() { X = right, Y = bottom },
      new() { X = x + width / 2, Y = y + height / 2 },
    ];

    var self = Environment.ProcessId;
    foreach (var sample in samples) {
      var window = WindowFromPoint(sample);
      if (window == IntPtr.Zero)
        return false;

      GetWindowThreadProcessId(window, out var owner);
      if (owner != self)
        return false;
    }

    return true;
  }

  // ============================================================================================
  // X11 — read the root window at the rectangle the client area occupies.
  // ============================================================================================

  private const int _ZPixmap = 2;

  [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
  [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
  [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr display);
  [DllImport("libX11.so.6")] private static extern IntPtr XRootWindow(IntPtr display, int screen);
  [DllImport("libX11.so.6")] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, UIntPtr planeMask, int format);
  [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
  [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
  [DllImport("libX11.so.6")] private static extern int XDisplayWidth(IntPtr display, int screen);
  [DllImport("libX11.so.6")] private static extern int XDisplayHeight(IntPtr display, int screen);

  // Field offsets into XImage on a 64-bit platform: width, height, xoffset and format are ints, then
  // the data pointer lands on the next 8-byte boundary and the remaining ints follow it.
  private const int _XImageDataOffset = 16;
  private const int _XImageByteOrderOffset = 24;
  private const int _XImageBytesPerLineOffset = 44;
  private const int _XImageBitsPerPixelOffset = 48;
  private const int _XImageRedMaskOffset = 56;
  private const int _XImageGreenMaskOffset = 64;
  private const int _XImageBlueMaskOffset = 72;
  private const int _MSBFirst = 1;

  /// <summary>Splits a channel mask into the shift that lands it at bit zero and its largest value.</summary>
  private static (int Shift, int Max) _MaskInfo(ulong mask) {
    if (mask == 0)
      return (0, 0);

    var shift = 0;
    while ((mask & 1) == 0) {
      mask >>= 1;
      ++shift;
    }

    return (shift, (int)mask);
  }

  /// <summary>Pulls one channel out of a pixel and widens it to eight bits.</summary>
  private static byte _Channel(uint pixel, int shift, int max)
    => (byte)((pixel >> shift & (uint)max) * 255 / (uint)max);

  private static RawImage? _CaptureX11(int x, int y, int width, int height) {
    var display = XOpenDisplay(IntPtr.Zero);
    if (display == IntPtr.Zero)
      return null;

    var image = IntPtr.Zero;
    try {
      XSync(display, false);
      var root = XRootWindow(display, XDefaultScreen(display));
      image = XGetImage(display, root, x, y, (uint)width, (uint)height, UIntPtr.MaxValue, _ZPixmap);
      if (image == IntPtr.Zero)
        return null;

      var bitsPerPixel = Marshal.ReadInt32(image, _XImageBitsPerPixelOffset);
      if (bitsPerPixel is not (24 or 32))
        return null;

      var bytesPerLine = Marshal.ReadInt32(image, _XImageBytesPerLineOffset);
      var data = Marshal.ReadIntPtr(image, _XImageDataOffset);
      if (data == IntPtr.Zero || bytesPerLine < 1)
        return null;

      // A pixel is a number the server assembles from its bytes in its own order, and which bits of
      // that number are red is the visual's business, not a convention. Reading the three masks is
      // the only way this is right on anything but the one arrangement that happens to be common.
      var msbFirst = Marshal.ReadInt32(image, _XImageByteOrderOffset) == _MSBFirst;
      var (redShift, redMax) = _MaskInfo((ulong)Marshal.ReadInt64(image, _XImageRedMaskOffset));
      var (greenShift, greenMax) = _MaskInfo((ulong)Marshal.ReadInt64(image, _XImageGreenMaskOffset));
      var (blueShift, blueMax) = _MaskInfo((ulong)Marshal.ReadInt64(image, _XImageBlueMaskOffset));
      if (redMax == 0 || greenMax == 0 || blueMax == 0)
        return null;

      var bytesPerPixel = bitsPerPixel / 8;
      var row = new byte[bytesPerLine];
      var pixels = new byte[width * height * 4];
      for (var line = 0; line < height; ++line) {
        Marshal.Copy(data + line * bytesPerLine, row, 0, bytesPerLine);
        var destination = line * width * 4;
        for (var column = 0; column < width; ++column) {
          var source = column * bytesPerPixel;
          var pixel = 0u;
          if (msbFirst)
            for (var b = 0; b < bytesPerPixel; ++b)
              pixel = pixel << 8 | row[source + b];
          else
            for (var b = bytesPerPixel - 1; b >= 0; --b)
              pixel = pixel << 8 | row[source + b];

          var o = destination + column * 4;
          pixels[o] = _Channel(pixel, blueShift, blueMax);
          pixels[o + 1] = _Channel(pixel, greenShift, greenMax);
          pixels[o + 2] = _Channel(pixel, redShift, redMax);
          pixels[o + 3] = 255;
        }
      }

      return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
    } finally {
      if (image != IntPtr.Zero) {
        // XDestroyImage is a macro over a function pointer in the struct and so has no symbol to
        // bind to. Releasing the two allocations it would release is the same thing.
        var data = Marshal.ReadIntPtr(image, _XImageDataOffset);
        if (data != IntPtr.Zero) XFree(data);
        XFree(image);
      }

      XCloseDisplay(display);
    }
  }
}
