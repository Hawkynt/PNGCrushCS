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
/// Both implementations read the screen at the window's client rectangle rather than addressing the
/// window itself, because the only handle the toolkit exposes is the screen position of a control's
/// client origin. The window therefore has to be unobscured, which it is on a machine running nothing
/// else — a capture session, or a headless X server with a single client.
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
  // Windows — GDI blit out of the screen device context.
  // ============================================================================================

  private const int _SM_CXSCREEN = 0;
  private const int _SM_CYSCREEN = 1;
  private const int _SRCCOPY = 0x00CC0020;
  private const int _CAPTUREBLT = 0x40000000;
  private const int _BI_RGB = 0;
  private const int _DIB_RGB_COLORS = 0;

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

  [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
  [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
  [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
  [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
  [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);
  [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, int usage, out IntPtr bits, IntPtr section, int offset);
  [DllImport("gdi32.dll")] private static extern bool GdiFlush();

  private static RawImage? _CaptureWindows(int x, int y, int width, int height) {
    var screen = GetDC(IntPtr.Zero);
    if (screen == IntPtr.Zero)
      return null;

    var memory = IntPtr.Zero;
    var bitmap = IntPtr.Zero;
    var previous = IntPtr.Zero;
    try {
      memory = CreateCompatibleDC(screen);
      if (memory == IntPtr.Zero)
        return null;

      // A negative height asks GDI for a top-down bitmap, which is the row order the writers want.
      var header = new BitmapInfoHeader {
        Size = Marshal.SizeOf<BitmapInfoHeader>(),
        Width = width,
        Height = -height,
        Planes = 1,
        BitCount = 32,
        Compression = _BI_RGB,
      };

      bitmap = CreateDIBSection(screen, ref header, _DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
      if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        return null;

      previous = SelectObject(memory, bitmap);
      if (!BitBlt(memory, 0, 0, width, height, screen, x, y, _SRCCOPY | _CAPTUREBLT))
        return null;

      // GDI batches its drawing, and a DIB section's memory is only guaranteed to hold the result
      // once the batch has been flushed. Reading it without this can return the bitmap as created.
      GdiFlush();

      var pixels = new byte[width * height * 4];
      Marshal.Copy(bits, pixels, 0, pixels.Length);

      // A screen blit carries no alpha; the writers would otherwise see a fully transparent picture.
      for (var i = 3; i < pixels.Length; i += 4)
        pixels[i] = 255;

      return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
    } finally {
      if (previous != IntPtr.Zero) SelectObject(memory, previous);
      if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
      if (memory != IntPtr.Zero) DeleteDC(memory);
      ReleaseDC(IntPtr.Zero, screen);
    }
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
