using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Sprite64;

/// <summary>In-memory representation of a C64 sprite (24x21, mono or multicolor, 64 bytes).</summary>
public sealed class Sprite64File : IImageFileFormat<Sprite64File> {

  /// <summary>Size of the sprite pixel data in bytes (3 bytes/row x 21 rows = 63).</summary>
  internal const int SpriteDataSize = 63;

  /// <summary>Total file size per sprite (63 data + 1 mode byte).</summary>
  public const int ExpectedFileSize = 64;

  /// <summary>Sprite width in pixels.</summary>
  internal const int PixelWidth = 24;

  /// <summary>Sprite height in pixels.</summary>
  internal const int PixelHeight = 21;

  /// <summary>Bytes per sprite row.</summary>
  internal const int BytesPerRow = 3;

  /// <summary>Bit 7 of the mode byte indicates multicolor mode.</summary>
  internal const byte MulticolorFlag = 0x80;

  /// <summary>Black and white palette for indexed output (2 entries, 3 bytes each).</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFileFormat<Sprite64File>.PrimaryExtension => ".s64";
  static string[] IImageFileFormat<Sprite64File>.FileExtensions => [".s64", ".spr64"];
  static FormatCapability IImageFileFormat<Sprite64File>.Capabilities => FormatCapability.IndexedOnly;
  static Sprite64File IImageFileFormat<Sprite64File>.FromFile(FileInfo file) => Sprite64Reader.FromFile(file);
  static Sprite64File IImageFileFormat<Sprite64File>.FromBytes(byte[] data) => Sprite64Reader.FromBytes(data);
  static Sprite64File IImageFileFormat<Sprite64File>.FromStream(Stream stream) => Sprite64Reader.FromStream(stream);
  static RawImage IImageFileFormat<Sprite64File>.ToRawImage(Sprite64File file) => ToRawImage(file);
  static Sprite64File IImageFileFormat<Sprite64File>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<Sprite64File>.ToBytes(Sprite64File file) => Sprite64Writer.ToBytes(file);

  /// <summary>Always 24.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 21.</summary>
  public int Height => PixelHeight;

  /// <summary>Sprite pixel data (63 bytes: 3 bytes per row x 21 rows).</summary>
  public byte[] SpriteData { get; init; } = [];

  /// <summary>Mode byte. Bit 7 set = multicolor sprite.</summary>
  public byte ModeByte { get; init; }

  /// <summary>Whether this sprite uses multicolor mode (bit 7 of ModeByte).</summary>
  public bool IsMulticolor => (ModeByte & MulticolorFlag) != 0;

  /// <summary>
  /// Converts this C64 sprite to a platform-independent <see cref="RawImage"/> in Indexed8 format.
  /// Mono mode: 1bpp, each bit selects index 0 (transparent/black) or 1 (sprite color/white).
  /// Multicolor mode: 2bpp, each pixel pair selects index 0 (transparent) or 1 (sprite color).
  /// Output is always 24x21 Indexed8 with a 2-entry B&amp;W palette.
  /// </summary>
  public static RawImage ToRawImage(Sprite64File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = new byte[PixelWidth * PixelHeight];

    if (file.IsMulticolor)
      _DecodeMulticolor(file.SpriteData, pixels);
    else
      _DecodeMono(file.SpriteData, pixels);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Not supported. C64 sprites cannot be created from raw images.</summary>
  public static Sprite64File FromRawImage(RawImage image) => throw new NotSupportedException("Creating C64 sprites from raw images is not supported.");

  /// <summary>Decodes a mono (hi-res) sprite: 1bpp, 24 pixels per row, 3 bytes per row.</summary>
  private static void _DecodeMono(byte[] spriteData, byte[] pixels) {
    for (var row = 0; row < PixelHeight; ++row)
      for (var col = 0; col < PixelWidth; ++col) {
        var byteIndex = row * BytesPerRow + col / 8;
        var bitPosition = 7 - (col % 8);
        var bitValue = byteIndex < spriteData.Length
          ? (spriteData[byteIndex] >> bitPosition) & 1
          : 0;

        pixels[row * PixelWidth + col] = (byte)bitValue;
      }
  }

  /// <summary>
  /// Decodes a multicolor sprite: 2bpp, 12 effective pixels per row doubled to 24 output pixels.
  /// Each 2-bit value: 00=index 0 (transparent), 01/10/11=index 1 (sprite color).
  /// Each multicolor pixel occupies 2 horizontal output pixels.
  /// </summary>
  private static void _DecodeMulticolor(byte[] spriteData, byte[] pixels) {
    for (var row = 0; row < PixelHeight; ++row)
      for (var mcPixel = 0; mcPixel < 12; ++mcPixel) {
        var bitOffset = mcPixel * 2;
        var byteIndex = row * BytesPerRow + bitOffset / 8;
        var bitShift = 6 - (bitOffset % 8);
        var value = byteIndex < spriteData.Length
          ? (spriteData[byteIndex] >> bitShift) & 0x03
          : 0;

        var colorIndex = (byte)(value != 0 ? 1 : 0);
        var outX = mcPixel * 2;
        pixels[row * PixelWidth + outX] = colorIndex;
        pixels[row * PixelWidth + outX + 1] = colorIndex;
      }
  }
}
