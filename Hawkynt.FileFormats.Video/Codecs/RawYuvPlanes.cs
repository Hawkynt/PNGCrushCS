using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// The three planes of a planar YUV picture, laid end to end with nothing between them and sited on
/// whichever chroma grid the packing that asked for them uses.
/// </summary>
/// <remarks>
/// Every one of the ten raw pixel layouts is a rearrangement of exactly these bytes, so the step that
/// gets from a <see cref="RawImage"/> to them belongs in one place — the same reasoning
/// <see cref="Yuv411Planes"/> already applies to the two codecs that pack 4:1:1.
/// <para/>
/// An eight-bit planar source is taken as it is: its luma plane is copied untouched and its chroma is
/// averaged down to the grid asked for where the source carried more, so a picture already sited on
/// that grid is reproduced exactly. Anything else is first converted to 4:4:4 under the ITU-R BT.601
/// studio-swing convention this package's uncompressed YUV decoders display with, and then averaged
/// the same way — which is why an RGB picture coded through one of these layouts and read back lands
/// where it started up to the rounding of the matrix and the loss of the subsampling itself.
/// <para/>
/// Averaging rather than dropping: a group of luma samples shares one chroma pair, and the pair that
/// represents them best is their mean, rounded to nearest.
/// </remarks>
internal static class RawYuvPlanes {

  /// <summary>The picture's luma, Cb and Cr planes, tightly packed and in that order.</summary>
  /// <remarks>
  /// Reading through <see cref="RawImage.GetPlaneData"/> rather than taking
  /// <see cref="RawImage.PixelData"/> whole is what makes this safe against a converter that hands
  /// back a longer buffer than the picture needs: the planes are copied at their stated sizes and
  /// whatever follows them is not.
  /// </remarks>
  public static byte[] Tight(RawImage source, int expected) {
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var luma = source.GetPlaneData(0);
    var cb = source.GetPlaneData(1);
    var cr = source.GetPlaneData(2);
    if (luma.Length + cb.Length + cr.Length != expected)
      throw new InvalidDataException(
        $"A {source.Width}x{source.Height} {source.Format} picture carries {luma.Length + cb.Length + cr.Length} "
        + $"bytes of planes where this packing needs {expected}.");

    var planes = new byte[expected];
    luma.CopyTo(planes);
    cb.CopyTo(planes.AsSpan(luma.Length));
    cr.CopyTo(planes.AsSpan(luma.Length + cb.Length));

    return planes;
  }

  /// <summary>
  /// The picture's planes with its chroma brought onto a grid of <paramref name="chromaWidth"/> by
  /// <paramref name="chromaHeight"/> samples, preserving <paramref name="native"/> byte for byte.
  /// </summary>
  public static byte[] Subsampled(RawImage frame, PixelFormat native, int chromaWidth, int chromaHeight)
    => _Subsampled(frame, native, chromaWidth, chromaHeight);

  /// <summary>
  /// The picture's planes with its chroma brought onto a grid of <paramref name="chromaWidth"/> by
  /// <paramref name="chromaHeight"/> samples when that grid has no exact <see cref="PixelFormat"/>.
  /// </summary>
  public static byte[] Subsampled(RawImage frame, int chromaWidth, int chromaHeight)
    => _Subsampled(frame, null, chromaWidth, chromaHeight);

  private static byte[] _Subsampled(RawImage frame, PixelFormat? native, int chromaWidth, int chromaHeight) {
    ArgumentNullException.ThrowIfNull(frame);
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var lumaSamples = frame.Width * frame.Height;
    var chromaSamples = chromaWidth * chromaHeight;
    if (native is { } nativeFormat && frame.Format == nativeFormat)
      return Tight(frame, lumaSamples + chromaSamples * 2);

    var source = _IsEightBitPlanar(frame.Format)
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);

    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(source.Format);
    var (sourceChromaWidth, sourceChromaHeight) = source.GetPlaneDimensions(1);
    var planes = new byte[lumaSamples + chromaSamples * 2];
    source.GetPlaneData(0)[..lumaSamples].CopyTo(planes);

    var groupWidth = frame.Width / chromaWidth;
    var groupHeight = frame.Height / chromaHeight;
    _Average(source.GetPlaneData(1), planes.AsSpan(lumaSamples, chromaSamples));
    _Average(source.GetPlaneData(2), planes.AsSpan(lumaSamples + chromaSamples, chromaSamples));

    return planes;

    // One target sample is the mean of the source samples the luma group it covers falls on: four at
    // 4:4:4 down to one where the source is already sited on this very grid, which is then a copy.
    void _Average(ReadOnlySpan<byte> from, Span<byte> to) {
      for (var cy = 0; cy < chromaHeight; ++cy) {
        var firstRow = cy * groupHeight / subsampleY;
        var lastRow = Math.Min((cy * groupHeight + groupHeight - 1) / subsampleY, sourceChromaHeight - 1);

        for (var cx = 0; cx < chromaWidth; ++cx) {
          var firstColumn = cx * groupWidth / subsampleX;
          var lastColumn = Math.Min((cx * groupWidth + groupWidth - 1) / subsampleX, sourceChromaWidth - 1);
          var sum = 0;
          var count = 0;

          for (var y = firstRow; y <= lastRow; ++y)
            for (var x = firstColumn; x <= lastColumn; ++x) {
              sum += from[y * sourceChromaWidth + x];
              ++count;
            }

          to[cy * chromaWidth + cx] = (byte)((sum + count / 2) / count);
        }
      }
    }
  }

  private static bool _IsEightBitPlanar(PixelFormat format)
    => format is PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8;
}
