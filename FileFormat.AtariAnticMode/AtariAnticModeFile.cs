using System;
using FileFormat.Core;

namespace FileFormat.AtariAnticMode;

/// <summary>In-memory representation of an Atari ANTIC Mode E/F Screen (.ame) file.</summary>
public readonly record struct AtariAnticModeFile : IImageFormatReader<AtariAnticModeFile>, IImageToRawImage<AtariAnticModeFile>, IImageFormatWriter<AtariAnticModeFile> {

  /// <summary>Size of the screen data in bytes.</summary>
  public const int ScreenDataSize = 7680;

  /// <summary>Total file size: 7680 data bytes + 1 mode byte.</summary>
  public const int ExpectedFileSize = ScreenDataSize + 1;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  /// <summary>Mode byte value for ANTIC Mode E (4-color, 160x192).</summary>
  public const byte ModeE = 0x0E;

  /// <summary>Mode byte value for ANTIC Mode F (mono, 320x192).</summary>
  public const byte ModeF = 0x0F;

  static string IImageFormatMetadata<AtariAnticModeFile>.PrimaryExtension => ".ame";
  static string[] IImageFormatMetadata<AtariAnticModeFile>.FileExtensions => [".ame", ".anm"];
  static AtariAnticModeFile IImageFormatReader<AtariAnticModeFile>.FromSpan(ReadOnlySpan<byte> data) => AtariAnticModeReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AtariAnticModeFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<AtariAnticModeFile>.ToBytes(AtariAnticModeFile file) => AtariAnticModeWriter.ToBytes(file);

  /// <summary>Width in pixels (320 for Mode F, 160 for Mode E).</summary>
  public int Width => Mode == ModeE ? 160 : 320;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>Raw screen data (7680 bytes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>ANTIC mode byte: 0x0E = Mode E (4-color), 0x0F = Mode F (mono).</summary>
  public byte Mode { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Default 4-color ANTIC Mode E palette as RGB triplets.</summary>
  private static readonly byte[] _ModeEPalette = [
    0x00, 0x00, 0x00, // 0: black
    0x88, 0x44, 0x00, // 1: brown
    0x00, 0xAA, 0x44, // 2: green
    0xDD, 0xCC, 0x88, // 3: tan
  ];

  /// <summary>
  /// Converts this ANTIC mode screen to a raw image.
  /// Mode F: Indexed1 320x192 with B&amp;W palette.
  /// Mode E: Indexed8 160x192 with 4-color palette.
  /// </summary>
  public static RawImage ToRawImage(AtariAnticModeFile file) {

    if (file.Mode == ModeE)
      return _DecodeModeE(file);

    return _DecodeModeF(file);
  }

  private static RawImage _DecodeModeF(AtariAnticModeFile file) {
    var pixelData = new byte[ScreenDataSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, ScreenDataSize)).CopyTo(pixelData);

    return new() {
      Width = 320,
      Height = 192,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  private static RawImage _DecodeModeE(AtariAnticModeFile file) {
    const int width = 160;
    const int height = 192;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < BytesPerRow; ++byteCol) {
        var srcOffset = y * BytesPerRow + byteCol;
        var b = srcOffset < file.PixelData.Length ? file.PixelData[srcOffset] : (byte)0;

        for (var p = 0; p < 4; ++p) {
          var shift = (3 - p) * 2;
          var index = (b >> shift) & 0x03;
          var x = byteCol * 4 + p;
          if (x < width)
            pixels[y * width + x] = (byte)index;
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _ModeEPalette[..],
      PaletteCount = 4,
    };
  }
}
