using System;
using FileFormat.Core;

namespace FileFormat.CpcAdvanced;

/// <summary>In-memory representation of a CPC Advanced Mode 0 image (16384 bytes: 160x200, 16 colors, CPC memory interleave).</summary>
public readonly record struct CpcAdvancedFile : IImageFormatReader<CpcAdvancedFile>, IImageToRawImage<CpcAdvancedFile>, IImageFormatWriter<CpcAdvancedFile> {

  static string IImageFormatMetadata<CpcAdvancedFile>.PrimaryExtension => ".cpa";
  static string[] IImageFormatMetadata<CpcAdvancedFile>.FileExtensions => [".cpa"];
  static CpcAdvancedFile IImageFormatReader<CpcAdvancedFile>.FromSpan(ReadOnlySpan<byte> data) => CpcAdvancedReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CpcAdvancedFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<CpcAdvancedFile>.ToBytes(CpcAdvancedFile file) => CpcAdvancedWriter.ToBytes(file);

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = 16384;

  /// <summary>Image width in pixels (Mode 0).</summary>
  internal const int PixelWidth = 160;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 80;

  /// <summary>Pixels per byte in Mode 0.</summary>
  internal const int PixelsPerByte = 2;

  /// <summary>Always 160.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Deinterleaved pixel data (200 rows x 80 bytes, Mode 0 packed).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Default CPC 16-color palette as RGB triplets.</summary>
  private static readonly byte[] _CpcPalette = [
    0x00, 0x00, 0x00,  // 0  Black
    0x00, 0x00, 0x80,  // 1  Blue
    0x00, 0x00, 0xFF,  // 2  Bright Blue
    0x80, 0x00, 0x00,  // 3  Red
    0x80, 0x00, 0x80,  // 4  Magenta
    0x80, 0x00, 0xFF,  // 5  Mauve
    0x80, 0x80, 0x00,  // 6  Olive
    0x80, 0x80, 0x80,  // 7  Gray
    0x80, 0x80, 0xFF,  // 8  Pastel Blue
    0xFF, 0x00, 0x00,  // 9  Bright Red
    0xFF, 0x00, 0x80,  // 10 Rose
    0xFF, 0x00, 0xFF,  // 11 Bright Magenta
    0xFF, 0x80, 0x00,  // 12 Orange
    0xFF, 0x80, 0x80,  // 13 Pink
    0xFF, 0x80, 0xFF,  // 14 Pastel Magenta
    0xFF, 0xFF, 0x00,  // 15 Yellow
  ];

  /// <summary>Converts the CPC Advanced screen to an Indexed8 raw image (160x200, 16-entry CPC palette).</summary>
  public static RawImage ToRawImage(CpcAdvancedFile file) {

    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var b = file.PixelData[srcOffset];
        var p0 = _UnpackMode0Pixel0(b);
        var p1 = _UnpackMode0Pixel1(b);
        var baseX = byteCol * PixelsPerByte;
        if (baseX < PixelWidth)
          pixels[y * PixelWidth + baseX] = p0;
        if (baseX + 1 < PixelWidth)
          pixels[y * PixelWidth + baseX + 1] = p1;
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _CpcPalette[..],
      PaletteCount = 16,
    };
  }

  /// <summary>Unpacks pixel 0 from a CPC Mode 0 byte: bits [7,3,5,1].</summary>
  private static byte _UnpackMode0Pixel0(byte value) =>
    (byte)(((value >> 7) & 1)
         | (((value >> 3) & 1) << 1)
         | (((value >> 5) & 1) << 2)
         | (((value >> 1) & 1) << 3));

  /// <summary>Unpacks pixel 1 from a CPC Mode 0 byte: bits [6,2,4,0].</summary>
  private static byte _UnpackMode0Pixel1(byte value) =>
    (byte)(((value >> 6) & 1)
         | (((value >> 2) & 1) << 1)
         | (((value >> 4) & 1) << 2)
         | (((value >> 0) & 1) << 3));
}
