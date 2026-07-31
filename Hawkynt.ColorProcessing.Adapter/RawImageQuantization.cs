using System;
using System.Collections.Generic;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Codecs;
using Hawkynt.ColorProcessing.Metrics;
using Hawkynt.ColorProcessing.Storage;
using Hawkynt.ColorProcessing.Working;

namespace Hawkynt.ColorProcessing.Adapter;

/// <summary>
/// Runs the colour library's quantizers and ditherers over a <see cref="RawImage"/>.
/// </summary>
/// <remarks>
/// The colour processing was never tied to Windows: it works on raw pointers and knows nothing
/// about bitmaps. What tied it was the one adapter that locked a <c>Bitmap</c> to get those
/// pointers. This is that adapter over a byte array instead.
/// <para/>
/// Both a quantizer and a ditherer are value types under generic constraints rather than
/// interfaces, so every call site is specialised and the inner loop carries no dispatch at all.
/// That is where the speed comes from, and it is also what makes this trimmable and
/// ahead-of-time compilable: naming the two types fixes the specialisation at build time, so
/// nothing has to be conjured at run time.
/// </remarks>
public static class RawImageQuantization {

  /// <summary>Bytes one pixel occupies in the layout the colour processing reads.</summary>
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>
  /// Reduces a picture to at most <paramref name="colors"/> colours, chosen by
  /// <typeparamref name="TQuantizer"/> and placed by <typeparamref name="TDitherer"/>.
  /// </summary>
  /// <remarks>
  /// The working space is linear RGB with alpha. Choosing and matching colours in the space a file
  /// happens to store them in makes the result depend on the file rather than on the picture;
  /// doing both in a linear space means two pictures that look alike come out alike.
  /// </remarks>
  public static RawImage Reduce<TQuantizer, TDitherer>(
    RawImage image, TQuantizer quantizer, TDitherer ditherer, int colors)
    where TQuantizer : struct, IQuantizer
    where TDitherer : struct, IDitherer {
    ArgumentNullException.ThrowIfNull(image);
    if (colors is < 2 or > 256)
      throw new ArgumentOutOfRangeException(nameof(colors), colors, "A palette holds 2 to 256 colours.");

    var source = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var decoder = default(Srgb32ToLinearRgbaF);
    var palette = quantizer.CreateKernel<LinearRgbaF>()
      .GeneratePalette(_Histogram(source.PixelData, ref decoder), colors);

    return _Place(source, palette, ditherer);
  }

  /// <summary>
  /// Maps every pixel to the nearest entry of a palette the caller already has.
  /// </summary>
  public static RawImage Dither<TDitherer>(RawImage image, ReadOnlySpan<byte> palette, TDitherer ditherer)
    where TDitherer : struct, IDitherer {
    ArgumentNullException.ThrowIfNull(image);

    var colors = palette.Length / 3;
    if (colors is < 2 or > 256)
      throw new ArgumentOutOfRangeException(nameof(palette), colors, "A palette holds 2 to 256 colours.");

    var source = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var decoder = default(Srgb32ToLinearRgbaF);
    var working = new LinearRgbaF[colors];

    for (var i = 0; i < colors; ++i) {
      var pixel = _ToStorage(palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2]);
      working[i] = decoder.Decode(ref pixel);
    }

    return _Place(source, working, ditherer);
  }

  /// <summary>Runs the ditherer and packs the result up as an indexed picture.</summary>
  /// <remarks>
  /// The ditherer wants the picture already in the working space, not in storage: it is comparing
  /// against a palette that lives there, and decoding every pixel again inside the inner loop
  /// would be the one thing in this design that is not free.
  /// </remarks>
  private static RawImage _Place<TDitherer>(RawImage source, LinearRgbaF[] palette, TDitherer ditherer)
    where TDitherer : struct, IDitherer {
    var width = source.Width;
    var height = source.Height;
    var indices = new byte[width * height];
    var metric = default(EuclideanSquared4F<LinearRgbaF>);
    var working = _ToWorking(source.PixelData, width * height);

    unsafe {
      fixed (LinearRgbaF* pixels = working)
      fixed (byte* target = indices)
        // Both strides count their own units: the source pointer is typed, the index pointer is
        // bytes. They are the same number here only because an index is one byte.
        ditherer.Dither<LinearRgbaF, EuclideanSquared4F<LinearRgbaF>>(
          pixels, target, width, height, width, width, 0, in metric, palette);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = _ToRgb(palette),
      PaletteCount = palette.Length,
    };
  }

  /// <summary>Decodes the whole picture into the working space, once.</summary>
  private static LinearRgbaF[] _ToWorking(byte[] bgra, int pixels) {
    var decoder = default(Srgb32ToLinearRgbaF);
    var working = new LinearRgbaF[pixels];

    for (var i = 0; i < pixels; ++i) {
      var at = i * _BYTES_PER_PIXEL;
      var pixel = _ToStorage((uint)(bgra[at] | (bgra[at + 1] << 8) | (bgra[at + 2] << 16) | (bgra[at + 3] << 24)));
      working[i] = decoder.Decode(ref pixel);
    }

    return working;
  }

  /// <summary>
  /// Counts how often each colour occurs, in the space the quantizer will judge them in.
  /// </summary>
  /// <remarks>
  /// A histogram rather than the pixels themselves, because that is what a quantizer takes: it is
  /// choosing among colours, and how many pixels share one is the only thing about position that
  /// matters to it.
  /// </remarks>
  private static IEnumerable<(LinearRgbaF, uint)> _Histogram(byte[] bgra, ref Srgb32ToLinearRgbaF decoder) {
    var counts = new Dictionary<uint, uint>();

    for (var i = 0; i + 3 < bgra.Length; i += _BYTES_PER_PIXEL) {
      var key = (uint)(bgra[i] | (bgra[i + 1] << 8) | (bgra[i + 2] << 16) | (bgra[i + 3] << 24));
      counts[key] = counts.TryGetValue(key, out var seen) ? seen + 1 : 1;
    }

    var result = new List<(LinearRgbaF, uint)>(counts.Count);
    foreach (var pair in counts) {
      var pixel = _ToStorage(pair.Key);
      result.Add((decoder.Decode(ref pixel), pair.Value));
    }

    return result;
  }

  private static Bgra8888 _ToStorage(byte red, byte green, byte blue)
    // The storage layout is blue, green, red, alpha; a RawImage palette is red, green, blue.
    => _ToStorage((uint)(blue | (green << 8) | (red << 16) | (0xFF << 24)));

  private static Bgra8888 _ToStorage(uint packed) {
    unsafe {
      return *(Bgra8888*)&packed;
    }
  }

  /// <summary>Brings the palette back out of the working space as RGB triplets.</summary>
  private static byte[] _ToRgb(LinearRgbaF[] palette) {
    var encoder = default(LinearRgbaFToSrgb32);
    var rgb = new byte[palette.Length * 3];

    for (var i = 0; i < palette.Length; ++i) {
      var stored = encoder.Encode(ref palette[i]);
      uint packed;
      unsafe {
        packed = *(uint*)&stored;
      }

      rgb[i * 3] = (byte)(packed >> 16);
      rgb[i * 3 + 1] = (byte)(packed >> 8);
      rgb[i * 3 + 2] = (byte)packed;
    }

    return rgb;
  }
}
