using System;
using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>Platform-neutral display filtering over BGRA pixels.</summary>
internal static class DisplayFilterPipeline {

  internal static RawImage Apply(RawImage source, DisplayFilter filter)
    => filter switch {
      DisplayFilter.NtscComposite => _NtscComposite(source),
      DisplayFilter.NtscSvideo => _NtscSvideo(source),
      DisplayFilter.Pal => _Pal(source),
      _ => source,
    };

  private static RawImage _NtscComposite(RawImage source) {
    var src = source.ToBgra32();
    var w = source.Width;
    var h = source.Height;
    var dst = new byte[src.Length];
    var stride = w * 4;

    for (var y = 0; y < h; ++y) {
      var row = y * stride;
      for (var x = 0; x < w; ++x) {
        var i = row + x * 4;
        var previous = x > 0 ? row + (x - 1) * 4 : i;
        var next = x < w - 1 ? row + (x + 1) * 4 : i;
        var b = (src[previous] + 2 * src[i] + src[next]) >> 2;
        var g = (src[previous + 1] + 2 * src[i + 1] + src[next + 1]) >> 2;
        var r = (src[previous + 2] + 2 * src[i + 2] + src[next + 2]) >> 2;
        var shiftedR = x < w - 1 ? src[next + 2] : src[i + 2];
        var shiftedB = x > 0 ? src[previous] : src[i];
        dst[i] = (byte)((b + shiftedB) >> 1);
        dst[i + 1] = (byte)g;
        dst[i + 2] = (byte)((r + shiftedR) >> 1);
        dst[i + 3] = src[i + 3];
      }
    }

    return _WithPixels(source, dst);
  }

  private static RawImage _NtscSvideo(RawImage source) {
    var src = source.ToBgra32();
    var w = source.Width;
    var h = source.Height;
    var dst = new byte[src.Length];
    var stride = w * 4;

    for (var y = 0; y < h; ++y) {
      var row = y * stride;
      for (var x = 0; x < w; ++x) {
        var i = row + x * 4;
        var previous = x > 0 ? row + (x - 1) * 4 : i;
        var next = x < w - 1 ? row + (x + 1) * 4 : i;
        dst[i] = (byte)((src[previous] + 2 * src[i] + src[next]) >> 2);
        dst[i + 1] = src[i + 1];
        dst[i + 2] = (byte)((src[previous + 2] + 2 * src[i + 2] + src[next + 2]) >> 2);
        dst[i + 3] = src[i + 3];
      }
    }

    return _WithPixels(source, dst);
  }

  private static RawImage _Pal(RawImage source) {
    var src = source.ToBgra32();
    var w = source.Width;
    var h = source.Height;
    var dst = new byte[src.Length];
    var stride = w * 4;

    for (var y = 0; y < h; ++y) {
      var row = y * stride;
      var chromaSign = (y & 1) == 0 ? 1 : -1;
      for (var x = 0; x < w; ++x) {
        var i = row + x * 4;
        var previous = x > 0 ? row + (x - 1) * 4 : i;
        var next = x < w - 1 ? row + (x + 1) * 4 : i;
        var r = (src[previous + 2] + 2 * src[i + 2] + src[next + 2]) >> 2;
        var b = (src[previous] + 2 * src[i] + src[next]) >> 2;
        dst[i] = (byte)Math.Clamp(b + chromaSign * 3, 0, 255);
        dst[i + 1] = src[i + 1];
        dst[i + 2] = (byte)Math.Clamp(r - chromaSign * 3, 0, 255);
        dst[i + 3] = src[i + 3];
      }
    }

    return _WithPixels(source, dst);
  }

  private static RawImage _WithPixels(RawImage source, byte[] pixels) => new() {
    Width = source.Width,
    Height = source.Height,
    Format = FileFormat.Core.PixelFormat.Bgra32,
    PixelData = pixels,
    Metadata = source.Metadata,
    ColorInfo = source.ColorInfo,
  };
}
