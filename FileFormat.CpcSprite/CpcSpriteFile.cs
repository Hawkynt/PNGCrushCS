using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CpcSprite;

/// <summary>In-memory representation of a CPC sprite (64 bytes: 16x16 pixels, Mode 1 packing, 4 colors).</summary>
public sealed class CpcSpriteFile : IImageFileFormat<CpcSpriteFile> {

  static string IImageFileFormat<CpcSpriteFile>.PrimaryExtension => ".cps";
  static string[] IImageFileFormat<CpcSpriteFile>.FileExtensions => [".cps"];
  static FormatCapability IImageFileFormat<CpcSpriteFile>.Capabilities => FormatCapability.IndexedOnly;
  static CpcSpriteFile IImageFileFormat<CpcSpriteFile>.FromFile(FileInfo file) => CpcSpriteReader.FromFile(file);
  static CpcSpriteFile IImageFileFormat<CpcSpriteFile>.FromBytes(byte[] data) => CpcSpriteReader.FromBytes(data);
  static CpcSpriteFile IImageFileFormat<CpcSpriteFile>.FromStream(Stream stream) => CpcSpriteReader.FromStream(stream);
  static RawImage IImageFileFormat<CpcSpriteFile>.ToRawImage(CpcSpriteFile file) => ToRawImage(file);
  static CpcSpriteFile IImageFileFormat<CpcSpriteFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<CpcSpriteFile>.ToBytes(CpcSpriteFile file) => CpcSpriteWriter.ToBytes(file);

  /// <summary>Expected file size in bytes (16 rows x 4 bytes per row).</summary>
  internal const int ExpectedFileSize = 64;

  /// <summary>Sprite width in pixels.</summary>
  internal const int PixelWidth = 16;

  /// <summary>Sprite height in pixels.</summary>
  internal const int PixelHeight = 16;

  /// <summary>Bytes per row (16 pixels / 4 pixels per byte in Mode 1).</summary>
  internal const int BytesPerRow = 4;

  /// <summary>Pixels per byte in Mode 1.</summary>
  internal const int PixelsPerByte = 4;

  /// <summary>Always 16.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 16.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw sprite data (64 bytes: Mode 1 packed, 4 bytes per row, 16 rows).</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Default CPC 4-color palette for Mode 1 as RGB triplets.</summary>
  private static readonly byte[] _CpcMode1Palette = [
    0x00, 0x00, 0x00,  // 0 Black
    0x00, 0x00, 0xFF,  // 1 Blue
    0xFF, 0x00, 0x00,  // 2 Red
    0xFF, 0xFF, 0x00,  // 3 Yellow
  ];

  /// <summary>Converts the CPC sprite to an Indexed8 raw image (16x16, 4-entry CPC palette).</summary>
  public static RawImage ToRawImage(CpcSpriteFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        var b = srcOffset < file.RawData.Length ? file.RawData[srcOffset] : (byte)0;
        var baseX = byteCol * PixelsPerByte;

        // Mode 1: 4 pixels per byte
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

  /// <summary>Not supported. CPC sprite data requires specific Mode 1 pixel packing.</summary>
  public static CpcSpriteFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CpcSpriteFile is not supported.");
  }
}
