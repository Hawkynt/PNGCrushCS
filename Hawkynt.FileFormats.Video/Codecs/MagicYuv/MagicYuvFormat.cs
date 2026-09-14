using System;
using FileFormat.Core;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>What a MagicYUV stream's samples mean.</summary>
internal enum MagicYuvColourSpace {
  Grey,
  Rgb,
  Yuv,
}

/// <summary>The layout a MagicYUV v7 FourCC stands for.</summary>
/// <remarks>
/// MagicYUV never published a bitstream specification. The format-byte/depth mapping below is
/// cross-checked against FFmpeg's LGPL-2.1-or-later decoder and OxideAV's MIT-licensed clean-room
/// implementation. The latter also independently documents the per-depth Huffman limit and the
/// vendor BITMAPINFOHEADER depth values.
/// </remarks>
internal sealed class MagicYuvFormat {
  internal static readonly byte[] Signature = [(byte)'M', (byte)'A', (byte)'G', (byte)'Y'];
  internal const int HEADER_SIZE = 32;
  internal const int VERSION_BYTE = 7;

  private MagicYuvFormat(
    MagicYuvColourSpace colourSpace,
    int planeCount,
    int chromaHorizontalShift,
    int chromaVerticalShift,
    bool hasAlpha,
    int bitDepth,
    byte formatByte,
    int streamBitsPerPixel
  ) {
    this.ColourSpace = colourSpace;
    this.PlaneCount = planeCount;
    this.ChromaHorizontalShift = chromaHorizontalShift;
    this.ChromaVerticalShift = chromaVerticalShift;
    this.HasAlpha = hasAlpha;
    this.BitDepth = bitDepth;
    this.FormatByte = formatByte;
    this.StreamBitsPerPixel = streamBitsPerPixel;
  }

  internal MagicYuvColourSpace ColourSpace { get; }
  internal int PlaneCount { get; }
  internal int ChromaHorizontalShift { get; }
  internal int ChromaVerticalShift { get; }
  internal bool HasAlpha { get; }
  internal int BitDepth { get; }
  internal byte FormatByte { get; }
  internal int StreamBitsPerPixel { get; }
  internal int SymbolCount => 1 << this.BitDepth;
  internal int SampleMask => this.SymbolCount - 1;
  internal int MaxHuffmanLength => this.BitDepth + 4;
  internal bool IsHighBitDepth => this.BitDepth > 8;

  internal PixelFormat NativePixelFormat => (this.ColourSpace, this.BitDepth, this.HasAlpha, this.ChromaHorizontalShift, this.ChromaVerticalShift) switch {
    (MagicYuvColourSpace.Grey, 8, _, _, _) => PixelFormat.Gray8,
    (MagicYuvColourSpace.Grey, 10, _, _, _) => PixelFormat.Gray10,
    (MagicYuvColourSpace.Rgb, 8, false, _, _) => PixelFormat.Rgb24,
    (MagicYuvColourSpace.Rgb, 8, true, _, _) => PixelFormat.Rgba32,
    (MagicYuvColourSpace.Rgb, _, false, _, _) => PixelFormat.Rgb48,
    (MagicYuvColourSpace.Rgb, _, true, _, _) => PixelFormat.Rgba64,
    (MagicYuvColourSpace.Yuv, 8, false, 1, 1) => PixelFormat.Yuv420P8,
    (MagicYuvColourSpace.Yuv, 8, false, 1, 0) => PixelFormat.Yuv422P8,
    (MagicYuvColourSpace.Yuv, 8, false, 0, 0) => PixelFormat.Yuv444P8,
    (MagicYuvColourSpace.Yuv, 10, false, 1, 1) => PixelFormat.Yuv420P10,
    (MagicYuvColourSpace.Yuv, 10, false, 1, 0) => PixelFormat.Yuv422P10,
    (MagicYuvColourSpace.Yuv, 10, false, 0, 0) => PixelFormat.Yuv444P10,
    (MagicYuvColourSpace.Yuv, 8, true, _, _) => PixelFormat.Rgba32,
    _ => throw new NotSupportedException(
      $"No RawImage representation exists for {this.BitDepth}-bit {this.ColourSpace} samples."),
  };

  internal bool IsChroma(int plane)
    => this.ColourSpace == MagicYuvColourSpace.Yuv && plane is 1 or 2;

  internal (int Width, int Height) PlaneSize(int plane, int width, int height) {
    if (!this.IsChroma(plane))
      return (width, height);

    var horizontal = 1 << this.ChromaHorizontalShift;
    var vertical = 1 << this.ChromaVerticalShift;
    return (
      (width + horizontal - 1) >> this.ChromaHorizontalShift,
      (height + vertical - 1) >> this.ChromaVerticalShift
    );
  }

  internal int SliceHeight(int plane, int frameSliceHeight) {
    if (!this.IsChroma(plane))
      return frameSliceHeight;

    var vertical = 1 << this.ChromaVerticalShift;
    return (frameSliceHeight + vertical - 1) >> this.ChromaVerticalShift;
  }

  internal static MagicYuvFormat Of(CodecTag codec, int streamIndex) {
    var name = codec.ToString();
    return name switch {
      "M8RG" => new(MagicYuvColourSpace.Rgb, 3, 0, 0, false, 8, 0x65, 24),
      "M8RA" => new(MagicYuvColourSpace.Rgb, 4, 0, 0, true, 8, 0x66, 32),
      "M8Y4" => new(MagicYuvColourSpace.Yuv, 3, 0, 0, false, 8, 0x67, 24),
      "M8Y2" => new(MagicYuvColourSpace.Yuv, 3, 1, 0, false, 8, 0x68, 24),
      "M8Y0" => new(MagicYuvColourSpace.Yuv, 3, 1, 1, false, 8, 0x69, 24),
      "M8YA" => new(MagicYuvColourSpace.Yuv, 4, 0, 0, true, 8, 0x6A, 32),
      "M8G0" => new(MagicYuvColourSpace.Grey, 1, 0, 0, false, 8, 0x6B, 24),
      "M0Y2" => new(MagicYuvColourSpace.Yuv, 3, 1, 0, false, 10, 0x6C, 20),
      "M0RG" => new(MagicYuvColourSpace.Rgb, 3, 0, 0, false, 10, 0x6D, 30),
      "M0RA" => new(MagicYuvColourSpace.Rgb, 4, 0, 0, true, 10, 0x6E, 40),
      "M2RG" => new(MagicYuvColourSpace.Rgb, 3, 0, 0, false, 12, 0x6F, 36),
      "M2RA" => new(MagicYuvColourSpace.Rgb, 4, 0, 0, true, 12, 0x70, 48),
      "M4RG" => new(MagicYuvColourSpace.Rgb, 3, 0, 0, false, 14, 0x71, 42),
      "M4RA" => new(MagicYuvColourSpace.Rgb, 4, 0, 0, true, 14, 0x72, 56),
      "M0G0" => new(MagicYuvColourSpace.Grey, 1, 0, 0, false, 10, 0x73, 10),
      "M0Y4" => new(MagicYuvColourSpace.Yuv, 3, 0, 0, false, 10, 0x76, 30),
      "M0Y0" => new(MagicYuvColourSpace.Yuv, 3, 1, 1, false, 10, 0x7B, 15),
      "M8GA" => throw new NotSupportedException(
        $"Video stream {streamIndex} is M8GA — grey with an alpha channel — which is not one of MagicYUV v7's native formats."),
      "MAGY" => throw new NotSupportedException(
        $"Video stream {streamIndex} is MAGY, the single code MagicYUV used before it gave each pixel format one of its own. Which format such a file holds is not in its code, so it is refused rather than guessed at."),
      _ => throw new NotSupportedException(
        $"Video stream {streamIndex} is named {name}, which is not a MagicYUV v7 native format code."),
    };
  }
}
