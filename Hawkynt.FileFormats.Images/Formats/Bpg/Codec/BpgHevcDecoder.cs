using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>Decodes a BPG file's pictures: its own uniform-PCM writer output directly, everything else through the general H.265 decoder.</summary>
internal static class BpgHevcDecoder {

  /// <summary>One decoded frame: interleaved samples and how many animation periods it lasts.</summary>
  internal sealed record DecodedFrame(byte[] Pixels, int Duration);

  /// <summary>
  /// Decodes every frame of a BPG file: Gray8, RGB24 or RGBA32 for an eight-bit picture, the
  /// sixteen-bit (big-endian) Gray16, RGB48 or RGBA64 for a deeper one.
  /// </summary>
  public static IReadOnlyList<DecodedFrame> Decode(BpgFile bpg) {
    ArgumentNullException.ThrowIfNull(bpg);

    if (bpg.PixelData.Length == 0)
      throw new InvalidOperationException("BPG file contains no HEVC data.");

    if (_TryDecodeUniformPcm(bpg, out var pcm))
      return [new(pcm, 1)];

    // alpha1_flag set: an alpha plane, premultiplied when alpha2_flag is set too. alpha1_flag clear
    // with alpha2_flag set: the plane is CMYK's W, which multiplies the colour rather than being a
    // transparency.
    var hasPlane = bpg.HasAlpha || bpg.HasAlpha2;
    var wPlane = !bpg.HasAlpha && bpg.HasAlpha2;
    var chroma = bpg.PixelFormat switch {
      BpgPixelFormat.Grayscale => 0,
      BpgPixelFormat.YCbCr420 or BpgPixelFormat.YCbCr420Mpeg2 => 1,
      BpgPixelFormat.YCbCr422 or BpgPixelFormat.YCbCr422Mpeg2 => 2,
      BpgPixelFormat.YCbCr444 => 3,
      _ => throw new InvalidDataException($"A BPG file states pixel format {(int)bpg.PixelFormat}, which the specification reserves."),
    };
    if (bpg.BitDepth > 14)
      throw new InvalidDataException($"A BPG file states {bpg.BitDepth}-bit samples; the specification stops at fourteen.");
    if (bpg.ColorSpace == BpgColorSpace.YCbCrBT2020Cl || (int)bpg.ColorSpace > 5)
      throw new NotSupportedException($"BPG colour space {bpg.ColorSpace} is reserved by the specification and has no conversion.");

    var frames = BpgStreamDecoder.Decode(bpg.PixelData, bpg.Width, bpg.Height, chroma, bpg.BitDepth, hasPlane);
    var deep = bpg.BitDepth > 8;
    var gray = chroma == 0;
    var rgba = bpg.HasAlpha;
    var result = new DecodedFrame[frames.Count];
    for (var i = 0; i < frames.Count; ++i) {
      var pixels = BpgColourOutput.ToPixels(
        frames[i].Colour, frames[i].Alpha, bpg.Width, bpg.Height, bpg.PixelFormat, bpg.BitDepth,
        bpg.ColorSpace, bpg.LimitedRange, premultiplied: bpg is { HasAlpha: true, HasAlpha2: true }, wPlane, rgba, deep);
      if (gray && !rgba)
        pixels = _FirstOfThree(pixels, deep);
      result[i] = new(pixels, frames[i].Duration);
    }

    return result;
  }

  /// <summary>A grey picture's RGB carries the same value three times; the raster keeps one.</summary>
  private static byte[] _FirstOfThree(byte[] rgb, bool deep) {
    var bytes = deep ? 2 : 1;
    var result = new byte[rgb.Length / 3];
    for (var i = 0; i < result.Length / bytes; ++i)
      for (var b = 0; b < bytes; ++b)
        result[i * bytes + b] = rgb[i * 3 * bytes + b];
    return result;
  }

  /// <summary>
  /// Decodes a picture whose coding units are all unsplit PCM ones, which is the shape this package
  /// writes, and answers false for everything else so the general path still gets its turn.
  /// </summary>
  /// <remarks>
  /// PCM coding units carry their samples verbatim, so this path needs none of the transform,
  /// prediction or residual machinery the general decoder is made of — but it is not a shortcut
  /// around it either. A stream it declines is one it has proved is not made of whole samples, not
  /// one it merely failed to read.
  /// </remarks>
  private static bool _TryDecodeUniformPcm(BpgFile bpg, out byte[] pixels) {
    pixels = [];
    if (bpg.BitDepth != 8 || bpg.HasAlpha || bpg.HasAlpha2 || bpg.LimitedRange)
      return false;

    // 4:4:4 with no colour matrix is the one shape this path reads, and the one this package
    // writes. Subsampled chroma and the YCbCr matrices go to the general decoder instead.
    if (bpg is not { PixelFormat: BpgPixelFormat.YCbCr444, ColorSpace: BpgColorSpace.Rgb })
      return false;

    var header = BpgSequenceHeader.TryParse(bpg.PixelData);
    if (header is not {
          PcmEnabled: true, SaoEnabled: false,
          PcmBitDepthLuma: 8, PcmBitDepthChroma: 8,
          Log2CtbSize: _CODING_TREE_BLOCK_LOG2,
          Log2MinLumaCodingBlockSize: _CODING_TREE_BLOCK_LOG2,
        }
        || header.Log2MinPcmCodingBlockSize > _CODING_TREE_BLOCK_LOG2
        || header.Log2MaxPcmCodingBlockSize < _CODING_TREE_BLOCK_LOG2)
      return false;

    var block = 1 << _CODING_TREE_BLOCK_LOG2;
    var codedWidth = (bpg.Width + block - 1) / block * block;
    var codedHeight = (bpg.Height + block - 1) / block * block;
    if (!H265PcmStillCodec.TryDecodeBpgStill(
          bpg.PixelData.AsSpan(header.HevcDataOffset), codedWidth, codedHeight, out var planes))
      return false;

    pixels = _InterleaveGbr(planes, codedWidth, bpg.Width, bpg.Height);
    return true;
  }

  /// <summary>
  /// The tree block size <see cref="H265PcmStillCodec"/> writes, which clause 7.4.3.2.1 also caps
  /// a PCM coding block at.
  /// </summary>
  private const int _CODING_TREE_BLOCK_LOG2 = 5;

  /// <summary>Packs BPG's G, B and R planes into the interleaved RGB24 the rest of the package uses.</summary>
  private static byte[] _InterleaveGbr(byte[][] planes, int stride, int width, int height) {
    var result = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var from = y * stride + x;
        var to = (y * width + x) * 3;
        result[to] = planes[2][from];
        result[to + 1] = planes[0][from];
        result[to + 2] = planes[1][from];
      }

    return result;
  }
}
