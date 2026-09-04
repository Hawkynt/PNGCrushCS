using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Turns a picture into the three eight-bit planes a 4:1:1 packing codes: luma at the full width, one
/// chroma pair for every four columns, every row its own.
/// </summary>
/// <remarks>
/// <see cref="PixelFormat"/> has no 4:1:1 layout of its own, so the two codecs that pack one — y41p
/// and CLJR — share this step rather than each inventing a different way of getting there. An
/// eight-bit planar source is taken as it is, its samples passed through untouched and its chroma
/// averaged down to one pair per four columns where the source carried more; anything else is first
/// converted to 4:4:4 under the same ITU-R BT.601 studio-swing convention the package's decoders of
/// these formats display with, so that a picture coded here and read back there lands where it
/// started up to the rounding of the matrix and the loss of the subsampling itself.
/// <para/>
/// Averaging rather than dropping: four columns share a pair, and the pair that represents them best
/// is their mean, rounded to nearest. A source already sited at one pair per four columns — 4:4:4
/// content whose chroma is constant across each group — is reproduced exactly.
/// </remarks>
internal static class Yuv411Planes {

  /// <summary>
  /// Builds the planes for a picture whose width is a whole number of four-pixel groups.
  /// </summary>
  public static (byte[] Luma, byte[] Cb, byte[] Cr) FromImage(RawImage frame) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width % 4 != 0)
      throw new InvalidDataException($"A 4:1:1 picture is a whole number of four-pixel groups wide; {frame.Width} is not.");

    var source = _IsEightBitPlanar(frame.Format)
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var width = source.Width;
    var height = source.Height;
    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(source.Format);
    var (sourceChromaWidth, _) = source.GetPlaneDimensions(1);
    var chromaWidth = width / 4;

    var luma = source.GetPlaneData(0).ToArray();
    var cb = new byte[chromaWidth * height];
    var cr = new byte[chromaWidth * height];
    _Average(source.GetPlaneData(1), cb, height, chromaWidth, sourceChromaWidth, subsampleX, subsampleY);
    _Average(source.GetPlaneData(2), cr, height, chromaWidth, sourceChromaWidth, subsampleX, subsampleY);

    return (luma, cb, cr);
  }

  private static bool _IsEightBitPlanar(PixelFormat format)
    => format is PixelFormat.Yuv420P8 or PixelFormat.Yuv422P8 or PixelFormat.Yuv440P8 or PixelFormat.Yuv444P8;

  /// <summary>One pair per four columns, the mean of the source samples those columns fall on.</summary>
  private static void _Average(
    ReadOnlySpan<byte> source, byte[] target, int height, int chromaWidth, int sourceChromaWidth,
    int subsampleX, int subsampleY) {
    // Four columns cover 4 / subsampleX distinct source samples: four at 4:4:4, two at 4:2:2, one at
    // 4:2:0 and 4:4:0.
    var samplesPerGroup = 4 / subsampleX;
    var half = samplesPerGroup / 2;

    for (var y = 0; y < height; ++y) {
      var sourceRow = (y / subsampleY) * sourceChromaWidth;
      var targetRow = y * chromaWidth;

      for (var c = 0; c < chromaWidth; ++c) {
        var first = sourceRow + (c * 4) / subsampleX;
        var sum = 0;
        for (var i = 0; i < samplesPerGroup; ++i)
          sum += source[first + i];

        target[targetRow + c] = (byte)((sum + half) / samplesPerGroup);
      }
    }
  }
}
