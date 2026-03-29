using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CpcPlus;

/// <summary>In-memory representation of a CPC Plus Mode 1 image (16384 bytes screen + 16 bytes palette: 320x200, 4 colors from 4096-color palette).</summary>
public sealed class CpcPlusFile : IImageFileFormat<CpcPlusFile> {

  static string IImageFileFormat<CpcPlusFile>.PrimaryExtension => ".cpp";
  static string[] IImageFileFormat<CpcPlusFile>.FileExtensions => [".cpp"];
  static CpcPlusFile IImageFileFormat<CpcPlusFile>.FromFile(FileInfo file) => CpcPlusReader.FromFile(file);
  static CpcPlusFile IImageFileFormat<CpcPlusFile>.FromBytes(byte[] data) => CpcPlusReader.FromBytes(data);
  static CpcPlusFile IImageFileFormat<CpcPlusFile>.FromStream(Stream stream) => CpcPlusReader.FromStream(stream);
  static RawImage IImageFileFormat<CpcPlusFile>.ToRawImage(CpcPlusFile file) => ToRawImage(file);
  static CpcPlusFile IImageFileFormat<CpcPlusFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<CpcPlusFile>.ToBytes(CpcPlusFile file) => CpcPlusWriter.ToBytes(file);

  /// <summary>Screen data size in bytes.</summary>
  internal const int ScreenDataSize = 16384;

  /// <summary>Palette data size in bytes (4 entries x 4 bytes each).</summary>
  internal const int PaletteDataSize = 16;

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = ScreenDataSize + PaletteDataSize;

  /// <summary>Image width in pixels (Mode 1).</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 80;

  /// <summary>Pixels per byte in Mode 1.</summary>
  internal const int PixelsPerByte = 4;

  /// <summary>Number of palette entries.</summary>
  internal const int PaletteEntries = 4;

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Deinterleaved pixel data (200 rows x 80 bytes, Mode 1 packed).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>12-bit palette entries (4 entries, each stored as 4 bytes: 0x0R, 0x0G, 0x0B, 0x00).</summary>
  public byte[] PaletteData { get; init; } = [];

  /// <summary>Converts the CPC Plus screen to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CpcPlusFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Decode 12-bit palette to RGB24
    var palette = new byte[PaletteEntries * 3];
    for (var i = 0; i < PaletteEntries; ++i) {
      var baseOffset = i * 4;
      if (baseOffset + 2 < file.PaletteData.Length) {
        // Each 4-bit channel scaled to 8-bit: value * 17 (0x0 -> 0x00, 0xF -> 0xFF)
        palette[i * 3] = (byte)((file.PaletteData[baseOffset] & 0x0F) * 17);
        palette[i * 3 + 1] = (byte)((file.PaletteData[baseOffset + 1] & 0x0F) * 17);
        palette[i * 3 + 2] = (byte)((file.PaletteData[baseOffset + 2] & 0x0F) * 17);
      }
    }

    var rgb = new byte[PixelWidth * PixelHeight * 3];

    for (var y = 0; y < PixelHeight; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        if (srcOffset >= file.PixelData.Length)
          continue;

        var b = file.PixelData[srcOffset];
        var baseX = byteCol * PixelsPerByte;

        // Mode 1: 4 pixels per byte
        var p0 = (byte)(((b >> 7) & 1) | (((b >> 3) & 1) << 1));
        var p1 = (byte)(((b >> 6) & 1) | (((b >> 2) & 1) << 1));
        var p2 = (byte)(((b >> 5) & 1) | (((b >> 1) & 1) << 1));
        var p3 = (byte)(((b >> 4) & 1) | (((b >> 0) & 1) << 1));

        byte[] indices = [p0, p1, p2, p3];
        for (var px = 0; px < 4; ++px) {
          var x = baseX + px;
          if (x >= PixelWidth)
            continue;

          var colorIdx = indices[px] % PaletteEntries;
          var dstOffset = (y * PixelWidth + x) * 3;
          rgb[dstOffset] = palette[colorIdx * 3];
          rgb[dstOffset + 1] = palette[colorIdx * 3 + 1];
          rgb[dstOffset + 2] = palette[colorIdx * 3 + 2];
        }
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. CPC Plus images require 12-bit palette mapping.</summary>
  public static CpcPlusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to CpcPlusFile is not supported.");
  }
}
