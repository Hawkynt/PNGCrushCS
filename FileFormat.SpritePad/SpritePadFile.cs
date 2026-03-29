using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SpritePad;

/// <summary>In-memory representation of a SpritePad (.spd) sprite collection file for the C64.</summary>
public sealed class SpritePadFile : IImageFileFormat<SpritePadFile> {

  static string IImageFileFormat<SpritePadFile>.PrimaryExtension => ".spd";
  static string[] IImageFileFormat<SpritePadFile>.FileExtensions => [".spd"];
  static FormatCapability IImageFileFormat<SpritePadFile>.Capabilities => FormatCapability.IndexedOnly;
  static SpritePadFile IImageFileFormat<SpritePadFile>.FromFile(FileInfo file) => SpritePadReader.FromFile(file);
  static SpritePadFile IImageFileFormat<SpritePadFile>.FromBytes(byte[] data) => SpritePadReader.FromBytes(data);
  static SpritePadFile IImageFileFormat<SpritePadFile>.FromStream(Stream stream) => SpritePadReader.FromStream(stream);
  static RawImage IImageFileFormat<SpritePadFile>.ToRawImage(SpritePadFile file) => ToRawImage(file);
  static SpritePadFile IImageFileFormat<SpritePadFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<SpritePadFile>.ToBytes(SpritePadFile file) => SpritePadWriter.ToBytes(file);

  /// <summary>Sprite width in pixels.</summary>
  internal const int SpritePixelWidth = 24;

  /// <summary>Sprite height in pixels.</summary>
  internal const int SpritePixelHeight = 21;

  /// <summary>Bytes per sprite row.</summary>
  internal const int BytesPerRow = 3;

  /// <summary>Sprite pixel data size (3 bytes/row x 21 rows = 63 bytes).</summary>
  internal const int SpriteDataSize = 63;

  /// <summary>Total bytes per sprite entry (63 data + 1 color/mode byte).</summary>
  internal const int BytesPerSprite = 64;

  /// <summary>Header size for SpritePad v1 files.</summary>
  internal const int V1HeaderSize = 3;

  /// <summary>Header size for SpritePad v2 files.</summary>
  internal const int V2HeaderSize = 6;

  /// <summary>Maximum number of sprites in a grid row before wrapping.</summary>
  private const int _MaxSpritesPerRow = 8;

  /// <summary>Bit 7 of the mode/color byte indicates multicolor mode.</summary>
  internal const byte MulticolorFlag = 0x80;

  /// <summary>Black and white palette for indexed output (2 entries, 3 bytes each).</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Version byte (1 or 2).</summary>
  public byte Version { get; init; } = 1;

  /// <summary>Number of sprites in the collection.</summary>
  public byte SpriteCount { get; init; }

  /// <summary>Whether the collection is in multicolor mode (v1 header flag).</summary>
  public bool IsMulticolor { get; init; }

  /// <summary>Extra header bytes for v2 (bytes 3..5); empty for v1.</summary>
  public byte[] ExtraHeader { get; init; } = [];

  /// <summary>Raw sprite data including color/mode bytes (SpriteCount x 64 bytes).</summary>
  public byte[] RawData { get; init; } = [];

  /// <summary>Composite image width based on sprite grid layout.</summary>
  public int Width {
    get {
      var cols = Math.Min((int)SpriteCount, _MaxSpritesPerRow);
      return cols < 1 ? SpritePixelWidth : cols * SpritePixelWidth;
    }
  }

  /// <summary>Composite image height based on sprite grid layout.</summary>
  public int Height {
    get {
      if (SpriteCount < 1)
        return SpritePixelHeight;
      var cols = Math.Min((int)SpriteCount, _MaxSpritesPerRow);
      var rows = ((int)SpriteCount + cols - 1) / cols;
      return rows * SpritePixelHeight;
    }
  }

  /// <summary>
  /// Converts this SpritePad collection to a platform-independent <see cref="RawImage"/> in Indexed8 format.
  /// Sprites are arranged in an 8-column grid.
  /// </summary>
  public static RawImage ToRawImage(SpritePadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var spriteCount = Math.Max((int)file.SpriteCount, 1);
    var cols = Math.Min(spriteCount, _MaxSpritesPerRow);
    var rows = (spriteCount + cols - 1) / cols;
    var width = cols * SpritePixelWidth;
    var height = rows * SpritePixelHeight;
    var pixels = new byte[width * height];

    for (var s = 0; s < file.SpriteCount; ++s) {
      var spriteOffset = s * BytesPerSprite;
      if (spriteOffset + SpriteDataSize > file.RawData.Length)
        break;

      var modeByte = spriteOffset + SpriteDataSize < file.RawData.Length
        ? file.RawData[spriteOffset + SpriteDataSize]
        : (byte)0;

      var isMulticolor = (modeByte & MulticolorFlag) != 0 || file.IsMulticolor;

      var gridCol = s % cols;
      var gridRow = s / cols;
      var baseX = gridCol * SpritePixelWidth;
      var baseY = gridRow * SpritePixelHeight;

      if (isMulticolor)
        _DecodeMulticolor(file.RawData, spriteOffset, pixels, width, baseX, baseY);
      else
        _DecodeMono(file.RawData, spriteOffset, pixels, width, baseX, baseY);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Not supported. SpritePad files cannot be created from raw images.</summary>
  public static SpritePadFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to SpritePadFile is not supported due to C64 sprite encoding constraints.");
  }

  /// <summary>Decodes a mono (hi-res) sprite at the given offset into the pixel buffer.</summary>
  private static void _DecodeMono(byte[] spriteData, int offset, byte[] pixels, int stride, int baseX, int baseY) {
    for (var row = 0; row < SpritePixelHeight; ++row)
      for (var col = 0; col < SpritePixelWidth; ++col) {
        var byteIndex = offset + row * BytesPerRow + col / 8;
        var bitPosition = 7 - (col % 8);
        var bitValue = byteIndex < spriteData.Length
          ? (spriteData[byteIndex] >> bitPosition) & 1
          : 0;

        pixels[(baseY + row) * stride + baseX + col] = (byte)bitValue;
      }
  }

  /// <summary>Decodes a multicolor sprite at the given offset: 2bpp, each pixel pair doubled horizontally.</summary>
  private static void _DecodeMulticolor(byte[] spriteData, int offset, byte[] pixels, int stride, int baseX, int baseY) {
    for (var row = 0; row < SpritePixelHeight; ++row)
      for (var mcPixel = 0; mcPixel < 12; ++mcPixel) {
        var bitOffset = mcPixel * 2;
        var byteIndex = offset + row * BytesPerRow + bitOffset / 8;
        var bitShift = 6 - (bitOffset % 8);
        var value = byteIndex < spriteData.Length
          ? (spriteData[byteIndex] >> bitShift) & 0x03
          : 0;

        var colorIndex = (byte)(value != 0 ? 1 : 0);
        var outX = mcPixel * 2;
        pixels[(baseY + row) * stride + baseX + outX] = colorIndex;
        pixels[(baseY + row) * stride + baseX + outX + 1] = colorIndex;
      }
  }
}
