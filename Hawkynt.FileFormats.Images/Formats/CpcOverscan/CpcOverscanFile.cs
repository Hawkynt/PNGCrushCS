using System;
using FileFormat.Core;

namespace FileFormat.CpcOverscan;

/// <summary>In-memory representation of a CPC overscan image (32768 bytes: 384x272, Mode 1, 4 colors).</summary>
public readonly record struct CpcOverscanFile : IImageFormatReader<CpcOverscanFile>, IImageToRawImage<CpcOverscanFile>, IImageFromRawImage<CpcOverscanFile>, IImageFormatWriter<CpcOverscanFile> {

  static string IImageFormatMetadata<CpcOverscanFile>.PrimaryExtension => ".cpo";
  static string[] IImageFormatMetadata<CpcOverscanFile>.FileExtensions => [".cpo"];
  static CpcOverscanFile IImageFormatReader<CpcOverscanFile>.FromSpan(ReadOnlySpan<byte> data) => CpcOverscanReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CpcOverscanFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 4)])
  ];
  static byte[] IImageFormatWriter<CpcOverscanFile>.ToBytes(CpcOverscanFile file) => CpcOverscanWriter.ToBytes(file);

  /// <summary>Expected file size in bytes (two 16KB banks).</summary>
  internal const int ExpectedFileSize = 32768;

  /// <summary>Image width in pixels (Mode 1 overscan).</summary>
  internal const int PixelWidth = 384;

  /// <summary>Image height in pixels (full overscan).</summary>
  internal const int PixelHeight = 272;

  /// <summary>Bytes per scanline (384 pixels / 4 pixels per byte in Mode 1 = 96).</summary>
  internal const int BytesPerRow = 96;

  /// <summary>Pixels per byte in Mode 1.</summary>
  internal const int PixelsPerByte = 4;

  /// <summary>Always 384.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 272.</summary>
  public int Height => PixelHeight;

  /// <summary>Deinterleaved pixel data (272 rows x 96 bytes, Mode 1 packed).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Default CPC 4-color palette for Mode 1 as RGB triplets.</summary>
  private static readonly byte[] _CpcMode1Palette = [
    0x00, 0x00, 0x00,  // 0 Black
    0x00, 0x00, 0xFF,  // 1 Blue
    0xFF, 0x00, 0x00,  // 2 Red
    0xFF, 0xFF, 0x00,  // 3 Yellow
  ];

  /// <summary>Converts the CPC overscan screen to an Indexed8 raw image (384x272, 4-entry palette).</summary>
  public static RawImage ToRawImage(CpcOverscanFile file) {

    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var b = file.PixelData[srcOffset];
        var baseX = byteCol * PixelsPerByte;

        // Mode 1: 4 pixels per byte
        // pixel0 = bits [7,3], pixel1 = bits [6,2], pixel2 = bits [5,1], pixel3 = bits [4,0]
        var p0 = (byte)(((b >> 7) & 1) | (((b >> 3) & 1) << 1));
        var p1 = (byte)(((b >> 6) & 1) | (((b >> 2) & 1) << 1));
        var p2 = (byte)(((b >> 5) & 1) | (((b >> 1) & 1) << 1));
        var p3 = (byte)(((b >> 4) & 1) | (((b >> 0) & 1) << 1));

        var dstBase = y * PixelWidth + baseX;
        if (baseX < PixelWidth)
          pixels[dstBase] = p0;
        if (baseX + 1 < PixelWidth)
          pixels[dstBase + 1] = p1;
        if (baseX + 2 < PixelWidth)
          pixels[dstBase + 2] = p2;
        if (baseX + 3 < PixelWidth)
          pixels[dstBase + 3] = p3;
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _CpcMode1Palette[..],
      PaletteCount = 4,
    };
  }

  /// <summary>Builds an overscan screen from any picture, sampling it to 384x272 and mapping it onto the four mode 1 inks.</summary>
  public static CpcOverscanFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.SampleTo(PixelWidth, PixelHeight).EnsureIndexed(PixelFormat.Indexed8, _CpcMode1Palette);
    var pixelData = new byte[PixelHeight * BytesPerRow];

    for (var y = 0; y < PixelHeight; ++y)
    for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
      var baseX = byteCol * PixelsPerByte;
      var value = 0;

      // Mode 1 keeps a pixel's two bits four places apart, one nibble to a bitplane, which is what
      // the four shifts the decoder above does undo.
      for (var p = 0; p < PixelsPerByte; ++p) {
        var index = indexed.PixelData[y * PixelWidth + baseX + p] & 3;
        value |= (index & 1) << (7 - p);
        value |= ((index >> 1) & 1) << (3 - p);
      }

      pixelData[y * BytesPerRow + byteCol] = (byte)value;
    }

    return new() { PixelData = pixelData };
  }

}
