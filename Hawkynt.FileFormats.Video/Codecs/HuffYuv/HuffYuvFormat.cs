using System;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

internal enum HuffYuvPredictor {
  Left = 0,
  Gradient = 1,
  Median = 2,
}

internal enum HuffYuvColourSpace {
  Grey,
  Yuv,
  PlanarRgb,
  PackedBgr,
}

/// <summary>Everything a HuffYUV/FFVHUFF stream says, or historically implies, about its frames.</summary>
internal sealed class HuffYuvFormat {

  private HuffYuvFormat() { }

  internal int Version { get; private init; }
  internal HuffYuvPredictor Predictor { get; private init; }
  internal bool Decorrelate { get; private init; }
  internal bool Interlaced { get; private init; }
  internal bool TablesPerFrame { get; private init; }
  internal bool UsesLegacyTables => this.Version == 1;
  internal HuffYuvColourSpace ColourSpace { get; private init; }
  internal int BitsPerSample { get; private init; }
  internal int ChromaHorizontalShift { get; private init; }
  internal int ChromaVerticalShift { get; private init; }
  internal bool HasAlpha { get; private init; }
  internal int BitstreamBitsPerPixel { get; private init; }
  internal int HuffmanSymbolCount => Math.Min(1 << this.BitsPerSample, HuffYuvHuffmanTable.MAX_SYMBOL_COUNT);

  internal int TableCount => this.ColourSpace switch {
    HuffYuvColourSpace.Grey => 1,
    HuffYuvColourSpace.PackedBgr => 3,
    _ => this.HasAlpha ? 4 : 3,
  };

  internal static HuffYuvFormat Parse(ReadOnlySpan<byte> extra, int bitsPerPixel, int streamIndex, int height = 0) {
    if (extra.Length < 4)
      return _Original(bitsPerPixel, height, streamIndex);
    return extra[3] == 1 ? _Planar(extra, height, streamIndex) : _Packed(extra, bitsPerPixel, height, streamIndex);
  }

  private static HuffYuvFormat _Original(int bitsPerPixel, int height, int streamIndex) {
    var packedDepth = bitsPerPixel & ~7;
    var mode = bitsPerPixel & 7;
    var predictor = mode switch {
      3 => HuffYuvPredictor.Gradient,
      4 => HuffYuvPredictor.Median,
      _ => HuffYuvPredictor.Left,
    };
    var decorrelate = mode == 2 || (mode == 3 && bitsPerPixel >= 24);
    var colourSpace = packedDepth switch {
      12 or 16 => HuffYuvColourSpace.Yuv,
      24 or 32 => HuffYuvColourSpace.PackedBgr,
      _ => throw new NotSupportedException($"Video stream {streamIndex} is headerless HuffYUV with coded depth {bitsPerPixel}; its packed depth {packedDepth} is not 12, 16, 24 or 32."),
    };

    return new() {
      Version = 1,
      Predictor = predictor,
      Decorrelate = decorrelate,
      Interlaced = height > 288,
      TablesPerFrame = false,
      ColourSpace = colourSpace,
      BitsPerSample = 8,
      BitstreamBitsPerPixel = packedDepth,
      ChromaHorizontalShift = 1,
      ChromaVerticalShift = packedDepth == 12 ? 1 : 0,
      HasAlpha = packedDepth == 32,
    };
  }

  private static HuffYuvFormat _Packed(ReadOnlySpan<byte> extra, int bitsPerPixel, int height, int streamIndex) {
    var method = extra[0];
    var bitstreamBpp = extra[1] != 0 ? extra[1] : bitsPerPixel & ~7;
    var colourSpace = bitstreamBpp switch {
      12 or 16 => HuffYuvColourSpace.Yuv,
      24 or 32 => HuffYuvColourSpace.PackedBgr,
      _ => throw new NotSupportedException($"Video stream {streamIndex} states {bitstreamBpp} bits a pixel in its HuffYUV bitstream."),
    };

    return new() {
      Version = 2,
      Predictor = _PredictorOf(method & 63, streamIndex),
      Decorrelate = (method & 64) != 0,
      Interlaced = _InterlacedFrom(extra[2], height),
      TablesPerFrame = (extra[2] & 0x40) != 0,
      ColourSpace = colourSpace,
      BitsPerSample = 8,
      BitstreamBitsPerPixel = bitstreamBpp,
      ChromaHorizontalShift = 1,
      ChromaVerticalShift = bitstreamBpp == 12 ? 1 : 0,
      HasAlpha = bitstreamBpp == 32,
    };
  }

  private static HuffYuvFormat _Planar(ReadOnlySpan<byte> extra, int height, int streamIndex) {
    var method = extra[0];
    var flags = extra[2];
    var chroma = (flags & 1) != 0;
    var rgb = (flags & 2) != 0;
    var bits = (extra[1] >> 4) + 1;
    if (bits is < 8 or > 16)
      throw new NotSupportedException($"Video stream {streamIndex} carries {bits}-bit FFVHUFF samples; version 3 defines depths from 8 through 16 bits.");

    return new() {
      Version = 3,
      Predictor = _PredictorOf(method & 63, streamIndex),
      Decorrelate = (method & 64) != 0,
      Interlaced = _InterlacedFrom(flags, height),
      TablesPerFrame = (flags & 0x40) != 0,
      ColourSpace = rgb ? HuffYuvColourSpace.PlanarRgb : chroma ? HuffYuvColourSpace.Yuv : HuffYuvColourSpace.Grey,
      BitsPerSample = bits,
      ChromaHorizontalShift = extra[1] & 3,
      ChromaVerticalShift = (extra[1] >> 2) & 3,
      HasAlpha = (flags & 4) != 0,
      BitstreamBitsPerPixel = 0,
    };
  }

  private static bool _InterlacedFrom(byte flags, int height) => ((flags >> 4) & 3) switch {
    1 => true,
    2 => false,
    _ => height > 288,
  };

  private static HuffYuvPredictor _PredictorOf(int method, int streamIndex) => method switch {
    0 => HuffYuvPredictor.Left,
    1 => HuffYuvPredictor.Gradient,
    2 => HuffYuvPredictor.Median,
    _ => throw new NotSupportedException($"Video stream {streamIndex} names prediction method {method}, which is not one of left, gradient and median."),
  };

  /// <summary>Kept for callers written before the historical height fallback was implemented.</summary>
  internal static void RefuseUnstatedInterlacing(ReadOnlySpan<byte> extra, int height, int streamIndex) { }
}
