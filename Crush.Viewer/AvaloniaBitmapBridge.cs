using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileFormat.Core;

namespace Crush.Viewer;

internal static class AvaloniaBitmapBridge {

  internal static WriteableBitmap ToBitmap(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);

    var pixels = source.ToBgra32();
    var sourceStride = checked(source.Width * 4);
    var bitmap = new WriteableBitmap(
      new PixelSize(source.Width, source.Height),
      new Vector(96, 96),
      Avalonia.Platform.PixelFormat.Bgra8888,
      AlphaFormat.Unpremul
    );

    using var framebuffer = bitmap.Lock();
    for (var y = 0; y < source.Height; ++y) {
      var destination = IntPtr.Add(framebuffer.Address, checked(y * framebuffer.RowBytes));
      Marshal.Copy(pixels, y * sourceStride, destination, sourceStride);
    }

    return bitmap;
  }
}
