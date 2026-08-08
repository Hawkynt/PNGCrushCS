using System;
using FileFormat.Core;

namespace FileFormat.CpcPlus;

/// <summary>In-memory representation of a CPC Plus Mode 1 image (16384 bytes screen + 16 bytes palette: 320x200, 4 colors from 4096-color palette).</summary>
public readonly record struct CpcPlusFile : IImageFormatReader<CpcPlusFile>, IImageToRawImage<CpcPlusFile>, IImageFromRawImage<CpcPlusFile>, IImageFormatWriter<CpcPlusFile> {

  static string IImageFormatMetadata<CpcPlusFile>.PrimaryExtension => ".cpp";
  static string[] IImageFormatMetadata<CpcPlusFile>.FileExtensions => [".cpp"];
  static CpcPlusFile IImageFormatReader<CpcPlusFile>.FromSpan(ReadOnlySpan<byte> data) => CpcPlusReader.FromSpan(data);
  static byte[] IImageFormatWriter<CpcPlusFile>.ToBytes(CpcPlusFile file) => CpcPlusWriter.ToBytes(file);

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
  public byte[] PixelData { get; init; }

  /// <summary>12-bit palette entries (4 entries, each stored as 4 bytes: 0x0R, 0x0G, 0x0B, 0x00).</summary>
  public byte[] PaletteData { get; init; }

  /// <summary>Converts the CPC Plus screen to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(CpcPlusFile file) {

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

  /// <summary>Builds a Plus screen from any picture, sampling it to 320x200 and reducing it to the four inks mode 1 shows.</summary>
  /// <remarks>
  /// Unlike the base machine the Plus has no fixed palette — its four inks are chosen freely out of
  /// 4096 — so the colours come from a quantiser rather than from a lookup, and only their top four
  /// bits a channel are kept.
  /// </remarks>
  public static CpcPlusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(PixelWidth, PixelHeight).EnsureFormat(PixelFormat.Bgra32);
    var quantized = ColorQuantizer.Quantize(source.PixelData, PixelWidth * PixelHeight, PaletteEntries);

    var paletteData = new byte[PaletteDataSize];
    for (var i = 0; i < quantized.Count; ++i) {
      paletteData[i * 4] = _ToNibble(quantized.Palette[i * 3]);
      paletteData[i * 4 + 1] = _ToNibble(quantized.Palette[i * 3 + 1]);
      paletteData[i * 4 + 2] = _ToNibble(quantized.Palette[i * 3 + 2]);
    }

    var pixelData = new byte[PixelHeight * BytesPerRow];
    for (var y = 0; y < PixelHeight; ++y)
    for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
      var baseX = byteCol * PixelsPerByte;
      var value = 0;

      for (var p = 0; p < PixelsPerByte; ++p) {
        var index = quantized.Indices[y * PixelWidth + baseX + p] & 3;
        value |= (index & 1) << (7 - p);
        value |= ((index >> 1) & 1) << (3 - p);
      }

      pixelData[y * BytesPerRow + byteCol] = (byte)value;
    }

    return new() { PixelData = pixelData, PaletteData = paletteData };
  }

  /// <summary>A channel of 0..255 as the nibble the file holds.</summary>
  /// <remarks>
  /// Rounded, not truncated. The decoder widens a nibble by multiplying by seventeen, and dividing
  /// by sixteen to get it back lands a step low on every value but nought and fifteen.
  /// </remarks>
  private static byte _ToNibble(byte channel) => (byte)((channel + 8) / 17);

}
