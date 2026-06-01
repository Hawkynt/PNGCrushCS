using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FileFormat.Core;

namespace Crush.Viewer;

/// <summary>
/// Applies a <see cref="DisplayFilter"/> post-decode to a Bitmap so the viewer can render formats
/// the way they'd be seen on their target hardware (NES via composite NTSC, etc.).
/// </summary>
/// <remarks>
/// Currently implements <see cref="DisplayFilter.None"/> (passthrough) and a lightweight
/// <see cref="DisplayFilter.NtscComposite"/> (3-tap horizontal blur + small chroma shift).
/// <see cref="DisplayFilter.NtscSvideo"/> and <see cref="DisplayFilter.Pal"/> are reserved
/// for future kernels; today they pass the source through unchanged.
/// <para/>
/// All methods are pure: input bitmap is not modified; a new bitmap is returned when a filter
/// is applied. Caller is responsible for disposing the returned bitmap when different from input.
/// </remarks>
internal static class DisplayFilterPipeline {

  /// <summary>Applies the filter and returns a new <see cref="Bitmap"/>. When the filter is <see cref="DisplayFilter.None"/>
  /// (or not yet implemented), returns <paramref name="source"/> unchanged — caller must check reference equality
  /// before disposing.</summary>
  internal static Bitmap Apply(Bitmap source, DisplayFilter filter) {
    if (source == null) throw new ArgumentNullException(nameof(source));
    return filter switch {
      DisplayFilter.NtscComposite => _NtscComposite(source),
      _ => source,
    };
  }

  /// <summary>3-tap horizontal box blur + 1-pixel chroma shift between R and B channels.
  /// Cheap approximation of composite-video bleed; good enough to make NES tiles look right.</summary>
  private static Bitmap _NtscComposite(Bitmap source) {
    var w = source.Width;
    var h = source.Height;
    var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    var srcRect = new Rectangle(0, 0, w, h);
    var srcData = source.LockBits(srcRect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    var dstData = result.LockBits(srcRect, ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    try {
      var pixels = w * h;
      var stride = w * 4;
      var src = new byte[pixels * 4];
      var dst = new byte[pixels * 4];
      Marshal.Copy(srcData.Scan0, src, 0, src.Length);

      for (var y = 0; y < h; ++y) {
        var row = y * stride;
        for (var x = 0; x < w; ++x) {
          var i = row + x * 4;
          // 3-tap horizontal blur: (prev + 2*current + next) / 4
          var i_prev = (x > 0)     ? row + (x - 1) * 4 : i;
          var i_next = (x < w - 1) ? row + (x + 1) * 4 : i;
          var b = (src[i_prev] + 2 * src[i] + src[i_next]) >> 2;
          var g = (src[i_prev + 1] + 2 * src[i + 1] + src[i_next + 1]) >> 2;
          var r = (src[i_prev + 2] + 2 * src[i + 2] + src[i_next + 2]) >> 2;
          // Small chroma shift: bias R 1 px right, B 1 px left, leave G centred — emulates Y/C separation artefact.
          var r_shifted = (x < w - 1) ? src[row + (x + 1) * 4 + 2] : src[i + 2];
          var b_shifted = (x > 0)     ? src[row + (x - 1) * 4]     : src[i];
          dst[i]     = (byte)((b + b_shifted) >> 1);
          dst[i + 1] = (byte)g;
          dst[i + 2] = (byte)((r + r_shifted) >> 1);
          dst[i + 3] = src[i + 3];
        }
      }

      Marshal.Copy(dst, 0, dstData.Scan0, dst.Length);
    } finally {
      source.UnlockBits(srcData);
      result.UnlockBits(dstData);
    }
    return result;
  }
}
