using System;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Codecs;
using Hawkynt.ColorProcessing.Metrics;
using Hawkynt.ColorProcessing.Storage;
using Hawkynt.ColorProcessing.Working;

namespace Hawkynt.ColorProcessing.Adapter;

/// <summary>
/// Runs one of the colour library's ditherers over a <see cref="RawImage"/>.
/// </summary>
/// <remarks>
/// The colour processing was never tied to Windows: its ditherers work on raw pointers and know
/// nothing about bitmaps. What tied it was the one adapter that locked a <c>Bitmap</c> to get those
/// pointers. This is that adapter over a byte array instead.
/// <para/>
/// A ditherer is a value type under a generic constraint rather than an interface, so the call site
/// is specialised and the inner loop carries no dispatch at all. That is where the speed comes
/// from, and it is also what makes this trimmable and ahead-of-time compilable: the pair of types
/// is fixed when the caller names them, so nothing has to be conjured at runtime.
/// <para/>
/// The palette is the caller's. The library's own quantizers reach their palette through an
/// internal member, so they cannot be driven from outside the assembly — see
/// <see cref="RawImageQuantization"/>'s remarks in the project's notes. Everything else works.
/// </remarks>
public static class RawImageQuantization {

  /// <summary>Bytes one pixel occupies in the layout the colour processing reads.</summary>
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>
  /// Maps every pixel to the nearest entry of <paramref name="palette"/>, spreading what each
  /// could not represent according to <typeparamref name="TDitherer"/>.
  /// </summary>
  /// <param name="palette">RGB triplets, at most 256 of them.</param>
  /// <remarks>
  /// The working space is linear RGB with alpha. Matching in the space a file happens to store its
  /// colours in makes the result depend on the file rather than on the picture; matching in a
  /// linear space means two pictures that look alike come out alike.
  /// </remarks>
  public static RawImage Dither<TDitherer>(RawImage image, ReadOnlySpan<byte> palette, TDitherer ditherer)
    where TDitherer : struct, IDitherer {
    ArgumentNullException.ThrowIfNull(image);

    var colors = palette.Length / 3;
    if (colors is < 2 or > 256)
      throw new ArgumentOutOfRangeException(nameof(palette), colors, "A palette holds 2 to 256 colours.");

    var source = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var width = image.Width;
    var height = image.Height;

    var decoder = default(Srgb32ToLinearRgbaF);
    var metric = default(EuclideanSquared4F<LinearRgbaF>);
    var working = _ToWorking(palette, colors, ref decoder);
    var indices = new byte[width * height];

    unsafe {
      fixed (byte* pixels = source.PixelData)
      fixed (byte* target = indices)
        // The source pointer is typed, so its stride counts pixels; the index pointer is bytes and
        // counts bytes. They are the same number here only because an index is one byte.
        ditherer.Dither<LinearRgbaF, Bgra8888, Srgb32ToLinearRgbaF, EuclideanSquared4F<LinearRgbaF>>(
          (Bgra8888*)pixels, target, width, height, width, width, 0,
          ref decoder, ref metric, working);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = palette.ToArray(),
      PaletteCount = colors,
    };
  }

  /// <summary>Brings a palette into the space the ditherer measures distance in.</summary>
  private static LinearRgbaF[] _ToWorking(ReadOnlySpan<byte> palette, int colors, ref Srgb32ToLinearRgbaF decoder) {
    var working = new LinearRgbaF[colors];

    for (var i = 0; i < colors; ++i) {
      // The storage layout is blue, green, red, alpha; a RawImage palette is red, green, blue.
      var packed = (uint)(palette[i * 3 + 2] | (palette[i * 3 + 1] << 8) | (palette[i * 3] << 16) | (0xFFu << 24));
      var pixel = _ToStorage(packed);
      working[i] = decoder.Decode(ref pixel);
    }

    return working;
  }

  private static Bgra8888 _ToStorage(uint packed) {
    unsafe {
      return *(Bgra8888*)&packed;
    }
  }
}
